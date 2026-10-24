using System.Globalization;
namespace SkynetZero.Web;
public sealed record Square(int X,int Y,int T,int L,bool White);
public sealed record MoveView(string Id,string Notation,Square From,Square To,int Piece,int Promotion);
public sealed record BoardView(int Layer,int Time,bool White,int Width,int Height,int[] Pieces,bool Frontier,bool Active);
public sealed record TurnView(int Index,bool White,string Notation,MoveView[] Moves,string? BookName=null);
public sealed record BranchView(int Layer,int ParentLayer,int ParentTime,bool ParentWhite,int StartTime,bool StartWhite,int Turn,int Action);
public sealed record CheckView(Square From,Square To);
public sealed record CheckPreviewView(BoardView[] Boards,CheckView[] Checks);
public sealed record PositionView(BoardView[] Boards,MoveView[] LegalMoves,bool White,int Present,bool CanSubmit,
    bool InCheck,string Status,string? Reason,bool? Winner,int PendingCount,int TurnCount,int Viewing,
    TurnView[] Turns,MoveView[] Pending,string Pgn,string Result,string WhiteName,string BlackName,bool Even,BranchView[] Branches,CheckPreviewView? CheckPreview,int EngineStrength)
{
    // Positive favors White: the side that has used fewer branching slots.
    public int TimelineAdvantage { get; init; }
}
public sealed record EngineRequest(string Command,string? Pgn=null,string? Move=null,int Index=-1,int Strength=SkynetZero.Web.Strength.Default,string? Side=null,string? Variant=null);
public sealed record EngineReply(PositionView? Position=null,Evaluation? Evaluation=null,string? Error=null,string? Info=null);
public sealed record Strength(int Level,int Depth,int Nodes,int Milliseconds) {
    public const int Minimum=1,Maximum=100,Default=34;
    // Distinct, smoothly increasing budgets. Maximum allows a minute of search;
    // the previous 15-second maximum often stopped after just two full turns.
    private static readonly Strength[] Anchors=[new(1,1,96,1000),new(34,2,512,3000),new(67,6,8192,15000),new(100,64,65536,60000)];
    public static Strength FromLevel(int value) {
        int level=Math.Clamp(value,Minimum,Maximum),segment=Math.Min((level-1)/33,2);
        var low=Anchors[segment];var high=Anchors[segment+1];double t=(level-low.Level)/33d;
        int Interpolate(int a,int b)=>(int)Math.Round(a*Math.Pow((double)b/a,t));
        return new(level,(int)Math.Round(low.Depth+(high.Depth-low.Depth)*t),Interpolate(low.Nodes,high.Nodes),Interpolate(low.Milliseconds,high.Milliseconds));
    }
}
public sealed record Evaluation(int Index,int? Centipawns,int? Mate,bool? Winner,int Depth,int Nodes,double Seconds,
    bool Interrupted,string BestTurn,string StopReason,bool StaticOnly=false,string? BookName=null)
{
    public PlayedTurnEvidence? PlayedTurn { get; init; }
    public string Display => Mate is { } mate ? (mate==0?"Checkmate":$"{(Winner==false?"−":"")}M{mate}")
        : Centipawns is { } cp ? (cp>=0?"+":"−")+(Math.Abs(cp)/100d).ToString("0.00",CultureInfo.InvariantCulture) : "—";
    public string Explanation => BookName is {} book ? $"Opening book · {book}" : Mate is { } m ? (m==0?"Checkmate":$"{(Winner==true?"White":"Black")} has a proven mate in {m}")
        : StaticOnly ? "Static model evaluation; search did not complete a depth" : $"Depth {Depth} · {Nodes:N0} nodes · {Seconds:0.0}s";
}
public sealed record PlayedTurnEvidence(bool Best,int SacrificeCentipawns,int? Loss=null,int ComparisonDepth=0,string? MateVerdict=null);
public sealed record TurnAnalysis(int Index,bool White,int? Loss,string Verdict,Evaluation Before,Evaluation After)
{
    public string Category => Verdict switch {
        "Book"=>"book", "Brilliant"=>"brilliant", "Best" or "Forced mate"=>"best",
        "Excellent"=>"excellent", "Good"=>"good", "Inaccuracy"=>"inaccuracy",
        "Mistake"=>"mistake", "Blunder" or "Missed mate" or "Allows mate"=>"blunder", _=>"unscored" };
    public string Symbol => Category switch { "book"=>"▤", "brilliant"=>"!!", "best"=>"★", "excellent"=>"✓", "good"=>"✓", "inaccuracy"=>"?!", "mistake"=>"?", "blunder"=>"??", _=>"·" };
    public string Description => Verdict switch {
        "Book"=>"A move from the supplied 5D opening repertoire.",
        "Brilliant"=>"The engine’s best turn offers a piece sacrifice while preserving the evaluation.",
        "Best"=>"Matches the engine’s searched best complete turn.",
        "Unscored"=>"Search did not complete enough work to classify this turn.",
        _=>Loss is {} loss?$"{Verdict} · {loss} centipawns lost":Verdict };
    // Lichess-style loss is the deterioration in the mover's evaluation. Mate
    // transitions are categorical and excluded from the numeric ACPL mean.
    public static TurnAnalysis Compare(int index,bool white,Evaluation before,Evaluation after,string? bookName=null) {
        if(before.PlayedTurn?.MateVerdict is {} mateVerdict)
            return new(index,white,null,mateVerdict,before,after);
        if(before.PlayedTurn is null&&(before.Mate!=null || after.Mate!=null)) {
            string label=before.Winner==white && before.Mate!=null && (after.Mate==null||after.Winner!=white)?"Missed mate":
                after.Mate!=null&&after.Winner!=white&&(before.Mate==null||before.Winner==white)?"Allows mate":
                after.Mate!=null&&after.Winner==white?"Forced mate":"Mate sequence";
            return new(index,white,null,label,before,after);
        }
        int loss;
        if(before.PlayedTurn is {} evidence){
            // Compare complete alternatives from the SAME root and completed
            // depth. The independent next-position search has an extra ply and
            // may change its score even when the engine's best move was played.
            if(before.StaticOnly||before.Depth<1||!evidence.Best&&(evidence.ComparisonDepth<1||evidence.Loss is null))
                return new(index,white,null,bookName is null?"Unscored":"Book",before,after);
            loss=evidence.Best?0:evidence.Loss!.Value;
        } else {
            if(before.StaticOnly||after.StaticOnly||before.Depth<1||after.Depth<1||before.Centipawns is null||after.Centipawns is null)
                return new(index,white,null,bookName is null?"Unscored":"Book",before,after);
            loss=Math.Max(0,(white?1:-1)*(Math.Clamp(before.Centipawns.Value,-1000,1000)-Math.Clamp(after.Centipawns.Value,-1000,1000)));
        }
        // Search identity, not a rounded zero loss, earns Best. A brilliant
        // additionally needs a real material offer and adequate completed
        // depth on both sides. Never award it merely for a large evaluation.
        bool best=before.PlayedTurn?.Best==true&&loss<20;
        bool brilliant=best&&before.Depth>=3&&after.Depth>=3&&before.PlayedTurn!.SacrificeCentipawns>=100&&
            after.Centipawns is {} a&&before.Centipawns is {} b&&(white?1:-1)*a>=-50&&(white?1:-1)*b<300;
        string verdict=bookName is not null?"Book":brilliant?"Brilliant":best?"Best":
            loss>=300?"Blunder":loss>=100?"Mistake":loss>=50?"Inaccuracy":loss>=20?"Good":"Excellent";
        return new(index,white,loss,verdict,before,after);
    }
}
