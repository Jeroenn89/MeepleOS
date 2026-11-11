using Microsoft.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Data;

public sealed class MeepleDbContext(DbContextOptions<MeepleDbContext> options) : DbContext(options)
{
    public DbSet<Boardgame> Boardgames => Set<Boardgame>();
    public DbSet<Player> Players => Set<Player>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Boardgame>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Player>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Player>().Property(x => x.NormalizedName).HasMaxLength(200);
        modelBuilder.Entity<Player>().HasIndex(x => x.NormalizedName).IsUnique();
    }
}
