using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileIO5D;
using SkynetZero;
using SkynetZero.Web;

internal static class Study
{
    private sealed record Sample(string Game,int Turn,string Split,float[] Features,float TeacherLogit);
    private sealed record Fixture(string Name,string Pgn,int Turn=-1);
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static readonly PortableModel Teacher=PortableModel.LoadBundled();
    private static GameStateManager Parse(string pgn){
        // Training exports supply explicit boards but omit the optional Board tag.
        if(!Regex.IsMatch(pgn,@"\[Board\s+""",RegexOptions.IgnoreCase))pgn="[Board \"custom\"]\n"+pgn;
        return FENParser.ShadSTDGSMFromString(pgn)??throw new InvalidDataException("No game");
    }
    public static void Main(string[] args)
    {
        using(var stream=typeof(PortableModel).Assembly.GetManifestResourceStream("SkynetZero.LearnedTables.bin")!)SacrificeTable.ReadLearned(stream,true);
        string command=args.ElementAtOrDefault(0)??"help",output=Path.GetFullPath(args.ElementAtOrDefault(1)??"artifacts/small-model");
        Directory.CreateDirectory(output);
        switch(command){
            case "prepare":Prepare(output,args[2]);break;
            case "train":Train(output);break;
            case "benchmark":Benchmark(output,args[2],int.Parse(args.ElementAtOrDefault(3)??"5000"),args.ElementAtOrDefault(4),int.Parse(args.ElementAtOrDefault(5)??"2"));break;
            default:Console.WriteLine("prepare OUT SAVED_GAMES | train OUT | benchmark OUT GAME116000 [MILLISECONDS] [POSITION,...] [REPEATS]");break;
        }
    }
    private static void Prepare(string output,string directory)
    {
        var samples=new List<Sample>();var seen=new HashSet<string>();var failures=new List<object>();var sources=new List<object>();
        var timer=Stopwatch.StartNew();
        foreach(var file in Directory.GetFiles(directory,"PGN_*.txt").OrderBy(f=>int.Parse(Path.GetFileNameWithoutExtension(f).Split('_')[1])))
        {
            int number=int.Parse(Path.GetFileNameWithoutExtension(file).Split('_')[1]);
            string split=number==116000||number/1000%10 is 0 or 1?"test":number/1000%10 is 8 or 9?"validation":"train";
            try {
                string text=File.ReadAllText(file);
                var game=Parse(text);
                int turns=AnnotationTree.GetNodesLinear(game.ATR.Root).Count;
                sources.Add(new{Game=Path.GetFileName(file),Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),Split=split,Turns=turns});
                // Sample across whole games, including positions with branches.
                int step=Math.Max(1,turns/28);
                for(int turn=0;turn<turns;turn+=step){
                    game.NavigateToTurn(turn);var state=game.Clone();
                    var features=new float[107];
                    using(SacrificeTable.UseRootTravel(SacrificeTable.CaptureRootTravel(state)))ExtendedEvaluationTermsEncoding.Fill(state,state.Color,features);
                    if(features.Any(f=>!float.IsFinite(f)))throw new InvalidDataException("Nonfinite feature");
                    // Exact feature vectors appear only once across all splits.
                    string key=Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(features.AsSpan())));
                    if(!seen.Add(key))continue;
                    float score=Teacher.ForwardLogit(features);
                    if(!float.IsFinite(score))throw new InvalidDataException("Nonfinite teacher");
                    samples.Add(new(Path.GetFileName(file),turn,split,features,score));
                }
            }catch(Exception e){failures.Add(new{Game=Path.GetFileName(file),Error=e.Message});}
            if(sources.Count>0&&sources.Count%10==0)Console.WriteLine($"DATA games={sources.Count} positions={samples.Count} seconds={timer.Elapsed.TotalSeconds:F1}");
        }
        File.WriteAllText(Path.Combine(output,"dataset.json"),JsonSerializer.Serialize(samples));
        File.WriteAllText(Path.Combine(output,"sources.json"),JsonSerializer.Serialize(new{Sources=sources,Failures=failures,Counts=samples.GroupBy(s=>s.Split).ToDictionary(g=>g.Key,g=>g.Count()),Seconds=timer.Elapsed.TotalSeconds},Json));
        Console.WriteLine($"PREPARED {samples.Count} positions, {failures.Count} rejected games in {timer.Elapsed.TotalSeconds:F1}s");
        if(samples.Count==0||samples.Select(s=>s.Split).Distinct().Count()!=3)throw new InvalidDataException("A nonempty training, validation and test split is required.");
    }
    private static void Train(string output)
    {
        var samples=JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Path.Combine(output,"dataset.json")))!;
        var train=samples.Where(s=>s.Split=="train").ToArray();var validation=samples.Where(s=>s.Split=="validation").ToArray();
        using var stream=typeof(PortableModel).Assembly.GetManifestResourceStream("SkynetZero.Model.json")!;
        var original=JsonSerializer.Deserialize<Dictionary<string,PortableModel.Tensor>>(stream)!;
        var linear=original["termOutput.weight"].data[64..];float bias=original["termOutput.bias"].data[0];
        var mean=new float[107];var scale=new float[107];
        float Enc(Sample s,int i){float x=s.Features[i]*(s.Features[MultiverseEvaluation.TempoIndex]>0?-1:1);return MathF.CopySign(MathF.Log(1+MathF.Abs(x)),x);}
        for(int i=0;i<107;i++){mean[i]=(float)train.Average(s=>Enc(s,i));scale[i]=1/Math.Max(.1f,(float)Math.Sqrt(train.Average(s=>Math.Pow(Enc(s,i)-mean[i],2))));}
        int seed=20261024;var rng=new Random(seed);var p=new float[CompactModel.Count];
        double Normal()=>Math.Sqrt(-2*Math.Log(Math.Max(1e-10,rng.NextDouble())))*Math.Cos(2*Math.PI*rng.NextDouble());
        for(int i=0;i<CompactModel.B1;i++)p[i]=(float)(Normal()*Math.Sqrt(2d/107));
        for(int i=CompactModel.W2;i<CompactModel.B2;i++)p[i]=(float)(Normal()*Math.Sqrt(2d/64));
        var model=new CompactModel(new(p,linear,bias,mean,scale,seed));
        (float[] X,float Y) Encode(Sample s){var x=new float[107];model.Encode(s.Features,x);float orientation=s.Features[MultiverseEvaluation.TempoIndex]>0?-1:1;float baseScore=bias;for(int i=0;i<107;i++)baseScore+=linear[i]*s.Features[i]*orientation;return(x,s.TeacherLogit*orientation-baseScore);}
        var rows=train.Select(Encode).ToArray();var dev=validation.Select(Encode).ToArray();
        var a=new float[64];var b=new float[32];var da=new float[64];var db=new float[32];
        var grad=new float[p.Length];var m=new float[p.Length];var v=new float[p.Length];var order=Enumerable.Range(0,rows.Length).ToArray();
        double Loss((float[] X,float Y)[] data)=>data.Average(row=>Math.Pow(model.Residual(row.X,a,b)-row.Y,2));
        float[] best=(float[])p.Clone();double bestLoss=Loss(dev);int bestEpoch=0,update=0;var log=new List<object>();var timer=Stopwatch.StartNew();
        for(int epoch=1;epoch<=160;epoch++){
            rng.Shuffle(order);
            for(int start=0;start<order.Length;start+=64){
                Array.Clear(grad);int count=Math.Min(64,order.Length-start);
                for(int n=start;n<start+count;n++){
                    var row=rows[order[n]];float error=2*(model.Residual(row.X,a,b)-row.Y)/count;
                    grad[CompactModel.B3]+=error;
                    for(int j=0;j<32;j++){grad[CompactModel.W3+j]+=error*b[j];db[j]=b[j]>0?error*p[CompactModel.W3+j]:0;}
                    Array.Clear(da);
                    for(int j=0;j<32;j++){
                        grad[CompactModel.B2+j]+=db[j];int offset=CompactModel.W2+j*64;
                        for(int i=0;i<64;i++){grad[offset+i]+=db[j]*a[i];da[i]+=db[j]*p[offset+i];}
                    }
                    for(int j=0;j<64;j++)if(a[j]>0){grad[CompactModel.B1+j]+=da[j];for(int i=0;i<107;i++)grad[j*107+i]+=da[j]*row.X[i];}
                }
                update++;float lr=.001f*(epoch>100?.3f:1),mBias=1-MathF.Pow(.9f,update),vBias=1-MathF.Pow(.999f,update);
                double norm=Math.Sqrt(grad.Sum(g=>(double)g*g));float clip=(float)Math.Min(1,5/Math.Max(norm,1e-12));
                for(int i=0;i<p.Length;i++){float g=grad[i]*clip+1e-5f*p[i];m[i]=.9f*m[i]+.1f*g;v[i]=.999f*v[i]+.001f*g*g;p[i]-=lr*(m[i]/mBias)/(MathF.Sqrt(v[i]/vBias)+1e-8f);}
            }
            double loss=Loss(dev);
            if(!double.IsFinite(loss))throw new Exception("Nonfinite training loss");
            if(loss<bestLoss){bestLoss=loss;bestEpoch=epoch;Array.Copy(p,best,p.Length);}
            if(epoch%10==0){double training=Loss(rows);log.Add(new{Epoch=epoch,TrainingMse=training,ValidationMse=loss});Console.WriteLine($"TRAIN epoch={epoch} mse={training:F6} validation={loss:F6} seconds={timer.Elapsed.TotalSeconds:F1}");}
        }
        Array.Copy(best,p,p.Length);model.Save(Path.Combine(output,"compact-mlp.json"));
        var metrics=samples.GroupBy(s=>s.Split).ToDictionary(g=>g.Key,g=>{
            var errors=g.Select(s=>Math.Abs(Math.Atanh(Math.Clamp(model.Forward(s.Features),-.999999,.999999))*8700-Math.Clamp(s.TeacherLogit*8700,-20000,20000))/484.2*100).Order().ToArray();
            return new{Positions=errors.Length,MeanAbsoluteCp=errors.Average(),MedianAbsoluteCp=errors[errors.Length/2],P95AbsoluteCp=errors[(int)(errors.Length*.95)]};
        });
        File.WriteAllText(Path.Combine(output,"training.json"),JsonSerializer.Serialize(new{Architecture="107-64-32-1 + frozen linear skip",Parameters=p.Length+linear.Length+1,Seed=seed,BestEpoch=bestEpoch,BestValidationMse=bestLoss,Seconds=timer.Elapsed.TotalSeconds,Metrics=metrics,Log=log},Json));
        Console.WriteLine(JsonSerializer.Serialize(new{BestEpoch=bestEpoch,Metrics=metrics}));
    }
    private static void Benchmark(string output,string game116,int milliseconds,string? selected,int repeats)
    {
        var compact=CompactModel.Load(Path.Combine(output,"compact-mlp.json"));
        string full=File.ReadAllText(game116);
        Fixture[] fixtures=[
            new("opening","[Board \"standard-t0\"]"),
            new("branch","[Board \"standard-t0\"]\n1. (0T1)e4 / (0T1)e5\n2. (0T2)Nb1>>(0T1)b3 /"),
            new("queen-capture","[Board \"custom\"]\n[Size \"8x8\"]\n[6k1/8/8/8/3q4/8/3R4/6K1:0:1:w]"),
            new("mate-one","[Board \"custom\"]\n[Size \"8x8\"]\n[2ppp3/2pkp3/N1ppp3/8/8/8/8/7K:0:1:w]"),
            new("game116k-white14",full,26),new("game116k-white16",full,30),
            new("game116k-white24",full,46),new("game116k-white26",full,50)
        ];
        if(selected is not null)fixtures=fixtures.Where(f=>selected.Split(',').Contains(f.Name)).ToArray();
        if(fixtures.Length==0||milliseconds<1||repeats is <1 or >10)throw new ArgumentException("Select existing positions, a positive time budget and 1–10 repeats.");
        var results=new List<object>();var micro=new List<object>();
        foreach(var fixture in fixtures){
            var parsed=Parse(fixture.Pgn);
            if(fixture.Turn>=0)parsed.NavigateToTurn(fixture.Turn);
            else if(parsed.TurnMoves.Count>0&&RoyalLegality.CanSubmit(parsed))parsed.SubmitMoves();
            var root=parsed.Clone();DrawRules.Enable(root);bool white=root.Color;
            using var travel=SacrificeTable.UseRootTravel(SacrificeTable.CaptureRootTravel(root));
            var features=new float[107];ExtendedEvaluationTermsEncoding.Fill(root,white,features);
            foreach(string name in new[]{"transformer","compact"}){
                Func<float> forward=name=="compact"?()=>compact.Forward(features):()=>Teacher.Forward(features);
                for(int i=0;i<10;i++)forward();var timer=Stopwatch.StartNew();int count=0;float sink=0;
                while(timer.ElapsedMilliseconds<250){sink+=forward();count++;}
                micro.Add(new{Position=fixture.Name,Model=name,EvaluationsPerSecond=count/timer.Elapsed.TotalSeconds,Sink=sink});
            }
            for(int repeat=0;repeat<repeats;repeat++)foreach(string name in repeat%2==0?new[]{"transformer","compact"}:new[]{"compact","transformer"}){
                double evaluationSeconds=0;int calls=0;
                double Eval(AlphaBetaLeaf leaf){long start=Stopwatch.GetTimestamp();float value=name=="compact"?compact.Evaluate(leaf.State,white):Teacher.Evaluate(leaf.State,white);evaluationSeconds+=Stopwatch.GetElapsedTime(start).TotalSeconds;calls++;return value;}
                var settings=new AlphaBetaSettings{CompleteTurnDepth=64,MaxNodes=2_000_000,Threads=1,MaxActionsPerTurn=32,TranspositionEntries=65536,QuiescenceTurns=1};
                var result=CompleteTurnAlphaBetaSearch.Choose(root,settings,milliseconds,evaluate:Eval,evaluatorUnits:true);
                var replay=root.Clone();var moves=new List<string>();
                foreach(var move in result.CompleteTurnMoves){if(move is null){if(!RoyalLegality.CanSubmit(replay)||!replay.SubmitMoves())throw new Exception("Invalid search submission");}else{moves.Add(FiveDProtocol.Format(move,replay.EvenStart));var before=replay.Clone();if(!replay.MakeMove(move.Clone(),true)||!RoyalLegality.IsLegalAfterMove(before,move,replay))throw new Exception("Illegal searched move");}}
                var row=new{Position=fixture.Name,Model=name,Repeat=repeat,Milliseconds=milliseconds,DepthLimit=64,CompletedDepth=result.CompletedDepth,result.Nodes,result.Leaves,result.GenerationWork,result.Seconds,StopReason=result.StopReason.ToString(),EvaluationCalls=calls,EvaluationSeconds=evaluationSeconds,Score=result.SourceScore,Mate=result.ProvenMateDistance,Move=string.Join(' ',moves)};
                results.Add(row);Console.WriteLine(JsonSerializer.Serialize(row));
                File.WriteAllText(Path.Combine(output,$"search-{milliseconds}ms.json"),JsonSerializer.Serialize(new{Microbenchmarks=micro,Searches=results},Json));
            }
        }
    }
}
