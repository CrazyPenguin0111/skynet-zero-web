using System.Globalization;
namespace SkynetZero.Web;
public sealed record Square(int X,int Y,int T,int L,bool White);
public sealed record MoveView(string Id,string Notation,Square From,Square To,int Piece,int Promotion);
public sealed record BoardView(int Layer,int Time,bool White,int Width,int Height,int[] Pieces,bool Frontier,bool Active);
public sealed record TurnView(int Index,bool White,string Notation,MoveView[] Moves);
public sealed record PositionView(BoardView[] Boards,MoveView[] LegalMoves,bool White,int Present,bool CanSubmit,
    bool InCheck,string Status,string? Reason,bool? Winner,int PendingCount,int TurnCount,int Viewing,
    TurnView[] Turns,MoveView[] Pending,string Pgn,string Result,string WhiteName,string BlackName,bool Even);
public sealed record EngineRequest(string Command,string? Pgn=null,string? Move=null,int Index=-1,int Strength=2,string? Side=null,string? Variant=null);
public sealed record EngineReply(PositionView? Position=null,Evaluation? Evaluation=null,string? Error=null,string? Info=null);
public sealed record Strength(string Name,int Depth,int Nodes,int Milliseconds) {
    public static readonly Strength[] All=[new("Casual",1,96,1000),new("Club",2,512,3000),new("Expert",3,2048,7000),new("Maximum",6,8192,15000)];
}
public sealed record Evaluation(int Index,int? Centipawns,int? Mate,bool? Winner,int Depth,int Nodes,double Seconds,
    bool Interrupted,string BestTurn,string StopReason,bool StaticOnly=false)
{
    public string Display => Mate is { } mate ? (mate==0?"Checkmate":$"{(Winner==false?"−":"")}M{mate}")
        : Centipawns is { } cp ? (cp>=0?"+":"−")+(Math.Abs(cp)/100d).ToString("0.00",CultureInfo.InvariantCulture) : "—";
    public string Explanation => Mate is { } m ? (m==0?"Checkmate":$"{(Winner==true?"White":"Black")} has a proven mate in {m}")
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
        if(before.Centipawns is not {} b || after.Centipawns is not {} a)return new(index,white,null,"Unscored",before,after);
        int loss=Math.Max(0,(white?1:-1)*(Math.Clamp(b,-1000,1000)-Math.Clamp(a,-1000,1000)));
        return new(index,white,loss,loss>=300?"Blunder":loss>=100?"Mistake":loss>=50?"Inaccuracy":loss>=20?"Good":"Excellent",before,after);
    }
}
