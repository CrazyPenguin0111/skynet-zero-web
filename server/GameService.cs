using Microsoft.EntityFrameworkCore;
using SkynetZero.Web;
namespace SkynetZero.Server;

public sealed class ApiError(int status,string message):Exception(message){public int Status{get;}=status;}
public sealed class Rules {
    public PortableModel Model {get;}
    private readonly Dictionary<string,(string Pgn,PositionView Current,PositionView Committed)> snapshots=[];
    public Rules(){
        using var stream=typeof(PortableModel).Assembly.GetManifestResourceStream("SkynetZero.LearnedTables.bin");
        if(stream!=null)SkynetZero.SacrificeTable.ReadLearned(stream,replace:true);
        Model=PortableModel.LoadBundled();
    }
    public GameSession Create(string pgn){var session=new GameSession(Model);session.Import(pgn);return session;}
    public PositionView Snapshot(OnlineGame game,string? viewer){
        if(!snapshots.TryGetValue(game.Id,out var entry)||entry.Pgn!=game.Pgn){
            var session=Create(game.Pgn);var current=session.Snapshot();
            while(session.SnapshotPendingCount()>0)session.Undo();
            entry=(game.Pgn,current,current.PendingCount==0?current:session.Snapshot());
            if(snapshots.Count>=256)snapshots.Remove(snapshots.Keys.First());
            snapshots[game.Id]=entry;
        }
        // Draft moves belong to the mover until the complete turn is submitted.
        return game.Status=="active"&&viewer!=(game.WhiteToMove?game.WhiteId:game.BlackId)?entry.Committed:entry.Current;
    }
}
public sealed class GameService(PlatformDb db,Rules rules,TimeProvider time) {
    public long Now=>time.GetUtcNow().ToUnixTimeMilliseconds();
    public async Task<Match> CreateMatch(string p1,string p2,string variant,int seconds,int increment,bool rated,bool isPrivate,Tournament? tournament=null,int round=0,string group=""){
        var a=await db.Users.FindAsync(p1)??throw new ApiError(404,"Player not found.");
        var b=await db.Users.FindAsync(p2)??throw new ApiError(404,"Player not found.");
        var match=new Match{Player1Id=p1,Player2Id=p2,Variant=variant,Seconds=seconds,Increment=increment,Rated=rated,Private=isPrivate,TournamentId=tournament?.Id,Round=round,Group=group,Created=Now};
        db.Matches.Add(match);
        for(int number=1;number<=2;number++){
            var session=rules.Create(VariantCatalog.StartingPgn(variant));
            session.SetPlayers(number==1?a.UserName!:b.UserName!,number==1?b.UserName!:a.UserName!);
            db.Games.Add(new(){MatchId=match.Id,Number=number,WhiteId=number==1?p1:p2,BlackId=number==1?p2:p1,Pgn=session.Export(),WhiteMs=seconds*1000L,BlackMs=seconds*1000L});
        }
        return match;
    }
    public async Task<MatchView> MatchView(Match m){
        string a=(await db.Users.FindAsync(m.Player1Id))?.UserName??"Bye",b=(await db.Users.FindAsync(m.Player2Id))?.UserName??"Bye";
        return new(m.Id,a,b,m.Variant,new TimeControl(m.Seconds,m.Increment).Category,m.Rated,m.Status,m.Score1,m.Score2,await db.Games.Where(g=>g.MatchId==m.Id).OrderBy(g=>g.Number).Select(g=>g.Id).ToArrayAsync(),m.TournamentId);
    }
    public async Task<(OnlineGame,Match)> Accessible(string id,string? user){
        var game=await db.Games.FindAsync(id)??throw new ApiError(404,"Game not found.");
        var match=await AccessibleMatch(game.MatchId,user);
        return(game,match);
    }
    public async Task<Match> AccessibleMatch(string id,string? user){
        var match=await db.Matches.FindAsync(id)??throw new ApiError(404,"Match not found.");
        if(match.Private&&user!=match.Player1Id&&user!=match.Player2Id&&!(await IsOrganizer(match,user)))throw new ApiError(404,"Match not found.");
        return match;
    }
    private async Task<bool> IsOrganizer(Match match,string? user)=>user!=null&&match.TournamentId!=null&&
        (await db.Tournaments.AnyAsync(t=>t.Id==match.TournamentId&&t.OwnerId==user)||await db.Users.AnyAsync(p=>p.Id==user&&p.IsAdmin&&!p.Suspended));
    public async Task<LiveGameView> View(OnlineGame game,Match match,string? viewer=null){
        var position=rules.Snapshot(game,viewer);long now=Now;
        long remaining=game.Status=="active"?Math.Max(0,now-game.TurnStarted!.Value):0;
        return new(game.Id,match.Id,game.WhiteId,(await db.Users.FindAsync(game.WhiteId))!.UserName!,game.BlackId,(await db.Users.FindAsync(game.BlackId))!.UserName!,match.Variant,new TimeControl(match.Seconds,match.Increment).Category,match.Increment,match.Rated,game.Status,game.Result,game.Reason,
            Math.Max(0,game.WhiteMs-(game.WhiteToMove?remaining:0)),Math.Max(0,game.BlackMs-(!game.WhiteToMove?remaining:0)),now,game.TurnStarted,game.Version,game.DrawOffer,
            new[]{game.WhiteReady?game.WhiteId:null,game.BlackReady?game.BlackId:null}.OfType<string>().ToArray(),position);
    }
    public async Task<bool> Expire(OnlineGame game,Match match){
        if(game.Status!="active"||game.TurnStarted==null)return false;
        long elapsed=Math.Max(0,Now-game.TurnStarted.Value),remaining=game.WhiteToMove?game.WhiteMs:game.BlackMs;
        if(elapsed<remaining)return false;
        if(game.WhiteToMove)game.WhiteMs=0;else game.BlackMs=0;
        await Finish(game,match,game.WhiteToMove?"0-1":"1-0","timeout");return true;
    }
    public async Task Act(OnlineGame game,Match match,string user,GameActionRequest action){
        if(user!=game.WhiteId&&user!=game.BlackId)throw new ApiError(403,"Only the players may act in this game.");
        if(action.Version!=game.Version)throw new ApiError(409,"The position changed. Refresh and try again.");
        if(game.Status=="finished")throw new ApiError(409,"This game has finished.");
        if(action.Action=="ready"){
            if(game.Status!="waiting")throw new ApiError(409,"This game has already started.");
            if(game.Number==2&&await db.Games.AnyAsync(g=>g.MatchId==match.Id&&g.Number==1&&g.Status!="finished"))throw new ApiError(409,"Finish game one before starting game two.");
            if(user==game.WhiteId)game.WhiteReady=true;else game.BlackReady=true;
            if(game.WhiteReady&&game.BlackReady){
                if(await db.Games.AnyAsync(g=>g.Status=="active"&&(g.WhiteId==game.WhiteId||g.BlackId==game.WhiteId||g.WhiteId==game.BlackId||g.BlackId==game.BlackId)))throw new ApiError(409,"One player is already playing a live game.");
                game.Status="active";game.TurnStarted=Now;match.Status="active";
            }
            game.Version++;return;
        }
        if(game.Status!="active")throw new ApiError(409,"Both players must be ready before the game starts.");
        if(action.Action=="resign"){await Finish(game,match,user==game.WhiteId?"0-1":"1-0","resignation");return;}
        if(action.Action=="offer-draw"){game.DrawOffer=user;game.Version++;return;}
        if(action.Action=="decline-draw"){if(game.DrawOffer==null||game.DrawOffer==user)throw new ApiError(409,"No opponent draw offer.");game.DrawOffer=null;game.Version++;return;}
        if(action.Action=="accept-draw"){
            if(game.DrawOffer==null||game.DrawOffer==user)throw new ApiError(409,"No opponent draw offer.");
            await Finish(game,match,"1/2-1/2","agreement");return;
        }
        if((game.WhiteToMove?game.WhiteId:game.BlackId)!=user)throw new ApiError(403,"It is your opponent’s turn.");
        var session=rules.Create(game.Pgn);var before=session.Snapshot();
        switch(action.Action){
            case "move":
                if(action.Move==null||!before.LegalMoves.Any(m=>m.Id==action.Move))throw new ApiError(400,"That move is not legal in this position.");
                session.Move(action.Move);break;
            case "undo":if(before.PendingCount==0)throw new ApiError(409,"There is no pending move to undo.");session.Undo();break;
            case "submit":
                if(!before.CanSubmit)throw new ApiError(400,"Move on the required boards and resolve every check before submitting.");
                session.Submit();break;
            default:throw new ApiError(400,"Unknown game action.");
        }
        long now=Now,elapsed=Math.Max(0,now-game.TurnStarted!.Value);
        // Validation consumes time too. Never accept a move after its clock expired.
        if(elapsed>=(game.WhiteToMove?game.WhiteMs:game.BlackMs)){await Expire(game,match);return;}
        if(game.WhiteToMove)game.WhiteMs-=elapsed;else game.BlackMs-=elapsed;
        if(action.Action=="submit"){
            if(game.WhiteToMove)game.WhiteMs+=match.Increment*1000L;else game.BlackMs+=match.Increment*1000L;
            game.DrawOffer=null;
        }
        var position=session.Snapshot();game.WhiteToMove=position.White;game.TurnStarted=now;game.Pgn=session.Export();game.Version++;
        if(position.Result!="*")await Finish(game,match,position.Result,position.Status);
    }
    public async Task Finish(OnlineGame game,Match match,string result,string reason){
        if(game.Status=="finished")return;
        if(game.Status=="active"&&game.TurnStarted is {} started){
            long elapsed=Math.Max(0,Now-started);
            if(game.WhiteToMove)game.WhiteMs=Math.Max(0,game.WhiteMs-elapsed);else game.BlackMs=Math.Max(0,game.BlackMs-elapsed);
        }
        var session=rules.Create(game.Pgn);session.End(result);game.Pgn=session.Export();
        game.Status="finished";game.Result=result;game.Reason=reason;game.TurnStarted=null;game.Finished=Now;game.Version++;
        double white=result=="1-0"?1:result=="0-1"?0:.5;
        if(match.Rated&&!game.Rated&&reason!="forfeit"){
            string category=new TimeControl(match.Seconds,match.Increment).Category;
            var a=await GetRating(game.WhiteId,match.Variant,category);var b=await GetRating(game.BlackId,match.Variant,category);
            RatingState Before(Rating r)=>Glicko2.Age(new(r.Value,r.Deviation,r.Volatility),r.Updated==0?0:Math.Max(0,(Now-r.Updated)/86400000d-1));
            var oldA=Before(a);var oldB=Before(b);
            ApplyRating(a,Glicko2.Update(oldA,[new(oldB,white)]),game.Id);ApplyRating(b,Glicko2.Update(oldB,[new(oldA,1-white)]),game.Id);
            game.Rated=true;
        }
        if(reason!="forfeit"&&match.TournamentId!=null&&(await db.Tournaments.FindAsync(match.TournamentId)) is {League:true}){
            var a=await db.LeaguePlayers.SingleAsync(x=>x.UserId==game.WhiteId);var b=await db.LeaguePlayers.SingleAsync(x=>x.UserId==game.BlackId);
            double change=25*(white-1/(1+Math.Pow(10,(b.Elo-a.Elo)/400)));
            double oldA=a.Elo,oldB=b.Elo;a.Elo+=change;b.Elo-=change;
            if(white==1){a.Wins++;b.Losses++;}else if(white==0){b.Wins++;a.Losses++;}else{a.Draws++;b.Draws++;}
            db.RatingChanges.AddRange(new RatingChange{GameId=game.Id,UserId=game.WhiteId,Pool="league",Before=oldA,After=a.Elo,At=Now},new RatingChange{GameId=game.Id,UserId=game.BlackId,Pool="league",Before=oldB,After=b.Elo,At=Now});
        }
        var games=await db.Games.Where(g=>g.MatchId==match.Id).ToListAsync();
        match.Score1=games.Where(g=>g.Result!="*").Sum(g=>g.Result=="1/2-1/2"?.5:(g.Result=="1-0"?g.WhiteId:g.BlackId)==match.Player1Id?1:0);
        match.Score2=games.Count(g=>g.Result!="*")-match.Score1;
        if(games.Count==2&&games.All(g=>g.Status=="finished")){
            match.Status="finished";
            match.WinnerId=match.Score1>match.Score2?match.Player1Id:match.Score2>match.Score1?match.Player2Id:null;
        }
    }
    public async Task<Rating> GetRating(string user,string variant,string category){
        var row=await db.Ratings.FindAsync(user,variant,category);
        if(row!=null)return row;
        row=new(){UserId=user,Variant=variant,Category=category};db.Ratings.Add(row);return row;
    }
    private void ApplyRating(Rating rating,RatingState next,string game){
        db.RatingChanges.Add(new(){GameId=game,UserId=rating.UserId,Pool=rating.Variant+"/"+rating.Category,Before=rating.Value,After=next.Rating,At=Now});
        rating.Value=next.Rating;rating.Deviation=next.Deviation;rating.Volatility=next.Volatility;rating.Games++;rating.Updated=Now;
    }
}
