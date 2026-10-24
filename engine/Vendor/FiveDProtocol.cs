using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FileIO5D;

namespace SkynetZero;

// The wire position is the initial multiverse plus every atomic move and submit.
// Sending only frontier boards loses temporal destinations and en-passant history.
internal static partial class FiveDProtocol
{
    [GeneratedRegex(@"\[([^:\[\]\s]+):([+-]?\d+):(\d+):([wb])\]")]
    private static partial Regex Boards();
    [GeneratedRegex(@"^\(([+-]?\d+)T(\d+)\)([PNBRSQKUDWYC]?)([a-h])([1-8])(?:>{0,2}\(([+-]?\d+)T(\d+)\))?([a-h])([1-8])([NBRSQUDWYC]?)$")]
    private static partial Regex Lan();

    internal static GameState ParseSetup(string fen)
    {
        var records = Boards().Matches(fen).Cast<Match>().ToArray();
        if (records.Length == 0) throw new InvalidDataException("Position needs explicit 5DFEN boards.");
        bool even = records.Any(m => m.Groups[2].Value is "-0" or "+0");
        var lines = new List<Timeline>();
        int width = 0, height = 0;
        foreach (var group in records.GroupBy(m => FENParser.ParseLayer(m.Groups[2].Value, even)).OrderBy(g => g.Key))
        {
            Timeline? line = null;
            int previous = -1;
            foreach (var record in group.OrderBy(m => 2 * Number(m, 3) + (m.Groups[4].Value == "w" ? 0 : 1)))
            {
                int time = Number(record, 3); bool white = record.Groups[4].Value == "w";
                int half = 2 * time + (white ? 0 : 1);
                var board = FENParser.BoardFromString(record.Groups[1].Value);
                width = width == 0 ? board.Width : width; height = height == 0 ? board.Height : height;
                if (width is < 1 or > 8 || height is < 1 or > 8 || width != board.Width || height != board.Height)
                    throw new InvalidDataException("All boards must have the same dimensions, at most 8x8.");
                if (line == null) line = new(board, white, time, group.Key);
                else
                {
                    if (half != previous + 1) throw new InvalidDataException("A timeline has missing or duplicate half-turns.");
                    FENParser.RestoreInitialEnPassant(line, board);
                    line.AddMove(board);
                }
                previous = half;
            }
            lines.Add(line!);
        }
        if (lines[^1].Layer - lines[0].Layer + 1 != lines.Count)
            throw new InvalidDataException("Initial timeline layers must be contiguous.");
        // Upstream defines original layers by the odd/even layout, even when
        // a supplied position already contains branches. Preserve that metadata
        // for the transformer's created/lost-timeline evaluation terms.
        var origins = lines.Where(line => line.Layer >= 0 && line.Layer <= (even ? 1 : 0)).ToArray();
        if (origins.Length != (even ? 2 : 1))
            throw new InvalidDataException("Position is missing an original timeline.");
        var state = new GameState(origins, width, height, even, true, 0)
        {
            QueenPromotionOnly = true, Multiverse = lines,
            MinTL = lines[0].Layer, MaxTL = lines[^1].Layer
        };
        state.DetermineActiveTLS();
        state.Color = state.CalcPresent();
        state.StartPresent = state.Present;
        return state;
    }

    internal static string Setup(GameState initial) =>
        $"size {initial.Width}x{initial.Height} {(initial.EvenStart ? "even" : "odd")} fen {Dump(initial)}";

    internal static string Position(string setup, IEnumerable<string> history)
    {
        string moves = string.Join(' ', history);
        return "position " + setup + (moves.Length == 0 ? "" : " moves " + moves);
    }

    internal static string Format(Move move, bool even)
    {
        string Coordinate(CoordFive c) => $"({c.L - (even ? 1 : 0)}T{c.T})";
        string Square(CoordFive c) => $"{(char)('a' + c.X)}{c.Y + 1}";
        string destination = move.Origin.L == move.Dest.L && move.Origin.T == move.Dest.T
            ? "" : Coordinate(move.Dest);
        string promotion = move.SpecialType >= Move.PROMOTION
            ? char.ToUpperInvariant(Board.PieceChars[move.SpecialType]).ToString() : "";
        return Coordinate(move.Origin) + Square(move.Origin) + destination + Square(move.Dest) + promotion;
    }

    internal static Move ParseMove(GameState state, string token)
    {
        Match m = Lan().Match(token);
        if (!m.Success) throw new InvalidDataException($"Invalid 5DUCI move: {token}");
        int layer = Number(m, 1) + (state.EvenStart ? 1 : 0), time = Number(m, 2);
        var from = new CoordFive(m.Groups[4].Value[0] - 'a', Number(m, 5) - 1, time, layer, state.Color);
        var to = new CoordFive(m.Groups[8].Value[0] - 'a', Number(m, 9) - 1,
            m.Groups[7].Success ? Number(m, 7) : time,
            m.Groups[6].Success ? Number(m, 6) + (state.EvenStart ? 1 : 0) : layer, state.Color);
        if (!state.IsInBounds(from) || !state.IsInBounds(to))
            throw new InvalidDataException($"Move coordinates outside the position: {token}");
        int piece = Math.Abs(state.GetSquare(from));
        if (piece is < 1 or > 24) throw new InvalidDataException($"No source piece for {token}.");
        if (m.Groups[3].Length != 0 && m.Groups[3].Value[0] != char.ToUpperInvariant(Board.PieceChars[piece]))
            throw new InvalidDataException($"Wrong source piece in {token}.");
        var move = new Move(from, to) { PieceMoved = piece };
        if (m.Groups[10].Length != 0)
            move.SpecialType = Array.IndexOf(Board.PieceChars, m.Groups[10].Value[0]) + (state.Color ? 0 : Board.NUMTYPES);
        else if (MoveGenerator.IsCastlingMove(piece, move)) move.SpecialType = Move.CASTLE;
        else if (piece % Board.NUMTYPES is (int)Board.Piece.WPAWN or (int)Board.Piece.WBRAWN &&
            move.Type == Move.SPATIALMOVE && to.Y == (state.Color ? state.Height - 1 : 0))
            move.SpecialType = (int)(state.Color ? Board.Piece.WQUEEN : Board.Piece.BQUEEN);
        return move;
    }

    internal static string PgnMove(GameState state, string token)
    {
        Move move = ParseMove(state, token);
        string Coordinate(CoordFive c)
        {
            string layer = !state.EvenStart ? c.L.ToString(CultureInfo.InvariantCulture)
                : c.L <= 0 ? "-" + -c.L : "+" + (c.L - 1);
            return $"({layer}T{c.T})";
        }
        string Square(CoordFive c) => $"{(char)('a' + c.X)}{c.Y + 1}";
        string origin = Coordinate(move.Origin);
        if (move.SpecialType == Move.CASTLE)
            return origin + (move.Dest.X > move.Origin.X ? "O-O" : "O-O-O");
        string destination = move.Type == Move.SPATIALMOVE ? "" :
            (state.GetTimeline(move.Dest.L)!.IsMostRecentTime(move.Dest.T, state.Color) ? ">" : ">>") + Coordinate(move.Dest);
        string promotion = move.SpecialType >= Move.PROMOTION
            ? "=" + char.ToUpperInvariant(Board.PieceChars[move.SpecialType]) : "";
        return origin + char.ToUpperInvariant(Board.PieceChars[move.PieceMoved]) +
            Square(move.Origin) + destination + Square(move.Dest) + promotion;
    }

    // All callers validate before committing, including the external opponent.
    internal static void Apply(GameState state, string token)
    {
        if (token == "submit")
        {
            if (!RoyalLegality.CanSubmit(state) || !state.SubmitMoves())
                throw new InvalidDataException("Engine proposed an illegal turn submission.");
            return;
        }
        var move = ParseMove(state, token);
        if (!RoyalLegality.IsLegalBeforeMove(state, move))
            throw new InvalidDataException($"Illegal royal move: {token}");
        bool white = state.Color;
        if (!state.MakeMove(move, true) || !RoyalLegality.IsSafeAfterMove(state, white))
            throw new InvalidDataException($"Engine proposed an illegal move: {token}");
    }

    internal static string Dump(GameState state)
    {
        var boards = new List<string>();
        foreach (var line in state.Multiverse)
        {
            string layer = !state.EvenStart ? line.Layer.ToString(CultureInfo.InvariantCulture)
                : line.Layer <= 0 ? "-" + -line.Layer : "+" + (line.Layer - 1);
            for (int i = 0; i < line.WBoards.Count; i++)
                boards.Add($"[{BoardFen(line.WBoards[i])}:{layer}:{line.WhiteStart + i}:w]");
            for (int i = 0; i < line.BBoards.Count; i++)
                boards.Add($"[{BoardFen(line.BBoards[i])}:{layer}:{line.BlackStart + i}:b]");
        }
        return string.Join(' ', boards.Order(StringComparer.Ordinal));
    }

    internal static string NormalizeDump(IEnumerable<string> lines) =>
        string.Join(' ', Boards().Matches(string.Join(' ', lines)).Select(m => m.Value).Order(StringComparer.Ordinal));

    private static string BoardFen(Board board)
    {
        var text = new StringBuilder();
        for (int y = board.Height - 1; y >= 0; y--)
        {
            if (y < board.Height - 1) text.Append('/');
            int empty = 0;
            for (int x = 0; x < board.Width; x++)
            {
                int piece = board.Brd[y * board.Width + x];
                if (piece == 0) { empty++; continue; }
                if (empty > 0) { text.Append(empty); empty = 0; }
                text.Append(Board.PieceChars[Math.Abs(piece)]);
                if (piece < 0) text.Append('*');
            }
            if (empty > 0) text.Append(empty);
        }
        return text.ToString();
    }

    private static int Number(Match match, int group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
