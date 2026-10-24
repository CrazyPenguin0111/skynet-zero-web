using System.Text.Json;
using SkynetZero;
using SkynetZero.Web;
using FileIO5D;
internal static class Regression
{
    private static int checks;
    private sealed record VectorCase(float[] input,float expected,string? pgn=null,bool white=true);
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static EngineReply Send(EngineRequest r,bool error=false){var reply=JsonSerializer.Deserialize<EngineReply>(EngineBridge.Dispatch(JsonSerializer.Serialize(r,EngineBridge.Json)),EngineBridge.Json)!;Check(error?reply.Error!=null:reply.Error==null,$"{r.Command}: {reply.Error}");return reply;}
    static PositionView State()=>Send(new("state")).Position!;
    static string Fingerprint(PositionView p)=>JsonSerializer.Serialize(new{p.Boards,p.White,p.Pending,p.CanSubmit,p.TurnCount,p.Result,p.Even},EngineBridge.Json);
    public static void Main()
    {
        var model=PortableModel.LoadBundled();
        var vectors=JsonSerializer.Deserialize<VectorCase[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"parity-vectors.json")))!;
        using(var table=typeof(PortableModel).Assembly.GetManifestResourceStream("SkynetZero.LearnedTables.bin")!)SacrificeTable.ReadLearned(table,true);
        double max=0;
        foreach(var v in vectors){if(v.pgn!=null){var actualFeatures=new float[107];ExtendedEvaluationTermsEncoding.Fill(FENParser.ShadSTDGSMFromString(v.pgn)!,v.white,actualFeatures);Check(actualFeatures.SequenceEqual(v.input),"Production feature parity: "+v.pgn);}var actual=model.Forward(v.input);max=Math.Max(max,Math.Abs(actual-v.expected));Check(Math.Abs(actual-v.expected)<.00002f,$"Model parity {actual} vs {v.expected}");}
        Console.WriteLine($"PASS TorchSharp model parity: {vectors.Length} vectors, max error {max:E3}");
        var s=Send(new("new")).Position!;Check(s.LegalMoves.Length==20,"Initial position must have 20 legal moves");
        Send(new("submit"),error:true);
        string original=Fingerprint(State());Send(new("move",Move:"(0T1)e2e5"),error:true);Check(Fingerprint(State())==original,"Illegal move mutated state");
        Send(new("move",Move:"(0T1)e2e4"));s=State();Check(s.CanSubmit&&s.PendingCount==1,"Pending move not submittable");
        string pending=Fingerprint(s);Send(new("import",Pgn:s.Pgn));Check(Fingerprint(State())==pending,"Pending-turn round trip");
        var boundary=Send(new("view",Index:0)).Position!;Check(boundary.PendingCount==0&&boundary.Boards.Length<s.Boards.Length,"Analysis must display the submitted boundary");
        Check(Fingerprint(State())==pending,"Reviewing a submitted position must preserve pending moves");
        Send(new("undo"));Check(Fingerprint(State())==original,"Undo did not restore exact state");
        Send(new("move",Move:"(0T1)e2e4"));Send(new("submit"));Send(new("move",Move:"(0T1)e7e5"));Send(new("submit"));
        Send(new("move",Move:"(0T2)b1(0T1)b3"));Send(new("submit"));s=State();Check(s.Boards.Select(b=>b.Layer).Distinct().Count()==2,"Time travel failed to branch");
        string branch=Fingerprint(s);Send(new("import",Pgn:s.Pgn));Check(Fingerprint(State())==branch,"Time-travel round trip changed position");
        Send(new("import",Pgn:"[Board \"standard-t0\"]\n1. (0T1)Pe2e4 / (0T1)Pe7e5\n2. (0T2)Nb1>>(0T1)b3 /\n*"));Check(Fingerprint(State())==branch,"Time-travel example failed");
        Send(new("import",Pgn:"[Board \"standard\"]\n1. e5 @"),error:true);Check(Fingerprint(State())==branch,"Invalid import destroyed previous game");
        Console.WriteLine("PASS legal moves, submissions, pending undo, time travel, PGN round trips and transactional errors");
        Send(new("import",Pgn:"[Board \"custom\"]\n[Size \"8x8\"]\n[4k3/8/8/8/8/8/8/R3K*2R*:0:1:w]\n1. (0T1)O-O /\n*"));s=State();Check(s.Boards.Last(b=>b.Frontier).Pieces[6]==7,"Castling king destination");
        Send(new("import",Pgn:s.Pgn));
        Send(new("import",Pgn:"[Board \"custom\"]\n[Size \"8x8\"]\n[7k/P7/8/8/8/8/8/7K:0:1:w]\n1. (0T1)a8=N /\n*"));s=State();Check(s.Boards.Last(b=>b.Frontier).Pieces[56]==2,"Knight promotion");Send(new("import",Pgn:s.Pgn));
        Send(new("import",Pgn:"[Board \"custom\"]\n[Size \"8x8\"]\n[Promotions \"Q N\"]\n[7k/P7/8/8/8/8/8/7K:0:1:w]"));s=State();
        Check(s.LegalMoves.Where(m=>m.Promotion>0).Select(m=>m.Promotion).Order().SequenceEqual(new[]{2,6}),"Promotion tag must restrict legal choices");
        string restricted=Fingerprint(s);Send(new("move",Move:"(0T1)a7a8R"),error:true);Check(Fingerprint(State())==restricted,"Disallowed promotion mutated state");
        Send(new("import",Pgn:s.Pgn));Check(Fingerprint(State())==restricted,"Promotion restrictions must survive export/import");
        Send(new("move",Move:"(0T1)a7a8N"));Send(new("submit"));
        Send(new("import",Pgn:"[Board \"standard\"]\n1. e4 / a6\n2. e5 / d5\n3. exd6 /\n*"));s=State();var board=s.Boards.Last(b=>b.Frontier);Check(board.Pieces[5*8+3]==1&&board.Pieces[4*8+3]==0,"En passant capture");Send(new("import",Pgn:s.Pgn));
        const string standard="r*nbqk*bnr*/p*p*p*p*p*p*p*p*/8/8/8/8/P*P*P*P*P*P*P*P*/R*NBQK*BNR*";
        Send(new("import",Pgn:$"[Board \"custom\"]\n[Size \"8x8\"]\n[{standard}:-0:1:w]\n[{standard}:+0:1:w]"));
        Send(new("move",Move:"(-1T1)e2e4"));Check(!State().CanSubmit,"Two-board turn must require both boards");Send(new("submit"),error:true);
        Send(new("move",Move:"(0T1)d2d4"));Check(State().CanSubmit,"Completed two-board turn must submit");Send(new("submit"));
        s=State();string even=Fingerprint(s);Send(new("import",Pgn:s.Pgn));Check(Fingerprint(State())==even,"Even timelines and multi-action turn round trip");
        Send(new("import",Pgn:$"[Board \"custom\"]\n[Size \"8x8\"]\n[Color \"Black\"]\n[{standard}:0:1:b]\n1b. (0T1)Pe7e5"));s=State();Check(s.TurnCount==1&&s.White,"Black-to-move initial position");Check(s.Pgn.Contains("1b."),"Black-first PGN numbering");Send(new("import",Pgn:s.Pgn));
        Send(new("import",Pgn:"[Board \"Standard - Turn Zero\"]\n1. (L0 T1)e4 / (L0 T1)e5"));Check(State().TurnCount==2,"Display variant names and long coordinates");
        Console.WriteLine("PASS castling, underpromotion and en passant");
        Send(new("import",Pgn:"[Board \"custom\"]\n[Size \"8x8\"]\n[2ppp3/2pkp3/N1ppp3/8/8/8/8/7K:0:1:w]"));
        var mate=Send(new("analyze",Index:0,Strength:1)).Evaluation!;
        Console.WriteLine($"Mate fixture: {mate.Display}, {mate.BestTurn}, depth {mate.Depth}, nodes {mate.Nodes}");
        Check(mate.Mate==1&&mate.Winner==true,"Mate in one must be proven and use mate notation");
        Send(new("engine",Strength:1));s=State();Check(s.Result=="1-0"&&s.Status=="Checkmate","Engine must execute mating turn");
        var terminal=Send(new("analyze",Index:s.TurnCount,Strength:1)).Evaluation!;Check(terminal.Mate==0&&terminal.Winner==true,"Terminal mate winner");
        Send(new("move",Move:"(0T1)c7c6"),error:true);
        var before=new Evaluation(0,100,null,null,2,20,.1,false,"","CompletedDepth");
        var after=before with{Index=1,Centipawns=-50};
        Check(TurnAnalysis.Compare(1,true,before,after).Loss==150,"White CPL orientation");
        Check(TurnAnalysis.Compare(1,false,after,before).Loss==150,"Black CPL orientation");
        Check(TurnAnalysis.Compare(1,true,after,before).Loss==0,"CPL must be nonnegative");
        Check(TurnAnalysis.Compare(1,true,before,mate).Loss==null,"Mate transitions excluded from ACPL");
        UiChecks.Run(State(),mate).GetAwaiter().GetResult();
        Console.WriteLine($"PASS {checks} assertions and Razor render checks");
    }
}
