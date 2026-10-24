using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SkynetZero.Web;
internal static class UiChecks
{
    public static async Task Run(PositionView position,Evaluation evaluation)
    {
        var collection=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime,NoJs>();
        collection.AddSingleton<IComponentActivator>(new FixtureActivator(position,evaluation));
        using var services=collection.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=> {
            var result=await renderer.RenderComponentAsync<App>();string html=result.ToHtmlString();
            foreach(string expected in new[]{"SKYNET","Analysis board","White ACPL","Export 5DPGN","role=\"gridcell\"","M1"})
                if(!html.Contains(expected))throw new Exception("Rendered Blazor UI is missing "+expected);
            if(html.Contains("Microsoft.AspNetCore.Components.RenderFragment"))throw new Exception("Broken Razor component binding");
            if(!html.Contains("type=\"range\" min=\"1\" max=\"100\" step=\"1\""))throw new Exception("Missing granular strength slider");
            if(!html.Contains(position.White?"alt=\"White to move\"":"alt=\"Black to move\""))throw new Exception("Wrong turn indicator");
            if(!html.Contains("class=\"eval-area white\"")||!html.Contains("class=\"eval-area black\""))throw new Exception("Evaluation chart must shade each side's advantage");
        });
        Console.WriteLine("PASS Blazor render: multiverse boards, analysis, mate score and export controls");
    }
    public static async Task MultiverseHistory(PositionView position,bool white=true) {
        using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=> {
            // Empty LastMoves simulates a subsequent spatial move: travel arrows
            // and ancestry must still come from the game's visible history.
            var rendered=await renderer.RenderComponentAsync<Multiverse>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Position",position},{"LastMoves",Array.Empty<MoveView>()}}));
            string html=rendered.ToHtmlString();
            var side=white?"white":"black";
            if(!html.Contains($"class=\"played-travel {side}\"")||!html.Contains($"marker-end=\"url(#travel-{side})\"")||!html.Contains("Branched from L 0"))throw new Exception("Lost persistent travel, side color or parent connector");
            if(html.Contains("T@board.Time")||!html.Contains(" T1</span>"))throw new Exception("Turn labels must render their numeric time");
        });
        Console.WriteLine("PASS persistent travel arrows, branch ancestry and numbered board labels");
    }
    public static async Task CheckPreview(PositionView position) {
        var activator=new FixtureActivator(position,null,false);var js=new GameJs();
        using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(js).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=> {
            var rendered=await renderer.RenderComponentAsync<App>();
            if(!rendered.ToHtmlString().Contains("class=\"check-indicator\""))throw new Exception("Missing red check indicator");
            var app=activator.App!;
            typeof(App).GetMethod("ToggleChecks",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,null);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,null);
            string html=rendered.ToHtmlString();
            if(!html.Contains("class=\"check-arrow\"")||!html.Contains("Return to position")||!html.Contains("aria-pressed=\"true\""))throw new Exception("Check toggle must render advanced boards and red arrows");
            await app.OnShortcut("f",false);
            if(js.Commands.Count!=0)throw new Exception("Check preview must never submit a move");
            await app.OnShortcut("Escape",false);
            if(rendered.ToHtmlString().Contains("class=\"check-arrow\""))throw new Exception("Escape must return to the real position");
        });
        Console.WriteLine("PASS checkmate indicator, advanced preview, red checks and return without mutation");
    }
    public static async Task AnalysisCompletion(PositionView position) {
        var activator=new FixtureActivator(position,null);
        using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime,AnalysisJs>().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=> {
            var rendered=await renderer.RenderComponentAsync<App>();var app=activator.App!;
            await (Task)typeof(App).GetMethod("AnalyzeGame",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,null)!;
            string html=rendered.ToHtmlString();
            if(html.Contains(">Analyze whole game</button>")||!html.Contains("Game analysis complete"))throw new Exception("Completed analysis must hide its button");
            if(!html.Contains("clip-path=\"url(#white-advantage)\"")||!html.Contains("clip-path=\"url(#black-advantage)\""))throw new Exception("Chart shading must split at zero");
            typeof(App).GetMethod("SetAnalysisLevel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,[67]);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,null);
            if(!rendered.ToHtmlString().Contains(">Analyze whole game</button>"))throw new Exception("New analysis strength must allow a new analysis");
            typeof(App).GetMethod("SetAnalysisLevel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,[1]);
            int selected=(int)typeof(App).GetField("_analysisLevel",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)!;
            if(selected<position.EngineStrength)throw new Exception("Review must not use a weaker budget than engine play");
        });
        Console.WriteLine("PASS completed-analysis control and advantage shading");
    }
    private sealed class AnalysisJs:IJSRuntime {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args) {
            if(identifier!="skynet.request")return ValueTask.FromResult(default(TValue)!);
            var request=JsonSerializer.Deserialize<EngineRequest>((string)args![0]!,EngineBridge.Json)!;
            var evaluation=new Evaluation(request.Index,request.Index%2==0?100:-100,null,null,2,20,.1,false,"","CompletedDepth");
            return ValueTask.FromResult((TValue)(object)JsonSerializer.Serialize(new EngineReply(Evaluation:evaluation),EngineBridge.Json));
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken token,object?[]? args)=>InvokeAsync<TValue>(identifier,args);
    }
    public static async Task Keyboard() {
        EngineReply Send(EngineRequest request)=>JsonSerializer.Deserialize<EngineReply>(EngineBridge.Dispatch(JsonSerializer.Serialize(request,EngineBridge.Json)),EngineBridge.Json)!;
        Send(new("new"));var pending=Send(new("move",Move:"(0T1)e2e4")).Position!;
        var js=new GameJs();var activator=new FixtureActivator(pending,null,false);
        using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(js).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=> {
            await renderer.RenderComponentAsync<App>();var app=activator.App!;
            void Set(string name,object value)=>typeof(App).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(app,value);
            await app.OnShortcut("f",true);
            Set("_review",true);await app.OnShortcut("f",false);Set("_review",false);
            Set("_help",true);await app.OnShortcut("d",false);await app.OnShortcut("f",false);Set("_help",false);
            if(js.Commands.Count!=0||js.Pans.Count!=0)throw new Exception("Hotkeys escaped dialog/review/repeat guards");
            Set("_busy",true);await app.OnShortcut("D",false);Set("_busy",false);
            if(js.Pans is not [(120,0)])throw new Exception("WASD must pan while thinking");
            await app.OnShortcut("F",false);
            if(!js.Commands.SequenceEqual(new[]{"submit","engine"})||Send(new("state")).Position!.TurnCount!=2)throw new Exception("F must submit and play exactly one engine reply");
        });
        Console.WriteLine("PASS keyboard submission, review/dialog guards and panning during search");
    }
    private sealed class FixtureActivator(PositionView position,Evaluation? evaluation,bool review=true):IComponentActivator {
        public App? App;
        public IComponent CreateInstance(Type type) {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is App) {
                App=(App)component;
                void Set(string field,object value)=>typeof(App).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(component,value);
                Set("_position",position);Set("_review",review);Set("_viewIndex",evaluation?.Index??position.Viewing);Set("_strength",1);
                var values=(Dictionary<int,Evaluation>)typeof(App).GetField("_evaluations",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(component)!;if(evaluation!=null)values[evaluation.Index]=evaluation;
            }
            return component;
        }
    }
    private sealed class GameJs:IJSRuntime {
        public List<string> Commands=[];
        public List<(int,int)> Pans=[];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args) {
            if(identifier=="skynet.pan")Pans.Add(((int)args![0]!, (int)args[1]!));
            if(identifier=="skynet.request") {
                string request=(string)args![0]!;Commands.Add(JsonSerializer.Deserialize<EngineRequest>(request,EngineBridge.Json)!.Command);
                return ValueTask.FromResult((TValue)(object)EngineBridge.Dispatch(request));
            }
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken token,object?[]? args)=>InvokeAsync<TValue>(identifier,args);
    }
    private sealed class NoJs:IJSRuntime {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args)=>ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken token,object?[]? args)=>ValueTask.FromResult(default(TValue)!);
    }
}
