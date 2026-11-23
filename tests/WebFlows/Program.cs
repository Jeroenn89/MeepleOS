using System.Text.Json;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using MeepleOS.DataLayer.Repositories;
using Microsoft.EntityFrameworkCore;

var path = Path.Combine(Path.GetTempPath(), $"meepleos-webflows-{Guid.NewGuid():N}.db");
try
{
    var options = new DbContextOptionsBuilder<MeepleDbContext>().UseSqlite($"Data Source={path}").Options;
    IDbContextFactory<MeepleDbContext> factory = new TestFactory(options);
    await using (var db = await factory.CreateDbContextAsync())
    {
        await db.Database.MigrateAsync();
        db.Users.Add(new MeepleUser { Id = "test-account", UserName = "test" });
        await db.SaveChangesAsync();
    }
    var account = new TestAccountContext("test-account");
    var boardgames = new BoardgameService(new BoardgameRepository(factory, account));
    var playerRepository = new PlayerRepository(factory, account);
    var players = new PlayerService(playerRepository);
    var expansions = new GameExpansionService(new GameExpansionRepository(factory, account), new BoardgameRepository(factory, account));
    var sessions = new GameSessionService(new GameSessionRepository(factory, account));
    var stats = new StatisticsService(new GameSessionRepository(factory, account), playerRepository, new BoardgameRepository(factory, account));
    var game = await boardgames.AddAsync("Testspel", 2, 4, 45);
    var jeroen = await players.AddAsync("Jeroen"); var pallas = await players.AddAsync("Pallas");
    await using (var db = await factory.CreateDbContextAsync())
    {
        (await db.Players.SingleAsync(p => p.Id == jeroen.Id)).AccountId = "test-account";
        await db.SaveChangesAsync();
    }
    var legacyPlayer = await playerRepository.AddOrGetAsync("ada", "ADA");
    var lowercasePlayer = await players.AddAsync("bert");
    Check(lowercasePlayer.Name == "Bert" && (await playerRepository.GetByIdAsync(lowercasePlayer.Id))?.Name == "Bert", "nieuwe spelersnaam met hoofdletter opgeslagen");
    Check((await players.GetAllAsync()).Take(2).Select(player => player.Name).SequenceEqual(["ada", "Bert"]), "spelers alfabetisch ongeacht hoofdletters");
    Check((await players.UpdateAsync(lowercasePlayer.Id, "bram")).Name == "Bram", "gewijzigde spelersnaam met hoofdletter opgeslagen");
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
    await using (var db = await factory.CreateDbContextAsync())
    {
        db.Users.Add(new MeepleUser { Id = "roan-account", UserName = "roan" });
        (await db.Players.SingleAsync(p => p.Id == pallas.Id)).AccountId = "roan-account";
        db.AccountPlayers.Add(new AccountPlayer { AccountId = "roan-account", PlayerId = pallas.Id });
        db.AccountBoardgames.Add(new AccountBoardgame { AccountId = "roan-account", BoardGameId = game.Id });
        foreach (var oldSessionId in await db.SessionPlayers.Where(p => p.PlayerId == pallas.Id).Select(p => p.GameSessionId).ToListAsync())
            db.AccountGameSessions.Add(new AccountGameSession { AccountId = "roan-account", GameSessionId = oldSessionId });
        await db.SaveChangesAsync();
    }
    var roanContext = new TestAccountContext("roan-account");
    var roanSessions = new GameSessionService(new GameSessionRepository(factory, roanContext));
    var roanPlayers = new PlayerService(new PlayerRepository(factory, roanContext));
    var oldRoanCount = (await roanSessions.GetAllAsync()).Count;
    Check(oldRoanCount == 3 && (await roanPlayers.GetAllAsync()).Select(p => p.Id).SequenceEqual([pallas.Id]),
        "historische gedeelde potjes en afgeschermde spelerslijst");
    var shared = await sessions.RecordAsync(game.Id, date.AddDays(3), [new(jeroen.Id), new(pallas.Id)], [pallas.Id]);
    Check((await roanSessions.GetAllAsync()).Count == oldRoanCount + 1, "nieuw potje zichtbaar bij beide accounts");
    Check((await roanPlayers.GetAllAsync()).Any(p => p.Id == jeroen.Id), "medespeler beschikbaar bij gedeeld potje");
    Check((await new StatisticsService(new GameSessionRepository(factory, roanContext), new PlayerRepository(factory, roanContext),
        new BoardgameRepository(factory, roanContext)).GetPlayStatisticsAsync()).TotalPlays == oldRoanCount + 1,
        "gedeeld potje telt eenmaal mee in Roans statistiek");
    var deniedDelete = false;
    try { await roanSessions.DeleteAsync(shared.Id); }
    catch (ArgumentException) { deniedDelete = true; }
    Check(deniedDelete, "deelnemer kan potje van ander account niet verwijderen");
    var roanOwned = await roanSessions.RecordAsync(game.Id, date.AddDays(4), [new(jeroen.Id), new(pallas.Id)], [jeroen.Id]);
    Check((await sessions.GetAllAsync()).Any(s => s.Id == roanOwned.Id), "potje van Roan zichtbaar bij Jeroen");
    await roanSessions.DeleteAsync(roanOwned.Id);
    await sessions.DeleteAsync(shared.Id);
    await using (var db = await factory.CreateDbContextAsync())
    {
        db.AccountBoardgames.Remove(await db.AccountBoardgames.SingleAsync(x => x.AccountId == "roan-account"));
        db.AccountPlayers.RemoveRange(await db.AccountPlayers.Where(x => x.AccountId == "roan-account").ToListAsync());
        (await db.Players.SingleAsync(p => p.Id == pallas.Id)).AccountId = null;
        await db.SaveChangesAsync();
    }
    await sessions.DeleteAsync(session.Id); await sessions.DeleteAsync(automatic.Id); await sessions.DeleteAsync(explicitWinner.Id); await expansions.DeleteAsync(expansion.Id); await players.DeleteAsync(pallas.Id); await players.DeleteAsync(legacyPlayer.Id); await players.DeleteAsync(lowercasePlayer.Id); await boardgames.DeleteAsync(game.Id);
    Check((await sessions.GetAllAsync()).Count == 0, "verwijderen");
    Console.WriteLine("WebFlows: toevoegen, lezen, filteren, profielen, wijzigen en verwijderen geslaagd; alleen tijdelijke database.");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
static void Check(bool condition, string label) { if (!condition) throw new Exception($"Mislukt: {label}"); }
sealed class TestFactory(DbContextOptions<MeepleDbContext> options) : IDbContextFactory<MeepleDbContext>
{ public MeepleDbContext CreateDbContext() => new(options); }
sealed class TestAccountContext(string id) : IAccountContext
{ public Task<string> GetAccountIdAsync() => Task.FromResult(id); }
