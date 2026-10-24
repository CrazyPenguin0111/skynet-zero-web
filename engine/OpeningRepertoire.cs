using System.Reflection;
using System.Text.Json;

namespace SkynetZero.Web;

internal sealed record OpeningChoice(string State, BookAction Action, string Id, string Name, string Variant, double Weight);
internal sealed record OpeningSource(string File, string Author, string Sha256, string SeedSha256);
internal sealed record OpeningData(int Version, OpeningSource[] Sources, OpeningChoice[] Choices);

// The native KnowledgeBooks opening shortcut, using an offline index of its
// supplied lines. The browser needs neither the trainer nor mutable book learning.
internal sealed class OpeningRepertoire
{
    private readonly Dictionary<string,OpeningChoice[]> positions;
    internal static readonly Lazy<OpeningRepertoire> Bundled=new(()=>{
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SkynetZero.Openings.json")
            ?? throw new InvalidDataException("The opening repertoire is missing.");
        var data=JsonSerializer.Deserialize<OpeningData>(stream)??throw new InvalidDataException("Empty opening repertoire.");
        return new(data);
    });
    internal OpeningRepertoire(OpeningData data){
        if(data.Version!=1||data.Choices.Any(c=>!double.IsFinite(c.Weight)||c.Weight<=0))
            throw new InvalidDataException("Invalid opening repertoire.");
        positions=data.Choices.GroupBy(c=>c.State,StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
    }
    internal OpeningChoice? TryChoose(GameState state,Random? random=null){
        if(state.kingWasTaken||state.TurnMoves.Count!=0||state.Multiverse.Count!=1)return null;
        // This is the native durable key: all historical boards, rights, color,
        // en passant and timeline coordinates. A current-board FEN is not enough.
        if(!positions.TryGetValue(SemanticStateHash.Compute(state),out var choices)||
            MoveGenerator.HasCurrentThreats(state,state.Color))return null;
        var legal=new List<OpeningChoice>();
        foreach(var choice in choices){
            var move=choice.Action.Move();
            if(move.Origin.Color!=state.Color||move.Origin.T!=move.Dest.T||move.Origin.L!=move.Dest.L)continue;
            var next=state.Clone();
            if(!next.MakeMove(move,true)||!RoyalLegality.IsLegalAfterMove(state,move,next)||!RoyalLegality.CanSubmit(next))continue;
            if(!legal.Any(c=>c.Action==choice.Action))legal.Add(choice);
        }
        if(legal.Count==0)return null;
        if(random is null)return legal.OrderByDescending(c=>c.Weight).ThenBy(c=>c.Id,StringComparer.Ordinal).First();
        double draw=random.NextDouble()*legal.Sum(c=>c.Weight);
        foreach(var choice in legal){draw-=choice.Weight;if(draw<=0)return choice;}
        return legal[^1];
    }
}
