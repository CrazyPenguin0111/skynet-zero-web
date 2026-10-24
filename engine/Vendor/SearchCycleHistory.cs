using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SkynetZero;

// A search preference for leaving a recurring frontier, never a repetition
// rule or a legal-position key. In 5D chess an older board remains playable, so
// matching frontiers do not prove that two full game states are equivalent.
internal sealed class SearchCycleHistory
{
    internal const int HistoryHalfTurns = 128, PenaltyPerRepeat = 32, MaximumPenalty = 4096;
    private readonly Dictionary<string, int> previous = new(StringComparer.Ordinal);
    internal int RootRepetitions { get; private set; }
    internal bool Active => RootRepetitions >= 2;

    internal static SearchCycleHistory FromRoot(GameState root, CancellationToken cancellation = default)
    {
        var history = new SearchCycleHistory();
        // A partially played multiboard turn is not a completed frontier.
        if (root.TurnMoves.Count != 0 || root.kingWasTaken) return history;
        string? current = Frontier(root, 0);
        if (current == null) return history;
        for (int offset = 1; offset <= HistoryHalfTurns; offset++)
        {
            cancellation.ThrowIfCancellationRequested();
            string? key = Frontier(root, offset);
            if (key == null) break;
            history.previous[key] = history.previous.GetValueOrDefault(key) + 1;
        }
        history.RootRepetitions = history.previous.GetValueOrDefault(current);
        return history;
    }

    internal int Penalty(GameState successor)
    {
        if (!Active || successor.kingWasTaken || successor.TurnMoves.Count != 0) return 0;
        string? key = Frontier(successor, 0);
        int repeats = key == null ? 0 : previous.GetValueOrDefault(key);
        // A mild nudge is enough for a new cycle. Persistently shuffling games
        // need increasing pressure to try another legal continuation instead of
        // saturating after a few repeats and accumulating thousands of boards.
        int persistence = Math.Max(1, RootRepetitions / 4);
        return Math.Min(MaximumPenalty, PenaltyPerRepeat * Math.Max(0, repeats - 1) * persistence);
    }

    // Shift active frontiers together, preserving their relative times/layers.
    // Inactive branches can remain frozen for thousands of turns; retain their
    // current layout instead of demanding nonexistent earlier boards from them.
    // The bounded historical layouts are evidence for a preference only: a
    // branching game need not actually have visited every aligned layout.
    internal static string? Frontier(GameState state, int offset)
    {
        if (state.Multiverse.Count == 0 || offset < 0) return null;
        int anchor = state.Multiverse.Where(line => state.LayerIsActive(line.Layer))
            .Select(line => 2 * line.TEnd + (line.ColorPlayable ? 0 : 1)).DefaultIfEmpty(0).Min() - offset;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[4096];
        var writer = new Writer(hash, bytes);
        writer.Add(state.Width); writer.Add(state.Height); writer.Add(state.EvenStart);
        writer.Add(offset % 2 == 0 ? state.Color : !state.Color);
        writer.Add(state.TLHandicap); writer.Add(state.MinActiveTL - state.MinTL);
        writer.Add(state.MaxActiveTL - state.MinTL); writer.Add(state.Multiverse.Count);
        foreach (var line in state.Multiverse)
        {
            bool active = state.LayerIsActive(line.Layer);
            int half = 2 * line.TEnd + (line.ColorPlayable ? 0 : 1) - (active ? offset : 0);
            bool white = (half & 1) == 0; int turn = half >> 1;
            var boards = white ? line.WBoards : line.BBoards;
            int index = turn - (white ? line.WhiteStart : line.BlackStart);
            if (index < 0 || index >= boards.Count) return null;
            var board = boards[index];
            writer.Add(line.Layer - state.MinTL); writer.Add(active); writer.Add(active ? half - anchor : 0); writer.Add(white);
            writer.Add(board.Width); writer.Add(board.Height); writer.Add(board.Brd.Length);
            // Signed pieces retain unmoved pawn/king/rook rights.
            foreach (int piece in board.Brd) writer.Add(piece);
            writer.Add(board.EnPassentSquare != null);
            if (board.EnPassentSquare is { } ep)
            {
                writer.Add(ep.X); writer.Add(ep.Y); writer.Add(ep.L - state.MinTL);
                writer.Add(2 * ep.T + (ep.Color ? 0 : 1) - half); writer.Add(ep.Color);
            }
        }
        writer.Flush(); Span<byte> digest = stackalloc byte[32]; hash.GetHashAndReset(digest);
        return Convert.ToHexString(digest);
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
            BinaryPrimitives.WriteInt32LittleEndian(buffer[length..], value); length += sizeof(int);
        }
        internal void Flush()
        { if (length > 0) { hash.AppendData(buffer[..length]); length = 0; } }
    }
}
