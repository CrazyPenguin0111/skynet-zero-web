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
            if(html.Contains("move-number")||html.Contains(">TURN</span>"))throw new Exception("PGN viewer must not retain the removed turn column");
            if(!html.Contains("class=\"eval-track vertical\"")||!html.Contains("role=\"meter\""))throw new Exception("Advantage meter must run vertically beside the sidebar");
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
            var move=position.Turns.SelectMany(turn=>turn.Moves).Last(m=>m.From.L!=m.To.L||m.From.T!=m.To.T);
            int half=position.Boards.Min(b=>b.Time*2+(b.White?0:1)),layer=position.Boards.Min(b=>b.Layer);
            double x=80+(move.To.T*2+(move.To.White?0:1)-half)*414+13+move.To.X*36+18;
            double y=80+(move.To.L-layer)*414+45+(7-move.To.Y)*36+18;
            var arrow=System.Text.RegularExpressions.Regex.Match(html,$"class=\"played-travel {side}\" d=\"([^\"]+)\"").Groups[1].Value;
            if(!arrow.EndsWith($" {x},{y}"))throw new Exception("Travel must end on the square in the original timeline, not its successor branch");
            if(html.IndexOf("class=\"present-line",StringComparison.Ordinal)>html.IndexOf("class=\"multiverse-scale",StringComparison.Ordinal))throw new Exception("Present band must span the scroll space independently of the scaled canvas");
            var flat=new MultiverseLayout(position.Boards,"normal");
            foreach(var line in position.Boards.GroupBy(b=>b.Layer)){
                var last=line.MaxBy(MultiverseLayout.Half)!;
                var track=System.Text.RegularExpressions.Regex.Match(html,$"class=\"timeline-line\" data-layer=\"{line.Key}\" style=\"left:([\\d.]+)px;top:[\\d.]+px;width:([\\d.]+)px");
                if(!track.Success||Math.Abs(double.Parse(track.Groups[1].Value)+double.Parse(track.Groups[2].Value)-(flat.Left(MultiverseLayout.Half(last))+last.Width*36+26))>.01)
                    throw new Exception("Timeline arrow must end beside its own last board");
            }
            foreach(string mode in new[]{"history","parallel"})
            foreach(bool flipped in new[]{false,true})
            {
                var view=await renderer.RenderComponentAsync<Multiverse>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Position",position},{"ViewMode",mode},{"Flipped",flipped}}));
                string iso=view.ToHtmlString();
                var sections=System.Text.RegularExpressions.Regex.Matches(iso,@"<section[^>]*class=""board .*?</section>",System.Text.RegularExpressions.RegexOptions.Singleline);
                if(sections.Count!=position.Boards.Length)throw new Exception(mode+" must retain both colors' historical boards and arrow destinations");
                if(!iso.Contains("class=\"isometric-floor\"")||!iso.Contains("isometric-band"))throw new Exception(mode+" must project the checkerboard and present band");
                var target=position.Boards.Single(b=>b.Layer==move.To.L&&b.Time==move.To.T&&b.White==move.To.White);
                string targetHtml=sections.Select(m=>m.Value).Single(s=>s.Contains($"timeline {target.Layer}, T{target.Time} {(target.White?"white":"black")}"));
                string style=System.Text.RegularExpressions.Regex.Match(targetHtml,"style=\"([^\"]+)\"").Groups[1].Value;
                double Read(string pattern)=>double.Parse(System.Text.RegularExpressions.Regex.Match(style,pattern).Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
                double left=Read(@"left:([\d.Ee+-]+)px"),top=Read(@"top:([\d.Ee+-]+)px");
                var matrix=System.Text.RegularExpressions.Regex.Match(style,@"matrix\(([^)]+)\)").Groups[1].Value.Split(',').Select(s=>double.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if(Math.Abs(matrix[1])<.1||matrix[2]!=0||matrix[3]!=1)throw new Exception(mode+" must stand boards upright with an isometric file axis");
                if((matrix[1]<0)!=(mode=="history"))throw new Exception("The two views must use distinct isometric angles");
                double sx=13+(flipped?target.Width-1-move.To.X:move.To.X)*36+18;
                double sy=45+(flipped?move.To.Y:target.Height-1-move.To.Y)*36+18;
                string path=System.Text.RegularExpressions.Regex.Match(iso,$"class=\"played-travel {side}\" d=\"([^\"]+)\"").Groups[1].Value;
                var end=path.Split(' ').Last().Split(',').Select(s=>double.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if(Math.Abs(end[0]-(left+matrix[0]*sx))>.001||Math.Abs(end[1]-(top+matrix[1]*sx+sy))>.001)throw new Exception(mode+" travel endpoint does not match the projected original square");
                var geometry=new MultiverseLayout(position.Boards,mode,flipped);
                foreach(var board in position.Boards)
                foreach(double bx in new[]{0d,board.Width*36+26d})
                foreach(double by in new[]{-24d,board.Height*36+86d})
                {
                    var corner=geometry.BoardPoint(board,bx,by);
                    if(corner.X<0||corner.Y<0||corner.X>geometry.Width||corner.Y>geometry.Height)throw new Exception(mode+" camera bounds clip a board");
                }
            }
        });
        Console.WriteLine("PASS persistent travel, ancestry, isometric views, flipped arrow alignment and camera bounds");
    }
    public static async Task CheckPreview(PositionView position) {
        var activator=new FixtureActivator(position,null,false);var js=new GameJs();
        using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(js).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=> {
            var rendered=await renderer.RenderComponentAsync<App>();
            string initial=rendered.ToHtmlString();
            var marked=System.Text.RegularExpressions.Regex.Matches(initial,@"<section[^>]*class=""board .*?</section>",System.Text.RegularExpressions.RegexOptions.Singleline)
                .Select(match=>match.Value).Where(section=>section.Contains("class=\"check-indicator board-check\"")).ToArray();
            if(marked.Length!=1)throw new Exception("The check indicator must belong to the checking knight's board");
            var move=position.Turns.Last().Moves.Last();int resultingTime=move.From.T+(move.From.White?0:1);
            if(!marked[0].Contains($"turn {resultingTime}, {(move.From.White?"Black":"White")}"))throw new Exception("Check badge is not on the checking knight's board");
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
        await CheckSourceBoards(position);
        Console.WriteLine("PASS checkmate indicator, advanced preview, red checks and return without mutation");
    }
    private static async Task CheckSourceBoards(PositionView original){
        // A discovered/cross-timeline check must not mark the last move's
        // board or the attacked king. Two checks from one board share a badge.
        BoardView Board(int layer,int time,bool white,bool frontier,params (int X,int Y,int Piece)[] pieces){
            var squares=new int[64];foreach(var p in pieces)squares[p.Y*8+p.X]=p.Piece;
            return new(layer,time,white,8,8,squares,frontier,true);
        }
        var king=Board(0,2,true,true,(7,7,7));
        var oldRook=Board(1,3,false,false,(0,0,16));
        var rook=Board(1,4,false,true,(0,0,16),(1,0,15));
        var knight=Board(2,2,true,true,(1,1,14));
        var target=new Square(7,7,2,0,false);
        var fixture=original with{Boards=[king,oldRook,rook,knight],White=true,Present=2,Pending=[],Branches=[],
            CheckPreview=new([king with{White=false},oldRook,rook,knight with{White=false}],
                [new(new(0,0,4,1,false),target),new(new(1,0,4,1,false),target),new(new(1,1,2,2,false),target)])};
        using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            foreach(bool preview in new[]{false,true}){
                var view=await renderer.RenderComponentAsync<Multiverse>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Position",fixture},{"ShowChecks",preview}}));
                var marked=System.Text.RegularExpressions.Regex.Matches(view.ToHtmlString(),@"<section[^>]*class=""board .*?</section>",System.Text.RegularExpressions.RegexOptions.Singleline)
                    .Select(m=>System.Net.WebUtility.HtmlDecode(m.Value)).Where(s=>s.Contains("class=\"check-indicator board-check\"")).ToArray();
                if(marked.Length!=2||!marked.Any(s=>s.Contains("Timeline L +1, turn 4, Black"))||
                    !marked.Any(s=>s.Contains($"Timeline L +2, turn 2, {(preview?"Black":"White")}")))
                    throw new Exception("Only checking-piece boards may have ! markers, before and during preview");
            }
        });
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
    public static async Task AnalysisVariations(){
        EngineReply Send(EngineRequest request)=>JsonSerializer.Deserialize<EngineReply>(EngineBridge.Dispatch(JsonSerializer.Serialize(request,EngineBridge.Json)),EngineBridge.Json)!;
        var original=Send(new("import",Pgn:"[Board \"standard-t0\"]\n1. e4 / e5\n*")).Position!;
        var start=Send(new("view",Index:0)).Position!;
        if(start.LegalMoves.Length!=20)throw new Exception("Historical analysis positions must expose legal moves");
        var failed=Send(new("fork",Index:0,Move:"(0T1)e2e5"));
        if(failed.Error is null||Send(new("state")).Position!.Pgn!=original.Pgn)throw new Exception("Invalid variation must preserve the original game");
        var js=new GameJs();var activator=new FixtureActivator(start,null);
        using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(js).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var rendered=await renderer.RenderComponentAsync<App>();var app=activator.App!;
            Task Invoke(string name,params object[] args)=>(Task)typeof(App).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,args)!;
            int Active()=>(int)typeof(App).GetField("_activeLine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)!;
            var nf3=start.LegalMoves.Single(m=>m.Id=="(0T1)g1f3");
            await Invoke("SelectSquare",nf3.From);await Invoke("SelectSquare",nf3.To);
            if(Active()!=1||Send(new("state")).Position!.PendingCount!=1)throw new Exception("Playing from a rewound position must create a variation");
            await app.OnShortcut("f",false);
            var variation=Send(new("state")).Position!;
            if(variation.TurnCount!=1||variation.Turns.Single().BookName is null||js.Commands.Contains("engine"))throw new Exception("Analysis must submit Nf3 as Book without an automatic engine reply");
            await Invoke("SwitchLine",0);
            if(Send(new("state")).Position!.Pgn!=original.Pgn)throw new Exception("Returning to main line must restore the original game exactly");
            await Invoke("SwitchLine",1);
            if(Send(new("state")).Position!.Pgn!=variation.Pgn)throw new Exception("Switching back must retain the analyzed variation");
            await Invoke("ChartClick",new MouseEventArgs{ClientX=250});
            var appPosition=(PositionView)typeof(App).GetField("_position",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)!;
            if(appPosition.Viewing!=0||!js.Calls.Contains("skynet.chartIndex"))throw new Exception("Clicking the evaluation graph must rewind the game");
            var d4=appPosition.LegalMoves.Single(m=>m.Id=="(0T1)d2d4");
            await Invoke("SelectSquare",d4.From);await Invoke("SelectSquare",d4.To);await app.OnShortcut("Enter",false);
            if(Active()!=2||Send(new("state")).Position!.Turns.Single().Moves.Single().Id!="(0T1)d2d4")throw new Exception("Rewinding a variation must preserve it and create a second branch");
            await Invoke("SwitchLine",1);
            if(Send(new("state")).Position!.Pgn!=variation.Pgn)throw new Exception("Creating another branch must not overwrite the first variation");
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,null);
            string html=rendered.ToHtmlString();
            if(!html.Contains("Variation 2")||!html.Contains("move-badge book")||html.Contains("move-number"))throw new Exception("Variation selector and book badge must render without a turn column");
        });
        Console.WriteLine("PASS clickable graph, rewound analysis moves, multiple preserved variations, Book labels and main-line restoration");
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
            int Cell()=>(int)typeof(App).GetField("_cell",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)!;
            Set("_busy",true);await app.OnWheelZoom(1,300,260);Set("_busy",false);
            if(Cell()!=38)throw new Exception("Wheel zoom must remain available while thinking");
            Set("_help",true);await app.OnWheelZoom(1,300,260);Set("_help",false);
            if(Cell()!=38)throw new Exception("Wheel zoom escaped dialog guard");
            for(int i=0;i<15;i++)await app.OnWheelZoom(4,300,260);
            if(Cell()!=72)throw new Exception("Zoom upper bound");
            for(int i=0;i<15;i++)await app.OnWheelZoom(-4,300,260);
            if(Cell()!=12)throw new Exception("Zoom lower bound");
            Set("_review",true);await app.OnShortcut("z",false);Set("_review",false);
            Set("_busy",true);await app.OnShortcut("z",false);Set("_busy",false);
            await app.OnShortcut("z",true);
            if(js.Commands.Count!=0)throw new Exception("Undo escaped review, busy or repeat guards");
            await app.OnShortcut("z",false);
            if(js.Commands is not ["undo"]||Send(new("state")).Position!.PendingCount!=0)throw new Exception("Z must undo exactly one pending move");
            Send(new("move",Move:"(0T1)e2e4"));Set("_position",Send(new("state")).Position!);js.Commands.Clear();
            await app.OnShortcut("Backspace",false);
            if(js.Commands is not ["undo"])throw new Exception("Backspace must share undo behavior");
            Send(new("move",Move:"(0T1)e2e4"));Set("_position",Send(new("state")).Position!);js.Commands.Clear();
            await app.OnShortcut("t",false);
            if((string)typeof(App).GetField("_boardView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)! != "history")throw new Exception("T must select history view");
            await app.OnShortcut("y",false);await app.OnShortcut("y",false);
            if((string)typeof(App).GetField("_boardView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)! != "normal")throw new Exception("Y must toggle parallel view off again");
            await app.OnShortcut("r",false);
            if(!(bool)typeof(App).GetField("_restartOpen",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)!)throw new Exception("R must open the restart prompt");
            await app.OnShortcut("Escape",false);
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
                Set("_position",position);Set("_livePgn",position.Pgn);Set("_review",review);Set("_panelOpen",review);Set("_viewIndex",evaluation?.Index??position.Viewing);Set("_strength",1);
                var values=(Dictionary<int,Evaluation>)typeof(App).GetField("_evaluations",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(component)!;if(evaluation!=null)values[evaluation.Index]=evaluation;
            }
            return component;
        }
    }
    private sealed class GameJs:IJSRuntime {
        public List<string> Commands=[];
        public List<string> Calls=[];
        public List<(int,int)> Pans=[];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args) {
            Calls.Add(identifier);
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
