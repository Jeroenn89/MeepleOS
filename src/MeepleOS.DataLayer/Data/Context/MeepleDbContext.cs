using Microsoft.EntityFrameworkCore;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Data.Context;

public sealed class MeepleDbContext(DbContextOptions<MeepleDbContext> options) : IdentityDbContext<MeepleUser>(options)
{
    public DbSet<AccountBoardgame> AccountBoardgames => Set<AccountBoardgame>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<AccountPlayer> AccountPlayers => Set<AccountPlayer>();
    public DbSet<AccountGameSession> AccountGameSessions => Set<AccountGameSession>();
    public DbSet<Boardgame> Boardgames => Set<Boardgame>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<SessionPlayer> SessionPlayers => Set<SessionPlayer>();
    public DbSet<GameExpansion> GameExpansions => Set<GameExpansion>();
    public DbSet<GameSessionExpansion> GameSessionExpansions => Set<GameSessionExpansion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ActivityLog>().HasOne(x => x.Account).WithMany()
            .HasForeignKey(x => x.AccountReferenceId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ActivityLog>().Property(x => x.CreatedAt)
            .HasConversion(value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        modelBuilder.Entity<ActivityLog>().HasIndex(x => new { x.CreatedAt, x.Id });
        modelBuilder.Entity<ActivityLog>().HasIndex(x => new { x.AccountId, x.CreatedAt });
        modelBuilder.Entity<ActivityLog>().HasIndex(x => new { x.ToolName, x.CreatedAt });
        modelBuilder.Entity<ActivityLog>().HasIndex(x => new { x.RequestId, x.AccountId });
        modelBuilder.Entity<AccountBoardgame>().HasKey(x => new { x.AccountId, x.BoardGameId });
        modelBuilder.Entity<AccountBoardgame>().HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AccountBoardgame>().HasOne(x => x.BoardGame).WithMany().HasForeignKey(x => x.BoardGameId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AccountBoardgame>().HasIndex(x => x.BoardGameId);
        modelBuilder.Entity<AccountPlayer>().HasKey(x => new { x.AccountId, x.PlayerId });
        modelBuilder.Entity<AccountPlayer>().HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AccountPlayer>().HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AccountPlayer>().HasIndex(x => x.PlayerId);
        modelBuilder.Entity<AccountGameSession>().HasKey(x => new { x.AccountId, x.GameSessionId });
        modelBuilder.Entity<AccountGameSession>().HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AccountGameSession>().HasOne(x => x.GameSession).WithMany().HasForeignKey(x => x.GameSessionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AccountGameSession>().HasIndex(x => x.GameSessionId);
        modelBuilder.Entity<Boardgame>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Player>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<Player>().Property(x => x.NormalizedName).HasMaxLength(200);
        modelBuilder.Entity<Player>().HasIndex(x => x.NormalizedName);
        modelBuilder.Entity<Player>().HasOne(x => x.Account).WithOne().HasForeignKey<Player>(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Player>().HasIndex(x => x.AccountId).IsUnique();

        modelBuilder.Entity<GameSession>().HasOne(x => x.BoardGame).WithMany()
            .HasForeignKey(x => x.BoardGameId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GameSession>().HasOne(x => x.CreatedByAccount).WithMany()
            .HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict);
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
