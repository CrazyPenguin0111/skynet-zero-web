using static SkynetZero.MultiverseEvaluation;

namespace SkynetZero;

// Inference path adapted from Kairos sac.rs / hce.rs at b81e706 (GPL-3.0).
// The bundled table contains local softmate signals, not full-multiverse mate
// certificates. No search, subprocess, or table generation runs during inference.
internal static class SacrificeTable
{
    internal const string ResourceSha256 = "98eac685fecf9fe348b214f32e262a15a7bc61a24dbfe5fa89dfb59e2e5889f6";
    private const int MaxBack = 7, Window = 15;
    internal enum OutcomeKind { None, Win, NoMate, Unknown }
    internal readonly record struct Outcome(OutcomeKind Kind, int Attacker = 0, int Plies = 0);
    internal readonly record struct Entry(ulong Key, Outcome Outcome);
    private sealed record Tables(Dictionary<int, Entry> Results, Dictionary<int, Entry> Proofs, ulong[] Travels);
    private readonly record struct Routes(ulong QueenFrom, ulong BishopFrom, ulong QueenOpen, ulong BishopOpen);
    private static readonly Lazy<Tables> Data = new(Load);
    private static readonly object PublishLock = new();
    private static readonly AsyncLocal<Snapshot?> Scoped = new();
    private static readonly AsyncLocal<RootTravelContext?> ScopedTravel = new();
    private static long nextTravelIdentity;
    private static Snapshot current = new(new(1 << 14), new(1 << 20), new(1 << 10), 0);

    // Fixed-slot copy-on-write pages preserve Kairos's bounded table replacement
    // while allowing an entire search to read an immutable version without locks.
    internal sealed class Pages<T> where T : struct
    {
        private const int PageBits = 7;
        private readonly T[]?[] pages;
        internal Pages(int capacity) => pages = new T[capacity >> PageBits][];
        private Pages(T[]?[] pages) => this.pages = pages;
        internal T Get(int slot) => pages[slot >> PageBits] is { } page ? page[slot & 127] : default;
        internal Pages<T> With(Dictionary<int, T> writes)
        {
            if (writes.Count == 0) return this;
            var next = (T[]?[])pages.Clone();
            var copied = new HashSet<int>();
            foreach (var (slot, value) in writes)
            {
                int page = slot >> PageBits;
                if (copied.Add(page)) next[page] = pages[page] is { } old ? (T[])old.Clone() : new T[128];
                next[page]![slot & 127] = value;
            }
            return new(next);
        }
        internal IEnumerable<T> Entries()
        {
            foreach (T[]? page in pages) if (page != null)
                foreach (T value in page) if (!EqualityComparer<T>.Default.Equals(value, default)) yield return value;
        }
    }

    internal sealed record Snapshot(Pages<Entry> Results, Pages<Entry> Proofs, Pages<ulong> Travels, long Revision);
    internal readonly record struct RootTravelEntry(int Layer, int SourceHalf, int Color,
        ulong QueenFrom, ulong BishopFrom, ulong QueenOpen, ulong BishopOpen);
    internal sealed class RootTravelContext
    {
        private readonly RootTravelEntry[] entries;
        internal long Identity { get; }
        internal long Revision { get; }
        internal ReadOnlySpan<RootTravelEntry> Entries => entries;
        internal RootTravelContext(RootTravelEntry[] entries, long revision)
        {
            this.entries = entries;
            Revision = revision;
            Identity = Interlocked.Increment(ref nextTravelIdentity);
        }
    }
    internal sealed class Updates
    {
        internal readonly Dictionary<int, Entry> Results = new(), Proofs = new();
        internal readonly Dictionary<int, ulong> Travels = new();
        internal void Result(ulong key, Outcome outcome) => Results[ResultSlot(key)] = new(key, outcome);
        internal void Proof(ulong key, int attacker, int plies)
        {
            key &= ~31UL;
            Proofs[ProofSlot(key)] = new(key, new(OutcomeKind.Win, attacker, plies));
        }
        internal void Travel(ulong key) => Travels[TravelSlot(key)] = key;
    }

    internal static long CurrentRevision => Volatile.Read(ref current).Revision;
    internal static bool HasScopedSnapshot => Scoped.Value != null;
    internal static Snapshot EffectiveSnapshot => Scoped.Value ?? CaptureSnapshot();
    internal static Snapshot CaptureSnapshot() => Volatile.Read(ref current);
    internal static RootTravelContext? CurrentRootTravel => ScopedTravel.Value;
    internal static RootTravelContext CaptureRootTravel(GameState state, Snapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        snapshot ??= Scoped.Value ?? CaptureSnapshot();
        var boards = new Dictionary<Board, BitBoard>(ReferenceEqualityComparer.Instance);
        BitBoard Bits(Board board)
        {
            if (!boards.TryGetValue(board, out BitBoard? bits)) boards.Add(board, bits = new(board));
            return bits;
        }
        return new(RootTravels(state, snapshot, Bits), snapshot.Revision);
    }
    internal static IDisposable UseRootTravel(RootTravelContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = ScopedTravel.Value;
        ScopedTravel.Value = context;
        return new TravelScope(previous);
    }
    private sealed class TravelScope(RootTravelContext? previous) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (!disposed) { ScopedTravel.Value = previous; disposed = true; } }
    }
    internal static void ClearLearned()
    {
        lock (PublishLock) Volatile.Write(ref current, new Snapshot(new(1 << 14), new(1 << 20), new(1 << 10), current.Revision + 1));
    }
    internal static IDisposable UseSnapshot(Snapshot snapshot)
    {
        var previous = Scoped.Value;
        Scoped.Value = snapshot;
        return new Scope(previous);
    }
    private sealed class Scope(Snapshot? previous) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (!disposed) { Scoped.Value = previous; disposed = true; } }
    }
    internal static long Publish(Updates updates, bool replace = false)
    {
        if (!replace && updates.Results.Count + updates.Proofs.Count + updates.Travels.Count == 0) return CurrentRevision;
        lock (PublishLock)
        {
            Snapshot old = current;
            var next = new Snapshot((replace ? new Pages<Entry>(1 << 14) : old.Results).With(updates.Results),
                (replace ? new Pages<Entry>(1 << 20) : old.Proofs).With(updates.Proofs),
                (replace ? new Pages<ulong>(1 << 10) : old.Travels).With(updates.Travels), old.Revision + 1);
            Volatile.Write(ref current, next);
            return next.Revision;
        }
    }
    internal static bool TryResult(Snapshot snapshot, ulong key, out Outcome outcome)
    {
        int slot = ResultSlot(key);
        Entry entry = snapshot.Results.Get(slot);
        if (entry.Outcome.Kind == OutcomeKind.None) Data.Value.Results.TryGetValue(slot, out entry);
        outcome = entry.Outcome;
        return entry.Key == key && outcome.Kind != OutcomeKind.None;
    }
    internal static bool TryProof(Snapshot snapshot, ulong key, out Outcome outcome)
    {
        key &= ~31UL;
        int slot = ProofSlot(key);
        Entry entry = snapshot.Proofs.Get(slot);
        if (entry.Outcome.Kind == OutcomeKind.None) Data.Value.Proofs.TryGetValue(slot, out entry);
        outcome = entry.Outcome;
        return entry.Key == key && outcome.Kind == OutcomeKind.Win;
    }
    internal static bool KnownTravel(Snapshot snapshot, ulong key)
    {
        ulong stored = snapshot.Travels.Get(TravelSlot(key));
        return (stored == 0 ? Data.Value.Travels[TravelSlot(key)] : stored) == key;
    }
    internal static int ProofEntries => Data.Value.Proofs.Count;
    internal static int TravelEntries => Data.Value.Travels.Count(key => key != 0);

    internal static void Fill(GameState state, int[][] extra)
    {
        // Cache only within this query: historical boards are shared, while a
        // caller can still edit a fixture board between independent queries.
        var boards = new Dictionary<Board, BitBoard>(ReferenceEqualityComparer.Instance);
        BitBoard Bits(Board board)
        {
            if (!boards.TryGetValue(board, out BitBoard? bits)) boards.Add(board, bits = new(board));
            return bits;
        }
        Fill(state, extra, Bits);
    }

    // The full evaluator has already converted these historical boards while
    // extracting its other temporal terms. Reusing its query-local lookup avoids
    // allocating and scanning another copy of every board at every search leaf.
    internal static void Fill(GameState state, int[][] extra, Func<Board, BitBoard> bits)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(bits);
        if (extra.Length != 2 || extra.Any(side => side.Length < 16))
            throw new ArgumentException("SAC terms require two sides with at least sixteen extra features.", nameof(extra));
        Snapshot snapshot = Scoped.Value ?? CaptureSnapshot();
        foreach (Timeline line in state.Multiverse)
        {
            if (!state.LayerIsActive(line.Layer) ||
                line.Layer >= state.InitialMinTL && line.Layer <= state.InitialMaxTL) continue;
            ulong key = 0x2545f4914f6cdd1dUL;
            int end = End(line), start = Math.Max(Start(line), end - Window + 1);
            for (int half = start; half <= end; half++)
                key ^= NativeBoardKey(state, line.Layer, half, bits(At(line, half)!));
            key = Mix(key) & ~31UL;
            if (!TryProof(snapshot, key, out Outcome proof)) continue;
            extra[proof.Attacker][12]++;
            extra[proof.Attacker][13] += 12 - Math.Min(proof.Plies, 11);
        }

        // A search freezes the root's four winning-travel routes, as upstream
        // does. Standalone encoding remains a fresh root when no search scope
        // has been supplied. Actual source boards must still have the same
        // source half-turn; advancing them invalidates that root route.
        ReadOnlySpan<RootTravelEntry> travels = ScopedTravel.Value is { } context
            ? context.Entries : RootTravels(state, snapshot, bits);
        foreach (RootTravelEntry entry in travels)
        {
            Timeline? line = state.GetTimeline(entry.Layer);
            if (line == null || SourceHalf(End(line), entry.Color) != entry.SourceHalf) continue;
            Board? board = At(line, entry.SourceHalf);
            if (board == null) continue;
            BitBoard source = bits(board);
            ulong mine = source.Mine(entry.Color), queen = source.Queen & mine, bishop = source.Bishop & mine;
            if (((queen & entry.QueenFrom) | (bishop & entry.BishopFrom)) != 0) extra[entry.Color][14]++;
            ulong empty = ~source.Occupied;
            extra[entry.Color][15] += Count(((queen != 0 ? entry.QueenOpen : 0) |
                (bishop != 0 ? entry.BishopOpen : 0)) & empty);
        }
    }

    private static RootTravelEntry[] RootTravels(GameState state, Snapshot snapshot, Func<Board, BitBoard> bits)
    {
        var entries = new List<RootTravelEntry>(4);
        foreach (Timeline line in state.Multiverse.OrderBy(line => line.Layer))
        {
            if (!state.LayerIsActive(line.Layer)) continue;
            for (int color = 0; color < 2; color++)
            {
                Routes routes = WinningSources(state, line, color, snapshot, bits);
                if ((routes.QueenFrom | routes.BishopFrom) == 0) continue;
                entries.Add(new(line.Layer, SourceHalf(End(line), color), color,
                    routes.QueenFrom, routes.BishopFrom, routes.QueenOpen, routes.BishopOpen));
                if (entries.Count == 4) return entries.ToArray();
            }
        }
        return entries.ToArray();
    }

    private static Routes WinningSources(GameState state, Timeline line, int color, Snapshot snapshot,
        Func<Board, BitBoard> bits)
    {
        ulong queenFrom = 0, bishopFrom = 0, queenOpen = 0, bishopOpen = 0;
        int source = SourceHalf(End(line), color);
        for (int back = 1; back <= MaxBack; back++)
        {
            int half = source - 2 * back;
            if (half < Start(line)) break;
            Board target = At(line, half)!;
            BitBoard board = bits(target);
            ulong royals = board.Royal & board.Mine(1 - color), zone = 0;
            foreach (int square in Squares(royals)) zone |= KingAttack(square);
            if (zone == 0) continue;
            ulong boardKey = NativeBoardKey(state, line.Layer, half, board);
            foreach (int square in Squares(zone & ~board.Friendly(color)))
            foreach (bool queen in new[] { true, false })
            {
                ulong key = TravelKey(boardKey, square, queen, color);
                if (!KnownTravel(snapshot, key)) continue;
                FindRoutes(state, line, source, half, square, queen, bits, (from, continues) =>
                {
                    if (queen) { queenFrom |= Mask(from); if (continues) queenOpen |= Mask(from); }
                    else { bishopFrom |= Mask(from); if (continues) bishopOpen |= Mask(from); }
                });
            }
        }
        return new(queenFrom, bishopFrom, queenOpen, bishopOpen);
    }

    internal static void FindRoutes(GameState state, Timeline line, int source, int target, int square,
        bool queen, Func<Board, BitBoard> bits, Action<int, bool> found)
    {
        int x0 = square & 7, y0 = square >> 3;
        // In 5D a bishop travels through time plus exactly one spatial axis.
        // A queen also permits time alone and time plus both spatial axes.
        int first = queen ? 0 : 1, last = queen ? XY9.Length : 5;
        bool On(int x, int y) => x >= 0 && y >= 0 && x < state.Width && y < state.Height;
        for (int direction = first; direction < last; direction++)
        {
            var (dx, dy) = XY9[direction];
            for (int step = 1; ; step++)
            {
                int x = x0 + step * dx, y = y0 + step * dy;
                if (!On(x, y)) break;
                int half = target + 2 * step;
                if (half == source) { found(x + 8 * y, On(x + dx, y + dy)); break; }
                Board? intermediate = At(line, half);
                if (intermediate == null || (bits(intermediate).Occupied & Mask(x + 8 * y)) != 0) break;
            }
        }
    }

    // Even starts label -0/+0 as native layers 0/1 in SkynetZero, while
    // the upstream table hashes them as -1/0. Geometry is translation-invariant;
    // these absolute-coordinate table keys require the explicit translation.
    internal static ulong NativeBoardKey(GameState state, int layer, int half, BitBoard board) =>
        BoardKey(layer - (state.EvenStart ? 1 : 0), half, board);

    // Raw upstream-coordinate hash, also used by independent parity fixtures.
    internal static ulong BoardKey(int layer, int half, BitBoard board)
    {
        ulong value = Mix((((ulong)(uint)layer << 32) | (uint)half) ^ 0x9e3779b97f4a7c15UL);
        foreach (ulong word in board.Raw) value = unchecked(Mix(value ^ word) + 0x632be59bd9b4e019UL);
        return value;
    }

    internal static ulong TravelKey(ulong board, int square, bool queen, int color) =>
        Mix(board ^ unchecked(((ulong)(uint)square | (queen ? 1UL << 6 : 0) | ((ulong)(uint)color << 7)) *
            0x9e3779b97f4a7c15UL)) | 1;

    internal static ulong WindowKey(IReadOnlyList<ulong> keys)
    {
        ulong value = 0x2545f4914f6cdd1dUL;
        for (int i = Math.Max(0, keys.Count - Window); i < keys.Count; i++) value ^= keys[i];
        return Mix(value);
    }
    internal static ulong ResultKey(ulong position, int attacker) => position ^
        (attacker == 0 ? 0x1d8e4e27c47d124fUL : 0x5851f42d4c957f2dUL);

    private static ulong Mix(ulong value)
    {
        value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebUL);
        return value ^ (value >> 31);
    }
    private static int ProofSlot(ulong key) => (int)((key >> 20) & ((1 << 20) - 1));
    private static int ResultSlot(ulong key) => (int)((key >> 20) & ((1 << 14) - 1));
    private static int TravelSlot(ulong key) => (int)((key >> 40) & ((1 << 10) - 1));
    internal static int Start(Timeline line) => HypercuboidGameAdapter.HalfTime(line.TStart, line.ColorStart);
    internal static int End(Timeline line) => HypercuboidGameAdapter.HalfTime(line.TEnd, line.ColorPlayable);
    internal static int SourceHalf(int end, int color) => (end & 1) == color ? end : end + 1;

    internal static Board? At(Timeline line, int half)
    {
        int end = End(line);
        if (half == end + 1) return line.GetPlayableBoard();
        if (half < Start(line) || half > end) return null;
        int turn = half >> 1;
        return (half & 1) == 0 ? line.WBoards[turn - line.WhiteStart] : line.BBoards[turn - line.BlackStart];
    }

    private static Tables Load()
    {
        using Stream stream = typeof(SacrificeTable).Assembly.GetManifestResourceStream("SkynetZero.SacrificeTable.bin") ??
            throw new InvalidDataException("The embedded Kairos f7 sacrifice table is missing.");
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt64() != 0x3142415443415346UL)
            throw new InvalidDataException("The embedded Kairos f7 sacrifice table has an invalid format.");
        var results = new Dictionary<int, Entry>();
        var proofs = new Dictionary<int, Entry>();
        var travels = new ulong[1 << 10];
        for (int section = 0; section < 3; section++)
        {
            ulong count = reader.ReadUInt64();
            if (count > (ulong)((stream.Length - stream.Position) / (section == 0 ? 16 : 8)))
                throw new InvalidDataException("The embedded Kairos f7 sacrifice table is truncated.");
            for (ulong index = 0; index < count; index++)
            {
                ulong key = reader.ReadUInt64();
                if (section == 0) results[ResultSlot(key)] = new(key, Decode(reader.ReadUInt64()));
                else if (section == 1)
                {
                    ulong position = key & ~31UL;
                    proofs[ProofSlot(position)] = new(position, new(OutcomeKind.Win, (int)((key >> 4) & 1), (int)(key & 15)));
                }
                else travels[TravelSlot(key)] = key;
            }
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing bytes in Kairos f7 sacrifice table.");
        return new(results, proofs, travels);
    }

    private static Outcome Decode(ulong data) => (data & 3) switch
    {
        1 => new(OutcomeKind.Win, (int)((data >> 2) & 1), (int)((data >> 8) & 255)),
        2 => new(OutcomeKind.NoMate),
        _ => new(OutcomeKind.Unknown)
    };
    private static ulong Encode(Outcome outcome) => outcome.Kind == OutcomeKind.Win
        ? 1UL | ((ulong)(uint)outcome.Attacker << 2) | ((ulong)(uint)outcome.Plies << 8)
        : outcome.Kind == OutcomeKind.NoMate ? 2UL : 3UL;

    // Learned local heuristic data is separate from full-game mate certificates
    // and from the original embedded resource. Callers choose when/where to save.
    internal static void WriteLearned(Stream stream, Snapshot? snapshot = null)
    {
        snapshot ??= CaptureSnapshot();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(0x3150414341535a53UL); // SZSACAP1
        foreach (var entries in new[] { snapshot.Results.Entries(), snapshot.Proofs.Entries() })
        {
            Entry[] values = entries.ToArray(); writer.Write(values.Length);
            foreach (Entry entry in values) { writer.Write(entry.Key); writer.Write(Encode(entry.Outcome)); }
        }
        ulong[] travels = snapshot.Travels.Entries().ToArray(); writer.Write(travels.Length);
        foreach (ulong key in travels) writer.Write(key);
    }
    internal static long ReadLearned(Stream stream, bool replace = false)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt64() != 0x3150414341535a53UL) throw new InvalidDataException("Invalid learned sacrifice table.");
        var updates = new Updates();
        for (int section = 0; section < 3; section++)
        {
            int count = reader.ReadInt32(), limit = section == 0 ? 1 << 14 : section == 1 ? 1 << 20 : 1 << 10;
            if (count < 0 || count > limit) throw new InvalidDataException("Invalid learned sacrifice table size.");
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.ReadUInt64();
                if (section == 2) { if (key == 0) throw new InvalidDataException("Invalid travel key."); updates.Travel(key); continue; }
                ulong data = reader.ReadUInt64();
                Outcome outcome = Decode(data);
                if (data != Encode(outcome) || outcome.Plies > 8 || section == 1 && outcome.Kind != OutcomeKind.Win)
                    throw new InvalidDataException("Invalid learned sacrifice outcome.");
                if (section == 0) updates.Result(key, outcome); else updates.Proof(key, outcome.Attacker, outcome.Plies);
            }
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Trailing learned sacrifice data.");
        return Publish(updates, replace);
    }
}
