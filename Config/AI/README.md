# KI-Auswahl und lokale Rechenlimits

Seit KI-Client-TODO 03 werden drei unabhängige Konfigurationen einmal pro Prozess geladen.
Alle Dateien liegen zur Laufzeit unter `<AppContext.BaseDirectory>/Config/AI`; Build und Publish kopieren sie dorthin.
Quelldateien ändern, neu bauen und das Spiel neu starten. Kein Hot-Reload und kein JSON-Zugriff im Update.

## Profilwahl: selection.json

- `schemaVersion`: 1.
- `fixedProfileId`: null für gewichtete Auswahl, sonst eine der vier IDs aus Profiles.
- `matchSeed`: null für den bestehenden zufälligen Match-Seed; eine Int32-Zahl überschreibt diesen für alle KI-Armies.
- `balancedAssaultWeight`, `infantryCompanyWeight`, `antiArmorWeight`, `fastReconWeight`: Ganzzahlen 0..10000, Summe größer null. Defaults 40/25/25/10.

Priorität: konfigurierter Match-Seed vor erzeugtem Match-Seed, anschließend bestehender Hash mit ArmyId.
Dieser effektive Seed bleibt am aufgelösten Profil. Eine feste Profil-ID überschreibt die Zufallsauswahl, nicht die Seed-Bildung.
Reproduzierbare Vergleiche benötigen neben gleichem Seed auch gleiche ArmyIds, Karte und Ausgangslage.
Netzlatenz und unterschiedliche verfügbare Rechenzeit können weiterhin Entscheidungszeitpunkte beeinflussen.

Beispiel: `"fixedProfileId": "anti-armor", "matchSeed": 1234`.
Die übrigen Felder dürfen entfallen; dafür gelten ihre dokumentierten Defaults.

## Verhalten: Profiles/*.json

Zusätzlich zu den bisherigen Feldern (siehe [Profil-Dokumentation](Profiles/README.md)):

| Feld | Default | Bedeutung / Grenze |
|---|---:|---|
| decisionIntervalSeconds | 1 | Simulationssekunden 0.25..10. Intervall für Aufbau/Wiederaufbau, Truppvorbereitung und Panzerproduktion; Angriff denkt mit Faktor 0.75 dieses Intervalls. Akute Basisverteidigung, Beobachtung und Auftragsüberwachung behalten ihre bisherigen Intervalle. |
| resourceReserve | 800 | Ressourcen 0..100000. Zentraler OrderQueue-Budgetplaner hält diese Reserve für Produktion/Expansion zurück. Überleben, Strom, Wirtschaft und Verteidigung dürfen sie wie bisher verwenden. |
| scoutReconsiderSeconds | 8 | Simulationssekunden 1..120. Abstand bis zur Neubewertung eines aktiven Scouting-Ziels. Ausgefallene/unerreichbare Ziele verwenden weiterhin ihre gesonderte Fehlerbehandlung. |

Diese drei Erweiterungsfelder sind optional, damit Schema-1-Dateien aus Schritt 02 weiter funktionieren.
Sie ändern echte vorhandene Entscheider. Es werden keine ungenutzten Rollen-Gewichte oder Difficulty-Presets eingeführt.
Ressourcen und Sicht werden durch Profile niemals künstlich erhöht.

## Maschine: compute.json

- `schemaVersion`: 1.
- `planningStepsPerUpdate`: 16..65536, Default 2048.
- `planningMillisecondsPerUpdate`: endliche Zahl 0.1..20, Default 2.

Ein gemeinsames Budget gilt für die vorhandene Round-Robin-Planungswarteschlange der Welt (Navigation, Bauplatzsuche, Scouting).
Es wird nicht pro Army vervielfacht. Unfertige Aufträge wandern ans Ende der Warteschlange; mehrere Armies erhalten weiter Arbeitsscheiben.
Es handelt sich um Arbeitsgrenzen pro Update, keine Garantie für die gesamte Frame-Dauer: ein einzelner Arbeitsschritt und nicht inkrementelle KI-Logik bleiben außerhalb einer harten Zeitgarantie.
Die spätere Client-Scouting-Warteschlange behält ihre bisherigen Obergrenzen 512/0.5ms und berücksichtigt zusätzlich niedrigere lokale Limits.
Explizite Scheduler-Budgets in Headless-Checks überschreiben diese Defaults für reproduzierbare Prüfungen.

## Fehler, Diagnose und Prüfung

Fehlende optionale Dateien nutzen Defaults. Vorhandene ungültige Dateien werden abgelehnt, inklusive unbekannter/doppelter Felder und falscher Versionen.
Explizite LoadSelection/LoadCompute-Aufrufe verlangen eine vorhandene Datei. game-start prüft alle Konfigurationen vor dem Welt-Reset.
`ai-list` zeigt Profil, effektiven Seed, Denkintervall, Reserve, Scouting-Intervall, lokale Planungslimits und bisherige Entscheidungen/Auftragsdiagnose.

Headless-Checks vergleichen alte und neue Default-Auswahl für 100 Seeds, feste/gewichtete Auswahl und Seed-Priorität,
Dateivalidierung, Reservewirkung und faire Arbeit unter reduziertem Budget. Zwei nacheinander ausgeführte KI-Szenarien mit gleichem Seed
werden über 30 Simulationsschritte hinsichtlich Profil, Zielzustand, Ressourcen und Requesttypen verglichen.
Das ist ein begrenzter Headless-Vergleich, kein grafischer Langzeit-/Multiplayervergleich und keine Zusage identischer Netzwerkzeitpunkte.

## Fensterloser Bot-Prozess

`bot-client.example.json` konfiguriert die TCP-Verbindung, den Profilvorschlag und die maximale Army-Anzahl. Start: `RTS --bot-client <config.json>`. Der Host weist die Armies ausdrücklich zu. Bedienung und Grenzen: [KI-Bot-Client](../../AI/KI-Bot-Client.md).
