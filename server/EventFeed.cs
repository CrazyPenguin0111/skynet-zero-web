using System.Collections.Concurrent;
using System.Threading.Channels;
namespace SkynetZero.Server;

/// <summary>Events are invalidations only; clients fetch authorized snapshots after reconnect.</summary>
public sealed class EventFeed {
    private readonly ConcurrentDictionary<string,ConcurrentDictionary<Guid,Channel<string>>> listeners=new();
    public (Guid Id,Channel<string> Channel) Subscribe(string key){
        var channel=Channel.CreateBounded<string>(new BoundedChannelOptions(16){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true});
        var id=Guid.NewGuid();listeners.GetOrAdd(key,_=>new())[id]=channel;return(id,channel);
    }
    public void Unsubscribe(string key,Guid id){if(listeners.TryGetValue(key,out var group)&&group.TryRemove(id,out var channel))channel.Writer.TryComplete();}
    public void Publish(string key){if(listeners.TryGetValue(key,out var group))foreach(var channel in group.Values)channel.Writer.TryWrite("changed");}
}
public sealed class MonotonicClock : TimeProvider {
    private readonly DateTimeOffset start=DateTimeOffset.UtcNow;
    private readonly long timestamp=global::System.Diagnostics.Stopwatch.GetTimestamp();
    public override DateTimeOffset GetUtcNow()=>start+global::System.Diagnostics.Stopwatch.GetElapsedTime(timestamp);
}
public sealed class PlatformTicker(IServiceScopeFactory scopes,WriteGate gate,EventFeed events,ILogger<PlatformTicker> logger):BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken token){
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while(await timer.WaitForNextTickAsync(token)){
            await gate.Lock.WaitAsync(token);
            try{
                using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PlatformDb>();var games=scope.ServiceProvider.GetRequiredService<GameService>();var tournaments=scope.ServiceProvider.GetRequiredService<TournamentService>();
                var active=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.Games.Where(g=>g.Status=="active"),token);
                var changed=new List<(string,string,string)>();
                await using var transaction=await db.Database.BeginTransactionAsync(token);
                foreach(var game in active){
                    var match=(await db.Matches.FindAsync([game.MatchId],token))!;
                    if(await games.Expire(game,match))changed.Add((game.Id,game.WhiteId,game.BlackId));
                }
                await db.SaveChangesAsync(token);
                if(changed.Count>0){
                    foreach(var t in await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.Tournaments.Where(t=>t.Status=="active"),token))await tournaments.Advance(t);
                }
                foreach(var t in await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.Tournaments.Where(t=>t.Status=="registration"&&t.StartsAt!=null&&t.StartsAt<=games.Now),token)){
                    try{await tournaments.Start(t);}catch(ApiError e){t.StartsAt=null;db.Audit.Add(new(){Actor="system",Action="scheduled-start-failed",Subject=t.Id,Detail=e.Message,At=games.Now});}
                }
                await db.SaveChangesAsync(token);await transaction.CommitAsync(token);
                foreach(var(g,a,b) in changed){events.Publish("game:"+g);events.Publish("user:"+a);events.Publish("user:"+b);}
            }catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Platform clock tick failed");}
            finally{gate.Lock.Release();}
        }
    }
}
