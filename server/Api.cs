using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkynetZero.Web;
namespace SkynetZero.Server;

public static partial class Api {
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static string UserId(HttpContext ctx)=>ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)??throw new ApiError(401,"Sign in to continue.");
    public static string? OptionalUser(HttpContext ctx)=>ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    public static async Task<Player> User(HttpContext c,PlatformDb db)=>await db.Users.FindAsync(UserId(c))??throw new ApiError(401,"Sign in to continue.");
    public static async Task Admin(HttpContext c,PlatformDb db){if(!(await User(c,db)).IsAdmin)throw new ApiError(403,"Administrator access is required.");}
    public static string Clean(string value,int min,int max,string field){value=value.Trim();if(value.Length<min||value.Length>max)throw new ApiError(400,$"{field} must have {min}–{max} characters.");return value;}
    public static async Task<UserView> UserView(Player p,PlatformDb db){var league=await db.LeaguePlayers.SingleOrDefaultAsync(l=>l.UserId==p.Id);return new(p.Id,p.UserName!,p.IsAdmin,p.DiscordUsername,league?.Elo??1380,league?.Legacy??false);}
    private static void ValidateControl(string variant,int seconds,int increment){VariantCatalog.Get(variant);if(!new TimeControl(seconds,increment).Valid)throw new ApiError(400,"Use 30 seconds to 180 minutes, with an increment from 0 to 180 seconds.");}
    public static void MapCommunity(this WebApplication app,bool discord){
        app.MapGet("/api/site",()=>new SiteInfo(true,discord,VariantCatalog.All,TournamentService.Formats));
        app.MapGet("/api/auth/session",async(HttpContext c,PlatformDb db,IAntiforgery csrf)=>{
            var player=OptionalUser(c) is {} id?await db.Users.FindAsync(id):null;
            return new SessionView(player==null?null:await UserView(player,db),csrf.GetAndStoreTokens(c).RequestToken!);
        });
        app.MapPost("/api/auth/register",async(AccountRequest r,HttpContext c,PlatformDb db,UserManager<Player> users,SignInManager<Player> signIn)=>{
            string username=Clean(r.Username,3,24,"Username");if(!Regex.IsMatch(username,@"^[A-Za-z0-9_-]+$"))throw new ApiError(400,"Usernames use letters, numbers, underscores and hyphens.");
            if(r.Password.Length>128)throw new ApiError(400,"Passwords must be at most 128 characters.");
            var player=new Player{UserName=username};var result=await users.CreateAsync(player,r.Password);
            if(!result.Succeeded)throw new ApiError(400,string.Join(" ",result.Errors.Select(e=>e.Description)));
            await signIn.SignInAsync(player,true);return await UserView(player,db);
        }).RequireRateLimiting("auth");
        app.MapPost("/api/auth/login",async(AccountRequest r,PlatformDb db,SignInManager<Player> signIn,UserManager<Player> users)=>{
            if(r.Password.Length>128||r.Username.Length>24)throw new ApiError(400,"Invalid username or password.");
            var player=await users.FindByNameAsync(r.Username);
            if(player==null||player.Suspended)throw new ApiError(401,"Invalid username or password.");
            var result=await signIn.PasswordSignInAsync(player,r.Password,true,true);
            if(!result.Succeeded)throw new ApiError(401,result.IsLockedOut?"Too many attempts. Try again in 15 minutes.":"Invalid username or password.");
            return await UserView(player,db);
        }).RequireRateLimiting("auth");
        app.MapPost("/api/auth/logout",async(SignInManager<Player> signIn)=>{await signIn.SignOutAsync();return Results.Ok();}).RequireAuthorization();
        app.MapPost("/api/auth/password",async(HttpContext c,PlatformDb db,UserManager<Player> users,PasswordRequest r)=>{
            var player=await User(c,db);if(r.NewPassword.Length>128)throw new ApiError(400,"Password is too long.");
            var result=await users.ChangePasswordAsync(player,r.CurrentPassword,r.NewPassword);
            if(!result.Succeeded)throw new ApiError(400,string.Join(" ",result.Errors.Select(e=>e.Description)));return Results.Ok();
        }).RequireAuthorization().RequireRateLimiting("auth");
        app.MapPost("/api/auth/discord",(HttpContext c)=>{
            if(!discord)throw new ApiError(503,"Discord sign-in has not been configured yet.");
            var properties=new AuthenticationProperties{RedirectUri="/api/auth/discord/complete"};properties.Items["linkUser"]=OptionalUser(c);
            return Results.Challenge(properties,["Discord"]);
        }).RequireRateLimiting("auth");
        app.MapGet("/api/auth/discord/complete",async(HttpContext c,PlatformDb db,UserManager<Player> users,SignInManager<Player> signIn,IConfiguration config)=>{
            var external=await c.AuthenticateAsync(IdentityConstants.ExternalScheme);
            if(!external.Succeeded||external.Principal==null)throw new ApiError(400,"Discord authentication expired. Please try again.");
            string id=external.Principal.FindFirstValue(ClaimTypes.NameIdentifier)??throw new ApiError(400,"Discord did not verify an account ID.");
            string name=external.Principal.FindFirstValue(ClaimTypes.Name)??"Discord player";
            string? link=null;external.Properties?.Items.TryGetValue("linkUser",out link);
            await c.SignOutAsync(IdentityConstants.ExternalScheme);
            await using var transaction=await db.Database.BeginTransactionAsync();Player? player;
            if(link!=null){if(OptionalUser(c)!=link)throw new ApiError(403,"Sign in to the account that started this connection.");player=await db.Users.FindAsync(link);}
            else player=await db.Users.SingleOrDefaultAsync(p=>p.DiscordId==id);
            if(player==null){
                string stem=Regex.Replace(name,@"[^A-Za-z0-9_-]","");if(stem.Length<3)stem="Player";stem=stem[..Math.Min(16,stem.Length)];
                string candidate=stem;for(int attempt=0;await users.FindByNameAsync(candidate)!=null;attempt++)candidate=stem+"_"+System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000000).ToString("D6");
                player=new(){UserName=candidate};var created=await users.CreateAsync(player);if(!created.Succeeded)throw new ApiError(409,"Could not create the Discord account.");
            }
            if(player.Suspended)throw new ApiError(403,"This account is unavailable.");
            await LeagueImport.Claim(db,player,id,name);
            if((config["AdminDiscordIds"]??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Contains(id))player.IsAdmin=true;
            await db.SaveChangesAsync();await transaction.CommitAsync();await signIn.SignInAsync(player,true);
            return Results.Redirect("/#account");
        });
        app.MapGet("/api/players/{name}",async(string name,PlatformDb db,UserManager<Player> users)=>{
            var player=await users.FindByNameAsync(name)??throw new ApiError(404,"Player not found.");var league=await db.LeaguePlayers.SingleOrDefaultAsync(l=>l.UserId==player.Id);
            var saved=await db.Ratings.Where(r=>r.UserId==player.Id).ToArrayAsync();
            var ratings=VariantCatalog.All.Where(v=>v.Available).SelectMany(v=>new[]{"bullet","blitz","rapid","classical"}.Select(category=>{
                var rating=saved.SingleOrDefault(r=>r.Variant==v.Id&&r.Category==category);return new RatingView(v.Id,category,rating?.Value??1500,rating?.Deviation??350,rating?.Games??0);
            })).ToArray();
            return new ProfileView(await UserView(player,db),ratings,league?.Wins??0,league?.Losses??0,league?.Draws??0);
        });
        app.MapGet("/api/leaderboard",async(string? variant,string? category,PlatformDb db)=>{
            variant??="standard-t0";category??="blitz";
            if(category=="league")return await db.LeaguePlayers.Where(l=>l.UserId!=null).Join(db.Users,l=>l.UserId,u=>u.Id,(l,u)=>new LeaderboardEntry(u.Id,u.UserName!,l.Elo,0,l.Wins+l.Losses+l.Draws)).OrderByDescending(r=>r.Rating).Take(100).ToArrayAsync();
            VariantCatalog.Get(variant);if(category is not ("bullet" or "blitz" or "rapid" or "classical"))throw new ApiError(400,"Unknown rating category.");
            return await db.Ratings.Where(r=>r.Variant==variant&&r.Category==category&&r.Games>0).Join(db.Users,r=>r.UserId,u=>u.Id,(r,u)=>new LeaderboardEntry(u.Id,u.UserName!,r.Value,r.Deviation,r.Games)).OrderByDescending(r=>r.Rating).Take(100).ToArrayAsync();
        });
        MapPlay(app);MapStudies(app);MapMessages(app);MapTournaments(app);MapEvents(app);
    }
    public sealed record PasswordRequest(string CurrentPassword,string NewPassword);
    private static void MapPlay(WebApplication app){
        app.MapGet("/api/lobby",async(HttpContext c,PlatformDb db,GameService service)=>{
            string? user=OptionalUser(c);long cutoff=service.Now-30*60*1000;
            var entries=await db.Challenges.Where(x=>!x.Cancelled&&x.MatchId==null&&x.Created>=cutoff&&(!x.Private||x.CreatorId==user||x.OpponentId==user)).OrderByDescending(x=>x.Created).Take(100).ToArrayAsync();
            var challenges=new List<ChallengeView>();foreach(var x in entries)challenges.Add(await ChallengeView(x,db));
            var matches=await db.Matches.Where(m=>!m.Bye&&(m.Player1Id==user||m.Player2Id==user)).OrderByDescending(m=>m.Created).Take(30).ToArrayAsync();
            var views=new List<MatchView>();foreach(var m in matches)views.Add(await service.MatchView(m));return new LobbyView(challenges.ToArray(),views.ToArray());
        });
        app.MapPost("/api/challenges",async(ChallengeRequest r,HttpContext c,PlatformDb db,GameService service,UserManager<Player> users)=>{
            var user=UserId(c);ValidateControl(r.Variant,r.Seconds,r.Increment);string? opponent=null;
            if(!string.IsNullOrWhiteSpace(r.Opponent))opponent=(await users.FindByNameAsync(r.Opponent))?.Id??throw new ApiError(404,"Opponent not found.");
            if(opponent==user)throw new ApiError(400,"Choose another player.");
            if(opponent!=null&&await Blocked(db,user,opponent))throw new ApiError(403,"This player is not accepting challenges from you.");
            if(await db.Challenges.CountAsync(x=>x.CreatorId==user&&!x.Cancelled&&x.MatchId==null&&x.Created>=service.Now-1800000)>=5)throw new ApiError(409,"You already have five open challenges.");
            var row=new Challenge{CreatorId=user,OpponentId=opponent,Variant=r.Variant,Seconds=r.Seconds,Increment=r.Increment,Rated=r.Rated,Private=r.Private,Created=service.Now};db.Challenges.Add(row);await db.SaveChangesAsync();return await ChallengeView(row,db);
        }).RequireAuthorization();
        app.MapGet("/api/challenges/{id}",async(string id,PlatformDb db)=>{var row=await db.Challenges.FindAsync(id)??throw new ApiError(404,"Challenge not found.");return await ChallengeView(row,db);});
        app.MapPost("/api/challenges/{id}/cancel",async(string id,HttpContext c,PlatformDb db)=>{var row=await db.Challenges.FindAsync(id)??throw new ApiError(404,"Challenge not found.");if(row.CreatorId!=UserId(c))throw new ApiError(403,"Only the creator may cancel.");row.Cancelled=true;await db.SaveChangesAsync();return Results.Ok();}).RequireAuthorization();
        app.MapPost("/api/challenges/{id}/accept",async(string id,HttpContext c,PlatformDb db,GameService service,EventFeed feed)=>{
            string user=UserId(c);await using var transaction=await db.Database.BeginTransactionAsync();var row=await db.Challenges.FindAsync(id)??throw new ApiError(404,"Challenge not found.");
            if(row.CreatorId==user||row.OpponentId!=null&&row.OpponentId!=user)throw new ApiError(403,"This challenge is for another player.");
            if(row.Cancelled||row.MatchId!=null||row.Created<service.Now-1800000)throw new ApiError(409,"This challenge is no longer open.");
            if(await Blocked(db,user,row.CreatorId))throw new ApiError(403,"This challenge is unavailable.");
            bool flip=System.Security.Cryptography.RandomNumberGenerator.GetInt32(2)==0;
            var match=await service.CreateMatch(flip?user:row.CreatorId,flip?row.CreatorId:user,row.Variant,row.Seconds,row.Increment,row.Rated,row.Private);row.MatchId=match.Id;
            await db.SaveChangesAsync();await transaction.CommitAsync();feed.Publish("user:"+user);feed.Publish("user:"+row.CreatorId);return await service.MatchView(match);
        }).RequireAuthorization();
        app.MapGet("/api/matches/{id}",async(string id,HttpContext c,PlatformDb db,GameService service)=>{
            var match=await service.AccessibleMatch(id,OptionalUser(c));return await service.MatchView(match);
        });
        app.MapGet("/api/games/{id}",async(string id,HttpContext c,GameService service)=>{var(game,match)=await service.Accessible(id,OptionalUser(c));return await service.View(game,match,OptionalUser(c));});
        app.MapPost("/api/games/{id}/actions",async(string id,GameActionRequest action,HttpContext c,PlatformDb db,GameService service,TournamentService tournaments,EventFeed feed)=>{
            await using var transaction=await db.Database.BeginTransactionAsync();var(game,match)=await service.Accessible(id,UserId(c));
            bool expired=await service.Expire(game,match);if(!expired)await service.Act(game,match,UserId(c),action);
            await db.SaveChangesAsync();if(match.TournamentId is {} t)await tournaments.Advance((await db.Tournaments.FindAsync(t))!);
            await db.SaveChangesAsync();await transaction.CommitAsync();feed.Publish("game:"+id);feed.Publish("user:"+game.WhiteId);feed.Publish("user:"+game.BlackId);return await service.View(game,match,UserId(c));
        }).RequireAuthorization();
        app.MapGet("/api/games/{id}/pgn",async(string id,HttpContext c,GameService service,Rules rules)=>{var(game,_)=await service.Accessible(id,OptionalUser(c));return Results.Text(rules.Snapshot(game,OptionalUser(c)).Pgn,"text/plain");});
    }
    private static async Task<ChallengeView> ChallengeView(Challenge x,PlatformDb db)=>new(x.Id,(await db.Users.FindAsync(x.CreatorId))!.UserName!,x.Variant,x.Seconds,x.Increment,x.Rated,x.Private,x.OpponentId==null?null:(await db.Users.FindAsync(x.OpponentId))?.UserName,x.Created);
    private static Task<bool> Blocked(PlatformDb db,string a,string b)=>db.Blocks.AnyAsync(x=>x.UserId==a&&x.OtherId==b||x.UserId==b&&x.OtherId==a);
}
