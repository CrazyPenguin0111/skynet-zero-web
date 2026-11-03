namespace SkynetZero.Web;

public sealed record VariantInfo(string Id,string Name,string Description,bool Available=true);
public sealed record TimeControl(int Seconds,int Increment) {
    public string Category => (Seconds+40*Increment) switch { <180=>"bullet", <480=>"blitz", <1500=>"rapid", _=>"classical" };
    public string Label => $"{Seconds/60d:0.##}+{Increment}";
    public bool Valid => Seconds is >=30 and <=10800 && Increment is >=0 and <=180;
}
public sealed record UserView(string Id,string Username,bool Admin,string? DiscordUsername,double LeagueRating,bool LeagueClaimed);
public sealed record RatingView(string Variant,string Category,double Rating,double Deviation,int Games);
public sealed record ProfileView(UserView User,RatingView[] Ratings,int LeagueWins,int LeagueLosses,int LeagueDraws);
public sealed record SiteInfo(bool Online,bool DiscordEnabled,VariantInfo[] Variants,string[] Formats,int GamesPerMatch=2){public bool NativeSearch { get; init; }}
public sealed record SessionView(UserView? User,string CsrfToken);
public sealed record ChallengeView(string Id,string Creator,string Variant,int Seconds,int Increment,bool Rated,bool Private,string? Opponent,long Created);
public sealed record MatchView(string Id,string Player1,string Player2,string Variant,string Category,bool Rated,string Status,double Score1,double Score2,string[] Games,string? TournamentId);
public sealed record LiveGameView(string Id,string MatchId,string WhiteId,string WhiteName,string BlackId,string BlackName,string Variant,string Category,int Increment,bool Rated,string Status,string Result,string? Reason,long WhiteMs,long BlackMs,long ServerTime,long? StartedAt,long Version,string? DrawOffer,string[] Ready,PositionView Position);
public sealed record LobbyView(ChallengeView[] Challenges,MatchView[] Matches);
public sealed record StudyChapter(string Id,string Title,string Pgn,string Notes);
public sealed record StudyView(string Id,string OwnerId,string Owner,string Title,string Description,bool Public,long Version,StudyChapter[] Chapters,long Updated);
public sealed record MessageView(string Id,string FromId,string From,string ToId,string To,string Body,long Sent,bool Read);
public sealed record ConversationView(string UserId,string Username,string Preview,long Updated,int Unread,bool Blocked);
public sealed record StandingView(string UserId,string Username,string Group,double Points,double GamePoints,double SonnebornBerger,double Buchholz,int Played,int Wins,int Losses,int Seed);
public sealed record TournamentMatchView(string Id,int Round,string Group,string Player1Id,string Player1,string Player2Id,string Player2,double Score1,double Score2,string Status,string[] Games,string? WinnerId,bool Bye);
public sealed record TournamentView(string Id,string Name,string OwnerId,string Format,string Variant,int Seconds,int Increment,bool Rated,bool Public,bool League,string Status,int Round,int Rounds,string Scoring,double WinPoints,double DrawPoints,double LossPoints,string TieBreak,long? StartsAt,string? ChampionId,int Entrants,bool Joined);
public sealed record TournamentDetail(TournamentView Tournament,StandingView[] Standings,TournamentMatchView[] Matches);
public sealed record LeaderboardEntry(string UserId,string Username,double Rating,double Deviation,int Games);
public sealed record LeagueSummary(int ImportedPlayers,int ClaimedPlayers,int HistoricalGames,TournamentView[] Seasons);
public sealed record AccountRequest(string Username,string Password);
public sealed record ChallengeRequest(string Variant,int Seconds,int Increment,bool Rated=true,bool Private=false,string? Opponent=null);
public sealed record GameActionRequest(string Action,long Version,string? Move=null);
public sealed record StudyRequest(string Title,string Description,bool Public,StudyChapter[] Chapters,long Version=0);
public sealed record MessageRequest(string Username,string Body);
public sealed record TournamentRequest(string Name,string Format,string Variant,int Seconds,int Increment,bool Rated=true,bool Public=true,bool League=false,int Rounds=5,string Scoring="game",double WinPoints=1,double DrawPoints=.5,double LossPoints=0,string TieBreak="seed",long? StartsAt=null);
