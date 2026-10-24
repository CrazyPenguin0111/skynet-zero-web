namespace SkynetZero.Web;

/// <summary>
/// An orthographic camera shared by HTML boards and SVG annotations. In the
/// isometric views the boards stand on the time/timeline plane; projecting the
/// entire flat canvas would leave the boards lying on that plane instead.
/// Zoom is applied afterwards, so the browser camera still pans in screen space.
/// </summary>
public sealed class MultiverseLayout
{
    public readonly record struct Point(double X, double Y);
    public const int Cell = 36;
    private const double Cos30 = .8660254037844386;
    private readonly BoardView[] boards;
    private readonly double a, b, c, d, boardA, boardB;
    private readonly double offsetX, offsetY;
    private readonly bool flipped;

    public bool Isometric { get; }
    public int MinHalf { get; }
    public int MaxHalf { get; }
    public int MinLayer { get; }
    public int MaxLayer { get; }
    public int TopLayer => flipped ? MaxLayer : MinLayer;
    public int BoardWidth { get; }
    public int BoardHeight { get; }
    public int Pitch { get; }
    public double FlatWidth { get; }
    public double FlatHeight { get; }
    public double Width { get; }
    public double Height { get; }
    public double PresentAngle { get; }
    public string BoardTransform => Matrix(boardA, boardB, 0, 1, 0, 0);
    public string GroundTransform => Matrix(a, b, c, d, offsetX, offsetY);

    public MultiverseLayout(BoardView[] boards, string view, bool flipped = false)
    {
        this.boards = boards;
        this.flipped = flipped;
        Isometric = view is "history" or "parallel";
        MinHalf = boards.Min(Half); MaxHalf = boards.Max(Half);
        MinLayer = boards.Min(b => b.Layer); MaxLayer = boards.Max(b => b.Layer);
        BoardWidth = boards.Max(b => b.Width) * Cell + 26;
        BoardHeight = boards.Max(b => b.Height) * Cell + 26;
        Pitch = Math.Max(BoardWidth + 100, BoardHeight + 100);
        FlatWidth = Math.Max(800, (MaxHalf - MinHalf + 1) * Pitch + 100);
        FlatHeight = Math.Max(460, (MaxLayer - MinLayer + 1) * Pitch + 85);
        if (!Isometric)
        {
            a = d = boardA = 1;
            Width = FlatWidth; Height = FlatHeight;
            return;
        }

        // History: look along time, with board files parallel to timelines.
        // Parallel: look across timelines, with board files parallel to time.
        a = boardA = Cos30; b = .5;
        c = view == "history" ? Cos30 : -Cos30;
        d = view == "history" ? -.5 : .5;
        boardB = view == "history" ? -.5 : .5;
        PresentAngle = view == "history" ? -30 : 150;

        var points = new List<Point> {
            Ground(0, 0), Ground(FlatWidth, 0),
            Ground(0, FlatHeight), Ground(FlatWidth, FlatHeight)
        };
        foreach (var board in boards)
        {
            double width = board.Width * Cell + 26, height = board.Height * Cell + 86;
            points.Add(BoardPoint(board, 0, -24)); // Branch caption above the board.
            points.Add(BoardPoint(board, width + 20, -24)); // Check badge at its corner.
            points.Add(BoardPoint(board, 0, height));
            points.Add(BoardPoint(board, width + 20, height));
        }
        offsetX = 100 - points.Min(p => p.X);
        offsetY = 100 - points.Min(p => p.Y);
        Width = Math.Max(800, points.Max(p => p.X) + offsetX + 100);
        Height = Math.Max(460, points.Max(p => p.Y) + offsetY + 100);
    }

    public static int Half(BoardView board) => board.Time * 2 + (board.White ? 0 : 1);
    public double Left(int half) => 80 + (half - MinHalf) * Pitch;
    // Native positive layers belong to White; negative layers belong to Black.
    // Their screen side follows the pieces' orientation, not the parent layer.
    public double Top(int layer) => 80 + (flipped ? MaxLayer - layer : layer - MinLayer) * Pitch;
    public double CenterY(int layer) => Top(layer) + 32 + BoardHeight / 2d;
    public Point Ground(double x, double y) => new(a * x + c * y + offsetX, b * x + d * y + offsetY);

    public Point BoardOrigin(BoardView board)
    {
        double left = Left(Half(board)), top = Top(board.Layer);
        if (!Isometric) return new(left, top);
        double width = board.Width * Cell + 26, height = board.Height * Cell + 26;
        var foot = Ground(left + BoardWidth / 2d, CenterY(board.Layer));
        return new(foot.X - boardA * width / 2, foot.Y - boardB * width / 2 - 32 - height);
    }

    public Point BoardPoint(BoardView board, double x, double y)
    {
        var origin = BoardOrigin(board);
        return new(origin.X + boardA * x, origin.Y + boardB * x + y);
    }

    public Point SquareCenter(Square square, bool flipped)
    {
        var board = boards.First(b => b.Layer == square.L && b.Time == square.T && b.White == square.White);
        double x = 13 + (flipped ? board.Width - 1 - square.X : square.X) * Cell + Cell / 2d;
        double y = 45 + (flipped ? square.Y : board.Height - 1 - square.Y) * Cell + Cell / 2d;
        return BoardPoint(board, x, y);
    }

    public double Depth(BoardView board) => Ground(Left(Half(board)) + BoardWidth / 2d, CenterY(board.Layer)).Y;
    public string GroundTransformAtZoom(double zoom) => Matrix(a * zoom, b * zoom, c * zoom, d * zoom, offsetX * zoom, offsetY * zoom);
    private static string Matrix(double a, double b, double c, double d, double x, double y) =>
        FormattableString.Invariant($"matrix({a},{b},{c},{d},{x},{y})");
}
