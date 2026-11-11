# MeepleOS: natuurlijke taal en MCP-tools

- Raadpleeg vóór het registreren van een potje `get_boardgames` en `get_players`. Koppel genoemde namen eerst aan bestaande bordspellen en spelers.
- Leid deelnemers logisch af uit de opdracht en de spelerslijst. Als Jeroen, Pallas en Roan afzonderlijk geregistreerd staan, betekent "met Jeroen Pallas en Roan" waarschijnlijk drie deelnemers. Houd ook rekening met geregistreerde spelers met een samengestelde naam; splits namen niet automatisch op spaties.
- De gebruiker is speler Jeroen. Interpreteer "ik", "mij" en "mezelf" als Jeroen en tel hem niet dubbel als Jeroen ook bij naam genoemd wordt.
- Stel alleen bij een relevante, onopgeloste dubbelzinnigheid één korte, gerichte verduidelijkingsvraag. Vraag niet opnieuw naar gegevens die al bekend zijn.
- Gebruik `update_player` uitsluitend als de gebruiker expliciet vraagt een bestaande speler te wijzigen. Maak geen speler aan op basis van een onzekere naaminterpretatie.
- Registreer met `record_game_session` zodra bordspel, deelnemers, winnaar(s) en speeldatum voldoende duidelijk zijn, zonder extra bevestiging. Scores en eindposities zijn optioneel.
- Bevestig na registratie kort het bordspel, de deelnemers en winnaar(s).
