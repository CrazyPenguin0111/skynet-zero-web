using System.Diagnostics;
using static SkynetZero.MultiverseEvaluation;
using static SkynetZero.SacrificeTable;

namespace SkynetZero;

// Adapted from Kairos sac.rs at b81e706 (GPL-3.0). This single-timeline
// soft-mate search supplies heuristic features; it NEVER adjudicates a game.
// Other-timeline defences and en passant are intentionally outside its model,
// and defenders may remove non-pawn/non-royal pieces to model departures.
internal static class SacrificePreparation
{
    internal const int MaxMoves = 4, ProbeNodes = 5_000;
    private const int Window = 15, MaxBack = 7, MaxPly = 12, ProofCap = 1 << 16;
    private const byte Promotion = 1, Castle = 2, Depart = 4;
    internal readonly record struct PreparationStats(int Resolved, int Wins, long Nodes, long Revision);
    internal readonly record struct Resolution(Outcome Outcome, long Nodes, bool TimedOut);
    private sealed record Job(int Priority, int Layer, int FirstHalf, BitBoard[] History, int Attacker, ulong? TravelKey);
    private readonly record struct LocalMove(int From, int To, byte Flags = 0)
    {
        internal ushort Code => (ushort)(From | To << 6 | ((Flags & Depart) != 0 ? 1 << 15 : 0));
    }
    private readonly record struct Candidate(int Score, int Order, LocalMove Move, BitBoard Board);
    private struct Slot { internal ulong Key; internal int Win, Fail; internal bool Or; internal ushort Best; }
    private readonly record struct Reach(ulong Axis0, ulong Axis1, ulong Axis2, ulong Knight, ulong King)
    {
        internal bool Hits(BitBoard board, int by) =>
            ((board.LRook & Axis0) | (board.LBishop & Axis1) | (board.LUnicorn & Axis2) |
             (board.LKnight & Knight) | (board.LKing & King)) is var attacks && (attacks & board.Mine(by)) != 0;
    }
    [ThreadStatic] private static Local? worker;

    // maxNodes=0 prepares known entries only, matching untimed Kairos searches.
    // These limits bound OPTIONAL heuristic learning, not legal-move/mate proof.
    internal static PreparationStats Prepare(GameState state, long maxNodes = ProbeNodes,
        int maxMilliseconds = 0, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (maxNodes < 0 || maxMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(maxNodes));
        cancellation.ThrowIfCancellationRequested();
        Snapshot snapshot = CaptureSnapshot();
        var updates = new Updates();
        long deadline = Deadline(maxMilliseconds);
        var boards = new Dictionary<Board, BitBoard>(ReferenceEqualityComparer.Instance);
        BitBoard Bits(Board board)
        {
            if (!boards.TryGetValue(board, out BitBoard? bits)) boards.Add(board, bits = new(board));
            return bits;
        }
        var jobs = new List<Job>();
        int layerOffset = state.EvenStart ? 1 : 0;
        foreach (Timeline line in state.Multiverse.OrderBy(line => line.Layer))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!state.LayerIsActive(line.Layer)) continue;
            int end = End(line), start = Math.Max(Start(line), end - Window + 1);
            if (line.Layer < state.InitialMinTL || line.Layer > state.InitialMaxTL)
            {
                BitBoard[] history = Enumerable.Range(start, end - start + 1).Select(half => Bits(At(line, half)!)).ToArray();
                ulong[] keys = history.Select((board, index) => NativeBoardKey(state, line.Layer, start + index, board)).ToArray();
                if (!TryProof(snapshot, WindowKey(keys), out _))
                    for (int attacker = 0; attacker < 2; attacker++)
                        jobs.Add(new(0, line.Layer - layerOffset, start, history, attacker, null));
            }
            for (int color = 0; color < 2; color++)
            {
                int sourceHalf = SourceHalf(end, color);
                BitBoard source = Bits(At(line, sourceHalf)!);
                ulong queens = source.Queen & source.Mine(color), bishops = source.Bishop & source.Mine(color);
                if ((queens | bishops) == 0) continue;
                ulong qReach = queens, bReach = bishops;
                foreach (int square in Squares(queens)) qReach |= SliderAttack(square, source.Occupied, true, true);
                foreach (int square in Squares(bishops)) bReach |= SliderAttack(square, source.Occupied, false, true);
                for (int back = 1; back <= MaxBack; back++)
                {
                    int half = sourceHalf - 2 * back;
                    if (half < Start(line)) break;
                    if ((half >> 1) > 8) continue;
                    BitBoard target = Bits(At(line, half)!);
                    ulong royals = target.Royal & target.Mine(1 - color);
                    int home = color == 0 ? state.Height - 1 : 0;
                    if ((royals & (0xffUL << (home * 8))) == 0) continue;
                    foreach (int royal in Squares(royals))
                    foreach (int square in Squares(KingAttack(royal) & ~target.Friendly(color)))
                    {
                        if ((AttackersTo(target, square) & target.Mine(1 - color) & ~target.Royal) != 0) continue;
                        for (int kind = 0; kind < 2; kind++)
                        {
                            bool queen = kind == 0;
                            if (!queen && ((square & 7) == (royal & 7) || (square >> 3) == (royal >> 3))) continue;
                            ulong piece = queen ? queens : bishops, reach = queen ? qReach : bReach;
                            if (piece == 0) continue;
                            bool now = false, soon = false;
                            FindRoutes(state, line, sourceHalf, half, square, queen, Bits, (from, continues) =>
                            { now |= (piece & Mask(from)) != 0; soon |= continues && (reach & Mask(from)) != 0; });
                            if (!now && !soon) continue;
                            ulong travel = TravelKey(NativeBoardKey(state, line.Layer, half, target), square, queen, color);
                            int newLayer = (color == 0 ? state.MaxTL + 1 : state.MinTL - 1) - layerOffset;
                            jobs.Add(new((now ? 16 : 32) + back, newLayer, half + 1,
                                [Branch(target, square, queen, color)], color, travel));
                        }
                    }
                }
            }
        }
        int resolved = 0, wins = 0;
        long nodes = 0, perProbe = Math.Min(ProbeNodes, maxNodes);
        bool spent = false;
        // OrderBy is stable, matching upstream job ordering for equal priorities.
        foreach (Job job in jobs.OrderBy(job => job.Priority))
        {
            cancellation.ThrowIfCancellationRequested();
            BitBoard last = job.History[^1];
            if ((last.Royal & last.Mine(0)) == 0 || (last.Royal & last.Mine(1)) == 0) continue;
            ulong[] keys = job.History.Select((board, index) => BoardKey(job.Layer, job.FirstHalf + index, board)).ToArray();
            ulong resultKey = ResultKey(WindowKey(keys), job.Attacker);
            Outcome outcome;
            if (updates.Results.TryGetValue(ResultSlotLocal(resultKey), out Entry cached) && cached.Key == resultKey)
                outcome = cached.Outcome;
            else if (TryResult(snapshot, resultKey, out outcome)) { }
            else
            {
                if (spent || perProbe == 0 || nodes + perProbe > maxNodes || Expired(deadline)) { spent = true; continue; }
                Resolution result = ResolveInto(job.History, job.Layer, job.FirstHalf, job.Attacker,
                    MaxMoves, perProbe, deadline, cancellation, updates);
                nodes += result.Nodes;
                if (result.TimedOut) { spent = true; continue; }
                outcome = result.Outcome;
                resolved++;
                if (outcome.Kind == OutcomeKind.Win) wins++;
                updates.Result(resultKey, outcome);
            }
            if (job.TravelKey is { } travel && outcome.Kind == OutcomeKind.Win && !KnownTravel(snapshot, travel))
                updates.Travel(travel);
        }
        cancellation.ThrowIfCancellationRequested();
        return new(resolved, wins, nodes, Publish(updates));
    }

    // The layer argument uses upstream absolute coordinates (-1/0 for an even
    // initial pair); Prepare translates the engine's native even coordinates.
    internal static Resolution Resolve(IReadOnlyList<BitBoard> history, int layer, int firstHalf, int attacker,
        int maxMoves = MaxMoves, long maxNodes = 400_000, CancellationToken cancellation = default,
        int maxMilliseconds = 0, bool publish = true)
    {
        if (history.Count == 0 || attacker is < 0 or > 1 || maxMoves is < 0 or > MaxMoves || maxNodes < 0 || maxMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(history));
        cancellation.ThrowIfCancellationRequested();
        var updates = new Updates();
        Resolution result = ResolveInto(history, layer, firstHalf, attacker, maxMoves, maxNodes,
            Deadline(maxMilliseconds), cancellation, updates);
        cancellation.ThrowIfCancellationRequested();
        if (publish) Publish(updates);
        return result;
    }
    private static Resolution ResolveInto(IReadOnlyList<BitBoard> history, int layer, int firstHalf,
        int attacker, int maxMoves, long maxNodes, long deadline, CancellationToken token, Updates updates) =>
        (worker ??= new()).Resolve(history, layer, firstHalf, attacker, maxMoves, maxNodes, deadline, token, updates);
    private static int ResultSlotLocal(ulong key) => (int)((key >> 20) & ((1 << 14) - 1));
    private static long Deadline(int milliseconds) => milliseconds == 0 ? 0 :
        Stopwatch.GetTimestamp() + (long)(milliseconds * (double)Stopwatch.Frequency / 1000);
    private static bool Expired(long deadline) => deadline != 0 && Stopwatch.GetTimestamp() >= deadline;

    internal static BitBoard Branch(BitBoard target, int square, bool queen, int attacker)
    {
        var board = new BitBoard(target.Width, target.Height, target.Raw);
        SetPiece(board, square, queen, attacker);
        return board;
    }
    private static void Clear(BitBoard board, int square)
    { ulong mask = ~Mask(square); for (int i = 0; i < 12; i++) board.Raw[i] &= mask; }
    private static void SetPiece(BitBoard board, int square, bool queen, int color)
    {
        Clear(board, square);
        ulong mask = Mask(square);
        board.Raw[color] |= mask;
        board.Raw[8] |= mask;
        if (queen) { board.Raw[7] |= mask; board.Raw[9] |= mask; board.Raw[10] |= mask; }
    }
    private static void Transfer(BitBoard board, int from, int to)
    {
        ulong origin = Mask(from), destination = Mask(to), clear = ~(origin | destination);
        for (int i = 0; i < 11; i++) board.Raw[i] = (board.Raw[i] & clear) | ((board.Raw[i] & origin) != 0 ? destination : 0);
        board.Raw[11] &= clear;
    }
    private static int FirstOccupiedX(BitBoard board, int x, int y, int direction)
    {
        for (x += direction; x >= 0 && x < 8; x += direction)
            if ((board.Occupied & Mask(x + 8 * y)) != 0) break;
        return x;
    }
    private static void Play(BitBoard source, LocalMove move, int color, BitBoard result)
    {
        source.Raw.CopyTo(result.Raw, 0);
        if ((move.Flags & Depart) != 0) Clear(result, move.From);
        else if ((move.Flags & Promotion) != 0) { Clear(result, move.From); SetPiece(result, move.To, true, color); }
        else if ((move.Flags & Castle) != 0)
        {
            int x = move.From & 7, y = move.From >> 3, dx = Math.Sign((move.To & 7) - x);
            int rook = FirstOccupiedX(source, x, y, dx);
            Transfer(result, rook + 8 * y, x + dx + 8 * y);
            Transfer(result, move.From, move.To);
        }
        else Transfer(result, move.From, move.To);
    }
    private static ulong AttackersTo(BitBoard board, int square)
    {
        ulong z = Mask(square);
        return ((Shift(z, 1, 1) | Shift(z, -1, 1)) & board.LPawn & board.Black) |
            ((Shift(z, 1, -1) | Shift(z, -1, -1)) & board.LPawn & board.White) |
            (KingAttack(square) & board.LKing) | (KnightAttack(square) & board.LKnight) |
            (SliderAttack(square, board.Occupied, true, false) & board.LRook) |
            (SliderAttack(square, board.Occupied, false, true) & board.LBishop);
    }
    private static bool Attacked(BitBoard board, int square, int defender) =>
        (AttackersTo(board, square) & board.Mine(1 - defender)) != 0;
    private static bool PhysicalCheck(BitBoard board, int defender)
    {
        foreach (int square in Squares(board.Royal & board.Mine(defender)))
            if (Attacked(board, square, defender)) return true;
        return false;
    }
    private static void Generate(BitBoard board, int color, List<LocalMove> moves)
    {
        ulong own = board.Friendly(color), enemy = board.Mine(1 - color), occupied = board.Occupied;
        int lastRank = color == 0 ? board.Height - 1 : 0;
        foreach (int from in Squares(board.Mine(color)))
        {
            ulong z = Mask(from), targets = 0;
            if ((board.LPawn & z) != 0)
            {
                int dy = color == 0 ? 1 : -1;
                ulong push = Shift(z, 0, dy) & ~occupied;
                targets |= push;
                if (push != 0 && (board.Unmoved & z) != 0) targets |= Shift(push, 0, dy) & ~occupied;
                targets |= (Shift(z, 1, dy) | Shift(z, -1, dy)) & enemy;
                foreach (int to in Squares(targets)) moves.Add(new(from, to, (to >> 3) == lastRank ? Promotion : (byte)0));
                continue;
            }
            if ((board.LKnight & z) != 0) targets |= KnightAttack(from) & ~own;
            if ((board.LKing & z) != 0)
            {
                targets |= KingAttack(from) & ~own;
                if ((board.Royal & board.Unmoved & z) != 0 && !Attacked(board, from, color))
                {
                    int x = from & 7, y = from >> 3;
                    foreach (int dx in new[] { 1, -1 })
                    {
                        int rookX = FirstOccupiedX(board, x, y, dx);
                        if (rookX >= 0 && rookX < 8 && Math.Abs(rookX - x) >= 2 &&
                            (board.Unmoved & board.Rook & own & Mask(rookX + 8 * y)) != 0 &&
                            !Attacked(board, x + dx + 8 * y, color) && !Attacked(board, x + 2 * dx + 8 * y, color))
                            moves.Add(new(from, x + 2 * dx + 8 * y, Castle));
                    }
                }
            }
            if ((board.LRook & z) != 0) targets |= SliderAttack(from, occupied, true, false) & ~own;
            if ((board.LBishop & z) != 0) targets |= SliderAttack(from, occupied, false, true) & ~own;
            foreach (int to in Squares(targets)) moves.Add(new(from, to));
        }
    }
    private static Reach TimeReach(IReadOnlyList<BitBoard> stack, int by, int q)
    {
        int back = Math.Min(q / 2, MaxBack);
        if (back == 0) return default;
        Span<ulong> royals = stackalloc ulong[MaxBack + 1];
        ulong any = 0;
        for (int j = 1; j <= back; j++)
        { BitBoard b = stack[q - 2 * j]; any |= royals[j] = b.Royal & b.Mine(1 - by); }
        if (any == 0) return default;
        ulong axis0 = 0, axis1 = 0, axis2 = 0, knight = 0, king = 0;
        foreach (var (dx, dy) in XY9)
        {
            ulong squares = 0;
            for (int j = back; j >= 1; j--)
            {
                squares = Shift(squares | royals[j], dx, dy);
                if (j > 1) squares &= ~stack[q - 2 * (j - 1)].Occupied;
            }
            int axes = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0);
            if (axes == 0) axis0 |= squares; else if (axes == 1) axis1 |= squares; else axis2 |= squares;
            king |= Shift(royals[1], dx, dy);
        }
        foreach (var (dx, dy) in Orth)
        {
            knight |= Shift(royals[1], 2 * dx, 2 * dy);
            if (back >= 2) knight |= Shift(royals[2], dx, dy);
        }
        return new(axis0, axis1, axis2, knight, king);
    }
    private static int Value(BitBoard board, int square)
    {
        ulong z = Mask(square);
        if ((board.LPawn & z) != 0) return 1;
        if (((board.Knight | board.Bishop) & z) != 0) return 3;
        if ((board.Rook & z) != 0) return 5;
        if (((board.Queen | board.Princess) & z) != 0) return 9;
        return (board.Occupied & z) == 0 ? 0 : 2;
    }

    private sealed class Local
    {
        // Same 18-bit replacement scheme as upstream; allocate only visited
        // slots rather than reserving megabytes for every self-play worker.
        private readonly Dictionary<int, Slot> table = new();
        private readonly int[] history = new int[2 * 64 * 64];
        private readonly List<BitBoard> stack = new();
        private readonly List<ulong> keys = new();
        private readonly List<LocalMove> scratch = new(128);
        private readonly List<Candidate>[] lists = Enumerable.Range(0, MaxPly + 2).Select(_ => new List<Candidate>()).ToArray();
        private long nodes, budget, deadline;
        private int attacker, layer, firstHalf;
        private bool aborted, timedOut;
        private CancellationToken cancellation;
        private static readonly IComparer<Candidate> CandidateOrder = Comparer<Candidate>.Create((a, b) =>
        { int score = b.Score.CompareTo(a.Score); return score != 0 ? score : a.Order.CompareTo(b.Order); });

        internal Resolution Resolve(IReadOnlyList<BitBoard> boards, int line, int first, int attacking,
            int maxMoves, long maxNodes, long stopAt, CancellationToken token, Updates updates)
        {
            table.Clear(); Array.Clear(history); stack.Clear(); keys.Clear();
            nodes = 0; budget = maxNodes; deadline = stopAt; cancellation = token;
            aborted = false; timedOut = false; attacker = attacking; layer = line;
            int from = Math.Max(0, boards.Count - Window);
            firstHalf = first + from;
            for (int i = from; i < boards.Count; i++) Push(boards[i]);
            bool or = ((firstHalf + stack.Count - 1) & 1) == attacker;
            Outcome outcome = new(OutcomeKind.NoMate);
            // A local heuristic has no meaningful mate target without royals
            // of both sides. Preparation filters this too; direct callers agree.
            BitBoard last = stack[^1];
            if ((last.Royal & last.Mine(0)) == 0 || (last.Royal & last.Mine(1)) == 0)
                return new(new(OutcomeKind.Unknown), nodes, false);
            for (int n = or ? 1 : 0; n <= maxMoves; n++)
            {
                bool win = or ? Or(n, 0) : And(n, 0);
                if (aborted) { outcome = new(OutcomeKind.Unknown); break; }
                if (win) { outcome = new(OutcomeKind.Win, attacker, or ? 2 * n - 1 : 2 * n); break; }
            }
            if (outcome.Kind == OutcomeKind.Win)
            {
                foreach (Slot entry in table.Values)
                    if (entry.Key != 0 && entry.Win > 0)
                        updates.Proof(entry.Key, attacker, entry.Or ? 2 * (entry.Win - 1) - 1 : 2 * (entry.Win - 1));
                int stored = 0;
                Extract((outcome.Plies + 1) / 2, or, 0, ref stored, updates);
            }
            return new(outcome, nodes, timedOut);
        }
        private void Push(BitBoard board)
        { keys.Add(BoardKey(layer, firstHalf + stack.Count, board)); stack.Add(board); }
        private void Pop() { keys.RemoveAt(keys.Count - 1); stack.RemoveAt(stack.Count - 1); }
        private (bool? Hit, ushort Best) Probe(ulong key, int n)
        {
            if (!table.TryGetValue((int)(key & ((1 << 18) - 1)), out Slot entry) || entry.Key != key) return (null, 0);
            if (entry.Win > 0 && entry.Win - 1 <= n) return (true, entry.Best);
            if (entry.Fail > 0 && entry.Fail - 1 >= n) return (false, entry.Best);
            return (null, entry.Best);
        }
        private void Store(ulong key, bool or, int n, bool win, ushort best)
        {
            int slot = (int)(key & ((1 << 18) - 1));
            if (!table.TryGetValue(slot, out Slot entry) || entry.Key != key) entry = new() { Key = key, Or = or };
            int value = Math.Min(n, 250) + 1;
            if (win) { if (entry.Win == 0 || value < entry.Win) entry.Win = value; }
            else if (value > entry.Fail) entry.Fail = value;
            if (best != 0) entry.Best = best;
            table[slot] = entry;
        }
        private bool Tick()
        {
            cancellation.ThrowIfCancellationRequested();
            nodes++;
            if (nodes > budget) aborted = true;
            if ((nodes & 31) == 1 && Expired(deadline)) { aborted = true; timedOut = true; }
            return aborted;
        }
        private bool Attacked()
        {
            int q = stack.Count - 1, stm = (firstHalf + q) & 1;
            return PhysicalCheck(stack[q], stm) || TimeReach(stack, 1 - stm, q + 1).Hits(stack[q], 1 - stm);
        }
        private int Moves(int color, int ply, bool checksOnly, ushort best)
        {
            BitBoard board = stack[^1];
            Reach theirs = TimeReach(stack, 1 - color, stack.Count), mine = TimeReach(stack, color, stack.Count + 1);
            scratch.Clear(); Generate(board, color, scratch);
            if (color != attacker)
                foreach (int from in Squares(board.Mine(color) & ~board.LPawn & ~board.Royal)) scratch.Add(new(from, from, Depart));
            List<Candidate> list = lists[ply];
            int count = 0, order = 0, royal = Squares(board.Royal & board.Mine(1 - color)).FirstOrDefault(-1);
            foreach (LocalMove move in scratch)
            {
                BitBoard next;
                if (count < list.Count && list[count].Board.Width == board.Width && list[count].Board.Height == board.Height)
                    next = list[count].Board;
                else
                {
                    next = new(board.Width, board.Height, board.Raw);
                    Candidate spare = new(0, 0, default, next);
                    if (count == list.Count) list.Add(spare); else list[count] = spare;
                }
                Play(board, move, color, next);
                if (PhysicalCheck(next, color) || theirs.Hits(next, 1 - color)) continue;
                bool check = PhysicalCheck(next, 1 - color) || mine.Hits(next, color);
                if (checksOnly && !check) continue;
                int score = Math.Min(history[(color << 12) | (move.From << 6) | move.To], 1 << 14);
                int victim = (move.Flags & Depart) != 0 ? 0 : Value(board, move.To);
                if (move.Code == best) score += 1 << 24;
                if ((move.Flags & Depart) != 0) score -= 1 << 12;
                if (victim > 0) score += (1 << 16) + 64 * victim - Value(board, move.From);
                if (color == attacker)
                {
                    if (check) score += 1 << 20;
                    if (royal >= 0) score += 8 * (8 - Math.Min(8, Math.Max(Math.Abs((royal & 7) - (move.To & 7)),
                        Math.Abs((royal >> 3) - (move.To >> 3)))));
                }
                Candidate candidate = new(score, order++, move, next);
                list[count] = candidate;
                count++;
            }
            list.Sort(0, count, CandidateOrder);
            return count;
        }
        private bool Or(int n, int ply)
        {
            if (Tick()) return false;
            ulong key = WindowKey(keys);
            var (hit, best) = Probe(key, n);
            if (hit.HasValue) return hit.Value;
            if (ply >= MaxPly) return false;
            int count = Moves(attacker, ply, n == 1, best);
            for (int i = 0; i < count; i++)
            {
                Candidate candidate = lists[ply][i];
                Push(candidate.Board); bool win = And(n - 1, ply + 1); Pop();
                if (aborted) return false;
                if (!win) continue;
                LocalMove move = candidate.Move;
                history[(attacker << 12) | (move.From << 6) | move.To] += n * n;
                Store(key, true, n, true, move.Code); return true;
            }
            Store(key, true, n, false, 0); return false;
        }
        private bool And(int n, int ply)
        {
            if (Tick()) return false;
            ulong key = WindowKey(keys);
            var (hit, best) = Probe(key, n);
            if (hit.HasValue) return hit.Value;
            if (ply >= MaxPly) return false;
            int defender = 1 - attacker, count = Moves(defender, ply, false, best);
            if (count == 0)
            { bool mate = Attacked(); Store(key, false, mate ? 0 : 250, mate, 0); return mate; }
            if (n == 0) { Store(key, false, 0, false, 0); return false; }
            for (int i = 0; i < count; i++)
            {
                Candidate candidate = lists[ply][i];
                Push(candidate.Board); bool win = Or(n, ply + 1); Pop();
                if (aborted) return false;
                if (win) continue;
                LocalMove move = candidate.Move;
                history[(defender << 12) | (move.From << 6) | move.To] += n * n;
                Store(key, false, n, false, move.Code); return false;
            }
            Store(key, false, n, true, 0); return true;
        }
        private bool Extract(int n, bool or, int ply, ref int stored, Updates updates)
        {
            cancellation.ThrowIfCancellationRequested();
            if (aborted || stored >= ProofCap || ply >= MaxPly) return false;
            ulong key = WindowKey(keys);
            var (hit, best) = Probe(key, n);
            if (table.TryGetValue((int)(key & ((1 << 18) - 1)), out Slot entry) && entry.Key == key && entry.Win > 0)
                n = Math.Min(n, entry.Win - 1);
            updates.Proof(key, attacker, or ? 2 * n - 1 : 2 * n); stored++;
            if (or)
            {
                if (hit != true || best == 0) { if (!Or(n, ply)) return false; best = Probe(key, n).Best; }
                int count = Moves(attacker, ply, n == 1, best);
                for (int i = 0; i < count; i++)
                {
                    Candidate candidate = lists[ply][i];
                    if (candidate.Move.Code != best) continue;
                    Push(candidate.Board); bool result = Extract(n - 1, false, ply + 1, ref stored, updates); Pop();
                    return result;
                }
                return false;
            }
            if (n == 0) return true;
            int replies = Moves(1 - attacker, ply, false, 0);
            for (int i = 0; i < replies; i++)
            {
                Push(lists[ply][i].Board); bool result = Extract(n, true, ply + 1, ref stored, updates); Pop();
                if (!result) return false;
            }
            return true;
        }
    }
}
