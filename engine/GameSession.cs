using System.Text;
using System.Text.RegularExpressions;
using FileIO5D;
namespace SkynetZero.Web;

public sealed class GameSession
{
    private GameState initial=null!, live=null!;
    private readonly List<GameState> positions=[];
    private readonly List<TurnView> turns=[];
    private readonly List<GameState> pendingUndo=[];
    private readonly List<MoveView> pending=[];
    private readonly List<BranchView> branches=[];
    private readonly PortableModel model;
    private readonly SacrificeTable.Snapshot evaluationTables;
    private string result="*", whiteName="You",blackName="SkynetZero";
    private int engineStrength;
    private CheckAndMateResult? liveStatus;
    public GameSession(PortableModel model){this.model=model;evaluationTables=SacrificeTable.CaptureSnapshot();New();}
    public void New(string? side=null,string? variant=null) {
        string board=variant=="standard"?"standard":"standard-t0";
        initial=FENParser.ShadSTDGSMFromString($"[Board \"{board}\"]")!.Clone();
        initial.QueenPromotionOnly=false;DrawRules.Enable(initial);live=initial.Clone();
        positions.Clear();positions.Add(initial.Clone());turns.Clear();pending.Clear();pendingUndo.Clear();branches.Clear();result="*";liveStatus=null;
        whiteName=side=="black"?"SkynetZero":"You";blackName=side=="black"?"You":"SkynetZero";engineStrength=0;
    }
    public static MoveView ViewMove(GameState state,Move move) {
        Square Sq(CoordFive c)=>new(c.X,c.Y,c.T,c.L,c.Color);
        string token=FiveDProtocol.Format(move,state.EvenStart);
        return new(token,FiveDProtocol.PgnMove(state,token),Sq(move.Origin),Sq(move.Dest),Math.Abs(state.GetSquare(move.Origin)),move.SpecialType>=SkynetZero.Move.PROMOTION?move.SpecialType:0);
    }
    private void Apply(Move move) {
        var next=live.Clone();
        if(!live.CoordIsPlayable(move.Origin)||!next.MakeMove(move.Clone(),true)||!RoyalLegality.IsLegalAfterMove(live,move,next))
            throw new InvalidDataException("That move is illegal or leaves a royal piece in check.");
        // A branch descends from the destination board in the past, which may be
        // on a different timeline from the travelling piece's origin.
        foreach(var line in next.Multiverse.Where(line=>!live.LayerExists(line.Layer)))
            branches.Add(new(line.Layer,move.Dest.L,move.Dest.T,move.Dest.Color,line.TStart,line.ColorStart,turns.Count+1,pending.Count+1));
        var view=ViewMove(live,move);pendingUndo.Add(live);pending.Add(view);live=next;liveStatus=null;
    }
    public void Move(string id) {
        if(result!="*")throw new InvalidOperationException("This game has ended. Start a new game to play again.");
        Apply(FiveDProtocol.ParseMove(live,id));
    }
    public void Undo() {
        if(pendingUndo.Count==0)return;
        live=pendingUndo[^1];pendingUndo.RemoveAt(pendingUndo.Count-1);pending.RemoveAt(pending.Count-1);liveStatus=null;
        branches.RemoveAll(b=>b.Turn==turns.Count+1&&b.Action>pending.Count);
    }
    public void Submit() {
        if(pending.Count==0||!RoyalLegality.CanSubmit(live))throw new InvalidOperationException("Move on the required boards and resolve every check before submitting.");
        bool white=live.Color;
        if(!live.SubmitMoves())throw new InvalidOperationException("The present has not advanced.");
        turns.Add(new(turns.Count+1,white,string.Join(' ',pending.Select(m=>m.Notation)),pending.ToArray()));
        positions.Add(live.Clone());pending.Clear();pendingUndo.Clear();liveStatus=null;
        UpdateStatus();
    }
    private CheckAndMateResult UpdateStatus() {
        liveStatus??=CheckAndMate.Analyze(live,new(){MaxWork=100000,MaxMilliseconds=250});
        if(liveStatus.IsTerminal)result=liveStatus.WinnerWhite is {} winner?winner?"1-0":"0-1":"1/2-1/2";
        return liveStatus;
    }
    public void Resign(string? side) {
        if(result!="*")return;
        while(pending.Count>0)Undo();result=side=="black"?"1-0":"0-1";
    }
    public PositionView Snapshot(int index=-1) {
        bool current=index<0||index>=positions.Count;int viewing=current?turns.Count:Math.Clamp(index,0,positions.Count-1);
        GameState state=current?live:positions[viewing];
        var status=current?UpdateStatus():CheckAndMate.Analyze(state,new(){MaxWork=25000,MaxMilliseconds=80});
        var moves=new List<MoveView>();
        if(current&&result=="*")foreach(var move in GameLegalActions.Enumerate(state)) {
            if(move==null)continue;var next=state.Clone();
            if(next.MakeMove(move.Clone(),true)&&RoyalLegality.IsLegalAfterMove(state,move,next))moves.Add(ViewMove(state,move));
        }
        return new(BoardViews(state),moves.ToArray(),state.Color,state.Present,current&&pending.Count>0&&RoyalLegality.CanSubmit(state),
            status.InCheck,status.Status.ToString(),status.Reason,status.WinnerWhite,current?pending.Count:0,turns.Count,viewing,
            turns.ToArray(),current?pending.ToArray():[],Export(),result,whiteName,blackName,state.EvenStart,
            branches.Where(b=>current||b.Turn<=viewing).ToArray(),status.InCheck?CheckPreview(state):null,engineStrength);
    }
    private static BoardView[] BoardViews(GameState state) {
        var boards=new List<BoardView>();
        foreach(var line in state.Multiverse) {
            void Add(List<Board> list,int start,bool white){for(int i=0;i<list.Count;i++){var b=list[i];boards.Add(new(line.Layer,start+i,white,b.Width,b.Height,(int[])b.Brd.Clone(),line.IsMostRecentTime(start+i,white),state.LayerIsActive(line.Layer)));}}
            Add(line.WBoards,line.WhiteStart,true);Add(line.BBoards,line.BlackStart,false);
        }
        return boards.ToArray();
    }
    private static CheckPreviewView CheckPreview(GameState state) {
        // Native check detection advances the active present boards by one
        // half-turn. Keep that hypothetical position separate from the game.
        var preview=state.Clone();HypercuboidGameAdapter.IsInCheck(preview);
        var threats=new List<Move>();HypercuboidGameAdapter.FindDirectThreat(preview,state.Color,threats:threats);
        Square Sq(CoordFive c)=>new(c.X,c.Y,c.T,c.L,c.Color);
        return new(BoardViews(preview),threats.Select(t=>new CheckView(Sq(t.Origin),Sq(t.Dest))).ToArray());
    }
    public string Export() {
        string Safe(string s)=>s.Replace('"','\'').Replace('\n',' ').Replace('\r',' ');
        var b=new StringBuilder($"[Event \"SkynetZero local game\"]\n[Site \"SkynetZero Web\"]\n[White \"{Safe(whiteName)}\"]\n[Black \"{Safe(blackName)}\"]\n[Result \"{result}\"]\n[Board \"custom\"]\n[Size \"{initial.Width}x{initial.Height}\"]\n[Color \"{(initial.Color?"white":"black")}\"]\n");
        b.AppendLine("[Mode \"5D\"]");
        if(engineStrength>0)b.AppendLine($"[SkynetStrength \"{engineStrength}\"]");
        string promotions=string.Join(' ',Enumerable.Range(2,11).Where(t=>t!=7&&initial.PromotionAllowed(t)).Select(t=>Board.PieceChars[t]));
        b.AppendLine($"[Promotions \"{promotions}\"]");
        if(pending.Count>0)b.AppendLine("[SkynetPartial \"true\"]");
        b.AppendLine(FiveDProtocol.Dump(initial).Replace("] [","]\n["));b.AppendLine();
        int Number(int index)=>(index-1+(initial.Color?0:1))/2+1;
        foreach(var turn in turns)b.Append($"{Number(turn.Index)}{(turn.White?"w":"b")}. ").AppendLine(turn.Notation);
        if(pending.Count>0)b.Append($"{Number(turns.Count+1)}{(live.Color?"w":"b")}. ").AppendLine(string.Join(' ',pending.Select(m=>m.Notation)));
        return b.AppendLine(result).ToString();
    }
    public void Import(string pgn) {
        if(pgn.Length>1_000_000)throw new InvalidDataException("5DPGN files must be under 1 MB.");
        var headers=new StringBuilder();var body=new StringBuilder();var tags=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string line in pgn.Replace("\r","").Split('\n')) {
            var t=line.Trim();if(t.StartsWith('[')) {
                foreach(Match m in Regex.Matches(t,@"\[[^\]]+\]")) {
                    headers.AppendLine(m.Value);
                    var tag=Regex.Match(m.Value,"^\\[(\\w+)\\s+\"(.*)\"\\]$");if(tag.Success)tags[tag.Groups[1].Value]=tag.Groups[2].Value;
                }
            } else body.AppendLine(line);
        }
        if(!tags.ContainsKey("Board"))headers.Insert(0,headers.ToString().Contains(':')?"[Board \"custom\"]\n[Size \"8x8\"]\n":"[Board \"standard\"]\n");
        string normalizedHeaders=headers.ToString();
        if(tags.TryGetValue("Board",out string? variant)) {
            string normalized=Regex.Replace(variant.ToLowerInvariant(),@"[\s_–—-]","") switch {
                "standardturnzero" or "standardt0"=>"standard-t0", "standardprincess"=>"standard-princess", "standard"=>"standard", "custom"=>"custom", _=>variant
            };
            normalizedHeaders=Regex.Replace(normalizedHeaders,@"\[Board\s+""[^""]*""\]",$"[Board \"{normalized}\"]",RegexOptions.IgnoreCase);
        }
        if(tags.TryGetValue("Size",out var size)&&(!Regex.IsMatch(size,@"^[1-8]x[1-8]$")))throw new InvalidDataException("Board size must be between 1×1 and 8×8.");
        normalizedHeaders=Regex.Replace(normalizedHeaders,@"\[Color\s+""([^""]*)""\]",m=>$"[Color \"{m.Groups[1].Value.ToLowerInvariant()}\"]",RegexOptions.IgnoreCase);
        var parsed=FENParser.ShadSTDGSMFromString(normalizedHeaders)??throw new InvalidDataException("Unsupported board setup. Use standard, standard-t0, standard-princess or explicit custom 5DFEN boards.");
        if(parsed.Width is <1 or >8||parsed.Height is <1 or >8||parsed.Multiverse.Count>64)throw new InvalidDataException("Use boards up to 8×8 and at most 64 initial timelines.");
        if(!tags.ContainsKey("Color"))parsed.Color=parsed.CalcPresent();parsed.StartPresent=parsed.Present;
        initial=parsed.Clone();initial.QueenPromotionOnly=false;
        if(tags.TryGetValue("Promotions",out var allowed)) {
            int mask=0;
            foreach(char piece in allowed.ToUpperInvariant().Where(char.IsLetter)) {
                int type=Array.IndexOf(Board.PieceChars,piece);
                if(type is <2 or >12 or 7)throw new InvalidDataException("Invalid promotion piece: "+piece);
                mask|=1<<type;
            }
            if(mask==0)throw new InvalidDataException("At least one promotion piece is required.");
            int all=Enumerable.Range(2,11).Where(t=>t!=7).Aggregate(0,(m,t)=>m|(1<<t));
            initial.PromotionMask=mask==all?0:mask;
        }
        DrawRules.Enable(initial);live=initial.Clone();positions.Clear();positions.Add(initial.Clone());turns.Clear();pending.Clear();pendingUndo.Clear();branches.Clear();liveStatus=null;result="*";
        whiteName=tags.GetValueOrDefault("White","White");blackName=tags.GetValueOrDefault("Black","Black");
        engineStrength=int.TryParse(tags.GetValueOrDefault("SkynetStrength"),out int savedStrength)?Math.Clamp(savedStrength,1,100):-1;
        string text=Regex.Replace(RemoveComments(body.ToString()),@"\(L\s*([+-]?\d+)\s+T\s*(\d+)\)","($1T$2)",RegexOptions.IgnoreCase);int offset=0;
        while(offset<text.Length) {
            if(char.IsWhiteSpace(text[offset])){offset++;continue;}
            string rest=text[offset..];
            var annotation=Regex.Match(rest,@"^(?:\(~T[+-]?\d+\)|\(>L[+-]?\d+\)|\$\d+)");
            if(annotation.Success){offset+=annotation.Length;continue;}
            var boundary=Regex.Match(rest,@"^(?:\d+[wWbB]?\.|/)");
            if(boundary.Success){if(pending.Count>0)Submit();offset+=boundary.Length;continue;}
            var end=Regex.Match(rest,@"^(?:1/2-1/2|1-0|0-1|\*)(?=\s|$)");
            if(end.Success){result=end.Value;offset+=end.Length;if(!string.IsNullOrWhiteSpace(text[offset..]))throw new InvalidDataException("Unexpected moves after the game result.");break;}
            var token=Regex.Match(rest,@"^(?:\([+-]?\d+T-?\d+\))?[A-Za-z0-9=+#?!~x-]+(?:>{1,2}x?\([+-]?\d+T-?\d+\)[A-Za-z0-9=+#?!~x-]+)?");
            if(!token.Success)throw new InvalidDataException($"Unrecognized 5DPGN near: {rest[..Math.Min(45,rest.Length)]}");
            if(result!="*")throw new InvalidDataException("Moves continue after an engine-detected game ending.");
            string clean=Regex.Replace(token.Value,@"[+#?!~]+$","");
            try {var turn=FENParser.StringToTurn(live,clean,live.EvenStart);if(turn?.Moves.Length!=1)throw new InvalidDataException("Expected one move.");Apply(turn.Moves[0]);}
            catch(Exception error){throw new InvalidDataException($"Turn {turns.Count+1}, {token.Value}: {error.Message}");}
            offset+=token.Length;if(turns.Count>1000)throw new InvalidDataException("Import is limited to 1,000 complete turns.");
        }
        string declared=result!="*"?result:tags.GetValueOrDefault("Result","*");result="*";
        if(pending.Count>0&&RoyalLegality.CanSubmit(live)&&tags.GetValueOrDefault("SkynetPartial")!="true")Submit();
        if(declared is "1-0" or "0-1" or "1/2-1/2")result=declared;
        UpdateStatus();
    }
    private static string RemoveComments(string text) {
        var b=new StringBuilder();int depth=0;bool semicolon=false;
        foreach(char c in text){if(c=='\n')semicolon=false;if(semicolon)continue;if(c==';'&&depth==0){semicolon=true;continue;}if(c=='{'){depth++;continue;}if(c=='}'){if(--depth<0)throw new InvalidDataException("Unexpected comment terminator.");continue;}if(depth==0)b.Append(c);}
        if(depth!=0)throw new InvalidDataException("Unclosed 5DPGN comment.");return b.ToString();
    }
    public Evaluation Analyze(int index,int strength) {
        int i=Math.Clamp(index,0,positions.Count-1);return Search(positions[i],i,strength).Evaluation;
    }
    public Evaluation Play(int strength) {
        if(pending.Count!=0)throw new InvalidOperationException("Submit your pending turn first.");
        if(UpdateStatus().IsTerminal||result!="*")throw new InvalidOperationException("This game has ended.");
        var search=Search(live,turns.Count,strength);
        Move?[] actions=search.Result?.CompleteTurnMoves??[];
        if(actions.Length==0)throw new InvalidOperationException("No complete turn was found. Try a higher strength or retry the search.");
        foreach(var move in actions){if(move==null){if(pending.Count>0)Submit();}else Apply(move);}
        if(pending.Count>0)Submit();engineStrength=Math.Max(engineStrength,Strength.FromLevel(strength).Level);return search.Evaluation;
    }
    private (AlphaBetaResult? Result,Evaluation Evaluation) Search(GameState source,int index,int strength) {
        var state=source.Clone();bool white=state.Color;var level=Strength.FromLevel(strength);
        // Match native frozen-arena evaluation: the same tables and root travel
        // routes apply throughout a search, instead of treating every leaf as
        // a new root and awarding new travel opportunities inconsistently.
        using var tables=SacrificeTable.UseSnapshot(evaluationTables);
        using var travel=SacrificeTable.UseRootTravel(SacrificeTable.CaptureRootTravel(state));
        var proof=CheckAndMate.Analyze(state,new(){MaxWork=100000,MaxMilliseconds=250});
        if(proof.IsTerminal)return(null,new(index,proof.WinnerWhite==null?0:null,proof.WinnerWhite!=null?0:null,proof.WinnerWhite,0,0,0,false,"",proof.Status.ToString()));
        var settings=new AlphaBetaSettings {CompleteTurnDepth=level.Depth,MaxNodes=level.Nodes,Threads=1,MaxActionsPerTurn=32,TranspositionEntries=4096,QuiescenceTurns=1};
        var found=CompleteTurnAlphaBetaSearch.Choose(state,settings,level.Milliseconds,evaluate:leaf=>model.Evaluate(leaf.State,white),evaluatorUnits:true);
        int? mate=found.ProvenMateDistance is {} d?(Math.Abs(d)+1)/2:null;
        bool? winner=mate!=null?(found.SourceScore>0?white:!white):null;
        // Internal units are normalized to the v10 active-pawn coefficient: 100 cp = one reference pawn.
        int? cp=mate!=null?null:(int)Math.Round(Math.Clamp(found.SourceScore??0,-20000,20000)/484.2*100)*(white?1:-1);
        bool staticOnly=found.CompletedDepth==0&&mate==null;
        if(staticOnly)cp=(int)Math.Round(Math.Atanh(model.Evaluate(state,true))*8700/484.2*100);
        var replay=state.Clone();var pv=new List<string>();
        foreach(var move in found.CompleteTurnMoves){if(move==null){if(RoyalLegality.CanSubmit(replay))replay.SubmitMoves();}else{pv.Add(ViewMove(replay,move).Notation);replay.MakeMove(move.Clone(),true);}}
        return(found,new(index,cp,mate,winner,found.CompletedDepth,found.Nodes,found.Seconds,found.Interrupted,string.Join(' ',pv),found.StopReason.ToString(),staticOnly));
    }
}
