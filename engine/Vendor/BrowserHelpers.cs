using System.Text;
using System.Security.Cryptography;
namespace SkynetZero;
internal static class Program {
internal const float MaximumFinitePieceValue=14.5f;
private const double ActiveTimelineCreationPenalty=.1,InactiveTimelineCreationPenalty=.2;
	internal static int GetCapturedPiece(GameState game, Move move)
	{
		int captured = game.GetSquare(move.Dest);
		if (captured != Board.EMPTYSQUARE || move.Type != Move.SPATIALMOVE || move.Origin.X == move.Dest.X) return captured;
		int moved = Math.Abs(game.GetSquare(move.Origin));
		if (moved != (int)Board.Piece.WPAWN && moved != (int)Board.Piece.BPAWN &&
		    moved != (int)Board.Piece.WBRAWN && moved != (int)Board.Piece.BBRAWN) return captured;
		if (game.GetBoard(move.Origin)?.EnPassentSquare?.SpatialEquals(move.Dest) != true) return captured;
		var enPassant = move.Dest.Clone();
		enPassant.Y = move.Origin.Y;
		return game.GetSquare(enPassant);
	}
	internal static double GetTimelineCreationPenalty(GameState game, GameState successor)
	{
		int timelinesBefore = game.MaxTL - game.MinTL + 1;
		int activeBefore = game.MaxActiveTL - game.MinActiveTL + 1;
		int timelinesCreated = Math.Max(0, successor.MaxTL - successor.MinTL + 1 - timelinesBefore);
		int activeCreated = Math.Min(timelinesCreated,
			Math.Max(0, successor.MaxActiveTL - successor.MinActiveTL + 1 - activeBefore));
		int inactiveCreated = timelinesCreated - activeCreated;
		return activeCreated * ActiveTimelineCreationPenalty +
		       inactiveCreated * InactiveTimelineCreationPenalty;
	}
}
internal static class CompleteTurnCandidates {
    internal static string ActionKey(Move? action) => action == null ? "SUBMIT" :
        $"{action.Origin.X},{action.Origin.Y},{action.Origin.T},{action.Origin.L},{action.Origin.Color}>" +
        $"{action.Dest.X},{action.Dest.Y},{action.Dest.T},{action.Dest.L},{action.Dest.Color}:" +
        $"{action.PieceMoved}:{action.Type}:{action.SpecialType}";
}
internal static class EvaluationEncoding { internal const float ScoreScale=8700f; }
internal static class TurnSearch {internal static SelfPlayProfile? Profile; }
internal sealed record BookAction(int X, int Y, int T, int L, bool White,
    int ToX, int ToY, int ToT, int ToL, bool ToWhite, int Special)
{
    internal static BookAction From(Move move) => new(move.Origin.X, move.Origin.Y, move.Origin.T,
        move.Origin.L, move.Origin.Color, move.Dest.X, move.Dest.Y, move.Dest.T, move.Dest.L, move.Dest.Color, move.SpecialType);
    internal Move Move() => new(new CoordFive(X, Y, T, L, White), new CoordFive(ToX, ToY, ToT, ToL, ToWhite))
        { SpecialType = Special };
}
internal static class QueryStateProvenance {
    internal static string Semantic(GameState state)
    {
        var text = new StringBuilder().Append(state.Width).Append('|').Append(state.Height).Append('|')
            .Append(state.EvenStart).Append('|').Append(state.Color).Append('|').Append(state.StartPresent).Append('|')
            .Append(state.Present).Append('|').Append(state.MinTL).Append('|').Append(state.MaxTL).Append('|')
            .Append(state.MinActiveTL).Append('|').Append(state.MaxActiveTL).Append('|').Append(state.TLHandicap).Append('|')
            .Append(state.kingWasTaken).Append('|').Append(state.kingWasTakenByColor).Append('|');
        foreach (Timeline line in state.Multiverse)
        {
            text.Append(line.Layer).Append(':').Append(line.TStart).Append(':').Append(line.TEnd).Append(':')
                .Append(line.WhiteStart).Append(':').Append(line.WhiteEnd).Append(':').Append(line.BlackStart).Append(':')
                .Append(line.BlackEnd).Append(':').Append(line.ColorPlayable).Append(':').Append(line.ColorStart).Append(';');
            AppendBoards(text, 'W', line.WBoards);
            AppendBoards(text, 'B', line.BBoards);
        }
        text.Append("TM:");
        foreach (Move move in state.TurnMoves) AppendMove(text, move);
        text.Append("TLS:").AppendJoin(',', state.TurnTLS).Append(';');
        return text.ToString();
    }

    private static void AppendBoards(StringBuilder text, char color, IEnumerable<Board> boards)
    {
        foreach (Board board in boards)
        {
            text.Append(color).Append(':').Append(board.Width).Append('x').Append(board.Height).Append(':')
                .AppendJoin(',', board.Brd).Append(':');
            if (board.EnPassentSquare is { } square) AppendCoord(text, square); else text.Append("null");
            text.Append(';');
        }
    }

    private static string MoveText(Move move)
    {
        var text = new StringBuilder();
        AppendMove(text, move);
        return text.ToString();
    }

    private static void AppendMove(StringBuilder text, Move move)
    {
        AppendCoord(text, move.Origin);
        text.Append('>');
        AppendCoord(text, move.Dest);
        text.Append(':').Append(move.PieceMoved).Append(':').Append(move.Type).Append(':').Append(move.SpecialType).Append(';');
    }

    private static void AppendCoord(StringBuilder text, CoordFive coord) => text.Append(coord.X).Append(',')
        .Append(coord.Y).Append(',').Append(coord.T).Append(',').Append(coord.L).Append(',').Append(coord.Color);

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
