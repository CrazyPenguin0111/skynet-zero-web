// Port of src/engine/chess.rs at 3697bf323a1a501428917ad218f7358d681f2904,
// FirePlank/5d-chess-engine; GPL-3.0-only. See THIRD_PARTY_NOTICES.md.
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using static SkynetZero.MultiverseEvaluation;
using T = SkynetZero.StandardChessTables;

namespace SkynetZero;

internal static class StandardChessEvaluation
{
    private const ulong FileA = 0x0101010101010101UL;
    private sealed class Pieces
    {
        internal ulong Pawn, Knight, Bishop, Rook, Queen, King, All;
        internal ulong[] Kinds => [Pawn, Knight, Bishop, Rook, Queen, King];
    }
    private sealed class Attacks
    {
        internal ulong Pawn, Minor, Rook, Queen, All;
        internal readonly int[] Mobility = new int[4], Low = new int[2];
        internal int Units;
    }
    private sealed class BoardTerms(Board board)
    {
        internal readonly int[] Pieces = (int[])board.Brd.Clone();
        internal readonly int Width = board.Width, Height = board.Height;
        internal readonly int[,] Groups = new int[2, 7], Threat = new int[2, 2];
        internal readonly int[] Material = new int[5];
        internal bool Matches(Board board) => Width == board.Width && Height == board.Height && Pieces.AsSpan().SequenceEqual(board.Brd);
    }
    [ThreadStatic] private static ConditionalWeakTable<Board, BoardTerms>? cache;

    internal static void Fill(GameState state, Span<int> output)
    {
        if (output.Length != 12) throw new ArgumentException("Expected twelve chess channels.");
        output.Clear();
        // Same fade and activation as hce::sides_with. Inactive timelines do
        // not disable the terms; having multiple active timelines does.
        if (state.MaxActiveTL - state.MinActiveTL >= 1) return;
        foreach (var line in state.Multiverse)
        {
            if (!state.LayerIsActive(line.Layer)) continue;
            var board = line.GetPlayableBoard();
            var entries = cache ??= new();
            if (!entries.TryGetValue(board, out var terms) || !terms.Matches(board))
            { terms = Analyze(board); entries.Remove(board); entries.Add(board, terms); }
            for (int g = 0; g < 7; g++) output[g] += terms.Groups[0, g] - terms.Groups[1, g];
            int mover = line.ColorPlayable ? 0 : 1;
            output[4] += terms.Threat[0, mover == 0 ? 0 : 1] - terms.Threat[1, mover == 1 ? 0 : 1];
            // Material replacement counts apply even on smaller/fairy boards,
            // matching CH_MF's use of active material in the source evaluator.
            for (int j = 0; j < 5; j++) output[7 + j] += terms.Material[j];
        }
    }

    private static int First(ulong bits) => BitOperations.TrailingZeroCount(bits);
    private static int Pop(ref ulong bits) { int sq = First(bits); bits &= bits - 1; return sq; }
    private static ulong Flip(ulong bits, int color) => color == 0 ? bits : BinaryPrimitives.ReverseEndianness(bits);
    private static ulong PawnAttack(ulong pieces, int color) => Shift(pieces, -1, color == 0 ? 1 : -1) | Shift(pieces, 1, color == 0 ? 1 : -1);
    private static ulong Above(int rank) => rank >= 7 ? 0 : ulong.MaxValue << (8 * (rank + 1));

    private static BoardTerms Analyze(Board board)
    {
        var result = new BoardTerms(board);
        var bits = new BitBoard(board);
        ulong[] material = [bits.Pawn, bits.Knight, bits.Bishop, bits.Rook, bits.Queen];
        for (int j = 0; j < 5; j++) result.Material[j] = Count(material[j] & bits.Mine(0)) - Count(material[j] & bits.Mine(1));
        if (board.Width != 8 || board.Height != 8) return result;
        var pieces = new Pieces[2]; var attacks = new Attacks[2]; var zones = new ulong[2];
        int phase = 0;
        for (int c = 0; c < 2; c++)
        {
            ulong mine = bits.Mine(c);
            var p = pieces[c] = new Pieces { Pawn = bits.Pawn & mine, Knight = bits.Knight & mine,
                Bishop = bits.Bishop & mine, Rook = bits.Rook & mine, Queen = bits.Queen & mine,
                King = bits.LKing & bits.Royal & mine, All = mine };
            if (p.King != 0) zones[c] = KingAttack(First(p.King)) | p.King;
            phase += Count(p.Knight | p.Bishop) + 2 * Count(p.Rook) + 4 * Count(p.Queen);
        }
        phase = Math.Min(24, phase);
        for (int c = 0; c < 2; c++)
        {
            var me = pieces[c]; var enemy = pieces[1 - c];
            var a = attacks[c] = new Attacks { Pawn = PawnAttack(me.Pawn, c) };
            ulong area = ~me.All & ~PawnAttack(enemy.Pawn, 1 - c);
            for (int kind = 0; kind < 4; kind++)
            {
                ulong todo = kind switch { 0 => me.Knight, 1 => me.Bishop, 2 => me.Rook, _ => me.Queen };
                while (todo != 0)
                {
                    int sq = Pop(ref todo);
                    ulong attack = kind == 0 ? KnightAttack(sq) : SliderAttack(sq, bits.Occupied, kind >= 2, kind != 2);
                    if (kind < 2) a.Minor |= attack; else if (kind == 2) a.Rook |= attack; else a.Queen |= attack;
                    int mobility = Count(attack & area); a.Mobility[kind] += mobility;
                    if (kind < 2 && mobility <= 1) a.Low[kind]++;
                    if ((attack & zones[1 - c]) != 0) a.Units += kind < 2 ? 2 : kind == 2 ? 3 : 5;
                }
            }
            a.All = a.Pawn | a.Minor | a.Rook | a.Queen | (me.King != 0 ? KingAttack(First(me.King)) : 0);
        }
        Span<int> middle = stackalloc int[7], end = stackalloc int[7];
        for (int c = 0; c < 2; c++)
        {
            var me = pieces[c]; var enemy = pieces[1 - c]; var a = attacks[c]; var ea = attacks[1 - c];
            middle.Clear(); end.Clear();
            ulong[] kinds = me.Kinds;
            for (int kind = 0; kind < 6; kind++)
            {
                if (kind < 5) Add(0, T.MAT + kind, Count(kinds[kind]), middle, end);
                ulong todo = Flip(kinds[kind], c);
                while (todo != 0)
                {
                    int sq = Pop(ref todo), file = sq & 7;
                    Add(1, T.PSQT + kind * 32 + (sq >> 3) * 4 + Math.Min(file, 7 - file), 1, middle, end);
                }
            }
            // The current source disables pawn and piece groups (zero fitted
            // weights and GROUPS=0b1110011); their input channels stay zero.
            ulong king = Flip(me.King, c), pawn = Flip(me.Pawn, c), enemyPawn = Flip(enemy.Pawn, c);
            if (king != 0)
            {
                int sq = First(king), file = sq & 7, rank = sq >> 3;
                ulong front = Shift(king, 0, 1) | Shift(king, -1, 1) | Shift(king, 1, 1);
                Add(5, T.SHIELD1, Count(pawn & front), middle, end);
                Add(5, T.SHIELD2, Count(pawn & Shift(front, 0, 1)), middle, end);
                Add(5, T.KING_OPEN, (pawn & (FileA << file)) == 0 ? 1 : 0, middle, end);
                int semi = (file > 0 && (pawn & (FileA << (file - 1))) == 0 ? 1 : 0) +
                    (file < 7 && (pawn & (FileA << (file + 1))) == 0 ? 1 : 0);
                Add(5, T.KING_SEMI, semi, middle, end);
                ulong files = FileA << file;
                if (file > 0) files |= FileA << (file - 1);
                if (file < 7) files |= FileA << (file + 1);
                Add(5, T.STORM, Count(enemyPawn & files & Above(rank) & ~Above(Math.Min(rank + 3, 7))), middle, end);
                int units = Math.Min(30, ea.Units);
                Add(5, T.KUNITS, units, middle, end); Add(5, T.KUNITS_SQ, units * units, middle, end);
                if (enemy.Queen != 0) Add(5, T.KUNITS_SQ_Q, units * units, middle, end);
            }
            for (int j = 0; j < 4; j++) Add(6, T.MOB + j, a.Mobility[j], middle, end);
            for (int j = 0; j < 2; j++) Add(6, T.LOWMOB + j, a.Low[j], middle, end);
            for (int g = 0; g < 7; g++) result.Groups[c, g] = Taper(middle[g], end[g], phase);
            ulong minor = enemy.Knight | enemy.Bishop, weak = a.All & ~ea.All;
            int[] threats = [Count(a.Pawn & minor), Count(a.Pawn & enemy.Rook), Count(a.Pawn & enemy.Queen),
                Count(a.Minor & enemy.Rook), Count(a.Minor & enemy.Queen), Count(a.Rook & enemy.Queen),
                Count(weak & minor), Count(weak & enemy.Rook), Count(weak & enemy.Queen)];
            for (int moving = 0; moving < 2; moving++)
            {
                int mg = 0, eg = 0, start = moving == 0 ? T.THREAT_M : T.THREAT_W;
                for (int j = 0; j < 9; j++) { var pair = T.Values[start + j]; mg += threats[j] * pair.Middle; eg += threats[j] * pair.End; }
                result.Threat[c, moving] = Taper(mg, eg, phase);
            }
        }
        return result;
    }
    private static int Taper(int middle, int end, int phase) => (middle * phase + end * (24 - phase)) / (24 * 8);
    private static void Add(int group, int term, int value, Span<int> middle, Span<int> end)
    { var pair = T.Values[term]; middle[group] += pair.Middle * value; end[group] += pair.End * value; }
}
