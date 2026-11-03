using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkynetZero.Web;
namespace SkynetZero.Server;

public static partial class Api {
    private static async Task<StudyView> StudyView(Study study,PlatformDb db)=>new(study.Id,study.OwnerId,(await db.Users.FindAsync(study.OwnerId))!.UserName!,study.Title,study.Description,study.Public,study.Version,JsonSerializer.Deserialize<StudyChapter[]>(study.Chapters,Json)??[],study.Updated);
    private static void ValidateStudy(StudyRequest r,Rules rules){
        Clean(r.Title,1,100,"Study title");Clean(r.Description,0,2000,"Description");
        if(r.Chapters.Length is <1 or >20)throw new ApiError(400,"A study needs 1–20 chapters.");
        if(r.Chapters.Any(c=>c==null))throw new ApiError(400,"Chapters cannot be empty.");
        if(r.Chapters.Select(c=>c.Id).Distinct().Count()!=r.Chapters.Length)throw new ApiError(400,"Chapter IDs must be unique.");
        foreach(var chapter in r.Chapters){
            if(chapter==null)throw new ApiError(400,"Chapters cannot be empty.");
            Clean(chapter.Id,1,80,"Chapter ID");Clean(chapter.Title,1,100,"Chapter title");Clean(chapter.Notes,0,20000,"Chapter notes");Clean(chapter.Pgn,1,100000,"Chapter 5DPGN");
            try{rules.Create(chapter.Pgn);}catch(InvalidOperationException){throw new ApiError(400,"The chapter contains an incomplete or illegal turn.");}
        }
    }
    private static void MapStudies(WebApplication app){
        app.MapGet("/api/studies",async(HttpContext c,PlatformDb db)=>{
            string? user=OptionalUser(c);var rows=await db.Studies.Where(s=>s.Public||s.OwnerId==user).OrderByDescending(s=>s.Updated).Take(100).ToArrayAsync();var views=new List<StudyView>();
            foreach(var row in rows)views.Add((await StudyView(row,db)) with {Chapters=[]});return views;
        });
        app.MapGet("/api/studies/{id}",async(string id,HttpContext c,PlatformDb db)=>{
            var row=await db.Studies.FindAsync(id);if(row==null||!row.Public&&row.OwnerId!=OptionalUser(c))throw new ApiError(404,"Study not found.");return await StudyView(row,db);
        });
        app.MapPost("/api/studies",async(StudyRequest r,HttpContext c,PlatformDb db,Rules rules,GameService clock)=>{
            string user=UserId(c);ValidateStudy(r,rules);
            if(await db.Studies.CountAsync(s=>s.OwnerId==user)>=200)throw new ApiError(409,"You have reached the 200-study limit.");
            var row=new Study{OwnerId=user,Title=r.Title.Trim(),Description=r.Description.Trim(),Public=r.Public,Chapters=JsonSerializer.Serialize(r.Chapters,Json),Updated=clock.Now};
            db.Studies.Add(row);await db.SaveChangesAsync();return await StudyView(row,db);
        }).RequireAuthorization();
        app.MapPut("/api/studies/{id}",async(string id,StudyRequest r,HttpContext c,PlatformDb db,Rules rules,GameService clock)=>{
            var row=await db.Studies.FindAsync(id)??throw new ApiError(404,"Study not found.");if(row.OwnerId!=UserId(c))throw new ApiError(403,"Only the owner can edit this study.");
            if(row.Version!=r.Version)throw new ApiError(409,"This study changed in another tab. Reload it before saving.");ValidateStudy(r,rules);
            row.Title=r.Title.Trim();row.Description=r.Description.Trim();row.Public=r.Public;row.Chapters=JsonSerializer.Serialize(r.Chapters,Json);row.Version++;row.Updated=clock.Now;
            await db.SaveChangesAsync();return await StudyView(row,db);
        }).RequireAuthorization();
        app.MapDelete("/api/studies/{id}",async(string id,HttpContext c,PlatformDb db)=>{
            var row=await db.Studies.FindAsync(id)??throw new ApiError(404,"Study not found.");if(row.OwnerId!=UserId(c))throw new ApiError(403,"Only the owner can delete this study.");db.Studies.Remove(row);await db.SaveChangesAsync();return Results.Ok();
        }).RequireAuthorization();
    }
    private static void MapMessages(WebApplication app){
        app.MapGet("/api/messages",async(HttpContext c,PlatformDb db)=>{
            string user=UserId(c);var recent=await db.Messages.Where(m=>m.FromId==user||m.ToId==user).OrderByDescending(m=>m.Sent).Take(2000).ToArrayAsync();var list=new List<ConversationView>();
            foreach(var group in recent.GroupBy(m=>m.FromId==user?m.ToId:m.FromId).Take(100)){
                string name=(await db.Users.FindAsync(group.Key))!.UserName!;var last=group.First();list.Add(new(group.Key,name,last.Body[..Math.Min(last.Body.Length,100)],last.Sent,group.Count(m=>m.ToId==user&&!m.Read),await db.Blocks.AnyAsync(b=>b.UserId==user&&b.OtherId==group.Key)));
            }
            return list;
        }).RequireAuthorization();
        app.MapGet("/api/messages/{other}",async(string other,long? before,HttpContext c,PlatformDb db)=>{
            string user=UserId(c);var person=await db.Users.FindAsync(other)??throw new ApiError(404,"Player not found.");string mine=(await db.Users.FindAsync(user))!.UserName!;
            var messages=await db.Messages.Where(m=>(m.FromId==user&&m.ToId==other||m.FromId==other&&m.ToId==user)&&(before==null||m.Sent<before)).OrderByDescending(m=>m.Sent).Take(100).ToArrayAsync();
            return messages.Reverse().Select(m=>new MessageView(m.Id,m.FromId,m.FromId==user?mine:person.UserName!,m.ToId,m.ToId==user?mine:person.UserName!,m.Body,m.Sent,m.Read));
        }).RequireAuthorization();
        app.MapPost("/api/messages/{other}/read",async(string other,HttpContext c,PlatformDb db)=>{
            string user=UserId(c);await db.Messages.Where(m=>m.ToId==user&&m.FromId==other&&!m.Read).ExecuteUpdateAsync(set=>set.SetProperty(m=>m.Read,true));return Results.Ok();
        }).RequireAuthorization();
        app.MapPost("/api/messages",async(MessageRequest r,HttpContext c,PlatformDb db,UserManager<Player> users,GameService clock,EventFeed feed)=>{
            string user=UserId(c),body=Clean(r.Body,1,4000,"Message");var recipient=await users.FindByNameAsync(r.Username)??throw new ApiError(404,"Player not found.");
            if(recipient.Id==user)throw new ApiError(400,"Choose another player.");if(recipient.Suspended||await Blocked(db,user,recipient.Id))throw new ApiError(403,"This player is not accepting messages from you.");
            if(await db.Messages.CountAsync(m=>m.FromId==user&&m.Sent>=clock.Now-60000)>=20)throw new ApiError(429,"Please wait before sending more messages.");
            var row=new DirectMessage{FromId=user,ToId=recipient.Id,Body=body,Sent=clock.Now};db.Messages.Add(row);await db.SaveChangesAsync();feed.Publish("user:"+recipient.Id);return new MessageView(row.Id,user,(await db.Users.FindAsync(user))!.UserName!,recipient.Id,recipient.UserName!,body,row.Sent,false);
        }).RequireAuthorization();
        app.MapPost("/api/players/{id}/block",async(string id,HttpContext c,PlatformDb db)=>{string user=UserId(c);if(user==id||await db.Users.FindAsync(id)==null)throw new ApiError(400,"Invalid player.");if(await db.Blocks.FindAsync(user,id)==null)db.Blocks.Add(new(){UserId=user,OtherId=id});await db.SaveChangesAsync();return Results.Ok();}).RequireAuthorization();
        app.MapDelete("/api/players/{id}/block",async(string id,HttpContext c,PlatformDb db)=>{var row=await db.Blocks.FindAsync(UserId(c),id);if(row!=null){db.Blocks.Remove(row);await db.SaveChangesAsync();}return Results.Ok();}).RequireAuthorization();
        app.MapPost("/api/reports",async(ReportRequest r,HttpContext c,PlatformDb db,GameService clock)=>{
            string user=UserId(c);Clean(r.Subject,1,100,"Subject");Clean(r.Reason,10,2000,"Report");if(await db.Audit.CountAsync(a=>a.Actor==user&&a.Action=="report"&&a.At>clock.Now-3600000)>=10)throw new ApiError(429,"Please wait before submitting another report.");
            db.Audit.Add(new(){Actor=user,Action="report",Subject=r.Subject,Detail=r.Reason,At=clock.Now});await db.SaveChangesAsync();return Results.Ok();
        }).RequireAuthorization();
    }
    public sealed record ReportRequest(string Subject,string Reason);
    private static void MapEvents(WebApplication app){
        app.MapGet("/api/events",async(HttpContext c,string? game,EventFeed feed,IServiceScopeFactory scopes)=>{
            string key;
            using(var scope=scopes.CreateScope()){
                var db=scope.ServiceProvider.GetRequiredService<PlatformDb>();var gate=scope.ServiceProvider.GetRequiredService<WriteGate>();await gate.Lock.WaitAsync(c.RequestAborted);
                try{
                    if(game!=null){await scope.ServiceProvider.GetRequiredService<GameService>().Accessible(game,OptionalUser(c));key="game:"+game;}
                    else{string user=UserId(c);var p=await db.Users.FindAsync(user);if(p==null||p.Suspended)throw new ApiError(403,"Account unavailable.");key="user:"+user;}
                }finally{gate.Lock.Release();}
            }
            var subscription=feed.Subscribe(key);c.Response.ContentType="text/event-stream";c.Response.Headers.CacheControl="no-cache";c.Response.Headers["X-Accel-Buffering"]="no";
            try{
                await c.Response.WriteAsync("data: connected\n\n",c.RequestAborted);await c.Response.Body.FlushAsync(c.RequestAborted);
                while(!c.RequestAborted.IsCancellationRequested){
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    string message;
                    try{message=await subscription.Channel.Reader.ReadAsync(timeout.Token);}catch(OperationCanceledException)when(!c.RequestAborted.IsCancellationRequested){message="heartbeat";}
                    await c.Response.WriteAsync($"data: {message}\n\n",c.RequestAborted);await c.Response.Body.FlushAsync(c.RequestAborted);
                }
            }catch(OperationCanceledException)when(c.RequestAborted.IsCancellationRequested){}finally{feed.Unsubscribe(key,subscription.Id);}
        });
    }
}
