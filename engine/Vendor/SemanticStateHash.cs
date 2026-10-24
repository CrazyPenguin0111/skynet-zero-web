using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace SkynetZero;

// Durable book/provenance keys must keep the exact UTF8 representation emitted
// by QueryStateProvenance.Semantic. Stream it into SHA256 instead of rebuilding
// the entire multiverse's text and UTF8 arrays on every lookup.
internal static class SemanticStateHash
{
    private static readonly ConditionalWeakTable<Board, BoardCache> Boards = new();
    private sealed class BoardCache { internal BoardText? Value; }
    private sealed class BoardText
    {
        private readonly int width, height;
        private readonly int[] pieces;
        private readonly (int X, int Y, int T, int L, bool Color)? enPassant;
        private readonly string negativeSign;
        internal readonly byte[] Bytes;

        internal BoardText(Board board, NumberFormatInfo format)
        {
            width = board.Width; height = board.Height;
            pieces = (int[])board.Brd.Clone(); enPassant = EnPassant(board);
            negativeSign = format.NegativeSign;
            // This small allocation happens once per shared immutable board,
            // not once per board in every search/book lookup. A content check
            // below also catches public-field and direct-array mutations.
            var text = new StringBuilder();
            text.Append(width.ToString(format)).Append('x').Append(height.ToString(format)).Append(':');
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(pieces[i].ToString(format));
            }
            text.Append(':');
            if (enPassant is { } square)
                text.Append(square.X.ToString(format)).Append(',').Append(square.Y.ToString(format)).Append(',')
                    .Append(square.T.ToString(format)).Append(',').Append(square.L.ToString(format)).Append(',')
                    .Append(square.Color);
            else text.Append("null");
            text.Append(';'); Bytes = Encoding.UTF8.GetBytes(text.ToString());
        }

        // General integer formatting uses ASCII digits plus the culture's
        // negative sign. Remember its value, since NumberFormatInfo is mutable.
        internal bool Matches(Board board, NumberFormatInfo format) => width == board.Width && height == board.Height &&
            negativeSign == format.NegativeSign && enPassant == EnPassant(board) && pieces.AsSpan().SequenceEqual(board.Brd);
        private static (int, int, int, int, bool)? EnPassant(Board board) =>
            board.EnPassentSquare is { } square ? (square.X, square.Y, square.T, square.L, square.Color) : null;
    }

    internal static string Compute(GameState state)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[4096];
        var format = CultureInfo.CurrentCulture.NumberFormat;
        var writer = new Writer(hash, buffer, format);
        writer.Add(state.Width, '|'); writer.Add(state.Height, '|');
        writer.Add(state.EvenStart, '|'); writer.Add(state.Color, '|');
        writer.Add(state.StartPresent, '|'); writer.Add(state.Present, '|');
        writer.Add(state.MinTL, '|'); writer.Add(state.MaxTL, '|');
        writer.Add(state.MinActiveTL, '|'); writer.Add(state.MaxActiveTL, '|'); writer.Add(state.TLHandicap, '|');
        writer.Add(state.kingWasTaken, '|'); writer.Add(state.kingWasTakenByColor, '|');
        foreach (var line in state.Multiverse)
        {
            writer.Add(line.Layer, ':'); writer.Add(line.TStart, ':'); writer.Add(line.TEnd, ':');
            writer.Add(line.WhiteStart, ':'); writer.Add(line.WhiteEnd, ':');
            writer.Add(line.BlackStart, ':'); writer.Add(line.BlackEnd, ':');
            writer.Add(line.ColorPlayable, ':'); writer.Add(line.ColorStart, ';');
            AddBoards(ref writer, line.WBoards, true, format);
            AddBoards(ref writer, line.BBoards, false, format);
        }
        writer.Add("TM:"u8);
        foreach (var move in state.TurnMoves)
        {
            writer.Add(move.Origin); writer.Add(">"u8); writer.Add(move.Dest); writer.Add(":"u8);
            writer.Add(move.PieceMoved, ':'); writer.Add(move.Type, ':'); writer.Add(move.SpecialType, ';');
        }
        writer.Add("TLS:"u8);
        for (int i = 0; i < state.TurnTLS.Count; i++)
        { if (i > 0) writer.Add(","u8); writer.Add(state.TurnTLS[i]); }
        writer.Add(";"u8); writer.Flush();
        Span<byte> digest = stackalloc byte[32]; hash.GetHashAndReset(digest);
        return Convert.ToHexString(digest);
    }

    private static void AddBoards(ref Writer writer, List<Board> boards, bool white, NumberFormatInfo format)
    {
        foreach (var board in boards)
        {
            var cache = Boards.GetValue(board, static _ => new BoardCache());
            var value = Volatile.Read(ref cache.Value);
            if (value == null || !value.Matches(board, format))
            {
                value = new BoardText(board, format);
                Volatile.Write(ref cache.Value, value);
            }
            writer.Add(white ? "W:"u8 : "B:"u8); writer.Add(value.Bytes);
        }
    }

    private ref struct Writer(IncrementalHash hash, Span<byte> buffer, NumberFormatInfo format)
    {
        private readonly IncrementalHash hash = hash;
        private readonly Span<byte> buffer = buffer;
        private readonly NumberFormatInfo format = format;
        private int length;
        internal void Add(int value)
        {
            if (!value.TryFormat(buffer[length..], out int written, provider: format))
            {
                Flush();
                if (!value.TryFormat(buffer, out written, provider: format))
                {
                    // Custom cultures may define an arbitrarily long Unicode
                    // negative sign; retain the original encoding in that case.
                    Add(Encoding.UTF8.GetBytes(value.ToString(format))); return;
                }
            }
            length += written;
        }
        internal void Add(int value, char separator) { Add(value); Separator(separator); }
        internal void Add(bool value, char separator) { Add(value ? "True"u8 : "False"u8); Separator(separator); }
        internal void Add(CoordFive value)
        {
            Add(value.X, ','); Add(value.Y, ','); Add(value.T, ','); Add(value.L, ',');
            Add(value.Color ? "True"u8 : "False"u8);
        }
        private void Separator(char value)
        { if (length == buffer.Length) Flush(); buffer[length++] = (byte)value; }
        internal void Add(ReadOnlySpan<byte> bytes)
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
