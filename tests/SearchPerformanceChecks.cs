using SkynetZero;
using SkynetZero.Web;
using FileIO5D;

internal static class SearchPerformanceChecks
{
    internal static void Run()
    {
        var history=new CompleteTurnHistory();
        history[0]=7;history[255]=11;history[256]=23;history[65535]=31;
        var frozen=history.Fork();
        history[255]=99;history[256]=43;
        if(frozen[255]!=11||frozen[256]!=23||frozen[65535]!=31)
            throw new Exception("Deferred move ordering must retain its original history snapshot");
        var helper=history.Fork();helper.Decay();history.Clear();
        if(helper[0]!=3||helper[255]!=49||helper[256]!=21||helper[65535]!=15||history.Any(v=>v!=0)||frozen[255]!=11)
            throw new Exception("History decay, reset and search helpers must not mutate another snapshot");

        var game=new GameSession(PortableModel.LoadBundled());
        var first=game.Analyze(0,34);var repeated=game.Analyze(0,34);
        if(first.Depth!=2||repeated.Depth!=first.Depth||repeated.Centipawns!=first.Centipawns||repeated.BestTurn!=first.BestTurn||repeated.Nodes!=0)
            throw new Exception("Repeated analysis must reuse its completed search without changing the result");
        var weak=game.Analyze(0,1);
        if(weak.Depth!=1||weak.Nodes==0)throw new Exception("A lower strength must not inherit deeper cached analysis");
        game.New(variant:"t1.5");
        if(game.Analyze(0,1).Nodes==0)throw new Exception("Changing variant must invalidate cached search");
        int turns=0;
        foreach(string variant in new[]{"standard-t0","standard","t1.5","reversed-royalty","standard-princess","defended-pawn"}) {
            var state=FENParser.ShadSTDGSMFromString(VariantCatalog.StartingPgn(variant))!;
            var expected=new HashSet<string>();
            foreach(var move in GameLegalActions.Enumerate(state)) {
                if(move==null)continue;
                var after=state.Clone();
                if(after.MakeMove(move.Clone(),true)&&RoyalLegality.IsLegalAfterMove(state,move,after)&&RoyalLegality.CanSubmit(after))
                    expected.Add(CompleteTurnCandidates.ActionKey(move));
            }
            var generated=new HashSet<string>();
            var generator=new CompleteTurnSearchGenerator(state,restrictOptionalBoards:false);
            while(!generator.Exhausted)foreach(var action in generator.NextBatch()) {
                var after=state.Clone();
                foreach(var move in action.Actions) {
                    var before=after.Clone();
                    if(move==null ? !RoyalLegality.CanSubmit(after)||!after.SubmitMoves() :
                        !after.MakeMove(move.Clone(),true)||!RoyalLegality.IsLegalAfterMove(before,move,after))
                        throw new Exception("Optimized complete-turn validation returned an unsafe turn: "+variant);
                }
                generated.Add(CompleteTurnCandidates.ActionKey(action.FirstAction));turns++;
            }
            if(!generated.SetEquals(expected))throw new Exception("Optimized validation changed the complete legal opening inventory: "+variant);
        }
        Console.WriteLine($"PASS immutable search history pages, repeat analysis reuse, strength/variant isolation and {turns} independently validated opening turns");
    }
}
