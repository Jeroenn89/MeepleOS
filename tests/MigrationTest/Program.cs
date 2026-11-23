using MeepleOS.DataLayer.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Repositories;

if (args.Length == 2 && args[0] == "--inspect-db")
{
    await using var inspected = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = args[1], Mode = SqliteOpenMode.ReadOnly, DefaultTimeout = 5
    }.ToString());
    try
    {
        await inspected.OpenAsync();
        foreach (var sql in new[] { "PRAGMA journal_mode", "PRAGMA quick_check",
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsLock'",
            "SELECT COUNT(*) FROM __EFMigrationsHistory" })
        {
            await using var command = inspected.CreateCommand();
            command.CommandText = sql;
            Console.WriteLine($"{sql}: {await command.ExecuteScalarAsync()}");
        }
        if (await Scalar(inspected, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsLock'") == 1)
            Console.WriteLine($"__EFMigrationsLock rows: {await Scalar(inspected, "SELECT COUNT(*) FROM __EFMigrationsLock")}");
    }
    catch (SqliteException ex)
    {
        Console.WriteLine($"SQLite error={ex.SqliteErrorCode}, extended={ex.SqliteExtendedErrorCode}: {ex.Message}");
        throw;
    }
    return;
}

if (args.Length == 3 && args[0] == "--verify-account-copy")
{
    await using var original = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = args[1], Mode = SqliteOpenMode.ReadOnly }.ToString());
    await using var migrated = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = args[2], Mode = SqliteOpenMode.ReadOnly }.ToString());
    await original.OpenAsync();
    await migrated.OpenAsync();
    foreach (var (table, columns) in new[]
    {
        ("Boardgames", "Id,Name,MinPlayers,MaxPlayers,EstimatedMinutes"),
        ("Players", "Id,Name,NormalizedName"),
        ("GameSessions", "Id,BoardGameId,PlayedAt,Notes"),
        ("SessionPlayers", "Id,GameSessionId,PlayerId,Score,Position,IsWinner"),
        ("GameExpansions", "Id,BoardGameId,Name,NormalizedName,Description"),
        ("GameSessionExpansions", "GameSessionId,GameExpansionId")
    })
    {
        var before = await Rows(original, $"SELECT {columns} FROM {table} ORDER BY 1,2");
        var after = await Rows(migrated, $"SELECT {columns} FROM {table} ORDER BY 1,2");
        if (!before.SequenceEqual(after)) throw new Exception($"Gegevens gewijzigd in {table}.");
        Console.WriteLine($"{table}: {before.Count} bestaande rijen behouden");
    }
    var accountIds = await Rows(migrated, "SELECT Id FROM AspNetUsers");
    if (accountIds.Count != 1) throw new Exception("Verwacht precies één eerste account.");
    var accountId = accountIds[0];
    var jeroenRows = await Rows(migrated, "SELECT AccountId FROM Players WHERE NormalizedName='JEROEN'");
    if (jeroenRows.Count != 1 || jeroenRows[0] != accountId)
        throw new Exception("Jeroen is niet eenduidig aan het account gekoppeld.");
    if (await Scalar(migrated, "SELECT COUNT(*) FROM Players WHERE NormalizedName <> 'JEROEN' AND AccountId IS NOT NULL") != 0)
        throw new Exception("Een andere speler is aan het account gekoppeld.");
    foreach (var (source, link) in new[] { ("Boardgames", "AccountBoardgames"), ("GameSessions", "AccountGameSessions"), ("Players", "AccountPlayers") })
        if (await Scalar(migrated, $"SELECT COUNT(*) FROM {source}") != await Scalar(migrated, $"SELECT COUNT(*) FROM {link}"))
            throw new Exception($"Koppelingen ontbreken in {link}.");
    var accountOptions = new DbContextOptionsBuilder<MeepleDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = args[2], Mode = SqliteOpenMode.ReadOnly }.ToString()).Options;
    var repository = new GameSessionRepository(new CopyFactory(accountOptions), new CopyAccount(accountId));
    var visibleSessions = await repository.GetAllAsync(null, null, null, null);
    if (visibleSessions.Count != await Scalar(migrated, "SELECT COUNT(*) FROM GameSessions"))
        throw new Exception("Niet alle bestaande potjes zijn zichtbaar voor het account.");
    Console.WriteLine("Accountmigratie gecontroleerd: alle bestaande gegevens en koppelingen behouden.");
    return;
}

static async Task<long> Scalar(SqliteConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return (long)(await command.ExecuteScalarAsync())!;
}

static async Task<List<string>> Rows(SqliteConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<string>();
    while (await reader.ReadAsync())
        rows.Add(string.Join("\u001f", Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.IsDBNull(i) ? "<NULL>" : reader.GetValue(i).ToString())));
    return rows;
}
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

    await using (var expansionDb = new MeepleDbContext(options))
    {
        await expansionDb.GetService<IMigrator>().MigrateAsync("20261111150748_AddGameExpansions");
        await expansionDb.Database.ExecuteSqlRawAsync("""
            INSERT INTO GameExpansions (Id, BoardGameId, Name, NormalizedName)
            VALUES (1, 1, 'Helden', 'HELDEN');
            INSERT INTO GameSessionExpansions (GameSessionId, GameExpansionId)
            VALUES (1, 1);
            """);
    }

    await using (var upgradedDb = new MeepleDbContext(options))
    {
        await upgradedDb.Database.MigrateAsync();
        if (await upgradedDb.Boardgames.CountAsync() != 1 ||
            await upgradedDb.Players.CountAsync() != 1 ||
            await upgradedDb.GameSessions.CountAsync() != 1 ||
            await upgradedDb.SessionPlayers.CountAsync() != 1 ||
            await upgradedDb.GameExpansions.CountAsync() != 1 ||
            await upgradedDb.GameSessionExpansions.CountAsync() != 1)
            throw new Exception("De migratie heeft bestaande gegevens gewijzigd of nieuwe tabellen ontbreken.");
        var game = await upgradedDb.Boardgames.SingleAsync();
        if (game.MaxPlayers != 4 || game.MinPlayers != 2)
            throw new Exception("De bestaande spelerslimieten zijn niet behouden.");
        var session = await upgradedDb.GameSessions.Include(x => x.Expansions).SingleAsync();
        if (session.Expansions.Count != 1)
            throw new Exception("De koppeling tussen potje en uitbreiding is niet behouden.");
    }

    Console.WriteLine("Migratietest geslaagd: bestaand SQLite-schema en gegevens behouden.");
}
finally
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
        if (File.Exists(path + suffix)) File.Delete(path + suffix);
}
sealed class CopyFactory(DbContextOptions<MeepleDbContext> options) : IDbContextFactory<MeepleDbContext>
{ public MeepleDbContext CreateDbContext() => new(options); }
sealed class CopyAccount(string id) : IAccountContext
{ public Task<string> GetAccountIdAsync() => Task.FromResult(id); }
