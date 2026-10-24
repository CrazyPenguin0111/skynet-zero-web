namespace SkynetZero;

// Positional evaluation and selection hints. The opt-in heuristic-fusion value
// model also consumes the component values through its versioned input contract.
// Cache immutable frontier-board analysis for one decision, never across games.
internal sealed class PositionHeuristics
{
    [ThreadStatic] internal static SelfPlayProfile? Profile;
    // Study override retains the original iterator/LINQ board analysis for
    // matched benchmarks. Null selects the exact direct-loop implementation.
    [ThreadStatic] internal static bool? OptimizedBoardAnalysisOverride;
    // Study override exposes the ignored-origin royal-ray candidate for matched
    // benchmarks. It missed the whole-search wall gate; null retains the board
    // clone used by the production path.
    [ThreadStatic] internal static bool? OptimizedRoyalPressureOverride;
    // Study override for sharing the already-validated StateInfo across the
    // five component reads in one static Evaluate call. Null stays original
    // until the full-wall exactness gate admits the candidate.
    [ThreadStatic] internal static bool? OptimizedStaticSummaryOverride;
    // Differential/benchmark switch. Production reuses exact immutable-board
    // strategic analysis within this evaluator's single decision lifetime.
    [ThreadStatic] internal static bool? StrategicBoardCacheOverride;
    internal const float DevelopmentWeight = .012f;
    internal const float MobilityWeight = .0005f;
    internal const float RoyalPressureWeight = .04f;
    private readonly Dictionary<Board, BoardInfo> boards = new();
    private readonly Dictionary<GameState, StateInfo> states = new();
    private readonly bool optimizedBoardAnalysis = OptimizedBoardAnalysisOverride ?? true;
    private readonly bool optimizedRoyalPressure = OptimizedRoyalPressureOverride ?? false;
    private readonly bool optimizedStaticSummary = OptimizedStaticSummaryOverride ?? false;
    private readonly StrategicHeuristics.Cache? strategicBoards = (StrategicBoardCacheOverride ?? true) ? new() : null;

    private sealed class StateInfo
    {
        private readonly record struct Frontier(Timeline Line, Board Board, int WhiteCount, int BlackCount);
        private Frontier[] frontier = [];
        private int present, min, max, minActive, maxActive;
        private bool color, evenStart, kingWasTaken, kingWasTakenByColor;
        internal float? WhiteHanging, BlackHanging, WhitePressure, BlackPressure, WhiteActivity, BlackActivity;
        internal float? WhiteStrategic;
        internal StrategicHeuristics.Terms? WhiteStrategicTerms;

        internal void Refresh(GameState current)
        {
            if (frontier.Length != current.Multiverse.Count) frontier = new Frontier[current.Multiverse.Count];
            for (int i = 0; i < frontier.Length; i++)
            {
                var line = current.Multiverse[i];
                frontier[i] = new(line, line.GetPlayableBoard(), line.WBoards.Count, line.BBoards.Count);
            }
            present = current.Present; min = current.MinTL; max = current.MaxTL;
            minActive = current.MinActiveTL; maxActive = current.MaxActiveTL; color = current.Color;
            evenStart = current.EvenStart;
            kingWasTaken = current.kingWasTaken; kingWasTakenByColor = current.kingWasTakenByColor;
            WhiteHanging = BlackHanging = WhitePressure = BlackPressure = WhiteActivity = BlackActivity = null;
            WhiteStrategic = null;
            WhiteStrategicTerms = null;
        }

        internal StateInfo(GameState state) => Refresh(state);

        internal bool Matches(GameState current)
        {
            if (current.Color != color || current.Present != present || current.MinTL != min ||
                current.MaxTL != max || current.EvenStart != evenStart || current.kingWasTaken != kingWasTaken ||
                current.kingWasTakenByColor != kingWasTakenByColor ||
                current.MinActiveTL != minActive || current.MaxActiveTL != maxActive || current.Multiverse.Count != frontier.Length)
                return false;
            for (int i = 0; i < frontier.Length; i++)
            {
                var line = current.Multiverse[i]; var saved = frontier[i];
                if (!ReferenceEquals(line, saved.Line) || !ReferenceEquals(line.GetPlayableBoard(), saved.Board) ||
                    line.WBoards.Count != saved.WhiteCount || line.BBoards.Count != saved.BlackCount) return false;
            }
            return true;
        }
    }

    private StateInfo Summary(GameState state)
    {
        // CandidateSimulation reuses a mutable GameState. A state-reference key
        // alone would reuse a previous candidate's score after make/undo.
        if (!states.TryGetValue(state, out var info) || !info.Matches(state))
        {
            if (info == null) states[state] = info = new StateInfo(state);
            else info.Refresh(state);
        }
        return info;
    }

    private sealed class BoardInfo
    {
        internal readonly bool[][] Attacks;
        internal readonly float[] Material = new float[2], Hanging = new float[2], Activity = new float[2];
        internal readonly int[] Undeveloped = new int[2];
        internal readonly float Phase;

        internal BoardInfo(Board board, bool optimized)
        {
            Attacks = [new bool[board.Brd.Length], new bool[board.Brd.Length]];
            float pawnMaterial;
            if (optimized) pawnMaterial = PopulateDirect(board);
            else
            {
                PopulateOriginal(board);
                pawnMaterial = board.Brd.Where(p => p != 0 && IsPawn(Type(p))).Sum(p => Board.GetPieceValue(p));
            }
            Phase = Math.Clamp((Material.Sum() - pawnMaterial) / 79.2f, 0, 1);
            PopulateDerived(board, optimized);
        }

        private void PopulateOriginal(Board board)
        {
            for (int square = 0; square < board.Brd.Length; square++)
            {
                int piece = board.Brd[square];
                if (piece == Board.EMPTYSQUARE) continue;
                int color = Board.GetColorBool(piece) ? 0 : 1;
                float value = Board.GetPieceValue(piece);
                if (float.IsFinite(value)) Material[color] += value;
                foreach (int target in Destinations(board, square, piece)) Attacks[color][target] = true;
                int type = Type(piece), home = color == 0 ? 0 : board.Height - 1;
                if (IsMinor(type) && square / board.Width == home) Undeveloped[color]++;
            }
        }

        private float PopulateDirect(Board board)
        {
            float pawnMaterial = 0;
            for (int square = 0; square < board.Brd.Length; square++)
            {
                int piece = board.Brd[square];
                if (piece == Board.EMPTYSQUARE) continue;
                int color = Board.GetColorBool(piece) ? 0 : 1, type = Type(piece);
                float value = Board.GetPieceValue(piece);
                if (float.IsFinite(value)) Material[color] += value;
                if (IsPawn(type)) pawnMaterial += value;
                AddDestinations(board, square, piece, Attacks[color]);
                int home = color == 0 ? 0 : board.Height - 1;
                if (IsMinor(type) && square / board.Width == home) Undeveloped[color]++;
            }
            return pawnMaterial;
        }

        private void PopulateDerived(Board board, bool optimized)
        {
            for (int square = 0; square < board.Brd.Length; square++)
            {
                int piece = board.Brd[square];
                if (piece == Board.EMPTYSQUARE) continue;
                int color = Board.GetColorBool(piece) ? 0 : 1, type = Type(piece);
                if (!Board.IsRoyalPiece(piece) && Attacks[1 - color][square])
                    Hanging[color] = Math.Max(Hanging[color], StaticExchange.Gain(board, square % board.Width, square / board.Width, color != 0));
                if (IsMinor(type) && square / board.Width != (color == 0 ? 0 : board.Height - 1))
                    Activity[color] += DevelopmentWeight * Phase;
                if (IsPawn(type) || Board.IsRoyalPiece(piece)) continue;
                int safe = optimized
                    ? CountSafeDestinations(board, square, piece, Attacks[1 - color], color == 0)
                    : Destinations(board, square, piece).Count(target => !Attacks[1 - color][target] &&
                        (board.Brd[target] == 0 || Board.GetColorBool(board.Brd[target]) != (color == 0)));
                Activity[color] += Math.Min(safe, 12) * MobilityWeight;
            }
        }
    }

    private BoardInfo Info(Board board)
    {
        if (!boards.TryGetValue(board, out var info)) boards[board] = info = new BoardInfo(board, optimizedBoardAnalysis);
        return info;
    }

    private static int Type(int piece) => (Math.Abs(piece) - 1) % Board.NUMTYPES + 1;
    private static bool IsPawn(int type) => type == (int)Board.Piece.WPAWN || type == (int)Board.Piece.WBRAWN;
    private static bool IsMinor(int type) => type == (int)Board.Piece.WKNIGHT || type == (int)Board.Piece.WBISHOP;

    // Geometric attack destinations, including a friendly blocker (a defended
    // square). Pawn pushes are deliberately not mistaken for pawn attacks.
    private static IEnumerable<int> Destinations(Board board, int square, int piece)
    {
        CoordFive[] vectors = Math.Abs(piece) switch
        {
            (int)Board.Piece.WPAWN => MoveGenerator.whitePawnAttack,
            (int)Board.Piece.BPAWN => MoveGenerator.blackPawnattack,
            (int)Board.Piece.WBRAWN => MoveGenerator.whiteBrawnattack,
            (int)Board.Piece.BBRAWN => MoveGenerator.blackBrawnattack,
            _ => MoveGenerator.GetMoveVectors(piece)
        };
        bool rider = MoveGenerator.PieceIsRider(piece);
        foreach (var vector in vectors)
        {
            if (!vector.IsSpatial()) continue;
            int x = square % board.Width + vector.X, y = square / board.Width + vector.Y;
            while (x >= 0 && x < board.Width && y >= 0 && y < board.Height)
            {
                int target = y * board.Width + x;
                yield return target;
                if (!rider || board.Brd[target] != Board.EMPTYSQUARE) break;
                x += vector.X; y += vector.Y;
            }
        }
    }

    private static CoordFive[] SpatialVectors(int piece) => Math.Abs(piece) switch
    {
        (int)Board.Piece.WPAWN => MoveGenerator.whitePawnAttack,
        (int)Board.Piece.BPAWN => MoveGenerator.blackPawnattack,
        (int)Board.Piece.WBRAWN => MoveGenerator.whiteBrawnattack,
        (int)Board.Piece.BBRAWN => MoveGenerator.blackBrawnattack,
        _ => MoveGenerator.GetMoveVectors(piece)
    };

    private static void AddDestinations(Board board, int square, int piece, bool[] attacks)
    {
        bool rider = MoveGenerator.PieceIsRider(piece);
        foreach (var vector in SpatialVectors(piece))
        {
            if (!vector.IsSpatial()) continue;
            int x = square % board.Width + vector.X, y = square / board.Width + vector.Y;
            while (x >= 0 && x < board.Width && y >= 0 && y < board.Height)
            {
                int target = y * board.Width + x;
                attacks[target] = true;
                if (!rider || board.Brd[target] != Board.EMPTYSQUARE) break;
                x += vector.X; y += vector.Y;
            }
        }
    }

    private static int CountSafeDestinations(Board board, int square, int piece, bool[] opponentAttacks, bool white)
    {
        int safe = 0;
        bool rider = MoveGenerator.PieceIsRider(piece);
        foreach (var vector in SpatialVectors(piece))
        {
            if (!vector.IsSpatial()) continue;
            int x = square % board.Width + vector.X, y = square / board.Width + vector.Y;
            while (x >= 0 && x < board.Width && y >= 0 && y < board.Height)
            {
                int target = y * board.Width + x;
                if (!opponentAttacks[target] && (board.Brd[target] == 0 || Board.GetColorBool(board.Brd[target]) != white)) safe++;
                if (!rider || board.Brd[target] != Board.EMPTYSQUARE) break;
                x += vector.X; y += vector.Y;
            }
        }
        return safe;
    }

    internal static Timeline Destination(GameState source, GameState successor, Move move) =>
        successor.GetTimeline(successor.MinTL < source.MinTL ? successor.MinTL :
            successor.MaxTL > source.MaxTL ? successor.MaxTL : move.Dest.L)!;

    internal float Hanging(GameState state, bool defender) => Hanging(state, defender, Summary(state));

    private float Hanging(GameState state, bool defender, StateInfo summary)
    {
        ref float? cached = ref (defender ? ref summary.WhiteHanging : ref summary.BlackHanging);
        if (cached is { } known) return known;
        float loss = 0;
        foreach (var line in state.Multiverse)
            if (state.LayerIsActive(line.Layer))
            {
                var board = line.GetPlayableBoard();
                var info = Info(board);
                loss = Math.Max(loss, info.Hanging[defender ? 0 : 1]);
                if (state.Multiverse.Count < 2) continue;
                for (int square = 0; square < board.Brd.Length; square++)
                {
                    int piece = board.Brd[square], color = defender ? 0 : 1;
                    if (piece == 0 || Board.IsRoyalPiece(piece) || Board.GetColorBool(piece) != defender ||
                        info.Attacks[color][square] || info.Attacks[1 - color][square]) continue;
                    float risk = Board.GetPieceValue(piece) * .5f;
                    if (risk <= loss) continue;
                    var target = new CoordFive(square % board.Width, square / board.Width, line.TEnd, line.Layer, line.ColorPlayable);
                    // An unprotected temporal target must not disappear from the
                    // heuristic just because spatial SEE finds no attacker. This
                    // discounted geometric risk triggers real-move tactical search;
                    // it is not a claimed forced exchange across histories.
                    if (MoveGenerator.IsSquareAttacked(state, target, !defender)) loss = risk;
                }
            }
        // One worst threatened piece, rather than adding mutually exclusive captures.
        cached = loss;
        return loss;
    }

    internal float MaterialTrade(GameState source, GameState successor, Move move)
    {
        float captured = Board.GetPieceValue(Program.GetCapturedPiece(source, move));
        if (float.IsPositiveInfinity(captured)) return captured;
        var destination = Destination(source, successor, move);
        var board = destination.GetPlayableBoard();
        var target = new CoordFive(move.Dest.X, move.Dest.Y, destination.TEnd, destination.Layer, destination.ColorPlayable);
        int moved = source.GetSquare(move.Origin);
        if (Board.IsRoyalPiece(moved) && MoveGenerator.IsSquareAttacked(successor, target, !move.Origin.Color))
            return float.NegativeInfinity;
        // Ordinary generated pawn moves auto-promote in Timeline.AddSpatialMove.
        float promotion = IsPawn(Type(moved))
            ? Board.GetPieceValue(board.GetSquare(target)) - Board.GetPieceValue(moved) : 0;
        float hanging = Math.Max(Hanging(successor, move.Origin.Color), Info(board).Hanging[move.Origin.Color ? 0 : 1]);
        return captured + promotion - hanging;
    }

    internal static bool Promotes(GameState state, Move move) =>
        move.SpecialType == Move.PROMOTION || move.Type == Move.SPATIALMOVE &&
        IsPawn(Type(state.GetSquare(move.Origin))) && move.Dest.Y == (move.Origin.Color ? state.Height - 1 : 0);

    internal bool RelievesThreat(GameState source, GameState successor, bool defender) =>
        Hanging(source, defender) > Hanging(successor, defender) + .25f;

    internal float PositionalChange(GameState source, GameState successor, Move move)
    {
        bool mover = move.Origin.Color;
        float change = Activity(successor, mover) - Activity(source, mover);
        // Prefer bringing the remaining minor pieces into play. This also catches
        // repeated queen/rook excursions separated by moves of other pieces.
        int type = Type(source.GetSquare(move.Origin));
        var origin = source.GetTimeline(move.Origin.L)!.GetPlayableBoard();
        int undeveloped = Info(origin).Undeveloped[mover ? 0 : 1];
        if (undeveloped > 0 && !IsPawn(type) && !Board.IsRoyalPiece(source.GetSquare(move.Origin)) &&
            move.Origin.Y != (mover ? 0 : origin.Height - 1) && Program.GetCapturedPiece(source, move) == 0 &&
            move.SpecialType == Move.NORMALMOVE && !RelievesThreat(source, successor, mover) &&
            !MoveGenerator.IsSquareAttacked(source, move.Origin, !mover) &&
            !MoveGenerator.HasCurrentThreats(source, mover) && !MoveGenerator.HasCurrentThreats(successor, !mover))
            change -= .006f * Info(origin).Phase * Math.Min(undeveloped, 4) / 4;
        return Math.Clamp(change, -.025f, .025f);
    }

    private float Activity(GameState state, bool perspective) => Activity(state, perspective, Summary(state));

    private float Activity(GameState state, bool perspective, StateInfo summary)
    {
        ref float? cached = ref (perspective ? ref summary.WhiteActivity : ref summary.BlackActivity);
        if (cached is { } known) return known;
        float total = 0; int count = 0, color = perspective ? 0 : 1;
        foreach (var line in state.Multiverse)
        {
            if (!state.LayerIsActive(line.Layer)) continue;
            var info = Info(line.GetPlayableBoard());
            total += info.Activity[color] - info.Activity[1 - color]; count++;
        }
        float result = count == 0 ? 0 : total / count;
        cached = result;
        return result;
    }

    internal float RoyalPressure(GameState state, bool defender) => RoyalPressure(state, defender, Summary(state));

    private float RoyalPressure(GameState state, bool defender, StateInfo summary)
    {
        ref float? cached = ref (defender ? ref summary.WhitePressure : ref summary.BlackPressure);
        if (cached is { } known) return known;
        float worst = 0;
        foreach (var line in state.Multiverse)
        {
            if (!state.LayerIsActive(line.Layer)) continue;
            var board = line.GetPlayableBoard();
            for (int square = 0; square < board.Brd.Length; square++)
            {
                int piece = board.Brd[square];
                if (!Board.IsRoyalPiece(piece) || Board.GetColorBool(piece) != defender) continue;
                int x = square % board.Width, y = square / board.Width;
                var royal = new CoordFive(x, y, line.TEnd, line.Layer, line.ColorPlayable);
                float pressure = MoveGenerator.IsSquareAttacked(state, royal, !defender) ? 1 : 0;
                int open = 0, unsafeSquares = 0;
                // Vacate the royal before checking spatial escapes: otherwise it
                // can hide a rook/bishop ray through its own square. The direct
                // path treats only that origin as empty and avoids a board clone.
                Board? vacated = null;
                if (!optimizedRoyalPressure)
                {
                    vacated = new Board(board);
                    vacated.SetSquare(royal, Board.EMPTYSQUARE);
                }
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 || x + dx < 0 || x + dx >= board.Width || y + dy < 0 || y + dy >= board.Height) continue;
                    var target = new CoordFive(x + dx, y + dy, line.TEnd, line.Layer, line.ColorPlayable);
                    int occupant = board.GetSquare(target);
                    bool attacked = (optimizedRoyalPressure
                            ? MoveGenerator.IsSquareAttacked(board, target, !defender, square)
                            : MoveGenerator.IsSquareAttacked(vacated!, target, !defender)) ||
                        state.Multiverse.Count > 1 && MoveGenerator.IsSquareAttacked(state, target, !defender);
                    if (attacked) unsafeSquares++;
                    else if (occupant == 0 || Board.GetColorBool(occupant) != defender) open++;
                }
                // This is geometric pressure, not a claim of checkmate or a count
                // of legal 5D escape moves. Distant royal-queen moves remain legal.
                pressure += unsafeSquares / 16f + (open == 0 ? .125f : 0);
                worst = Math.Max(worst, pressure);
            }
        }
        float result = worst * RoyalPressureWeight;
        cached = result;
        return result;
    }

    internal float RoyalChange(GameState source, GameState successor, bool mover) =>
        Math.Clamp(RoyalPressure(source, mover) - RoyalPressure(successor, mover) +
            RoyalPressure(successor, !mover) - RoyalPressure(source, !mover), -.08f, .08f);

    internal double MoveScore(GameState source, GameState successor, Move move)
    {
        float material;
        using (Profile?.Measure("heuristic-material")) material = MaterialTrade(source, successor, move);
        float positional;
        using (Profile?.Measure("heuristic-positional")) positional = PositionalChange(source, successor, move);
        float royal;
        using (Profile?.Measure("heuristic-royal")) royal = RoyalChange(source, successor, move.Origin.Color);
        double timeline;
        using (Profile?.Measure("heuristic-timeline")) timeline = Program.GetTimelineCreationPenalty(source, successor);
        return (float.IsPositiveInfinity(material) ? 1 : float.IsNegativeInfinity(material) ? -1 : material / Program.MaximumFinitePieceValue)
            + positional + royal - timeline + Math.Clamp(Strategic(successor, move.Origin.Color) -
                Strategic(source, move.Origin.Color), -.04f, .04f);
    }

    private float Strategic(GameState state, bool perspective)
    {
        StateInfo summary = Summary(state);
        float white = summary.WhiteStrategic ??= StrategicTerms(state, true).Total;
        return perspective ? white : -white;
    }

    internal StrategicHeuristics.Terms StrategicTerms(GameState state, bool perspective)
    {
        var summary = Summary(state);
        var white = summary.WhiteStrategicTerms ??= strategicBoards == null
            ? StrategicHeuristics.Evaluate(state, true) : StrategicHeuristics.Evaluate(state, true, strategicBoards);
        return perspective ? white : new(-white.Pawns, -white.Centrality, -white.Shelter,
            -white.NeighborPressure, -white.TimelineReserve, -white.InactiveMaterial, -white.ExiledRoyals);
    }

    internal readonly record struct EvaluationTerms(float OwnMaterial, float OpponentMaterial,
        float OwnHanging, float OpponentHanging, float Activity, float OwnRoyalPressure,
        float OpponentRoyalPressure, float Phase, StrategicHeuristics.Terms Strategic, float Total);

    // Expose the evaluator's components without repeating the expensive board,
    // exchange and temporal-threat analyses for each feature. Only latest active
    // boards count toward material; historical copies never create free pieces.
    internal EvaluationTerms Describe(GameState state, bool perspective)
    {
        if (state.kingWasTaken)
            return new(0, 0, 0, 0, 0, 0, 0, 0, default, state.kingWasTakenByColor == perspective ? 1 : -1);
        var summary = Summary(state);
        float own = 0, opponent = 0, phase = 0;
        int count = 0, color = perspective ? 0 : 1;
        foreach (var line in state.Multiverse)
        {
            if (!state.LayerIsActive(line.Layer)) continue;
            var info = Info(line.GetPlayableBoard());
            own += info.Material[color]; opponent += info.Material[1 - color]; phase += info.Phase; count++;
        }
        int divisor = Math.Max(1, count);
        return new(own / divisor, opponent / divisor,
            Hanging(state, perspective, summary), Hanging(state, !perspective, summary),
            Activity(state, perspective, summary), RoyalPressure(state, perspective, summary),
            RoyalPressure(state, !perspective, summary), phase / divisor,
            StrategicTerms(state, perspective), Evaluate(state, perspective));
    }

    internal float Evaluate(GameState state, bool perspective)
    {
        if (state.kingWasTaken) return state.kingWasTakenByColor == perspective ? 1 : -1;
        float material = 0; int count = 0, color = perspective ? 0 : 1;
        foreach (var line in state.Multiverse)
        {
            if (!state.LayerIsActive(line.Layer)) continue;
            var info = Info(line.GetPlayableBoard());
            material += info.Material[color] - info.Material[1 - color]; count++;
        }
        // Average frontier material; copying a historical board isn't free material.
        float score = (count == 0 ? 0 : material / count) / Program.MaximumFinitePieceValue;
        if (!optimizedStaticSummary)
        {
            score += (Hanging(state, !perspective) - Hanging(state, perspective)) / (2 * Program.MaximumFinitePieceValue);
            return Math.Clamp(.5f * MathF.Tanh(score) + Activity(state, perspective) +
                RoyalPressure(state, !perspective) - RoyalPressure(state, perspective) + Strategic(state, perspective), -.9f, .9f);
        }
        StateInfo summary = Summary(state);
        score += (Hanging(state, !perspective, summary) - Hanging(state, perspective, summary)) /
            (2 * Program.MaximumFinitePieceValue);
        return Math.Clamp(.5f * MathF.Tanh(score) + Activity(state, perspective, summary) +
            RoyalPressure(state, !perspective, summary) - RoyalPressure(state, perspective, summary) + Strategic(state, perspective), -.9f, .9f);
    }
}
