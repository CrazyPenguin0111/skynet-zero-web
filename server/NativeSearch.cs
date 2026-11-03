using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SkynetZero.Web;

namespace SkynetZero.Server;

// Native searches run outside the web process. A hard deadline and disconnect
// cancellation also cover PGN parsing, legal fallback and terminal proofs.
public sealed class NativeSearch(IConfiguration configuration,ILogger<NativeSearch> logger) : IDisposable
{
    public const int MaximumPgnLength=64_000;
    private const int MaximumReplyLength=8_000_000;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim slots=new(Math.Clamp(configuration.GetValue("NativeSearch:Workers",1),1,4));
    public bool Enabled { get; }=configuration.GetValue("NativeSearch:Enabled",true);

    public static void Validate(EngineRequest request)
    {
        if(request.Command is not ("engine" or "analyze")||string.IsNullOrWhiteSpace(request.Pgn)||
            request.Pgn.Length>MaximumPgnLength||request.Index is < -1 or >10000||request.Strength is <1 or >100)
            throw new ApiError(400,"Invalid engine search request.");
    }

    public async Task<string> Run(EngineRequest request,CancellationToken cancellation)
    {
        Validate(request);
        if(!Enabled)throw new ApiError(503,"Server search is disabled.");
        if(!await slots.WaitAsync(0,cancellation))throw new ApiError(429,"The server engine is busy. Use the browser engine.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(Strength.FromLevel(request.Strength).Milliseconds+4000);
        using var process=new Process();
        Task<string>? output=null,errors=null;
        try
        {
            string executable=Environment.ProcessPath??throw new InvalidOperationException("No process executable.");
            process.StartInfo=new(executable){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            if(Path.GetFileNameWithoutExtension(executable)=="dotnet")
                process.StartInfo.ArgumentList.Add(typeof(NativeSearch).Assembly.Location);
            process.StartInfo.ArgumentList.Add("--engine-worker");
            process.StartInfo.Environment["DOTNET_GCHeapHardLimit"]="18000000"; // 384 MiB, hexadecimal.
            process.StartInfo.Environment["DOTNET_gcServer"]="0";
            // Search itself has one thread. Permit the runtime's background
            // GC/JIT thread; advertising one processor halves throughput.
            process.StartInfo.Environment["DOTNET_PROCESSOR_COUNT"]="2";
            if(!process.Start())throw new InvalidOperationException("Could not start the engine.");
            using var stop=deadline.Token.Register(()=>Kill(process));
            try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(System.ComponentModel.Win32Exception){}
            output=ReadBounded(process.StandardOutput,MaximumReplyLength,deadline.Token);
            errors=ReadBounded(process.StandardError,16_000,deadline.Token);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request,Json).AsMemory(),deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            string response=await output;await errors;
            if(process.ExitCode!=0)throw new ApiError(503,"The server engine is temporarily unavailable.");
            using var parsed=JsonDocument.Parse(response);
            if(parsed.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            return response;
        }
        catch(Exception) when(cancellation.IsCancellationRequested)
        {throw new OperationCanceledException(cancellation);}
        catch(Exception) when(deadline.IsCancellationRequested)
        {throw new ApiError(504,"The server search reached its time limit. Try a lower strength.");}
        catch(ApiError){throw;}
        catch(Exception error) when(error is not OperationCanceledException)
        {
            logger.LogWarning("Native engine process failed: {Type}",error.GetType().Name);
            throw new ApiError(503,"The server engine is temporarily unavailable.");
        }
        finally{
            Kill(process);deadline.Cancel();
            if(output!=null)try{await output;}catch{}
            if(errors!=null)try{await errors;}catch{}
            slots.Release();
        }
    }

    private static void Kill(Process process)
    {
        try{if(!process.HasExited)process.Kill(entireProcessTree:true);}
        catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
    }
    private static async Task<string> ReadBounded(TextReader reader,int maximum,CancellationToken token)
    {
        var text=new StringBuilder();var buffer=new char[8192];int count;
        while((count=await reader.ReadAsync(buffer.AsMemory(),token))>0){
            if(text.Length+count>maximum)throw new InvalidDataException("Engine response exceeded its limit.");
            text.Append(buffer,0,count);
        }
        return text.ToString();
    }
    public void Dispose()=>slots.Dispose();

    public static async Task Worker()
    {
        EngineReply reply;
        try
        {
            string input=await ReadBounded(Console.In,MaximumPgnLength*6+2000,default);
            var request=JsonSerializer.Deserialize<EngineRequest>(input,Json)??throw new InvalidDataException();
            Validate(request);
            using(var table=typeof(PortableModel).Assembly.GetManifestResourceStream("SkynetZero.LearnedTables.bin")!)
                SacrificeTable.ReadLearned(table,true);
            var game=new GameSession(PortableModel.LoadBundled());game.Import(request.Pgn!);
            Evaluation evaluation=request.Command=="engine"?game.Play(request.Strength):game.Analyze(request.Index,request.Strength);
            reply=new(request.Command=="engine"?game.Snapshot():null,evaluation with {SearchLocation="server"});
        }
        catch(Exception error) when(error is InvalidDataException or InvalidOperationException or ArgumentException or ApiError)
        {reply=new(Error:"That position could not be searched. Check the game and try again.");}
        string response=JsonSerializer.Serialize(reply,Json);
        await Console.Out.WriteAsync(response.Length<=MaximumReplyLength?response:JsonSerializer.Serialize(new EngineReply(Error:"That game is too large for server search."),Json));
    }
}
