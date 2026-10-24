namespace SkynetZero;

// Adapted from penteract/hcuboid-ts, hcuboid.ts, revision
// b558270418c27e8f8cefe314280499600af09597 (public domain).
// The upstream imports.ts is an example interface, not a chess rules implementation.
// Rule-specific present/check validation is supplied by CheckAndMate's engine adapter.
internal readonly record struct HcBoard(int Timeline, int Time);
internal readonly record struct HcSquare(int Timeline, int Time, int X, int Y)
{
    public HcBoard Board => new(Timeline, Time);
}

internal sealed record HcMove<TMove>(TMove Move, HcSquare Source, HcSquare Destination,
    object? SourceBoard = null, object? DestinationBoard = null);

internal enum HcLocationKind { Physical, Arrival, Departure, Pass }
internal sealed record HcLocation<TMove>(HcLocationKind Kind, HcMove<TMove>? Move = null,
    HcSquare? Source = null, object? Board = null, int DepartureIndex = -1, HcBoard? PassBoard = null);
internal readonly record struct HcChoice<TMove>(int Index, HcLocation<TMove> Location);
internal sealed class HcCube<TMove> : Dictionary<int, Dictionary<int, HcLocation<TMove>>>
{
    public HcCube() { }
    public HcCube(IEnumerable<KeyValuePair<int, Dictionary<int, HcLocation<TMove>>>> source) : base(source) { }
}
internal sealed class HcPoint<TMove> : Dictionary<int, HcChoice<TMove>>
{
    public HcPoint() { }
    public HcPoint(int capacity) : base(capacity) { }
}
internal sealed class HcSlice : Dictionary<int, int[]>
{
    public HcSlice() { }
    public HcSlice(int capacity) : base(capacity) { }
}
internal sealed record HcModel<TMove>(HcCube<TMove> Whole, IReadOnlyList<HcCube<TMove>> Cubes, int NewTimeline);
internal enum HcSearchStatus { Found, Exhausted, Unknown }
internal sealed record HcSearchResult<TMove>(HcSearchStatus Status, IReadOnlyList<TMove>? Moves, long Work);

internal sealed class HcBudgetExceededException : Exception { }
internal sealed class HcBudget
{
    private readonly long maximum;
    private readonly CancellationToken cancellation;
    private readonly long deadline;
    private readonly Func<bool>? interrupted;
    private readonly Action<long>? cooperate;
    private long nextCooperation;
    private long cooperationWork;
    private static readonly long CooperationInterval = Math.Max(1, System.Diagnostics.Stopwatch.Frequency / 32);
    public long Work { get; private set; }
    public HcBudget(long maxWork, CancellationToken cancellation = default, int maxMilliseconds = 0,
        Func<bool>? interrupted = null, Action<long>? cooperate = null)
    {
        if (maxWork < 0) throw new ArgumentOutOfRangeException(nameof(maxWork));
        if (maxMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(maxMilliseconds));
        maximum = maxWork;
        this.cancellation = cancellation;
        this.interrupted = interrupted;
        this.cooperate = cooperate;
        nextCooperation = cooperate == null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp() + CooperationInterval;
        deadline = maxMilliseconds == 0 ? 0 : System.Diagnostics.Stopwatch.GetTimestamp() +
            (long)(maxMilliseconds * (double)System.Diagnostics.Stopwatch.Frequency / 1_000);
    }

    public void Spend(long amount = 1)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (cancellation.IsCancellationRequested || interrupted?.Invoke() == true || maximum > 0 && amount > maximum - Work ||
            deadline != 0 && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
            throw new HcBudgetExceededException();
        // Unlimited proofs still report work. Saturating telemetry must not
        // overflow or silently reintroduce a stopping condition.
        Work = amount > long.MaxValue - Work ? long.MaxValue : Work + amount;
        // Only callers at a rule-only boundary supply this callback. The proof
        // keeps its current cube, matching workspace and private game snapshot
        // while the rolling trainer parks this worker for an epoch update.
        // Sampling the clock per 1024 operations avoids a lock/clock per edge.
        if (cooperate != null && Work - cooperationWork >= 1024)
        {
            cooperationWork = Work;
            if (System.Diagnostics.Stopwatch.GetTimestamp() >= nextCooperation)
            {
                cooperate(Work);
                nextCooperation = System.Diagnostics.Stopwatch.GetTimestamp() + CooperationInterval;
            }
        }
    }
}

internal static class HcSearch<TMove>
{
    // A location on each original timeline is a physical move, departure, arrival, or
    // pass. Additional axes encode branch arrivals; splitting by branch count excludes
    // holes in the sequence of created timelines. Coordinates retain stable indexes.
    public static HcModel<TMove> BuildHCs(IReadOnlyDictionary<int, int> playable,
        IEnumerable<HcMove<TMove>> moves, int newTimeline, HcBudget budget)
    {
        if (newTimeline == 0) throw new ArgumentOutOfRangeException(nameof(newTimeline));
        var axes = new HcCube<TMove>();
        foreach (var (timeline, time) in playable.OrderBy(pair => pair.Key))
        {
            budget.Spend();
            axes.Add(timeline, new() { [0] = new(HcLocationKind.Pass, PassBoard: new(timeline, time)) });
        }
        var arrivals = new Dictionary<int, HcLocation<TMove>>
        {
            [0] = new(HcLocationKind.Pass)
        };
        var departures = new Dictionary<HcSquare, int>();
        foreach (HcMove<TMove> move in moves)
        {
            budget.Spend();
            if (!axes.TryGetValue(move.Source.Timeline, out var sourceAxis) ||
                playable[move.Source.Timeline] != move.Source.Time)
                throw new ArgumentException("A hypercuboid move must start on a playable frontier.", nameof(moves));
            if (move.Source.Board == move.Destination.Board)
            {
                sourceAxis.Add(sourceAxis.Count, new(HcLocationKind.Physical, move, Board: move.SourceBoard));
                continue;
            }
            // Unlike upstream's consecutive-source grouping, this also accepts an
            // interleaved move generator. All jumps by a piece have the same departure.
            if (!departures.TryGetValue(move.Source, out int departureIndex))
            {
                departureIndex = sourceAxis.Count;
                sourceAxis.Add(departureIndex, new(HcLocationKind.Departure, Source: move.Source, Board: move.SourceBoard));
                departures.Add(move.Source, departureIndex);
            }
            var arrival = new HcLocation<TMove>(HcLocationKind.Arrival, move,
                Board: move.DestinationBoard, DepartureIndex: departureIndex);
            arrivals.Add(arrivals.Count, arrival);
            if (playable.TryGetValue(move.Destination.Timeline, out int endTime) && endTime == move.Destination.Time)
            {
                var destinationAxis = axes[move.Destination.Timeline];
                destinationAxis.Add(destinationAxis.Count, arrival);
            }
        }
        int maxBranches = axes.Values.Count(axis => axis.Values.Any(loc => loc.Kind == HcLocationKind.Departure));
        int sign = Math.Sign(newTimeline);
        var branchArrivals = arrivals.Where(pair => pair.Key != 0).ToDictionary();
        var noBranch = new Dictionary<int, HcLocation<TMove>> { [0] = arrivals[0] };
        var cubes = new List<HcCube<TMove>>();
        for (int branches = maxBranches; branches >= 0; --branches)
        {
            budget.Spend();
            var cube = new HcCube<TMove>(axes);
            for (int offset = 0; offset < maxBranches; ++offset)
            {
                budget.Spend();
                int timeline = checked(newTimeline + sign * offset);
                if (cube.ContainsKey(timeline)) throw new ArgumentException("New branch overlaps an existing playable timeline.");
                cube.Add(timeline, offset < branches ? branchArrivals : noBranch);
            }
            cubes.Add(cube);
        }
        for (int offset = 0; offset < maxBranches; ++offset)
            axes.Add(checked(newTimeline + sign * offset), arrivals);
        return new(axes, cubes, newTimeline);
    }

    // Exhausted is a proof only when the adapter's move inventory is complete and
    // every returned rejection slice is sound. Budget exhaustion is always Unknown.
    public static HcSearchResult<TMove> Search(HcModel<TMove> model,
        Func<HcPoint<TMove>, HcSlice?> validate, HcBudget budget)
    {
        try
        {
            var pending = new Stack<(HcCube<TMove> Cube, int Applied)>(model.Cubes.Select(cube => (cube, 0)));
            var learned = new List<HcSlice>();
            var workspace = new PointWorkspace();
            while (pending.TryPop(out var next))
            {
                budget.Spend();
                HcCube<TMove>? cube = next.Cube;
                // An adapter slice certifies an illegal family in the whole
                // inventory, including sibling cubes created by jump-order
                // partitioning. Apply each learned rejection before doing another
                // matching/validation. Otherwise one identical temporal check can
                // be rediscovered millions of times across those siblings.
                for (int i = next.Applied; i < learned.Count; i++)
                {
                    budget.Spend();
                    var pieces = RemoveSlice(cube, learned[i], budget);
                    if (pieces.Count == 0) { cube = null; break; }
                    for (int j = 0; j < pieces.Count - 1; j++) pending.Push((pieces[j], i + 1));
                    cube = pieces[^1];
                }
                if (cube == null) continue;
                HcPoint<TMove>? point = TakePoint(cube, budget, workspace);
                if (point is null) continue;
                HcSlice? problem = JumpOrderConsistent(point, model.Whole, model.NewTimeline, budget);
                bool fromAdapter = problem == null;
                if (fromAdapter) problem = validate(point);
                if (problem is null)
                    return new(HcSearchStatus.Found, ToAction(point, model.NewTimeline), budget.Work);
                // A faulty adapter must not loop without removing its rejected point.
                if (!Contains(problem, point))
                    throw new InvalidOperationException("A rejection slice must contain the rejected point.");
                // Complete singleton points already belong to one disjoint
                // product. Remembering thousands of them would add quadratic
                // propagation work without eliminating any sibling family.
                if (fromAdapter && (problem.Count < model.Whole.Count || problem.Values.Any(axis => axis.Length > 1)))
                    learned.Add(problem);
                foreach (HcCube<TMove> remainder in RemoveSlice(cube, problem, budget))
                    pending.Push((remainder, learned.Count));
            }
            return new(HcSearchStatus.Exhausted, null, budget.Work);
        }
        catch (HcBudgetExceededException)
        {
            return new(HcSearchStatus.Unknown, null, budget.Work);
        }
    }

    public static TMove[] ToAction(HcPoint<TMove> point, int newTimeline)
    {
        int sign = Math.Sign(newTimeline);
        return point.OrderBy(pair => (long)pair.Key * sign)
            .Select(pair => pair.Value.Location)
            .Where(loc => loc.Kind is HcLocationKind.Physical or HcLocationKind.Arrival)
            .Select(loc => loc.Move!.Move).ToArray();
    }

    public static HcSlice ExactPointSlice(HcPoint<TMove> point)
    {
        var result = new HcSlice(point.Count);
        foreach (var (timeline, choice) in point) result.Add(timeline, new[] { choice.Index });
        return result;
    }

    public static HcSlice? JumpOrderConsistent(HcPoint<TMove> point, HcCube<TMove> whole,
        int newTimeline, HcBudget budget)
    {
        var earlierArrivals = new Dictionary<HcBoard, int>();
        int sign = Math.Sign(newTimeline);
        for (int timeline = newTimeline; point.TryGetValue(timeline, out var branch) &&
            branch.Location.Kind == HcLocationKind.Arrival; timeline = checked(timeline + sign))
        {
            budget.Spend();
            HcMove<TMove> move = branch.Location.Move!;
            HcBoard target = move.Destination.Board;
            if (point.TryGetValue(target.Timeline, out var targetChoice) &&
                targetChoice.Location.Kind == HcLocationKind.Pass && targetChoice.Location.PassBoard == target)
            {
                return new HcSlice
                {
                    [target.Timeline] = new[] { targetChoice.Index },
                    [timeline] = SelectIndexes(whole[timeline], loc => loc.Kind == HcLocationKind.Arrival &&
                        loc.Move!.Destination.Board == target, budget)
                };
            }
            HcBoard source = move.Source.Board;
            if (earlierArrivals.TryGetValue(source, out int earlier))
            {
                return new HcSlice
                {
                    [timeline] = SelectIndexes(whole[timeline], loc => loc.Kind == HcLocationKind.Arrival &&
                        loc.Move!.Source.Board == source, budget),
                    [earlier] = SelectIndexes(whole[earlier], loc => loc.Kind == HcLocationKind.Arrival &&
                        loc.Move!.Destination.Board == source, budget)
                };
            }
            earlierArrivals[target] = timeline;
        }
        return null;
    }

    private static int[] SelectIndexes(Dictionary<int, HcLocation<TMove>> axis,
        Func<HcLocation<TMove>, bool> select, HcBudget budget)
    {
        var indexes = new List<int>();
        foreach (var (index, location) in axis)
        {
            budget.Spend();
            if (select(location)) indexes.Add(index);
        }
        return indexes.ToArray();
    }

    // Pair an arrival with its departure. Every axis without a physical move/pass
    // must participate in the matching; optional axes can instead use their fallback.
    // A proof visits many closely related cubes. Keep only temporary matching
    // storage here: points and selected choices never reference mutable scratch.
    // The workspace belongs to one Search call, so parallel games cannot share it.
    internal sealed class PointWorkspace
    {
        internal readonly HashSet<int> Required = new();
        internal readonly List<HcMatchingEdge<HcChoice<TMove>>> Edges = new();
        internal readonly HcMatching.Workspace<HcChoice<TMove>> Matching = new();
    }

    public static HcPoint<TMove>? TakePoint(HcCube<TMove> cube, HcBudget budget) =>
        TakePoint(cube, budget, new PointWorkspace());

    internal static HcPoint<TMove>? TakePoint(HcCube<TMove> cube, HcBudget budget, PointWorkspace workspace)
    {
        var point = new HcPoint<TMove>(cube.Count);
        var required = workspace.Required;
        var edges = workspace.Edges;
        required.Clear();
        edges.Clear();
        foreach (var (timeline, axis) in cube)
        {
            budget.Spend();
            if (axis.Count == 0) return null;
            foreach (var (index, loc) in axis)
            {
                budget.Spend();
                if (loc.Kind is HcLocationKind.Physical or HcLocationKind.Pass)
                {
                    point[timeline] = new(index, loc);
                    break;
                }
            }
            if (!point.ContainsKey(timeline)) required.Add(timeline);
            foreach (var (index, loc) in axis)
            {
                budget.Spend();
                if (loc.Kind != HcLocationKind.Arrival) continue;
                int sourceTimeline = loc.Move!.Source.Timeline;
                if (sourceTimeline == timeline || !cube.TryGetValue(sourceTimeline, out var source) ||
                    !source.TryGetValue(loc.DepartureIndex, out var departure) ||
                    departure.Kind != HcLocationKind.Departure || departure.Source != loc.Move.Source) continue;
                edges.Add(new(timeline, sourceTimeline, new(index, loc), new(loc.DepartureIndex, departure)));
            }
        }
        Dictionary<int, HcChoice<TMove>>? matching = HcMatching.Find(cube.Keys, edges, required, budget, workspace.Matching);
        if (matching is null) return null;
        foreach (var (timeline, choice) in matching) point[timeline] = choice;
        return point;
    }

    // Partition HC \ slice into disjoint hypercuboids. At the first constrained
    // axis where a point differs, take the complement; preceding axes intersect.
    public static IReadOnlyList<HcCube<TMove>> RemoveSlice(HcCube<TMove> cube, HcSlice slice, HcBudget budget)
    {
        // A slice mentioning an absent axis is outside this product space.
        if (slice.Keys.Any(axis => !cube.ContainsKey(axis))) return new[] { cube };
        int constrainedAxes = Math.Min(slice.Count, cube.Count);
        var pieces = new List<HcCube<TMove>>(constrainedAxes);
        var prefix = new Dictionary<int, Dictionary<int, HcLocation<TMove>>>(constrainedAxes);
        foreach (var (timeline, axis) in cube)
        {
            budget.Spend();
            if (!slice.TryGetValue(timeline, out int[]? selected)) continue;
            // Exact-point rejections dominate difficult proofs. Their singleton
            // cuts do not need a hash set, and both resulting axis sizes are known.
            // Larger slices may contain duplicates or coordinates outside this cube.
            HashSet<int>? indexes = selected.Length > 1 ? selected.ToHashSet() : null;
            int intersectionCount = indexes == null
                ? selected.Length == 1 && axis.ContainsKey(selected[0]) ? 1 : 0
                : indexes.Count(axis.ContainsKey);
            int complementCount = axis.Count - intersectionCount;
            // Axes are immutable after construction and already shared by cubes.
            // Reuse an unchanged axis instead of copying every coordinate again.
            var intersection = intersectionCount == 0 ? null : intersectionCount == axis.Count
                ? axis : new Dictionary<int, HcLocation<TMove>>(intersectionCount);
            var complement = complementCount == 0 ? null : complementCount == axis.Count
                ? axis : new Dictionary<int, HcLocation<TMove>>(complementCount);
            foreach (var (index, loc) in axis)
            {
                budget.Spend();
                if (intersectionCount == 0 || complementCount == 0) continue;
                bool inside = indexes?.Contains(index) ?? index == selected[0];
                (inside ? intersection! : complement!).Add(index, loc);
            }
            if (complement != null)
            {
                var piece = new HcCube<TMove>(cube);
                foreach (var earlier in prefix) piece[earlier.Key] = earlier.Value;
                piece[timeline] = complement;
                pieces.Add(piece);
            }
            if (intersection == null) break;
            prefix[timeline] = intersection;
        }
        return pieces;
    }

    public static IReadOnlyList<HcCube<TMove>> RemovePoint(HcCube<TMove> cube, HcPoint<TMove> point, HcBudget budget) =>
        RemoveSlice(cube, ExactPointSlice(point), budget);

    private static bool Contains(HcSlice slice, HcPoint<TMove> point) =>
        slice.All(pair => point.TryGetValue(pair.Key, out var choice) && pair.Value.Contains(choice.Index));
}

internal sealed record HcMatchingEdge<T>(int A, int B, T AtA, T AtB);
internal static class HcMatching
{
    // The exhaustive implementation remains available only for differential tests.
    // Production always uses polynomial matching unless a test explicitly opts out.
    [ThreadStatic] internal static bool? PolynomialOverride;

    internal sealed class Frame<T>
    {
        internal List<HcMatchingEdge<T>> Edges = null!;
        internal int Next;
        internal HcMatchingEdge<T>? Applied;
    }

    // Retained only for the duration of one proof. Both graph storage and blossom
    // search arrays are reused between cubes. The legacy DFS scratch is used only
    // by differential tests, and never by the production matching algorithm.
    internal sealed class Workspace<T>
    {
        internal readonly Dictionary<int, List<HcMatchingEdge<T>>> Graph = new();
        internal readonly Dictionary<int, T> Matching = new();
        private readonly List<List<HcMatchingEdge<T>>> adjacency = new();
        private readonly List<Frame<T>> frames = new();
        internal readonly Dictionary<int, int> VertexIndexes = new();
        internal readonly List<int> VertexIds = new();
        internal readonly Dictionary<ulong, HcMatchingEdge<T>> OriginalEdges = new();
        internal readonly List<List<int>> Neighbors = new();
        internal int[] Mate = Array.Empty<int>();
        internal int[] Parent = Array.Empty<int>();
        internal int[] Base = Array.Empty<int>();
        internal int[] Queue = Array.Empty<int>();
        internal bool[] Required = Array.Empty<bool>();
        internal bool[] Queued = Array.Empty<bool>();
        internal bool[] Blossom = Array.Empty<bool>();
        internal bool[] Ancestor = Array.Empty<bool>();

        internal void ResetPolynomial(int vertexCount, int requiredCount)
        {
            VertexIndexes.Clear();
            VertexIndexes.EnsureCapacity(vertexCount);
            VertexIds.Clear();
            VertexIds.EnsureCapacity(vertexCount);
            OriginalEdges.Clear();
            Matching.Clear();
            Matching.EnsureCapacity((int)Math.Min(vertexCount, 2L * requiredCount));
            foreach (var neighbors in Neighbors) neighbors.Clear();
        }

        internal void PreparePolynomial(int count)
        {
            if (Mate.Length < count)
            {
                int capacity = Math.Max(count, Mate.Length * 2);
                Array.Resize(ref Mate, capacity);
                Array.Resize(ref Parent, capacity);
                Array.Resize(ref Base, capacity);
                Array.Resize(ref Queue, capacity);
                Array.Resize(ref Required, capacity);
                Array.Resize(ref Queued, capacity);
                Array.Resize(ref Blossom, capacity);
                Array.Resize(ref Ancestor, capacity);
            }
            Array.Fill(Mate, -1, 0, count);
            Array.Clear(Required, 0, count);
            while (Neighbors.Count < count) Neighbors.Add(new());
        }

        internal void Reset(int vertexCount, int requiredCount)
        {
            Graph.Clear();
            Graph.EnsureCapacity(vertexCount);
            Matching.Clear();
            Matching.EnsureCapacity((int)Math.Min(vertexCount, 2L * requiredCount));
            foreach (var edges in adjacency) edges.Clear();
            adjacency.EnsureCapacity(vertexCount);
            frames.EnsureCapacity(Math.Min(vertexCount / 2, requiredCount));
        }

        internal List<HcMatchingEdge<T>> Adjacency(int index)
        {
            if (index == adjacency.Count) adjacency.Add(new());
            return adjacency[index];
        }

        internal Frame<T> Push(int depth, List<HcMatchingEdge<T>> edges)
        {
            if (depth == frames.Count) frames.Add(new());
            Frame<T> frame = frames[depth];
            frame.Edges = edges;
            frame.Next = 0;
            frame.Applied = null;
            return frame;
        }

        internal Frame<T> Peek(int depth) => frames[depth - 1];
    }

    // Find a matching covering every mandatory vertex. Unlike the upstream short
    // augmenting-path routine, Edmonds' algorithm contracts odd cycles; unlike our
    // former exhaustive solver, it takes polynomial work even when no cover exists.
    // Witness and work order may change, but every selected original edge is exact.
    public static Dictionary<int, T>? Find<T>(IEnumerable<int> vertices,
        IReadOnlyList<HcMatchingEdge<T>> edges, IReadOnlySet<int> required, HcBudget budget) =>
        Find(vertices, edges, required, budget, new Workspace<T>());

    internal static Dictionary<int, T>? Find<T>(IEnumerable<int> vertices,
        IReadOnlyList<HcMatchingEdge<T>> edges, IReadOnlySet<int> required, HcBudget budget, Workspace<T> workspace)
    {
        if (PolynomialOverride == false) return FindLegacy(vertices, edges, required, budget, workspace);
        return FindPolynomial(vertices, edges, required, budget, workspace);
    }

    private static Dictionary<int, T>? FindPolynomial<T>(IEnumerable<int> vertices,
        IReadOnlyList<HcMatchingEdge<T>> edges, IReadOnlySet<int> required, HcBudget budget, Workspace<T> workspace)
    {
        budget.Spend();
        vertices.TryGetNonEnumeratedCount(out int vertexCount);
        workspace.ResetPolynomial(vertexCount, required.Count);
        var indexes = workspace.VertexIndexes;
        var ids = workspace.VertexIds;
        foreach (int vertex in vertices)
        {
            budget.Spend();
            indexes.Add(vertex, ids.Count);
            ids.Add(vertex);
        }
        if (required.Count == 0) return workspace.Matching;
        foreach (int vertex in required)
        {
            budget.Spend();
            if (!indexes.ContainsKey(vertex)) return null;
        }
        int originalCount = ids.Count;
        // Reduce mandatory coverage to perfect matching: connect every optional
        // vertex to every other optional vertex, and add one optional dummy if the
        // original vertex count is odd. Any cover leaves an even number of optional
        // vertices unmatched in this even-size graph, so its clique completes it.
        // Conversely, every edge touching a required vertex remains an original
        // edge; removing synthetic optional pairs recovers a mandatory cover.
        int count = checked(originalCount + (originalCount & 1));
        if ((originalCount & 1) != 0 && required.Count == originalCount) return null;
        workspace.PreparePolynomial(count);
        foreach (int vertex in required)
        {
            budget.Spend();
            workspace.Required[indexes[vertex]] = true;
        }
        var originalEdges = workspace.OriginalEdges;
        var graph = workspace.Neighbors;
        bool hasRequiredPair = false;
        foreach (var edge in edges)
        {
            budget.Spend();
            if (edge.A == edge.B || !indexes.TryGetValue(edge.A, out int a) ||
                !indexes.TryGetValue(edge.B, out int b) ||
                !workspace.Required[a] && !workspace.Required[b]) continue;
            // Parallel edges have identical matching feasibility. Keep the first
            // exact payload, including its original orientation, deterministically.
            if (!originalEdges.TryAdd(PairKey(a, b), edge)) continue;
            graph[a].Add(b);
            graph[b].Add(a);
            hasRequiredPair |= workspace.Required[a] && workspace.Required[b];
        }
        // Branch arrivals usually form a bipartite graph: required branch axes
        // connect only to optional departure axes. Match only those requirements;
        // a dense optional clique and a perfect matching are unnecessary here.
        if (!hasRequiredPair)
        {
            if (!MatchBipartite(originalCount, workspace, budget)) return null;
            return ExtractMatching(originalCount, workspace, budget);
        }
        for (int a = 0; a < count; a++)
        {
            budget.Spend();
            if (workspace.Required[a]) continue;
            for (int b = a + 1; b < count; b++)
            {
                budget.Spend();
                if (workspace.Required[b]) continue;
                graph[a].Add(b);
                graph[b].Add(a);
            }
        }

        // A breadth-first alternating forest with blossom contraction finds an
        // augmenting path in O(V^2); at most V searches give O(V^3) total work.
        // See Edmonds, "Paths, Trees, and Flowers" (1965). All loops are charged
        // to the proof budget, so time/work limits and cancellation remain Unknown.
        int[] mate = workspace.Mate;
        for (int root = 0; root < count; root++)
        {
            budget.Spend();
            if (mate[root] >= 0) continue;
            if (!Augment(root, count, workspace, budget)) return null;
            // If a perfect matching exists, the symmetric difference with any
            // partial matching contains an augmenting path from every free vertex.
            // Therefore failure from even one root proves no perfect matching.
        }
        return ExtractMatching(originalCount, workspace, budget);
    }

    private static Dictionary<int, T> ExtractMatching<T>(int originalCount, Workspace<T> workspace, HcBudget budget)
    {
        var matching = workspace.Matching;
        for (int a = 0; a < originalCount; a++)
        {
            budget.Spend();
            int b = workspace.Mate[a];
            if (b < 0 || a > b || !workspace.Required[a] && !workspace.Required[b]) continue;
            HcMatchingEdge<T> edge = workspace.OriginalEdges[PairKey(a, b)];
            matching.Add(edge.A, edge.AtA);
            matching.Add(edge.B, edge.AtB);
        }
        return matching;
    }

    // Each BFS either augments the cover by one required vertex or proves that
    // the currently reached required set has too few optional neighbors. Earlier
    // assignments are reversible; a greedy choice can never cause a false failure.
    private static bool MatchBipartite<T>(int count, Workspace<T> workspace, HcBudget budget)
    {
        int[] mate = workspace.Mate, parent = workspace.Parent, queue = workspace.Queue;
        bool[] queued = workspace.Queued;
        for (int root = 0; root < count; root++)
        {
            budget.Spend();
            if (!workspace.Required[root]) continue;
            for (int i = 0; i < count; i++)
            {
                budget.Spend();
                parent[i] = -1;
                queued[i] = false;
            }
            int head = 0, tail = 0;
            queue[tail++] = root;
            queued[root] = true;
            bool augmented = false;
            while (head < tail && !augmented)
            {
                budget.Spend();
                int vertex = queue[head++];
                foreach (int neighbor in workspace.Neighbors[vertex])
                {
                    budget.Spend();
                    if (parent[neighbor] >= 0) continue;
                    parent[neighbor] = vertex;
                    int partner = mate[neighbor];
                    if (partner < 0)
                    {
                        int cursor = neighbor;
                        while (cursor >= 0)
                        {
                            budget.Spend();
                            int previous = parent[cursor];
                            int next = mate[previous];
                            mate[previous] = cursor;
                            mate[cursor] = previous;
                            cursor = next;
                        }
                        augmented = true;
                        break;
                    }
                    if (queued[partner]) continue;
                    queue[tail++] = partner;
                    queued[partner] = true;
                }
            }
            if (!augmented) return false;
        }
        return true;
    }

    private static ulong PairKey(int a, int b) =>
        ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);

    private static bool Augment<T>(int root, int count, Workspace<T> workspace, HcBudget budget)
    {
        int[] mate = workspace.Mate, parent = workspace.Parent, basis = workspace.Base, queue = workspace.Queue;
        bool[] queued = workspace.Queued, blossom = workspace.Blossom;
        for (int i = 0; i < count; i++)
        {
            budget.Spend();
            parent[i] = -1;
            basis[i] = i;
            queued[i] = false;
        }
        int head = 0, tail = 0;
        queue[tail++] = root;
        queued[root] = true;
        while (head < tail)
        {
            budget.Spend();
            int vertex = queue[head++];
            foreach (int neighbor in workspace.Neighbors[vertex])
            {
                budget.Spend();
                if (basis[vertex] == basis[neighbor] || mate[vertex] == neighbor) continue;
                if (neighbor == root || mate[neighbor] >= 0 && parent[mate[neighbor]] >= 0)
                {
                    int common = CommonAncestor(vertex, neighbor, workspace, budget, count);
                    Array.Clear(blossom, 0, count);
                    budget.Spend(count);
                    MarkPath(vertex, common, neighbor, workspace, budget);
                    MarkPath(neighbor, common, vertex, workspace, budget);
                    for (int i = 0; i < count; i++)
                    {
                        budget.Spend();
                        if (!blossom[basis[i]]) continue;
                        basis[i] = common;
                        if (queued[i]) continue;
                        queue[tail++] = i;
                        queued[i] = true;
                    }
                }
                else if (parent[neighbor] < 0)
                {
                    parent[neighbor] = vertex;
                    if (mate[neighbor] < 0)
                    {
                        // Parent rewrites performed by MarkPath expand contracted
                        // blossoms directly while flipping this augmenting path.
                        int cursor = neighbor;
                        while (cursor >= 0)
                        {
                            budget.Spend();
                            int previous = parent[cursor];
                            int next = previous < 0 ? -1 : mate[previous];
                            mate[cursor] = previous;
                            if (previous >= 0) mate[previous] = cursor;
                            cursor = next;
                        }
                        return true;
                    }
                    int partner = mate[neighbor];
                    if (!queued[partner])
                    {
                        queue[tail++] = partner;
                        queued[partner] = true;
                    }
                }
            }
        }
        return false;
    }

    private static int CommonAncestor<T>(int a, int b, Workspace<T> workspace, HcBudget budget, int count)
    {
        bool[] ancestors = workspace.Ancestor;
        Array.Clear(ancestors, 0, count);
        budget.Spend(count);
        while (true)
        {
            budget.Spend();
            a = workspace.Base[a];
            ancestors[a] = true;
            if (workspace.Mate[a] < 0) break;
            a = workspace.Parent[workspace.Mate[a]];
        }
        while (true)
        {
            budget.Spend();
            b = workspace.Base[b];
            if (ancestors[b]) return b;
            b = workspace.Parent[workspace.Mate[b]];
        }
    }

    private static void MarkPath<T>(int vertex, int common, int child, Workspace<T> workspace, HcBudget budget)
    {
        while (workspace.Base[vertex] != common)
        {
            budget.Spend();
            int partner = workspace.Mate[vertex];
            workspace.Blossom[workspace.Base[vertex]] = true;
            workspace.Blossom[workspace.Base[partner]] = true;
            workspace.Parent[vertex] = child;
            child = partner;
            vertex = workspace.Parent[partner];
        }
    }

    internal static Dictionary<int, T>? FindLegacy<T>(IEnumerable<int> vertices,
        IReadOnlyList<HcMatchingEdge<T>> edges, IReadOnlySet<int> required, HcBudget budget, Workspace<T> workspace)
    {
        vertices.TryGetNonEnumeratedCount(out int vertexCount);
        workspace.Reset(vertexCount, required.Count);
        var graph = workspace.Graph;
        int adjacencyIndex = 0;
        foreach (int vertex in vertices)
        {
            budget.Spend();
            graph.Add(vertex, workspace.Adjacency(adjacencyIndex++));
        }
        foreach (var edge in edges)
        {
            budget.Spend();
            if (edge.A == edge.B || !graph.ContainsKey(edge.A) || !graph.ContainsKey(edge.B)) continue;
            graph[edge.A].Add(edge);
            graph[edge.B].Add(edge);
        }
        foreach (int vertex in required)
        {
            budget.Spend();
            if (!graph.ContainsKey(vertex)) return null;
        }
        // A matching only needs pairs covering required vertices. In the common
        // all-fallback case, it stays empty and needs no backing allocation.
        var matching = workspace.Matching;
        int depth = 0;
        while (true)
        {
            budget.Spend();
            int selected = 0, fewest = int.MaxValue;
            bool incomplete = false, impossible = false;
            foreach (int vertex in required)
            {
                budget.Spend();
                if (matching.ContainsKey(vertex)) continue;
                incomplete = true;
                int available = 0;
                foreach (var edge in graph[vertex])
                {
                    budget.Spend();
                    if (!matching.ContainsKey(edge.A == vertex ? edge.B : edge.A)) ++available;
                }
                if (available == 0) { impossible = true; break; }
                if (available < fewest) { selected = vertex; fewest = available; }
            }
            if (!incomplete) return matching;
            if (!impossible) workspace.Push(depth++, graph[selected]);

            // Each frame remembers its next alternative and the pair that was
            // installed before descending. Undo that pair before trying the
            // next edge. The explicit stack has no matching-depth cutoff and
            // cannot overflow the CLR call stack on many-timeline positions.
            bool advanced = false;
            while (depth > 0)
            {
                Frame<T> frame = workspace.Peek(depth);
                budget.Spend();
                if (frame.Applied is { } previous)
                {
                    matching.Remove(previous.A);
                    matching.Remove(previous.B);
                    frame.Applied = null;
                }
                while (frame.Next < frame.Edges.Count)
                {
                    budget.Spend();
                    HcMatchingEdge<T> edge = frame.Edges[frame.Next++];
                    if (matching.ContainsKey(edge.A) || matching.ContainsKey(edge.B)) continue;
                    matching[edge.A] = edge.AtA;
                    matching[edge.B] = edge.AtB;
                    frame.Applied = edge;
                    advanced = true;
                    break;
                }
                if (advanced) break;
                depth--;
            }
            if (!advanced) return null;
        }
    }
}
