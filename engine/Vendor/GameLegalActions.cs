namespace SkynetZero;

// Exhaustive movement-only inventory for proof and rules audits. This low-level
// inventory includes exposed royals and captures for historical replay. Production
// search applies RoyalLegality; the hypercuboid adapter independently enforces the
// same rules while proving complete turns. Policy pruning is never a mate proof.
internal static class GameLegalActions
{
    internal static Move?[] Enumerate(GameState state)
    {
        if (!TryEnumerate(state, null, out Move?[] actions))
            throw new InvalidOperationException("Unbounded game-legal enumeration was unexpectedly interrupted.");
        return actions;
    }

    // The visitor runs only for a successfully validated move, while its
    // make/undo workspace still contains the successor. Search can compute
    // ordering and safety here without applying the same move a second time.
    // Neither the move nor the simulation may be mutated or retained by it.
    internal static bool TryEnumerate(GameState state, Func<bool>? interrupted, out Move?[] result,
        Action<Move, CandidateSimulation>? visitValidated = null)
    {
        if (state.kingWasTaken) { result = []; return true; }

        var actions = new List<Move?>();
        // Each generated action still passes the authoritative validator. A
        // private make/undo workspace avoids cloning all history per action.
        var simulation = new CandidateSimulation(state);
        foreach (Timeline line in state.Multiverse.OrderBy(line => line.Layer))
        {
            if (interrupted?.Invoke() == true) { result = []; return false; }
            if (line.ColorPlayable != state.Color) continue;
            foreach (Move generated in MoveGenerator.GetAllMoves(state,
                         new CoordFive(0, 0, line.TEnd, line.Layer, state.Color)))
            {
                if (interrupted?.Invoke() == true) { result = []; return false; }
                generated.PieceMoved = Math.Abs(state.GetSquare(generated.Origin));
                if (IsPromotionMove(state, generated))
                {
                    foreach (int promotedPiece in PromotionPieces(state.Color))
                    {
                        if (!state.PromotionAllowed((promotedPiece-1)%Board.NUMTYPES+1)) continue;
                        if (state.QueenPromotionOnly &&
                            promotedPiece != (int)(state.Color ? Board.Piece.WQUEEN : Board.Piece.BQUEEN)) continue;
                        if (interrupted?.Invoke() == true) { result = []; return false; }
                        Move promotion = generated.Clone();
                        promotion.SpecialType = promotedPiece;
                        AddValidated(simulation, actions, promotion, visitValidated);
                    }
                }
                else AddValidated(simulation, actions, generated, visitValidated);
            }
        }

        // Submission is legal exactly when the engine says the current
        // multiverse turn has completed. It is not subject to a safety policy.
        if (interrupted?.Invoke() == true) { result = []; return false; }
        if (state.CalcPresent() != state.Color) actions.Add(null);

        result = actions
            .GroupBy(CompleteTurnCandidates.ActionKey, StringComparer.Ordinal)
            .Select(group => group.First()?.Clone())
            .OrderBy(CompleteTurnCandidates.ActionKey, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    internal static string Display(Move? action) => action == null
        ? "SUBMIT"
        : SkynetZero._5DChess.FileIO.StringUtils.ToShadString(action);

    private static void AddValidated(CandidateSimulation simulation, List<Move?> actions, Move action,
        Action<Move, CandidateSimulation>? visitValidated)
    {
        using var applied = simulation.Apply(action.Clone(), validate: true);
        if (!applied.Succeeded)
            throw new InvalidOperationException($"The authoritative move generator produced an invalid action: {Display(action)}; " +
                $"origin={action.Origin}, destination={action.Dest}, mover={simulation.State.Color}, " +
                $"piece={simulation.State.GetSquare(action.Origin)}, target={simulation.State.GetSquare(action.Dest)}, " +
                $"en-passant={simulation.State.GetBoard(action.Origin)?.EnPassentSquare}");
        visitValidated?.Invoke(action, simulation);
        actions.Add(action.Clone());
    }

    private static bool IsPromotionMove(GameState state, Move move)
    {
        int piece = Math.Abs(state.GetSquare(move.Origin));
        int type = piece > Board.NUMTYPES ? piece - Board.NUMTYPES : piece;
        bool pawnish = type is (int)Board.Piece.WPAWN or (int)Board.Piece.WBRAWN;
        return pawnish && move.Type == Move.SPATIALMOVE &&
            move.Dest.Y == (state.Color ? state.Height - 1 : 0);
    }

    // The parser and validator accept promotion to every same-color piece
    // except a pawn or king. Preserve that executable rules surface, including
    // variant pieces, rather than silently reducing it to automatic queen.
    private static IEnumerable<int> PromotionPieces(bool white)
    {
        int colorOffset = white ? 0 : Board.NUMTYPES;
        for (int type = (int)Board.Piece.WKNIGHT; type <= Board.NUMTYPES; type++)
            if (type != (int)Board.Piece.WKING)
                yield return type + colorOffset;
    }
}
