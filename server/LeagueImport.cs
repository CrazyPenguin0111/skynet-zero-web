using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace SkynetZero.Server;

public static class LeagueImport {
    public static async Task<(int Players,int Records)> Run(PlatformDb db,string source){
        // Never migrate or modify the Discord bot's original database.
        await using var input=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(source),Mode=SqliteOpenMode.ReadOnly}.ToString());
        await input.OpenAsync();await using var transaction=await db.Database.BeginTransactionAsync();
        int players=0,records=0;
        using(var command=input.CreateCommand()){
            command.CommandText="SELECT id,elo,wins,losses,draws,seasons_missed FROM players";
            using var reader=await command.ExecuteReaderAsync();
            while(await reader.ReadAsync()){
                string id=reader.GetInt64(0).ToString(CultureInfo.InvariantCulture);
                if(await db.LeaguePlayers.FindAsync(id)!=null)continue;
                double elo=reader.GetDouble(1);if(!double.IsFinite(elo))throw new InvalidDataException("A legacy rating is not finite.");
                db.LeaguePlayers.Add(new(){DiscordId=id,Elo=elo,Wins=reader.GetInt32(2),Losses=reader.GetInt32(3),Draws=reader.GetInt32(4),SeasonsMissed=reader.GetInt32(5),Legacy=true});players++;
            }
        }
        foreach(string table in new[]{"players","seasons","match_history","pairings","elo_history","player_aliases"}){
            using var command=input.CreateCommand();command.CommandText=$"SELECT * FROM {table}";
            using var reader=await command.ExecuteReaderAsync();
            while(await reader.ReadAsync()){
                // Preserve numeric identifiers as strings so JavaScript cannot round Discord snowflakes.
                string id=Convert.ToString(reader.GetValue(0),CultureInfo.InvariantCulture)!;
                if(await db.LegacyRecords.FindAsync(table,id)!=null)continue;
                var fields=Enumerable.Range(0,reader.FieldCount).ToDictionary(reader.GetName,i=>reader.IsDBNull(i)?null:Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture));
                db.LegacyRecords.Add(new(){Table=table,SourceId=id,Json=JsonSerializer.Serialize(fields)});records++;
            }
        }
        db.Audit.Add(new(){Actor="operator",Action="legacy-import",Subject="league",Detail=$"Imported {players} players and {records} historical rows; existing rows unchanged.",At=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});
        await db.SaveChangesAsync();await transaction.CommitAsync();return(players,records);
    }
    public static async Task Claim(PlatformDb db,Player player,string verifiedId,string verifiedUsername){
        if(!System.Text.RegularExpressions.Regex.IsMatch(verifiedId,@"^\d{15,22}$"))throw new ApiError(400,"Discord returned an invalid account ID.");
        if(player.DiscordId!=null&&player.DiscordId!=verifiedId)throw new ApiError(409,"This site account is already connected to another Discord account.");
        if(await db.Users.AnyAsync(u=>u.DiscordId==verifiedId&&u.Id!=player.Id))throw new ApiError(409,"That Discord account is already linked to another site account.");
        var league=await db.LeaguePlayers.FindAsync(verifiedId);
        if(league?.UserId!=null&&league.UserId!=player.Id)throw new ApiError(409,"That league record is already claimed.");
        if(league==null){league=new(){DiscordId=verifiedId};db.LeaguePlayers.Add(league);}
        league.UserId=player.Id;player.DiscordId=verifiedId;player.DiscordUsername=verifiedUsername;
        db.Audit.Add(new(){Actor=player.Id,Action="discord-claim",Subject=verifiedId,Detail=league.Legacy?"Claimed imported league record by verified Discord ID.":"Connected Discord; new league rating 1380.",At=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});
    }
}
