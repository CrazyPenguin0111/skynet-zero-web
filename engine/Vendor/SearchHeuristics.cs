// Adapted from FirePlank/5d-chess-engine ab.rs (Kairos), commit
// b81e7064d0c568f3c7972bb75744e3ecd9d87535, GPL-3.0.
// Native coordinates, colors and branch-result board lookup are SkynetZero adapters.
using static SkynetZero.MultiverseEvaluation;

namespace SkynetZero;

internal static class SearchHeuristics
{
    internal const int HistorySize = 65536;
    internal const int CheckBonus = 30000;

    internal static int PieceKind(int nativePiece)
    {
        int piece = Math.Abs(nativePiece);
        if (piece == 0 || piece == Math.Abs(Board.ERRORSQUARE)) return 0;
        return Board.PieceChars[(piece - 1) % Board.NUMTYPES + 1];
    }

    internal static int PieceValue(int nativePiece) => ValueOfKind(PieceKind(nativePiece));

    internal static int ValueOfKind(int kind) => kind switch
    {
        'P' => 100, 'W' => 150, 'N' => 400, 'B' => 450, 'R' => 330,
        'Q' => 1250, 'S' => 900, 'U' => 250, 'D' => 150, 'C' => 300,
        'K' or 'Y' => 2000, _ => 0
    };

    internal static int HistoryIndex(int nativeMover, Move move)
    {
        int displacement = Math.Clamp(move.Dest.T - move.Origin.T + 2, 0, 3) * 4 +
            Math.Sign(move.Dest.L - move.Origin.L) + 1;
        return (((PieceKind(nativeMover) * 64 + move.Dest.X + 8 * move.Dest.Y) * 16) + displacement) & 0xffff;
    }

    internal static (int Mover, int Victim) MoverVictim(GameState source, Move move)
    {
        int mover = source.GetSquare(move.Origin);
        if (move.SpecialType == Move.NULLMOVE) return (0, 0);
        int victim = source.GetSquare(move.Dest), kind = PieceKind(mover);
        bool spatial = move.Origin.L == move.Dest.L && move.Origin.T == move.Dest.T;
        if (spatial && kind == 'K' && Math.Abs(move.Origin.X - move.Dest.X) > 1) victim = 0;
        else if (spatial && victim == 0 && kind is 'P' or 'W' && move.Origin.X != move.Dest.X)
            victim = source.GetBoard(move.Origin)!.GetSquare(move.Dest.X, move.Origin.Y);
        return (mover, victim);
    }

    internal static Board ResultBoard(GameState source, Move move, GameState successor)
    {
        int layer = successor.MaxTL > source.MaxTL ? successor.MaxTL :
            successor.MinTL < source.MinTL ? successor.MinTL : move.Dest.L;
        return successor.GetTimeline(layer)?.GetPlayableBoard() ??
            throw new InvalidOperationException("A scored move must have a result board.");
    }

    // This score orders atomic choices. It is not an evaluation or a mate certificate.
    internal static int CoordinateScore(GameState source, Move move, GameState successor,
        int history = 0, bool givesCheck = false)
    {
        if (move.SpecialType == Move.NULLMOVE) return -5;
        var (mover, victim) = MoverVictim(source, move);
        return CoordinateScore(mover, victim, move, ResultBoard(source, move, successor),
            source.Color, history, givesCheck);
    }

    internal static int CoordinateScore(int nativeMover, int nativeVictim, Move move,
        Board result, bool playerWhite, int history = 0, bool givesCheck = false)
    {
        if (move.SpecialType == Move.NULLMOVE) return -5;
        int mover = PieceKind(nativeMover), victim = PieceKind(nativeVictim), value = ValueOfKind(mover);
        int score = -10 * ExchangeLoss(result, move.Dest.X, move.Dest.Y, playerWhite, value);
        score += victim != 0 ? 10000 + 10 * ValueOfKind(victim) - value / 10 : Math.Clamp(history, -5000, 5000);
        if (mover is 'P' or 'W' && move.Dest.Y == (playerWhite ? result.Height - 1 : 0)) score += 9000;
        if ((move.Origin.L != move.Dest.L || move.Origin.T != move.Dest.T) && victim == 0) score -= 300;
        if (mover is 'K' or 'C') score -= 200;
        if (givesCheck) score += CheckBonus;
        return score;
    }

    internal static int ExchangeLoss(Board board, int x, int y, bool playerWhite, int moverValue) =>
        ExchangeLoss(new BitBoard(board), x + 8 * y, playerWhite ? 0 : 1, moverValue);

    // Complete-turn scores deliberately differ from per-coordinate scores:
    // exchange loss/check/promotion/king bonuses steer the product cursor,
    // while action batches and late reductions use this source action_score.
    internal static int ActionScore(GameState state, IEnumerable<Move?> actions,
        IReadOnlyList<int>? history = null, int killerBonus = 0)
    {
        int score = killerBonus;
        foreach (Move? move in actions)
        {
            if (move == null || move.SpecialType == Move.NULLMOVE) continue;
            var (mover, victim) = MoverVictim(state, move);
            if (victim != 0) score += 10_000 + 10 * PieceValue(victim) - PieceValue(mover) / 10;
            else if (history != null) score += Math.Clamp(history[HistoryIndex(mover, move)], -5000, 5000) / 4;
            if ((move.Origin.L != move.Dest.L || move.Origin.T != move.Dest.T) && victim == 0) score -= 300;
        }
        return score;
    }

    internal static int ExchangeLoss(BitBoard board, int target, int color, int moverValue)
    {
        ulong attacks = AttacksTo(board, target) & ~Mask(target);
        ulong enemy = attacks & board.Mine(1 - color);
        if (enemy == 0) return 0;
        if ((attacks & board.Mine(color)) == 0) return moverValue;
        int least = int.MaxValue;
        foreach (int square in Squares(enemy)) least = Math.Min(least, ValueOfKind(Kind(board, square)));
        return Math.Max(0, moverValue - least);
    }

    internal static ulong PawnAttack(int square, int color) =>
        Shift(Mask(square), -1, color == 0 ? 1 : -1) | Shift(Mask(square), 1, color == 0 ? 1 : -1);

    internal static ulong AttacksTo(BitBoard board, int square) =>
        (PawnAttack(square, 0) & board.LPawn & board.Black) |
        (PawnAttack(square, 1) & board.LPawn & board.White) |
        (KingAttack(square) & board.LKing) | (KnightAttack(square) & board.LKnight) |
        (SliderAttack(square, board.Occupied, true, false) & board.LRook) |
        (SliderAttack(square, board.Occupied, false, true) & board.LBishop);

    internal static bool HasPhysicalCheck(BitBoard board, int defender)
    {
        foreach (int royal in Squares(board.Royal & board.Mine(defender)))
            if ((AttacksTo(board, royal) & board.Mine(1 - defender)) != 0) return true;
        return false;
    }

    internal static int Kind(BitBoard board, int square)
    {
        ulong z = Mask(square);
        if ((board.Occupied & z) == 0) return 0;
        if ((board.LPawn & z) != 0) return (board.LRawn & z) != 0 ? 'W' : 'P';
        if ((board.LKnight & z) != 0) return 'N';
        if ((board.LKing & z) != 0) return (board.Royal & z) != 0 ? 'K' : 'C';
        bool rook = (board.LRook & z) != 0, bishop = (board.LBishop & z) != 0, dragon = (board.LDragon & z) != 0;
        if (rook && dragon) return (board.Royal & z) != 0 ? 'Y' : 'Q';
        if (rook && bishop) return 'S';
        if (rook) return 'R';
        if (bishop) return 'B';
        if ((board.LUnicorn & z) != 0) return 'U';
        return dragon ? 'D' : 1;
    }
}
