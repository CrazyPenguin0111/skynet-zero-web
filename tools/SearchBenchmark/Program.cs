using SkynetZero.Benchmarks;
using System.Text.Json;

var requests=JsonSerializer.Deserialize<Benchmark.Request[]>(File.ReadAllText(args.ElementAtOrDefault(0)??"tools/search-positions.json"),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
Console.WriteLine(Benchmark.Run(JsonSerializer.Serialize(requests[0] with {Name="warmup",Depth=1,Nodes=64,Milliseconds=0})));
foreach(var request in requests) Console.WriteLine(Benchmark.Run(JsonSerializer.Serialize(request)));
