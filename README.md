# MeepleOS

MeepleOS beheert lokaal bordspellen, spelers en gespeelde potjes via een MCP-server. Het project gebruikt C#/.NET 10, de officiële MCP C# SDK, Entity Framework Core en SQLite. Statistieken worden op aanvraag berekend uit de geregistreerde potjes.

## Structuur

- `src/MeepleOS.ServerLayer`: MCP-host en twintig tools. De host gebruikt stdio.
- `src/MeepleOS.AiClient`: interactieve Gemini-consoleclient die de bestaande stdio-tools aanroept.
- `src/MeepleOS.Web`: responsive Blazor Web App met dashboard, collectie, profielen, potjes, statistieken, invoer en AI-chat.
- `src/MeepleOS.BusinessLayer`: businesslogica, validatie en opslaginterfaces, zonder afhankelijkheid van EF Core.
- `src/MeepleOS.DataLayer`: EF Core-datalaag met `Data/Models` voor tabelmodellen, `Data/Context` voor de databasecontext, plus repositories en migraties.
- `tests/SmokeTest.ps1`: lokale protocoltest met een tijdelijke database.
- `tests/MigrationTest`: controleert een upgrade van het bestaande SQLite-schema met bewaarde gegevens.
- `tests/WebFlows`: controleert de handmatige lees-, filter-, profiel- en schrijfstromen op een tijdelijke database.

## Webcode lezen

In `src/MeepleOS.Web/Components` staat de HTML-achtige opmaak in `.razor`-bestanden. De bijbehorende velden, filters en laad- en opslagmethoden staan in de gelijknamige `.razor.cs`-bestanden. `Components/Pages/Manage.razor.cs` bevat de handmatige invoer voor bordspellen, uitbreidingen, spelers en potjes. De eigen vormgeving staat in `wwwroot/app.css`; de gekopieerde bestanden onder `wwwroot/lib` zijn externe Bootstrap-bestanden. `Program.cs` regelt de start, databaseverbinding en dependency injection.

## Website lokaal starten

De website vereist altijd een expliciet databasepad. Een lege testdatabase bevat de spellen en spelers van de normale MCP-server niet. Maak daarom een aparte testkopie met de SQLite-back-upfunctie; die neemt ook wijzigingen uit het WAL-bestand mee en leest de bron alleen. De kopieeropdracht weigert een bestaand doelbestand te overschrijven. `data\meepleos.db` wordt niet gewijzigd.

```powershell
Set-Location X:\MeepleOS
dotnet build MeepleOS.slnx -c Release
if (-not (Test-Path 'data\web-chat-test.db')) {
    dotnet run -c Release --project tools\CloneTestDatabase -- data\meepleos.db data\web-chat-test.db
}
$env:MEEPLEOS_DB_PATH = 'X:\MeepleOS\data\web-chat-test.db'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run -c Release --no-build --no-launch-profile --project src\MeepleOS.Web --urls http://127.0.0.1:5197
```

Open op de computer `http://127.0.0.1:5197/`. De kopieeropdracht voer je alleen uit als `web-chat-test.db` nog niet bestaat; kies voor een nieuwe kopie een nieuwe bestandsnaam en stel `MEEPLEOS_DB_PATH` daarop in. De website en de door haar gestarte MCP-server gebruiken hetzelfde databasepad. `GEMINI_API_KEY` is voor de website optioneel; zonder sleutel blijven alle handmatige functies beschikbaar. De AI-chat gebruikt server-side Gemini 3.5 Flash-Lite. Stel de sleutel, indien gewenst, alleen in de huidige PowerShell-sessie in:

Een tweede `dotnet run` kan niet bouwen zolang een eerdere `MeepleOS.Web.exe` nog dezelfde Debug- of Release-bestanden gebruikt. Stop de vorige instantie met Ctrl+C in het bijbehorende terminalvenster voordat je opnieuw bouwt of start. Bij een achtergebleven instantie toont `Get-Process MeepleOS.Web` de proces-ID's.

```powershell
$secureKey = Read-Host 'Gemini API-sleutel' -AsSecureString
$env:GEMINI_API_KEY = [System.Net.NetworkCredential]::new('', $secureKey).Password
Remove-Variable secureKey
```

Voor een telefoon op hetzelfde wifi-netwerk: zet een eigen websitewachtwoord in de huidige sessie en laat de server op het lokale netwerk luisteren:

```powershell
$securePassword = Read-Host 'Websitewachtwoord' -AsSecureString
$env:MEEPLEOS_WEB_PASSWORD = [System.Net.NetworkCredential]::new('', $securePassword).Password
Remove-Variable securePassword
$env:MEEPLEOS_DB_PATH = 'X:\MeepleOS\data\web-chat-test.db'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run -c Release --no-build --no-launch-profile --project src\MeepleOS.Web --urls http://0.0.0.0:5197
```

Zoek het lokale IP-adres van de computer met `ipconfig` en open op de telefoon `http://<lokaal-ip>:5197/`. De browser vraagt om het websitewachtwoord. De website start niet op een netwerkadres zonder `MEEPLEOS_WEB_PASSWORD`. Gebruik dit uitsluitend op vertrouwde wifi; Basic-authenticatie over HTTP versleutelt het wachtwoord niet. Zet geen routerpoort open en publiceer deze lokale configuratie niet op internet.

Een gepubliceerde Release-versie start je vanuit de publicatiemap. Bouw eerst ook de MCP-server in Release voor de AI-chat:

```powershell
Set-Location X:\MeepleOS
dotnet build src\MeepleOS.ServerLayer -c Release
dotnet publish src\MeepleOS.Web\MeepleOS.Web.csproj -c Release -o data\web-publish
$env:MEEPLEOS_DB_PATH = 'X:\MeepleOS\data\web-chat-test.db'
$env:MEEPLEOS_ROOT = 'X:\MeepleOS'
$env:ASPNETCORE_ENVIRONMENT = 'Production'
Set-Location X:\MeepleOS\data\web-publish
dotnet MeepleOS.Web.dll --urls http://127.0.0.1:5198
```

Open dan `http://127.0.0.1:5198/`. Start de DLL vanuit deze map, zodat de website de gepubliceerde CSS- en Blazor-bestanden en de bijbehorende configuratie laadt.

Start de lokale en gepubliceerde website niet tegelijk met hetzelfde testdatabasebestand. Gebruik voor een tweede instantie een andere testkopie. Gebruik de echte database pas als je bewust van test naar productie overstapt.

De pagina's zijn: `/` (dashboard), `/games` en `/games/{id}` (collectie en spelprofiel), `/players` en `/players/{id}` (spelers), `/sessions` en `/sessions/{id}` (potjes), `/statistics` (combineerbare filters), `/manage` (toevoegen, wijzigen en bevestigd verwijderen) en `/chat` (AI). Alle functies zijn op telefoon en desktop beschikbaar. De AI-chat vraagt voor verwijderingen en wijzigingen een extra bevestiging; de bestaande opdrachtverwerker voorkomt dubbele AI-registraties met hetzelfde spel, dezelfde datum en dezelfde deelnemers en meldt reeds opgeslagen deelstappen.

Bij een duidelijk genoemde nieuwe deelnemer voegt de AI-chat eerst die speler toe en gebruikt daarna het nieuwe ID voor het potje. Bij een onbekend bordspel vraagt de chat naar minimum, maximum (of expliciet geen vast maximum) en speelduur als die gegevens ontbreken. Een kort antwoord op die vervolgvraag wordt bij de oorspronkelijke opdracht gevoegd; een nieuwe zelfstandige opdracht begint opnieuw. Met `annuleer` wis je de openstaande vervolgvraag. De opdrachtverwerker controleert het hele plan vóór de eerste schrijfactie; de alleen-lezenstand voegt nooit gegevens toe.

Een duidelijke tikfout van maximaal één teken in een unieke bestaande spelnaam, zoals `munckin` voor `Munchkin`, wordt in de chat naar de catalogusnaam omgezet. Als meerdere spellen passen, vraagt de chat om verduidelijking.

Controle zonder echte Gemini-verzoeken:

```powershell
dotnet run -c Release --project tests\WebFlows\WebFlows.csproj
dotnet run -c Release --project tests\AiClientTests\AiClientTests.csproj
```

Voor de Raspberry Pi 4 is later een .NET 10-runtime voor Linux ARM64 nodig. Publiceer dan met `dotnet publish src\MeepleOS.Web -c Release -r linux-arm64 --self-contained false -o publish`, kopieer de output én de MCP-server naar de Pi, stel daar `MEEPLEOS_DB_PATH`, `MEEPLEOS_ROOT` en `MEEPLEOS_SERVER_DLL` als absolute paden en de servergeheimen via de serviceomgeving in, en draai de website als `systemd`-service achter een HTTPS-reverseproxy met sterkere authenticatie. Voor toegang buitenshuis is een VPN zoals WireGuard of Tailscale de veilige route; publieke blootstelling van de huidige HTTP-wifi-instelling is niet geschikt. De Windows-pc hoeft na verhuizing van database, webapp en MCP-server niet aan te blijven.

De tools roepen services in `MeepleOS.BusinessLayer` aan. Die services valideren invoer en gebruiken opslaginterfaces. `MeepleOS.DataLayer` implementeert die interfaces met SQLite. De host registreert de implementaties via dependency injection.

## Bouwen en starten

```powershell
dotnet build MeepleOS.slnx
dotnet run --no-build --project src/MeepleOS.ServerLayer
```

Start vanuit de projectmap. De server wacht op MCP-berichten via stdin; hij biedt geen interactieve prompt. EF Core maakt de database bij de eerste start aan en voert bij volgende starts openstaande migraties uit. De migraties behouden bestaande bordspellen, uitbreidingen, spelers en potjes. Standaard staat de database in `data/meepleos.db`; met `MEEPLEOS_DB_PATH` kies je een ander pad. De `data`-map en SQLite-bestanden staan in `.gitignore`.

Voor een lokale test:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SmokeTest.ps1
dotnet run --project tests/MigrationTest/MigrationTest.csproj
```

## Gemini-consoleclient

Bouw de oplossing en stel de sleutel alleen in voor de huidige PowerShell-sessie:

```powershell
$secureKey = Read-Host 'Gemini API-sleutel' -AsSecureString
$env:GEMINI_API_KEY = [System.Net.NetworkCredential]::new('', $secureKey).Password
Remove-Variable secureKey
dotnet build MeepleOS.slnx
dotnet run --no-build --project src/MeepleOS.AiClient
```

De sleutel wordt niet naar een bestand geschreven of gelogd. Bewaar hem niet in Git. De client gebruikt `gemini-3.5-flash-lite`.

Je echte collectie veilig bekijken, zonder Gemini-sleutel of wijzigingen aan de database:

```powershell
dotnet build MeepleOS.slnx -c Release --no-restore
$env:MEEPLEOS_DB_PATH = (Resolve-Path 'data\meepleos.db').Path
dotnet run -c Release --no-build --project src/MeepleOS.AiClient -- --read-only --list
```

`--list` toont bordspellen, spelers en uitbreidingen. `--read-only` laat de MCP-server SQLite alleen-lezen openen, slaat migraties over en blokkeert alle schrijfacties in de client. Voor interactieve vragen gebruik je dezelfde opdracht zonder `--list`, met `GEMINI_API_KEY` ingesteld. `--check-connect` controleert alleen MCP-leestools en heeft geen sleutel nodig.

Per opdracht leest de client bordspellen, spelers en uitbreidingen één keer, vraagt Gemini één gestructureerd actieplan, valideert het hele plan en voert de bestaande MCP-tools in volgorde uit. Bevestigingen komen uit MCP-resultaten, zonder tweede Gemini-aanroep. Bij ontbrekende gegevens verschijnt één gerichte vraag. Na elke opdracht staat het aantal verstuurde Gemini-verzoeken; MCP-aanroepen tellen niet mee. De lokale teller staat in de genegeerde `data/gemini-requests.json` en begrenst standaard op 15 verzoeken per minuut en 500 per Amsterdamse dag. Pas dit eventueel aan met `GEMINI_RPM_LIMIT` en `GEMINI_RPD_LIMIT`. Deze teller houdt alleen verzoeken van MeepleOS bij, ook als Gemini HTTP 429 teruggeeft. Google AI Studio toont de werkelijke limiet voor je project; ander gebruik van dezelfde projectlimiet staat niet in de MeepleOS-teller. Bij HTTP 429 wordt niet automatisch opnieuw geprobeerd.

Geautomatiseerde tests gebruiken mocks en een aparte tijdelijke SQLite-database; ze verbruiken geen Gemini-verzoeken en raken `data\meepleos.db` niet:

```powershell
dotnet run --project tests/AiClientTests/AiClientTests.csproj
```

De client hergebruikt de MCP-server en diens bestaande SQLite-database. Gebruik zo nodig `MEEPLEOS_DB_PATH` om expliciet dezelfde database te kiezen. De Blazor-website hergebruikt de planner, MCP-adapter en opdrachtverwerker als aparte C#-klassen.

## Verbinden met Codex

Bouw eerst het project. Voeg daarna in de Codex CLI de lokale stdio-server toe:

```powershell
codex mcp add meepleos --env MEEPLEOS_DB_PATH=X:\MeepleOS\data\meepleos.db -- dotnet X:\MeepleOS\src\MeepleOS.ServerLayer\bin\Debug\net10.0\MeepleOS.ServerLayer.dll
codex mcp list
```

Het absolute databasepad in dit commando voorkomt dat Codex vanuit een andere werkmap een tweede database aanmaakt. Pas het pad aan als je het project verplaatst. In Codex kun je met `/mcp` controleren of de server actief is.

Beschikbare tools:

- Bordspellen: `add_boardgame`, `get_boardgames`, `update_boardgame`, `delete_boardgame`.
- Uitbreidingen: `add_game_expansion`, `get_game_expansions`, `update_game_expansion`, `delete_game_expansion`.
- Spelers: `add_player`, `get_players`, `update_player`, `delete_player`.
- Potjes: `record_game_session`, `get_game_sessions`, `update_game_session`, `delete_game_session`.
- Statistieken: `get_player_statistics`, `get_boardgame_statistics`, `get_leaderboard`, `get_play_statistics`.

`add_player` geeft bij een bestaande naam dezelfde speler terug; hoofdletters en spaties aan het begin of einde tellen niet als verschil.

`add_game_expansion` verwacht `boardGameId`, `name` en optioneel `description`. Namen zijn per basisspel uniek, ongeacht hoofdletters. `get_game_expansions` toont ook de naam van het basisspel en kan met `boardGameId` filteren. `update_game_expansion` verwacht `id` en een `changes`-object met optioneel `name` en/of `description`; `description: null` wist de beschrijving. `delete_game_expansion` weigert uitbreidingen die in gespeelde potjes voorkomen. Een basisspel met uitbreidingen kan evenmin worden verwijderd.

`GameExpansions.BoardGameId` verwijst naar `Boardgames.Id`. `GameSessionExpansions` koppelt potjes aan hun gebruikte uitbreidingen via `GameSessionId` en `GameExpansionId`; samen vormen ze de primaire sleutel, zodat een uitbreiding maar eenmaal per potje voorkomt. Een potje mag alleen uitbreidingen van zijn eigen basisspel gebruiken. Potjes zonder uitbreidingen hebben geen koppelrijen. De koppeltabel bewaart de gegevens voor latere uitbreidingsstatistieken; die statistieken zijn nog niet geïmplementeerd.

`record_game_session` verwacht `boardGameId`, `playedAt` (bijvoorbeeld `2026-11-11`), `players` (een lijst met `playerId` en optioneel `score` en `position`) en `winnerPlayerIds` (één of meer deelnemende spelers). `notes` is optioneel. Zo kan een potje zonder scores worden opgeslagen; gedeelde winst is ook mogelijk. `get_game_sessions` accepteert optioneel `boardGameId`, `playerId`, `from` en `to`. De datums in het filter zijn inclusief.

Geef bij `record_game_session` optioneel `expansionIds` op, bijvoorbeeld `[3, 4]`. Zonder deze parameter wordt het potje zonder uitbreidingen opgeslagen. `get_game_sessions` bevat per potje een `expansions`-lijst, ook als die leeg is. De server controleert of iedere uitbreiding bestaat en bij het gekozen basisspel hoort.

Voorbeeld via Codex: vraag eerst "Welke uitbreidingen zijn er voor El Dorado?" en daarna "Voeg uitbreiding Helden toe aan El Dorado." Codex kan met `get_boardgames` het bordspel-ID bepalen en met `add_game_expansion` de uitbreiding toevoegen. Vraag vervolgens "Registreer dat Jeroen en Roan op 2026-11-11 El Dorado met Helden speelden; Jeroen won." Codex zoekt de speler- en uitbreiding-ID's op en geeft het uitbreiding-ID mee aan `record_game_session`.

## Wijzigen en verwijderen

`add_boardgame` verwacht `name`, `minPlayers`, `maxPlayers` en `estimatedMinutes`. `minPlayers` is minimaal 1. Geef voor `maxPlayers` een getal van minstens `minPlayers` op, of expliciet `null` als het spel geen vast spelersmaximum heeft. Een onbekend maximum is geen reden om `null` te gebruiken. `get_boardgames` geeft het maximum eveneens als getal of `null` terug.

`update_boardgame` verwacht `id` en een `changes`-object met `name`, `minPlayers`, `maxPlayers` en/of `estimatedMinutes`. Een weggelaten veld blijft gelijk; `maxPlayers: null` verwijdert een bestaand maximum. Bijvoorbeeld: `changes: { "maxPlayers": null }`. `update_player` verwacht `id` en `name`. De speler houdt hetzelfde ID en blijft daardoor zichtbaar in bestaande potjes.

`update_game_session` verwacht `id` en een object `changes`. Ondersteunde velden daarin zijn `boardGameId`, `playedAt`, `notes`, `players`, `winnerPlayerIds` en `playerChanges`. Weggelaten velden blijven gelijk. `notes: null` wist opmerkingen. `players` vervangt de volledige deelnemerslijst; geef dan zo nodig ook nieuwe `winnerPlayerIds` op. `playerChanges` wijzigt `score` en/of `position` van bestaande deelnemers op basis van `playerId`; een weggelaten waarde blijft gelijk en `null` wist de waarde. Gebruik `players` en `playerChanges` niet tegelijk. Een potje moet minstens één bestaande deelnemer en één deelnemende winnaar hebben. De wijziging wordt in één databasetransactie opgeslagen.

Voor uitbreidingen accepteert `changes` ook `expansionIds` (vervangt de hele lijst; `[]` maakt haar leeg), `expansionIdsToAdd` en `expansionIdsToRemove`. Laat deze velden weg om de koppelingen te behouden. Gebruik `expansionIds` niet samen met de toevoeg- of verwijdervelden. Als het basisspel wijzigt, moeten de behouden of opgegeven uitbreidingen bij het nieuwe basisspel horen. Alle wijzigingen van het potje worden in dezelfde transactie opgeslagen. Bij `delete_game_session` verdwijnen de koppelingen, terwijl de uitbreidingen geregistreerd blijven.

De drie `delete_`-tools verwachten `id` en zijn met de MCP SDK als destructief gemarkeerd. Hun beschrijvingen vragen de AI-client om vooraf bevestiging; of de client die bevestiging daadwerkelijk toont, hangt van die client af. De server blokkeert zelf het verwijderen van bordspellen en spelers die in gespeelde potjes voorkomen. `delete_game_session` verwijdert alleen het potje en zijn deelnameregistraties, en laat spelers, bordspellen en andere potjes staan.

Voorbeelden van opdrachten aan Codex:

- “Wijzig bordspel 3 met `update_boardgame`: zet `changes.estimatedMinutes` op 60.”
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
