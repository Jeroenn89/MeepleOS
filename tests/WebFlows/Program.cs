using System.Text.Json;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Repositories;
using Microsoft.EntityFrameworkCore;

var path = Path.Combine(Path.GetTempPath(), $"meepleos-webflows-{Guid.NewGuid():N}.db");
try
{
    var options = new DbContextOptionsBuilder<MeepleDbContext>().UseSqlite($"Data Source={path}").Options;
    IDbContextFactory<MeepleDbContext> factory = new TestFactory(options);
    await using (var db = await factory.CreateDbContextAsync()) await db.Database.MigrateAsync();
    var boardgames = new BoardgameService(new BoardgameRepository(factory));
    var playerRepository = new PlayerRepository(factory);
    var players = new PlayerService(playerRepository);
    var expansions = new GameExpansionService(new GameExpansionRepository(factory), new BoardgameRepository(factory));
    var sessions = new GameSessionService(new GameSessionRepository(factory));
    var stats = new StatisticsService(new GameSessionRepository(factory), playerRepository, new BoardgameRepository(factory));
    var game = await boardgames.AddAsync("Testspel", 2, 4, 45);
    var jeroen = await players.AddAsync("Jeroen"); var pallas = await players.AddAsync("Pallas");
    var expansion = await expansions.AddAsync(game.Id, "Testuitbreiding");
    var date = new DateOnly(2026, 11, 11);
    var session = await sessions.RecordAsync(game.Id, date, [new(jeroen.Id, 42, 1), new(pallas.Id, 30, 2)], [jeroen.Id], "Test", [expansion.Id]);
    Check((await sessions.GetAllAsync(game.Id, jeroen.Id, date, date)).Single().Expansions.Single().Id == expansion.Id, "potje en filters");
    Check((await stats.GetPlayerStatisticsAsync(jeroen.Id, game.Id)).Single().WinPercentage == 100, "profielstatistiek");
    Check((await stats.GetPlayStatisticsAsync("month", StatisticsPeriod.Create(year: 2026, month: 11))).TotalPlays == 1, "periode en grafiek");
    var filtered = await stats.GetFilteredStatisticsAsync(jeroen.Id, game.Id, "week", StatisticsPeriod.Create(year: 2026, week: 46));
    Check(filtered.TotalPlays == 1 && filtered.Wins == 1 && filtered.Players.Single().WinPercentage == 100 && filtered.PlaysByPeriod.Single().Plays == 1, "gecombineerde statistiekfilters");
    var automatic = await sessions.RecordAsync(game.Id, date.AddDays(1), [new(jeroen.Id, 20), new(pallas.Id, 40)]);
    Check(automatic.Players.Single(p => p.PlayerId == pallas.Id).IsWinner, "automatische winnaar uit scores");
    var explicitWinner = await sessions.RecordAsync(game.Id, date.AddDays(2), [new(jeroen.Id, 20), new(pallas.Id, 40)], [jeroen.Id]);
    Check(explicitWinner.Players.Single(p => p.PlayerId == jeroen.Id).IsWinner, "expliciete winnaar krijgt voorrang");
    await sessions.UpdateAsync(session.Id, JsonSerializer.SerializeToElement(new { notes = "Bijgewerkt", expansionIds = Array.Empty<int>() }));
    Check((await sessions.GetAllAsync()).Single(s => s.Id == session.Id).Expansions.Count == 0, "potje wijzigen");
    await sessions.UpdateAsync(session.Id, JsonSerializer.SerializeToElement(new { players = new[] { new { playerId = jeroen.Id, score = (int?)55, position = (int?)2 }, new { playerId = pallas.Id, score = (int?)60, position = (int?)1 } }, winnerPlayerIds = new[] { pallas.Id } }));
    var changed = (await sessions.GetAllAsync()).Single(s => s.Id == session.Id);
    Check(changed.Players.Count == 2 && changed.Players.Single(p => p.PlayerId == pallas.Id).IsWinner && changed.Players.Single(p => p.PlayerId == jeroen.Id).Score == 55, "meerdere deelnemers en winnaar wijzigen");
    await expansions.UpdateAsync(expansion.Id, JsonSerializer.SerializeToElement(new { name = "Hernoemd" }));
    await players.UpdateAsync(pallas.Id, "Roan");
    await boardgames.UpdateAsync(game.Id, JsonSerializer.SerializeToElement(new { estimatedMinutes = 50 }));
    Check((await boardgames.GetAllAsync()).Single().EstimatedMinutes == 50, "bordspel wijzigen");
    await sessions.DeleteAsync(session.Id); await sessions.DeleteAsync(automatic.Id); await sessions.DeleteAsync(explicitWinner.Id); await expansions.DeleteAsync(expansion.Id); await players.DeleteAsync(pallas.Id); await boardgames.DeleteAsync(game.Id);
    Check((await sessions.GetAllAsync()).Count == 0, "verwijderen");
    Console.WriteLine("WebFlows: toevoegen, lezen, filteren, profielen, wijzigen en verwijderen geslaagd; alleen tijdelijke database.");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
static void Check(bool condition, string label) { if (!condition) throw new Exception($"Mislukt: {label}"); }
sealed class TestFactory(DbContextOptions<MeepleDbContext> options) : IDbContextFactory<MeepleDbContext>
{ public MeepleDbContext CreateDbContext() => new(options); }
