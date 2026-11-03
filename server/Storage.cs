using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace SkynetZero.Server;

public sealed class Player : IdentityUser {
    public string? DiscordId {get;set;}
    public string? DiscordUsername {get;set;}
    public bool IsAdmin {get;set;}
    public bool Suspended {get;set;}
    public long Created {get;set;}=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
public sealed class Rating {
    public string UserId {get;set;}="";
    public string Variant {get;set;}="";
    public string Category {get;set;}="";
    public double Value {get;set;}=1500;
    public double Deviation {get;set;}=350;
    public double Volatility {get;set;}=.06;
    public int Games {get;set;}
    public long Updated {get;set;}
}
public sealed class RatingChange {
    public string GameId {get;set;}="";
    public string UserId {get;set;}="";
    public string Pool {get;set;}="";
    public double Before {get;set;}
    public double After {get;set;}
    public long At {get;set;}
}
public sealed class LeaguePlayer {
    public string DiscordId {get;set;}="";
    public string? UserId {get;set;}
    public double Elo {get;set;}=1380;
    public int Wins {get;set;}
    public int Losses {get;set;}
    public int Draws {get;set;}
    public int SeasonsMissed {get;set;}
    public bool Legacy {get;set;}
}
public sealed class LegacyRecord {
    public string Table {get;set;}="";
    public string SourceId {get;set;}="";
    public string Json {get;set;}="";
}
public sealed class Challenge {
    public string Id {get;set;}=Ids.New();
    public string CreatorId {get;set;}="";
    public string? OpponentId {get;set;}
    public string Variant {get;set;}="standard-t0";
    public int Seconds {get;set;}=300;
    public int Increment {get;set;}=3;
    public bool Rated {get;set;}=true;
    public bool Private {get;set;}
    public long Created {get;set;}
    public string? MatchId {get;set;}
    public bool Cancelled {get;set;}
}
public sealed class Match {
    public string Id {get;set;}=Ids.New();
    public string Player1Id {get;set;}="";
    public string Player2Id {get;set;}="";
    public string Variant {get;set;}="standard-t0";
    public int Seconds {get;set;}
    public int Increment {get;set;}
    public bool Rated {get;set;}
    public bool Private {get;set;}
    public string Status {get;set;}="waiting";
    public double Score1 {get;set;}
    public double Score2 {get;set;}
    public string? TournamentId {get;set;}
    public int Round {get;set;}
    public string Group {get;set;}="";
    public string? WinnerId {get;set;}
    public bool Bye {get;set;}
    public long Created {get;set;}
}
public sealed class OnlineGame {
    public string Id {get;set;}=Ids.New();
    public string MatchId {get;set;}="";
    public int Number {get;set;}
    public string WhiteId {get;set;}="";
    public string BlackId {get;set;}="";
    public string Pgn {get;set;}="";
    public string Status {get;set;}="waiting";
    public string Result {get;set;}="*";
    public string? Reason {get;set;}
    public bool WhiteToMove {get;set;}=true;
    public long WhiteMs {get;set;}
    public long BlackMs {get;set;}
    public long? TurnStarted {get;set;}
    public long Version {get;set;}
    public bool WhiteReady {get;set;}
    public bool BlackReady {get;set;}
    public string? DrawOffer {get;set;}
    public bool Rated {get;set;}
    public long? Finished {get;set;}
}
public sealed class Study {
    public string Id {get;set;}=Ids.New();
    public string OwnerId {get;set;}="";
    public string Title {get;set;}="";
    public string Description {get;set;}="";
    public bool Public {get;set;}
    public string Chapters {get;set;}="[]";
    public long Version {get;set;}=1;
    public long Updated {get;set;}
}
public sealed class DirectMessage {
    public string Id {get;set;}=Ids.New();
    public string FromId {get;set;}="";
    public string ToId {get;set;}="";
    public string Body {get;set;}="";
    public long Sent {get;set;}
    public bool Read {get;set;}
}
public sealed class Block {
    public string UserId {get;set;}="";
    public string OtherId {get;set;}="";
}
public sealed class Tournament {
    public string Id {get;set;}=Ids.New();
    public string Name {get;set;}="";
    public string OwnerId {get;set;}="";
    public string Format {get;set;}="round-robin";
    public string Variant {get;set;}="standard-t0";
    public int Seconds {get;set;}=900;
    public int Increment {get;set;}=10;
    public bool Rated {get;set;}=true;
    public bool Public {get;set;}=true;
    public bool League {get;set;}
    public string Status {get;set;}="registration";
    public int Round {get;set;}
    public int Rounds {get;set;}=5;
    public string Scoring {get;set;}="game";
    public double WinPoints {get;set;}=1;
    public double DrawPoints {get;set;}=.5;
    public double LossPoints {get;set;}
    public string TieBreak {get;set;}="seed";
    public long? StartsAt {get;set;}
    public string? ChampionId {get;set;}
    public long Created {get;set;}
}
public sealed class Entrant {
    public string TournamentId {get;set;}="";
    public string UserId {get;set;}="";
    public int Seed {get;set;}
    public double SeedRating {get;set;}
    public string Group {get;set;}="";
    public long Joined {get;set;}
}
public sealed class AuditEntry {
    public long Id {get;set;}
    public string Actor {get;set;}="";
    public string Action {get;set;}="";
    public string Subject {get;set;}="";
    public string Detail {get;set;}="";
    public long At {get;set;}
}
public sealed class PlatformDb(DbContextOptions<PlatformDb> options) : IdentityDbContext<Player>(options) {
    public DbSet<Rating> Ratings=>Set<Rating>();
    public DbSet<RatingChange> RatingChanges=>Set<RatingChange>();
    public DbSet<LeaguePlayer> LeaguePlayers=>Set<LeaguePlayer>();
    public DbSet<LegacyRecord> LegacyRecords=>Set<LegacyRecord>();
    public DbSet<Challenge> Challenges=>Set<Challenge>();
    public DbSet<Match> Matches=>Set<Match>();
    public DbSet<OnlineGame> Games=>Set<OnlineGame>();
    public DbSet<Study> Studies=>Set<Study>();
    public DbSet<DirectMessage> Messages=>Set<DirectMessage>();
    public DbSet<Block> Blocks=>Set<Block>();
    public DbSet<Tournament> Tournaments=>Set<Tournament>();
    public DbSet<Entrant> Entrants=>Set<Entrant>();
    public DbSet<AuditEntry> Audit=>Set<AuditEntry>();
    protected override void OnModelCreating(ModelBuilder b){
        base.OnModelCreating(b);
        b.Entity<Player>().HasIndex(x=>x.DiscordId).IsUnique();
        b.Entity<Rating>().HasKey(x=>new{x.UserId,x.Variant,x.Category});
        b.Entity<RatingChange>().HasKey(x=>new{x.GameId,x.UserId,x.Pool});
        b.Entity<LeaguePlayer>().HasKey(x=>x.DiscordId);
        b.Entity<LeaguePlayer>().HasIndex(x=>x.UserId).IsUnique();
        b.Entity<LegacyRecord>().HasKey(x=>new{x.Table,x.SourceId});
        b.Entity<Block>().HasKey(x=>new{x.UserId,x.OtherId});
        b.Entity<Entrant>().HasKey(x=>new{x.TournamentId,x.UserId});
        b.Entity<OnlineGame>().HasIndex(x=>new{x.MatchId,x.Number}).IsUnique();
        b.Entity<OnlineGame>().Property(x=>x.Version).IsConcurrencyToken();
        b.Entity<Study>().Property(x=>x.Version).IsConcurrencyToken();
        b.Entity<DirectMessage>().HasIndex(x=>new{x.ToId,x.Sent});
        b.Entity<DirectMessage>().HasIndex(x=>new{x.FromId,x.Sent});
        b.Entity<Match>().HasIndex(x=>new{x.TournamentId,x.Round});
        b.Entity<Challenge>().HasIndex(x=>x.Created);
        b.Entity<Rating>().HasOne<Player>().WithMany().HasForeignKey(x=>x.UserId);
        b.Entity<Entrant>().HasOne<Tournament>().WithMany().HasForeignKey(x=>x.TournamentId);
        b.Entity<OnlineGame>().HasOne<Match>().WithMany().HasForeignKey(x=>x.MatchId);
    }
}
public static class Ids {public static string New()=>Guid.NewGuid().ToString("N");}
public sealed class WriteGate {public SemaphoreSlim Lock {get;}=new(1,1);}
