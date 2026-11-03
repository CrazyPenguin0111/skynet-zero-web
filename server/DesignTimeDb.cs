using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace SkynetZero.Server;
public sealed class DesignTimeDb : IDesignTimeDbContextFactory<PlatformDb> {
    public PlatformDb CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<PlatformDb>().UseSqlite("Data Source=design-time.db").Options);
}
