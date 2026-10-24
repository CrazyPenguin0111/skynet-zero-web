using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
namespace SkynetZero.Web;
public partial class App : IAsyncDisposable
{
    private DotNetObjectReference<App>? _keyboardReference;
    private PositionView? _position;
    private Evaluation? _lastSearch;
    private Square? _selected;
    private MoveView[] _promotions=[];
    private bool _busy,_review,_flipped,_humanWhite=true,_importOpen,_newOpen,_help,_resignOpen,_analyzing,_showChecks,_analysisComplete;
    private string? _error,_message,_importError;
    private string _activity="Loading engine",_importText="",_newSide="white",_variant="standard-t0",_livePgn="";
    private int _strength=Strength.Default,_analysisLevel=Strength.Default,_cell=36,_viewIndex,_operation,_analysisDone;
    private readonly Dictionary<int,Evaluation> _evaluations=[];
    private readonly Dictionary<int,TurnAnalysis> _analysis=[];
    private bool IsEngineTurn=>_position!=null&&_position.White!=_humanWhite;
    private Evaluation? SelectedEvaluation=>_evaluations.GetValueOrDefault(_viewIndex);
    private MoveView[] LastMoves=>_position is null?[]:_position.PendingCount>0?_position.Pending:_position.Turns.FirstOrDefault(t=>t.Index==_viewIndex)?.Moves??[];
    private bool DialogOpen=>_importOpen||_newOpen||_help||_resignOpen||_promotions.Length>0;
    private bool CanSubmitTurn=>!_busy&&!_review&&!_showChecks&&!IsEngineTurn&&_position is {CanSubmit:true,Result:"*"}&&_viewIndex==_position.TurnCount;
    private string StatusTitle=>_position is null?"Loading":_review?$"Position after turn {_viewIndex}":_viewIndex<_position.TurnCount?"Viewing an earlier turn":_position.Result!="*"?$"Game over · {_position.Result}":_busy?"Thinking":_position.InCheck?$"{(_position.White?"White":"Black")} is in check":IsEngineTurn?"SkynetZero to move":$"You play {(_humanWhite?"White":"Black")}";
    private string StatusDetail=>_position is null?"":_review?"Click a turn to revisit its position.":_viewIndex<_position.TurnCount?"Use the last-position button to return to your game.":_position.Result!="*"?"Analyze the game to explore your turning points.":_position.CanSubmit?"The present has advanced. Press F to submit your turn.":_position.PendingCount>0?"Continue this turn on the required boards, or undo.":IsEngineTurn?"": "Select a piece on an active board to begin.";
    private double EvalPercent=>SelectedEvaluation is {} e?e.Mate!=null?e.Winner==true?100:0:50+45*Math.Tanh((e.Centipawns??0)/400d):50;
    private double ChartX(int i)=>10+280d*i/Math.Max(1,_position?.TurnCount??1);
    private static double ChartY(Evaluation e)=>39-Math.Clamp(e.Mate!=null?(e.Winner==true?1000:-1000):e.Centipawns??0,-1000,1000)*.033;
    private string ChartPath=>string.Join(' ',_evaluations.Values.OrderBy(e=>e.Index).Select((e,i)=>$"{(i==0?"M":"L")}{ChartX(e.Index).ToString("0.0",CultureInfo.InvariantCulture)},{ChartY(e).ToString("0.0",CultureInfo.InvariantCulture)}"));
    private string ChartAreaPath=>_evaluations.Count==0?"":$"{ChartPath} L{ChartX(_evaluations.Keys.Max()).ToString("0.0",CultureInfo.InvariantCulture)},39 L{ChartX(_evaluations.Keys.Min()).ToString("0.0",CultureInfo.InvariantCulture)},39 Z";
    private string AverageLoss(bool white){var values=_analysis.Values.Where(a=>a.White==white&&a.Loss!=null).Select(a=>a.Loss!.Value).ToArray();return values.Length==0?"—":values.Average().ToString("0.0",CultureInfo.InvariantCulture);}
    protected override async Task OnAfterRenderAsync(bool firstRender){if(firstRender){_keyboardReference=DotNetObjectReference.Create(this);await JS.InvokeVoidAsync("skynet.bindKeyboard",_keyboardReference);await Initialize();}}
    private async Task<EngineReply> Call(EngineRequest request) {
        string json=await JS.InvokeAsync<string>("skynet.request",JsonSerializer.Serialize(request,EngineBridge.Json));
        var reply=JsonSerializer.Deserialize<EngineReply>(json,EngineBridge.Json)??throw new InvalidDataException("The engine returned an empty response.");
        if(reply.Error!=null)throw new InvalidDataException(reply.Error);return reply;
    }
    private async Task ApplyReply(EngineReply reply,bool save=true) {
        if(reply.Position is {} p){if(_position?.TurnCount!=p.TurnCount)_analysisComplete=false;_position=p;_viewIndex=p.Viewing;_livePgn=p.Pgn;_selected=null;_showChecks=false;if(AnalysisMinimum>_analysisLevel){_analysisLevel=AnalysisMinimum;_analysisComplete=false;}if(save)await JS.InvokeVoidAsync("skynet.save",p.Pgn);}
    }
    private async Task<EngineReply?> Execute(EngineRequest request,string activity,bool save=true) {
        if(_busy)return null;
        int operation=++_operation;_busy=true;_activity=activity;_error=null;StateHasChanged();
        try{var reply=await Call(request);if(operation!=_operation)return null;await ApplyReply(reply,save);return reply;}
        catch(Exception e){if(operation==_operation)_error=e.Message;return null;}
        finally{if(operation==_operation)_busy=false;StateHasChanged();}
    }
    private async Task Initialize() {
        _error=null;
        string? saved=null;try{saved=await JS.InvokeAsync<string?>("skynet.restore");}catch{}
        var reply=await Execute(new("state"),"Loading engine",save:false);
        if(reply==null)return;
        if(!string.IsNullOrWhiteSpace(saved)) {
            var restored=await Execute(new("import",Pgn:saved),"Restoring game");
            if(restored!=null){_review=true;_humanWhite=restored.Position!.WhiteName!="SkynetZero";await Navigate(_viewIndex);_message="Your last game was restored. Review it here, or return to Play.";}
        }
        StateHasChanged();
    }
    private async Task NewGame() {
        if(_busy)return;_newOpen=false;_review=false;_humanWhite=_newSide=="random"?Random.Shared.Next(2)==0:_newSide=="white";_flipped=!_humanWhite;
        _evaluations.Clear();_analysis.Clear();_message=null;_lastSearch=null;_analysisComplete=false;
        var reply=await Execute(new("new",Side:_humanWhite?"white":"black",Variant:_variant),"Starting game");
        if(reply!=null&&!_humanWhite)await EngineMove();
    }
    private async Task SelectSquare(Square square) {
        if(_busy||_review||_showChecks||_position is null||_position.Result!="*"||IsEngineTurn||_viewIndex!=_position.TurnCount)return;
        if(_selected!=null){var choices=_position.LegalMoves.Where(m=>m.From==_selected&&m.To==square).ToArray();if(choices.Length>1){_promotions=choices;return;}if(choices.Length==1){await MakeMove(choices[0]);return;}}
        _selected=_selected==square?null:_position.LegalMoves.Any(m=>m.From==square)?square:null;
    }
    private async Task MakeMove(MoveView move){_promotions=[];await Execute(new("move",Move:move.Id),"Checking move");}
    private async Task Undo()=>await Execute(new("undo"),"Undoing move");
    private async Task Submit(){if(!CanSubmitTurn)return;var reply=await Execute(new("submit"),"Submitting turn");if(reply!=null&&_position?.Result=="*"&&IsEngineTurn)await EngineMove();}
    private async Task EngineMove(){if(_review||_showChecks)return;var reply=await Execute(new("engine",Strength:_strength),"Thinking");if(reply?.Evaluation is {} e)_lastSearch=e;await JS.InvokeVoidAsync("skynet.scrollToEnd");}
    private async Task Resign(){_resignOpen=false;await Execute(new("resign",Side:_humanWhite?"white":"black"),"Ending game");_review=true;}
    private async Task Navigate(int index) {
        if(_position is null||_busy)return;index=Math.Clamp(index,0,_position.TurnCount);
        // Analysis scores describe submitted turn boundaries. Keep pending moves
        // in the live game and show them again when the player returns to Play.
        await Execute(new("view",Index:_review?index:index==_position.TurnCount?-1:index),"Loading position",save:false);
    }
    private Task GoLive()=>Navigate(_position?.TurnCount??0);
    private async Task PlayMode(){if(_busy)return;_review=false;await GoLive();}
    private async Task ReviewMode(){if(!_busy){_review=true;_selected=null;_message=null;await Navigate(_viewIndex);}}
    private Task FocusPresent()=>JS.InvokeVoidAsync("skynet.focusPresent").AsTask();
    private void ToggleChecks(){if(_position?.CheckPreview is null)return;_showChecks=!_showChecks;_selected=null;}
    private async Task Export(){if(_position!=null)await JS.InvokeVoidAsync("skynet.download",$"skynetzero-{DateTime.Now:yyyy-MM-dd-HHmm}.5dpgn",_livePgn);}
    private async Task ReadPgnFile(InputFileChangeEventArgs e){try{using var reader=new StreamReader(e.File.OpenReadStream(1_000_000));_importText=await reader.ReadToEndAsync();_importError=null;}catch(Exception ex){_importError=ex.Message;}}
    private void LoadExample(){_importText="[Event \"A fork in time\"]\n[White \"Explorer\"]\n[Black \"Explorer\"]\n[Board \"standard-t0\"]\n\n1. (0T1)Pe2e4 / (0T1)Pe7e5\n2. (0T2)Nb1>>(0T1)b3 /\n*";_importError=null;}
    private async Task Import() {
        if(_busy)return;_importError=null;
        var reply=await Execute(new("import",Pgn:_importText),"Reading 5DPGN");
        if(reply==null){_importError=_error;_error=null;return;}
        _analysisComplete=false;_lastSearch=null;
        _importOpen=false;_review=true;_evaluations.Clear();_analysis.Clear();_humanWhite=true;_message=$"Imported {_position!.TurnCount} complete turns. Choose Analyze whole game to calculate centipawn loss.";
        await Navigate(_viewIndex);
        await FocusPresent();
    }
    private void RecordEvaluation(Evaluation e) {
        _evaluations[e.Index]=e;
        foreach(int index in new[]{e.Index,e.Index+1})if(index>0&&_evaluations.TryGetValue(index-1,out var before)&&_evaluations.TryGetValue(index,out var after)) {
            var turn=_position?.Turns.FirstOrDefault(t=>t.Index==index);if(turn!=null)_analysis[index]=TurnAnalysis.Compare(index,turn.White,before,after);
        }
    }
    // Older exports did not record the play budget. Use maximum analysis for
    // those engine games instead of silently reviewing at the casual default.
    private int AnalysisMinimum=>_position is not {} p?1:p.EngineStrength>=0?Math.Max(1,p.EngineStrength):
        p.TurnCount>0&&(p.WhiteName=="SkynetZero"||p.BlackName=="SkynetZero")?Strength.Maximum:1;
    private void SetAnalysisLevel(int level){_analysisLevel=Math.Max(level,AnalysisMinimum);_analysisComplete=false;}
    private async Task AnalyzePosition() {
        if(_busy||_position==null)return;
        var reply=await Execute(new("analyze",Index:_viewIndex,Strength:_analysisLevel),"Analyzing position",save:false);
        if(reply?.Evaluation is {} e)RecordEvaluation(e);
    }
    private async Task AnalyzeGame() {
        if(_busy||_position is null)return;
        int operation=++_operation;_busy=true;_analyzing=true;_analysisDone=0;_error=null;_message=null;_evaluations.Clear();_analysis.Clear();
        int count=_position.TurnCount;
        try {
            for(int i=0;i<=count;i++){
                _activity=$"Analyzing position {i+1}/{count+1}";StateHasChanged();
                var reply=await Call(new("analyze",Index:i,Strength:_analysisLevel));if(operation!=_operation)return;
                if(reply.Evaluation is {} e)RecordEvaluation(e);_analysisDone=i+1;StateHasChanged();
            }
            _analysisComplete=true;_message="Game analysis complete. Select any turn to inspect its evaluation and centipawn loss.";
        }catch(Exception e){if(operation==_operation)_error=e.Message;}
        finally{if(operation==_operation){_busy=false;_analyzing=false;}StateHasChanged();}
    }
    private async Task Cancel() {
        ++_operation;_busy=true;_analyzing=false;_activity="Restoring game";await JS.InvokeVoidAsync("skynet.cancel");
        try{var reply=await Call(new("import",Pgn:_livePgn));await ApplyReply(reply);_message="Search stopped. Your game is unchanged.";}
        catch(Exception e){_error=e.Message;}finally{_busy=false;StateHasChanged();}
    }
    [JSInvokable]
    public async Task OnShortcut(string key,bool repeat) {
        if(DialogOpen)return;
        // Panning remains available while the engine is thinking.
        switch(key.ToLowerInvariant()) {
            case "w":await JS.InvokeVoidAsync("skynet.pan",0,-120);return;
            case "a":await JS.InvokeVoidAsync("skynet.pan",-120,0);return;
            case "s":await JS.InvokeVoidAsync("skynet.pan",0,120);return;
            case "d":await JS.InvokeVoidAsync("skynet.pan",120,0);return;
        }
        if(_busy)return;
        if(key.Equals("f",StringComparison.OrdinalIgnoreCase)&&!repeat)await Submit();
        else if(key=="Escape"){_selected=null;_showChecks=false;}
        else if(key=="ArrowLeft")await Navigate(_viewIndex-1);
        else if(key=="ArrowRight")await Navigate(_viewIndex+1);
        StateHasChanged();
    }
    public async ValueTask DisposeAsync() {
        if(_keyboardReference==null)return;
        try{await JS.InvokeVoidAsync("skynet.unbindKeyboard");}catch(JSDisconnectedException){}
        _keyboardReference.Dispose();
    }
}
