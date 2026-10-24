using SkynetZero;
using TorchSharp;
using System.Text.Json;
using System.Security.Cryptography;
using static TorchSharp.torch;
set_num_threads(1); set_num_interop_threads(1);
using var model = TurnModel.CreateEvaluationTerms(extended:true);
model.load(args[0]); model.eval();
var weights = model.state_dict().ToDictionary(p=>p.Key,p=> new { shape=p.Value.shape, data=p.Value.cpu().flatten().data<float>().ToArray() });
Directory.CreateDirectory(args[1]);
File.WriteAllText(Path.Combine(args[1],"weights.json"),JsonSerializer.Serialize(weights));
using var guard = no_grad();
var random=new Random(617); var cases=new List<object>();
for(int i=0;i<16;i++) {
 var input=Enumerable.Range(0,107).Select(_=>(float)(random.NextDouble()*4-2)).ToArray();input[43]=(i%2==0?1:-1)/32f;
 using var t=tensor(input).reshape(1,107,1);using var output=model.forward(t);
 cases.Add(new{input,expected=output.item<float>()});
}
// Real position encodings compare the production evaluator as well as inference.
if(args.Length>2) {
 using var table=File.OpenRead(args[2]);
 typeof(TurnModel).Assembly.GetType("SkynetZero.SacrificeTable")!.GetMethod("ReadLearned",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[table,true]);
}
string[] positions=[
 "[Board \"standard-t0\"]",
 "[Board \"standard-t0\"]\n1. e4 / e5 /",
 "[Board \"standard-t0\"]\n1. e4 / e5\n2. (0T2)Nb1>>(0T1)b3 /",
 "[Board \"custom\"]\n[Size \"8x8\"]\n[2ppp3/2pkp3/N1ppp3/8/8/8/8/7K:0:1:w]",
 "[Board \"standard\"]\n1. e4 / a6\n2. e5 / d5\n3. exd6 /"
];
var fill=typeof(TurnModel).Assembly.GetType("SkynetZero.ExtendedEvaluationTermsEncoding")!.GetMethod("Fill",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
foreach(string pgn in positions) foreach(bool white in new[]{true,false}) {
 var state=FileIO5D.FENParser.ShadSTDGSMFromString(pgn)!;var input=new float[107];fill.Invoke(null,[state,white,input,0]);
 using var t=tensor(input).reshape(1,107,1);using var output=model.forward(t);
 cases.Add(new{input,expected=output.item<float>(),pgn,white});
}
File.WriteAllText(Path.Combine(args[1],"parity-vectors.json"),JsonSerializer.Serialize(cases));
Console.WriteLine($"Exported {weights.Count} tensors; SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0])))}");
