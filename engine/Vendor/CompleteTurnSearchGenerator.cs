namespace SkynetZero;

// Search-space ordering and selective generation adapted from FirePlank's
// 5d-chess-engine, core/hypercuboid.rs and engine/ab.rs, revision
// 57e97a550f731e163cc9156d269e1aa5c6fe3874, with optional-travel probes from
// f444a5ea3ba15a4c67e517ab67654d59200b32eb (GPL-3.0-only; THIRD_PARTY_NOTICES.md).
// This is a search cursor. Exhausting a restricted or wasted-move-pruned space
// is not evidence of checkmate; CheckAndMate retains its exhaustive proof path.
internal sealed record CompleteTurnSearchAction(Move?[] Actions, GameState State,
    double OrderingScore, int Branches, bool Capture, bool Promotion, bool Travel,
    bool GivesCheck, bool OptionalBoardMove, Move? LastMove, int OriginalMin, int OriginalMax)
{
    // Exact fallback witnesses can retain an explicitly requested move that
    // ordinary selective generation would prune. Keep their verified state.
    internal bool VerifiedFallback { get; init; }
    internal Move? FirstAction => Actions[0];
    internal bool Quiet => !Capture && !Promotion && !GivesCheck;
    internal string Key { get; } = string.Join("|", Actions.Select(CompleteTurnCandidates.ActionKey));
}

internal sealed class CompleteTurnSearchGenerator
{
    [ThreadStatic] internal static bool? WastedSliceOptimizationOverride;
    private sealed record Space(HcCube<Move> Cube, int Class);
    private sealed class LoneWalk(HcCube<Move> cube, int layer, int precedence)
    {
        internal readonly HcCube<Move> Cube = cube;
        internal readonly int Layer = layer;
        internal readonly int Class = precedence;
        internal KeyValuePair<int, HcLocation<Move>>[]? Choices;
        internal int Position;
    }
    private sealed class Cursor
    {
        internal readonly PriorityQueue<Space, (int Class, double NegativeBound, long Sequence)> Heap = new();
        internal long Sequence;
        internal LoneWalk? Lone;
    }

    private readonly GameState root;
    private readonly HcBudget budget;
    private readonly CancellationToken cancellation;
    private readonly HypercuboidGameAdapter adapter;
    private readonly HcModel<Move> model;
    private readonly Func<Move, GameState, double>? scoreMove;
    private readonly CandidateSimulation scoring;
    private readonly Dictionary<string, double> scores = new(StringComparer.Ordinal);
    private readonly Dictionary<HcLocation<Move>, double> coordinateScores = new(ReferenceEqualityComparer.Instance);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Dictionary<int, HcLocation<Move>>,
        Dictionary<int, HcLocation<Move>>> orderedAxes = new();
    private readonly Dictionary<string, bool> checks = new(StringComparer.Ordinal);
    private readonly HashSet<string> emitted = new(StringComparer.Ordinal);
    private readonly HashSet<int> optional;
    private readonly HashSet<string>? restrictedFirst;
    private readonly Dictionary<string, Move>? requestedFirst;
    private readonly HcSearch<Move>.PointWorkspace matching = new();
    private readonly Cursor cursor = new();
    private readonly IReadOnlyList<HcCube<Move>> allCubes;
    private int batchSize = 1;
    private bool recovered;
    private bool pendingQuietTravels;
    private Func<bool>? probeInterrupted;

    // A budget can fire after a cube was popped but before its remainder was
    // restored. An empty cursor then says nothing about legal exhaustion.
    internal bool Exhausted => !Interrupted && !pendingQuietTravels && cursor.Heap.Count == 0 && cursor.Lone == null;
    internal bool Interrupted { get; private set; }
    internal bool Incomplete { get; private set; }
    internal bool RestrictedOptionalBoards { get; private set; }
    internal int GeneratedCount { get; private set; }
    internal int LastBranchClass { get; private set; }
    internal int WastedMovesPruned { get; private set; }
    internal int SingleFrontierSteps { get; private set; }
    internal long Work => budget.Work;

    internal CompleteTurnSearchGenerator(GameState root,
        Func<Move, GameState, double>? scoreMove = null, CancellationToken cancellation = default,
        Func<bool>? interrupted = null, bool restrictOptionalBoards = true,
        IReadOnlyList<Move>? restrictedRoot = null)
    {
        this.root = root.Clone();
        this.root.TLHandicap = root.TLHandicap;
        this.root.DetermineActiveTLS();
        this.root.CalcPresent();
        this.scoreMove = scoreMove;
        this.cancellation = cancellation;
        budget = new(0, cancellation, interrupted: () => interrupted?.Invoke() == true || probeInterrupted?.Invoke() == true);
        adapter = new(this.root, budget);
        scoring = new(this.root);
        requestedFirst = restrictedRoot?.GroupBy(RequestKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Clone(), StringComparer.Ordinal);
        restrictedFirst = requestedFirst?.Keys.ToHashSet(StringComparer.Ordinal);
        int present = HypercuboidGameAdapter.HalfTime(this.root.Present, this.root.CalcPresent());
        optional = this.root.Multiverse.Where(line => line.ColorPlayable == this.root.Color &&
                (!this.root.LayerIsActive(line.Layer) ||
                 HypercuboidGameAdapter.HalfTime(line.TEnd, line.ColorPlayable) != present))
            .Select(line => line.Layer).ToHashSet();

        // The proof adapter's fast witness cannot stand in for a search space:
        // search needs alternate complete turns even when immediate submission is legal.
        model = adapter.BuildSearchModel();
        allCubes = restrictedFirst == null ? model.Cubes : RestrictFirst(model.Cubes);
        RestrictedOptionalBoards = restrictOptionalBoards && allCubes.Any(cube =>
            optional.Any(layer => cube.TryGetValue(layer, out var axis) && axis.Keys.Any(index => index != 0)));
        recovered = !RestrictedOptionalBoards;
        pendingQuietTravels = RestrictedOptionalBoards;
        AddInitial(cursor, RestrictedOptionalBoards ? WithoutOptional(allCubes) : allCubes);
    }

    // Progressive batches keep the first useful complete turn cheap, then permit
    // broader action-level ordering once the search has earned that work.
    internal CompleteTurnSearchAction[] NextBatch(int maximum = 12)
    {
        if (maximum < 1) return [];
        if (Interrupted) { cancellation.ThrowIfCancellationRequested(); throw new HcBudgetExceededException(); }
        int wanted = Math.Min(maximum, batchSize);
        batchSize = Math.Min(12, 2 * batchSize);
        var result = new List<CompleteTurnSearchAction>(wanted);
        try
        {
            while (result.Count < wanted && NextMain() is { } candidate)
                if (emitted.Add(candidate.Key))
                {
                    GeneratedCount++;
                    result.Add(candidate);
                }
        }
        catch (HcBudgetExceededException)
        {
            Interrupted = Incomplete = true;
            cancellation.ThrowIfCancellationRequested();
            // Keep already verified turns from this batch. The next request
            // observes interruption; this prefix must not disappear with it.
            if (result.Count == 0) throw;
        }
        // Coordinate sums order combinations; action bonuses supplied by the
        // search may further order this small batch without materializing turns.
        return result.OrderByDescending(candidate => candidate.OrderingScore).ToArray();
    }

    private CompleteTurnSearchAction? NextMain()
    {
        var candidate = Next(cursor);
        if (candidate != null || !pendingQuietTravels) return candidate;
        // Their source class is last; most nodes cut off before reaching it.
        // Defer constructing its products instead of adding per-node overhead.
        pendingQuietTravels = false;
        AddInitial(cursor, QuietOptionalTravelSpace());
        return Next(cursor);
    }

    // The optional-only complement is partitioned by the first optional axis
    // played. It is disjoint from the restricted initial inventory and from its
    // other partitions, so widening never restarts an enormous prefix.
    internal bool RecoverOptionalMoves(bool onlyOptional = false)
    {
        if (onlyOptional) return RestartOptionalOnly();
        if (recovered) return false;
        recovered = true;
        pendingQuietTravels = false;
        RestrictedOptionalBoards = false;
        Interrupted = false;
        AddInitial(cursor, OptionalOnly(allCubes));
        return true;
    }

    internal bool RestartOptionalOnly()
    {
        cursor.Heap.Clear();
        cursor.Lone = null;
        Interrupted = false;
        recovered = true;
        pendingQuietTravels = false;
        RestrictedOptionalBoards = false;
        batchSize = 1;
        AddInitial(cursor, OptionalOnly(allCubes));
        return !Exhausted;
    }

    internal void RestartFull()
    {
        cursor.Heap.Clear();
        cursor.Lone = null;
        Interrupted = false;
        recovered = true;
        pendingQuietTravels = false;
        RestrictedOptionalBoards = false;
        batchSize = 1;
        AddInitial(cursor, allCubes);
    }

    internal CompleteTurnSearchAction[] GetStandaloneCheckingTravelCandidates(int limit = int.MaxValue,
        Func<CompleteTurnSearchAction, bool>? visitCandidate = null)
    {
        if (limit <= 0) return [];
        var result = new List<CompleteTurnSearchAction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var travel in Travels())
        {
            budget.Spend();
            int from = travel.Location.Move!.Source.Timeline;
            if (!IsBranch(travel.Layer) && !optional.Contains(from) && !optional.Contains(travel.Layer)) continue;
            // A standalone travel has a single predetermined matching: its
            // departure, its arrival, and passes everywhere else. Find a raw
            // compatible cube before allocating anything. Building every
            // optional-board-restricted cube for every travel makes this small
            // tactical probe allocate gigabytes on a many-timeline position.
            HcCube<Move>? compatible = null;
            foreach (HcCube<Move> cube in allCubes)
            {
                budget.Spend();
                if (!cube.TryGetValue(from, out var departures) || !departures.ContainsKey(travel.Location.DepartureIndex) ||
                    !cube.TryGetValue(travel.Layer, out var arrivals) || !arrivals.ContainsKey(travel.Index)) continue;
                if (cube.Any(pair => pair.Key != from && pair.Key != travel.Layer && !pair.Value.ContainsKey(0))) continue;
                compatible = cube;
                break;
            }
            if (compatible == null || !MoveGivesCheck(travel.Location.Move.Move)) continue;
            var point = new HcPoint<Move>(compatible.Count);
            foreach (var (layer, axis) in compatible)
            {
                int index = layer == from ? travel.Location.DepartureIndex : layer == travel.Layer ? travel.Index : 0;
                point[layer] = new(index, axis[index]);
            }
            if (HcSearch<Move>.JumpOrderConsistent(point, model.Whole, model.NewTimeline, budget) != null ||
                adapter.ValidateSearchPoint(point, out GameState after) != null) continue;
            var candidate = MakeAction(HcSearch<Move>.ToAction(point, model.NewTimeline), after, point);
            if (candidate is { GivesCheck: true } && seen.Add(candidate.Key))
            {
                result.Add(candidate);
                if (visitCandidate?.Invoke(candidate) == true) return result.ToArray();
            }
            if (result.Count >= limit) break;
        }
        return result.ToArray();
    }

    internal CompleteTurnSearchAction[] GetFutureCheckingTravelCandidates(int limit = int.MaxValue,
        int perTravelCandidates = 16, Func<bool>? interruptedProbe = null,
        Func<CompleteTurnSearchAction, bool>? visitCandidate = null)
    {
        if (limit <= 0 || perTravelCandidates <= 0) return [];
        var result = new List<CompleteTurnSearchAction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var departures = new HashSet<(int From, int Departure, int? ArrivalLayer, int? ArrivalIndex)>();
        Func<bool>? previous = probeInterrupted;
        probeInterrupted = interruptedProbe;
        try
        {
            foreach (var travel in Travels())
            {
                budget.Spend();
                int from = travel.Location.Move!.Source.Timeline;
                if (!optional.Contains(from) || !MoveGivesCheck(travel.Location.Move.Move)) continue;
                bool branch = IsBranch(travel.Layer);
                var departure = (from, travel.Location.DepartureIndex,
                    branch ? (int?)null : travel.Layer, branch ? (int?)null : travel.Index);
                if (!departures.Add(departure)) continue;
                var probe = new Cursor();
                AddInitial(probe, FutureTravelSpace(from, travel.Location.DepartureIndex,
                    branch ? null : travel.Layer, branch ? null : travel.Index));
                for (int i = 0; i < perTravelCandidates; i++)
                {
                    CompleteTurnSearchAction? candidate = Next(probe);
                    if (candidate == null) break;
                    if (candidate.GivesCheck && seen.Add(candidate.Key))
                    {
                        result.Add(candidate);
                        if (visitCandidate?.Invoke(candidate) == true) return result.ToArray();
                    }
                    if (result.Count >= limit) return result.ToArray();
                }
            }
        }
        catch (HcBudgetExceededException) when (interruptedProbe?.Invoke() == true)
        {
            // A root's short optional-mate probe has its own deadline. Ending
            // that probe does not exhaust the main node or its legal inventory.
        }
        finally { probeInterrupted = previous; }
        return result.ToArray();
    }

    // Capturing travels to an existing frontier, leaving or entering an
    // optional board, are searched before the ordinary branch-count classes.
    // Only the best legal mandatory filler is retained per compatible original
    // hypercuboid class, as upstream. Each class has its own rejection allowance.
    internal CompleteTurnSearchAction[] GetTravelCaptureCandidates(int limit, int maxWork = 16,
        Func<CompleteTurnSearchAction, bool>? visitCandidate = null)
    {
        if (limit <= 0 || maxWork <= 0) return [];
        var result = new List<CompleteTurnSearchAction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var travel in Travels().Where(travel => !IsBranch(travel.Layer) &&
                         (optional.Contains(travel.Layer) || optional.Contains(travel.Location.Move!.Source.Timeline)) &&
                         root.GetSquare(travel.Location.Move!.Move.Dest) != Board.EMPTYSQUARE)
                     .OrderBy(travel => SearchHeuristics.PieceValue(root.GetSquare(travel.Location.Move!.Move.Dest))).Reverse())
            {
                budget.Spend();
                // Appended source classes are visited last-first. A difficult first
                // class cannot consume another compatible class's sixteen steps.
                foreach (HcCube<Move> cube in TravelSpace(travel.Layer, travel.Index, travel.Location, capturesOnly: true).Reverse())
                {
                    var probe = new Cursor();
                    AddInitial(probe, [cube]);
                    CompleteTurnSearchAction? candidate = Next(probe, maxWork, out _);
                    if (candidate != null && seen.Add(candidate.Key))
                    {
                        result.Add(candidate);
                        if (visitCandidate?.Invoke(candidate) == true) return result.ToArray();
                    }
                    if (result.Count >= limit) return result.ToArray();
                }
            }
        }
        catch (HcBudgetExceededException)
        {
            Interrupted = Incomplete = true;
            cancellation.ThrowIfCancellationRequested();
            if (result.Count == 0) throw;
        }
        return result.ToArray();
    }

    private CompleteTurnSearchAction? Next(Cursor source) => Next(source, int.MaxValue, out _);

    private CompleteTurnSearchAction? Next(Cursor source, int maximumCubes, out int spent)
    {
        spent = 0;
        if (source.Lone is { } walk)
        {
            walk.Choices ??= OrderedAxis(walk.Cube[walk.Layer]).Where(pair =>
                pair.Value.Kind is HcLocationKind.Physical or HcLocationKind.Pass).ToArray();
            while (spent < maximumCubes && walk.Position < walk.Choices.Length)
            {
                budget.Spend();
                spent++;
                SingleFrontierSteps++;
                var choice = walk.Choices[walk.Position++];
                var point = new HcPoint<Move>(walk.Cube.Count);
                foreach (var (layer, axis) in walk.Cube)
                    point[layer] = layer == walk.Layer ? new(choice.Key, choice.Value) : new(0, axis[0]);
                if (adapter.ValidateSearchPoint(point, out GameState after) != null) continue;
                CompleteTurnSearchAction? candidate = MakeAction(HcSearch<Move>.ToAction(point, model.NewTimeline), after, point);
                if (candidate != null)
                {
                    LastBranchClass = 0;
                    return candidate;
                }
            }
            if (walk.Position >= walk.Choices.Length) source.Lone = null;
        }
        while (spent < maximumCubes && source.Heap.TryDequeue(out Space? space, out _))
        {
            budget.Spend();
            spent++;
            HcCube<Move> ordered = Ordered(space.Cube);
            HcPoint<Move>? point = HcSearch<Move>.TakePoint(ordered, budget, matching);
            if (point == null) continue;
            HcSlice? problem = HcSearch<Move>.JumpOrderConsistent(point, model.Whole, model.NewTimeline, budget);
            GameState? after = null;
            if (problem == null) problem = adapter.ValidateSearchPoint(point, out after!);
            CompleteTurnSearchAction? result = null;
            if (problem == null)
            {
                Move[] moves = HcSearch<Move>.ToAction(point, model.NewTimeline);
                if (PlaysWastedMove(after!, moves, point))
                {
                    WastedMovesPruned++;
                    problem = (WastedSliceOptimizationOverride ?? true)
                        ? WastedSlice(point, after!) : HcSearch<Move>.ExactPointSlice(point);
                }
                else
                {
                    result = MakeAction(moves, after!, point);
                    problem = HcSearch<Move>.ExactPointSlice(point);
                }
            }
            if (!problem.All(pair => point.TryGetValue(pair.Key, out var choice) && pair.Value.Contains(choice.Index)))
                throw new InvalidOperationException("Search rejection did not contain the rejected turn.");
            foreach (HcCube<Move> remainder in HcSearch<Move>.RemoveSlice(space.Cube, problem, budget))
                Push(source, remainder, space.Class);
            if (result != null)
            {
                LastBranchClass = result.Branches;
                return result;
            }
        }
        return null;
    }

    private CompleteTurnSearchAction? MakeAction(Move[] moves, GameState after, HcPoint<Move> point)
    {
        (int Min, int Max)? replayBounds = null;
        // A restricted atomic root still searches whole turns. Independent
        // moves can be reordered to put the requested move first; the exact
        // replay below rejects any travel ordering that changes its meaning.
        if (restrictedFirst != null)
        {
            int wanted = Array.FindIndex(moves, move => restrictedFirst.Contains(RequestKey(move)));
            if (wanted < 0) return null;
            if (wanted != 0)
            {
                moves = new[] { moves[wanted] }.Concat(moves.Take(wanted)).Concat(moves.Skip(wanted + 1)).ToArray();
                after = root.Clone();
                after.TLHandicap = root.TLHandicap;
                foreach (Move action in moves)
                {
                    budget.Spend();
                    replayBounds = (after.MinTL, after.MaxTL);
                    if (!after.CoordIsPlayable(action.Origin) || !after.MakeMove(action.Clone(), validate: true) ||
                        !RoyalLegality.IsLegalAfterMove(root, action, after, budget)) return null;
                }
                if (!RoyalLegality.PresentHasPassed(after) || HypercuboidGameAdapter.HasDirectThreat(after, root.Color, budget))
                    return null;
            }
            // Caller-supplied moves may leave PieceMoved unset. Match by the
            // actual move, then retain the caller's first-action representation
            // so restricted-root result indexes keep their established contract.
            moves = moves.Select(move => move.Clone()).ToArray();
            moves[0] = requestedFirst![RequestKey(moves[0])].Clone();
        }
        bool capture = false, promotion = false, travel = false, onOptional = false;
        foreach (Move move in moves)
        {
            budget.Spend();
            capture |= Program.GetCapturedPiece(root, move) != Board.EMPTYSQUARE;
            promotion |= PositionHeuristics.Promotes(root, move);
            travel |= move.Origin.L != move.Dest.L || move.Origin.T != move.Dest.T;
            onOptional |= optional.Contains(move.Origin.L) || optional.Contains(move.Dest.L);
        }
        int branches = after.MaxTL - root.MaxTL + root.MinTL - after.MinTL;
        int originalMin = after.MinTL, originalMax = after.MaxTL;
        Move? last = moves.LastOrDefault()?.Clone();
        if (last != null)
        {
            // New timelines are assigned in action order. The final action can
            // add at most one; its branch axis identifies the prior bounds.
            var finalBranch = point.FirstOrDefault(pair => IsBranch(pair.Key) &&
                pair.Value.Location.Kind == HcLocationKind.Arrival &&
                RequestKey(pair.Value.Location.Move!.Move) == RequestKey(last));
            if (finalBranch.Value.Location != null)
            {
                if (root.Color) originalMax--;
                else originalMin++;
            }
        }
        if (replayBounds is { } bounds) { originalMin = bounds.Min; originalMax = bounds.Max; }
        // ValidateSearchPoint already established royal safety before submission.
        if (!after.SubmitMoves()) throw new InvalidOperationException("Validated complete turn failed submission.");
        bool givesCheck = HypercuboidGameAdapter.IsInCheck(after.Clone());
        double score = point.Sum(pair => Score(pair.Value.Location));
        return new(moves.Select(move => (Move?)move.Clone()).Append(null).ToArray(), after,
            score, branches, capture, promotion, travel, givesCheck, onOptional, last, originalMin, originalMax);
    }

    // Same wasted-move rule as upstream: after a branch moves the present
    // backward, advancing unrelated boards beyond that present only gives away
    // flexibility. A move that makes a later arrival branch is never wasted.
    internal static bool HasWastedMove(GameState after, IReadOnlyList<Move> moves, IReadOnlySet<int> branching) =>
        FindWastedMove(after, moves, branching) >= 0;

    internal static int FindWastedMove(GameState after, IReadOnlyList<Move> moves, IReadOnlySet<int> branching)
    {
        if (branching.Count == 0) return -1;
        bool color = after.CalcPresent();
        int present = HypercuboidGameAdapter.HalfTime(after.Present, color);
        bool Outside(int layer) => !after.LayerIsActive(layer) || after.GetTimeline(layer) is { } line &&
            HypercuboidGameAdapter.HalfTime(line.TEnd, line.ColorPlayable) > present;
        bool Landed(CoordFive square) => branching.Any(index =>
            moves[index].Dest.L == square.L && moves[index].Dest.T == square.T && moves[index].Dest.Color == square.Color);
        for (int i = 0; i < moves.Count; i++)
            if (!branching.Contains(i) && Outside(moves[i].Origin.L) && Outside(moves[i].Dest.L) &&
                !Landed(moves[i].Origin) && !Landed(moves[i].Dest)) return i;
        return -1;
    }

    private bool PlaysWastedMove(GameState after, Move[] moves, HcPoint<Move> point)
    {
        var branchMoves = point.Where(pair => IsBranch(pair.Key) && pair.Value.Location.Kind == HcLocationKind.Arrival)
            .Select(pair => CompleteTurnCandidates.ActionKey(pair.Value.Location.Move!.Move)).ToHashSet(StringComparer.Ordinal);
        if (branchMoves.Count == 0) return false;
        var branching = Enumerable.Range(0, moves.Length)
            .Where(index => branchMoves.Contains(CompleteTurnCandidates.ActionKey(moves[index]))).ToHashSet();
        return HasWastedMove(after, moves, branching);
    }

    // Remove the whole family of moves wasted for the same reason. Fixed
    // branch choices preserve the active range and their landing boards; one
    // active frontier holds the present no later than the witnessed time. The
    // wasted axis may then vary over every physical move and every arrival
    // whose departure is also outside that present. Its pass remains available.
    // Upstream finds the cheapest such witness and further relaxes branch
    // coordinates; retaining exact branch choices is a conservative slice with
    // the same eventual pruned action set and a simpler native-board certificate.
    private HcSlice WastedSlice(HcPoint<Move> point, GameState after)
    {
        bool presentColor = after.CalcPresent();
        int present = HypercuboidGameAdapter.HalfTime(after.Present, presentColor);
        var branches = point.Where(pair => IsBranch(pair.Key) && pair.Value.Location.Kind == HcLocationKind.Arrival)
            .Select(pair => pair.Value.Location.Move!).ToArray();
        bool Outside(int layer)
        {
            Timeline line = root.GetTimeline(layer)!;
            return !after.LayerIsActive(layer) || HypercuboidGameAdapter.HalfTime(line.TEnd, line.ColorPlayable) + 1 > present;
        }
        bool Landed(int layer)
        {
            Timeline line = root.GetTimeline(layer)!;
            var board = new HcBoard(layer, HypercuboidGameAdapter.HalfTime(line.TEnd, line.ColorPlayable));
            return branches.Any(move => move.Destination.Board == board);
        }
        foreach (var (layer, selected) in point)
        {
            budget.Spend();
            HcLocation<Move> location = selected.Location;
            if (IsBranch(layer) || location.Kind is not (HcLocationKind.Physical or HcLocationKind.Arrival)) continue;
            int from = location.Move!.Source.Timeline;
            if (!Outside(layer) || !Outside(from) || Landed(layer) || Landed(from)) continue;
            var problem = new HcSlice();
            foreach (var (branch, choice) in point)
                if (IsBranch(branch)) problem[branch] = [choice.Index];
            Timeline? witness = after.Multiverse.FirstOrDefault(line => after.LayerIsActive(line.Layer) &&
                HypercuboidGameAdapter.HalfTime(line.TEnd, line.ColorPlayable) == present);
            if (witness == null) return HcSearch<Move>.ExactPointSlice(point);
            if (point.TryGetValue(witness.Layer, out HcChoice<Move> fixedChoice))
            {
                if (witness.Layer == layer) return HcSearch<Move>.ExactPointSlice(point);
                problem[witness.Layer] = [fixedChoice.Index];
            }
            problem[layer] = model.Whole[layer].Where(pair =>
            {
                budget.Spend();
                if (pair.Value.Kind == HcLocationKind.Physical) return true;
                if (pair.Value.Kind != HcLocationKind.Arrival) return false;
                int source = pair.Value.Move!.Source.Timeline;
                return Outside(source) && !Landed(source);
            }).Select(pair => pair.Key).ToArray();
            return problem.All(pair => point.TryGetValue(pair.Key, out var choice) && pair.Value.Contains(choice.Index))
                ? problem : HcSearch<Move>.ExactPointSlice(point);
        }
        return HcSearch<Move>.ExactPointSlice(point);
    }

    private IEnumerable<HcCube<Move>> WithoutOptional(IEnumerable<HcCube<Move>> cubes)
    {
        foreach (HcCube<Move> cube in cubes)
        {
            budget.Spend();
            if (optional.Any(layer => cube.TryGetValue(layer, out var axis) && !axis.ContainsKey(0))) continue;
            var restricted = new HcCube<Move>(cube);
            foreach (int layer in optional)
                if (cube.TryGetValue(layer, out var axis)) restricted[layer] = new() { [0] = axis[0] };
            yield return restricted;
        }
    }

    private IEnumerable<HcCube<Move>> OptionalOnly(IEnumerable<HcCube<Move>> cubes)
    {
        int[] layers = optional.Order().ToArray();
        foreach (HcCube<Move> cube in cubes)
        for (int i = 0; i < layers.Length; i++)
        {
            budget.Spend();
            if (!cube.TryGetValue(layers[i], out var axis) || !axis.Keys.Any(index => index != 0)) continue;
            if (layers.Take(i).Any(layer => cube.TryGetValue(layer, out var earlier) && !earlier.ContainsKey(0))) break;
            var restricted = new HcCube<Move>(cube);
            foreach (int earlier in layers.Take(i))
                if (cube.TryGetValue(earlier, out var choices)) restricted[earlier] = new() { [0] = choices[0] };
            restricted[layers[i]] = axis.Where(pair => pair.Key != 0).ToDictionary();
            yield return restricted;
        }
    }

    private IReadOnlyList<HcCube<Move>> RestrictFirst(IEnumerable<HcCube<Move>> cubes)
    {
        var result = new List<HcCube<Move>>();
        foreach (HcCube<Move> cube in cubes)
        foreach (var (layer, axis) in cube)
        foreach (var (index, location) in axis)
        {
            budget.Spend();
            if (location.Kind is not (HcLocationKind.Physical or HcLocationKind.Arrival) ||
                !restrictedFirst!.Contains(RequestKey(location.Move!.Move))) continue;
            // The first branch must occupy the first branch axis. Later branch
            // positions cannot become the first action by merely reordering.
            if (IsBranch(layer) && layer != model.NewTimeline) continue;
            var restricted = new HcCube<Move>(cube) { [layer] = new() { [index] = location } };
            if (location.Kind == HcLocationKind.Arrival)
            {
                int source = location.Move!.Source.Timeline;
                if (!cube.TryGetValue(source, out var departures) || !departures.TryGetValue(location.DepartureIndex, out var departure))
                    continue;
                restricted[source] = new() { [location.DepartureIndex] = departure };
            }
            result.Add(restricted);
        }
        return result;
    }

    private IEnumerable<(int Layer, int Index, HcLocation<Move> Location)> Travels()
    {
        foreach (var (layer, axis) in model.Whole)
        {
            // Every branch axis has the same entries; inspect its shared table once.
            if (IsBranch(layer) && layer != model.NewTimeline) continue;
            foreach (var (index, location) in axis)
                if (location.Kind == HcLocationKind.Arrival) yield return (layer, index, location);
        }
    }

    private IEnumerable<HcCube<Move>> TravelSpace(int layer, int index, HcLocation<Move> arrival, bool capturesOnly)
    {
        int from = arrival.Move!.Source.Timeline;
        foreach (HcCube<Move> cube in allCubes)
        {
            budget.Spend();
            if (!cube.TryGetValue(layer, out var arrivals) || !arrivals.ContainsKey(index) ||
                !cube.TryGetValue(from, out var departures) || !departures.ContainsKey(arrival.DepartureIndex)) continue;
            if (optional.Any(other => other != layer && other != from &&
                    cube.TryGetValue(other, out var axis) && !axis.ContainsKey(0))) continue;
            if (capturesOnly && cube.Any(pair => IsBranch(pair.Key) && !pair.Value.ContainsKey(0))) continue;
            var restricted = new HcCube<Move>(cube);
            foreach (int other in optional)
                if (other != layer && other != from && cube.TryGetValue(other, out var axis))
                    restricted[other] = new() { [0] = axis[0] };
            if (capturesOnly)
                foreach (var (branch, axis) in cube.Where(pair => IsBranch(pair.Key)))
                    restricted[branch] = new() { [0] = axis[0] };
            restricted[layer] = new() { [index] = arrival };
            restricted[from] = new() { [arrival.DepartureIndex] = departures[arrival.DepartureIndex] };
            yield return restricted;
        }
    }

    // Upstream optional_travels: quiet arrivals from an optional board onto a
    // mandatory board follow the ordinary classes. They create no timeline and
    // retain every legal filler; the sixteen-cube/one-filler limit is captures only.
    private IEnumerable<HcCube<Move>> QuietOptionalTravelSpace()
    {
        if (optional.Count == 0) yield break;
        foreach (var travel in Travels())
        {
            budget.Spend();
            Move move = travel.Location.Move!.Move;
            if (IsBranch(travel.Layer) || optional.Contains(travel.Layer) ||
                !optional.Contains(travel.Location.Move.Source.Timeline) ||
                root.GetSquare(move.Dest) != Board.EMPTYSQUARE) continue;
            foreach (var cube in TravelSpace(travel.Layer, travel.Index, travel.Location, capturesOnly: true))
                yield return cube;
        }
    }

    private IEnumerable<HcCube<Move>> FutureTravelSpace(int from, int departure, int? arrivalLayer, int? arrivalIndex)
    {
        foreach (HcCube<Move> cube in allCubes)
        {
            budget.Spend();
            if (!cube.TryGetValue(from, out var departures) || !departures.ContainsKey(departure) ||
                optional.Any(other => other != from && cube.TryGetValue(other, out var axis) && !axis.ContainsKey(0)) ||
                arrivalLayer.HasValue && (!cube.TryGetValue(arrivalLayer.Value, out var arrivals) ||
                                          !arrivals.ContainsKey(arrivalIndex!.Value))) continue;
            var restricted = new HcCube<Move>(cube);
            foreach (int other in optional)
                if (other != from && cube.TryGetValue(other, out var axis)) restricted[other] = new() { [0] = axis[0] };
            restricted[from] = new() { [departure] = departures[departure] };
            if (arrivalLayer.HasValue)
                restricted[arrivalLayer.Value] = new() { [arrivalIndex!.Value] = cube[arrivalLayer.Value][arrivalIndex.Value] };
            yield return restricted;
        }
    }

    private void AddInitial(Cursor target, IEnumerable<HcCube<Move>> cubes)
    {
        HcCube<Move>[] initial = cubes.Where(cube => cube.Values.All(axis => axis.Count != 0)).ToArray();
        int offset = target.Heap.Count == 0 ? 0 : target.Heap.UnorderedItems.Max(item => item.Element.Class) + 1;
        if (target.Lone != null) offset = Math.Max(offset, target.Lone.Class + 1);
        int? loneLayer = model.Whole.Keys.Count(layer => !IsBranch(layer)) == 1
            ? model.Whole.Keys.First(layer => !IsBranch(layer)) : null;
        for (int i = 0; i < initial.Length; i++)
        {
            budget.Spend();
            HcCube<Move> cube = initial[i];
            int precedence = offset + initial.Length - 1 - i;
            // Initial classes need no score bound. This leaves branch scoring
            // deferred until a branch-count class is actually consumed. Every
            // initial cube keeps its own precedence, including optional-only
            // partitions within the same branch count (last initial cube first).
            if (offset == 0 && i == initial.Length - 1 && loneLayer is { } only &&
                cube.All(pair => pair.Key == only || pair.Value.Count == 1 && pair.Value.ContainsKey(0)))
            {
                // With one original playable frontier and no new branches,
                // complete turns are individual spatial moves (or submission).
                // Walk their ordered coordinates without matching, heap splits,
                // or repeated Cartesian-product construction.
                target.Lone = new(cube, only, precedence);
                continue;
            }
            target.Heap.Enqueue(new(cube, precedence), (precedence, 0, -target.Sequence++));
        }
    }

    private void Push(Cursor target, HcCube<Move> cube, int branchClass)
    {
        budget.Spend();
        if (cube.Values.Any(axis => axis.Count == 0)) return;
        double bound = 0;
        foreach (var axis in cube.Values)
        {
            double best = double.NegativeInfinity;
            foreach (HcLocation<Move> location in axis.Values) best = Math.Max(best, Score(location));
            bound += best;
        }
        target.Heap.Enqueue(new(cube, branchClass), (branchClass, -bound, -target.Sequence++));
    }

    private HcCube<Move> Ordered(HcCube<Move> cube)
    {
        var ordered = new HcCube<Move>();
        foreach (var (layer, axis) in cube)
        {
            budget.Spend();
            ordered.Add(layer, OrderedAxis(axis));
        }
        return ordered;
    }

    private Dictionary<int, HcLocation<Move>> OrderedAxis(Dictionary<int, HcLocation<Move>> axis)
    {
        if (!orderedAxes.TryGetValue(axis, out var sorted))
        {
            sorted = axis.OrderByDescending(pair => Score(pair.Value)).ThenBy(pair => pair.Key).ToDictionary();
            orderedAxes.Add(axis, sorted);
        }
        return sorted;
    }

    private double Score(HcLocation<Move> location)
    {
        if (location.Kind == HcLocationKind.Pass) return -5;
        if (location.Kind == HcLocationKind.Departure) return 0;
        if (coordinateScores.TryGetValue(location, out double cached)) return cached;
        Move move = location.Move!.Move;
        string key = CompleteTurnCandidates.ActionKey(move);
        if (scores.TryGetValue(key, out double value))
        {
            coordinateScores.Add(location, value);
            return value;
        }
        budget.Spend();
        using var applied = scoring.Apply(move.Clone());
        if (!applied.Succeeded) throw new InvalidOperationException("Scored inventory move no longer applies.");
        value = scoreMove?.Invoke(move, scoring.State) ?? DefaultScore(move, scoring.State);
        if (!double.IsFinite(value)) throw new InvalidOperationException("Complete-turn ordering returned a non-finite score.");
        scores.Add(key, value);
        coordinateScores.Add(location, value);
        return value;
    }

    private double DefaultScore(Move move, GameState after)
    {
        return SearchHeuristics.CoordinateScore(root, move, after, givesCheck: MoveGivesCheck(move, after));
    }

    internal bool MoveGivesCheck(Move move, GameState? after = null)
    {
        string key = CompleteTurnCandidates.ActionKey(move);
        if (checks.TryGetValue(key, out bool cached)) return cached;
        budget.Spend();
        if (after != null) cached = SemimoveGivesCheck(root, move, after, budget);
        else
        {
            using var applied = scoring.Apply(move.Clone());
            cached = applied.Succeeded && SemimoveGivesCheck(root, move, scoring.State, budget);
        }
        checks.Add(key, cached);
        return cached;
    }

    internal static bool SemimoveGivesCheck(GameState root, Move move, GameState after, HcBudget? budget = null)
    {
        bool HasCheck(Board board)
        {
            for (int x = 0; x < board.Width; x++)
            for (int y = 0; y < board.Height; y++)
            {
                budget?.Spend();
                int piece = board.GetSquareSafe(x, y);
                if (Board.IsRoyalPiece(piece) && Board.GetColorBool(piece) != root.Color &&
                    MoveGenerator.IsSquareAttacked(board, new(x, y, 0, 0, !root.Color), root.Color)) return true;
            }
            return false;
        }
        // Semimove ordering checks the result and departure boards directly.
        // An arrival after Black's move is WhiteT+1 even while an unplayed
        // mandatory board keeps the present at BlackT; a present-only phantom
        // scan would miss that checking travel and never try its fillers.
        bool travel = move.Origin.L != move.Dest.L || move.Origin.T != move.Dest.T;
        int arrival = after.MaxTL > root.MaxTL ? after.MaxTL :
            after.MinTL < root.MinTL ? after.MinTL : move.Dest.L;
        Board resultBoard = after.GetTimeline(arrival)!.GetPlayableBoard();
        Board departureBoard = after.GetTimeline(move.Origin.L)!.GetPlayableBoard();
        if (HasCheck(resultBoard) || travel && HasCheck(departureBoard)) return true;

        // Match the upstream candidate-board overlay: original opponent
        // frontiers are also available after a hypothetical pass, and the
        // candidate's two result boards remain available after that pass.
        // These are read-only lookups; no timeline histories need copying.
        int SquareAt(int x, int y, int time, int layer)
        {
            if (x < 0 || x >= root.Width || y < 0 || y >= root.Height) return Board.ERRORSQUARE;
            if (layer == arrival && time == move.Dest.T + 1) return resultBoard.GetSquareSafe(x, y);
            if (travel && layer == move.Origin.L && time == move.Origin.T + 1)
                return departureBoard.GetSquareSafe(x, y);
            Timeline? line = root.GetTimeline(layer);
            if (line == null) return Board.ERRORSQUARE;
            if (line.ColorPlayable != root.Color && time == line.TEnd + (root.Color ? 1 : 0))
                return line.GetPlayableBoard().GetSquareSafe(x, y);
            if (root.Color)
                return time < line.WhiteStart || time > line.WhiteEnd ? Board.ERRORSQUARE :
                    line.WBoards[time - line.WhiteStart].GetSquare(x, y);
            return time < line.BlackStart || time > line.BlackEnd ? Board.ERRORSQUARE :
                line.BBoards[time - line.BlackStart].GetSquare(x, y);
        }

        bool HasTemporalCheck(Board board, int layer, int time)
        {
            int piece = board.GetSquareSafe(move.Dest.X, move.Dest.Y);
            if (piece == Board.EMPTYSQUARE || Board.GetColorBool(piece) != root.Color) return false;
            piece = Math.Abs(piece);
            CoordFive[] vectors = piece switch
            {
                (int)Board.Piece.WPAWN => MoveGenerator.whitePawnAttack,
                (int)Board.Piece.BPAWN => MoveGenerator.blackPawnattack,
                (int)Board.Piece.WBRAWN => MoveGenerator.whiteBrawnattack,
                (int)Board.Piece.BBRAWN => MoveGenerator.blackBrawnattack,
                _ => MoveGenerator.GetMoveVectors(piece)
            };
            bool rider = MoveGenerator.PieceIsRider(piece);
            foreach (CoordFive vector in vectors)
            {
                if (vector.IsSpatial()) continue;
                int x = move.Dest.X + vector.X, y = move.Dest.Y + vector.Y;
                int t = time + vector.T, l = layer + vector.L;
                while (true)
                {
                    budget?.Spend();
                    int target = SquareAt(x, y, t, l);
                    if (target == Board.ERRORSQUARE) break;
                    if (target != Board.EMPTYSQUARE)
                    {
                        if (Board.GetColorBool(target) != root.Color && Board.IsRoyalPiece(target)) return true;
                        break;
                    }
                    if (!rider) break;
                    x += vector.X; y += vector.Y; t += vector.T; l += vector.L;
                }
            }
            return false;
        }
        // Only the destination-square mask is scanned for temporal checks;
        // unrelated discovered temporal attacks are not ordering bonuses.
        return HasTemporalCheck(resultBoard, arrival, move.Dest.T + 1) ||
            travel && HasTemporalCheck(departureBoard, move.Origin.L, move.Origin.T + 1);
    }

    private static string RequestKey(Move move)
    {
        Move normalized = move.Clone();
        normalized.PieceMoved = 0;
        normalized.Type = 0;
        return CompleteTurnCandidates.ActionKey(normalized);
    }
    private bool IsBranch(int layer) => (long)layer * Math.Sign(model.NewTimeline) >= (long)model.NewTimeline * Math.Sign(model.NewTimeline);
}
