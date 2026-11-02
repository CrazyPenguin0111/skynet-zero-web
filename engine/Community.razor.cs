using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
namespace SkynetZero.Web;

public partial class Community {
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static readonly (string Route,string Label)[] Nav=[("play","Play"),("studies","Studies"),("tournaments","Tournaments"),("league","5D League"),("players","Players"),("messages","Messages")];
    private static readonly TimeControl[] Presets=[new(60,0),new(120,1),new(180,2),new(300,3),new(600,5),new(1800,10)];
    private DotNetObjectReference<Community>? _reference;
    private readonly CancellationTokenSource _stop=new();
    private Task? _poll;
    private bool _initialized,_online,_loading,_busy,_register,_rated=true,_private,_newTournament,_leagueForm,_studyAnalysis,_blocked;
    private string _page="play",_argument="",_route="play",_returnTo="play";
    private string? _error,_notice,_analysisPgn;
    private SiteInfo? _site;private SessionView? _session;private LobbyView? _lobby;private ChallengeView? _challenge;private MatchView? _match;private StudyView? _study;private ProfileView? _profile;private TournamentDetail? _tournament;private LeagueSummary? _league;
    private StudyView[] _studies=[];private ConversationView[] _conversations=[];private MessageView[] _messages=[];private TournamentView[] _tournaments=[];private LeaderboardEntry[] _leaders=[];private AuditView[] _audit=[];
    private string _variant="standard-t0",_opponent="",_shareUrl="",_username="",_password="",_oldPassword="",_newPassword="";
    private double _minutes=5;private int _increment=3,_chapter,_rounds=5;
    private string _recipient="",_conversationName="",_otherId="",_messageBody="",_tournamentName="",_format="round-robin",_scoring="game",_tieBreak="seed";
    private double _winPoints=3,_drawPoints=1,_lossPoints;
    private DateTime? _scheduledStart;
    private string _leaderVariant="standard-t0",_leaderCategory="blitz",_playerSearch="",_decisionWinner="",_decisionReason="",_moderateId="",_moderateReason="";
    private TournamentMatchView? _decisionMatch;
    private bool OwnStudy=>_study!=null&&_session?.User!=null&&_study.OwnerId==_session.User.Id;
    private sealed record AuditView(string Action,string Subject,string Detail,long At);
    private string VariantName(string id)=>_site?.Variants.FirstOrDefault(v=>v.Id==id)?.Name??id;
    private static string FormatName(string id)=>id switch {"round-robin"=>"Round robin","single-elimination"=>"Single elimination","double-elimination"=>"Double elimination","groups-knockout"=>"Groups → knockout","swiss"=>"Swiss",_=>id};
    private static string Date(long value)=>DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime().ToString("MMM d, yyyy");
    private async Task<T> Request<T>(string path,string method="GET",object? body=null){
        string raw=await JS.InvokeAsync<string>("community.request",path,method,body==null?null:JsonSerializer.Serialize(body,Json));
        return JsonSerializer.Deserialize<T>(raw,Json)!;
    }
    private async Task Mutate(string path,string method="POST",object? body=null)=>await Request<JsonElement>(path,method,body);
    protected override async Task OnAfterRenderAsync(bool firstRender){
        if(!firstRender)return;
        try{_site=await Request<SiteInfo>("api/site");_online=_site.Online;}catch{_online=false;}
        _initialized=true;
        if(_online){
            _reference=DotNetObjectReference.Create(this);
            try{_session=await Request<SessionView>("api/auth/session");}catch(Exception e){_error=Error(e);}
            string route=await JS.InvokeAsync<string>("community.init",_reference);await CommunityNavigate(route);
            if(_session?.User!=null)await JS.InvokeVoidAsync("community.watch","user",null,_reference);
            _poll=Poll();
        }
        StateHasChanged();
    }
    private async Task Poll(){
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(8));
        try{while(await timer.WaitForNextTickAsync(_stop.Token))await InvokeAsync(CommunityChanged);}catch(OperationCanceledException){}
    }
    [JSInvokable] public async Task CommunityNavigate(string route){
        _route=route.Split('?')[0];var parts=_route.Split('/',2);_page=parts[0];_argument=parts.Length>1?Uri.UnescapeDataString(parts[1]):"";_error=null;_notice=null;_studyAnalysis=false;_decisionMatch=null;
        _study=null;_match=null;_challenge=null;_tournament=null;_profile=null;
        await Refresh();
    }
    [JSInvokable] public async Task CommunityChanged(){
        if(_busy||_loading)return;
        if(_page is "play" or "match" or "tournament")await Refresh();
        if(_page=="messages"&&_session?.User!=null){
            try{_conversations=await Request<ConversationView[]>("api/messages");if(_otherId.Length>0)_messages=await Request<MessageView[]>("api/messages/"+_otherId);StateHasChanged();}catch{}
        }
    }
    private async Task Refresh(){
        _loading=true;StateHasChanged();string route=_route;
        try{
            switch(_page){
                case "play":_lobby=await Request<LobbyView>("api/lobby");break;
                case "invite":_challenge=await Request<ChallengeView>("api/challenges/"+_argument);break;
                case "match":_match=await Request<MatchView>("api/matches/"+_argument);break;
                case "studies":_studies=await Request<StudyView[]>("api/studies");break;
                case "study":_study=await Request<StudyView>("api/studies/"+_argument);_chapter=Math.Clamp(_chapter,0,_study.Chapters.Length-1);break;
                case "tournaments":_tournaments=await Request<TournamentView[]>("api/tournaments");break;
                case "league":_league=await Request<LeagueSummary>("api/league");break;
                case "tournament":_tournament=await Request<TournamentDetail>("api/tournaments/"+_argument);break;
                case "players":await LoadLeaderboard();break;
                case "player":_profile=await Request<ProfileView>("api/players/"+Uri.EscapeDataString(_argument));break;
                case "account":_session=await Request<SessionView>("api/auth/session");break;
                case "messages":if(_session?.User!=null)_conversations=await Request<ConversationView[]>("api/messages");break;
                case "admin":_audit=await Request<AuditView[]>("api/admin/audit");break;
            }
        }catch(Exception e){if(route==_route)_error=Error(e);}
        finally{_loading=false;StateHasChanged();}
    }
    private static string Error(Exception e)=>e.Message.Split('\n')[0].Replace("Error: ","");
    private Task Go(string route)=>JS.InvokeVoidAsync("community.go",route).AsTask();
    private async Task Run(Func<Task> action){if(_busy)return;_busy=true;_error=null;try{await action();}catch(Exception e){_error=Error(e);}finally{_busy=false;StateHasChanged();}}
    private async Task<bool> SignedIn(){if(_session?.User!=null)return true;_returnTo=_route;await Go("account");return false;}
    private Task QuickMatch(TimeControl tc)=>Run(async()=>{_minutes=tc.Seconds/60d;_increment=tc.Increment;_opponent="";_private=false;_challenge=null;await Go("challenge");});
    private Task CreateChallenge()=>Run(async()=>{
        if(!await SignedIn())return;
        _challenge=await Request<ChallengeView>("api/challenges","POST",new ChallengeRequest(_variant,(int)(_minutes*60),_increment,_rated,_private,_opponent));
        _shareUrl=await JS.InvokeAsync<string>("community.share","invite/"+_challenge.Id);
    });
    private Task CancelChallenge(string id)=>Run(async()=>{await Mutate("api/challenges/"+id+"/cancel");await Refresh();});
    private Task AcceptChallenge(string id)=>Run(async()=>{if(!await SignedIn())return;var match=await Request<MatchView>("api/challenges/"+id+"/accept","POST");await Go("match/"+match.Id);});
    private Task Authenticate()=>Run(async()=>{
        await Request<UserView>("api/auth/"+(_register?"register":"login"),"POST",new AccountRequest(_username,_password));_password="";
        _session=await Request<SessionView>("api/auth/session");await JS.InvokeVoidAsync("community.watch","user",null,_reference);await Go(_returnTo);
    });
    private Task Logout()=>Run(async()=>{await Mutate("api/auth/logout");await JS.InvokeVoidAsync("community.stop","user");_session=await Request<SessionView>("api/auth/session");_conversations=[];_messages=[];_otherId="";await Go("play");});
    private Task ChangePassword()=>Run(async()=>{await Mutate("api/auth/password",body:new{currentPassword=_oldPassword,newPassword=_newPassword});_oldPassword=_newPassword="";_notice="Password changed.";});
    private Task ReviewGame(string pgn)=>Run(async()=>{_analysisPgn=pgn;await Go("bot");});
    private Task Copy(string value)=>Run(async()=>{await JS.InvokeVoidAsync("community.copy",value);_notice="Link copied.";});
    private async Task CopyRoute(string route){string link=await JS.InvokeAsync<string>("community.share",route);await Copy(link);}
    private Task NewStudy()=>Run(async()=>{
        if(!await SignedIn())return;
        var request=new StudyRequest("Untitled study","",false,[new(Ids(),"Chapter 1",VariantCatalog.StartingPgn("standard-t0"),"")]);
        _study=await Request<StudyView>("api/studies","POST",request);_chapter=0;await Go("study/"+_study.Id);
    });
    private static string Ids()=>Guid.NewGuid().ToString("N");
    private void EditChapter(string? title=null,string? pgn=null,string? notes=null){if(!OwnStudy)return;var old=_study!.Chapters[_chapter];_study.Chapters[_chapter]=old with {Title=title??old.Title,Pgn=pgn??old.Pgn,Notes=notes??old.Notes};}
    private void AddChapter(){if(_study==null||!OwnStudy||_study.Chapters.Length>=20)return;_study=_study with {Chapters=[.._study.Chapters,new(Ids(),$"Chapter {_study.Chapters.Length+1}",VariantCatalog.StartingPgn("standard-t0"),"")]};_chapter=_study.Chapters.Length-1;}
    private void RemoveChapter(){if(_study==null||!OwnStudy||_study.Chapters.Length<2)return;_study=_study with {Chapters=_study.Chapters.Where((_,i)=>i!=_chapter).ToArray()};_chapter=Math.Clamp(_chapter,0,_study.Chapters.Length-1);}
    private Task SaveStudy()=>Run(async()=>{
        if(_study==null||!OwnStudy)return;_study=await Request<StudyView>("api/studies/"+_study.Id,"PUT",new StudyRequest(_study.Title,_study.Description,_study.Public,_study.Chapters,_study.Version));_notice="Study saved.";
    });
    private async Task SaveAnalysisChapter(string pgn){if(!OwnStudy)return;EditChapter(pgn:pgn);await SaveStudy();}
    private Task OpenConversation()=>Run(async()=>{if(!await SignedIn())return;var player=await Request<ProfileView>("api/players/"+Uri.EscapeDataString(_recipient));await LoadConversation(player.User.Id,player.User.Username);});
    private async Task LoadConversation(string id,string name){
        if(_otherId!=id)_messageBody="";_otherId=id;_recipient=_conversationName=name;_blocked=_conversations.FirstOrDefault(p=>p.UserId==id)?.Blocked??false;_messages=await Request<MessageView[]>("api/messages/"+id);await Mutate("api/messages/"+id+"/read");
    }
    private Task SendMessage()=>Run(async()=>{if(!await SignedIn())return;await Request<MessageView>("api/messages","POST",new MessageRequest(_conversationName,_messageBody));_messageBody="";await LoadConversation(_otherId,_conversationName);_conversations=await Request<ConversationView[]>("api/messages");});
    private Task OlderMessages()=>Run(async()=>{if(_messages.Length==0)return;var older=await Request<MessageView[]>($"api/messages/{_otherId}?before={_messages[0].Sent}");_messages=[..older,.._messages];});
    private Task ToggleBlock()=>Run(async()=>{await Mutate("api/players/"+_otherId+"/block",_blocked?"DELETE":"POST");_blocked=!_blocked;});
    private void OpenTournamentForm(){_newTournament=true;_leagueForm=_page=="league";_tournamentName="";_variant="standard-t0";_private=false;}
    private Task CreateTournament()=>Run(async()=>{
        if(!await SignedIn())return;
        var t=await Request<TournamentView>("api/tournaments","POST",new TournamentRequest(_tournamentName,_format,_variant,(int)(_minutes*60),_increment,_rated,!_private,_leagueForm,_rounds,_scoring,_winPoints,_drawPoints,_lossPoints,_tieBreak));_newTournament=false;await Go("tournament/"+t.Id);
    });
    private bool CanOrganize(TournamentView t)=>_session?.User is {} u&&(u.Admin||!t.League&&u.Id==t.OwnerId);
    private Task JoinTournament()=>Run(async()=>{if(!await SignedIn()||_tournament==null)return;await Mutate($"api/tournaments/{_tournament.Tournament.Id}/{(_tournament.Tournament.Joined?"leave":"join")}");await Refresh();});
    private Task StartTournament()=>Run(async()=>{if(_tournament==null)return;_tournament=await Request<TournamentDetail>($"api/tournaments/{_tournament.Tournament.Id}/start","POST");});
    private Task ScheduleTournament()=>Run(async()=>{if(_tournament==null||_scheduledStart==null)return;long at=new DateTimeOffset(DateTime.SpecifyKind(_scheduledStart.Value,DateTimeKind.Utc)).ToUnixTimeMilliseconds();await Mutate($"api/tournaments/{_tournament.Tournament.Id}/schedule",body:new{startsAt=at});await Refresh();});
    private Task ClearSchedule()=>Run(async()=>{if(_tournament==null)return;await Mutate($"api/tournaments/{_tournament.Tournament.Id}/schedule",body:new{startsAt=(long?)null});await Refresh();});
    private Task CancelTournament()=>Run(async()=>{if(_tournament==null)return;await Mutate($"api/tournaments/{_tournament.Tournament.Id}/cancel");await Refresh();});
    private Task ResolveMatch()=>Run(async()=>{
        if(_tournament==null||_decisionMatch==null)return;string action=_decisionMatch.Status=="finished"?"resolve":"forfeit";
        _tournament=await Request<TournamentDetail>($"api/tournaments/{_tournament.Tournament.Id}/{action}","POST",new{matchId=_decisionMatch.Id,winnerId=_decisionWinner,reason=_decisionReason});_decisionMatch=null;_decisionWinner=_decisionReason="";
    });
    private async Task LoadLeaderboard(){try{_leaders=await Request<LeaderboardEntry[]>($"api/leaderboard?variant={Uri.EscapeDataString(_leaderVariant)}&category={_leaderCategory}");}catch(Exception e){_error=Error(e);}}
    private Task FindPlayer()=>Go("player/"+Uri.EscapeDataString(_playerSearch.Trim()));
    private Task ChallengePlayer(){_opponent=_profile!.User.Username;_private=true;_challenge=null;return Go("challenge");}
    private async Task MessagePlayer(){if(!await SignedIn())return;_recipient=_profile!.User.Username;await LoadConversation(_profile.User.Id,_recipient);await Go("messages");}
    private Task Suspend(bool suspended)=>Run(async()=>{await Mutate($"api/admin/players/{_moderateId}/suspend",body:new{suspended,reason=_moderateReason});_notice=suspended?"Account suspended.":"Account restored.";await Refresh();});
    public async ValueTask DisposeAsync(){_stop.Cancel();if(_poll!=null)await _poll;try{await JS.InvokeVoidAsync("community.dispose");}catch{} _reference?.Dispose();_stop.Dispose();}
}
