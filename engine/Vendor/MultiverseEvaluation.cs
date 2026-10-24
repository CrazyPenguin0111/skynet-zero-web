// Adapted from FirePlank/5d-chess-engine hce.rs (Kairos), commit
// b81e7064d0c568f3c7972bb75744e3ecd9d87535, GPL-3.0.
// C# board/coordinate/cache adapters and transformer input contract are SkynetZero changes.
using System.Numerics;
using System.Collections;
using System.Runtime.CompilerServices;

namespace SkynetZero;

internal static class MultiverseEvaluation
{
    internal const int SideCount = 54, ExtraCount = 30, FeatureCount = 95;
    internal const int TempoIndex = 43, Checks5DIndex = 45, SuperphysicalChecksIndex = 46;
    internal const string SourceCommit = "b81e7064d0c568f3c7972bb75744e3ecd9d87535";
    internal sealed record Result(int[] Own, int[] Opponent, float[] Globals, int[] Features,
        int BaselineCentipawns, int SacCentipawns);
    internal static class SI
    {
        internal const int MAT_A = 0;
        internal const int MAT_I = 10;
        internal const int CENT = 20;
        internal const int PAWN_ADV = 25;
        internal const int PAWN_NEAR = 26;
        internal const int KING_CENT = 27;
        internal const int SLOTS = 28;
        internal const int K_HOLES = 29;
        internal const int K_KNIGHTS = 30;
        internal const int K_OPEN_ORTH = 31;
        internal const int K_OPEN_DIAG = 32;
        internal const int K_SHIELD = 33;
        internal const int K_ZONE_ATT = 34;
        internal const int MOB = 35;
        internal const int HANGING = 40;
        internal const int ROYAL_ATT = 41;
        internal const int TRAVEL_PAST = 42;
        internal const int TRAVEL_NOW = 43;
        internal const int TRAVEL_BACKED = 44;
        internal const int WEAK_SQ = 45;
        internal const int OPEN_ORTH5 = 46;
        internal const int OPEN_DIAG5 = 47;
        internal const int OPEN_MULTI5 = 48;
        internal const int KING_OFF = 49;
        internal const int KING_OFF_NEW = 50;
        internal const int LOW_FLIGHT = 51;
        internal const int EXILED = 52;
        internal const int INACTIVE_ROYAL_ATT = 53;
        internal const int COUNT = 54;
    }
    internal static class EX
    {
        internal const int PASSED = 0;
        internal const int DOUBLED = 1;
        internal const int ISOLATED = 2;
        internal const int KING_PROX = 3;
        internal const int UNDEVELOPED = 4;
        internal const int KING_PROX_5D = 5;
        internal const int QUEEN_PROX = 6;
        internal const int F7_WEAK = 7;
        internal const int F7_OPEN = 8;
        internal const int F7_QREACH = 9;
        internal const int F7_BROUTE = 10;
        internal const int KN_CHECKS = 11;
        internal const int SAC_LINE = 12;
        internal const int SAC_CLOSE = 13;
        internal const int SAC_NOW = 14;
        internal const int SAC_OPEN = 15;
        internal const int ZONE_HOT = 16;
        internal const int WEAK_HOT = 17;
        internal const int DMAX5 = 18;
        internal const int ZONE_MAX = 19;
        internal const int K_DEF = 20;
        internal const int K_FLIGHT = 21;
        internal const int TL_LOST = 22;
        internal const int TO_MOVE = 23;
        internal const int ZONE_MAND = 24;
        internal const int DEF_BOARD = 25;
        internal const int DEF_HOT = 26;
        internal const int TRAVEL_HOT = 27;
        internal const int KINGLESS = 28;
        internal const int KL_SWARM = 29;
        internal const int COUNT = 30;
    }
    internal static readonly string[] FeatureNames = [
        "mat_a_pawn", "mat_a_brawn", "mat_a_knight", "mat_a_bishop", "mat_a_rook", "mat_a_queen",
        "mat_a_princess", "mat_a_unicorn", "mat_a_dragon", "mat_a_cking", "mat_i_pawn", "mat_i_brawn",
        "mat_i_knight", "mat_i_bishop", "mat_i_rook", "mat_i_queen", "mat_i_princess", "mat_i_unicorn",
        "mat_i_dragon", "mat_i_cking", "cent_knight", "cent_bishop", "cent_rook", "cent_queen",
        "cent_other", "pawn_adv", "pawn_near", "king_cent", "tl_slots", "tl_slots_heavy",
        "k_holes", "k_knights", "k_open_orth", "k_open_diag", "k_shield", "k_zone_att",
        "mob_knight", "mob_bishop", "mob_rook", "mob_queen", "mob_king", "hanging",
        "royal_att", "tempo", "active_lines", "checks_5d", "sp_checks", "mandatory",
        "passed", "doubled", "isolated", "king_prox", "undeveloped", "king_prox_5d",
        "queen_prox", "travel_past", "travel_now", "travel_backed", "weak_sq", "open_orth5",
        "open_diag5", "open_multi5", "king_off", "king_off_new", "low_flight", "exiled",
        "inactive_royal_att", "travel_stm", "tl_slots_threat", "tl_slots_stm", "f7_weak", "f7_open",
        "f7_qreach", "f7_broute", "kn_checks", "sac_line", "sac_close", "sac_now",
        "sac_now_stm", "sac_open", "zone_hot", "weak_hot", "dmax5", "zone_max",
        "k_def", "k_flight", "tl_lost", "to_move", "zone_mand_stm", "zone_mand_opp",
        "def_hot", "def_board", "travel_hot", "kingless", "kl_swarm",
    ];
    internal static readonly float[] TunedWeights = [
        484.2f, 150.0f, 845.5f, 1553.5f, 944.9f, 2464.5f, 899.8f, 250.0f, 150.0f, 195.3f,
        -83.8f, 74.9f, 219.3f, 630.7f, 316.4f, 1097.3f, 450.0f, 125.1f, 74.9f, 150.0f,
        97.5f, -50.9f, 52.0f, -6.4f, 142.7f, -28.2f, 302.0f, -123.6f, 2332.7f, -98.2f,
        -224.7f, 2526.0f, -511.5f, 31.5f, 624.7f, 234.0f, 76.0f, 129.6f, 106.9f, 91.3f,
        93.1f, -15.6f, -2076.0f, -106.2f, 142.2f, 0.0f, 0.0f, 278.9f, 64.0f, -2.4f,
        -29.8f, 972.7f, -381.6f, 84.5f, -341.1f, 54.9f, 532.9f, -567.5f, 380.5f, 0.0f,
        -20.2f, -20.5f, -847.1f, -1536.2f, 746.0f, -11.8f, -4.4f, 608.4f, 552.0f, -415.3f,
        26.4f, 62.9f, 248.4f, -11.6f, 296.2f, 600.0f, 100.0f, 0.0f, 700.0f, 150.0f,
        460.2f, -74.7f, -197.1f, -430.7f, -15.5f, 226.9f, -92.7f, 372.2f, -20.4f, 732.9f,
        -76.5f, -15.5f, -129.1f, -171.3f, -313.5f,
    ];
    internal static readonly string[] SideNames = [
        "mat_a_pawn", "mat_a_brawn", "mat_a_knight", "mat_a_bishop", "mat_a_rook", "mat_a_queen",
        "mat_a_princess", "mat_a_unicorn", "mat_a_dragon", "mat_a_cking", "mat_i_pawn", "mat_i_brawn",
        "mat_i_knight", "mat_i_bishop", "mat_i_rook", "mat_i_queen", "mat_i_princess", "mat_i_unicorn",
        "mat_i_dragon", "mat_i_cking", "cent_knight", "cent_bishop", "cent_rook", "cent_queen",
        "cent_other", "pawn_adv", "pawn_near", "king_cent", "slots", "k_holes",
        "k_knights", "k_open_orth", "k_open_diag", "k_shield", "k_zone_att", "mob_knight",
        "mob_bishop", "mob_rook", "mob_queen", "mob_king", "hanging", "royal_att",
        "travel_past", "travel_now", "travel_backed", "weak_sq", "open_orth5", "open_diag5",
        "open_multi5", "king_off", "king_off_new", "low_flight", "exiled", "inactive_royal_att",
    ];

    // Kairos's bitboards use x + 8*y even on smaller boards; off-board squares
    // are walls, belonging to both colors. Color indices here are white=0/black=1,
    // deliberately independent of GameState's opposite bool convention.
    internal sealed class BitBoard
    {
        internal readonly ulong[] Raw = new ulong[12];
        internal readonly int Width, Height;
        internal ulong White => Raw[0];
        internal ulong Black => Raw[1];
        internal ulong Royal => Raw[2];
        internal ulong LKing => Raw[3];
        internal ulong LKnight => Raw[4];
        internal ulong LPawn => Raw[5];
        internal ulong LRawn => Raw[6];
        internal ulong LRook => Raw[7];
        internal ulong LBishop => Raw[8];
        internal ulong LUnicorn => Raw[9];
        internal ulong LDragon => Raw[10];
        internal ulong Unmoved => Raw[11];
        internal ulong Wall => White & Black;
        internal ulong Occupied => White | Black;
        internal ulong Pawn => LPawn & ~LRawn;
        internal ulong Brawn => LPawn & LRawn;
        internal ulong Knight => LKnight;
        internal ulong Bishop => LBishop & ~LRook;
        internal ulong Rook => LRook & ~LBishop;
        internal ulong Queen => LRook & LDragon & ~Royal;
        internal ulong Princess => LRook & LBishop & ~LUnicorn;
        internal ulong Unicorn => LUnicorn & ~LDragon;
        internal ulong Dragon => LDragon & ~LUnicorn;
        internal ulong CommonKing => LKing & ~Royal;
        internal ulong Friendly(int color) => color == 0 ? White : Black;
        internal ulong Mine(int color) => Friendly(color) & ~Friendly(1 - color);
        internal ulong Component(int axes) => axes switch { 1 => LRook, 2 => LBishop, 3 => LUnicorn, 4 => LDragon, _ => 0 };
        internal BitBoard(Board board)
        {
            Width = board.Width; Height = board.Height;
            Read(board, Raw);
        }
        internal BitBoard(int width, int height, ReadOnlySpan<ulong> raw)
        { Width = width; Height = height; raw.CopyTo(Raw); }
        internal static void Read(Board board, Span<ulong> raw)
        {
            int width = board.Width, height = board.Height;
            if (width is < 1 or > 8 || height is < 1 or > 8)
                throw new NotSupportedException("Kairos evaluation supports boards from 1x1 through 8x8.");
            raw.Clear();
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                ulong z = Mask(x + 8 * y);
                if (x >= width || y >= height) { raw[0] |= z; raw[1] |= z; continue; }
                int signed = board.GetSquare(x, y), piece = Math.Abs(signed);
                if (piece == 0) continue;
                if (piece == Math.Abs(Board.ERRORSQUARE)) { raw[0] |= z; raw[1] |= z; continue; }
                int color = piece > Board.NUMTYPES ? 1 : 0;
                int kind = (piece - 1) % Board.NUMTYPES + 1;
                raw[color] |= z;
                if (signed < 0) raw[11] |= z;
                switch ((Board.Piece)kind)
                {
                    case Board.Piece.WPAWN: raw[5] |= z; break;
                    case Board.Piece.WBRAWN: raw[5] |= z; raw[6] |= z; break;
                    case Board.Piece.WKNIGHT: raw[4] |= z; break;
                    case Board.Piece.WBISHOP: raw[8] |= z; break;
                    case Board.Piece.WROOK: raw[7] |= z; break;
                    case Board.Piece.WPRINCESS: raw[7] |= z; raw[8] |= z; break;
                    case Board.Piece.WQUEEN:
                    case Board.Piece.WROYALQUEEN:
                        raw[7] |= z; raw[8] |= z; raw[9] |= z; raw[10] |= z;
                        if (kind == (int)Board.Piece.WROYALQUEEN) raw[2] |= z;
                        break;
                    case Board.Piece.WKING: raw[2] |= z; raw[3] |= z; break;
                    case Board.Piece.WCOMMONKING: raw[3] |= z; break;
                    case Board.Piece.WUNICORN: raw[9] |= z; break;
                    case Board.Piece.WDRAGON: raw[10] |= z; break;
                    default: throw new InvalidDataException($"Unknown piece {signed} in Kairos evaluation.");
                }
            }
        }
        internal ulong Hash() => Hash(Raw);
        internal static ulong Hash(ReadOnlySpan<ulong> raw)
        {
            ulong key = 0x9e3779b97f4a7c15UL;
            foreach (ulong x in raw) { key = unchecked((key ^ x) * 0xbf58476d1ce4e5b9UL); key ^= key >> 29; }
            return key;
        }
    }

    private sealed class BoardTerms
    {
        internal readonly BitBoard Board;
        internal readonly int[][] Side = [new int[SideCount], new int[SideCount]];
        internal readonly int[][] Extra = [new int[ExtraCount], new int[ExtraCount]];
        internal readonly ulong[] Att = new ulong[2], AttNR = new ulong[2];
        internal int Heavy;
        internal BoardTerms(BitBoard b) { Board = b; }
    }

    // A bounded per-worker exact-content cache: no locks, no mutable Board references,
    // and no cache hits based only on a hash. Historical boards dominate repeat work.
    [ThreadStatic] private static BoardTerms?[]? _boardCache;
    private static BoardTerms Terms(Board board)
    {
        Span<ulong> raw = stackalloc ulong[12];
        BitBoard.Read(board, raw);
        var cache = _boardCache ??= new BoardTerms[4096];
        int slot = (int)(BitBoard.Hash(raw) >> 52);
        var old = cache[slot];
        if (old != null && old.Board.Width == board.Width && old.Board.Height == board.Height &&
            raw.SequenceEqual(old.Board.Raw)) return old;
        return cache[slot] = ComputeBoard(new BitBoard(board.Width, board.Height, raw));
    }

    private const ulong AFile = 0x0101010101010101UL, HFile = 0x8080808080808080UL;
    internal static readonly (int X, int Y)[] Orth = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    internal static readonly (int X, int Y)[] Diag = [(1, 1), (1, -1), (-1, 1), (-1, -1)];
    internal static readonly (int X, int Y)[] XY9 = [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];
    private static readonly (int X, int Y, int T)[] KnightPast =
        [(2, 0, -1), (-2, 0, -1), (0, 2, -1), (0, -2, -1), (1, 0, -2), (-1, 0, -2), (0, 1, -2), (0, -1, -2)];
    private static readonly ulong[] KnightMasks = Enumerable.Range(0, 64).Select(BuildKnightAttack).ToArray();
    internal static ulong Mask(int square) => 1UL << square;
    internal static int Count(ulong b) => BitOperations.PopCount(b);
    // Most callers enumerate this in tight board loops. A value enumerator keeps
    // that path allocation-free while preserving the descending source order.
    internal static SquareSequence Squares(ulong bits) => new(bits);
    internal readonly struct SquareSequence(ulong bits) : IEnumerable<int>
    {
        public Enumerator GetEnumerator() => new(bits);
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public struct Enumerator(ulong bits) : IEnumerator<int>
        {
            private ulong remaining = bits;
            public int Current { get; private set; }
            object IEnumerator.Current => Current;
            public bool MoveNext()
            {
                if (remaining == 0) return false;
                Current = 63 - BitOperations.LeadingZeroCount(remaining);
                remaining ^= Mask(Current);
                return true;
            }
            public void Dispose() { }
            void IEnumerator.Reset() => throw new NotSupportedException();
        }
    }
    internal static ulong Shift(ulong b, int dx, int dy)
    {
        for (int x = 0; x < Math.Abs(dx); x++) b = dx > 0 ? (b & ~HFile) << 1 : (b & ~AFile) >> 1;
        int shift = 8 * Math.Abs(dy);
        if (shift >= 64) return 0;
        return dy >= 0 ? b << shift : b >> shift;
    }
    private static ulong Spread1(ulong b)
    {
        ulong h = b | Shift(b, 1, 0) | Shift(b, -1, 0);
        return h | (h << 8) | (h >> 8);
    }
    internal static ulong KingAttack(int square) => Spread1(Mask(square)) & ~Mask(square);
    internal static ulong KnightAttack(int square) => KnightMasks[square];
    private static ulong BuildKnightAttack(int square)
    {
        ulong b = Mask(square), a = 0;
        for (int dx = -2; dx <= 2; dx++)
        {
            if (dx == 0) continue;
            int dy = 3 - Math.Abs(dx);
            a |= Shift(b, dx, dy) | Shift(b, dx, -dy);
        }
        return a;
    }
    private static ulong Ring2(int square) => Spread1(Spread1(Mask(square)));
    internal static ulong SliderAttack(int square, ulong occupied, bool rook, bool bishop)
    {
        ulong result = 0;
        foreach (var (dx, dy) in XY9)
        {
            if (dx == 0 && dy == 0 || (dx == 0 || dy == 0 ? !rook : !bishop)) continue;
            ulong at = Mask(square);
            for (int step = 0; step < 7; step++)
            {
                at = Shift(at, dx, dy); if (at == 0) break;
                result |= at; if ((at & occupied) != 0) break;
            }
        }
        return result;
    }
    private static int Centrality(int square, int w, int h) =>
        Math.Max(0, w + h - 2 - Math.Abs(2 * (square & 7) - w + 1) - Math.Abs(2 * (square >> 3) - h + 1)) / 2;
    private static int OpenRays(int square, ulong own, (int X, int Y)[] dirs, int w, int h)
    {
        int result = 0;
        foreach (var (dx, dy) in dirs)
        {
            int x = (square & 7) + dx, y = (square >> 3) + dy;
            bool open = true;
            while (x >= 0 && y >= 0 && x < w && y < h)
            {
                if ((own & Mask(x + 8 * y)) != 0) { open = false; break; }
                x += dx; y += dy;
            }
            if (open) result++;
        }
        return result;
    }
    private static int KindValue10(BitBoard b, int square)
    {
        ulong z = Mask(square);
        if ((b.LPawn & z) != 0) return 10;
        if ((b.LKnight & z) != 0) return 40;
        if ((b.LRook & b.LDragon & z) != 0) return 125;
        if ((b.LRook & b.LBishop & z) != 0) return 90;
        if ((b.LBishop & z) != 0) return 45;
        if ((b.LRook & z) != 0) return 33;
        if ((b.LKing & ~b.Royal & z) != 0) return 30;
        if ((b.LUnicorn & z) != 0) return 25;
        return (b.LDragon & z) != 0 ? 15 : 0;
    }
    internal static (ulong Attacks, ulong NonRoyal, int[] Mobility) SideAttacks(BitBoard b, int c)
    {
        ulong own = b.Friendly(c), mine = b.Mine(c), occ = b.Occupied, attacks = 0, nonRoyal = 0;
        int[] mobility = new int[5];
        foreach (int square in Squares(mine))
        {
            ulong z = Mask(square), attack;
            if ((b.LPawn & z) != 0)
                attack = Shift(z, -1, c == 0 ? 1 : -1) | Shift(z, 1, c == 0 ? 1 : -1);
            else if ((b.LKnight & z) != 0)
            { attack = KnightAttack(square); mobility[0] += Count(attack & ~own); }
            else if ((b.LKing & z) != 0)
            { attack = KingAttack(square); mobility[4] += Count(attack & ~own); }
            else
            {
                bool r = (b.LRook & z) != 0, bi = (b.LBishop & z) != 0;
                attack = SliderAttack(square, occ, r, bi);
                int index = r && bi ? 3 : r ? 2 : bi ? 1 : -1;
                if (index >= 0) mobility[index] += Count(attack & ~own);
            }
            attacks |= attack;
            if ((b.Royal & z) == 0) nonRoyal |= attack;
        }
        return (attacks, nonRoyal, mobility);
    }

    private static BoardTerms ComputeBoard(BitBoard b)
    {
        var terms = new BoardTerms(b);
        ReadOnlySpan<ulong> kinds = [b.Pawn, b.Brawn, b.Knight, b.Bishop, b.Rook, b.Queen, b.Princess, b.Unicorn, b.Dragon, b.CommonKing];
        ReadOnlySpan<ulong> groups = [b.Knight, b.Bishop, b.Rook, b.Queen | b.Princess, b.Unicorn | b.Dragon | b.CommonKing];
        var attacks = new[] { SideAttacks(b, 0), SideAttacks(b, 1) };
        for (int c = 0; c < 2; c++)
        {
            ulong me = b.Mine(c), them = b.Mine(1 - c), myAtt = attacks[c].Attacks, theirAtt = attacks[1 - c].Attacks;
            terms.Att[c] = myAtt; terms.AttNR[c] = attacks[c].NonRoyal;
            int[] f = terms.Side[c];
            for (int k = 0; k < 10; k++) f[SI.MAT_A + k] = Count(kinds[k] & me);
            for (int group = 0; group < 5; group++)
                foreach (int square in Squares(groups[group] & me)) f[SI.CENT + group] += Centrality(square, b.Width, b.Height);
            foreach (int square in Squares(b.LPawn & me))
            {
                int rank = square >> 3, advance = c == 1 ? b.Height - 1 - rank : rank;
                f[SI.PAWN_ADV] += Math.Max(0, advance - 1);
                f[SI.PAWN_NEAR] += advance >= b.Height - 3 ? 1 : 0;
            }
            for (int k = 0; k < 5; k++) f[SI.MOB + k] += attacks[c].Mobility[k];
            foreach (int king in Squares(b.Royal & me))
            {
                ulong zone = KingAttack(king);
                f[SI.KING_CENT] += Centrality(king, b.Width, b.Height);
                f[SI.K_HOLES] += Count(zone & ~b.Occupied);
                f[SI.K_KNIGHTS] += Count(KnightAttack(king) & b.Knight & them);
                f[SI.K_OPEN_ORTH] += OpenRays(king, b.Friendly(c), Orth, b.Width, b.Height);
                f[SI.K_OPEN_DIAG] += OpenRays(king, b.Friendly(c), Diag, b.Width, b.Height);
                ulong forward = Shift(Mask(king), 0, c == 1 ? -1 : 1);
                f[SI.K_SHIELD] += Count((forward | Shift(forward, -1, 0) | Shift(forward, 1, 0)) & b.LPawn & me);
                f[SI.K_ZONE_ATT] += Count(zone & theirAtt);
                if ((theirAtt & Mask(king)) != 0) f[SI.ROYAL_ATT]++;
            }
            foreach (int square in Squares(me & ~b.Royal & theirAtt & ~myAtt)) f[SI.HANGING] += KindValue10(b, square);
        }
        terms.Heavy = Count(b.Queen | b.Bishop | b.Princess);
        BoardExtra(b, terms);
        return terms;
    }

    private static void BoardExtra(BitBoard b, BoardTerms terms)
    {
        ulong occ = b.Occupied;
        for (int c = 0; c < 2; c++)
        {
            ulong me = b.Mine(c), them = b.Mine(1 - c), ownP = b.LPawn & me, theirP = b.LPawn & them;
            int[] f = terms.Extra[c];
            foreach (int square in Squares(ownP))
            {
                int x = square & 7, y = square >> 3;
                ulong file = AFile << x, adjacent = (x > 0 ? AFile << (x - 1) : 0) | (x < 7 ? AFile << (x + 1) : 0);
                ulong ahead = c == 0 ? (y >= 7 ? 0 : ulong.MaxValue << (8 * (y + 1))) : y == 0 ? 0 : (1UL << (8 * y)) - 1;
                if ((theirP & (file | adjacent) & ahead) == 0) f[EX.PASSED]++;
                if ((ownP & file & ahead) != 0) f[EX.DOUBLED]++;
                if ((ownP & adjacent) == 0) f[EX.ISOLATED]++;
            }
            ulong pieces = me & ~b.LPawn & ~b.Royal;
            foreach (int king in Squares(b.Royal & them))
            {
                ulong ring = Ring2(king);
                f[EX.KING_PROX] += Count(ring & pieces);
                f[EX.QUEEN_PROX] += Count(ring & b.Queen & me);
            }
            ulong back = c == 0 ? 0xffUL : 0xffUL << (8 * (b.Height - 1));
            f[EX.UNDEVELOPED] += Count((b.Knight | b.Bishop) & me & back);
            foreach (int king in Squares(b.Royal & me))
            {
                int defended = Count(KingAttack(king) & terms.AttNR[c]);
                f[EX.K_DEF] += defended; f[EX.DEF_BOARD] += defended;
            }
            if ((b.Royal & me) == 0)
            {
                f[EX.KINGLESS]++;
                f[EX.KL_SWARM] += Count(them & ~b.LPawn & ~b.Royal);
            }
            ulong royals = b.Royal & them;
            if (royals == 0) continue;
            ulong queens = b.Queen & me, qReach = queens, knightTo = 0;
            foreach (int queen in Squares(queens)) qReach |= SliderAttack(queen, occ, true, true);
            foreach (int knight in Squares(b.Knight & me)) knightTo |= KnightAttack(knight);
            knightTo &= ~me;
            int home = c == 0 ? b.Height - 1 : 0, dy = c == 0 ? -1 : 1;
            // Upstream assumes at least two ranks; one-rank variants have no front rank.
            int front = home + dy;
            ulong frontRank = front is >= 0 and < 8 ? 0xffUL << (8 * front) : 0;
            ulong bishops = b.LBishop & ~b.LRook & me;
            int myBack = c == 0 ? 0 : b.Height - 1;
            foreach (int king in Squares(royals))
            {
                f[EX.KN_CHECKS] += Count(knightTo & KnightAttack(king));
                if (king >> 3 != home) continue;
                ulong weak = KingAttack(king) & frontRank & ~terms.AttNR[1 - c] & ~(occ & ~(b.LPawn & them));
                foreach (int square in Squares(weak))
                {
                    f[EX.F7_WEAK]++;
                    ulong lines = 0; bool queenOn = false;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = (square & 7) + dx, y = (square >> 3) + dy, n = 0;
                        while (x >= 0 && y >= 0 && x < b.Width && y < b.Height)
                        {
                            ulong z = Mask(x + 8 * y);
                            if ((occ & z) != 0) { queenOn |= (queens & z) != 0; break; }
                            lines |= z; n++; x += dx; y += dy;
                        }
                        if (n >= 2) f[EX.F7_OPEN]++;
                    }
                    if (queenOn || (qReach & lines) != 0) f[EX.F7_QREACH]++;
                    int file = square & 7, pawnRank = myBack - dy;
                    if ((bishops & Mask(file + 8 * myBack)) != 0 && pawnRank is >= 0 and < 8 &&
                        (b.LPawn & me & Mask(file + 8 * pawnRank)) == 0) f[EX.F7_BROUTE]++;
                }
            }
        }
    }

    private sealed class Sides
    {
        internal readonly int[][] Side = [new int[SideCount], new int[SideCount]];
        internal readonly int[][] Extra = [new int[ExtraCount], new int[ExtraCount]];
        internal int Heavy, Active, Inactive, Mandatory;
    }
    private sealed class Geometry
    {
        internal readonly GameState State;
        internal readonly Dictionary<int, Timeline> Lines;
        internal readonly Dictionary<int, BoardTerms> Latest = [];
        private readonly Dictionary<Board, BoardTerms> _history = new(ReferenceEqualityComparer.Instance);
        private Dictionary<Board, BitBoard>? _extraBits;
        internal Geometry(GameState state)
        {
            State = state;
            Lines = state.Multiverse.ToDictionary(line => line.Layer);
            foreach (var line in state.Multiverse) Latest[line.Layer] = GetTerms(line.GetPlayableBoard());
        }
        internal BoardTerms GetTerms(Board board)
        {
            if (!_history.TryGetValue(board, out var terms)) _history.Add(board, terms = Terms(board));
            return terms;
        }
        internal BitBoard GetBits(Board board)
        {
            if (_history.TryGetValue(board, out var terms)) return terms.Board;
            // SAC can inspect history that other terms did not need (for
            // example after the last temporal attacker has disappeared). Do
            // not compute its unrelated physical evaluation just for a lookup.
            var bits = _extraBits ??= new(ReferenceEqualityComparer.Instance);
            if (!bits.TryGetValue(board, out BitBoard? result)) bits.Add(board, result = new(board));
            return result;
        }
        internal int End(int layer)
        {
            var line = Lines[layer]; return 2 * line.TEnd + (line.ColorPlayable ? 0 : 1);
        }
        internal BoardTerms? At(int layer, int halfTurn)
        {
            if (!Lines.TryGetValue(layer, out var line)) return null;
            int end = End(layer);
            if (halfTurn == end + 1) return Latest[layer];
            bool white = (halfTurn & 1) == 0;
            int time = (halfTurn - (white ? 0 : 1)) / 2;
            if (white)
                return time >= line.WhiteStart && time <= line.WhiteEnd ? GetTerms(line.WBoards[time - line.WhiteStart]) : null;
            return time >= line.BlackStart && time <= line.BlackEnd ? GetTerms(line.BBoards[time - line.BlackStart]) : null;
        }
    }
    private static int SourceHalfTurn(int end, int color) => (end & 1) == color ? end : end + 1;

    private static Sides Extract(GameState state, int present, bool includeSacrifice = true)
    {
        var sd = new Sides();
        var g = new Geometry(state);
        foreach (var line in state.Multiverse)
        {
            int layer = line.Layer, color = line.ColorPlayable ? 0 : 1;
            var bt = g.Latest[layer];
            if (state.LayerIsActive(layer))
            {
                for (int c = 0; c < 2; c++)
                {
                    for (int i = 0; i < SideCount; i++) sd.Side[c][i] += bt.Side[c][i];
                    for (int i = 0; i < ExtraCount; i++) sd.Extra[c][i] += bt.Extra[c][i];
                }
                sd.Heavy += bt.Heavy; sd.Active++;
                bool mandatory = g.End(layer) == present;
                if (mandatory) sd.Mandatory++;
                sd.Extra[color][EX.TO_MOVE]++;
                for (int d = 0; d < 2; d++)
                {
                    int zone = bt.Side[d][SI.K_ZONE_ATT];
                    if (color != d)
                    {
                        sd.Extra[d][EX.DEF_HOT] += bt.Extra[d][EX.DEF_BOARD];
                        sd.Extra[d][EX.ZONE_HOT] += zone;
                    }
                    if (mandatory) sd.Extra[d][EX.ZONE_MAND] += zone;
                    sd.Extra[d][EX.ZONE_MAX] = Math.Max(sd.Extra[d][EX.ZONE_MAX], zone);
                }
            }
            else
            {
                for (int c = 0; c < 2; c++) for (int k = 0; k < 10; k++) sd.Side[c][SI.MAT_I + k] += bt.Side[c][SI.MAT_A + k];
                sd.Inactive++;
                if (layer > state.InitialMaxTL) sd.Extra[0][EX.TL_LOST]++;
                else if (layer < state.InitialMinTL) sd.Extra[1][EX.TL_LOST]++;
            }
        }
        foreach (var line in state.Multiverse)
        {
            if (!state.LayerIsActive(line.Layer)) continue;
            var b = g.Latest[line.Layer].Board;
            for (int color = 0; color < 2; color++)
                foreach (int king in Squares(b.Royal & b.Mine(color)))
                {
                    ulong ring = Ring2(king);
                    for (int neighbor = line.Layer - 2; neighbor <= line.Layer + 2; neighbor++)
                    {
                        if (neighbor == line.Layer || !g.Latest.TryGetValue(neighbor, out var bt)) continue;
                        var b2 = bt.Board;
                        sd.Extra[1 - color][EX.KING_PROX_5D] += Count(ring & b2.Mine(1 - color) & ~b2.LPawn & ~b2.Royal);
                    }
                }
        }
        Royal5D(g, sd);
        if (includeSacrifice) SacrificeTable.Fill(state, sd.Extra, g.GetBits);
        int whiteCreated = state.MaxTL - state.InitialMaxTL, blackCreated = state.InitialMinTL - state.MinTL;
        sd.Side[0][SI.SLOTS] = Math.Clamp(1 + blackCreated - whiteCreated, 0, 4);
        sd.Side[1][SI.SLOTS] = Math.Clamp(1 + whiteCreated - blackCreated, 0, 4);
        return sd;
    }

    private sealed record PastEntry(int Color, ulong[] Movers, BitBoard[] Boards, int Landings, int Backed);
    [InlineArray(8)]
    private struct PastBoardBuffer { private BoardTerms _first; }
    [ThreadStatic] private static PastEntry?[]? _pastCache;
    private static ulong Mix(ulong key, ulong x)
    { key = unchecked((key ^ x) * 0xbf58476d1ce4e5b9UL); return key ^ (key >> 31); }

    private static (int Landings, int Backed) PastLine(Geometry g, int layer, int color)
    {
        var b0 = g.Latest[layer].Board;
        int source = SourceHalfTurn(g.End(layer), color), n = 0;
        ulong mine = b0.Mine(color);
        ReadOnlySpan<ulong> movers = [b0.LRook & mine, b0.LBishop & mine, b0.LUnicorn & mine, b0.LKnight & mine];
        if ((movers[0] | movers[1] | movers[2] | movers[3]) == 0) return (0, 0);
        PastBoardBuffer boards = default;
        Span<ulong> zone = stackalloc ulong[8];
        Span<ulong> hits = stackalloc ulong[8];
        hits.Clear();
        ulong zones = 0, key = Mix(0x9e3779b97f4a7c15UL, (ulong)color);
        foreach (ulong mover in movers) key = Mix(key, mover);
        while (n < 8)
        {
            var terms = g.At(layer, source - 2 * (n + 1));
            if (terms == null) break;
            var b = terms.Board;
            boards[n] = terms;
            ulong royal = b.Royal & b.Mine(1 - color);
            zone[n] = Spread1(royal) & ~royal & ~b.Friendly(color);
            zones |= zone[n]; key = Mix(key, b.Hash()); n++;
        }
        if (zones == 0) return (0, 0);
        var cache = _pastCache ??= new PastEntry[4096];
        int slot = (int)(key >> 52);
        var old = cache[slot];
        if (old != null && old.Color == color && old.Boards.Length == n && old.Movers.AsSpan().SequenceEqual(movers))
        {
            bool same = true;
            for (int k = 0; k < n; k++)
                if (!old.Boards[k].Raw.AsSpan().SequenceEqual(boards[k].Board.Raw)) { same = false; break; }
            if (same) return (old.Landings, old.Backed);
        }
        // Pre-filter by the union of landing zones, then walk the same stepped
        // bitboards as the optimized upstream past_line implementation.
        foreach (var (sx, sy) in XY9)
        {
            ulong pos = movers[(sx != 0 ? 1 : 0) + (sy != 0 ? 1 : 0)];
            ulong reachable = zones, back = zones;
            for (int k = 0; k < 7; k++) { back = Shift(back, -sx, -sy); reachable |= back; }
            if ((pos & reachable) == 0) continue;
            for (int k = 0; k < n; k++)
            {
                pos = Shift(pos, sx, sy); if (pos == 0) break;
                hits[k] |= pos & zone[k]; pos &= ~boards[k].Board.Occupied;
            }
        }
        ulong knights = movers[3];
        if (knights != 0)
            foreach (var (dx, dy, dt) in KnightPast)
            {
                int k = -dt - 1;
                if (k < n) hits[k] |= Shift(knights, dx, dy) & zone[k];
            }
        int past = 0, backed = 0;
        for (int k = 0; k < n; k++)
        {
            ulong weak = hits[k] & ~boards[k].AttNR[1 - color];
            past += Count(weak); backed += Count(weak & boards[k].Att[color]);
        }
        var cachedBoards = new BitBoard[n];
        for (int k = 0; k < n; k++) cachedBoards[k] = boards[k].Board;
        cache[slot] = new PastEntry(color, movers.ToArray(), cachedBoards, past, backed);
        return (past, backed);
    }

    private static void Landing(ulong target, ulong defenders, ulong attackers, ref ulong travelAttack,
        ref int count, ref int backed)
    {
        travelAttack |= target;
        ulong weak = target & ~defenders;
        count += Count(weak); backed += Count(weak & attackers);
    }

    private static void Royal5D(Geometry g, Sides sd)
    {
        var state = g.State;
        foreach (var line in state.Multiverse)
            for (int color = 0; color < 2; color++)
            {
                var past = PastLine(g, line.Layer, color);
                sd.Side[1 - color][SI.TRAVEL_PAST] += past.Landings;
                sd.Side[1 - color][SI.TRAVEL_BACKED] += past.Backed;
            }
        Span<bool> royalsActive = stackalloc bool[2], royalsAny = stackalloc bool[2];
        royalsActive.Clear(); royalsAny.Clear();
        // Reuse these small workspaces across boards instead of allocating per ray.
        Span<ulong> rays = stackalloc ulong[27];
        Span<int> walk = stackalloc int[4];
        Span<ulong> occ = stackalloc ulong[3], block = stackalloc ulong[3], attackers = stackalloc ulong[12], any = stackalloc ulong[4];
        Span<int> open = stackalloc int[3];
        foreach (var targetLine in state.Multiverse)
        {
            int layer2 = targetLine.Layer, end2 = g.End(layer2);
            var targetTerms = g.Latest[layer2];
            var b2 = targetTerms.Board;
            if (b2.Royal == 0) continue;
            for (int color = 0; color < 2; color++)
            {
                int defender = 1 - color;
                ulong royal = b2.Royal & b2.Mine(defender);
                if (royal == 0) continue;
                royalsAny[color] = true;
                ulong attColor = targetTerms.Att[color], defNR = targetTerms.AttNR[defender];
                if (!state.LayerIsActive(layer2))
                {
                    sd.Side[defender][SI.INACTIVE_ROYAL_ATT] += Count(royal & attColor);
                    continue;
                }
                royalsActive[color] = true;
                int v2 = SourceHalfTurn(end2, color);
                ulong zone = Spread1(royal) & ~royal & ~b2.Wall, land = zone & ~b2.Friendly(color), travelAttack = 0;
                int now = 0, backed = 0;
                open.Clear(); walk.Clear();
                ulong target = land | royal;
                for (int j = 0; j < 9; j++)
                {
                    var (sx, sy) = XY9[j]; ulong ray = target;
                    for (int distance = 0; distance < 3; distance++)
                    { ray = Shift(ray, -sx, -sy); rays[distance * 9 + j] = ray; }
                }
                // The latest fast path: inspect only source frontiers at the matching
                // half-turn and rule out every direction with no possible slider.
                for (int offset = -3; offset <= 3; offset++)
                {
                    if (offset == 0 || !g.Latest.TryGetValue(layer2 + offset, out var sourceTerms)) continue;
                    int distance = Math.Abs(offset), dv = SourceHalfTurn(g.End(layer2 + offset), color) - v2;
                    bool slide = dv == 0 || dv == 2 * distance;
                    bool leap = distance <= 2 && (dv == 0 || dv == 6 - 2 * distance);
                    if (!slide && !leap) continue;
                    var b1 = sourceTerms.Board; ulong mine = b1.Mine(color);
                    if (slide)
                    {
                        int timeAxis = dv == 0 ? 0 : 1, bits = 0;
                        for (int j = 0; j < 9; j++)
                        {
                            var (sx, sy) = XY9[j];
                            int axes = 1 + timeAxis + (sx != 0 ? 1 : 0) + (sy != 0 ? 1 : 0);
                            if ((rays[(distance - 1) * 9 + j] & b1.Component(axes) & mine) != 0) bits |= 1 << j;
                        }
                        walk[(offset < 0 ? 1 : 0) + 2 * timeAxis] |= bits;
                    }
                    if (leap)
                    {
                        ulong knights = b1.LKnight & mine;
                        if (knights == 0) continue;
                        if (dv != 0)
                            Landing(knights & land, defNR, attColor, ref travelAttack, ref now, ref backed);
                        else
                        {
                            int shift = distance == 1 ? 2 : 1;
                            foreach (var (dx, dy) in Orth)
                                Landing(Shift(knights, dx * shift, dy * shift) & land, defNR, attColor, ref travelAttack, ref now, ref backed);
                        }
                    }
                }
                for (int direction = 0; direction < 4; direction++)
                {
                    if (walk[direction] == 0) continue;
                    int sl = direction % 2 == 0 ? -1 : 1, st = direction < 2 ? 0 : -1;
                    occ.Clear(); block.Clear(); attackers.Clear(); any.Clear();
                    int n = 0; ulong region = land | royal, near = 0;
                    while (n < 3)
                    {
                        int i = n + 1, layer1 = layer2 - i * sl, v1 = v2 - 2 * i * st;
                        var sourceTerms = g.At(layer1, v1);
                        if (sourceTerms == null) break;
                        var b1 = sourceTerms.Board;
                        occ[n] = b1.Occupied; block[n] = b1.Friendly(defender);
                        region = Spread1(region);
                        if (v1 == SourceHalfTurn(g.End(layer1), color))
                        {
                            ulong mine = b1.Mine(color), combined = 0;
                            for (int axes = 1; axes <= 4; axes++)
                            {
                                ulong a = b1.Component(axes) & mine;
                                attackers[n * 4 + axes - 1] = a; combined |= a; any[axes - 1] |= a;
                            }
                            near |= combined & region;
                        }
                        n++;
                    }
                    if (near == 0) continue;
                    for (int j = 0; j < 9; j++)
                    {
                        var (sx, sy) = XY9[j];
                        int axes = 1 + (st != 0 ? 1 : 0) + (sx != 0 ? 1 : 0) + (sy != 0 ? 1 : 0);
                        if (any[axes - 1] == 0 || (walk[direction] & 1 << j) == 0) continue;
                        ulong candidates = land, royalRay = royal;
                        for (int k = 0; k < n; k++)
                        {
                            candidates = Shift(candidates, -sx, -sy); royalRay = Shift(royalRay, -sx, -sy);
                            if ((candidates | royalRay) == 0) break;
                            ulong a = attackers[k * 4 + axes - 1];
                            if (a != 0)
                            {
                                ulong hits = candidates & a;
                                if (hits != 0)
                                {
                                    for (int step = 0; step <= k; step++) hits = Shift(hits, sx, sy);
                                    Landing(hits, defNR, attColor, ref travelAttack, ref now, ref backed);
                                }
                                ulong royalHits = royalRay & a;
                                if (royalHits != 0)
                                { open[Math.Min(axes, 3) - 1] += Count(royalHits); royalRay &= ~royalHits; }
                            }
                            candidates &= ~occ[k]; royalRay &= ~block[k];
                        }
                    }
                }
                int[] f = sd.Side[defender], extra = sd.Extra[defender];
                f[SI.TRAVEL_NOW] += now; f[SI.TRAVEL_BACKED] += backed;
                f[SI.OPEN_ORTH5] += open[0]; f[SI.OPEN_DIAG5] += open[1]; f[SI.OPEN_MULTI5] += open[2];
                bool hot = v2 == end2;
                if (hot) extra[EX.TRAVEL_HOT] += now;
                ulong hit = attColor | travelAttack;
                int weak = Count(zone & ~defNR & hit);
                f[SI.WEAK_SQ] += weak;
                if (hot) extra[EX.WEAK_HOT] += weak;
                extra[EX.DMAX5] = Math.Max(extra[EX.DMAX5], weak + now);
                int home = defender == 0 ? 0 : state.Height - 1;
                bool created = layer2 < state.InitialMinTL || layer2 > state.InitialMaxTL;
                foreach (int king in Squares(royal))
                {
                    if (king >> 3 != home) { f[SI.KING_OFF]++; if (created) f[SI.KING_OFF_NEW]++; }
                    ulong near = KingAttack(king), flight = near & ~b2.Friendly(defender) & ~hit;
                    if (Count(flight) <= 1 && ((near | Mask(king)) & hit) != 0) f[SI.LOW_FLIGHT]++;
                    extra[EX.K_FLIGHT] += Math.Min(Count(flight), 3);
                }
            }
        }
        for (int color = 0; color < 2; color++) sd.Side[1 - color][SI.EXILED] = royalsAny[color] && !royalsActive[color] ? 1 : 0;
    }

    private static void CombineFeatures(Sides sd, int stm, Span<int> f)
    {
        int D(int i) => sd.Side[0][i] - sd.Side[1][i];
        int E(int i) => sd.Extra[0][i] - sd.Extra[1][i];
        int Capped(int i, int cap) => Math.Min(sd.Extra[0][i], cap) - Math.Min(sd.Extra[1][i], cap);
        for (int k = 0; k < 20; k++) f[k] = D(k);
        for (int group = 0; group < 5; group++)
        { f[20 + group] = D(SI.CENT + group); f[36 + group] = D(SI.MOB + group); }
        f[25] = D(SI.PAWN_ADV); f[26] = D(SI.PAWN_NEAR); f[27] = D(SI.KING_CENT);
        int slots = D(SI.SLOTS), full = 6 * Math.Max(sd.Active, 1), sign = stm == 0 ? 1 : -1;
        f[28] = slots; f[29] = slots * 8 * Math.Min(sd.Heavy, full) / full;
        for (int k = 0; k < 6; k++) f[30 + k] = D(SI.K_HOLES + k);
        f[41] = D(SI.HANGING); f[42] = D(SI.ROYAL_ATT);
        f[TempoIndex] = sign; f[44] = sd.Active;
        // The optional phantom-check scan is filled separately. As in the
        // upstream static evaluator, its zero tuned weights avoid that work.
        f[Checks5DIndex] = f[SuperphysicalChecksIndex] = 0;
        f[47] = sign * sd.Mandatory;
        for (int k = 0; k < 7; k++) f[48 + k] = E(EX.PASSED + k);
        for (int k = 0; k < 12; k++) f[55 + k] = D(SI.TRAVEL_PAST + k);
        int[] opponent = sd.Side[1 - stm];
        f[67] = sign * (opponent[SI.TRAVEL_PAST] + opponent[SI.TRAVEL_NOW]);
        f[68] = sd.Side[0][SI.SLOTS] * RoyalDanger(sd.Side[1]) - sd.Side[1][SI.SLOTS] * RoyalDanger(sd.Side[0]);
        f[69] = sign * sd.Side[stm][SI.SLOTS];
        for (int k = 0; k < 5; k++) f[70 + k] = E(EX.F7_WEAK + k);
        f[75] = Capped(EX.SAC_LINE, 2); f[76] = Capped(EX.SAC_CLOSE, 22);
        f[77] = Capped(EX.SAC_NOW, 1); f[78] = sign * Math.Min(sd.Extra[stm][EX.SAC_NOW], 1);
        f[79] = Capped(EX.SAC_OPEN, 8);
        for (int k = 0; k < 6; k++) f[80 + k] = E(EX.ZONE_HOT + k);
        f[86] = E(EX.TL_LOST); f[87] = E(EX.TO_MOVE);
        f[88] = sign * sd.Extra[stm][EX.ZONE_MAND]; f[89] = sign * sd.Extra[1 - stm][EX.ZONE_MAND];
        f[90] = E(EX.DEF_HOT); f[91] = E(EX.DEF_BOARD); f[92] = E(EX.TRAVEL_HOT);
        f[93] = E(EX.KINGLESS); f[94] = E(EX.KL_SWARM);
    }
    private static int RoyalDanger(int[] f) => Math.Min(6, f[SI.ROYAL_ATT] + f[SI.TRAVEL_PAST] + f[SI.TRAVEL_NOW] +
        f[SI.WEAK_SQ] + f[SI.LOW_FLIGHT] + f[SI.KING_OFF]);

    internal static bool NeedsChecks(ReadOnlySpan<float> weights)
    {
        if (weights.Length != FeatureCount) throw new ArgumentException("Incorrect evaluation weight count.", nameof(weights));
        return weights[Checks5DIndex] != 0 || weights[SuperphysicalChecksIndex] != 0;
    }

    /// <summary>
    /// Private phantom position: advance only the active frontiers at the actual
    /// present without changing their contents. Actor, pending-turn bookkeeping
    /// and present metadata are retained; callers decide whether to submit/flip.
    /// </summary>
    internal static GameState Phantom(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Phantom(state, FindPresent(state));
    }

    private static int FindPresent(GameState state)
    {
        int present = int.MaxValue;
        foreach (Timeline line in state.Multiverse)
            if (state.LayerIsActive(line.Layer))
                present = Math.Min(present, 2 * line.TEnd + (line.ColorPlayable ? 0 : 1));
        return present == int.MaxValue ? (state.Color ? 0 : 1) : present;
    }

    private static GameState Phantom(GameState state, int present)
    {
        var phantom = state.Clone();
        foreach (Timeline line in phantom.Multiverse)
            if (state.LayerIsActive(line.Layer) && 2 * line.TEnd + (line.ColorPlayable ? 0 : 1) == present)
                line.AddNullMove();
        return phantom;
    }

    // Port of hce::features_from(checks=true), adapting CheckPosition's borrowed
    // phantom overlay to a private native-state clone. Only active boards at the
    // actual present are passed. Future and inactive boards are not passed, but
    // their already-playable enemy pieces can still attack any existing board.
    // These are capture counts, not legal moves or a checkmate determination.
    private static void FillCheckFeatures(GameState state, int present, Span<int> features)
    {
        bool defenderWhite = (present & 1) == 0, attackerWhite = !defenderWhite;
        int sign = defenderWhite ? 1 : -1;
        var phantom = Phantom(state, present);
        int total = 0, superphysical = 0;
        foreach (Timeline line in phantom.Multiverse)
        {
            if (line.ColorPlayable != attackerWhite) continue;
            Board board = line.GetPlayableBoard();
            for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++)
            {
                int piece = board.GetSquare(x, y);
                if (piece == Board.EMPTYSQUARE || piece == Board.ERRORSQUARE || Board.GetColorBool(piece) != attackerWhite) continue;
                var source = new CoordFive(x, y, line.TEnd, line.Layer, attackerWhite);
                foreach (CoordFive target in MoveGenerator.GetCaptures(piece, phantom, source))
                {
                    if (!Board.IsRoyalPiece(phantom.GetSquare(target))) continue;
                    total = Math.Min(8, total + 1);
                    // Vec4::tl() contains both time and timeline in the source.
                    // A capture into one's own timeline's past is superphysical.
                    if (target.L != source.L || target.T != source.T) superphysical = Math.Min(8, superphysical + 1);
                    // Both counters are capped at eight. Once temporal captures
                    // reach the cap, no remaining piece can change either term.
                    if (superphysical == 8)
                    {
                        features[Checks5DIndex] = features[SuperphysicalChecksIndex] = sign * 8;
                        return;
                    }
                }
            }
        }
        features[Checks5DIndex] = sign * total;
        features[SuperphysicalChecksIndex] = sign * superphysical;
    }

    private static int Score(int[] features, bool perspective, bool sac)
    {
        float result = 0;
        for (int i = 0; i < FeatureCount; i++)
            result += TunedWeights[i] * ((i is >= 75 and <= 79) == sac ? features[i] : 0);
        result = MathF.Round(result, MidpointRounding.AwayFromZero);
        if (!perspective) result = -result;
        return result >= int.MaxValue ? int.MaxValue : result <= int.MinValue ? int.MinValue : (int)result;
    }

    /// <summary>
    /// Kairos's 95-term linear evaluation and 113 evalnet inputs. Scores use its
    /// internal units (not the separately normalized UCI "score cp" units).
    /// The reported Own/Opponent and scores use the requested perspective; tempo,
    /// mandatory-board and initiative terms still use the actual present player.
    /// By default the optional phantom-check channels run only if their weights
    /// are nonzero, matching the tuned upstream evaluator. Passing includeChecks
    /// explicitly also exposes them for feature audits or tuning. No legal-check
    /// proof, source-state mutation or neural network runs here.
    /// </summary>
    internal static Result Describe(GameState state, bool perspective, bool? includeChecks = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Multiverse.Count == 0) throw new ArgumentException("Kairos evaluation requires at least one timeline.", nameof(state));
        int present = FindPresent(state);
        var sides = Extract(state, present);
        int stm = present & 1, own = perspective ? 0 : 1;
        int[] features = new int[FeatureCount];
        CombineFeatures(sides, stm, features);
        if (includeChecks ?? NeedsChecks(TunedWeights)) FillCheckFeatures(state, present, features);
        int baseline = Score(features, perspective, false), sac = Score(features, perspective, true);
        int full = 6 * Math.Max(sides.Active, 1);
        float[] globals = [sides.Active, sides.Inactive, 8 * Math.Min(sides.Heavy, full) / full,
            Math.Clamp(baseline / 100f, -30f, 30f), sides.Mandatory];
        return new Result(sides.Side[own], sides.Side[1 - own], globals, features, baseline, sac);
    }

    // The v9 model needs only these terms: avoid constructing legacy side/global
    // audit outputs and calculating two unused linear scores on every leaf.
    internal static void FillFeatures(GameState state, Span<int> features)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Multiverse.Count == 0) throw new ArgumentException("Evaluation requires at least one timeline.", nameof(state));
        if (features.Length != FeatureCount) throw new ArgumentException("Incorrect evaluation feature count.", nameof(features));
        int present = FindPresent(state);
        CombineFeatures(Extract(state, present), present & 1, features);
        FillCheckFeatures(state, present, features);
    }

    // Preserve the actor's frozen table/root-travel scopes before dispatching
    // geometry to another thread/device. These seven channels remain exactly
    // the existing CPU implementation; GPU geometry supplies the other 88.
    internal static void FillScopedFeatures(GameState state, Span<int> output)
    {
        if (output.Length != 7) throw new ArgumentException("Expected seven scoped channels.", nameof(output));
        int present = FindPresent(state), stm = present & 1, sign = stm == 0 ? 1 : -1;
        Span<int> features = stackalloc int[FeatureCount];
        FillCheckFeatures(state, present, features);
        output[0] = features[Checks5DIndex]; output[1] = features[SuperphysicalChecksIndex];
        int[][] extra = [new int[ExtraCount], new int[ExtraCount]];
        SacrificeTable.Fill(state, extra, ScopedBits);
        output[2] = Math.Min(extra[0][EX.SAC_LINE], 2) - Math.Min(extra[1][EX.SAC_LINE], 2);
        output[3] = Math.Min(extra[0][EX.SAC_CLOSE], 22) - Math.Min(extra[1][EX.SAC_CLOSE], 22);
        output[4] = Math.Min(extra[0][EX.SAC_NOW], 1) - Math.Min(extra[1][EX.SAC_NOW], 1);
        output[5] = sign * Math.Min(extra[stm][EX.SAC_NOW], 1);
        output[6] = Math.Min(extra[0][EX.SAC_OPEN], 8) - Math.Min(extra[1][EX.SAC_OPEN], 8);
    }

    private sealed class ScopedBoardBits(Board board)
    {
        internal readonly BitBoard Bits = new(board);
        private readonly int[] pieces = (int[])board.Brd.Clone();
        internal bool Matches(Board other) => Bits.Width == other.Width && Bits.Height == other.Height &&
            pieces.AsSpan().SequenceEqual(other.Brd);
    }
    [ThreadStatic] private static ConditionalWeakTable<Board, ScopedBoardBits>? scopedBits;
    private static BitBoard ScopedBits(Board board)
    {
        var cache = scopedBits ??= new();
        if (!cache.TryGetValue(board, out var entry) || !entry.Matches(board))
        { entry = new(board); cache.Remove(board); cache.Add(board, entry); }
        return entry.Bits;
    }

    internal static void FillFeaturesWithScope(GameState state, ReadOnlySpan<int> scoped, Span<int> features)
    {
        if (scoped.Length != 7 || features.Length != FeatureCount) throw new ArgumentException("Incorrect feature count.");
        int present = FindPresent(state);
        CombineFeatures(Extract(state, present, includeSacrifice: false), present & 1, features);
        features[Checks5DIndex] = scoped[0]; features[SuperphysicalChecksIndex] = scoped[1];
        scoped[2..].CopyTo(features[75..80]);
    }
}
