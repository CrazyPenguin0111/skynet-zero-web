using Microsoft.EntityFrameworkCore;
using SkynetZero.Web;
namespace SkynetZero.Server;

public sealed class TournamentService(PlatformDb db,GameService games){
    public static readonly string[] Formats=["round-robin","swiss","single-elimination","double-elimination","groups-knockout"];
    public static string LeagueBand(double elo)=>elo>=1550?"Pro League":elo>=1410?"Advanced League":"Entry League";
    public static int[] GroupSizes(int count){
        if(count<=7)return[count];int groups=(int)Math.Ceiling(count/6d);
        return Enumerable.Range(0,groups).Select(i=>count/groups+(i<count%groups?1:0)).ToArray();
    }
    public async Task<TournamentView> View(Tournament t,string? user)=>new(t.Id,t.Name,t.OwnerId,t.Format,t.Variant,t.Seconds,t.Increment,t.Rated,t.Public,t.League,t.Status,t.Round,t.Rounds,t.Scoring,t.WinPoints,t.DrawPoints,t.LossPoints,t.TieBreak,t.StartsAt,t.ChampionId,await db.Entrants.CountAsync(e=>e.TournamentId==t.Id),user!=null&&await db.Entrants.AnyAsync(e=>e.TournamentId==t.Id&&e.UserId==user));
    public async Task<TournamentDetail> Detail(Tournament t,string? user){
        var standings=await Standings(t);var matches=await db.Matches.Where(m=>m.TournamentId==t.Id).OrderBy(m=>m.Round).ThenBy(m=>m.Group).ThenBy(m=>m.Id).ToArrayAsync();
        var names=await db.Users.ToDictionaryAsync(u=>u.Id,u=>u.UserName!);var rows=new List<TournamentMatchView>();
        foreach(var m in matches)rows.Add(new(m.Id,m.Round,m.Group,m.Player1Id,names.GetValueOrDefault(m.Player1Id,"Bye"),m.Player2Id,names.GetValueOrDefault(m.Player2Id,"Bye"),m.Score1,m.Score2,m.Status,await db.Games.Where(g=>g.MatchId==m.Id).OrderBy(g=>g.Number).Select(g=>g.Id).ToArrayAsync(),m.WinnerId,m.Bye));
        return new(await View(t,user),standings,rows.ToArray());
    }
    public async Task<StandingView[]> Standings(Tournament t){
        var entrants=await db.Entrants.Where(e=>e.TournamentId==t.Id).ToArrayAsync();
        var matches=await db.Matches.Where(m=>m.TournamentId==t.Id&&m.Status=="finished").ToArrayAsync();
        var names=await db.Users.Where(u=>entrants.Select(e=>e.UserId).Contains(u.Id)).ToDictionaryAsync(u=>u.Id,u=>u.UserName!);
        var rows=entrants.Select(e=>{
            var played=matches.Where(m=>m.Player1Id==e.UserId||m.Player2Id==e.UserId).ToArray();
            double GamePoints(Match m)=>m.Player1Id==e.UserId?m.Score1:m.Score2;
            double points=played.Sum(m=>t.Scoring=="game"?GamePoints(m):GamePoints(m)>1?t.WinPoints:GamePoints(m)<1?t.LossPoints:t.DrawPoints);
            return new StandingView(e.UserId,names[e.UserId],e.Group,points,played.Sum(GamePoints),0,0,played.Count(m=>!m.Bye),played.Count(m=>!m.Bye&&m.WinnerId==e.UserId),played.Count(m=>!m.Bye&&m.WinnerId!=null&&m.WinnerId!=e.UserId),e.Seed);
        }).ToArray();
        var lookup=rows.ToDictionary(r=>r.UserId);
        return rows.Select(row=>{
            double sb=0,buchholz=0;
            foreach(var m in matches.Where(m=>!m.Bye&&(m.Player1Id==row.UserId||m.Player2Id==row.UserId))){
                var opponent=lookup[m.Player1Id==row.UserId?m.Player2Id:m.Player1Id];double score=m.Player1Id==row.UserId?m.Score1:m.Score2;
                sb+=opponent.Points*score/2;buchholz+=opponent.Points;
            }
            return row with {SonnebornBerger=sb,Buchholz=buchholz};
        }).OrderBy(r=>r.Group).ThenByDescending(r=>r.Points).ThenByDescending(r=>r.SonnebornBerger).ThenByDescending(r=>r.Buchholz).ThenBy(r=>r.Seed).ToArray();
    }
    public async Task Start(Tournament t){
        if(t.Status!="registration")throw new ApiError(409,"Registration has already closed.");
        var entrants=await db.Entrants.Where(e=>e.TournamentId==t.Id).ToListAsync();
        if(entrants.Count<2)throw new ApiError(409,"At least two players must join.");
        if(entrants.Count>128)throw new ApiError(400,"A tournament supports at most 128 players.");
        foreach(var e in entrants){
            if(t.League)e.SeedRating=(await db.LeaguePlayers.SingleOrDefaultAsync(p=>p.UserId==e.UserId))?.Elo??throw new ApiError(409,"League entrants must link Discord first.");
            else e.SeedRating=(await games.GetRating(e.UserId,t.Variant,new TimeControl(t.Seconds,t.Increment).Category)).Value;
        }
        entrants=entrants.OrderByDescending(e=>e.SeedRating).ThenBy(e=>e.Joined).ThenBy(e=>e.UserId).ToList();
        for(int i=0;i<entrants.Count;i++)entrants[i].Seed=i+1;
        t.Status="active";t.Round=1;t.StartsAt=null;
        if(t.League||t.Format=="groups-knockout"){
            var bands=t.League?entrants.GroupBy(e=>LeagueBand(e.SeedRating)).ToDictionary(g=>g.Key,g=>g.ToList()):new Dictionary<string,List<Entrant>>{{"Group",entrants}};
            int maxRounds=0;
            foreach(var (name,players) in bands){
                var shuffled=players.OrderBy(_=>System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray();
                int[] sizes=GroupSizes(players.Count);int offset=0;
                for(int i=0;i<sizes.Length;i++){
                    string group=sizes.Length==1?name:$"{name} {i+1}";var subset=shuffled.Skip(offset).Take(sizes[i]).ToArray();offset+=sizes[i];
                    foreach(var e in subset)e.Group=group;
                    maxRounds=Math.Max(maxRounds,await RoundRobin(t,subset,group));
                }
            }
            t.Rounds=maxRounds;
        }else if(t.Format=="round-robin")t.Rounds=await RoundRobin(t,entrants.ToArray(),"");
        else if(t.Format=="swiss")await SwissRound(t,entrants,[]);
        else await EliminationRound(t,entrants,[]);
        await db.SaveChangesAsync();
        await Advance(t);
    }
    private async Task<int> RoundRobin(Tournament t,Entrant[] entrants,string group){
        if(entrants.Length<2)return 0;
        var circle=entrants.Select(e=>(string?)e.UserId).ToList();if(circle.Count%2==1)circle.Add(null);
        int count=circle.Count;
        for(int round=1;round<count;round++){
            for(int i=0;i<count/2;i++){
                string? a=circle[i],b=circle[count-1-i];
                // Rest rounds in round robin are not wins and do not affect ratings.
                if(a==null||b==null)continue;
                if((round+i)%2==0)(a,b)=(b,a);
                await Pair(t,a,b,round,group);
            }
            string? last=circle[^1];circle.RemoveAt(circle.Count-1);circle.Insert(1,last);
        }
        return count-1;
    }
    private Task<Match> Pair(Tournament t,string a,string b,int round,string group="")=>games.CreateMatch(a,b,t.Variant,t.Seconds,t.Increment,t.Rated,!t.Public,t,round,group);
    private void Bye(Tournament t,Entrant entrant,string group="")=>db.Matches.Add(new(){TournamentId=t.Id,Player1Id=entrant.UserId,Player2Id="",Variant=t.Variant,Seconds=t.Seconds,Increment=t.Increment,Rated=false,Private=!t.Public,Round=t.Round,Group=group,Status="finished",Bye=true,WinnerId=entrant.UserId,Score1=2,Score2=0,Created=games.Now});
    private async Task SwissRound(Tournament t,List<Entrant> entrants,Match[] previous){
        var standings=await Standings(t);var ranks=standings.Select((s,i)=>(s.UserId,i)).ToDictionary(x=>x.UserId,x=>x.i);
        var ordered=entrants.OrderBy(e=>ranks.GetValueOrDefault(e.UserId,e.Seed)).ToList();
        if(ordered.Count%2==1){
            var bye=ordered.AsEnumerable().Reverse().FirstOrDefault(e=>!previous.Any(m=>m.Bye&&m.Player1Id==e.UserId))??ordered[^1];
            Bye(t,bye);ordered.Remove(bye);
        }
        var opponents=previous.Where(m=>!m.Bye).SelectMany(m=>new[]{m.Player1Id+"/"+m.Player2Id,m.Player2Id+"/"+m.Player1Id}).ToHashSet();
        // Backtracking avoids a greedy last-pair rematch. Only allow repeats if no
        // complete non-repeating pairing exists (or the bounded search is exhausted).
        List<(Entrant,Entrant)>? Solve(List<Entrant> left,bool repeats,ref int budget){
            if(left.Count==0)return[];if(--budget<0)return null;
            var a=left[0];
            foreach(var b in left.Skip(1).Where(b=>repeats||!opponents.Contains(a.UserId+"/"+b.UserId))){
                var tail=Solve(left.Where(e=>e!=a&&e!=b).ToList(),repeats,ref budget);
                if(tail!=null){tail.Insert(0,(a,b));return tail;}
            }
            return null;
        }
        int work=100000;var pairs=Solve(ordered,false,ref work);if(pairs==null){work=100000;pairs=Solve(ordered,true,ref work)!;}
        foreach(var(a,b) in pairs)await Pair(t,a.UserId,b.UserId,t.Round);
    }
    private async Task EliminationRound(Tournament t,List<Entrant> entrants,Match[] previous){
        int Losses(Entrant e)=>previous.Count(m=>!m.Bye&&m.WinnerId!=null&&m.WinnerId!=e.UserId&&(m.Player1Id==e.UserId||m.Player2Id==e.UserId));
        int limit=t.Format=="double-elimination"?2:1;
        var remaining=entrants.Where(e=>Losses(e)<limit).OrderBy(e=>e.Seed).ToList();
        if(remaining.Count==1){t.Status="finished";t.ChampionId=remaining[0].UserId;return;}
        if(t.Format=="double-elimination"&&remaining.Count==2){await Pair(t,remaining[0].UserId,remaining[1].UserId,t.Round,"Final");return;}
        foreach(var band in remaining.GroupBy(e=>limit==2?Losses(e):0)){
            var pool=band.ToList();string group=limit==2?(band.Key==0?"Upper bracket":"Lower bracket"):"Knockout";
            if(pool.Count%2==1){
                var bye=pool.OrderBy(e=>previous.Count(m=>m.Bye&&m.Player1Id==e.UserId)).ThenBy(e=>e.Seed).First();
                Bye(t,bye,group);pool.Remove(bye);
            }
            while(pool.Count>1){var a=pool[0];var b=pool[^1];pool.RemoveAt(pool.Count-1);pool.RemoveAt(0);await Pair(t,a.UserId,b.UserId,t.Round,group);}
        }
    }
    public async Task Advance(Tournament t){
        if(t.Status!="active")return;
        var matches=await db.Matches.Where(m=>m.TournamentId==t.Id).ToArrayAsync();
        var entrants=await db.Entrants.Where(e=>e.TournamentId==t.Id).ToListAsync();
        bool knockout=t.Format is "single-elimination" or "double-elimination" || t.Format=="groups-knockout"&&matches.Any(m=>m.Group=="Knockout");
        if(knockout){
            foreach(var m in matches.Where(m=>m.Status=="finished"&&!m.Bye&&m.WinnerId==null&&(t.Format!="groups-knockout"||m.Group=="Knockout"))){
                if(t.TieBreak=="organizer")continue;
                var seeds=entrants.Where(e=>e.UserId==m.Player1Id||e.UserId==m.Player2Id).OrderBy(e=>e.Seed).ToArray();m.WinnerId=seeds[0].UserId;
                db.Audit.Add(new(){Actor="system",Action="seed-tiebreak",Subject=m.Id,Detail="Two-game match tied; higher pre-tournament seed advances.",At=games.Now});
            }
            if(matches.Any(m=>m.Status!="finished"||(!m.Bye&&m.WinnerId==null&&(t.Format!="groups-knockout"||m.Group=="Knockout"))))return;
            t.Round++;
            if(t.Format=="groups-knockout"){
                var qualified=entrants.Where(e=>matches.Any(m=>m.Group=="Knockout"&&(m.Player1Id==e.UserId||m.Player2Id==e.UserId))).ToList();
                await EliminationRound(t,qualified,matches.Where(m=>m.Group=="Knockout").ToArray());
            }else await EliminationRound(t,entrants,matches);
        }else if(t.Format=="swiss"){
            if(matches.Any(m=>m.Status!="finished"))return;
            if(t.Round>=t.Rounds){t.Status="finished";t.ChampionId=(await Standings(t)).FirstOrDefault()?.UserId;}
            else{t.Round++;await SwissRound(t,entrants,matches);}
        }else{
            if(matches.Any(m=>m.Status!="finished")){t.Round=matches.Where(m=>m.Status!="finished").Min(m=>m.Round);return;}
            if(t.Format=="groups-knockout"){
                var qualified=(await Standings(t)).GroupBy(s=>s.Group).SelectMany(g=>g.Take(2)).Select(s=>entrants.Single(e=>e.UserId==s.UserId)).ToList();
                t.Round=t.Rounds+1;await EliminationRound(t,qualified,[]);
            }else{t.Status="finished";t.ChampionId=t.League?null:(await Standings(t)).FirstOrDefault()?.UserId;}
        }
    }
}
