using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace SkynetZero;

// Search keys cover the full multiverse history and unfinished turn. Stream
// fixed-width values instead of constructing a large textual provenance record
// for every node; durable provenance keeps its existing independent format.
internal static class CompleteTurnStateHash
{
    // Histories share Board instances across search clones. Cache only the
    // expensive digest, checking the actual contents on every use: Board and
    // its en-passant coordinate are mutable, including direct array writes.
    // Weak keys release the snapshots with their boards rather than accumulating
    // a process-wide history of positions over training epochs.
    private static readonly ConditionalWeakTable<Board, BoardCache> BoardHashes = new();
    private sealed class BoardCache { internal BoardFingerprint? Value; }
    private sealed class BoardFingerprint
    {
        private readonly int width, height;
        private readonly int[] pieces;
        private readonly (int X, int Y, int T, int L, bool Color)? enPassant;
        internal readonly byte[] Digest;

        internal BoardFingerprint(Board board)
        {
            width = board.Width; height = board.Height;
            pieces = (int[])board.Brd.Clone(); enPassant = EnPassant(board);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> buffer = stackalloc byte[4096];
            var writer = new Writer(hash, buffer);
            writer.Add(width); writer.Add(height); writer.Add(pieces);
            writer.Add(enPassant.HasValue);
            if (enPassant is { } square)
            {
                writer.Add(square.X); writer.Add(square.Y); writer.Add(square.T);
                writer.Add(square.L); writer.Add(square.Color);
            }
            writer.Flush(); Digest = hash.GetHashAndReset();
        }
        internal bool Matches(Board board) => width == board.Width && height == board.Height &&
            enPassant == EnPassant(board) && pieces.AsSpan().SequenceEqual(board.Brd);
        private static (int, int, int, int, bool)? EnPassant(Board board) =>
            board.EnPassentSquare is { } square ? (square.X, square.Y, square.T, square.L, square.Color) : null;
    }
    internal static string Compute(GameState state)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[4096];
        var writer = new Writer(hash, buffer);
        writer.Add(state.Width); writer.Add(state.Height); writer.Add(state.EvenStart); writer.Add(state.Color);
        // Preserve existing ordinary-game identities.
        if (state.PromotionMask != 0) { writer.Add(0x50524F4D); writer.Add(state.PromotionMask); }
        if (state.QueenPromotionOnly) writer.Add(0x51554545);
        if (state.DrawHistory is { } draws)
        {
            writer.Add(0x44524157);
            foreach (byte value in Convert.FromHexString(draws.Context)) writer.Add(value);
        }
        writer.Add(state.StartPresent); writer.Add(state.Present); writer.Add(state.MinTL); writer.Add(state.MaxTL);
        writer.Add(state.InitialMinTL); writer.Add(state.InitialMaxTL);
        writer.Add(state.MinActiveTL); writer.Add(state.MaxActiveTL); writer.Add(state.TLHandicap);
        writer.Add(state.kingWasTaken); writer.Add(state.kingWasTakenByColor);
        writer.Add(state.Multiverse.Count);
        foreach (var line in state.Multiverse)
        {
            writer.Add(line.Layer); writer.Add(line.TStart); writer.Add(line.TEnd);
            writer.Add(line.WhiteStart); writer.Add(line.WhiteEnd); writer.Add(line.BlackStart); writer.Add(line.BlackEnd);
            writer.Add(line.ColorPlayable); writer.Add(line.ColorStart);
            AddBoards(ref writer, line.WBoards); AddBoards(ref writer, line.BBoards);
        }
        writer.Add(state.TurnMoves.Count);
        foreach (var move in state.TurnMoves)
        {
            writer.Add(move.Origin); writer.Add(move.Dest);
            writer.Add(move.PieceMoved); writer.Add(move.Type); writer.Add(move.SpecialType);
        }
        writer.Add(state.TurnTLS.Count);
        foreach (int layer in state.TurnTLS) writer.Add(layer);
        writer.Flush();
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return Convert.ToHexString(digest);
    }

    private static void AddBoards(ref Writer writer, List<Board> boards)
    {
        writer.Add(boards.Count);
        foreach (var board in boards)
        {
            var cache = BoardHashes.GetValue(board, static _ => new BoardCache());
            var fingerprint = Volatile.Read(ref cache.Value);
            if (fingerprint == null || !fingerprint.Matches(board))
            {
                fingerprint = new BoardFingerprint(board);
                Volatile.Write(ref cache.Value, fingerprint);
            }
            writer.AddBytes(fingerprint.Digest);
        }
    }

    private ref struct Writer(IncrementalHash hash, Span<byte> buffer)
    {
        private readonly IncrementalHash hash = hash;
        private readonly Span<byte> buffer = buffer;
        private int length;
        internal void Add(bool value) => Add(value ? 1 : 0);
        internal void Add(int value)
        {
            if (length + sizeof(int) > buffer.Length) Flush();
            BinaryPrimitives.WriteInt32LittleEndian(buffer[length..], value);
            length += sizeof(int);
        }
        internal void Add(CoordFive value)
        { Add(value.X); Add(value.Y); Add(value.T); Add(value.L); Add(value.Color); }
        internal void Add(int[] values)
        {
            Add(values.Length);
            if (!BitConverter.IsLittleEndian) { foreach (int value in values) Add(value); return; }
            AddBytes(MemoryMarshal.AsBytes(values.AsSpan()));
        }
        internal void AddBytes(ReadOnlySpan<byte> bytes)
        {
            while (!bytes.IsEmpty)
            {
                int count = Math.Min(bytes.Length, buffer.Length - length);
                bytes[..count].CopyTo(buffer[length..]); length += count; bytes = bytes[count..];
                if (length == buffer.Length) Flush();
            }
        }
        internal void Flush()
        { if (length == 0) return; hash.AppendData(buffer[..length]); length = 0; }
    }
}
