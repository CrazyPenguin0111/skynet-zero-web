namespace SkynetZero;

// Live play keeps a certified way to finish the pending turn. Search may explore
// unproved partial choices, but cannot commit the game to a dead end or use a
// resource-limited proof as permission to play an unsafe move.
internal sealed class LegalTurnContinuation(CheckAndMateSettings limits)
{
    private Move[]? witness;
    private string? witnessPosition;
    internal int UnknownProofs { get; private set; }
    internal int Fallbacks { get; private set; }

    internal void Reset(GameState state, IReadOnlyList<Move>? moves = null)
    {
        witness = moves?.Select(move => move.Clone()).ToArray();
        // This identity is private to the live turn, not a durable book key.
        // Reuse the complete binary state hash instead of serializing every
        // historical square into a new string for each atomic continuation.
        witnessPosition = witness is { Length: > 0 } ? CompleteTurnStateHash.Compute(state) : null;
    }

    // Native search has already found a complete turn. Replay its selected
    // sequence once instead of launching another exhaustive completion search
    // after its first move. Exploration may choose a different turn from best.
    internal bool TryRememberSearchTurn(GameState state, AlphaBetaResult result, Move? selected)
    {
        if (selected == null) return false;
        string selectedKey = CompleteTurnCandidates.ActionKey(selected);
        if (witness is { Length: > 0 } && witnessPosition == CompleteTurnStateHash.Compute(state) &&
            CompleteTurnCandidates.ActionKey(witness[0]) == selectedKey) return true;
        int index = Array.FindIndex(result.Actions, move => CompleteTurnCandidates.ActionKey(move) == selectedKey);
        if (index < 0) return false;
        Move?[] sequence = index < result.CompleteTurnChoices.Length ? result.CompleteTurnChoices[index] :
            index == result.Selected ? result.CompleteTurnMoves : [];
        if (sequence.Length < 2 || sequence[^1] != null ||
            CompleteTurnCandidates.ActionKey(sequence[0]) != selectedKey) return false;
        var replay = state.Clone();
        bool playerWhite = state.Color;
        for (int i = 0; i < sequence.Length - 1; i++)
        {
            limits.CancellationToken.ThrowIfCancellationRequested();
            if (sequence[i] is not { } move || !replay.CoordIsPlayable(move.Origin) ||
                !RoyalLegality.IsLegalBeforeMove(replay, move) || !replay.MakeMove(move.Clone(), true) ||
                !RoyalLegality.IsSafeAfterMove(replay, playerWhite)) return false;
        }
        if (!RoyalLegality.CanSubmit(replay) || !replay.SubmitMoves() || replay.Color == playerWhite) return false;
        Reset(state, sequence.OfType<Move>().ToArray());
        return true;
    }

    internal bool TryChoose(GameState state, bool hasProposal, Move? proposal,
        out Move? selected, out bool changed)
    {
        selected = null; changed = false;
        if (witness is { Length: > 0 } && witnessPosition != CompleteTurnStateHash.Compute(state))
            Reset(state);
        if (hasProposal && TryCandidate(state, proposal))
        { selected = proposal?.Clone(); return true; }

        changed = true;
        if (RoyalLegality.CanSubmit(state))
        { Reset(state); Fallbacks++; return true; }
        if (witness is not { Length: > 0 })
        {
            var proof = CheckAndMate.FindSafeCompletion(state, limits);
            if (proof.Status == SafeCompletionStatus.Unknown) UnknownProofs++;
            if (proof.Status != SafeCompletionStatus.Found) return false;
            Reset(state, proof.Moves);
        }
        if (witness is not { Length: > 0 }) return false;
        Move fallback = witness[0].Clone();
        if (!TryCandidate(state, fallback)) return false;
        selected = fallback; Fallbacks++;
        return true;
    }

    private bool TryCandidate(GameState state, Move? candidate)
    {
        if (candidate == null)
        {
            if (!RoyalLegality.CanSubmit(state)) return false;
            Reset(state);
            return true;
        }
        var next = state.Clone();
        Move move = candidate.Clone();
        if (!state.CoordIsPlayable(move.Origin) || !next.MakeMove(move, true) ||
            !RoyalLegality.IsLegalAfterMove(state, move, next)) return false;
        if (RoyalLegality.CanSubmit(next)) { Reset(next); return true; }

        // A previously certified prefix needs no repeated complete-turn proof.
        if (witness is { Length: > 0 } &&
            BookAction.From(witness[0]) == BookAction.From(candidate))
        { Reset(next, witness.Skip(1).ToArray()); return true; }
        var proof = CheckAndMate.FindSafeCompletion(next, limits);
        if (proof.Status == SafeCompletionStatus.Unknown) UnknownProofs++;
        if (proof.Status != SafeCompletionStatus.Found) return false;
        Reset(next, proof.Moves);
        return true;
    }
}
