# MeepleOS

MeepleOS beheert lokaal bordspellen, spelers en gespeelde potjes via een MCP-server. Het project gebruikt C#/.NET 10, de officiële MCP C# SDK, Entity Framework Core en SQLite. Statistieken worden op aanvraag berekend uit de geregistreerde potjes.

## Structuur

- `src/MeepleOS.ServerLayer`: MCP-host en zestien tools. De host gebruikt stdio.
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

Beschikbare tools:

- Bordspellen: `add_boardgame`, `get_boardgames`, `update_boardgame`, `delete_boardgame`.
- Spelers: `add_player`, `get_players`, `update_player`, `delete_player`.
- Potjes: `record_game_session`, `get_game_sessions`, `update_game_session`, `delete_game_session`.
- Statistieken: `get_player_statistics`, `get_boardgame_statistics`, `get_leaderboard`, `get_play_statistics`.

`add_player` geeft bij een bestaande naam dezelfde speler terug; hoofdletters en spaties aan het begin of einde tellen niet als verschil.

`record_game_session` verwacht `boardGameId`, `playedAt` (bijvoorbeeld `2026-11-11`), `players` (een lijst met `playerId` en optioneel `score` en `position`) en `winnerPlayerIds` (één of meer deelnemende spelers). `notes` is optioneel. Zo kan een potje zonder scores worden opgeslagen; gedeelde winst is ook mogelijk. `get_game_sessions` accepteert optioneel `boardGameId`, `playerId`, `from` en `to`. De datums in het filter zijn inclusief.

## Wijzigen en verwijderen

`update_boardgame` verwacht `id` en alleen de velden die moeten wijzigen: `name`, `minPlayers`, `maxPlayers` en/of `estimatedMinutes`. `update_player` verwacht `id` en `name`. De speler houdt hetzelfde ID en blijft daardoor zichtbaar in bestaande potjes.

`update_game_session` verwacht `id` en een object `changes`. Ondersteunde velden daarin zijn `boardGameId`, `playedAt`, `notes`, `players`, `winnerPlayerIds` en `playerChanges`. Weggelaten velden blijven gelijk. `notes: null` wist opmerkingen. `players` vervangt de volledige deelnemerslijst; geef dan zo nodig ook nieuwe `winnerPlayerIds` op. `playerChanges` wijzigt `score` en/of `position` van bestaande deelnemers op basis van `playerId`; een weggelaten waarde blijft gelijk en `null` wist de waarde. Gebruik `players` en `playerChanges` niet tegelijk. Een potje moet minstens één bestaande deelnemer en één deelnemende winnaar hebben. De wijziging wordt in één databasetransactie opgeslagen.

De drie `delete_`-tools verwachten `id` en zijn met de MCP SDK als destructief gemarkeerd. Hun beschrijvingen vragen de AI-client om vooraf bevestiging; of de client die bevestiging daadwerkelijk toont, hangt van die client af. De server blokkeert zelf het verwijderen van bordspellen en spelers die in gespeelde potjes voorkomen. `delete_game_session` verwijdert alleen het potje en zijn deelnameregistraties, en laat spelers, bordspellen en andere potjes staan.

Voorbeelden van opdrachten aan Codex:

- “Wijzig bordspel 3 met `update_boardgame`: zet `estimatedMinutes` op 60.”
- “Hernoem speler 2 naar ‘Alexandra’ met `update_player`.”
- “Gebruik `update_game_session` voor potje 5 met `changes: { "notes": null, "playerChanges": [{ "playerId": 2, "score": null }] }`.”
- “Gebruik `update_game_session` voor potje 5 met `changes: { "players": [{ "playerId": 2 }, { "playerId": 4 }], "winnerPlayerIds": [4] }`.”
- “Verwijder potje 5 met `delete_game_session`, na mijn bevestiging.”

## Statistieken

De vier statistiektools accepteren dezelfde optionele periodeparameters: `year` voor een kalenderjaar; `year` met `month` voor een maand; `year` met `week` voor een ISO-week (maandag tot en met zondag); of `from` en/of `to` als inclusieve grens in `YYYY-MM-DD`-vorm. Laat alles weg voor alle geregistreerde potjes. Combineer deze periodevormen niet. Potjes buiten de periode tellen niet mee; spelers en bordspellen zonder potjes blijven met nulwaarden zichtbaar.

- `get_player_statistics`: optioneel `playerId`, `boardGameId` en periode. Geeft potjes, overwinningen en winstpercentage per speler.
- `get_boardgame_statistics`: optioneel `boardGameId` en periode. Geeft potjes, unieke spelers en alle spelers die het vaakst wonnen.
- `get_leaderboard`: optioneel `sortBy` (`wins` of `winPercentage`, standaard `wins`), `boardGameId` en periode. Geeft ook potjes per speler.
- `get_play_statistics`: optioneel `groupBy` (`week`, `month` of `year`, standaard `month`) en periode. Geeft totaal, aantallen per periode en bordspellen op volgorde van aantal potjes. Alleen periodes met potjes staan in de reeks.

De BusinessLayer telt een overwinning voor iedere deelnemer met `IsWinner = true`, ook bij een gedeelde winst. Scores en eindposities spelen daarbij geen rol. Het winstpercentage is `wins / plays × 100` en is nul bij geen potjes. Dezelfde `StatisticsService` is later rechtstreeks vanuit een website aan te roepen zonder MCP.

Voorbeelden van vragen aan Codex na het verbinden van de server:

- “Gebruik `get_player_statistics` voor speler 1 in november 2026 (`playerId=1`, `year=2026`, `month=11`).”
- “Gebruik `get_boardgame_statistics` voor bordspel 2 in ISO-week 46 van 2026 (`boardGameId=2`, `year=2026`, `week=46`).”
- “Gebruik `get_leaderboard` op winstpercentage voor bordspel 2 sinds 2026-01-01 (`sortBy=winPercentage`, `boardGameId=2`, `from=2026-01-01`).”
- “Gebruik `get_play_statistics` per maand voor 2026 (`groupBy=month`, `year=2026`).”
