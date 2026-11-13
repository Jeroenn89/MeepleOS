# MeepleOS: natuurlijke taal en MCP-tools

- Raadpleeg vóór het registreren van een potje `get_boardgames` en `get_players`. Koppel genoemde namen eerst aan bestaande bordspellen en spelers.
- MeepleOS gebruikt uitsluitend spelersnamen en kent geen aparte voor- of achternamen. Interpreteer genoemde namen daarom als afzonderlijke spelers wanneer deze overeenkomen met geregistreerde spelers. Als Jeroen, Pallas en Roan geregistreerd staan, betekent "Jeroen Pallas en Roan" drie deelnemers. Ga niet automatisch uit van een voor- en achternaam. Gebruik de bestaande spelerslijst als uitgangspunt.
- De gebruiker is speler Jeroen. Interpreteer "ik", "mij" en "mezelf" als Jeroen en tel hem niet dubbel als Jeroen ook bij naam genoemd wordt.
- Stel alleen bij een relevante, onopgeloste dubbelzinnigheid één korte, gerichte verduidelijkingsvraag. Vraag niet opnieuw naar gegevens die al bekend zijn.
- Gebruik `update_player` uitsluitend als de gebruiker expliciet vraagt een bestaande speler te wijzigen. Maak geen speler aan op basis van een onzekere naaminterpretatie.
- Registreer met `record_game_session` zodra bordspel, deelnemers, winnaar(s) en speeldatum voldoende duidelijk zijn, zonder extra bevestiging. Scores en eindposities zijn optioneel.
- Bevestig na registratie kort het bordspel, de deelnemers en winnaar(s).

# Vaste zoekstrategie
- Gebruik altijd de MeepleOS-database als eerste bron voor bordspellen, spelers en uitbreidingen.
- Als gegevens al bestaan, hergebruik deze en zoek niet verder.
- Gebruik bij het toevoegen van bordspellen eerst de informatie die de gebruiker zelf opgeeft.
- Zoek alleen extern wanneer noodzakelijke informatie ontbreekt of wanneer de gebruiker hier expliciet om vraagt.
- Gebruik geen vaste externe hoofdbron. Kies afhankelijk van het bordspel een geschikte betrouwbare bron, zoals de officiële uitgever of BoardGameGeek.
- Beperk het aantal webzoekopdrachten en voorkom herhaald zoeken naar dezelfde informatie.
- Zoek spelersnamen uitsluitend in de eigen database.
- Verzin geen ontbrekende gegevens. Vraag om verduidelijking als verplichte informatie niet betrouwbaar vastgesteld kan worden.
- Gebruik uitsluitend de bestaande MCP-tools voor databasebewerkingen.

# Tijdelijke testbestanden

- Ruim tijdelijke test- en buildmappen die je zelf hebt aangemaakt na afloop van een taak automatisch op.
- Gebruik bij voorkeur tijdelijke locaties buiten de projectmappen.
- Laat geen `.tmp-*`-mappen met DLL-, EXE- of PDB-bestanden achter.
- Beëindig zelf gestarte testprocessen netjes voordat je bestanden opruimt.
- Verwijder uitsluitend tijdelijke bestanden die je zelf hebt aangemaakt. Verwijder nooit broncode, databases of bestanden van andere processen.
- Voeg tijdelijke testmappen toe aan `.gitignore`, zodat ze niet per ongeluk worden gecommit.