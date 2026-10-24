namespace SkynetZero;

/// <summary>
/// Royal safety for an already movement-validated atomic move or turn submission.
/// These checks do not invoke the complete-turn search and do not change either state.
/// </summary>
public static class RoyalLegality
{
    // Version 2 removes proof work/time limits from strict legal play and search.
    public const int Version = 2;

    public static bool IsLegalAfterMove(GameState source, Move move, GameState next) =>
        IsLegalAfterMove(source, move, next, null);

    internal static bool IsLegalAfterMove(GameState source, Move move, GameState next, HcBudget? budget)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(move);
        ArgumentNullException.ThrowIfNull(next);
        budget?.Spend();
        return !next.kingWasTaken && source.Color == next.Color &&
            IsLegalBeforeMove(source, move) && IsSafeAfterMove(next, source.Color, budget);
    }

    // A private replay can check the original board before applying its move,
    // then check the successor without cloning every timeline's history.
    internal static bool IsLegalBeforeMove(GameState source, Move move)
    {
        if (source.kingWasTaken || source.Color != move.Origin.Color ||
            Board.IsRoyalPiece(source.GetSquare(move.Dest))) return false;
        int piece = source.GetSquare(move.Origin);
        return !MoveGenerator.IsCastlingMove(piece, move) || MoveGenerator.CanCastle(source, move);
    }

    internal static bool IsSafeAfterMove(GameState next, bool playerWhite, HcBudget? budget = null)
    {
        // A real attack after an atomic move is permanent for the rest of this
        // player's turn: every path board already exists and is immutable, and
        // the attacking frontier belongs to the other player. New boards cannot
        // block an already existing path. This catches exposed/pinned royals and
        // unsafe king destinations, including arrival boards after time travel.
        // Never null-pass the remaining own boards here: those boards may still
        // answer their own checks in a later atomic move of the same turn.
        return !next.kingWasTaken && playerWhite == next.Color &&
            !HypercuboidGameAdapter.HasDirectThreat(next, playerWhite, budget);
    }

    public static bool CanSubmit(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return !state.kingWasTaken && PresentHasPassed(state) &&
            !HypercuboidGameAdapter.HasDirectThreat(state, state.Color);
    }

    // Read the present without calling CalcPresent (which writes state.Present).
    // Advancing the present allows submission; it does not require submission.
    internal static bool PresentHasPassed(GameState state)
    {
        int earliest = int.MaxValue;
        bool presentColor = state.Color;
        foreach (Timeline line in state.Multiverse)
        {
            if (!state.LayerIsActive(line.Layer)) continue;
            int time = HypercuboidGameAdapter.HalfTime(line.TEnd, line.ColorPlayable);
            if (time < earliest) { earliest = time; presentColor = line.ColorPlayable; }
        }
        return earliest != int.MaxValue && presentColor != state.Color;
    }
}
