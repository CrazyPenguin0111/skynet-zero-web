using Microsoft.EntityFrameworkCore;
using SkynetZero.Web;
namespace SkynetZero.Server;

public static partial class Api {
    private static async Task<Tournament> AccessibleTournament(string id,HttpContext c,PlatformDb db){
        var t=await db.Tournaments.FindAsync(id)??throw new ApiError(404,"Tournament not found.");
        // A private tournament has an unguessable invite URL and is omitted from discovery.
        return t;
    }
    private static async Task Organize(Tournament t,HttpContext c,PlatformDb db){var user=await User(c,db);if(t.League?!user.IsAdmin:t.OwnerId!=user.Id&&!user.IsAdmin)throw new ApiError(403,"Only the organizer or an administrator can manage this tournament.");}
    private static void MapTournaments(WebApplication app){
        app.MapGet("/api/tournaments",async(HttpContext c,PlatformDb db,TournamentService service)=>{
            string? user=OptionalUser(c);var joined=db.Entrants.Where(e=>e.UserId==user).Select(e=>e.TournamentId);
            var rows=await db.Tournaments.Where(t=>t.Public||t.OwnerId==user||joined.Contains(t.Id)).OrderByDescending(t=>t.Created).Take(100).ToArrayAsync();var views=new List<TournamentView>();foreach(var t in rows)views.Add(await service.View(t,user));return views;
        });
        app.MapPost("/api/tournaments",async(TournamentRequest r,HttpContext c,PlatformDb db,TournamentService service,GameService clock)=>{
            string user=UserId(c);if(r.League)await Admin(c,db);ValidateControl(r.Variant,r.Seconds,r.Increment);Clean(r.Name,3,100,"Tournament name");
            if(!TournamentService.Formats.Contains(r.Format)||r.Rounds is <1 or >20||r.Scoring is not ("game" or "match")||r.TieBreak is not ("seed" or "organizer"))throw new ApiError(400,"Invalid tournament format, round count, scoring or tie-break.");
            if(!double.IsFinite(r.WinPoints)||!double.IsFinite(r.DrawPoints)||!double.IsFinite(r.LossPoints)||r.WinPoints<=r.DrawPoints||r.DrawPoints<r.LossPoints||r.LossPoints<0||r.WinPoints>100)throw new ApiError(400,"Scoring must satisfy 100 ≥ win > draw ≥ loss ≥ 0.");
            if(r.StartsAt!=null&&r.StartsAt<clock.Now)throw new ApiError(400,"The scheduled start must be in the future.");
            if(await db.Tournaments.CountAsync(t=>t.OwnerId==user&&t.Status!="finished"&&t.Status!="cancelled")>=10)throw new ApiError(409,"Finish an existing tournament before creating another.");
            if(r.League&&await db.Tournaments.AnyAsync(t=>t.League&&(t.Status=="registration"||t.Status=="active")))throw new ApiError(409,"Finish the current league season before opening the next one.");
            var row=new Tournament{Name=r.League?"5D Chess League · "+r.Name.Trim():r.Name.Trim(),OwnerId=user,Format=r.League?"round-robin":r.Format,Variant=r.Variant,Seconds=r.Seconds,Increment=r.Increment,Rated=r.Rated,Public=r.Public,League=r.League,Rounds=r.Rounds,Scoring=r.Scoring,WinPoints=r.WinPoints,DrawPoints=r.DrawPoints,LossPoints=r.LossPoints,TieBreak=r.TieBreak,StartsAt=r.StartsAt,Created=clock.Now};
            db.Tournaments.Add(row);await db.SaveChangesAsync();return await service.View(row,user);
        }).RequireAuthorization();
        app.MapGet("/api/tournaments/{id}",async(string id,HttpContext c,PlatformDb db,TournamentService service)=>await service.Detail(await AccessibleTournament(id,c,db),OptionalUser(c)));
        app.MapPost("/api/tournaments/{id}/join",async(string id,HttpContext c,PlatformDb db,TournamentService service,GameService clock)=>{
            string user=UserId(c);var t=await AccessibleTournament(id,c,db);if(t.Status!="registration")throw new ApiError(409,"Registration is closed.");
            if(t.League&&!await db.LeaguePlayers.AnyAsync(p=>p.UserId==user))throw new ApiError(409,"Connect your Discord account before joining the league.");
            if(await db.Entrants.CountAsync(e=>e.TournamentId==id)>=128)throw new ApiError(409,"This tournament is full.");
            if(await db.Entrants.FindAsync(id,user)==null)db.Entrants.Add(new(){TournamentId=id,UserId=user,Joined=clock.Now});await db.SaveChangesAsync();return await service.Detail(t,user);
        }).RequireAuthorization();
        app.MapPost("/api/tournaments/{id}/leave",async(string id,HttpContext c,PlatformDb db)=>{
            var t=await AccessibleTournament(id,c,db);if(t.Status!="registration")throw new ApiError(409,"Contact the organizer to withdraw after the tournament starts.");
            var row=await db.Entrants.FindAsync(id,UserId(c));if(row!=null)db.Entrants.Remove(row);await db.SaveChangesAsync();return Results.Ok();
        }).RequireAuthorization();
        app.MapPost("/api/tournaments/{id}/start",async(string id,HttpContext c,PlatformDb db,TournamentService service,GameService clock)=>{
            var t=await AccessibleTournament(id,c,db);await Organize(t,c,db);await using var transaction=await db.Database.BeginTransactionAsync();await service.Start(t);
            db.Audit.Add(new(){Actor=UserId(c),Action="start-tournament",Subject=id,Detail="Registration closed and seeds frozen.",At=clock.Now});await db.SaveChangesAsync();await transaction.CommitAsync();return await service.Detail(t,UserId(c));
        }).RequireAuthorization();
        app.MapPost("/api/tournaments/{id}/schedule",async(string id,ScheduleRequest r,HttpContext c,PlatformDb db,GameService clock)=>{
            var t=await AccessibleTournament(id,c,db);await Organize(t,c,db);if(t.Status!="registration")throw new ApiError(409,"This tournament has started.");if(r.StartsAt!=null&&r.StartsAt<=clock.Now)throw new ApiError(400,"Choose a future start time.");
            t.StartsAt=r.StartsAt;await db.SaveChangesAsync();return Results.Ok();
        }).RequireAuthorization();
        app.MapPost("/api/tournaments/{id}/cancel",async(string id,HttpContext c,PlatformDb db)=>{
            var t=await AccessibleTournament(id,c,db);await Organize(t,c,db);if(t.Status!="registration")throw new ApiError(409,"Only an unstarted tournament can be cancelled.");t.Status="cancelled";t.StartsAt=null;await db.SaveChangesAsync();return Results.Ok();
        }).RequireAuthorization();
        app.MapPost("/api/tournaments/{id}/resolve",async(string id,ResolveRequest r,HttpContext c,PlatformDb db,TournamentService service,GameService clock)=>{
            var t=await AccessibleTournament(id,c,db);await Organize(t,c,db);Clean(r.Reason,10,1000,"Decision reason");
            var m=await db.Matches.FindAsync(r.MatchId)??throw new ApiError(404,"Match not found.");
            if(m.TournamentId!=id||m.Status!="finished"||m.Score1!=m.Score2||m.WinnerId!=null||t.TieBreak!="organizer"||t.Format is "round-robin" or "swiss"||t.Format=="groups-knockout"&&m.Group!="Knockout")throw new ApiError(409,"This match does not need a knockout tie-break.");
            if(r.WinnerId!=m.Player1Id&&r.WinnerId!=m.Player2Id)throw new ApiError(400,"Choose a player from this match.");
            await using var transaction=await db.Database.BeginTransactionAsync();m.WinnerId=r.WinnerId;db.Audit.Add(new(){Actor=UserId(c),Action="resolve-tie",Subject=m.Id,Detail=r.Reason,At=clock.Now});await db.SaveChangesAsync();await service.Advance(t);await db.SaveChangesAsync();await transaction.CommitAsync();return await service.Detail(t,UserId(c));
        }).RequireAuthorization();
        app.MapPost("/api/tournaments/{id}/forfeit",async(string id,ResolveRequest r,HttpContext c,PlatformDb db,TournamentService service,GameService clock,EventFeed feed)=>{
            var t=await AccessibleTournament(id,c,db);await Organize(t,c,db);Clean(r.Reason,10,1000,"Forfeit reason");
            var m=await db.Matches.FindAsync(r.MatchId)??throw new ApiError(404,"Match not found.");
            if(m.TournamentId!=id||m.Status=="finished"||r.WinnerId!=m.Player1Id&&r.WinnerId!=m.Player2Id)throw new ApiError(409,"Choose an unfinished match and one of its players.");
            await using var transaction=await db.Database.BeginTransactionAsync();
            foreach(var game in await db.Games.Where(g=>g.MatchId==m.Id&&g.Status!="finished").OrderBy(g=>g.Number).ToArrayAsync())await clock.Finish(game,m,game.WhiteId==r.WinnerId?"1-0":"0-1","forfeit");
            db.Audit.Add(new(){Actor=UserId(c),Action="forfeit",Subject=m.Id,Detail=r.Reason,At=clock.Now});await db.SaveChangesAsync();await service.Advance(t);await db.SaveChangesAsync();await transaction.CommitAsync();feed.Publish("user:"+m.Player1Id);feed.Publish("user:"+m.Player2Id);return await service.Detail(t,UserId(c));
        }).RequireAuthorization();
        app.MapGet("/api/league",async(HttpContext c,PlatformDb db,TournamentService service)=>{
            var rows=await db.Tournaments.Where(t=>t.League).OrderByDescending(t=>t.Created).Take(50).ToArrayAsync();var views=new List<TournamentView>();foreach(var row in rows)views.Add(await service.View(row,OptionalUser(c)));
            return new LeagueSummary(await db.LeaguePlayers.CountAsync(p=>p.Legacy),await db.LeaguePlayers.CountAsync(p=>p.Legacy&&p.UserId!=null),await db.LegacyRecords.CountAsync(r=>r.Table=="match_history"),views.ToArray());
        });
        app.MapGet("/api/admin/audit",async(HttpContext c,PlatformDb db)=>{await Admin(c,db);return await db.Audit.OrderByDescending(a=>a.Id).Take(100).ToArrayAsync();}).RequireAuthorization();
        app.MapPost("/api/admin/players/{id}/suspend",async(string id,SuspendRequest r,HttpContext c,PlatformDb db,GameService clock)=>{
            await Admin(c,db);if(id==UserId(c))throw new ApiError(400,"You cannot suspend your own account.");Clean(r.Reason,10,1000,"Reason");var p=await db.Users.FindAsync(id)??throw new ApiError(404,"Player not found.");p.Suspended=r.Suspended;
            db.Audit.Add(new(){Actor=UserId(c),Action=r.Suspended?"suspend":"unsuspend",Subject=id,Detail=r.Reason,At=clock.Now});await db.SaveChangesAsync();return Results.Ok();
        }).RequireAuthorization();
    }
    public sealed record ScheduleRequest(long? StartsAt);
    public sealed record ResolveRequest(string MatchId,string WinnerId,string Reason);
    public sealed record SuspendRequest(bool Suspended,string Reason);
}
