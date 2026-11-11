using Microsoft.EntityFrameworkCore;
using MeepleOS.DataLayer.Data.Models;

namespace MeepleOS.DataLayer.Data.Context;

public sealed class MeepleDbContext(DbContextOptions<MeepleDbContext> options) : DbContext(options)
{
    public DbSet<Boardgame> Boardgames => Set<Boardgame>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<SessionPlayer> SessionPlayers => Set<SessionPlayer>();
    public DbSet<GameExpansion> GameExpansions => Set<GameExpansion>();
    public DbSet<GameSessionExpansion> GameSessionExpansions => Set<GameSessionExpansion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Boardgame>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Player>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Player>().Property(x => x.NormalizedName).HasMaxLength(200);
        modelBuilder.Entity<Player>().HasIndex(x => x.NormalizedName).IsUnique();

        modelBuilder.Entity<GameSession>().HasOne(x => x.BoardGame).WithMany()
            .HasForeignKey(x => x.BoardGameId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GameSession>().HasIndex(x => new { x.BoardGameId, x.PlayedAt });
        modelBuilder.Entity<GameSession>().HasIndex(x => x.PlayedAt);

        modelBuilder.Entity<SessionPlayer>().HasOne(x => x.GameSession).WithMany(x => x.Players)
            .HasForeignKey(x => x.GameSessionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SessionPlayer>().HasOne(x => x.Player).WithMany()
            .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SessionPlayer>().HasIndex(x => new { x.GameSessionId, x.PlayerId }).IsUnique();
        modelBuilder.Entity<SessionPlayer>().HasIndex(x => x.PlayerId);

        modelBuilder.Entity<GameExpansion>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<GameExpansion>().Property(x => x.NormalizedName).HasMaxLength(200);
        modelBuilder.Entity<GameExpansion>().HasIndex(x => new { x.BoardGameId, x.NormalizedName }).IsUnique();
        modelBuilder.Entity<GameExpansion>().HasOne(x => x.BoardGame).WithMany()
            .HasForeignKey(x => x.BoardGameId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GameSessionExpansion>().HasKey(x => new { x.GameSessionId, x.GameExpansionId });
        modelBuilder.Entity<GameSessionExpansion>().HasOne(x => x.GameSession).WithMany(x => x.Expansions)
            .HasForeignKey(x => x.GameSessionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GameSessionExpansion>().HasOne(x => x.GameExpansion).WithMany()
            .HasForeignKey(x => x.GameExpansionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GameSessionExpansion>().HasIndex(x => x.GameExpansionId);
    }
}
