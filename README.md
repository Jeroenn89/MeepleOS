# MeepleOS

MeepleOS beheert lokaal bordspellen, spelers en gespeelde potjes via een MCP-server. Het project gebruikt C#/.NET 10, de officiële MCP C# SDK, Entity Framework Core en SQLite. Statistieken en een mobiele website volgen later.

## Structuur

- `src/MeepleOS.ServerLayer`: MCP-host en zes tools. De host gebruikt stdio.
- `src/MeepleOS.BusinessLayer`: businesslogica, validatie en opslaginterfaces, zonder afhankelijkheid van EF Core.
- `src/MeepleOS.DataLayer`: EF Core-datalaag met `Data/Models` voor tabelmodellen, `Data/Context` voor de databasecontext, plus repositories en migraties.
- `tests/SmokeTest.ps1`: lokale protocoltest met een tijdelijke database.

De tools roepen services in `MeepleOS.BusinessLayer` aan. Die services valideren invoer en gebruiken opslaginterfaces. `MeepleOS.DataLayer` implementeert die interfaces met SQLite. De host registreert de implementaties via dependency injection.

## Bouwen en starten

```powershell
dotnet build MeepleOS.slnx
dotnet run --no-build --project src/MeepleOS.ServerLayer
```

Start vanuit de projectmap. De server wacht op MCP-berichten via stdin; hij biedt geen interactieve prompt. EF Core maakt de database bij de eerste start aan en voert bij volgende starts openstaande migraties uit. Een bestaande database uit de vorige versie wordt uitgebreid zonder de opgeslagen bordspellen en spelers te verwijderen. Standaard staat de database in `data/meepleos.db`; met `MEEPLEOS_DB_PATH` kies je een ander pad. De `data`-map en SQLite-bestanden staan in `.gitignore`.

Voor een lokale test:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SmokeTest.ps1
```

## Verbinden met Codex

Bouw eerst het project. Voeg daarna in de Codex CLI de lokale stdio-server toe:

```powershell
codex mcp add meepleos --env MEEPLEOS_DB_PATH=X:\MeepleOS\data\meepleos.db -- dotnet X:\MeepleOS\src\MeepleOS.ServerLayer\bin\Debug\net10.0\MeepleOS.ServerLayer.dll
codex mcp list
```

Het absolute databasepad in dit commando voorkomt dat Codex vanuit een andere werkmap een tweede database aanmaakt. Pas het pad aan als je het project verplaatst. In Codex kun je met `/mcp` controleren of de server actief is.

Beschikbare tools: `add_boardgame`, `get_boardgames`, `add_player`, `get_players`, `record_game_session` en `get_game_sessions`. `add_player` geeft bij een bestaande naam dezelfde speler terug; hoofdletters en spaties aan het begin of einde tellen niet als verschil.

`record_game_session` verwacht `boardGameId`, `playedAt` (bijvoorbeeld `2026-11-11`), `players` (een lijst met `playerId` en optioneel `score` en `position`) en `winnerPlayerIds` (één of meer deelnemende spelers). `notes` is optioneel. Zo kan een potje zonder scores worden opgeslagen; gedeelde winst is ook mogelijk. `get_game_sessions` accepteert optioneel `boardGameId`, `playerId`, `from` en `to`. De datums in het filter zijn inclusief.
