namespace SkynetZero;

// A cheap positive proof before constructing the exhaustive hypercuboid model.
// Failure means only that this greedy spatial attempt did not find a witness;
// the caller must still use the complete solver to establish mate or stalemate.
internal static class FastTurnWitness
{
    [ThreadStatic] internal static bool? OptimizationOverride;
    private const int CandidatesPerBoard = 64;

    internal static bool TryFind(GameState original, HcBudget budget, out IReadOnlyList<Move> witness)
    {
        witness = Array.Empty<Move>();
        budget.Spend();
        if (original.kingWasTaken) return false;
        GameState working = original.Clone();
        var moves = new List<Move>();
        // Spatial moves advance an existing frontier exactly once, without
        // creating branches or changing which timelines are active. Thus there
        // can be at most one successful iteration per original timeline.
        for (int advanced = 0; advanced <= original.Multiverse.Count; advanced++)
        {
            budget.Spend();
            Timeline? present = null;
            int earliest = int.MaxValue;
            foreach (Timeline line in working.Multiverse)
            {
                budget.Spend();
                if (!working.LayerIsActive(line.Layer)) continue;
                int time = HypercuboidGameAdapter.HalfTime(line.TEnd, line.ColorPlayable);
                if (time < earliest) { earliest = time; present = line; }
            }
            if (present == null) return false;
            if (present.ColorPlayable != working.Color)
            {
                if (HypercuboidGameAdapter.HasDirectThreat(working, working.Color, budget)) return false;
                witness = moves;
                return true;
            }
            bool accepted = false;
            int attempted = 0;
            foreach (Move move in SpatialCandidates(working, present, budget))
            {
                // This limit bounds only the speculative shortcut, never the
                // complete proof or the length of a game. Its failure falls back.
                if (attempted++ == CandidatesPerBoard) break;
                budget.Spend();
                // Validation uses the current scratch state, including castling
                // transit attacks exposed by earlier moves on other timelines.
                var applied = new CandidateSimulation.MoveScope(working, move, null, validate: true);
                bool keep = false;
                try
                {
                    // All candidates stay on a not-yet-advanced original board;
                    // their source/destination squares therefore still agree
                    // with original. Every successor gets full temporal safety.
                    if (!applied.Succeeded || !RoyalLegality.IsLegalAfterMove(original, move, working, budget))
                        continue;
                    moves.Add(move.Clone());
                    keep = accepted = true;
                }
                finally
                {
                    // Successful scopes deliberately remain applied to this
                    // private scratch state. Only rejected candidates are undone.
                    if (!keep) applied.Dispose();
                }
                if (accepted) break;
            }
            if (!accepted) return false;
        }
        return false;
    }

    // Generate only spatial proposals, lazily, so a quiet witness never needs
    // the full time-travel move inventory. The authoritative validator above is
    // the rules boundary. Underpromotions and every omitted temporal choice
    // remain available to the exhaustive fallback.
    internal static IEnumerable<Move> SpatialCandidates(GameState state, Timeline line, HcBudget budget)
    {
        Board board = line.GetPlayableBoard();
        for (int x = 0; x < state.Width; x++)
        for (int y = 0; y < state.Height; y++)
        {
            budget.Spend();
            int piece = board.GetSquareSafe(x, y);
            if (piece == Board.EMPTYSQUARE || Board.GetColorBool(piece) != state.Color) continue;
            int type = (Math.Abs(piece) - 1) % Board.NUMTYPES + 1;
            bool pawn = type is (int)Board.Piece.WPAWN or (int)Board.Piece.WBRAWN;
            var origin = new CoordFive(x, y, line.TEnd, line.Layer, state.Color);
            foreach (CoordFive vector in MoveGenerator.GetMoveVectors(piece))
            {
                if (!vector.IsSpatial()) continue;
                budget.Spend();
                int range = pawn ? (piece < 0 ? 2 : 1) : MoveGenerator.PieceIsRider(piece) ? int.MaxValue : 1;
                int dx = x, dy = y;
                for (int step = 0; step < range; step++)
                {
                    dx += vector.X; dy += vector.Y;
                    int target = board.GetSquareSafe(dx, dy);
                    if (target == Board.ERRORSQUARE || target != Board.EMPTYSQUARE &&
                        (pawn || Board.GetColorBool(target) == state.Color || Board.IsRoyalPiece(target))) break;
                    yield return CreateMove(origin, dx, dy, piece, pawn, state.Height);
                    if (target != Board.EMPTYSQUARE) break;
                }
            }
            if (pawn)
            {
                CoordFive[] attacks = type == (int)Board.Piece.WBRAWN
                    ? state.Color ? MoveGenerator.whiteBrawnattack : MoveGenerator.blackBrawnattack
                    : state.Color ? MoveGenerator.whitePawnAttack : MoveGenerator.blackPawnattack;
                foreach (CoordFive vector in attacks)
                {
                    if (!vector.IsSpatial()) continue;
                    budget.Spend();
                    int dx = x + vector.X, dy = y + vector.Y;
                    int target = board.GetSquareSafe(dx, dy);
                    if (target == Board.ERRORSQUARE || Board.IsRoyalPiece(target)) continue;
                    bool enPassant = target == Board.EMPTYSQUARE && board.EnPassentSquare is { } ep &&
                        ep.X == dx && ep.Y == dy;
                    if (target != Board.EMPTYSQUARE && Board.GetColorBool(target) != state.Color || enPassant)
                        yield return CreateMove(origin, dx, dy, piece, true, state.Height);
                }
            }
            else if (type == (int)Board.Piece.WKING && piece < 0)
            {
                foreach (bool kingSide in new[] { true, false })
                {
                    budget.Spend();
                    CoordFive? destination = MoveGenerator.KingCanCastle(board, origin, kingSide);
                    if (destination != null)
                        yield return CreateMove(origin, destination.X, destination.Y, piece, false, state.Height);
                }
            }
        }
    }

    private static Move CreateMove(CoordFive origin, int x, int y, int piece, bool pawn, int height)
    {
        var move = new Move(origin.Clone(), new(x, y, origin.T, origin.L, origin.Color))
        { PieceMoved = Math.Abs(piece) };
        if (pawn && y == (origin.Color ? height - 1 : 0))
            move.SpecialType = (int)(origin.Color ? Board.Piece.WQUEEN : Board.Piece.BQUEEN);
        return move;
    }
}
