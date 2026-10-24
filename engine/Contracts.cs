using System.Globalization;
namespace SkynetZero.Web;
public sealed record Square(int X,int Y,int T,int L,bool White);
public sealed record MoveView(string Id,string Notation,Square From,Square To,int Piece,int Promotion);
public sealed record BoardView(int Layer,int Time,bool White,int Width,int Height,int[] Pieces,bool Frontier,bool Active);
public sealed record TurnView(int Index,bool White,string Notation,MoveView[] Moves);
public sealed record BranchView(int Layer,int ParentLayer,int ParentTime,bool ParentWhite,int StartTime,bool StartWhite,int Turn,int Action);
public sealed record CheckView(Square From,Square To);
public sealed record CheckPreviewView(BoardView[] Boards,CheckView[] Checks);
public sealed record PositionView(BoardView[] Boards,MoveView[] LegalMoves,bool White,int Present,bool CanSubmit,
    bool InCheck,string Status,string? Reason,bool? Winner,int PendingCount,int TurnCount,int Viewing,
    TurnView[] Turns,MoveView[] Pending,string Pgn,string Result,string WhiteName,string BlackName,bool Even,BranchView[] Branches,CheckPreviewView? CheckPreview,int EngineStrength);
public sealed record EngineRequest(string Command,string? Pgn=null,string? Move=null,int Index=-1,int Strength=SkynetZero.Web.Strength.Default,string? Side=null,string? Variant=null);
public sealed record EngineReply(PositionView? Position=null,Evaluation? Evaluation=null,string? Error=null,string? Info=null);
public sealed record Strength(int Level,int Depth,int Nodes,int Milliseconds) {
    public const int Minimum=1,Maximum=100,Default=34;
    // Distinct, smoothly increasing budgets. Maximum allows a minute of search;
    // the previous 15-second maximum often stopped after just two full turns.
    private static readonly Strength[] Anchors=[new(1,1,96,1000),new(34,2,512,3000),new(67,6,8192,15000),new(100,8,65536,60000)];
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
    public string Display => Mate is { } mate ? (mate==0?"Checkmate":$"{(Winner==false?"−":"")}M{mate}")
        : Centipawns is { } cp ? (cp>=0?"+":"−")+(Math.Abs(cp)/100d).ToString("0.00",CultureInfo.InvariantCulture) : "—";
    public string Explanation => BookName is {} book ? $"Opening book · {book}" : Mate is { } m ? (m==0?"Checkmate":$"{(Winner==true?"White":"Black")} has a proven mate in {m}")
        : StaticOnly ? "Static model evaluation; search did not complete a depth" : $"Depth {Depth} · {Nodes:N0} nodes · {Seconds:0.0}s";
}
public sealed record TurnAnalysis(int Index,bool White,int? Loss,string Verdict,Evaluation Before,Evaluation After)
{
    // Lichess-style loss is the deterioration in the mover's evaluation. Mate
    // transitions are categorical and excluded from the numeric ACPL mean.
    public static TurnAnalysis Compare(int index,bool white,Evaluation before,Evaluation after) {
        if(before.Mate!=null || after.Mate!=null) {
            string label=before.Winner==white && before.Mate!=null && (after.Mate==null||after.Winner!=white)?"Missed mate":
                after.Mate!=null&&after.Winner!=white&&(before.Mate==null||before.Winner==white)?"Allows mate":
                after.Mate!=null&&after.Winner==white?"Forced mate":"Mate sequence";
            return new(index,white,null,label,before,after);
        }
        if(before.StaticOnly||after.StaticOnly||before.Depth<1||after.Depth<1)return new(index,white,null,"Unscored",before,after);
        if(before.Centipawns is not {} b || after.Centipawns is not {} a)return new(index,white,null,"Unscored",before,after);
        int loss=Math.Max(0,(white?1:-1)*(Math.Clamp(b,-1000,1000)-Math.Clamp(a,-1000,1000)));
        return new(index,white,loss,loss>=300?"Blunder":loss>=100?"Mistake":loss>=50?"Inaccuracy":loss>=20?"Good":"Excellent",before,after);
    }
}
