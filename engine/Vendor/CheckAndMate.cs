namespace SkynetZero;

public enum CheckAndMateStatus { Playable, Checkmate, Stalemate, RoyalCapture, Unknown, Draw }
public enum SafeCompletionStatus { Found, Exhausted, Unknown }

/// <summary>A proof about continuing this exact pending turn, not about whether undoing it avoids mate.</summary>
public sealed record SafeCompletionResult(SafeCompletionStatus Status, IReadOnlyList<Move>? Moves = null,
    string? Reason = null, long SearchWork = 0);

/// <summary>Optional proof limits; zero means unlimited. Hitting an explicit limit never proves mate.</summary>
public sealed record CheckAndMateSettings
{
    public long MaxWork { get; init; }
    public int MaxMilliseconds { get; init; }
    public CancellationToken CancellationToken { get; init; }
    // Explicitly opt in only when the caller holds no in-progress neural
    // search. This parks a proof cursor; it never bounds or restarts the proof.
    internal Action<long>? Cooperate { get; init; }
}

public sealed record CheckAndMateResult(bool InCheck, CheckAndMateStatus Status,
    bool? WinnerWhite = null, IReadOnlyList<Move>? SafeTurn = null,
    string? Reason = null, long SearchWork = 0)
{
    public bool IsTerminal => Status is CheckAndMateStatus.Checkmate or
        CheckAndMateStatus.Stalemate or CheckAndMateStatus.RoyalCapture or CheckAndMateStatus.Draw;
    public bool IsConclusive => Status != CheckAndMateStatus.Unknown;
}

/// <summary>
/// Check detection and exhaustive complete-turn mate detection. The
/// hypercuboid search is adapted from penteract/hcuboid-ts; this facade supplies
/// SkynetZero's real move, board, present, and attack rules for its stub imports.
/// </summary>
public static class CheckAndMate
{
    public static bool IsInCheck(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return HypercuboidGameAdapter.IsInCheck(state.Clone());
    }

    public static CheckAndMateResult Analyze(GameState state, CheckAndMateSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        settings = ValidateSettings(settings);
        if (state.kingWasTaken)
            return new(false, CheckAndMateStatus.RoyalCapture, state.kingWasTakenByColor);

        // Rule queries and move simulation work only on private timeline lists.
        // Existing historical boards are immutable and can be shared by Clone.
        GameState snapshot = state.Clone();
        bool inCheck = HypercuboidGameAdapter.IsInCheck(snapshot.Clone());
        // With no check, checkmate cannot take precedence. Claim a known
        // draw without enumerating complete turns across a large multiverse.
        if (!inCheck && DrawRules.ClaimableReason(snapshot) is { } drawReason)
            return new(false, CheckAndMateStatus.Draw, Reason: drawReason);
        SafeCompletionResult result = FindSafeCompletionCore(snapshot, settings);
        return result.Status switch
        {
            SafeCompletionStatus.Found when DrawRules.ClaimableReason(snapshot) is { } draw =>
                new(inCheck, CheckAndMateStatus.Draw, Reason: draw, SearchWork: result.SearchWork),
            SafeCompletionStatus.Found => new(inCheck, CheckAndMateStatus.Playable,
                SafeTurn: result.Moves, SearchWork: result.SearchWork),
            SafeCompletionStatus.Exhausted when snapshot.TurnMoves.Count != 0 => new(inCheck,
                CheckAndMateStatus.Unknown,
                Reason: "The pending turn has no safe completion; undoing its earlier moves may still avoid mate.",
                SearchWork: result.SearchWork),
            SafeCompletionStatus.Exhausted => new(inCheck,
                inCheck ? CheckAndMateStatus.Checkmate : CheckAndMateStatus.Stalemate,
                inCheck ? !snapshot.Color : null, SearchWork: result.SearchWork),
            _ => new(inCheck, CheckAndMateStatus.Unknown, Reason: result.Reason, SearchWork: result.SearchWork)
        };
    }

    /// <summary>
    /// Finds a legal completion of the exact current turn. Exhausted can prune a
    /// bad partial choice, but must not be interpreted as mate of its starting
    /// position: earlier pending moves could be undone. Unknown never proves an
    /// action illegal. This method does not perform the separate check-status query.
    /// </summary>
    public static SafeCompletionResult FindSafeCompletion(GameState state, CheckAndMateSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        return FindSafeCompletionCore(state.Clone(), ValidateSettings(settings));
    }

    private static SafeCompletionResult FindSafeCompletionCore(GameState snapshot, CheckAndMateSettings settings)
    {
        if (snapshot.kingWasTaken)
            return new(SafeCompletionStatus.Exhausted, Reason: "A game ending in royal capture has no legal continuation.");
        var budget = new HcBudget(settings.MaxWork, settings.CancellationToken, settings.MaxMilliseconds,
            cooperate: settings.Cooperate);
        try
        {
            var adapter = new HypercuboidGameAdapter(snapshot, budget);
            HcSearchResult<Move> result = adapter.Search();
            return result.Status switch
            {
                HcSearchStatus.Found => new(SafeCompletionStatus.Found,
                    Moves: result.Moves!.Select(move => move.Clone()).ToArray(), SearchWork: result.Work),
                HcSearchStatus.Exhausted => new(SafeCompletionStatus.Exhausted, SearchWork: result.Work),
                _ => new(SafeCompletionStatus.Unknown,
                    Reason: "Complete-turn proof reached its work, time, or cancellation limit.", SearchWork: result.Work)
            };
        }
        catch (HcBudgetExceededException)
        {
            return new(SafeCompletionStatus.Unknown,
                Reason: "Complete-turn proof reached its work, time, or cancellation limit.", SearchWork: budget.Work);
        }
        catch (NotSupportedException exception)
        {
            return new(SafeCompletionStatus.Unknown, Reason: exception.Message, SearchWork: budget.Work);
        }
    }

    private static CheckAndMateSettings ValidateSettings(CheckAndMateSettings? settings)
    {
        settings ??= new();
        if (settings.MaxWork < 0 || settings.MaxMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "Mate detection limits must be nonnegative.");
        return settings;
    }
}
