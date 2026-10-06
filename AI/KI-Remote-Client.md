# KI auf einem normalen zweiten Client

Stand: 06.10.2026, KI-Client-TODO Punkte 06/07. Netzwerkprotokoll 10; beide Prozesse benötigen denselben Build.

## Manuell ausprobieren

1. Zwei normale Spielinstanzen starten, Host und Client wie bisher mit `session-host` / `session-join` verbinden. KI-Spieler auf dem Host erstellen und das Spiel starten.
2. Auf dem Host mit `ai-list` die KI und mit `session-members` den Ziel-Client ermitteln. Für Namen mit Leerzeichen stattdessen die angezeigten IDs verwenden.
3. Auf dem Host: `ai-controller-assign <KI-Name-oder-ID> <Peer-Name-oder-ID>`. Eindeutige ID-Präfixe sind möglich.
4. `ai-controller-list` auf beiden Instanzen zeigt Actor, Army, Controller-Peer, Generation, Profil und Seed. Auf dem ausführenden Client zusätzlich Ziel und letzte Entscheidung.
5. Manuell zurückgeben: `ai-controller-assign <KI> host`. Dies erhöht die Generation und verwirft alte lokale Planung. Gameplay-Jobs werden dadurch nicht pauschal abgebrochen.

Die Befehlsnamen sind im normalen Console-Register eingetragen und damit per Tab vervollständigbar. Parameter werden wie bei den übrigen Befehlen nicht vervollständigt. `game-start` weist KI-Spieler erneut dem Host zu; danach bei Bedarf wieder an den Client vergeben.

## Laufzeit und Autorität

`RemoteAIRuntime` erstellt einen normalen AIPlayer/AIController erst bei `Connected` und vorhandener replizierter Army. Derselbe Controller, dieselben Katalogangebote, Preise, Perks und PlayerCommandService-Requests wie auf dem Host werden verwendet. Das bestätigte Profil einschließlich Seed kommt unverändert aus der Host-Zuweisung, statt auf dem Client erneut aus lokalen Dateien gewählt zu werden.

Auf dem Host stoppt die hochstufige KI für diese Army. Bewegung, Ernte, Baufortschritt, Produktion, Kampf und Request-Validierung bleiben dort aktiv. Eine Peer-Verbindung kann mehrere getrennte Army-Controller tragen. Lokale Claims, Fortschrittsmonitor, Budget und Auftragsqueue gehören jeweils dem ausführenden Controller; sie sind keine Referenzen auf Host-Queues.

Client-Queues halten Geld für unbestätigte Requests zurück. Nach Annahme geben sie diese Reservierung frei; der vom Host replizierte Ressourcenstand bleibt maßgeblich. Eine fehlende lokale Produktionsorder ist kein Beweis für Fertigstellung oder Fehlschlag. Abschluss/Ablehnung kommen über RequestReceipt und Host-Rückmeldungen. Fortgesetzte Bauaufträge behalten die ursprüngliche Controllergeneration.

Remote-Bauplatzvorprüfungen laufen inkrementell in derselben fairen ClientPlanning-Warteschlange wie Scouting: höchstens die kleineren Werte aus 512 Schritten / 0,5 ms und der lokalen compute.json-Konfiguration pro Update. Einzelne Placement-/Spacing-Prüfungen bleiben atomar; dies ist keine harte Echtzeitgarantie. Gefundene Kandidaten werden bei Verwendung erneut geprüft, die endgültige Bauentscheidung trifft der Host. Der bestehende Host-Bauplatzsuchweg bleibt unverändert.

Generation-/Sessionwechsel, Widerruf oder fehlender Army-Kontext entfernen die lokalen Controller und deren Planung. Heartbeat, automatischer Rückfall und Rekonstruktion bei Übergaben sind in Punkt 07 implementiert: [KI-Controller-Wechsel.md](KI-Controller-Wechsel.md). Ein wiederverbundener Client braucht eine ausdrückliche neue Zuweisung. Fensterloser Produktiv-Bot und Dedicated-Server bleiben spätere Schritte 08 und 11.

Bestätigte Baubefehle übertragen ihre Army explizit bis zur Gebäuderegistrierung. So hängt die Zuordnung nicht von einem menschlichen Player-Eintrag des virtuellen KI-Akteurs auf dem Client ab.

## Reproduzierbare Prüfung

```powershell
dotnet build RTS.csproj
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj
& ./tests/GridNavigationChecks/RunRemoteAI.ps1
```

Das Skript baut den Harness und startet zwei getrennte dotnet-Prozesse mit echter TCP-Verbindung, Session-Snapshot und Host-Zuweisung. Der Host führt keine hochstufige KI aus. Nur der Client entscheidet. Geprüft werden eingegangene Bau-/Produktions-/Ernte-/Bewegungs-/Angriffsrequests, zusätzliche erkundete Zellen, tatsächlich gesammelte Ladung und Kampfschaden, entstandene Gebäude/Units sowie Host-Ausführungsrückmeldungen und gezielte Ablehnung eines fremden Army-Befehls.

Der Test nutzt eine grafiklose 65×65-Welt, einfache Test-Meshes und teils Katalog-Testgebäude, echte produzierbare Fahrzeuge/Soldaten und die normalen Simulations-/Netzwerkdienste. Die Simulation läuft beschleunigt in 0,25-Sekunden-Schritten bei rund 10 ms Pause. Die Prozesse laufen maximal 38/40 Sekunden; JSON und Fehlerausgaben bleiben in einem ausgegebenen RTS-remote-ai-Verzeichnis unter TEMP. Dies ersetzt keinen grafischen Langzeit-/Performancevergleich oder Test mit den vollständigen Asset-Footprints.

Der vollständige Regression-Harness besteht mit 1.812 Checks, davon 22 neue Remote-Laufzeit-/Queue-/Budgetprüfungen. Der Mehrprozess-Test bestätigt zusätzlich Wirtschaft, Bau, Produktion, Erkundung und Kampf über den entfernten Befehlsweg.

Aktueller Stand nach Punkt 07: 1.839 Regressionschecks und zusätzliche Zwei-Prozess-Tests mit `-Mode disconnect` und `-Mode stall` bestanden; siehe Wechsel-Dokumentation.
