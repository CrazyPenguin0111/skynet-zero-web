using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkynetZero.Server;
using SkynetZero.Web;

namespace PlatformVerification;
public static class Runner {
    private static int checks;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    private static async Task Reject(Task<HttpResponseMessage> response,HttpStatusCode status,string label){using var result=await response;Check(result.StatusCode==status,$"{label} ({(int)result.StatusCode})");}
    public static async Task Main(){
        var example=Glicko2.Update(new(1500,200,.06),[new(new(1400,30,.06),1),new(new(1550,100,.06),0),new(new(1700,300,.06),0)]);
        Check(Math.Abs(example.Rating-1464.06)<.01&&Math.Abs(example.Deviation-151.52)<.01&&Math.Abs(example.Volatility-.059996)<.000001,"Glicko-2 published reference example");
        Check(Glicko2.Age(new(1600,60,.06),60).Deviation>60,"Rating uncertainty grows with inactivity");
        Check(new TimeControl(60,0).Category=="bullet"&&new TimeControl(180,0).Category=="blitz"&&new TimeControl(480,0).Category=="rapid"&&new TimeControl(1500,0).Category=="classical"&&new TimeControl(120,2).Category=="blitz","Time categories include increment and exact boundaries");
        Check(TournamentService.LeagueBand(1409.9)=="Entry League"&&TournamentService.LeagueBand(1410)=="Advanced League"&&TournamentService.LeagueBand(1550)=="Pro League","Legacy league bands have no fractional-rating gaps");
        for(int n=2;n<=128;n++){int[] sizes=TournamentService.GroupSizes(n);Check(sizes.Sum()==n&&sizes.Max()<=7&&sizes.Max()-sizes.Min()<=1,$"Balanced league groups for {n} entrants");}
        await using var factory=new PlatformFactory();
        Client alice=new(factory),bob=new(factory),stranger=new(factory);
        using(var assets=factory.CreateClient(new(){BaseAddress=new("https://localhost")})){
            for(int i=0;i<305;i++){using var response=await assets.GetAsync("/health");if(!response.IsSuccessStatusCode)throw new Exception("Static requests exhausted the API limit");}
            using var apiResponse=await assets.GetAsync("/api/site");Check(apiResponse.IsSuccessStatusCode,"Static resource requests do not consume the API rate limit");
        }
        await alice.Register("Alice");await bob.Register("Bob");await stranger.Register("Stranger");
        await Reject(alice.Send("api/messages","POST",new {username="Bob",body=(string?)null}),HttpStatusCode.BadRequest,"Null request fields are rejected before dispatch");
        await Reject(alice.Send("api/messages","POST",new {username="Bob"}),HttpStatusCode.BadRequest,"Missing required request fields are rejected");
        await Reject(alice.Send("api/studies","POST",new {title="Null chapter",description="",@public=false,chapters=new object?[]{null}}),HttpStatusCode.BadRequest,"Null study chapters are rejected");
        var profile=await alice.Get<ProfileView>("api/players/Alice");Check(profile.Ratings.Length==24&&profile.Ratings.All(r=>r.Rating==1500&&r.Games==0),"Every variant/time pool starts at 1500");
        using(var bad=new HttpClient(factory.Server.CreateHandler()){BaseAddress=new("https://localhost")})await Reject(bad.PostAsJsonAsync("api/auth/register",new AccountRequest("NoCsrf","password enough long")),HttpStatusCode.BadRequest,"Registration rejects requests without CSRF");
        var pgn=VariantCatalog.StartingPgn("standard-t0");
        await VerifyNativeSearch(factory,alice,pgn);
        var study=await alice.Post<StudyView>("api/studies",new StudyRequest("Secret prep","Private notes",false,[new("ch1","Opening",pgn,"<script>alert(1)</script>")]));
        await Reject(stranger.Http.GetAsync("api/studies/"+study.Id),HttpStatusCode.NotFound,"Private study is not readable by another user");
        Check(!(await stranger.Get<StudyView[]>("api/studies")).Any(s=>s.Id==study.Id),"Private study is excluded from discovery");
        await Reject(stranger.Send("api/studies/"+study.Id,"PUT",new StudyRequest("Hijacked","",true,study.Chapters,study.Version)),HttpStatusCode.Forbidden,"Study editing requires ownership");
        var updated=await alice.SendJson<StudyView>("api/studies/"+study.Id,"PUT",new StudyRequest("Published prep","Description",true,study.Chapters,study.Version));
        Check((await stranger.Get<StudyView>("api/studies/"+study.Id)).Title=="Published prep","Publishing a study changes its visibility");
        await Reject(alice.Send("api/studies/"+study.Id,"PUT",new StudyRequest("Old version","",false,study.Chapters,study.Version)),HttpStatusCode.Conflict,"Concurrent study edits cannot overwrite newer work");
        await alice.Post<MessageView>("api/messages",new MessageRequest("Bob","A private chess message"));
        var thread=await bob.Get<MessageView[]>("api/messages/"+alice.User.Id);Check(thread.Length==1&&thread[0].Body=="A private chess message","A recipient can read a private message");
        Check((await stranger.Get<MessageView[]>("api/messages/"+alice.User.Id)).Length==0,"A third user cannot read someone else’s conversation");
        await bob.Post<JsonElement>("api/players/"+alice.User.Id+"/block");
        await Reject(alice.Send("api/messages","POST",new MessageRequest("Bob","Blocked message")),HttpStatusCode.Forbidden,"Blocking prevents new messages");
        await bob.SendJson<JsonElement>("api/players/"+alice.User.Id+"/block","DELETE");
        var challenge=await alice.Post<ChallengeView>("api/challenges",new ChallengeRequest("t1.5",180,2,true,true,"Bob"));
        await Reject(stranger.Send("api/challenges/"+challenge.Id+"/accept","POST"),HttpStatusCode.Forbidden,"A direct invite cannot be accepted by another player");
        var match=await bob.Post<MatchView>("api/challenges/"+challenge.Id+"/accept");
        Check(match.Games.Length==2,"A match creates exactly two games");
        await Reject(bob.Send("api/challenges/"+challenge.Id+"/accept","POST"),HttpStatusCode.Conflict,"A challenge cannot be accepted twice");
        await Reject(stranger.Http.GetAsync("api/games/"+match.Games[0]),HttpStatusCode.NotFound,"Private games are hidden from third parties");
        var game1=await alice.Get<LiveGameView>("api/games/"+match.Games[0]);var game2=await alice.Get<LiveGameView>("api/games/"+match.Games[1]);
        Check(game1.WhiteId==game2.BlackId&&game1.BlackId==game2.WhiteId,"Colors reverse in game two");
        await Reject(alice.Send($"api/games/{game2.Id}/actions","POST",new GameActionRequest("ready",game2.Version)),HttpStatusCode.Conflict,"Game two cannot start before game one finishes");
        var white=game1.WhiteId==alice.User.Id?alice:bob;var black=white==alice?bob:alice;
        long readyVersion=game1.Version;
        game1=await white.Post<LiveGameView>($"api/games/{game1.Id}/actions",new GameActionRequest("ready",readyVersion));
        game1=await black.Post<LiveGameView>($"api/games/{game1.Id}/actions",new GameActionRequest("ready",readyVersion));
        Check(game1.Status=="active"&&game1.StartedAt!=null,"Both players can ready from the same snapshot; clocks start once");
        await Reject(white.Send($"api/games/{game1.Id}/actions","POST",new GameActionRequest("submit",game1.Version)),HttpStatusCode.BadRequest,"Submitting without a complete legal turn is a validation error");
        await Reject(black.Send($"api/games/{game1.Id}/actions","POST",new GameActionRequest("move",game1.Version,game1.Position.LegalMoves[0].Id)),HttpStatusCode.Forbidden,"The server rejects opponent moves");
        await Reject(white.Send($"api/games/{game1.Id}/actions","POST",new GameActionRequest("move",game1.Version,"made-up-move")),HttpStatusCode.BadRequest,"The server rejects fabricated moves");
        long version=game1.Version;var move=game1.Position.LegalMoves.First(m=>m.From.X==4&&m.From.Y==1&&m.To.X==4&&m.To.Y==3);
        factory.Clock.Advance(3000);game1=await white.Post<LiveGameView>($"api/games/{game1.Id}/actions",new GameActionRequest("move",version,move.Id));
        Check(game1.Position.PendingCount==1&&game1.WhiteMs==177000,"Partial moves consume time without receiving increment");
        var opponentView=await black.Get<LiveGameView>("api/games/"+game1.Id);Check(opponentView.Position.PendingCount==0&&!opponentView.Position.Pgn.Contains("e2e4"),"Unsubmitted moves stay private to the mover");
        await Reject(white.Send($"api/games/{game1.Id}/actions","POST",new GameActionRequest("submit",version)),HttpStatusCode.Conflict,"Stale game commands cannot be replayed");
        factory.Clock.Advance(1000);game1=await white.Post<LiveGameView>($"api/games/{game1.Id}/actions",new GameActionRequest("submit",game1.Version));
        Check(game1.WhiteMs==178000&&!game1.Position.White&&game1.Position.TurnCount==1,"Increment is credited once per complete turn");
        game1=await black.Post<LiveGameView>($"api/games/{game1.Id}/actions",new GameActionRequest("resign",game1.Version));
        Check(game1.Result=="1-0"&&game1.Status=="finished","Resignation finalizes the game on the server");
        var winner=await white.Get<ProfileView>("api/players/"+white.User.Username);Check(winner.Ratings.Single(r=>r.Variant=="t1.5"&&r.Category=="blitz").Games==1&&winner.Ratings.Single(r=>r.Variant=="t1.5"&&r.Category=="blitz").Rating>1500,"A result updates only its Glicko-2 pool");
        await Reject(black.Send($"api/games/{game1.Id}/actions","POST",new GameActionRequest("resign",game1.Version)),HttpStatusCode.Conflict,"A finished game cannot rate a second time");
        game2=await alice.Post<LiveGameView>($"api/games/{game2.Id}/actions",new GameActionRequest("ready",game2.Version));game2=await bob.Post<LiveGameView>($"api/games/{game2.Id}/actions",new GameActionRequest("ready",game2.Version));
        factory.Clock.Advance(181000);
        // A reconnect/action must adjudicate the persisted server clock, not trust the browser.
        game2=await alice.Post<LiveGameView>($"api/games/{game2.Id}/actions",new GameActionRequest("offer-draw",game2.Version));
        Check(game2.Status=="finished"&&game2.Result=="0-1"&&game2.Reason=="timeout","Server time adjudicates flagging after disconnect");
        var completed=await alice.Get<MatchView>("api/matches/"+match.Id);Check(completed.Status=="finished"&&completed.Score1+completed.Score2==2,"Match standings wait for both games");
        await Reject(alice.Send("api/tournaments","POST",new TournamentRequest("Season test","round-robin","standard-t0",300,0,League:true)),HttpStatusCode.Forbidden,"Only admins can create league seasons");
        await VerifyRules(factory);
        await VerifyImport(factory);
        await VerifyTournaments(factory);
        await alice.Post<JsonElement>("api/auth/logout");await alice.Session();await Reject(alice.Http.GetAsync("api/messages"),HttpStatusCode.Unauthorized,"Logout removes access to private data");
        Console.WriteLine($"PASS {checks} platform checks");
    }
    private static async Task VerifyNativeSearch(PlatformFactory factory,Client alice,string pgn){
        var guest=new Client(factory);await guest.Session();
        await Reject(guest.Send("api/engine/search","POST",new EngineRequest("analyze",Pgn:pgn,Index:0,Strength:1)),HttpStatusCode.Unauthorized,"Native search requires an account");
        alice.Http.DefaultRequestHeaders.Remove("X-CSRF");
        try{await Reject(alice.Send("api/engine/search","POST",new EngineRequest("analyze",Pgn:pgn,Index:0,Strength:1)),HttpStatusCode.BadRequest,"Native search requires CSRF protection");}
        finally{await alice.Session();}
        await Reject(alice.Send("api/engine/search","POST",new EngineRequest("import",Pgn:pgn)),HttpStatusCode.BadRequest,"Native search rejects non-search commands");
        await Reject(alice.Send("api/engine/search","POST",new EngineRequest("analyze",Pgn:new string('x',NativeSearch.MaximumPgnLength+1))),HttpStatusCode.BadRequest,"Native search bounds uploaded PGN");
        var analyzed=await alice.Post<EngineReply>("api/engine/search",new EngineRequest("analyze",Pgn:pgn,Index:0,Strength:1));
        Check(analyzed.Error==null&&analyzed.Position==null&&analyzed.Evaluation is {Depth:1,SearchLocation:"server"},"Native child process returns real searched analysis");
        using var scope=factory.Services.CreateScope();var gate=scope.ServiceProvider.GetRequiredService<WriteGate>();
        await gate.Lock.WaitAsync();
        try{
            var played=await alice.Post<EngineReply>("api/engine/search",new EngineRequest("engine",Pgn:pgn,Strength:1)).WaitAsync(TimeSpan.FromSeconds(8));
            Check(played.Error==null&&played.Position?.TurnCount==1&&played.Evaluation is {StopReason:"OpeningBook",SearchLocation:"server"},"Native bot plays a complete turn without holding the multiplayer write lock");
        }finally{gate.Lock.Release();}
        var native=scope.ServiceProvider.GetRequiredService<NativeSearch>();
        using var cancellation=new CancellationTokenSource();
        var searching=native.Run(new("analyze",Pgn:pgn,Index:0,Strength:100),cancellation.Token);
        try{await native.Run(new("analyze",Pgn:pgn,Index:0,Strength:1),default);throw new Exception("Native worker pool exceeded its limit");}
        catch(ApiError error){Check(error.Status==429,"Native worker pool rejects excess work without a queue");}
        var timer=System.Diagnostics.Stopwatch.StartNew();cancellation.Cancel();
        try{await searching;throw new Exception("Canceled native search returned a result");}
        catch(OperationCanceledException){Check(timer.Elapsed<TimeSpan.FromSeconds(5),"Canceling a request terminates its native search process promptly");}
        var after=JsonSerializer.Deserialize<EngineReply>(await native.Run(new("analyze",Pgn:pgn,Index:0,Strength:1),default),Json)!;
        Check(after.Error==null&&after.Evaluation?.Depth==1,"Canceled native search releases its worker slot");
    }
    private static async Task VerifyRules(PlatformFactory factory){
        using var scope=factory.Services.CreateScope();var rules=scope.ServiceProvider.GetRequiredService<Rules>();
        foreach(var variant in VariantCatalog.All){var session=rules.Create(VariantCatalog.StartingPgn(variant.Id));Check(session.Snapshot().LegalMoves.Length>0,$"Playable {variant.Name} starting position");var reloaded=rules.Create(session.Export());Check(reloaded.Snapshot().LegalMoves.Length==session.Snapshot().LegalMoves.Length,$"{variant.Name} survives 5DPGN round trip");}
        string moves="\n1w. (0T1)Pe2e4\n1b. (0T1)Pe7e5\n*";
        var standard=rules.Create(VariantCatalog.StartingPgn("standard").Replace("\n*",moves));
        var t15=rules.Create(VariantCatalog.StartingPgn("t1.5").Replace("\n*",moves));
        var forbidden=standard.Snapshot().LegalMoves.Where(m=>m.From.T>1&&m.To.T==1).ToArray();Check(forbidden.Length>0,"T1.5 fixture has legal Standard travel to turn one");
        Check(!t15.Snapshot().LegalMoves.Any(m=>m.From.T>1&&m.To.T==1),"T1.5 filters White’s travel to turn one");
        bool rejected=false;try{t15.Move(forbidden[0].Id);}catch(InvalidDataException){rejected=true;}Check(rejected,"T1.5 also rejects a forged forbidden move");
        Check(rules.Create(t15.Export()).Export().Contains("[WhiteTurnOneTravel \"forbidden\"]"),"T1.5 restriction is retained in exports and reconnects");
        var blackMove=t15.Snapshot().LegalMoves.First(m=>m.From.X==6&&m.From.Y==0&&m.To.X==5&&m.To.Y==2);t15.Move(blackMove.Id);t15.Submit();
        Check(t15.Snapshot().LegalMoves.Any(m=>m.From.T>1&&m.To.T==1),"T1.5 keeps Black’s travel to turn one");
        await Task.CompletedTask;
    }
    private static async Task VerifyImport(PlatformFactory factory){
        string source=Path.Combine(factory.Path,"legacy.db");await using(var connection=new SqliteConnection("Data Source="+source)){
            await connection.OpenAsync();using var cmd=connection.CreateCommand();cmd.CommandText="""
            CREATE TABLE players(id INTEGER PRIMARY KEY,elo REAL,wins INTEGER,losses INTEGER,draws INTEGER,seasons_missed INTEGER);
            INSERT INTO players VALUES(123456789012345678,1623.5,12,3,4,1);
            CREATE TABLE seasons(season_number INTEGER PRIMARY KEY,active INTEGER);
            INSERT INTO seasons VALUES(3,1);
            CREATE TABLE match_history(match INTEGER PRIMARY KEY,whiteplayer INTEGER,blackplayer INTEGER,colorwon TEXT,season TEXT,league TEXT);
            INSERT INTO match_history VALUES(1,123456789012345678,234567890123456789,'w','Season 3','Pro League');
            CREATE TABLE pairings(id INTEGER PRIMARY KEY);CREATE TABLE elo_history(id INTEGER PRIMARY KEY);CREATE TABLE player_aliases(id INTEGER PRIMARY KEY);
            """;await cmd.ExecuteNonQueryAsync();
        }
        byte[] before=await File.ReadAllBytesAsync(source);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PlatformDb>();var gate=scope.ServiceProvider.GetRequiredService<WriteGate>();await gate.Lock.WaitAsync();
        try{
            var first=await LeagueImport.Run(db,source);var again=await LeagueImport.Run(db,source);Check(first.Players==1&&again.Players==0&&again.Records==0,"Legacy import is idempotent");
            byte[] after=await File.ReadAllBytesAsync(source);Check(before.SequenceEqual(after),"Legacy import leaves the original database unchanged");
            var alice=await db.Users.SingleAsync(p=>p.UserName=="Alice");var bob=await db.Users.SingleAsync(p=>p.UserName=="Bob");
            await LeagueImport.Claim(db,alice,"123456789012345678","renamed_discord_user");await db.SaveChangesAsync();
            var row=await db.LeaguePlayers.FindAsync("123456789012345678");Check(row!.UserId==alice.Id&&row.Elo==1623.5&&row.Wins==12,"Verified Discord ID claims original rating despite username changes");
            bool blocked=false;try{await LeagueImport.Claim(db,bob,"123456789012345678","renamed_discord_user");}catch(ApiError e){blocked=e.Status==409;}Check(blocked,"One Discord league identity cannot be claimed twice");
        }finally{gate.Lock.Release();}
    }
    private static async Task VerifyTournaments(PlatformFactory factory){
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PlatformDb>();var games=scope.ServiceProvider.GetRequiredService<GameService>();var service=scope.ServiceProvider.GetRequiredService<TournamentService>();var gate=scope.ServiceProvider.GetRequiredService<WriteGate>();await gate.Lock.WaitAsync();
        try{
            var people=new List<Player>();for(int i=0;i<8;i++){var p=new Player{Id="tournament-player-"+i,UserName="Tournament"+i,NormalizedUserName="TOURNAMENT"+i};db.Users.Add(p);people.Add(p);}await db.SaveChangesAsync();
            foreach(string format in TournamentService.Formats){
                var t=new Tournament{Name="Test "+format,OwnerId=people[0].Id,Format=format,Variant="standard-t0",Seconds=180,Increment=0,Rated=false,Rounds=3,TieBreak="seed",Created=games.Now};db.Tournaments.Add(t);
                int count=format=="groups-knockout"?8:5;for(int i=0;i<count;i++)db.Entrants.Add(new(){TournamentId=t.Id,UserId=people[i].Id,Joined=games.Now+i});await db.SaveChangesAsync();await service.Start(t);await db.SaveChangesAsync();
                if(format=="round-robin")Check(await db.Matches.CountAsync(m=>m.TournamentId==t.Id)==10,"Round robin pairs every player exactly once");
                int cycles=0;
                while(t.Status=="active"&&cycles++<25){
                    var open=await db.Matches.Where(m=>m.TournamentId==t.Id&&m.Status!="finished").ToArrayAsync();
                    Check(open.Length>0,$"{format} has playable pairings at round {t.Round}");
                    foreach(var m in open){
                        var pair=await db.Games.Where(g=>g.MatchId==m.Id).OrderBy(g=>g.Number).ToArrayAsync();
                        Check(pair.Length==2&&pair[0].WhiteId==pair[1].BlackId,$"{format} enforces two games with reversed colors");
                        // A tied match verifies deterministic knockout resolution without a third game.
                        foreach(var game in pair)await games.Finish(game,m,"1-0","test-result");
                    }
                    await db.SaveChangesAsync();await service.Advance(t);await db.SaveChangesAsync();
                }
                Check(t.Status=="finished"&&t.ChampionId!=null,$"{format} reaches a champion");
                Check(!(await db.Matches.Where(m=>m.TournamentId==t.Id&&!m.Bye).ToArrayAsync()).Any(m=>m.Player1Id==m.Player2Id),$"{format} never pairs a player with themselves");
            }
            // The lower-bracket finalist wins the first grand final: the unbeaten
            // player must get a second chance before the champion is decided.
            var reset=new Tournament{Name="Reset final",OwnerId=people[0].Id,Format="double-elimination",Variant="standard-t0",Seconds=60,Rated=false};db.Tournaments.Add(reset);
            foreach(var p in people.Take(2))db.Entrants.Add(new(){TournamentId=reset.Id,UserId=p.Id,Joined=games.Now});await db.SaveChangesAsync();await service.Start(reset);await db.SaveChangesAsync();
            int[] winners=[0,1,1];foreach(int winner in winners){var m=await db.Matches.SingleAsync(m=>m.TournamentId==reset.Id&&m.Status!="finished");foreach(var g in await db.Games.Where(g=>g.MatchId==m.Id).ToArrayAsync())await games.Finish(g,m,g.WhiteId==people[winner].Id?"1-0":"0-1","test-result");await db.SaveChangesAsync();await service.Advance(reset);await db.SaveChangesAsync();}
            Check(reset.Status=="finished"&&reset.ChampionId==people[1].Id&&await db.Matches.CountAsync(m=>m.TournamentId==reset.Id)==3,"Double elimination resets the final when both finalists have one loss");
            var league=new Tournament{Name="League",OwnerId=people[0].Id,League=true,Variant="standard-t0",Seconds=900,Rated=false};db.Tournaments.Add(league);
            for(int i=0;i<8;i++){db.LeaguePlayers.Add(new(){DiscordId=(345678901234567800L+i).ToString(),UserId=people[i].Id,Elo=i<2?1300:i<4?1450:1600});db.Entrants.Add(new(){TournamentId=league.Id,UserId=people[i].Id,Joined=games.Now+i});}await db.SaveChangesAsync();await service.Start(league);await db.SaveChangesAsync();
            var grouped=await db.Entrants.Where(e=>e.TournamentId==league.Id).ToArrayAsync();Check(grouped.Select(e=>e.Group).Distinct().Count()==3,"League uses Entry, Advanced and Pro Elo groups");
            var leagueMatch=await db.Matches.FirstAsync(m=>m.TournamentId==league.Id);var leagueGame=await db.Games.FirstAsync(g=>g.MatchId==leagueMatch.Id&&g.Number==1);double before=(await db.LeaguePlayers.SingleAsync(l=>l.UserId==leagueGame.WhiteId)).Elo;
            await games.Finish(leagueGame,leagueMatch,"1-0","test-result");await db.SaveChangesAsync();Check(Math.Abs((await db.LeaguePlayers.SingleAsync(l=>l.UserId==leagueGame.WhiteId)).Elo-before-12.5)<.00001,"League retains the bot’s K=25 Elo formula");
        }finally{gate.Lock.Release();}
    }
    private sealed class Client {
        public HttpClient Http{get;}public UserView User{get;private set;}=null!;
        public Client(PlatformFactory factory){Http=factory.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false,HandleCookies=true});}
        public async Task Session(){var s=await Get<SessionView>("api/auth/session");Http.DefaultRequestHeaders.Remove("X-CSRF");Http.DefaultRequestHeaders.Add("X-CSRF",s.CsrfToken);if(s.User!=null)User=s.User;}
        public async Task Register(string name){await Session();User=await Post<UserView>("api/auth/register",new AccountRequest(name,"Test-only password 9281!"));await Session();}
        public async Task<T> Get<T>(string path){using var response=await Http.GetAsync(path);return await Read<T>(response);}
        public Task<HttpResponseMessage> Send(string path,string method,object? body=null){var request=new HttpRequestMessage(new(method),path);if(body!=null)request.Content=JsonContent.Create(body);return Http.SendAsync(request);}
        public Task<T> Post<T>(string path,object? body=null)=>SendJson<T>(path,"POST",body);
        public async Task<T> SendJson<T>(string path,string method,object? body=null){using var response=await Send(path,method,body);return await Read<T>(response);}
        private static async Task<T> Read<T>(HttpResponseMessage response){string text=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new Exception($"HTTP {(int)response.StatusCode}: {text}");return JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(text)?"{}":text,Json)!;}
    }
}
public sealed class TestClock:TimeProvider {
    private long milliseconds=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public override DateTimeOffset GetUtcNow()=>DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref milliseconds));
    public void Advance(long value)=>Interlocked.Add(ref milliseconds,value);
}
public sealed class PlatformFactory:WebApplicationFactory<global::Program> {
    public string Path{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"skynet-platform-tests-"+Guid.NewGuid().ToString("N"));
    public TestClock Clock{get;}=new();
    protected override void ConfigureWebHost(IWebHostBuilder builder){
        builder.UseContentRoot(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../server")));
        builder.UseEnvironment("Testing");builder.UseSetting("DataPath",Path);builder.UseSetting("Logging:LogLevel:Default","Warning");
        builder.ConfigureServices(services=>services.AddSingleton<TimeProvider>(Clock));
    }
    public override async ValueTask DisposeAsync(){await base.DisposeAsync();SqliteConnection.ClearAllPools();if(Directory.Exists(Path))Directory.Delete(Path,true);}
}
