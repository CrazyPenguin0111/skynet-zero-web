using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace SkynetZero;

// Immutable history is shared by clones; submitting a hypothetical turn creates
// a new history rather than incrementing the real game's repetition counters.
internal sealed record DrawHistory(ImmutableDictionary<string, int> Repetitions,
    ImmutableDictionary<int, int> LastProgress, int WhiteRepeats, int BlackRepeats,
    int MinimumTimeline, int MaximumTimeline, string Context, string? Reason, DrawHistory? Previous = null);

internal static class DrawRules
{
    internal const int Version = 1, RepetitionsRequired = 7, QuietBoardsRequired = 60, MaximumTurn = 100;
    internal static int Half(Timeline line) => 2 * line.TEnd + (line.ColorPlayable ? 0 : 1);
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static void Enable(GameState state)
    {
        if (state.DrawHistory != null) return;
        var clocks = state.Multiverse.ToImmutableDictionary(line => line.Layer, Half);
        state.DrawHistory = new(ImmutableDictionary<string, int>.Empty, clocks, 0, 0,
            state.MinTL, state.MaxTL, Hash("draw-v1|" + QueryStateProvenance.Semantic(state)), StaticReason(state));
    }

    internal static DrawHistory Advance(GameState state, DrawHistory previous)
    {
        // Origin/destination coordinates still refer to immutable pre-move
        // boards, including promotion and en-passant source boards.
        bool progress = state.MinTL != previous.MinimumTimeline || state.MaxTL != previous.MaximumTimeline ||
            state.TurnMoves.Any(move =>
            {
                if (!state.LayerIsActive(move.Origin.L) && !state.LayerIsActive(move.Dest.L)) return false;
                var pieces = SearchHeuristics.MoverVictim(state, move);
                return SearchHeuristics.PieceKind(pieces.Mover) is 'P' or 'W' || pieces.Victim != 0;
            });
        var clocks = previous.LastProgress;
        foreach (var line in state.Multiverse)
            if (progress || !clocks.ContainsKey(line.Layer)) clocks = clocks.SetItem(line.Layer, Half(line));
        string frontier = Frontier(state);
        string key = (state.Color ? "W|" : "B|") + frontier;
        int repeated = previous.Repetitions.GetValueOrDefault(key) + 1;
        var repetitions = previous.Repetitions.SetItem(key, repeated);
        int white = state.Color ? repeated : previous.WhiteRepeats;
        int black = state.Color ? previous.BlackRepeats : repeated;
        string? reason = StaticReason(state);
        if (reason == null && white >= RepetitionsRequired && black >= RepetitionsRequired)
            reason = "sevenfold-repetition";
        var active = state.Multiverse.Where(line => state.LayerIsActive(line.Layer)).ToArray();
        if (reason == null && active.Length > 0 && active.All(line => Half(line) - clocks[line.Layer] >= QuietBoardsRequired))
            reason = "sixty-quiet-boards";
        return new(repetitions, clocks, white, black, state.MinTL, state.MaxTL,
            Hash(previous.Context + key + "|" + progress), reason, previous);
    }

    internal static string? ClaimableReason(GameState state) =>
        state.kingWasTaken || state.TurnMoves.Count != 0 ? null : state.DrawHistory?.Reason;

    // Only currently remaining pieces count here, including inactive frontiers.
    // Historical boards are not extra surviving pieces. Require both kings;
    // an absent/captured king must never be relabelled as insufficient material.
    private static string? StaticReason(GameState state)
    {
        bool white = false, black = false, onlyKings = true;
        foreach (var line in state.Multiverse)
        foreach (int piece in line.GetPlayableBoard().Brd)
        {
            if (piece == 0) continue;
            int kind = Math.Abs(piece);
            if (kind == (int)Board.Piece.WKING) white = true;
            else if (kind == (int)Board.Piece.BKING) black = true;
            else onlyKings = false;
        }
        if (onlyKings && white && black) return "kings-only";
        if (state.Multiverse.Any(line => state.LayerIsActive(line.Layer) && line.TEnd >= MaximumTurn))
            return "active-timeline-t100";
        return null;
    }

    // Position on every active frontier, with piece rights and en-passant.
    // Absolute time advances during a repetition; relative board times and
    // identities remain significant. Inactive layouts are not part of this rule.
    internal static string Frontier(GameState state)
    {
        var active = state.Multiverse.Where(line => state.LayerIsActive(line.Layer)).OrderBy(line => line.Layer).ToArray();
        int earliest = active.Select(Half).DefaultIfEmpty(0).Min();
        var text = new StringBuilder().Append(state.Width).Append('|').Append(state.Height).Append('|');
        foreach (var line in active)
        {
            var board = line.GetPlayableBoard();
            text.Append(line.Layer).Append(':').Append(Half(line) - earliest).Append(':').Append(line.ColorPlayable).Append(':');
            // Only pawn double steps and king/rook castling retain unmoved
            // rights. A knight returning home is the same position even if
            // its first move changed the engine's generic unmoved marker.
            text.AppendJoin(',', board.Brd.Select(piece => SearchHeuristics.PieceKind(piece) is 'P' or 'W' or 'R' or 'K'
                ? piece : Math.Abs(piece))).Append(';');
            if (board.EnPassentSquare is { } ep)
                text.Append("ep:").Append(ep.X).Append(',').Append(ep.Y).Append(',').Append(ep.L).Append(',')
                    .Append(2 * ep.T + (ep.Color ? 0 : 1) - Half(line)).Append(';');
        }
        return Hash(text.ToString());
    }
}
