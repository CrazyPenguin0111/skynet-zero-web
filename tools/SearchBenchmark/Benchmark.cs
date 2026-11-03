using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using FileIO5D;
using SkynetZero;
using SkynetZero.Web;

namespace SkynetZero.Benchmarks;

// Compiled into the browser only for explicit benchmark builds. Both runtimes
// execute these same positions, evaluator and complete-turn search budgets.
public static partial class Benchmark
{
    public sealed record Request(string Name,string Pgn,int Depth=10,int Nodes=2_000_000,int Milliseconds=16_000,int TableEntries=4096);
    private static readonly PortableModel Model=PortableModel.LoadBundled();
    static Benchmark()
    {
        using var table=typeof(PortableModel).Assembly.GetManifestResourceStream("SkynetZero.LearnedTables.bin")!;
        SacrificeTable.ReadLearned(table,true);
    }
#if !ENGINE_TESTS
    [JSExport]
#endif
    public static string Run(string json)
    {
        var request=JsonSerializer.Deserialize<Request>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var state=FENParser.ShadSTDGSMFromString(request.Pgn)??throw new InvalidDataException("Invalid fixture");
        if(state.TurnMoves.Count>0 && (!RoyalLegality.CanSubmit(state)||!state.SubmitMoves()))
            throw new InvalidDataException("Fixture must end at a complete turn");
        state.QueenPromotionOnly=false;
        DrawRules.Enable(state);
        using var travel=SacrificeTable.UseRootTravel(SacrificeTable.CaptureRootTravel(state));
        string before=CompleteTurnStateHash.Compute(state);
        var features=new float[107];int evaluations=0;long encoding=0,inference=0;
        double Evaluate(AlphaBetaLeaf leaf)
        {
            long start=Stopwatch.GetTimestamp();
            ExtendedEvaluationTermsEncoding.Fill(leaf.State,state.Color,features);
            long encoded=Stopwatch.GetTimestamp();
            float value=Model.Forward(features);
            encoding+=encoded-start;inference+=Stopwatch.GetTimestamp()-encoded;evaluations++;
            return value;
        }
        long allocation=GC.GetAllocatedBytesForCurrentThread();
        var timer=Stopwatch.StartNew();
        var result=CompleteTurnAlphaBetaSearch.Choose(state,new(){CompleteTurnDepth=request.Depth,
            MaxNodes=request.Nodes,MaxActionsPerTurn=32,TranspositionEntries=request.TableEntries,QuiescenceTurns=1},
            request.Milliseconds,evaluate:Evaluate,evaluatorUnits:true);
        timer.Stop();
        long allocated=GC.GetAllocatedBytesForCurrentThread()-allocation;
        if(before!=CompleteTurnStateHash.Compute(state))throw new Exception("Search mutated its input");
        var replay=state.Clone();
        foreach(var move in result.CompleteTurnMoves) {
            var previous=replay.Clone();
            if(move is null ? !RoyalLegality.CanSubmit(replay)||!replay.SubmitMoves() : !replay.MakeMove(move.Clone(),true)||!RoyalLegality.IsLegalAfterMove(previous,move,replay))
                throw new Exception("Search returned an illegal complete turn");
        }
        return JsonSerializer.Serialize(new {request.Name,request.Depth,TargetNodes=request.Nodes,request.Milliseconds,request.TableEntries,
            result.CompletedDepth,result.Nodes,result.Leaves,result.TableHits,result.GenerationWork,result.SourceScore,
            StopReason=result.StopReason.ToString(),ElapsedMs=timer.Elapsed.TotalMilliseconds,AllocatedBytes=allocated,
            Evaluations=evaluations,EncodingMs=1000d*encoding/Stopwatch.Frequency,InferenceMs=1000d*inference/Stopwatch.Frequency,
            Turn=result.CompleteTurnMoves.Select(m=>m is null?"submit":FiveDProtocol.Format(m,state.EvenStart)).ToArray()});
    }
}
