// Adapted from FirePlank/5d-chess-engine local.rs (Kairos), commit
// b81e7064d0c568f3c7972bb75744e3ecd9d87535, GPL-3.0.
using static SkynetZero.MultiverseEvaluation;
using static SkynetZero.SearchHeuristics;

namespace SkynetZero;

// Cheap board-local material settling, not a complete-turn search or mate detector.
internal static class LocalCaptureSearch
{
    internal const int Depth = 6, RoyalValue = 20000, MoveCapacity = 64;
    private static readonly int[] Values = ValuesFrom(MultiverseEvaluation.TunedWeights.AsSpan(0, 10));
    private readonly record struct Capture(int Order, int From, int To, int Gain, bool Promotes);

    internal static int[] ValuesFrom(ReadOnlySpan<float> material)
    {
        if (material.Length < 10) throw new ArgumentException("Ten active material weights are required.", nameof(material));
        int[] values = new int[128];
        ReadOnlySpan<char> kinds = ['P', 'W', 'N', 'B', 'R', 'Q', 'S', 'U', 'D', 'C'];
        for (int i = 0; i < kinds.Length; i++)
            values[kinds[i]] = Math.Max(100, (int)MathF.Round(material[i], MidpointRounding.AwayFromZero));
        values['K'] = values['Y'] = RoyalValue;
        return values;
    }

    internal static int CaptureGain(Board board, bool playerWhite, CancellationToken cancellationToken = default) =>
        CaptureGain(new BitBoard(board), playerWhite ? 0 : 1, Values, cancellationToken);

    internal static int CaptureGain(BitBoard board, int color, int[] values, CancellationToken cancellationToken = default) =>
        Search(board, color, 0, int.MaxValue / 2, Depth, values, cancellationToken);

    private static int Search(BitBoard board, int color, int alpha, int beta, int depth,
        int[] values, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (depth == 0 || beta <= 0) return 0;
        alpha = Math.Max(0, alpha);
        ulong mine = board.Mine(color), theirs = board.Mine(1 - color), occupied = board.Occupied;
        int lastRank = color == 0 ? board.Height - 1 : 0;
        Span<Capture> moves = stackalloc Capture[MoveCapacity];
        int count = 0;
        foreach (int from in Squares(mine))
        {
            ulong z = Mask(from), attack = 0;
            int mover = Kind(board, from);
            bool pawn = (board.LPawn & z) != 0;
            if (pawn) attack |= PawnAttack(from, color);
            if ((board.LKnight & z) != 0) attack |= KnightAttack(from);
            if ((board.LKing & z) != 0) attack |= KingAttack(from);
            if ((board.LRook & z) != 0) attack |= SliderAttack(from, occupied, true, false);
            if ((board.LBishop & z) != 0) attack |= SliderAttack(from, occupied, false, true);
            ulong targets = attack & theirs;
            if (pawn)
            {
                ulong push = Shift(z, 0, color == 0 ? 1 : -1) & ~occupied;
                if (push != 0 && (System.Numerics.BitOperations.Log2(push) >> 3) == lastRank) targets |= push;
            }
            foreach (int to in Squares(targets))
            {
                if (count == moves.Length) break;
                int victim = (board.Royal & Mask(to)) != 0 ? RoyalValue : values[Kind(board, to)];
                if (victim == RoyalValue) return RoyalValue;
                bool promotes = pawn && (to >> 3) == lastRank;
                int gain = victim + (promotes ? values['Q'] - values[mover] : 0);
                moves[count++] = new Capture(gain * 16 - values[mover] / 16, from, to, gain, promotes);
            }
        }
        moves = moves[..count];
        moves.Sort(static (a, b) => b.Order.CompareTo(a.Order));
        int best = 0;
        foreach (var move in moves)
        {
            var next = new BitBoard(board.Width, board.Height, board.Raw);
            ulong from = Mask(move.From), to = Mask(move.To), clear = ~(from | to);
            for (int i = 0; i < next.Raw.Length; i++)
                next.Raw[i] = (board.Raw[i] & clear) | ((board.Raw[i] & from) != 0 && i != 11 ? to : 0);
            if (move.Promotes)
            {
                for (int i = 0; i < next.Raw.Length; i++) next.Raw[i] &= ~to;
                next.Raw[color] |= to;
                next.Raw[7] |= to; next.Raw[8] |= to; next.Raw[9] |= to; next.Raw[10] |= to;
            }
            if (HasPhysicalCheck(next, color)) continue;
            int value = move.Gain - Search(next, 1 - color, move.Gain - beta, move.Gain - alpha,
                depth - 1, values, token);
            if (value <= best) continue;
            best = value;
            if (value <= alpha) continue;
            alpha = value;
            if (value >= beta) break;
        }
        return best;
    }
}
