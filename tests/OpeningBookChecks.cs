using System.Security.Cryptography;
using System.Text.Json;
using FileIO5D;
using SkynetZero;
using SkynetZero.Web;

internal static class OpeningBookChecks
{
    private sealed record Source(string File,string Author,string Sha256);
    private sealed record Line(string Id,string Name,string Variant,string[] Plies,double Weight=1,bool? RecommendForWhite=null);
    private sealed record Seed(int Version,Source Source,Line[] Lines);

    // Run with --export-book SOURCE/resources/books OUTPUT.json. Uses the
    // production 5D parser, complete history hashing and strict royal legality.
    internal static void Export(string directory,string output){
        var entries=new Dictionary<(string,BookAction),OpeningChoice>();
        var sources=new List<OpeningSource>();int plies=0,lines=0;
        foreach(string name in new[]{"openings.json","f7-sacrifice.json","london-ng5.json"}){
            byte[] bytes=File.ReadAllBytes(Path.Combine(directory,name));
            var seed=JsonSerializer.Deserialize<Seed>(bytes)??throw new InvalidDataException(name);
            if(seed.Version!=1)throw new InvalidDataException("Unknown book version.");
            sources.Add(new(seed.Source.File,seed.Source.Author,seed.Source.Sha256,Convert.ToHexString(SHA256.HashData(bytes))));
            foreach(var line in seed.Lines){
                string variant=line.Variant switch{"turn-zero"=>"standard-t0","standard"=>"standard",_=>throw new InvalidDataException(line.Variant)};
                GameState state=FENParser.ShadSTDGSMFromString($"[Board \"{variant}\"]")!;lines++;
                foreach(string san in line.Plies){
                    var move=FENParser.StringToTurn(state,san,state.EvenStart)?.Moves.SingleOrDefault()
                        ??throw new InvalidDataException($"{line.Id}: {san}");
                    if(move.Origin.L!=move.Dest.L||move.Origin.T!=move.Dest.T)throw new InvalidDataException("A spatial opening line contains travel.");
                    var next=state.Clone();
                    if(!next.MakeMove(move.Clone(),true)||next.kingWasTaken||!RoyalLegality.IsLegalAfterMove(state,move,next)||
                        !RoyalLegality.CanSubmit(next)||!next.SubmitMoves())throw new InvalidDataException($"Unsafe opening: {line.Id}, {san}");
                    if(line.RecommendForWhite is null||line.RecommendForWhite==state.Color){
                        var key=(SemanticStateHash.Compute(state),BookAction.From(move));
                        if(!entries.TryGetValue(key,out var prior)||prior.Weight<line.Weight)
                            entries[key]=new(key.Item1,key.Item2,line.Id,line.Name,line.Variant,line.Weight);
                    }
                    state=next;plies++;
                }
            }
        }
        var data=new OpeningData(1,sources.ToArray(),entries.Values.OrderBy(c=>c.State,StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal).ToArray());
        File.WriteAllText(output,JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true})+"\n");
        Console.WriteLine($"Exported {lines} supplied lines, {plies} verified plies, {data.Choices.Length} choices at {data.Choices.Select(c=>c.State).Distinct().Count()} exact historical positions.");
    }

    internal static void Run(){
        var book=OpeningRepertoire.Bundled.Value;
        var start=FENParser.ShadSTDGSMFromString("[Board \"standard-t0\"]")!;
        var chosen=book.TryChoose(start)??throw new Exception("Turn-zero opening missing");
        if(chosen.Action.ToX!=5||chosen.Action.ToY!=2)throw new Exception("Classical first move must be Nf3");
        var next=start.Clone();next.MakeMove(chosen.Action.Move(),true);
        if(book.TryChoose(next)!=null)throw new Exception("Book must reject pending turns");
        next.SubmitMoves();
        if(book.TryChoose(next) is not {} reply||reply.Action.ToX!=5||reply.Action.ToY!=5)throw new Exception("Classical reply must be Nf6");
        // Same current board but different multiverse history must miss the book.
        var changed=start.Clone();changed.GetTimeline(0)!.BBoards[0].Brd[0]=0;
        if(book.TryChoose(changed)!=null)throw new Exception("Book ignored a changed past board");
        var standard=FENParser.ShadSTDGSMFromString("[Board \"standard\"]")!;
        if(book.TryChoose(standard) is not {Variant:"standard"})throw new Exception("Standard must have its own repertoire");
        var travel=FENParser.ShadSTDGSMFromString("[Board \"standard-t0\"]\n1. e4 / e5\n2. (0T2)Nb1>>(0T1)b3 /")!;
        if(book.TryChoose(travel)!=null)throw new Exception("Branched games must return to search");
        var offBook=FENParser.ShadSTDGSMFromString("[Board \"standard-t0\"]\n1. a4 /")!;
        if(book.TryChoose(offBook)!=null)throw new Exception("Unknown opening must return to search");
        Console.WriteLine("PASS C# opening repertoire: both variants, classical continuation, pending/history/branch guards and out-of-book fallback");
    }
}
