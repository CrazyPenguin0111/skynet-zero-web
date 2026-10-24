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
    static string Fingerprint(PositionView p)=>JsonSerializer.Serialize(new{p.Boards,p.White,p.Pending,p.CanSubmit,p.TurnCount,p.Result,p.Even,p.Branches,p.TimelineAdvantage},EngineBridge.Json);
    public static void Main(string[] args)
    {
        if(args is ["--export-book",var directory,var output]){OpeningBookChecks.Export(directory,output);return;}
        OpeningBookChecks.Run();
        var levels=Enumerable.Range(1,100).Select(Strength.FromLevel).ToArray();
        Check(levels.Select(l=>l.Nodes).Distinct().Count()==100&&levels.Select(l=>l.Milliseconds).Distinct().Count()==100,"Strength must have 100 distinct search budgets");
        Check(levels.Zip(levels.Skip(1)).All(p=>p.First.Nodes<p.Second.Nodes&&p.First.Milliseconds<p.Second.Milliseconds&&p.First.Depth<=p.Second.Depth),"Strength budgets must increase monotonically");
        Check(Strength.FromLevel(-1)==levels[0]&&Strength.FromLevel(101)==levels[^1],"Strength range bounds");
        Check(levels[0].Milliseconds==1000&&levels[^1].Milliseconds==60000&&levels[^1].Nodes==65536&&levels[33].Nodes==512,"Strength endpoints and default budget");
        Check(levels[^1].Depth==64,"Maximum strength must allow iterative deepening to depth 64 within its time/node budget");
        var model=PortableModel.LoadBundled();
        var vectors=JsonSerializer.Deserialize<VectorCase[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"parity-vectors.json")))!;
        using(var table=typeof(PortableModel).Assembly.GetManifestResourceStream("SkynetZero.LearnedTables.bin")!)SacrificeTable.ReadLearned(table,true);
        double max=0;
        foreach(var v in vectors){if(v.pgn!=null){var actualFeatures=new float[107];ExtendedEvaluationTermsEncoding.Fill(FENParser.ShadSTDGSMFromString(v.pgn)!,v.white,actualFeatures);Check(actualFeatures.SequenceEqual(v.input),"Production feature parity: "+v.pgn);}var actual=model.Forward(v.input);max=Math.Max(max,Math.Abs(actual-v.expected));Check(Math.Abs(actual-v.expected)<.00002f,$"Model parity {actual} vs {v.expected}");}
        Console.WriteLine($"PASS TorchSharp model parity: {vectors.Length} vectors, max error {max:E3}");
        Send(new("new",Side:"black"));
        var bookMove=Send(new("engine",Strength:100));
        Check(bookMove.Evaluation is {StopReason:"OpeningBook",BookName:not null,Centipawns:null,Mate:null}&&bookMove.Position!.TurnCount==1,"Engine play must use the opening book without inventing a score");
        Check(bookMove.Position!.Turns.Single().BookName!=null,"Book membership must be attached to the actual played turn");
        Send(new("import",Pgn:bookMove.Position.Pgn));Check(State().Turns.Single().BookName!=null,"Imported book moves must be identified from their original position");
        var openingAnalysis=Send(new("analyze",Index:0,Strength:1)).Evaluation!;
        Check(openingAnalysis.BookName is null&&openingAnalysis.StopReason!="OpeningBook","Analysis must search even when a position is in the opening book");
        Send(new("new"));Send(new("move",Move:"(0T1)g1f3"));Send(new("submit"));
        var nf3=State();Check(nf3.Turns.Single().BookName is not null,"1. Nf3 must be a book move");
        Send(new("import",Pgn:nf3.Pgn));
        var nf3Before=Send(new("analyze",Index:0,Strength:1)).Evaluation!;
        var nf3After=Send(new("analyze",Index:1,Strength:1)).Evaluation!;
        Check(TurnAnalysis.Compare(1,true,nf3Before,nf3After,State().Turns.Single().BookName).Verdict=="Book","Imported Nf3 must remain Book after real analysis");
        Check(Send(new("engine",Strength:100)).Evaluation!.BookName is not null,"Black must reply from the opening book");
        foreach(string variant in new[]{"standard","standard-t0"}){
            Send(new("import",Pgn:$"[Board \"{variant}\"]\n1. (0T1)Ng1f3 /\n*"));
            Check(State().Turns.Single().BookName is not null,"1. Nf3 must be Book in "+variant);
        }
        var s=Send(new("new")).Position!;Check(s.LegalMoves.Length==20,"Initial position must have 20 legal moves");
        Check(s.TimelineAdvantage==0,"Starting timelines must be balanced");
        Check(s.CheckPreview is null,"Quiet positions must not expose a check preview");
        Send(new("submit"),error:true);
        string original=Fingerprint(State());Send(new("move",Move:"(0T1)e2e5"),error:true);Check(Fingerprint(State())==original,"Illegal move mutated state");
        Send(new("move",Move:"(0T1)e2e4"));s=State();Check(s.CanSubmit&&s.PendingCount==1,"Pending move not submittable");
        string pending=Fingerprint(s);Send(new("import",Pgn:s.Pgn));Check(Fingerprint(State())==pending,"Pending-turn round trip");
        var boundary=Send(new("view",Index:0)).Position!;Check(boundary.PendingCount==0&&boundary.Boards.Length<s.Boards.Length,"Analysis must display the submitted boundary");
        Check(Fingerprint(State())==pending,"Reviewing a submitted position must preserve pending moves");
        Send(new("undo"));Check(Fingerprint(State())==original,"Undo did not restore exact state");
        Send(new("move",Move:"(0T1)e2e4"));Send(new("submit"));Send(new("move",Move:"(0T1)e7e5"));Send(new("submit"));
        Send(new("move",Move:"(0T2)b1(0T1)b3"));
        Check(State().Branches is [{Layer:1,ParentLayer:0,ParentTime:1,ParentWhite:true,StartTime:1,StartWhite:false}],"Branch ancestry must identify the destination board");
        Check(State().TimelineAdvantage==-1,"A pending White branch gives Black timeline advantage");
        Send(new("undo"));Check(State().Branches.Length==0,"Undo must remove pending branch ancestry");
        Check(State().TimelineAdvantage==0,"Undo must restore timeline balance");
        Send(new("move",Move:"(0T2)b1(0T1)b3"));Send(new("submit"));s=State();Check(s.Boards.Select(b=>b.Layer).Distinct().Count()==2,"Time travel failed to branch");
        Check(Send(new("view",Index:2)).Position!.Branches.Length==0,"Historical view must hide future branches");
        Check(s.TimelineAdvantage==-1&&Send(new("view",Index:2)).Position!.TimelineAdvantage==0,"Analysis rewinds must use historical timeline balance");
        UiChecks.MultiverseHistory(s).GetAwaiter().GetResult();
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
        Check(State().TimelineAdvantage==0,"The +0/-0 starting pair must not invent a timeline advantage");
        Send(new("move",Move:"(-1T1)e2e4"));Check(!State().CanSubmit,"Two-board turn must require both boards");Send(new("submit"),error:true);
        Send(new("move",Move:"(0T1)d2d4"));Check(State().CanSubmit,"Completed two-board turn must submit");Send(new("submit"));
        s=State();string even=Fingerprint(s);Send(new("import",Pgn:s.Pgn));Check(Fingerprint(State())==even,"Even timelines and multi-action turn round trip");
        Send(new("import",Pgn:$"[Board \"custom\"]\n[Size \"8x8\"]\n[Color \"Black\"]\n[{standard}:0:1:b]\n1b. (0T1)Pe7e5"));s=State();Check(s.TurnCount==1&&s.White,"Black-to-move initial position");Check(s.Pgn.Contains("1b."),"Black-first PGN numbering");Send(new("import",Pgn:s.Pgn));
        Send(new("import",Pgn:"[Board \"Standard - Turn Zero\"]\n1. (L0 T1)e4 / (L0 T1)e5"));Check(State().TurnCount==2,"Display variant names and long coordinates");
        Console.WriteLine("PASS castling, underpromotion and en passant");
        Send(new("import",Pgn:"[Board \"custom\"]\n[Size \"8x8\"]\n[2ppp3/2pkp3/N1ppp3/8/8/8/8/7K:0:1:w]"));
        var mate=Send(new("analyze",Index:0,Strength:34)).Evaluation!;
        Check(SacrificeTable.CurrentRootTravel is null&&!SacrificeTable.HasScopedSnapshot,"Search evaluation scopes must not leak into another request");
        Console.WriteLine($"Mate fixture: {mate.Display}, {mate.BestTurn}, depth {mate.Depth}, nodes {mate.Nodes}");
        Check(mate.Mate==1&&mate.Winner==true,"Mate in one must be proven and use mate notation");
        Send(new("engine",Strength:34));s=State();Check(s.Result=="1-0"&&s.Status=="Checkmate","Engine must execute mating turn");
        Check(s.CheckPreview is {Checks.Length:>0},"Checkmate must expose the native checking attacks");
        Check(s.EngineStrength==34&&s.Pgn.Contains("[SkynetStrength \"34\"]"),"Export must retain the highest engine strength for review");
        UiChecks.CheckPreview(s).GetAwaiter().GetResult();
        // Landing on the opponent's timeline must still create a branch on
        // the mover's side, and undo/export must retain the native layer IDs.
        foreach(bool white in new[]{true,false}){
            string empty="7k/8/8/8/8/8/8/7K",queen=$"7k/8/8/8/{(white?'Q':'q')}7/8/8/7K";
            var setup=new System.Text.StringBuilder($"[Board \"custom\"]\n[Size \"8x8\"]\n[Color \"{(white?"white":"black")}\"]\n");
            foreach(int layer in new[]{-1,0,1})foreach(var (time,color) in white?new[]{(1,'w'),(1,'b'),(2,'w')}:new[]{(1,'b'),(2,'w'),(2,'b')})
                setup.AppendLine($"[{(layer==0?queen:empty)}:{layer}:{time}:{color}]");
            Send(new("import",Pgn:setup.ToString()));var beforeCrossing=State();
            Check(beforeCrossing.TimelineAdvantage==0,"Symmetric imported timelines are balanced");
            Send(new("move",Move:$"(0T2)a4>({(white?-1:1)}T1)a4"));var branched=State();
            Check(branched.TimelineAdvantage==(white?-1:1),"Advantage belongs to the side with fewer branches, even after cross-side travel");
            var crossBranch=branched.Branches.Single();
            Check(crossBranch.Layer==(white?2:-2)&&crossBranch.ParentLayer==(white?-1:1),"Cross-side travel must create a branch owned by the mover");
            foreach(bool flipped in new[]{false,true}){
                var camera=new MultiverseLayout(branched.Boards,"normal",flipped);
                Check((camera.Top(crossBranch.Layer)>camera.Top(0))==(white!=flipped),"Branch display must follow the mover's side even across opposing timelines");
            }
            Send(new("import",Pgn:branched.Pgn));Check(State().Branches.Single()==crossBranch,"Cross-side branch PGN round trip");
            Send(new("undo"));Check(State().Boards.Length==beforeCrossing.Boards.Length&&State().Branches.Length==0,"Cross-side branch undo");
        }
        Send(new("import",Pgn:s.Pgn));
        var terminal=Send(new("analyze",Index:s.TurnCount,Strength:34)).Evaluation!;Check(terminal.Mate==0&&terminal.Winner==true,"Terminal mate winner");
        Send(new("move",Move:"(0T1)c7c6"),error:true);
        var before=new Evaluation(0,100,null,null,2,20,.1,false,"","CompletedDepth");
        var after=before with{Index=1,Centipawns=-50};
        Check(TurnAnalysis.Compare(1,true,before,after).Loss==150,"White CPL orientation");
        Check(TurnAnalysis.Compare(1,false,after,before).Loss==150,"Black CPL orientation");
        Check(TurnAnalysis.Compare(1,true,after,before).Loss==0,"CPL must be nonnegative");
        Check(TurnAnalysis.Compare(1,true,before,mate).Loss==null,"Mate transitions excluded from ACPL");
        Check(TurnAnalysis.Compare(1,true,before,after with{Depth=0,StaticOnly=true,Centipawns=-900}).Loss==null,"An unsearched static score must never produce a blunder label");
        var equal=before with{Index=1};
        Check(TurnAnalysis.Compare(1,true,before,equal).Verdict=="Excellent","Zero loss alone does not establish an engine-best move");
        var best=before with{PlayedTurn=new(true,0)};
        Check(TurnAnalysis.Compare(1,true,best,equal).Verdict=="Best","Best must match the engine’s searched turn");
        Check(TurnAnalysis.Compare(1,true,best,equal,"Classical").Verdict=="Book","Known repertoire moves have the Book badge");
        var sacrifice=best with{Depth=3,PlayedTurn=new(true,200)};
        Check(TurnAnalysis.Compare(1,true,sacrifice,equal with{Depth=3}).Verdict=="Brilliant","A sound engine-best piece sacrifice qualifies for Brilliant");
        Check(TurnAnalysis.Compare(1,true,sacrifice,equal).Verdict!="Brilliant","Shallow evidence cannot award Brilliant");
        Check(TurnAnalysis.Compare(1,true,sacrifice with{PlayedTurn=new(false,200,600,3)},equal with{Depth=3,Centipawns=-500}).Verdict=="Blunder","An unsound sacrifice is a blunder, not Brilliant");
        Check(TurnAnalysis.Compare(1,true,best,after with{Centipawns=-900}).Loss==0,"A different next-position horizon cannot turn the same searched best move into a blunder");
        Check(TurnAnalysis.Compare(1,true,before with{PlayedTurn=new(false,0)},after).Verdict=="Unscored","Incomplete root comparisons must not fall back to mismatched horizons");
        foreach(var (loss,label) in new[]{(50,"Inaccuracy"),(100,"Mistake"),(300,"Blunder")})
            Check(TurnAnalysis.Compare(1,true,before,equal with{Centipawns=100-loss}).Verdict==label,"Centipawn loss classification: "+label);
        var offeredBefore=FENParser.ShadSTDGSMFromString("[Board \"custom\"]\n[Size \"8x8\"]\n[7k/6p1/8/7p/8/8/3B4/7K:0:1:w]")!;
        var offeredMove=FiveDProtocol.ParseMove(offeredBefore,"(0T1)d2h6");
        var offeredView=GameSession.ViewMove(offeredBefore,offeredMove);var offeredAfter=offeredBefore.Clone();
        Check(offeredAfter.MakeMove(offeredMove,true)&&offeredAfter.SubmitMoves(),"Sacrifice fixture must be a legal submitted move");
        Check(GameSession.SacrificeOffer(offeredBefore,offeredAfter,new(1,true,offeredView.Notation,[offeredView]))>100,"Sacrifice detector must verify a real hanging bishop with the native exchange evaluator");
        Check(GameSession.SacrificeOffer(offeredBefore,offeredAfter,new(1,true,"multi",[offeredView,offeredView]))==0,"Ambiguous multi-board sacrifices cannot earn an invented Brilliant badge");
        UiChecks.Run(State(),mate).GetAwaiter().GetResult();
        UiChecks.AnalysisCompletion(State()).GetAwaiter().GetResult();
        Send(new("import",Pgn:s.Pgn));Check(State().EngineStrength==34,"Imported game must retain engine analysis strength");
        UiChecks.Keyboard().GetAwaiter().GetResult();
        Send(new("import",Pgn:"[Board \"custom\"]\n[Size \"8x8\"]\n[k3r3/8/8/8/1b6/8/8/4K3:0:1:w]"));s=State();
        Check(s.InCheck&&s.CheckPreview?.Checks.Length==2,"Double check must show both attackers");
        Check(s.Boards.Length==1&&s.CheckPreview!.Boards.Length==2,"Preview must advance the present by one half-turn without changing real boards");
        Check(s.CheckPreview!.Checks.All(c=>!c.From.White&&!c.To.White),"White check preview uses the following black board");
        string checkedPgn=s.Pgn;Check(State().Pgn==checkedPgn,"Check preview must not change exported moves");
        Send(new("import",Pgn:"[Board \"custom\"]\n[Size \"8x8\"]\n[k7/8/8/8/8/8/8/4K3:-0:1:w]\n[k7/8/8/8/8/8/8/4r2K:+0:1:w]"));s=State();
        Check(s.InCheck&&s.CheckPreview!.Checks.Any(c=>c.From.L!=c.To.L),"Cross-timeline checks must retain their native coordinates");
        Send(new("import",Pgn:"[Board \"standard-t0\"]\n1. e4 / e5\n2. a3 / (0T2)Nb8>>(0T1)b6\n*"));
        UiChecks.MultiverseHistory(State(),false).GetAwaiter().GetResult();
        UiChecks.AnalysisVariations().GetAwaiter().GetResult();
        Console.WriteLine($"PASS {checks} assertions and Razor render checks");
    }
}
