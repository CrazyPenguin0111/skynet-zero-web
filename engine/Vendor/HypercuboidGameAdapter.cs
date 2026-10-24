namespace SkynetZero;

// Engine implementations of the interfaces left as stubs in hcuboid-ts/imports.ts.
// Coordinates count half-turns (white T => 2*T, black T => 2*T+1). Raw timeline
// indexes preserve the engine's even-start handicap; present validation is delegated
// to the actual engine instead of assuming the upstream symmetric start formula.
internal sealed class HypercuboidGameAdapter
{
    [ThreadStatic] internal static bool? PresentSliceOptimizationOverride;
    [ThreadStatic] internal static bool? PresentDomainOptimizationOverride;
    [ThreadStatic] internal static bool? CastlingSliceOptimizationOverride;
    private readonly GameState original;
    private readonly HcBudget budget;
    private HcModel<Move> model = null!;

    internal HypercuboidGameAdapter(GameState original, HcBudget budget)
    {
        this.original = original;
        this.budget = budget;
    }

    internal HcSearchResult<Move> Search()
    {
        budget.Spend();
        // Existing attacks on immutable boards cannot be repaired by appending
        // more boards during this same turn. This also handles dead partial
        // continuations without misclassifying the whole original turn as mate.
        if (HasDirectThreat(original, original.Color, budget))
            return new(HcSearchStatus.Exhausted, null, budget.Work);
        if (RoyalLegality.PresentHasPassed(original))
            return new(HcSearchStatus.Found, Array.Empty<Move>(), budget.Work);
        if ((FastTurnWitness.OptimizationOverride ?? true) &&
            FastTurnWitness.TryFind(original, budget, out var witness))
            return new(HcSearchStatus.Found, witness, budget.Work);
        BuildSearchModel();
        return HcSearch<Move>.Search(model, FindProblem, budget);
    }

    // Search shares the exact inventory and certified rejection slices with the
    // proof adapter, but owns its own ordering, pruning and exhaustion semantics.
    // In particular a selectively exhausted search cursor is never a mate proof.
    internal HcModel<Move> BuildSearchModel()
    {
        int newTimeline = original.Color ? checked(original.MaxTL + 1) : checked(original.MinTL - 1);
        if (newTimeline == 0 || Math.Sign(newTimeline) != (original.Color ? 1 : -1))
            throw new NotSupportedException("This timeline numbering cannot represent branch ordering safely.");
        var playable = original.Multiverse.Where(line => line.ColorPlayable == original.Color)
            .ToDictionary(line => line.Layer, line => HalfTime(line.TEnd, line.ColorPlayable));
        var moves = new List<HcMove<Move>>();
        var seenMoves = new HashSet<string>(StringComparer.Ordinal);
        // The authoritative exhaustive inventory includes underpromotions, castling,
        // inactive playable timelines, and both directions of timeline travel. Its
        // private make/undo workspace also supplies exact successor board snapshots.
        GameLegalActions.TryEnumerate(original, () => { budget.Spend(); return false; }, out _,
            (move, simulation) =>
            {
                budget.Spend();
                if (Board.IsRoyalPiece(original.GetSquare(move.Dest))) return;
                if (!seenMoves.Add(CompleteTurnCandidates.ActionKey(move))) return;
                GameState successor = simulation.State;
                if (!RoyalLegality.IsLegalAfterMove(original, move, successor, budget)) return;
                Board sourceBoard = successor.GetTimeline(move.Origin.L)!.GetPlayableBoard();
                int destinationLayer = successor.MaxTL > original.MaxTL ? successor.MaxTL :
                    successor.MinTL < original.MinTL ? successor.MinTL : move.Dest.L;
                Board destinationBoard = successor.GetTimeline(destinationLayer)!.GetPlayableBoard();
                moves.Add(new(move.Clone(), Square(move.Origin), Square(move.Dest), sourceBoard, destinationBoard));
            });
        model = HcSearch<Move>.BuildHCs(playable, moves, newTimeline, budget);
        if (PresentDomainOptimizationOverride ?? true)
            model = model with { Cubes = RestrictPresentDomains(model) };
        return model;
    }

    // Tactical search's short evasion probe is measured in hypercuboids, not
    // low-level proof operations. A spent allowance is inconclusive; only an
    // exhausted complete inventory establishes that no evasion exists. The
    // normal mate/proof Search method above remains unlimited by this bound.
    internal HcSearchResult<Move> SearchBoundedCubes(int maximumCubes)
    {
        if (maximumCubes < 1) throw new ArgumentOutOfRangeException(nameof(maximumCubes));
        try
        {
            budget.Spend();
            if (HasDirectThreat(original, original.Color, budget))
                return new(HcSearchStatus.Exhausted, null, budget.Work);
            if (RoyalLegality.PresentHasPassed(original))
                return new(HcSearchStatus.Found, Array.Empty<Move>(), budget.Work);
            // A directly replayed legal spatial witness proves an evasion
            // without building every temporal coordinate on a large frontier.
            // Failure still falls through to the exact bounded cube search.
            if ((FastTurnWitness.OptimizationOverride ?? true) &&
                FastTurnWitness.TryFind(original, budget, out var witness))
                return new(HcSearchStatus.Found, witness, budget.Work);
            BuildSearchModel();
            var pending = new Stack<HcCube<Move>>(model.Cubes);
            var workspace = new HcSearch<Move>.PointWorkspace();
            int visited = 0;
            while (pending.TryPop(out HcCube<Move>? cube))
            {
                budget.Spend();
                if (visited++ >= maximumCubes)
                    return new(HcSearchStatus.Unknown, null, budget.Work);
                HcPoint<Move>? point = HcSearch<Move>.TakePoint(cube, budget, workspace);
                if (point == null) continue;
                HcSlice? problem = HcSearch<Move>.JumpOrderConsistent(point, model.Whole, model.NewTimeline, budget)
                    ?? FindProblem(point);
                if (problem == null)
                    return new(HcSearchStatus.Found, HcSearch<Move>.ToAction(point, model.NewTimeline), budget.Work);
                if (!problem.All(pair => point.TryGetValue(pair.Key, out var choice) && pair.Value.Contains(choice.Index)))
                    throw new InvalidOperationException("A rejection slice must contain the rejected evasion.");
                foreach (HcCube<Move> remainder in HcSearch<Move>.RemoveSlice(cube, problem, budget)) pending.Push(remainder);
            }
            return new(visited >= maximumCubes ? HcSearchStatus.Unknown : HcSearchStatus.Exhausted, null, budget.Work);
        }
        catch (HcBudgetExceededException)
        {
            return new(HcSearchStatus.Unknown, null, budget.Work);
        }
    }

    // Each starting cube fixes the number of new branches, hence its final
    // active bounds. Every non-pass frontier belongs to the opponent. Compute
    // the earliest time any such frontier could possibly have, without even
    // requiring its arrival/departure choices to match. An earlier own-color
    // frontier cannot remain a pass in any complete turn from this cube.
    // This is an over-approximation: impossible early arrivals may weaken the
    // restriction but cannot cause it to discard a legal completion.
    private IReadOnlyList<HcCube<Move>> RestrictPresentDomains(HcModel<Move> inventory)
    {
        var survivors = new List<HcCube<Move>>(inventory.Cubes.Count);
        int sign = Math.Sign(inventory.NewTimeline);
        foreach (HcCube<Move> cube in inventory.Cubes)
        {
            budget.Spend();
            int branches = 0;
            foreach (var (layer, axis) in cube)
            {
                budget.Spend();
                if ((long)layer * sign >= (long)inventory.NewTimeline * sign &&
                    axis.Values.Any(location => location.Kind == HcLocationKind.Arrival)) branches++;
            }
            // Use the engine's exact active-bound rules, including even starts
            // and timeline handicaps. No board or original timeline is changed.
            GameState bounds = original.Clone();
            bounds.TLHandicap = original.TLHandicap;
            if (sign > 0) bounds.MaxTL = checked(original.MaxTL + branches);
            else bounds.MinTL = checked(original.MinTL - branches);
            bounds.DetermineActiveTLS();
            int earliestOpponent = int.MaxValue;
            foreach (Timeline line in original.Multiverse)
            {
                budget.Spend();
                if (bounds.LayerIsActive(line.Layer) && line.ColorPlayable != original.Color)
                    earliestOpponent = Math.Min(earliestOpponent, HalfTime(line.TEnd, line.ColorPlayable));
            }
            foreach (var (layer, axis) in cube)
            {
                budget.Spend();
                if (!bounds.LayerIsActive(layer)) continue;
                foreach (HcLocation<Move> location in axis.Values)
                {
                    budget.Spend();
                    if (ResultTime(location) is { } time)
                        earliestOpponent = Math.Min(earliestOpponent, time);
                }
            }

            HcCube<Move>? restricted = null;
            bool impossible = false;
            foreach (var (layer, axis) in cube)
            {
                budget.Spend();
                if (!bounds.LayerIsActive(layer)) continue;
                foreach (var (index, location) in axis)
                {
                    budget.Spend();
                    if (location.Kind != HcLocationKind.Pass || location.PassBoard is not { } board ||
                        board.Time >= earliestOpponent) continue;
                    if (axis.Count == 1) { impossible = true; break; }
                    restricted ??= new HcCube<Move>(cube);
                    var withoutPass = new Dictionary<int, HcLocation<Move>>(axis);
                    withoutPass.Remove(index);
                    restricted[layer] = withoutPass;
                }
                if (impossible) break;
            }
            if (!impossible) survivors.Add(restricted ?? cube);
        }
        return survivors;
    }

    private HcSlice? FindProblem(HcPoint<Move> point) => ValidateSearchPoint(point, out _);

    internal HcSlice? ValidateSearchPoint(HcPoint<Move> point, out GameState successor)
    {
        budget.Spend();
        successor = original.Clone();
        foreach (Move action in HcSearch<Move>.ToAction(point, model.NewTimeline))
        {
            budget.Spend();
            Move move = action.Clone();
            // Source-frontier validation matters: MakeMove's path validator alone
            // permits coordinates on a historical board of a playable timeline.
            // validate:true also checks castling transit on this current prefix,
            // including temporal attacks exposed by its earlier atomic moves.
            if (!successor.CoordIsPlayable(move.Origin))
                return HcSearch<Move>.ExactPointSlice(point);
            if (!successor.MakeMove(move, validate: true))
                return (CastlingSliceOptimizationOverride ?? true)
                    ? CastlingFailureSlice(point, successor, action) ?? HcSearch<Move>.ExactPointSlice(point)
                    : HcSearch<Move>.ExactPointSlice(point);
            if (successor.kingWasTaken) return HcSearch<Move>.ExactPointSlice(point);
            if (!RoyalLegality.IsLegalAfterMove(original, action, successor, budget))
            {
                Move? immutableThreat = FindDirectThreat(successor, original.Color, budget);
                // A check introduced by an earlier atomic move cannot be fixed
                // by the remaining moves. Its certified path still removes a
                // whole slice of turn combinations, as in hcuboid-ts.
                return immutableThreat == null ? HcSearch<Move>.ExactPointSlice(point) :
                    CheckSlice(point, successor, immutableThreat);
            }
        }
        // This is the real engine's complete-turn rule, including partial turns,
        // newly activated branches, black half-turns, and even-start variants.
        if (successor.CalcPresent() == original.Color)
            return (PresentSliceOptimizationOverride ?? true) ? IncompletePresentSlice(point, successor) :
                HcSearch<Move>.ExactPointSlice(point);

        Move? threat = FindDirectThreat(successor, original.Color, budget);
        return threat == null ? null : CheckSlice(point, successor, threat);
    }

    // A still-unmoved mandatory board can invalidate a whole family of turns,
    // not just this one Cartesian-product point. Fix the branch count so the
    // active bounds are fixed too; the individual branch destinations may vary.
    private HcSlice IncompletePresentSlice(HcPoint<Move> point, GameState successor)
    {
        int sign = Math.Sign(model.NewTimeline);
        int present = HalfTime(successor.Present, original.Color);
        int? anchor = null;
        foreach (var (layer, choice) in point)
            if (successor.LayerIsActive(layer) && choice.Location.Kind == HcLocationKind.Pass &&
                choice.Location.PassBoard is { } board && board.Time == present)
            { anchor = layer; break; }
        if (anchor == null) return HcSearch<Move>.ExactPointSlice(point);
        var slice = new HcSlice();
        foreach (var (layer, axis) in model.Whole)
        {
            budget.Spend();
            bool branch = (long)layer * sign >= (long)model.NewTimeline * sign;
            bool active = successor.LayerIsActive(layer);
            if (!branch && !active) continue;
            if (!point.TryGetValue(layer, out var selected))
                return HcSearch<Move>.ExactPointSlice(point);
            int[] indexes = axis.Where(pair =>
            {
                budget.Spend();
                var location = pair.Value;
                // Matching and jump-order validation ensure every Arrival on a
                // branch axis creates exactly that branch. Fixing Arrival/Pass
                // therefore preserves both extrema and all active timelines.
                if (branch)
                {
                    if (location.Kind != selected.Location.Kind) return false;
                    if (location.Kind == HcLocationKind.Pass || !active) return true;
                }
                int? time = location.Kind == HcLocationKind.Pass ? location.PassBoard?.Time : ResultTime(location);
                return time.HasValue && (layer == anchor ? time == present : time >= present);
            }).Select(pair => pair.Key).ToArray();
            if (!indexes.Contains(selected.Index))
                return HcSearch<Move>.ExactPointSlice(point);
            slice.Add(layer, indexes);
        }
        return slice;
    }

    // A castle may be legal in the original inventory but become illegal after
    // an earlier atomic move exposes a temporal attack on its starting/transit
    // square. Rejecting the entire Cartesian point would repeatedly rediscover
    // that same attack while varying unrelated boards. Reconstruct the three
    // king boards used by the castle validator and retain the certified path.
    private HcSlice? CastlingFailureSlice(HcPoint<Move> point, GameState prefix, Move castle)
    {
        if (!MoveGenerator.IsCastlingMove(prefix.GetSquare(castle.Origin), castle) ||
            !point.TryGetValue(castle.Origin.L, out var selected) ||
            selected.Location.Kind != HcLocationKind.Physical)
            return null;
        for (int step = 0; step <= 2; step++)
        {
            budget.Spend();
            GameState trial = prefix.Clone();
            if (step == 0)
                trial.GetTimeline(castle.Origin.L)!.AddNullMove();
            else
            {
                Move move = castle.Clone();
                if (step == 1)
                {
                    move.Dest.X = move.Origin.X + Math.Sign(move.Dest.X - move.Origin.X);
                    move.SpecialType = Move.NORMALMOVE;
                }
                if (!trial.MakeSilentMove(move)) continue;
            }
            Move? threat = FindDirectThreat(trial, original.Color, budget);
            if (threat != null) return CheckSlice(point, trial, threat, castle.Origin.L);
        }
        return null;
    }

    private HcSlice CheckSlice(HcPoint<Move> point, GameState successor, Move threat,
        int? castlingLayer = null)
    {
        var slice = new HcSlice();
        // The temporary castling board differs from the final inventory board.
        // Fix its exact castle choice; the immutable original source board and
        // that choice reproduce the same starting/transit/destination trial.
        if (castlingLayer is { } castleLayer)
            slice[castleLayer] = new[] { point[castleLayer].Index };
        foreach (CoordFive coordinate in CheckPath(successor, threat))
        {
            budget.Spend();
            // Original boards are frozen. Only new boards can change this attack
            // path; intersect the locations that reproduce every path square.
            if (original.GetBoard(coordinate) != null) continue;
            if (coordinate.L == castlingLayer) continue;
            if (!model.Whole.TryGetValue(coordinate.L, out var axis))
                return HcSearch<Move>.ExactPointSlice(point);
            if (castlingLayer is { } castleAt && point.TryGetValue(coordinate.L, out var pathChoice) &&
                pathChoice.Location.Kind == HcLocationKind.Departure)
            {
                // A departure board is created by its matching arrival action,
                // not when its source axis is encountered. Fix both that source
                // choice and a matching arrival that executes before the castle.
                if (ResultTime(pathChoice.Location) != HalfTime(coordinate.T, coordinate.Color) ||
                    pathChoice.Location.Board is not Board departureBoard ||
                    departureBoard.GetSquareSafe(coordinate.X, coordinate.Y) != successor.GetSquare(coordinate) ||
                    !ConstrainDepartureBeforeCastle(point, slice, coordinate.L, pathChoice, castleAt))
                    return HcSearch<Move>.ExactPointSlice(point);
                continue;
            }
            // Physical/arrival path boards are created on their own axes, which
            // must precede the castle. A final board created later is no witness
            // to an attack on the king's starting or transit square.
            if (castlingLayer is { } castle &&
                (long)coordinate.L * Math.Sign(model.NewTimeline) >= (long)castle * Math.Sign(model.NewTimeline))
                return HcSearch<Move>.ExactPointSlice(point);
            int piece = successor.GetSquare(coordinate);
            IEnumerable<int> candidates = slice.TryGetValue(coordinate.L, out int[]? previous)
                ? previous : axis.Keys;
            int time = HalfTime(coordinate.T, coordinate.Color);
            int[] equivalent = candidates.Where(index =>
            {
                budget.Spend();
                HcLocation<Move> location = axis[index];
                return (castlingLayer == null || location.Kind is HcLocationKind.Physical or HcLocationKind.Arrival) &&
                    ResultTime(location) == time && location.Board is Board board &&
                    board.GetSquareSafe(coordinate.X, coordinate.Y) == piece;
            }).ToArray();
            if (!point.TryGetValue(coordinate.L, out var selected) || !equivalent.Contains(selected.Index))
                return HcSearch<Move>.ExactPointSlice(point);
            slice[coordinate.L] = equivalent;
        }
        // Empty means the entire attack is on frozen original boards. No move in
        // any candidate turn can remove it, so removing the whole cube is sound.
        return slice;
    }

    private bool ConstrainDepartureBeforeCastle(HcPoint<Move> point, HcSlice slice, int sourceLayer,
        HcChoice<Move> departure, int castleLayer)
    {
        HcSquare? source = departure.Location.Source;
        int? arrivalLayer = null;
        foreach (var (layer, choice) in point)
        {
            budget.Spend();
            if (choice.Location.Kind != HcLocationKind.Arrival || choice.Location.Move!.Source != source ||
                choice.Location.DepartureIndex != departure.Index) continue;
            // A valid matching has exactly one arrival for this departure.
            // Do not generalize malformed or ambiguous points.
            if (arrivalLayer != null) return false;
            arrivalLayer = layer;
        }
        if (arrivalLayer is not { } support ||
            (long)support * Math.Sign(model.NewTimeline) >= (long)castleLayer * Math.Sign(model.NewTimeline))
            return false;
        if (slice.TryGetValue(sourceLayer, out var sources) && !sources.Contains(departure.Index)) return false;
        var axis = model.Whole[support];
        IEnumerable<int> candidates = slice.TryGetValue(support, out var previous) ? previous : axis.Keys;
        int[] equivalent = candidates.Where(index =>
        {
            budget.Spend();
            var location = axis[index];
            // Varying the destination preserves this exact departure board.
            // Other attack-path squares still constrain their own axes, and
            // matching/jump-order validation remains authoritative.
            return location.Kind == HcLocationKind.Arrival && location.Move!.Source == source &&
                location.DepartureIndex == departure.Index;
        }).ToArray();
        if (!equivalent.Contains(point[support].Index)) return false;
        slice[sourceLayer] = [departure.Index];
        slice[support] = equivalent;
        return true;
    }

    private static int? ResultTime(HcLocation<Move> location) => location.Kind switch
    {
        HcLocationKind.Physical => location.Move!.Source.Time + 1,
        HcLocationKind.Arrival => location.Move!.Destination.Time + 1,
        HcLocationKind.Departure => location.Source!.Value.Time + 1,
        _ => null
    };

    internal static bool IsInCheck(GameState privateState)
    {
        privateState.DetermineActiveTLS();
        bool presentColor = privateState.CalcPresent();
        int present = HalfTime(privateState.Present, presentColor);
        // Expose the attacks that would exist if current present boards were
        // passed. Future optional boards must not be advanced speculatively.
        foreach (Timeline line in privateState.Multiverse)
            if (line.ColorPlayable == privateState.Color && privateState.LayerIsActive(line.Layer) &&
                HalfTime(line.TEnd, line.ColorPlayable) == present)
                line.AddNullMove();
        return FindDirectThreat(privateState, privateState.Color, null) != null;
    }

    internal static bool HasDirectThreat(GameState state, bool defender, HcBudget? budget = null) =>
        FindDirectThreat(state, defender, budget) != null;

    internal static Move? FindDirectThreat(GameState state, bool defender, HcBudget? budget = null, List<Move>? threats = null)
    {
        foreach (Timeline line in state.Multiverse)
        {
            budget?.Spend();
            if (line.ColorPlayable == defender) continue;
            Board board = line.GetPlayableBoard();
            for (int x = 0; x < state.Width; x++)
            for (int y = 0; y < state.Height; y++)
            {
                budget?.Spend();
                int piece = board.GetSquareSafe(x, y);
                if (piece == Board.EMPTYSQUARE || Board.GetColorBool(piece) == defender) continue;
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
                // Retain the old scan order and budget charges, but never build
                // capture lists or coordinates for the many non-royal targets.
                foreach (CoordFive vector in vectors)
                {
                    int dx = x + vector.X, dy = y + vector.Y;
                    int time = line.TEnd + vector.T, layer = line.Layer + vector.L;
                    bool spatial = vector.IsSpatial();
                    while (true)
                    {
                        int target = spatial ? board.GetSquareSafe(dx, dy) : AttackSquare(state, dx, dy, time, layer, !defender);
                        if (target == Board.ERRORSQUARE) break;
                        if (target != Board.EMPTYSQUARE)
                        {
                            if (Board.GetColorBool(target) == defender && Board.IsRoyalPiece(target))
                            {
                                var threat = new Move(new(x, y, line.TEnd, line.Layer, !defender), new(dx, dy, time, layer, !defender));
                                if (threats == null) return threat;
                                // The browser preview collects every check using the same
                                // attack scan; search retains its original early exit.
                                threats.Add(threat);
                            }
                            break;
                        }
                        if (!rider) break;
                        dx += vector.X; dy += vector.Y; time += vector.T; layer += vector.L;
                    }
                }
            }
        }
        return threats?.FirstOrDefault();
    }

    private static int AttackSquare(GameState state, int x, int y, int time, int layer, bool white)
    {
        if (x < 0 || x >= state.Width || y < 0 || y >= state.Height || layer < state.MinTL || layer > state.MaxTL)
            return Board.ERRORSQUARE;
        Timeline line = state.Multiverse[layer - state.MinTL];
        if (white)
            return time < line.WhiteStart || time > line.WhiteEnd ? Board.ERRORSQUARE :
                line.WBoards[time - line.WhiteStart].GetSquare(x, y);
        return time < line.BlackStart || time > line.BlackEnd ? Board.ERRORSQUARE :
            line.BBoards[time - line.BlackStart].GetSquare(x, y);
    }

    private static IEnumerable<CoordFive> CheckPath(GameState state, Move threat)
    {
        yield return threat.Origin;
        if (MoveGenerator.PieceIsRider(state.GetSquare(threat.Origin)))
        {
            CoordFive direction = CoordFive.Sub(threat.Dest, threat.Origin);
            direction.Flatten();
            CoordFive current = CoordFive.Add(threat.Origin, direction);
            while (!current.EqualsFull(threat.Dest))
            {
                yield return current.Clone();
                current.Add(direction);
            }
        }
        yield return threat.Dest;
    }

    internal static int HalfTime(int time, bool white) => checked(time * 2 + (white ? 0 : 1));
    internal static HcSquare Square(CoordFive coordinate) =>
        new(coordinate.L, HalfTime(coordinate.T, coordinate.Color), coordinate.X, coordinate.Y);
}
