namespace SkynetZero;

// A spatial swap-off estimate in pawn units. Temporal exchanges are handled by
// TacticalSearch, which uses real multiverse moves instead of inventing boards.
internal static class StaticExchange
{
    internal static float Gain(Board position, int x, int y, bool attacker)
    {
        int victim = position.GetSquare(x, y);
        if (victim == Board.EMPTYSQUARE || Board.GetColorBool(victim) == attacker) return 0;
        return Capture(new Board(position), new CoordFive(x, y), attacker, 16);
    }

    private static float Capture(Board board, CoordFive target, bool attacker, int remaining)
    {
        if (remaining == 0) return 0;
        int victim = board.GetSquare(target);
        float value = Board.GetPieceValue(victim);
        int selected = -1, selectedPiece = 0;
        float cheapest = float.PositiveInfinity, promotion = 0;
        for (int square = 0; square < board.Brd.Length; square++)
        {
            int piece = board.Brd[square];
            if (piece == Board.EMPTYSQUARE || Board.GetColorBool(piece) != attacker) continue;
            float cost = Board.GetPieceValue(piece);
            if (selected >= 0 && cost >= cheapest) continue;
            int x = square % board.Width, y = square / board.Width;
            if (x == target.X && y == target.Y ||
                !MoveGenerator.AttacksSpatial(board, piece, x, y, target)) continue;
            int placed = Math.Abs(piece);
            int type = (placed - 1) % Board.NUMTYPES + 1;
            if ((type == (int)Board.Piece.WPAWN || type == (int)Board.Piece.WBRAWN) &&
                target.Y == (attacker ? board.Height - 1 : 0))
                placed = (int)(attacker ? Board.Piece.WQUEEN : Board.Piece.BQUEEN);
            board.Brd[square] = Board.EMPTYSQUARE;
            board.SetSquare(target, placed);
            // Capturing a royal ends the game. Otherwise pinned pieces and royal
            // recaptures onto attacked squares must not count as defenders.
            bool safe = Board.IsRoyalPiece(victim) || !RoyalAttacked(board, attacker);
            board.Brd[square] = piece;
            board.SetSquare(target, victim);
            if (!safe) continue;
            selected = square; selectedPiece = placed; cheapest = cost;
            promotion = Board.GetPieceValue(placed) - cost;
            if (!float.IsFinite(promotion)) promotion = 0;
        }
        if (selected < 0) return 0;
        if (float.IsPositiveInfinity(value)) return value;
        int original = board.Brd[selected];
        board.Brd[selected] = Board.EMPTYSQUARE;
        board.SetSquare(target, selectedPiece);
        float reply = Capture(board, target, !attacker, remaining - 1);
        board.Brd[selected] = original;
        board.SetSquare(target, victim);
        // Either side may decline an unfavorable exchange.
        return Math.Max(0, value + promotion - reply);
    }

    private static bool RoyalAttacked(Board board, bool defender)
    {
        for (int square = 0; square < board.Brd.Length; square++)
            if (Board.IsRoyalPiece(board.Brd[square]) && Board.GetColorBool(board.Brd[square]) == defender &&
                MoveGenerator.IsSquareAttacked(board, new CoordFive(square % board.Width, square / board.Width), !defender))
                return true;
        return false;
    }
}
