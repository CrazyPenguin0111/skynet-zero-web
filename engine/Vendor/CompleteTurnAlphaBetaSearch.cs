// Adapted from FirePlank/5d-chess-engine src/engine/ab.rs, commit
// 57e97a550f731e163cc9156d269e1aa5c6fe3874, GPL-3.0. Complete actions use SkynetZero's authoritative rules.
// Search updates audited against 0ff04c293c05c149246bf34bf423f64353c85abe;
// see ENGINE_SOURCE_PARITY.md for ports and deliberate differences.
using System.Diagnostics;

namespace SkynetZero;

internal enum CompleteTurnBound { Exact, Lower, Upper }
internal sealed record CompleteTurnTableEntry(int Depth, int Score, bool Proven, CompleteTurnBound Bound, Move?[] Action);
internal sealed record CompleteTurnResume(string Key, Move?[][] Actions, int[] Scores, int Depth,
    string BestKey, int Score, bool Proven, bool Complete, int PvLength)
{ internal Move?[][] PrincipalVariation { get; init; } = []; }

// One game retains ordering information; helpers share only synchronized caches.
// Call ResetEvaluation whenever the model, books or evaluation configuration changes.
internal sealed class CompleteTurnSearchMemory
{
    internal sealed class Shared
    {
        internal readonly object Gate = new();
        internal readonly Dictionary<string, CompleteTurnTableEntry> Table = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> Static = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, bool> Checks = new(StringComparer.Ordinal);
        internal object? Context;
    }
    internal Shared Cache = new();
    internal readonly int[] History = new int[SearchHeuristics.HistorySize];
    internal readonly Dictionary<int, (Move?[]? First, Move?[]? Second)> Killers = new();
    internal readonly Dictionary<int, Move?[]> EvasionKillers = new();
    internal CompleteTurnResume? Resume;
    internal void BeginSearch()
    {
        // Source begin_search retains history and TT, but resets leaf caches
        // and ply-specific killers for the new root search.
        lock (Cache.Gate) { Cache.Static.Clear(); Cache.Checks.Clear(); }
        Killers.Clear(); EvasionKillers.Clear();
    }
    internal void ResetEvaluation(object? context = null)
    {
        lock (Cache.Gate)
        {
            if (context != null && Equals(context, Cache.Context)) return;
            Cache.Context = context; Cache.Table.Clear(); Cache.Static.Clear(); Cache.Checks.Clear(); Resume = null;
        }
    }
    internal void Clear()
    { ResetEvaluation(); Array.Clear(History); Killers.Clear(); EvasionKillers.Clear(); }
    internal CompleteTurnSearchMemory Fork()
    {
        var fork = new CompleteTurnSearchMemory { Cache = Cache, Resume = Resume };
        History.CopyTo(fork.History, 0);
        foreach (var pair in Killers) fork.Killers.Add(pair.Key, pair.Value);
        foreach (var pair in EvasionKillers) fork.EvasionKillers.Add(pair.Key, pair.Value);
        return fork;
    }
}

internal sealed class CompleteTurnSearchControl(int maximumNodes)
{
    private int nodes;
    private long generationWork;
    private int stopped;
    internal int Nodes => Volatile.Read(ref nodes);
    internal long GenerationWork => Volatile.Read(ref generationWork);
    internal bool GenerationExhausted => GenerationWork >= CompleteTurnAlphaBetaSearch.GenerationAllowance(maximumNodes);
    internal void SpendGeneration(long amount) => Interlocked.Add(ref generationWork, amount);
    internal bool Stop { get => Volatile.Read(ref stopped) != 0; set => Volatile.Write(ref stopped, value ? 1 : 0); }
    internal bool Exhausted => Stop || Nodes >= maximumNodes;
    internal bool TryVisit()
    {
        while (!Stop)
        {
            int before = Volatile.Read(ref nodes);
            if (before >= maximumNodes) return false;
            if (Interlocked.CompareExchange(ref nodes, before + 1, before) == before) return true;
        }
        return false;
    }
}

internal sealed class CompleteTurnAlphaBetaSearch
{
    internal const int Mate = 30_000, Infinity = 32_000, EvaluationMaximum = 20_000;
    internal const int RootCap = 1000, RootWidenCap = 256, QuiescenceCap = 4, QuiescenceDepth = 2;
    internal const int PrincipalVariationCapMultiplier = 4;
    internal const int SearchWorkVersion = 3;
    // Nodes alone do not bound rejected hypercuboids or matching work. These
    // deterministic search operations replace the source clock allocation when
    // training/arena searches deliberately have no wall-clock deadline.
    internal const int GenerationWorkPerNode = 4096;
    internal static long GenerationAllowance(int maximumNodes) => Math.Max(262_144L, (long)maximumNodes * GenerationWorkPerNode);
    private const int MaxPly = 128;
    private static readonly int[] LateMoveCaps = [0, 20, 32, 44, 56];
    private readonly record struct Value(int Score, bool Proven = false);
    private sealed class SearchInterrupted(AlphaBetaStopReason reason) : Exception
    { internal readonly AlphaBetaStopReason Reason = reason; }
    private readonly AlphaBetaSettings settings;
    private readonly int milliseconds, threadId;
    private readonly bool rootWhite, evaluatorUnits, allowSyntheticSearch, collectRootScores;
    private readonly Func<AlphaBetaLeaf, double>? evaluate;
    private readonly object? books;
    private readonly Func<GameState, IReadOnlyList<Move?>, float[]>? policy;
    private readonly CancellationToken token;
    private readonly CompleteTurnSearchMemory memory;
    private readonly Func<bool>? interruptedSignal;
    private readonly CompleteTurnSearchControl? control;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<Move?> path = new();
    private readonly Dictionary<int, int> pvLength = new();
    private readonly Dictionary<int, Move?[][]> pvLines = new();
    private Move?[][] bestLine = [];
    private GameState? rootForPv;
    private readonly Dictionary<string, int> firstActionOrder = new(StringComparer.Ordinal);
    private SearchCycleHistory? cycleHistory;
    private readonly Dictionary<string, int> cyclePenalties = new(StringComparer.Ordinal);
    private readonly HashSet<string> penalizedCycleRoots = new(StringComparer.Ordinal);
    private int nodes, leaves, hits, cutoffs, researches, reduced, reducedTravels, pruned, futility, nullProbes, nullCutoffs, aspirations;
    private int internalReductions, singularSearches, singularExtensions;
    private long generationWork;
    private (string Position, string Action)? excluded;
    private Func<bool>? rootGenerationInterrupted;

    private CompleteTurnAlphaBetaSearch(GameState root, AlphaBetaSettings settings, int milliseconds,
        Func<AlphaBetaLeaf, double>? evaluate, object? books, bool collectRootScores,
        Func<GameState, IReadOnlyList<Move?>, float[]>? policy, CancellationToken token,
        bool evaluatorUnits, bool allowSyntheticSearch, CompleteTurnSearchMemory memory, int threadId,
        Func<bool>? interrupted, CompleteTurnSearchControl? control)
    {
        this.settings = settings; this.milliseconds = milliseconds; this.evaluate = evaluate;
        this.books = books; this.collectRootScores = collectRootScores; this.policy = policy;
        this.token = token; this.evaluatorUnits = evaluatorUnits; this.allowSyntheticSearch = allowSyntheticSearch;
        this.memory = memory; this.threadId = threadId; rootWhite = root.Color;
        interruptedSignal = interrupted; this.control = control;
    }

    internal static AlphaBetaResult Choose(GameState state, AlphaBetaSettings settings, int milliseconds = 0,
        bool guard = true, Func<AlphaBetaLeaf, double>? evaluate = null, IReadOnlyList<Move>? restrictedRoot = null,
        object? books = null, bool collectRootScores = false,
        Func<GameState, IReadOnlyList<Move?>, float[]>? rootPolicy = null, CancellationToken cancellationToken = default,
        bool evaluatorUnits = false, bool allowSyntheticSearch = true, CompleteTurnSearchMemory? memory = null,
        int threadId = 0, Func<bool>? interrupted = null, CompleteTurnSearchControl? control = null)
    {
        settings.Validate(); cancellationToken.ThrowIfCancellationRequested();
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        return new CompleteTurnAlphaBetaSearch(state, settings, milliseconds, evaluate, books, collectRootScores,
            rootPolicy, cancellationToken, evaluatorUnits, allowSyntheticSearch, memory ?? new(), threadId, interrupted, control)
            .Run(state.Clone(), restrictedRoot);
    }

    internal static int ActionCap(int depth, bool principalVariation) =>
        LateMoveCaps[Math.Clamp(depth, 0, LateMoveCaps.Length - 1)] * (principalVariation ? PrincipalVariationCapMultiplier : 1);

    // A bounded teacher compares explicit legal complete turns. This path never
    // replaces live move selection and owns a fresh transposition/search state.
    internal static AlphaBetaResult CompareForTraining(GameState state, AlphaBetaSettings settings,
        IReadOnlyList<Move?[]> alternatives, Func<AlphaBetaLeaf, double> evaluate, CancellationToken token)
    {
        settings.Validate(); token.ThrowIfCancellationRequested();
        if (alternatives.Count < 2) throw new ArgumentException("Teacher requires multiple complete turns.");
        return new CompleteTurnAlphaBetaSearch(state, settings, 0, evaluate, null, true, null,
            token, true, false, new(), 0, null, null).Run(state.Clone(), null, alternatives);
    }

    private ScoredTrainingTurn[] trainingTurns = [];
    private int trainingDepth;
    internal static int Reduction(int depth, int index, bool principalVariation) => Math.Clamp(
        (int)(1f + MathF.Log(Math.Max(depth, 1)) * MathF.Log(Math.Max(index, 1)) / 3f) - (principalVariation ? 1 : 0), 0, Math.Max(0, depth - 2));
    internal static int MateToTable(int score, int ply) => score > Mate - 1000 ? score + ply : score < -Mate + 1000 ? score - ply : score;
    internal static int MateFromTable(int score, int ply) => score > Mate - 1000 ? score - ply : score < -Mate + 1000 ? score + ply : score;
    private long GenerationWork => control?.GenerationWork ?? generationWork;
    private bool GenerationExhausted => control?.GenerationExhausted ?? generationWork >= GenerationAllowance(settings.MaxNodes);
    private void SpendGeneration(long amount)
    {
        if (amount <= 0) return;
        generationWork += amount;
        control?.SpendGeneration(amount);
    }
    private bool Expired => (control?.Exhausted ?? nodes >= settings.MaxNodes) || GenerationExhausted || interruptedSignal?.Invoke() == true ||
        milliseconds > 0 && clock.ElapsedMilliseconds >= milliseconds;
    private bool GenerationInterrupted() { token.ThrowIfCancellationRequested(); return Expired; }
    private void CheckBudget()
    {
        token.ThrowIfCancellationRequested();
        if (GenerationExhausted) throw new SearchInterrupted(AlphaBetaStopReason.GenerationLimit);
        if ((control?.Exhausted ?? nodes >= settings.MaxNodes) || interruptedSignal?.Invoke() == true)
            throw new SearchInterrupted(AlphaBetaStopReason.NodeLimit);
        if (milliseconds > 0 && clock.ElapsedMilliseconds >= milliseconds) throw new SearchInterrupted(AlphaBetaStopReason.TimeLimit);
    }
    private void VisitNode()
    {
        CheckBudget();
        if (control != null && !control.TryVisit()) throw new SearchInterrupted(AlphaBetaStopReason.NodeLimit);
        nodes++;
    }
    private static string ActionKey(IEnumerable<Move?> actions) => string.Join("|", actions.Select(CompleteTurnCandidates.ActionKey));
    private static Move?[] Copy(IEnumerable<Move?> actions) => actions.Select(move => move?.Clone()).ToArray();
    private string Key(GameState state, Move? last, int minimum, int maximum) =>
        $"{CompleteTurnStateHash.Compute(state)}|{rootWhite}|{evaluatorUnits}|{SacrificeTable.CurrentRootTravel?.Identity ?? 0}|{(evaluate == null ? "" : $"{CompleteTurnCandidates.ActionKey(last)}|{minimum}|{maximum}")}";

    private CompleteTurnSearchGenerator Generator(GameState state, bool restrictedOptional = true,
        bool fallback = false, IReadOnlyList<Move>? restrictedRoot = null, Func<bool>? interrupt = null)
    {
        // Deferred branch-axis ordering must use the same history snapshot as
        // the physical axes even after earlier actions update the live history.
        int[] history = (int[])memory.History.Clone();
        CompleteTurnSearchGenerator? generator = null;
        long observedWork = 0;
        double Score(Move move, GameState next)
        {
            int slot = SearchHeuristics.HistoryIndex(state.GetSquare(move.Origin), move);
            bool check = generator!.MoveGivesCheck(move, next);
            double score = SearchHeuristics.CoordinateScore(state, move, next, history[slot], check);
            return score;
        }
        bool Stop()
        {
            token.ThrowIfCancellationRequested();
            if (generator != null)
            {
                long work = generator.Work;
                SpendGeneration(work - observedWork);
                observedWork = work;
            }
            if (interrupt != null) return interrupt();
            if (!fallback && Expired) CheckBudget();
            return false;
        }
        generator = new(state, Score, token, Stop, restrictedOptional, restrictedRoot: restrictedRoot);
        // Root inventory construction is finite input work needed before any
        // action can be generated; it cannot consume the first-action reserve.
        // All deeper inventories count against the shared search allowance.
        observedWork = generator.Work;
        if (!fallback) { SpendGeneration(observedWork); CheckBudget(); }
        return generator;
    }

    private AlphaBetaResult Run(GameState root, IReadOnlyList<Move>? restricted, IReadOnlyList<Move?[]>? comparison = null)
    {
        rootForPv = root;
        cycleHistory = SearchCycleHistory.FromRoot(root, token);
        var roots = new List<CompleteTurnSearchAction>();
        var rootKeys = new HashSet<string>(StringComparer.Ordinal);
        CompleteTurnSearchGenerator? generator = null;
        bool interrupted = false, complete = false, widened = false, rootGenerating = false, recoveredEarly = false;
        long generationEnd = milliseconds > 0 ? Math.Max(1, milliseconds / 4) : long.MaxValue;
        long quickEnd = milliseconds > 0 ? Math.Max(1, milliseconds / 8) : long.MaxValue;
        long generationStart = GenerationWork;
        long generationAllowance = GenerationAllowance(settings.MaxNodes);
        bool RootAllowanceSpent() => GenerationWork - generationStart >= generationAllowance / 4 ||
            clock.ElapsedMilliseconds >= generationEnd;
        bool QuickAllowanceSpent() => GenerationWork - generationStart >= generationAllowance / 8 ||
            clock.ElapsedMilliseconds >= quickEnd;
        var reason = AlphaBetaStopReason.CompletedDepth;
        Value best = new(0); CompleteTurnSearchAction? chosen = null;
        int completed = 0, resumedDepth = 0, bestPv = 0, scoresDepth = 0;
        Dictionary<string, int> exactScores = new(StringComparer.Ordinal), previousScores = new(StringComparer.Ordinal);
        var searchedFirst = new HashSet<string>(StringComparer.Ordinal);
        var resume = memory.Resume;
        string rootKey = Key(root, null, root.MinTL, root.MaxTL) + (restricted == null ? "|all-root-actions" :
            "|restricted:" + string.Join(";", restricted.Select(CompleteTurnCandidates.ActionKey)));
        bool Add(CompleteTurnSearchAction action)
        {
            if (!rootKeys.Add(action.Key)) return false;
            cyclePenalties[action.Key] = cycleHistory.Penalty(action.State);
            // Roots retain move sequences and metadata, not a clone of every
            // timeline's complete history for each of up to 1000 choices.
            roots.Add(action.VerifiedFallback ? action : action with { State = null! });
            return true;
        }
        try
        {
            if (root.kingWasTaken) return Result([], null, Terminal(root, 0), 0, false, AlphaBetaStopReason.NoActions, new Dictionary<string,int>(), 0, [], 0);
            if (DrawTerminal(root, 0) is { } draw)
                return Result([], null, draw, 0, false, AlphaBetaStopReason.NoActions, new Dictionary<string,int>(), 0, [], 0);
            // Revalidate remembered actions against the current immutable root.
            if (resume?.Key == rootKey)
            {
                for (int i = 0; i < resume.Actions.Length; i++)
                {
                    var replay = Replay(root, resume.Actions[i]);
                    if (replay == null || restricted != null && !restricted.Any(move => CompleteTurnCandidates.ActionKey(move) == CompleteTurnCandidates.ActionKey(replay.FirstAction))) continue;
                    Add(replay); previousScores[replay.Key] = resume.Scores[i];
                }
                chosen = roots.FirstOrDefault(action => action.Key == resume.BestKey);
                if (chosen != null)
                {
                    resumedDepth = resume.Depth;
                    if (threadId == 0)
                    { best = new(resume.Score, resume.Proven); completed = resume.Depth; bestPv = resume.PvLength; bestLine = resume.PrincipalVariation; }
                }
            }
            bool resuming = chosen != null && resume?.Key == rootKey;
            if (resuming)
            {
                complete = resume!.Complete;
                if (threadId == 0 && completed >= settings.CompleteTurnDepth && !collectRootScores)
                    return Result(roots, chosen, best, completed, false, reason, exactScores, 0, searchedFirst, bestPv);
            }
            else if (comparison != null)
            {
                foreach (var moves in comparison)
                    Add(Replay(root, moves) ?? throw new InvalidDataException("Teacher alternative is not a legal complete turn."));
                // Exhausting this subset cannot certify exhaustion of legal moves.
                complete = false;
            }
            else
            {
            generator = Generator(root, fallback: true, restrictedRoot: restricted, interrupt: () =>
                (roots.Count > 0 || threadId > 0) && Expired || generator != null && (RootAllowanceSpent() || GenerationExhausted) || rootGenerating &&
                roots.Count == 0 && !recoveredEarly && generator?.RestrictedOptionalBoards == true && QuickAllowanceSpent());
            generationStart = GenerationWork;
            rootGenerationInterrupted = RootAllowanceSpent;
            try
            {
            // Probe the tactically important classes before a wide normal
            // root inventory can use the available generation time.
            long probeEnd = clock.ElapsedMilliseconds + Math.Max(1, milliseconds > 0 ? milliseconds / 32 : 50);
            generator.GetFutureCheckingTravelCandidates(int.MaxValue, 16,
                () => Expired || clock.ElapsedMilliseconds >= probeEnd, action =>
                {
                    if (Expired || clock.ElapsedMilliseconds >= probeEnd) return true;
                    Value mate = ProbeFutureMate(action, 1, probeEnd);
                    if (!mate.Proven || mate.Score >= 0) return false;
                    Add(action); chosen = action; best = new(-mate.Score, true); bestPv = 1;
                    bestLine = [Copy(action.Actions)]; return true;
                });
            if (!best.Proven)
                generator.GetStandaloneCheckingTravelCandidates(visitCandidate: action =>
                {
                    Add(action); Value mate = ProbeMate(action, 1);
                    if (!mate.Proven || mate.Score >= 0) return false;
                    chosen = action; best = new(-mate.Score, true); bestPv = 1;
                    bestLine = [Copy(action.Actions)]; return true;
                });
            // Establish a replayable full-turn fallback before obeying an expired
            // tiny wall-clock budget. No artificial outcome is inferred from caps.
            int checkingRoots = roots.Count;
            foreach (var action in generator.GetTravelCaptureCandidates(RootCap, 16)) Add(action);
            rootGenerating = true;
            while (roots.Count < RootCap + checkingRoots)
            {
                if (roots.Count > 0 && Expired) break;
                CompleteTurnSearchAction[] batch;
                try { batch = generator.NextBatch(Math.Min(12, RootCap + checkingRoots - roots.Count)); }
                catch (HcBudgetExceededException) when (roots.Count == 0 && !recoveredEarly &&
                    generator.RestrictedOptionalBoards && QuickAllowanceSpent() && !RootAllowanceSpent())
                {
                    recoveredEarly = true; generator.RestartFull(); continue;
                }
                foreach (var action in batch) Add(action);
                if (generator.Exhausted)
                {
                    if (generator.GeneratedCount == 0 && generator.RestrictedOptionalBoards) { generator.RecoverOptionalMoves(); continue; }
                    break;
                }
            }
            }
            catch (HcBudgetExceededException) when (RootAllowanceSpent() || GenerationExhausted)
            {
                token.ThrowIfCancellationRequested();
                // A root allowance stops alternative enumeration, not the game.
                // With no candidate, the unordered exact cursor below supplies
                // a replayable positive witness instead of inventing no-actions.
            }
            rootGenerating = false;
            rootGenerationInterrupted = null;
                complete = !recoveredEarly && restricted == null && generator.Exhausted && !generator.Incomplete &&
                    !generator.RestrictedOptionalBoards && generator.WastedMovesPruned == 0;
            if (roots.Count == 0 && !complete)
            {
                CompleteTurnSearchAction? witness = LegalFallback(root, restricted);
                if (witness != null) Add(witness);
            }
            }
            chosen ??= roots.FirstOrDefault();
            if (chosen == null)
            {
                if (Expired) { CheckBudget(); }
                best = restricted == null ? NoActions(root, 0, null, root.MinTL, root.MaxTL) : new(Static(root, null, root.MinTL, root.MaxTL));
                reason = AlphaBetaStopReason.NoActions;
                return Result(roots, chosen, best, completed, false, reason, exactScores, 0, searchedFirst, bestPv);
            }
            if (!complete) pruned++;
            foreach (var action in roots)
            {
                string key = CompleteTurnCandidates.ActionKey(action.FirstAction);
                firstActionOrder.TryAdd(key, firstActionOrder.Count);
            }
            if (policy != null)
            {
                var firsts = roots.GroupBy(action => CompleteTurnCandidates.ActionKey(action.FirstAction)).Select(group => group.First().FirstAction).ToArray();
                float[] logits = policy(root, firsts);
                if (logits.Length != firsts.Length || logits.Any(value => !float.IsFinite(value)))
                    throw new InvalidOperationException("Complete-turn policy returned invalid logits.");
                var hints = firsts.Select((move, i) => (Key: CompleteTurnCandidates.ActionKey(move), Value: 100 * Math.Tanh(logits[i])))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                if (!resuming) roots.Sort((a, b) => (ActionOrder(root, b, 0) + hints[CompleteTurnCandidates.ActionKey(b.FirstAction)])
                    .CompareTo(ActionOrder(root, a, 0) + hints[CompleteTurnCandidates.ActionKey(a.FirstAction)]));
            }
            else if (!resuming) roots.Sort((a, b) => ActionOrder(root, b, 0).CompareTo(ActionOrder(root, a, 0)));
            if (chosen != null && (completed > 0 || best.Proven)) { roots.RemoveAll(action => action.Key == chosen.Key); roots.Insert(0, chosen); }
            int firstDepth = Math.Max(threadId % 2 == 1 ? Math.Min(2, settings.CompleteTurnDepth) : 1,
                Math.Min(resumedDepth + 1, settings.CompleteTurnDepth));
            for (int baseDepth = firstDepth; baseDepth <= settings.CompleteTurnDepth; baseDepth++)
            {
                int depth = threadId % 2 == 1 ? Math.Min(baseDepth + 1, settings.CompleteTurnDepth) : baseDepth;
                VisitNode();
                bool aspiration = settings.UseAspirationWindows && !collectRootScores && !cycleHistory.Active &&
                    depth >= 2 && completed > 0 && Math.Abs(best.Score) < Mate - 1000;
                int center = best.Score, width = 750, tries = 0, low = aspiration ? center - width : -Infinity, high = aspiration ? center + width : Infinity;
                CompleteTurnSearchAction? failHigh = null; Move?[][] failHighLine = [];
                while (true)
                {
                    int alpha = low; Value iterationBest = new(-Infinity); CompleteTurnSearchAction? iterationAction = null;
                    var iterationScores = new Dictionary<string, int>(StringComparer.Ordinal);
                    var rawTrainingScores = new List<ScoredTrainingTurn>();
                    bool allProven = complete; int iterationPv = 0; Move?[][] iterationLine = [];
                    try
                    {
                        for (int index = 0; index < roots.Count; index++)
                        {
                            CheckBudget(); var action = roots[index];
                            int reduction = comparison == null && depth >= 3 && index >= 6 && !action.GivesCheck ? Reduction(depth, index, true) : 0;
                            // A revisitation adjustment is outside the model's
                            // value. Full root windows keep raw PVS bounds from
                            // being compared against adjusted root scores.
                            bool fullRoot = collectRootScores || cycleHistory.Active;
                            Value value = Child(action, depth, 0, fullRoot ? -Infinity : alpha,
                                fullRoot ? Infinity : high, fullRoot || index == 0, true, reduction);
                            if (comparison != null) rawTrainingScores.Add(new(Copy(action.Actions), value.Score, value.Proven));
                            value = AdjustCycle(action.Key, value);
                            iterationScores[action.Key] = value.Score; searchedFirst.Add(CompleteTurnCandidates.ActionKey(action.FirstAction));
                            allProven &= value.Proven;
                            if (value.Score > iterationBest.Score) { iterationBest = value; iterationAction = action; iterationPv = 1 + pvLength.GetValueOrDefault(1); iterationLine = [Copy(action.Actions), ..pvLines.GetValueOrDefault(1, [])]; }
                            alpha = Math.Max(alpha, value.Score);
                            if (!collectRootScores && alpha >= high) break;
                        }
                    }
                    catch (SearchInterrupted error)
                    {
                        interrupted = true; reason = error.Reason;
                        if (iterationAction != null && !KeepPreviousMate(best, iterationBest, true,
                                roots.Count > 0 && iterationScores.TryGetValue(roots[0].Key, out int firstScore) ? firstScore : null))
                        { chosen = iterationAction; best = iterationBest with { Proven = iterationBest.Score > Mate - 1000 && iterationBest.Proven }; bestPv = iterationPv; bestLine = iterationLine; }
                        else if (iterationAction == null && failHigh != null)
                        { chosen = failHigh; bestLine = failHighLine; bestPv = failHighLine.Length; }
                        break;
                    }
                    tries++;
                    if (aspiration && (iterationBest.Score <= low || iterationBest.Score >= high))
                    {
                        aspirations++; width *= 2;
                        if (iterationBest.Score <= low) { low = Math.Max(-Infinity, center - width); failHigh = null; failHighLine = []; }
                        else
                        {
                            high = Math.Min(Infinity, center + width); failHigh = iterationAction; failHighLine = iterationLine;
                            if (failHigh != null) { roots.RemoveAll(action => action.Key == failHigh.Key); roots.Insert(0, failHigh); }
                        }
                        if (tries >= 4 || width > 12_500) { low = -Infinity; high = Infinity; aspiration = false; }
                        continue;
                    }
                    if (comparison == null && !widened && ShouldWidenRoot(iterationBest.Score))
                    {
                        widened = true; var extras = Widen(root, roots, RootWidenCap, restricted);
                        if (extras.Count > 0) { roots.InsertRange(0, extras); foreach (var extra in extras) rootKeys.Add(extra.Key); complete = false; continue; }
                    }
                    Value accepted = iterationBest with { Proven = MateProven(iterationBest.Score, iterationBest.Proven,
                        complete && allProven && iterationScores.Count == roots.Count) };
                    if (iterationAction != null && !KeepPreviousMate(best, accepted, false,
                            iterationScores.GetValueOrDefault(roots[0].Key)))
                    { chosen = iterationAction; best = accepted; bestPv = iterationPv; bestLine = iterationLine; }
                    completed = depth;
                    if (comparison != null && rawTrainingScores.Count == roots.Count)
                    { trainingTurns = rawTrainingScores.ToArray(); trainingDepth = depth; }
                    previousScores = iterationScores;
                    if (collectRootScores && complete && iterationScores.Count == roots.Count)
                    { exactScores = new(iterationScores, StringComparer.Ordinal); scoresDepth = depth; }
                    roots = roots.OrderByDescending(action => action.Key == chosen?.Key)
                        .ThenByDescending(action => iterationScores.GetValueOrDefault(action.Key, -Infinity)).ToList();
                    break;
                }
                if (interrupted) break;
                // Do not stop at a shallow selective mate estimate: deepen until
                // twice the mate distance plus two, as the source does.
                if (Math.Abs(best.Score) > Mate - 1000 && depth >= 2 * (Mate - Math.Abs(best.Score)) + 2)
                { reason = best.Score > 0 && best.Proven ? AlphaBetaStopReason.ProvenWin : AlphaBetaStopReason.SelectiveMateEstimate; break; }
            }
        }
        catch (SearchInterrupted error) { interrupted = true; reason = error.Reason; chosen ??= roots.FirstOrDefault(); }
        catch (HcBudgetExceededException)
        {
            token.ThrowIfCancellationRequested(); interrupted = true;
            reason = milliseconds > 0 && clock.ElapsedMilliseconds >= milliseconds
                ? AlphaBetaStopReason.TimeLimit : GenerationExhausted ? AlphaBetaStopReason.GenerationLimit : AlphaBetaStopReason.NodeLimit;
            chosen ??= roots.FirstOrDefault();
        }
        rootGenerationInterrupted = null;
        if (chosen == null && interrupted && threadId == 0)
        {
            chosen = LegalFallback(root, restricted);
            if (chosen != null) { Add(chosen); complete = false; pruned++; }
        }
        if (chosen != null && completed == 0 && bestLine.Length == 0 && !best.Proven)
        {
            // Even an interrupted search scores its verified fallback with the
            // actual evaluator. Budget exhaustion is not an invented zero target.
            var scored = chosen.State == null ? Replay(root, chosen.Actions)! : chosen;
            int pathStart = path.Count;
            path.AddRange(scored.Actions);
            try { best = AdjustCycle(chosen.Key, new(-Static(scored.State, scored.LastMove, scored.OriginalMin, scored.OriginalMax))); }
            finally { path.RemoveRange(pathStart, path.Count - pathStart); }
        }
        if (chosen != null && completed > 0)
            memory.Resume = new(rootKey, roots.Select(action => Copy(action.Actions)).ToArray(),
                roots.Select(action => previousScores.GetValueOrDefault(action.Key, -Infinity)).ToArray(), completed,
                chosen.Key, best.Score, best.Proven, complete, bestPv) { PrincipalVariation = bestLine };
        return Result(roots, chosen, best, completed, interrupted, reason, exactScores, scoresDepth, searchedFirst, bestPv);
    }

    private static bool KeepPreviousMate(Value old, Value next, bool interrupted, int? firstScore)
    {
        bool refuted = !old.Proven && firstScore is <= Mate - 1000;
        return old.Score > Mate - 1000 && next.Score < old.Score && !refuted ||
            old.Score < -Mate + 1000 && old.Proven && next.Score >= -Mate + 1000 ||
            interrupted && next.Score < -Mate + 1000;
    }
    private Value AdjustCycle(string actionKey, Value value)
    {
        int penalty = cyclePenalties.GetValueOrDefault(actionKey);
        if (penalty == 0 || value.Proven || Math.Abs(value.Score) > Mate - 1000) return value;
        penalizedCycleRoots.Add(actionKey);
        return new(CycleAdjustedScore(value.Score, penalty, value.Proven));
    }
    internal static int CycleAdjustedScore(int score, int penalty, bool proven) =>
        penalty <= 0 || proven || Math.Abs(score) > Mate - 1000 ? score : Math.Max(-Mate + 1000, score - penalty);
    private static bool MateProven(int score, bool bestProven, bool allProven) =>
        score > Mate - 1000 ? bestProven : score < -Mate + 1000 && allProven;

    // Upstream 0ff04c2: optional boards can save a loss or shorten a mate in
    // three or more (five plies). A mate in one/two needs no extra root inventory.
    internal static bool ShouldWidenRoot(int score) =>
        score < -Mate + 1000 || score > Mate - 1000 && score <= Mate - 5;

    private Value Visit(GameState state, int depth, int ply, int alpha, int beta, bool pv,
        Move? last, int minimum, int maximum, bool? knownCheck = null)
    {
        if (depth <= 0) return Quiescence(state, ply, alpha, beta, settings.QuiescenceTurns == 0 ? 0 : QuiescenceDepth,
            last, minimum, maximum, knownCheck);
        VisitNode(); pvLength[ply] = 0; pvLines[ply] = [];
        if (state.kingWasTaken) return Terminal(state, ply);
        if (DrawTerminal(state, ply) is { } draw) return draw;
        if (ply >= MaxPly) return new(Static(state, last, minimum, maximum));
        int initialAlpha = alpha; string key = Key(state, last, minimum, maximum);
        bool excludedNode = excluded?.Position == key;
        CompleteTurnTableEntry? saved = Probe(key);
        if (saved != null && !excludedNode && !pv && saved.Depth >= depth)
        {
            int score = MateFromTable(saved.Score, ply);
            if (saved.Bound == CompleteTurnBound.Exact || saved.Bound == CompleteTurnBound.Lower && score >= beta ||
                saved.Bound == CompleteTurnBound.Upper && score <= alpha) { hits++; return new(score, saved.Proven); }
        }
        bool check = knownCheck ?? InCheck(state);
        // Upstream IIR: without a TT move, reduce quiet nodes from depth four.
        // A later iterative-deepening pass can use the move found here.
        if (saved == null && depth >= 4 && !check) { depth--; internalReductions++; }
        if (!pv && Math.Abs(beta) < Mate - 1000 && !check)
        {
            if (depth <= 2)
            {
                int estimate = Static(state, last, minimum, maximum);
                if (estimate - 250 * depth >= beta) { futility++; return new(estimate); }
            }
            if (settings.UseNullMovePruning && allowSyntheticSearch && depth >= 3 && HasNullMaterial(state) &&
                Static(state, last, minimum, maximum) >= beta)
            {
                var passed = MultiverseEvaluation.Phantom(state);
                // A synthetic pass cannot earn a repetition or quiet-board claim.
                passed.DrawHistory = null;
                if (passed.SubmitMoves())
                {
                    nullProbes++; path.Add(null); Value value;
                    try { value = Visit(passed, depth - 1 - (2 + depth / 4), ply + 1, -beta, -beta + 1, false, null, passed.MinTL, passed.MaxTL); }
                    finally { path.RemoveAt(path.Count - 1); }
                    if (-value.Score >= beta) { nullCutoffs++; return new(-value.Score > Mate - 1000 ? beta : -value.Score); }
                }
            }
        }
        Value best = new(-Infinity); CompleteTurnSearchAction? bestAction = null;
        int count = 0, cap = ActionCap(depth, pv); bool raised = false, allProven = true, exhausted = false, skipped = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (excludedNode) seen.Add(excluded!.Value.Action);
        bool Search(CompleteTurnSearchAction action, bool remembered = false, int extension = 0)
        {
            if (!seen.Add(action.Key)) return false;
            int reduction = !remembered && depth >= 3 && count >= 4 && ActionOrder(state, action, ply) < 10_000 && !action.GivesCheck
                ? Reduction(depth, count, pv) : 0;
            Value value = Child(action, depth + extension, ply, alpha, beta, count == 0, pv, reduction); count++; allProven &= value.Proven;
            if (value.Score > best.Score)
            { best = value; bestAction = action; if (value.Score > alpha) { alpha = value.Score; pvLength[ply] = 1 + pvLength.GetValueOrDefault(ply + 1); pvLines[ply] = [Copy(action.Actions), ..pvLines.GetValueOrDefault(ply + 1, [])]; } }
            if (alpha < beta) return false;
            cutoffs++; OnCutoff(state, action, depth, ply);
            Store(key, depth, ply, best with { Proven = MateProven(best.Score, best.Proven, false) }, CompleteTurnBound.Lower, bestAction);
            return true;
        }
        if (saved != null)
        {
            var replay = Replay(state, saved.Action);
            int extension = 0;
            if (replay != null && depth >= 4 && excluded == null && ply > 0 && saved.Depth >= depth - 3 &&
                saved.Bound is CompleteTurnBound.Exact or CompleteTurnBound.Lower && Math.Abs(MateFromTable(saved.Score, ply)) < Mate - 1000)
            {
                // Test whether alternatives fall below the remembered move by
                // 100 source units per ply. The excluded search cannot read or
                // write a bound for the full node, or certify its checkmate.
                int threshold = MateFromTable(saved.Score, ply) - 100 * depth;
                excluded = (key, replay.Key); singularSearches++;
                Value alternatives;
                try { alternatives = Visit(state, (depth - 1) / 2, ply, threshold - 1, threshold, false, last, minimum, maximum, check); }
                finally { excluded = null; pvLength[ply] = 0; pvLines[ply] = []; }
                if (alternatives.Score < threshold) { extension = 1; singularExtensions++; }
            }
            if (replay != null && Search(replay, true, extension)) return best with { Proven = MateProven(best.Score, best.Proven, false) };
        }
        var killers = memory.Killers.GetValueOrDefault(ply);
        foreach (var moves in new[] { killers.First, killers.Second })
        {
            if (moves == null) continue; var replay = Replay(state, moves);
            if (replay != null && Search(replay, true)) return best with { Proven = MateProven(best.Score, best.Proven, false) };
        }
        var generator = Generator(state);
        if (ply <= 2)
        {
            CompleteTurnSearchAction? matingAction = null;
            var checks = generator.GetStandaloneCheckingTravelCandidates(visitCandidate: action =>
            {
                if (seen.Contains(action.Key)) return false;
                CheckBudget(); var mate = ProbeMate(action, ply + 1);
                if (!mate.Proven || mate.Score >= 0) return false;
                matingAction = action; return true;
            });
            if (matingAction != null)
            {
                var win = new Value(Mate - ply - 1, true); pvLength[ply] = 1; pvLines[ply] = [Copy(matingAction.Actions)];
                Store(key, depth, ply, win, CompleteTurnBound.Exact, matingAction); return win;
            }
            if (ply == 1)
                foreach (var action in checks)
                    if (Search(action, true)) return best with { Proven = MateProven(best.Score, best.Proven, false) };
        }
        foreach (var action in generator.GetTravelCaptureCandidates(cap, 16))
        { CheckBudget(); if (Search(action, true)) return best with { Proven = MateProven(best.Score, best.Proven, false) }; }
        while (true)
        {
            // 7ebac0b keeps checking actions past LMP. Use the verified full-turn
            // check flag: the source's action_score threshold omits quiet checks
            // (CHECK_BONUS belongs to coordinate scoring, not action_score).
            CheckBudget(); var batch = generator.NextBatch();
            foreach (var action in batch.OrderByDescending(action => action.GivesCheck)
                         .ThenByDescending(action => ActionOrder(state, action, ply)))
            {
                if (seen.Contains(action.Key)) continue;
                if (count >= cap && !action.GivesCheck)
                {
                    if (!raised && best.Score < -Mate + 1000) { cap *= 2; raised = true; }
                    else { skipped = true; pruned++; break; }
                }
                if (Search(action)) return best with { Proven = MateProven(best.Score, best.Proven, false) };
            }
            if (skipped) break;
            if (generator.Exhausted)
            {
                if (generator.GeneratedCount == 0 && generator.RestrictedOptionalBoards) { generator.RecoverOptionalMoves(); continue; }
                exhausted = true; break;
            }
        }
        bool optionalComplete = !generator.Incomplete && !generator.RestrictedOptionalBoards && generator.WastedMovesPruned == 0;
        if (best.Score < -Mate + 1000 && generator.RestrictedOptionalBoards)
        {
            generator.RestartOptionalOnly(); int widened = 0;
            while (widened < cap && !generator.Exhausted)
            {
                CheckBudget(); var batch = generator.NextBatch(Math.Min(12, cap - widened));
                foreach (var action in batch.OrderByDescending(action => action.Travel))
                {
                    if (!seen.Contains(action.Key)) widened++;
                    if (Search(action, true)) return best with { Proven = MateProven(best.Score, best.Proven, false) };
                    if (best.Score >= -Mate + 1000) break;
                }
                if (best.Score >= -Mate + 1000) break;
            }
            optionalComplete = generator.Exhausted && !generator.Incomplete && generator.WastedMovesPruned == 0;
        }
        if (!optionalComplete) pruned++;
        if (count == 0) return !excludedNode && exhausted && optionalComplete ? NoActions(state, ply, last, minimum, maximum) : new(Static(state, last, minimum, maximum));
        best = best with { Proven = MateProven(best.Score, best.Proven, !excludedNode && allProven && exhausted && !skipped && optionalComplete) };
        var bound = best.Score > initialAlpha ? CompleteTurnBound.Exact : CompleteTurnBound.Upper;
        // An unsearched action can invalidate a fail-low upper bound.
        if (bound != CompleteTurnBound.Upper || exhausted && !skipped && optionalComplete)
            Store(key, depth, ply, best, bound, bestAction);
        return best;
    }

    private Value Child(CompleteTurnSearchAction action, int depth, int ply, int alpha, int beta,
        bool first, bool pv, int reduction = 0)
    {
        if (action.State == null)
        {
            if (ply != 0 || rootForPv == null)
                throw new InvalidOperationException("Only root actions may omit their materialized successor.");
            action = Replay(rootForPv, action.Actions) ??
                throw new InvalidOperationException("A remembered complete root turn no longer replays legally.");
        }
        int previous = path.Count; path.AddRange(action.Actions);
        try
        {
            Value Search(int low, int high, int reducedDepth, bool childPv)
            {
                Value value = Visit(action.State, depth - 1 - reducedDepth, ply + 1, -high, -low, childPv,
                    action.LastMove, action.OriginalMin, action.OriginalMax, action.GivesCheck);
                return new(-value.Score, value.Proven);
            }
            if (first || !settings.UsePrincipalVariationSearch) return Search(alpha, beta, 0, pv);
            Value result;
            if (reduction > 0)
            {
                reduced++; if (action.Travel) reducedTravels++;
                result = Search(alpha, alpha + 1, reduction, false);
                if (result.Score <= alpha) return result with { Proven = false };
                researches++;
            }
            result = Search(alpha, alpha + 1, 0, false);
            if (result.Score > alpha && (result.Score < beta || ply == 0) && pv)
            { researches++; result = Search(alpha, beta, 0, true); }
            return result;
        }
        finally { path.RemoveRange(previous, path.Count - previous); }
    }

    private Value Quiescence(GameState state, int ply, int alpha, int beta, int remaining,
        Move? last, int minimum, int maximum, bool? knownCheck = null)
    {
        VisitNode(); pvLength[ply] = 0; pvLines[ply] = [];
        if (state.kingWasTaken) return Terminal(state, ply);
        if (DrawTerminal(state, ply) is { } draw) return draw;
        // Fresh and replayed complete actions already checked this exact child.
        // Reuse that result instead of cloning and scanning its history again.
        bool check = knownCheck ?? InCheck(state);
        if (!check)
        {
            int stand = Static(state, last, minimum, maximum);
            if (stand >= beta) return new(stand);
            alpha = Math.Max(alpha, stand);
            if (remaining <= 0 || ply >= MaxPly) return new(alpha);
            int gain = 0; bool presentColor = state.CalcPresent();
            foreach (var line in state.Multiverse)
            {
                token.ThrowIfCancellationRequested();
                if (state.LayerIsActive(line.Layer) && line.TEnd == state.Present && line.ColorPlayable == presentColor)
                    gain += LocalCaptureSearch.CaptureGain(line.GetPlayableBoard(), presentColor, token);
            }
            return new(stand + gain);
        }
        if (remaining <= 0 || ply >= MaxPly) return new(Static(state, last, minimum, maximum));
        var generator = Generator(state, restrictedOptional: false);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int count = 0; Value best = new(-Infinity); bool allProven = true;
        bool Search(CompleteTurnSearchAction action)
        {
            if (!seen.Add(action.Key)) return false;
            int before = path.Count; path.AddRange(action.Actions); Value value;
            try { var child = Quiescence(action.State, ply + 1, -beta, -alpha, remaining - 1,
                action.LastMove, action.OriginalMin, action.OriginalMax, action.GivesCheck); value = new(-child.Score, child.Proven); }
            finally { path.RemoveRange(before, path.Count - before); }
            count++; allProven &= value.Proven;
            if (value.Score > best.Score) best = value;
            if (value.Score > alpha)
            {
                alpha = value.Score; pvLength[ply] = 1 + pvLength.GetValueOrDefault(ply + 1); pvLines[ply] = [Copy(action.Actions), ..pvLines.GetValueOrDefault(ply + 1, [])];
                if (alpha >= beta) { cutoffs++; memory.EvasionKillers[ply] = Copy(action.Actions); return true; }
            }
            return false;
        }
        if (memory.EvasionKillers.TryGetValue(ply, out var remembered))
        { var action = Replay(state, remembered); if (action != null && Search(action)) return best with { Proven = MateProven(best.Score, best.Proven, false) }; }
        while (count < QuiescenceCap && !generator.Exhausted)
        {
            CheckBudget(); foreach (var action in generator.NextBatch(Math.Min(12, QuiescenceCap - count)))
                if (Search(action)) return best with { Proven = MateProven(best.Score, best.Proven, false) };
        }
        if (count == 0) return generator.Exhausted && !generator.Incomplete ? NoActions(state, ply, last, minimum, maximum) : new(Static(state, last, minimum, maximum));
        bool exhausted = generator.Exhausted && !generator.Incomplete && count < QuiescenceCap && generator.WastedMovesPruned == 0;
        if (!exhausted) pruned++;
        return best with { Proven = MateProven(best.Score, best.Proven, allProven && exhausted) };
    }

    private Value ProbeMate(CompleteTurnSearchAction action, int ply)
    {
        if (!action.GivesCheck) return new(0);
        CheckBudget();
        // This bound applies only to speculative mate-in-one search. Actual
        // legality/adjudication remains the separate unlimited proof path.
        var evasion = ProbeEvasions(action.State, 64, long.MaxValue);
        token.ThrowIfCancellationRequested();
        return evasion.Status == HcSearchStatus.Exhausted ? new(-Mate + ply, true) : new(0);
    }
    private Value ProbeFutureMate(CompleteTurnSearchAction action, int ply, long deadline)
    {
        if (!action.GivesCheck) return new(0);
        CheckBudget();
        // The optional-board root probe has the shared short time allowance,
        // but no 64-cube cap. That cap belongs only to standalone travel probes.
        var evasion = ProbeEvasions(action.State, 0, deadline);
        token.ThrowIfCancellationRequested();
        return evasion.Status == HcSearchStatus.Exhausted ? new(-Mate + ply, true) : new(0);
    }

    private HcSearchResult<Move> ProbeEvasions(GameState state, int maximumCubes, long deadline)
    {
        HcBudget? budget = null;
        long observed = 0;
        void ObserveWork()
        {
            long work = budget!.Work;
            SpendGeneration(work - observed); observed = work;
        }
        budget = new(0, token, interrupted: () =>
        {
            ObserveWork();
            return GenerationInterrupted() || rootGenerationInterrupted?.Invoke() == true ||
                clock.ElapsedMilliseconds >= deadline;
        });
        try
        {
            var adapter = new HypercuboidGameAdapter(state.Clone(), budget);
            return maximumCubes > 0 ? adapter.SearchBoundedCubes(maximumCubes) : adapter.Search();
        }
        catch (HcBudgetExceededException)
        {
            token.ThrowIfCancellationRequested();
            return new(HcSearchStatus.Unknown, null, budget.Work);
        }
        finally
        {
            // The final successful operation has no following callback. Count
            // it on every return, including fast witnesses and interrupted
            // inventory construction, in the same shared search allowance.
            ObserveWork();
        }
    }
    private List<CompleteTurnSearchAction> Widen(GameState state, IEnumerable<CompleteTurnSearchAction> old,
        int cap, IReadOnlyList<Move>? restricted)
    {
        var seen = old.Select(action => action.Key).ToHashSet(StringComparer.Ordinal);
        var generator = Generator(state, restrictedRoot: restricted); generator.RestartOptionalOnly();
        var result = new List<CompleteTurnSearchAction>();
        while (result.Count < cap && !generator.Exhausted)
        {
            CheckBudget(); var batch = generator.NextBatch(Math.Min(12, cap - result.Count));
            foreach (var action in batch.OrderByDescending(action => action.Travel))
                if (seen.Add(action.Key))
                {
                    cyclePenalties[action.Key] = cycleHistory?.Penalty(action.State) ?? 0;
                    result.Add(action with { State = null! });
                }
        }
        if (!generator.Exhausted) pruned++;
        return result;
    }

    private CompleteTurnSearchAction? LegalFallback(GameState root, IReadOnlyList<Move>? restricted)
    {
        // A heuristic ordering may traverse billions of rejected combinations
        // before its first move, while the unscored proof cursor finds a witness
        // immediately. This is an unlimited legal-turn proof, never a move cap
        // or a negative result inferred from the selective search allowance.
        CompleteTurnSearchAction? Complete(GameState position, Move? first)
        {
            var proof = CheckAndMate.FindSafeCompletion(position, new() { CancellationToken = token });
            token.ThrowIfCancellationRequested();
            if (proof.Status == SafeCompletionStatus.Exhausted) return null;
            if (proof.Status != SafeCompletionStatus.Found)
                throw new InvalidOperationException($"The unlimited legal-turn fallback was inconclusive: {proof.Reason}");
            Move?[] moves = first == null ? [..proof.Moves!, null] : [first, ..proof.Moves!, null];
            CompleteTurnSearchAction? action = ReplayCore(root, moves, trimWasted: true);
            if (restricted != null && (action == null || !restricted.Any(move =>
                CompleteTurnCandidates.ActionKey(move) == CompleteTurnCandidates.ActionKey(action.FirstAction))))
                // A caller may explicitly require the otherwise wasted first
                // move. Its proved legal completion remains a valid fallback.
                action = ReplayCore(root, moves, trimWasted: false, retainWasted: true);
            return action ?? throw new InvalidOperationException("A proved legal complete turn failed its independent replay.");
        }
        if (restricted == null) return Complete(root, null);
        foreach (Move move in restricted)
        {
            token.ThrowIfCancellationRequested();
            var next = root.Clone();
            if (!next.MakeMove(move.Clone(), true) || !RoyalLegality.IsLegalAfterMove(root, move, next)) continue;
            if (Complete(next, move) is { } action) return action;
        }
        return null;
    }
    private CompleteTurnSearchAction? Replay(GameState root, Move?[] moves) => ReplayCore(root, moves, trimWasted: false);

    private CompleteTurnSearchAction? ReplayCore(GameState root, Move?[] moves, bool trimWasted, bool retainWasted = false)
    {
        var state = root.Clone(); Move? last = null; int minimum = root.MinTL, maximum = root.MaxTL;
        bool capture = false, promotion = false, travel = false, optional = false; int branches = 0;
        bool submitted = false; var replayed = new List<Move>(); var branching = new HashSet<int>();
        foreach (var move in moves)
        {
            token.ThrowIfCancellationRequested();
            if (submitted || state.kingWasTaken) return null;
            if (move == null)
            {
                int wasted = CompleteTurnSearchGenerator.FindWastedMove(state, replayed, branching);
                if (wasted >= 0 && !retainWasted)
                    return trimWasted ? ReplayCore(root, moves.Where((_, index) => index != wasted).ToArray(), true) ??
                        ReplayCore(root, moves, trimWasted: false, retainWasted: true) : null;
                if (!RoyalLegality.CanSubmit(state) || !state.SubmitMoves()) return null;
                submitted = true; continue;
            }
            int mover = state.GetSquare(move.Origin); if (mover == 0 || mover == Board.ERRORSQUARE) return null;
            int victim = SearchHeuristics.MoverVictim(state, move).Victim;
            capture |= victim != 0; promotion |= PositionHeuristics.Promotes(state, move);
            travel |= move.Origin.L != move.Dest.L || move.Origin.T != move.Dest.T;
            optional |= !state.LayerIsActive(move.Origin.L) || state.GetTimeline(move.Origin.L)?.TEnd != state.Present;
            minimum = state.MinTL; maximum = state.MaxTL;
            int timelines = state.Multiverse.Count;
            bool playerWhite = state.Color;
            var copy = move.Clone();
            if (!RoyalLegality.IsLegalBeforeMove(state, copy) || !state.MakeMove(copy, true) ||
                !RoyalLegality.IsSafeAfterMove(state, playerWhite)) return null;
            if (state.Multiverse.Count > timelines) branching.Add(replayed.Count);
            replayed.Add(copy); branches += state.Multiverse.Count - timelines;
            last = copy;
        }
        if (!submitted && !state.kingWasTaken) return null;
        // Coordinate scores only order fresh generator batches. Remembered
        // actions use ActionOrder; redoing their exchange/check scores is wasted.
        return new(Copy(moves), state, 0, branches, capture, promotion, travel,
            !state.kingWasTaken && InCheck(state), optional, last, minimum, maximum) { VerifiedFallback = retainWasted };
    }
    private int ActionOrder(GameState state, CompleteTurnSearchAction action, int ply)
    {
        var killers = memory.Killers.GetValueOrDefault(ply);
        int bonus = killers.First != null && ActionKey(killers.First) == action.Key ? 8000 :
            killers.Second != null && ActionKey(killers.Second) == action.Key ? 7000 : 0;
        return SearchHeuristics.ActionScore(state, action.Actions, memory.History, bonus);
    }
    private bool InCheck(GameState state)
    {
        token.ThrowIfCancellationRequested(); string key = CompleteTurnStateHash.Compute(state);
        lock (memory.Cache.Gate) if (memory.Cache.Checks.TryGetValue(key, out bool cached)) return cached;
        bool check = CheckAndMate.IsInCheck(state);
        lock (memory.Cache.Gate)
        {
            if (memory.Cache.Checks.Count >= Math.Max(1, settings.TranspositionEntries)) memory.Cache.Checks.Clear();
            memory.Cache.Checks[key] = check;
        }
        return check;
    }
    private void OnCutoff(GameState source, CompleteTurnSearchAction action, int depth, int ply)
    {
        // Checks themselves are quiet for history/killers; only captures and
        // promotions are tactical in the source's cutoff update.
        if (action.Capture || action.Promotion) return;
        foreach (Move move in action.Actions.OfType<Move>())
        {
            int index = SearchHeuristics.HistoryIndex(source.GetSquare(move.Origin), move);
            memory.History[index] += depth * depth;
            if (memory.History[index] > 20_000)
                for (int i = 0; i < memory.History.Length; i++) memory.History[i] /= 2;
        }
        var previous = memory.Killers.GetValueOrDefault(ply);
        if (previous.First == null || ActionKey(previous.First) != action.Key)
            memory.Killers[ply] = (Copy(action.Actions), previous.First);
    }
    private int Static(GameState state, Move? last, int minimum, int maximum)
    {
        token.ThrowIfCancellationRequested(); leaves++; string key = Key(state, last, minimum, maximum);
        lock (memory.Cache.Gate) if (memory.Cache.Static.TryGetValue(key, out int cached)) return cached;
        double value = evaluate?.Invoke(new(state, last, minimum, maximum, path)) ?? new PositionHeuristics().Evaluate(state, rootWhite);
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(value)) throw new InvalidOperationException("Complete-turn search received a nonfinite evaluation.");
        double raw = evaluatorUnits ? Math.Atanh(Math.Clamp(value, -.999999, .999999)) * EvaluationEncoding.ScoreScale : value * 10_000;
        int score = (int)Math.Round(Math.Clamp(raw, -EvaluationMaximum, EvaluationMaximum), MidpointRounding.AwayFromZero) * (state.Color == rootWhite ? 1 : -1);
        lock (memory.Cache.Gate)
        {
            if (memory.Cache.Static.Count >= Math.Max(1, settings.TranspositionEntries)) memory.Cache.Static.Clear();
            memory.Cache.Static[key] = score;
        }
        return score;
    }
    private CompleteTurnTableEntry? Probe(string key)
    { if (settings.TranspositionEntries == 0) return null; lock (memory.Cache.Gate) return memory.Cache.Table.GetValueOrDefault(key); }
    private void Store(string key, int depth, int ply, Value value, CompleteTurnBound bound, CompleteTurnSearchAction? action)
    {
        if (settings.TranspositionEntries == 0 || action == null || excluded?.Position == key) return;
        lock (memory.Cache.Gate)
        {
            if (memory.Cache.Table.TryGetValue(key, out var old) && depth < old.Depth - 1 && bound != CompleteTurnBound.Exact) return;
            if (memory.Cache.Table.Count >= settings.TranspositionEntries && !memory.Cache.Table.ContainsKey(key)) memory.Cache.Table.Clear();
            memory.Cache.Table[key] = new(depth, MateToTable(value.Score, ply), value.Proven, bound, Copy(action.Actions));
        }
    }
    private Value NoActions(GameState state, int ply, Move? last, int minimum, int maximum)
    {
        CheckBudget();
        if (state.TurnMoves.Count != 0)
        {
            var continuation = CheckAndMate.FindSafeCompletion(state, new() { CancellationToken = token }); token.ThrowIfCancellationRequested();
            return continuation.Status == SafeCompletionStatus.Exhausted ? new(-EvaluationMaximum) : new(Static(state, last, minimum, maximum));
        }
        var proof = CheckAndMate.Analyze(state, new() { CancellationToken = token }); token.ThrowIfCancellationRequested();
        return proof.Status switch
        {
            CheckAndMateStatus.Checkmate or CheckAndMateStatus.RoyalCapture => new(proof.WinnerWhite == state.Color ? Mate - ply : -Mate + ply, true),
            CheckAndMateStatus.Stalemate or CheckAndMateStatus.Draw => new(0, true), _ => new(Static(state, last, minimum, maximum))
        };
    }
    private static Value Terminal(GameState state, int ply) => new(state.kingWasTakenByColor == state.Color ? Mate - ply : -Mate + ply, true);
    private static bool HasNullMaterial(GameState state)
    {
        bool presentColor = state.CalcPresent();
        foreach (var line in state.Multiverse)
        {
            if (!state.LayerIsActive(line.Layer)) continue;
            int count = 0;
            foreach (int piece in line.GetPlayableBoard().Brd)
                if (piece != 0 && Board.GetColorBool(piece) == presentColor && SearchHeuristics.PieceKind(piece) is not ('P' or 'W' or 'K' or 'Y') && ++count >= 2) return true;
        }
        return false;
    }
    private int PublicScore(int source, bool proven)
    {
        if (Math.Abs(source) > Mate - 1000)
            return proven ? Math.Sign(source) * AlphaBetaSearch.Mate : Math.Sign(source) * (AlphaBetaSearch.EvaluationScale - 1);
        double bounded = evaluatorUnits ? Math.Tanh(source / EvaluationEncoding.ScoreScale) : source / 10_000.0;
        return (int)Math.Round(Math.Clamp(bounded, -.99999, .99999) * AlphaBetaSearch.EvaluationScale);
    }

    private Value? DrawTerminal(GameState state, int ply)
    {
        if (DrawRules.ClaimableReason(state) == null) return null;
        var proof = CheckAndMate.Analyze(state, new() { CancellationToken = token });
        return proof.Status switch
        {
            CheckAndMateStatus.Draw or CheckAndMateStatus.Stalemate => new Value(0, true),
            CheckAndMateStatus.Checkmate or CheckAndMateStatus.RoyalCapture =>
                new Value((proof.WinnerWhite == state.Color ? 1 : -1) * (Mate - ply), true),
            _ => null
        };
    }
    private int VerifiedPvLength(CompleteTurnSearchAction? chosen, int score, int depth, bool extendFromTable = true)
    {
        if (chosen == null || rootForPv == null) return 0;
        var state = rootForPv; var seen = new HashSet<string>(StringComparer.Ordinal)
            { CompleteTurnStateHash.Compute(state) };
        int length = 0, target = Math.Abs(score) > Mate - 1000 ? Math.Max(0, Mate - Math.Abs(score)) : Math.Max(0, depth);
        Move?[][] line = bestLine.Length > 0 && ActionKey(bestLine[0]) == chosen.Key ? bestLine : [chosen.Actions];
        CompleteTurnSearchAction? applied = null;
        foreach (Move?[] moves in line)
        {
            applied = length == 0 && chosen.VerifiedFallback ? chosen : Replay(state, moves);
            if (applied == null) return length;
            state = applied.State; length++; seen.Add(CompleteTurnStateHash.Compute(state));
        }
        while (extendFromTable && length < target && applied != null)
        {
            var saved = Probe(Key(state, applied.LastMove, applied.OriginalMin, applied.OriginalMax));
            if (saved == null || saved.Action.Length == 0) break;
            applied = Replay(state, saved.Action); if (applied == null) break;
            state = applied.State;
            if (!seen.Add(CompleteTurnStateHash.Compute(state))) break;
            length++;
        }
        return length;
    }
    private AlphaBetaResult Result(IReadOnlyList<CompleteTurnSearchAction> roots, CompleteTurnSearchAction? chosen,
        Value best, int depth, bool interrupted, AlphaBetaStopReason reason, IReadOnlyDictionary<string, int> exactScores,
        int scoreDepth, IReadOnlyCollection<string> searched, int pv)
    {
        var groups = roots.GroupBy(action => CompleteTurnCandidates.ActionKey(action.FirstAction))
            .OrderBy(group => firstActionOrder.GetValueOrDefault(group.Key, int.MaxValue)).ToArray();
        Move?[] firsts = groups.Select(group => group.First().FirstAction?.Clone()).ToArray();
        int selected = chosen == null ? -1 : Array.FindIndex(firsts, first => CompleteTurnCandidates.ActionKey(first) == CompleteTurnCandidates.ActionKey(chosen.FirstAction));
        Move?[][] alternatives = groups.Select(group => Copy(group.OrderByDescending(action => action.Key == chosen?.Key)
            .ThenByDescending(action => exactScores.GetValueOrDefault(action.Key, -Infinity)).First().Actions)).ToArray();
        bool allScored = scoreDepth > 0 && roots.All(action => exactScores.ContainsKey(action.Key));
        int[] scores = allScored ? groups.Select(group => PublicScore(group.Max(action => exactScores[action.Key]), false)).ToArray() : [];
        bool mateProven = best.Proven && Math.Abs(best.Score) > Mate - 1000;
        if (reason == AlphaBetaStopReason.CompletedDepth && mateProven && best.Score > 0) reason = AlphaBetaStopReason.ProvenWin;
        if (!mateProven && Math.Abs(best.Score) > Mate - 1000 && !interrupted) reason = AlphaBetaStopReason.SelectiveMateEstimate;
        return new(firsts, selected, PublicScore(best.Score, mateProven), depth, nodes, leaves, hits, cutoffs, researches,
            interrupted, clock.Elapsed.TotalSeconds)
        {
            ReducedMoves = reduced + internalReductions, PrunedMoves = pruned, FutilityPrunes = futility, NullMoveProbes = nullProbes,
            NullMoveCutoffs = nullCutoffs, AspirationResearches = aspirations, RootScores = scores,
            RootScoresDepth = allScored ? scoreDepth : 0, RootActionsSearched = searched.Count,
            StopReason = reason, GenerationWork = generationWork,
            RootCycleRepetitions = cycleHistory?.RootRepetitions ?? 0, CyclePenalizedRoots = penalizedCycleRoots.Count,
            CompleteTurnMoves = chosen == null ? [] : Copy(chosen.Actions), CompleteTurnChoices = alternatives,
            TrainingTurns = trainingTurns, TrainingDepth = trainingDepth,
            ProvenMateDistance = mateProven ? Math.Sign(best.Score) * (Mate - Math.Abs(best.Score)) : null,
            PrincipalVariationLength = VerifiedPvLength(chosen, best.Score, depth, extendFromTable: false), SourceScore = best.Score
        };
    }
}
