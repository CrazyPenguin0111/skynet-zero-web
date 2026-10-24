namespace SkynetZero;

// A partial 5D turn may still need repair on another board. Such a continuation
// remains searchable, but neither an illegal royal move nor unsafe submission
// becomes eligible because the optional heuristic safeguard is disabled.
internal enum RoyalSafety { UnfinishedTurn, Safe, Exposed, Winning }

internal static class KingSafety
{
    public static RoyalSafety Classify(GameState source, Move move)
    {
        var successor = source.Clone();
        if (!successor.CoordIsPlayable(move.Origin) || !successor.MakeMove(move.Clone(), true))
            throw new InvalidOperationException("Safety candidate failed to apply.");
        return ClassifyApplied(source, move, successor);
    }

    // Only attacks from actual opponent frontiers are irreversible within this
    // turn. Synthetic pass attacks on still-unmoved boards may be repaired by
    // later actions, so they cannot make an unfinished move illegal.
    public static RoyalSafety ClassifyApplied(GameState source, Move? move, GameState successor)
    {
        if (move == null) return RoyalLegality.CanSubmit(source) ? RoyalSafety.Safe : RoyalSafety.Exposed;
        if (!source.CoordIsPlayable(move.Origin) || !RoyalLegality.IsLegalAfterMove(source, move, successor))
            return RoyalSafety.Exposed;
        return RoyalLegality.PresentHasPassed(successor) ? RoyalSafety.Safe : RoyalSafety.UnfinishedTurn;
    }

    public static int[] Allowed(IReadOnlyList<RoyalSafety> safety, bool enabled)
    {
        var wins = Enumerable.Range(0, safety.Count).Where(i => safety[i] == RoyalSafety.Winning).ToArray();
        if (wins.Length > 0) return wins;
        return Enumerable.Range(0, safety.Count)
            .Where(i => safety[i] != RoyalSafety.Exposed).ToArray();
    }
}

internal sealed class SafetyCounters
{
    public long Decisions { get; set; }
    public long CompletedTurns { get; set; }
    public long RejectedCandidates { get; set; }
    public long NoSafeCompletingAlternative { get; set; }
    public long UnguardedGreedyExposures { get; set; }
    public long SelectedExposures { get; set; }
    public long NewTimelines { get; set; }

    public void Add(SafetyCounters other)
    {
        Decisions += other.Decisions; CompletedTurns += other.CompletedTurns;
        RejectedCandidates += other.RejectedCandidates;
        NoSafeCompletingAlternative += other.NoSafeCompletingAlternative;
        UnguardedGreedyExposures += other.UnguardedGreedyExposures;
        SelectedExposures += other.SelectedExposures; NewTimelines += other.NewTimelines;
    }
}

internal sealed class GameSafety
{
    public SafetyCounters White { get; set; } = new();
    public SafetyCounters Black { get; set; } = new();
    public SafetyCounters For(bool white) => white ? White : Black;
}
