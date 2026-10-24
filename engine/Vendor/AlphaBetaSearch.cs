using System.Diagnostics;

namespace SkynetZero;

// Independently implemented C# search, inspired by Kairos's iterative PVS,
// transposition bounds, move ordering and quiescence. Uses SkynetZero's rules.
internal sealed record AlphaBetaSettings
{
    public int CompleteTurnDepth { get; init; } = 3;
    public int MaxNodes { get; init; } = 4_000;
    // Minimum allowance for FUTURE atomic actions in a searched turn. Search
    // expands it for playable active frontiers, including newly activated ones,
    // so an ordinary many-board turn can reach its submission before evaluation.
    public int MaxActionsPerTurn { get; init; } = 16;
    public int QuiescenceTurns { get; init; } = 1;
    public int TranspositionEntries { get; init; } = 8_192;
    public bool UsePrincipalVariationSearch { get; init; } = true;
    public bool SelectivePruning { get; init; } = true;
    public bool UseAspirationWindows { get; init; } = true;
    public bool UseNullMovePruning { get; init; } = true;
    public int Threads { get; init; } = 1;

    public void Validate()
    {
        if (CompleteTurnDepth is < 1 or > 64 || MaxNodes < 1 || MaxActionsPerTurn is < 1 or > 128 ||
            QuiescenceTurns is < 0 or > 8 || TranspositionEntries is < 0 or > 1_000_000 || Threads is < 1 or > 64)
            throw new ArgumentException("Invalid alpha-beta depth, node, within-turn, quiescence or table limit.");
    }
}

internal sealed record AlphaBetaLeaf(GameState State, Move? LastMove, int OriginalMin, int OriginalMax,
    IReadOnlyList<Move?> Path);

internal enum AlphaBetaStopReason { CompletedDepth, ProvenWin, NoActions, ForcedSubmission, NodeLimit, TimeLimit, TurnActionLimit, RecursionLimit, SelectiveMateEstimate, GenerationLimit }

internal sealed record AlphaBetaResult(Move?[] Actions, int Selected, int Score, int CompletedDepth,
    int Nodes, int Leaves, int TableHits, int Cutoffs, int Researches, bool Interrupted, double Seconds)
{
    public int ReducedMoves { get; init; }
    public int PrunedMoves { get; init; }
    public int FutilityPrunes { get; init; }
    public int NullMoveProbes { get; init; }
    public int NullMoveCutoffs { get; init; }
    public int AspirationResearches { get; init; }
    public bool Selective => ReducedMoves + PrunedMoves + FutilityPrunes + NullMoveProbes > 0;
    public int[] RootScores { get; init; } = [];
    public int RootScoresDepth { get; init; }
    public int RootActionsSearched { get; init; }
    public Move?[] CompleteTurnMoves { get; init; } = [];
    public Move?[][] CompleteTurnChoices { get; init; } = [];
    // Complete, common-depth full-window comparisons. Raw evaluator units;
    // no repetition penalty, first-action grouping or incomplete iteration.
    internal ScoredTrainingTurn[] TrainingTurns { get; init; } = [];
    internal int TrainingDepth { get; init; }
    public int? ProvenMateDistance { get; init; }
    public int PrincipalVariationLength { get; init; }
    public int? SourceScore { get; init; }
    public bool PlannedContinuation { get; init; }
    public int HelperResults { get; init; }
    public long GenerationWork { get; init; }
    public int RootCycleRepetitions { get; init; }
    public int CyclePenalizedRoots { get; init; }
    public AlphaBetaStopReason StopReason { get; init; } = AlphaBetaStopReason.CompletedDepth;
    public bool Fallback => Interrupted && Selected >= 0 && CompletedDepth == 0;
    public bool ProvenWin => Score == AlphaBetaSearch.Mate &&
        (ProvenMateDistance >= 0 || !Selective && (!Interrupted || CompletedDepth > 0));
}

internal sealed record ScoredTrainingTurn(Move?[] Actions, int Score, bool Proven);

internal static class AlphaBetaSearch { internal const int Mate=1_000_000, EvaluationScale=100_000; }
