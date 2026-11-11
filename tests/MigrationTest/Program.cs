using MeepleOS.DataLayer.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

var path = Path.Combine(Path.GetTempPath(), $"meepleos-migration-{Guid.NewGuid():N}.db");
var options = new DbContextOptionsBuilder<MeepleDbContext>()
    .UseSqlite($"Data Source={path};Pooling=False").Options;

try
{
    await using (var oldDb = new MeepleDbContext(options))
    {
        await oldDb.GetService<IMigrator>().MigrateAsync("20261111001754_InitialSchemaWithGameSessions");
        await oldDb.Database.ExecuteSqlRawAsync("""
            INSERT INTO Boardgames (Id, Name, MinPlayers, MaxPlayers, EstimatedMinutes)
            VALUES (1, 'El Dorado', 2, 4, 60);
            INSERT INTO Players (Id, Name, NormalizedName) VALUES (1, 'Jeroen', 'JEROEN');
            INSERT INTO GameSessions (Id, BoardGameId, PlayedAt, Notes)
            VALUES (1, 1, '2026-11-10', 'Bestaand potje');
            INSERT INTO SessionPlayers (GameSessionId, PlayerId, IsWinner)
            VALUES (1, 1, 1);
            """);
    }

    await using (var upgradedDb = new MeepleDbContext(options))
    {
        await upgradedDb.Database.MigrateAsync();
        if (await upgradedDb.Boardgames.CountAsync() != 1 ||
            await upgradedDb.Players.CountAsync() != 1 ||
            await upgradedDb.GameSessions.CountAsync() != 1 ||
            await upgradedDb.SessionPlayers.CountAsync() != 1 ||
            await upgradedDb.GameExpansions.CountAsync() != 0 ||
            await upgradedDb.GameSessionExpansions.CountAsync() != 0)
            throw new Exception("De migratie heeft bestaande gegevens gewijzigd of nieuwe tabellen ontbreken.");
        var session = await upgradedDb.GameSessions.Include(x => x.Expansions).SingleAsync();
        if (session.Expansions.Count != 0)
            throw new Exception("Bestaande potjes moeten zonder uitbreidingen geldig blijven.");
    }

    Console.WriteLine("Migratietest geslaagd: bestaand SQLite-schema en gegevens behouden.");
}
finally
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
        if (File.Exists(path + suffix)) File.Delete(path + suffix);
}
