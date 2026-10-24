using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
namespace SkynetZero.Web;
public static partial class EngineBridge
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static PortableModel? model;
    private static GameSession? session;
#if !ENGINE_TESTS
    [JSExport]
#endif
    public static string Dispatch(string json)
    {
        try {
            if(model==null) {
                using var table=typeof(PortableModel).Assembly.GetManifestResourceStream("SkynetZero.LearnedTables.bin");
                if(table!=null)SacrificeTable.ReadLearned(table,replace:true);
                model=PortableModel.LoadBundled();session=new(model);
            }
            var r=JsonSerializer.Deserialize<EngineRequest>(json,Json)??throw new InvalidDataException("Invalid request.");
            Evaluation? evaluation=null;
            switch(r.Command) {
                case "new":session!.New(r.Side,r.Variant);break;
                case "import":var imported=new GameSession(model);imported.Import(r.Pgn??"");session=imported;break;
                case "move":session!.Move(r.Move??"");break;
                case "undo":session!.Undo();break;
                case "submit":session!.Submit();break;
                case "resign":session!.Resign(r.Side);break;
                case "engine":evaluation=session!.Play(r.Strength);break;
                case "analyze":return JsonSerializer.Serialize(new EngineReply(Evaluation:session!.Analyze(r.Index,r.Strength)),Json);
                case "view":return JsonSerializer.Serialize(new EngineReply(Position:session!.Snapshot(r.Index)),Json);
                case "state":break;
                default:throw new InvalidDataException("Unknown engine command.");
            }
            return JsonSerializer.Serialize(new EngineReply(session!.Snapshot(),evaluation),Json);
        }catch(Exception error){return JsonSerializer.Serialize(new EngineReply(Error:error.Message),Json);}
    }
}
