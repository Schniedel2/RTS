# Dedicated-Server: Arbeitsplan und Betrieb

Umsetzung von KI-Client-TODO 11. Ein eigener RTS-Prozess verwendet zunächst dieselbe Assembly. Eine separate Engine-Projektaufteilung würde aktuell Grafiktypen und Modellbibliothek künstlich duplizieren; die Runtime trennt stattdessen ihren Einstiegspunkt und ihre expliziten Simulation-Abhängigkeiten.

Arbeitsplan:
1. GPU-freien Kartenimport mit identischem PNG-/JSON-Vertrag bereitstellen.
2. Host-Pricing, Team-/Skin-Vergabe und Match-Pacing vom grafischen RTSGame lösen; Host-Dienste wiederverwenden.
3. Konfigurierten Server-Lifecycle mit eigenem Spieler-/KI-Register, Start, Neustart, Late Join und lokalen Adminbefehlen bauen.
4. Lokale/entfernte KI über bestehende Generationen und Rückfall betreiben.
5. Echte Mehrprozess- und Belastungstests mit Produktions-Server, menschlichem Clientprotokoll und Bot ausführen; Grenzen dokumentieren.

## Start und Verbindung

Zuerst in `Config/AI/dedicated-server.example.json` den Kartenpfad und die Slots passend zum Level setzen. Die Karte benötigt mindestens `maximumPlayers + aiPlayers.length` gültige PlayerStart-Marker mit PlayerSlot. `maximumPlayers` zählt Menschen; Bot-Peers verbrauchen keinen Spielerstart. Der Server selbst ist kein Spieler und erhält keine Army.

```powershell
dotnet build RTS.csproj
dotnet bin/Debug/net9.0/RTS.dll --dedicated-server Config/AI/dedicated-server.example.json
```

Menschen verbinden sich über den vorhandenen Session-/Join-Konsolenweg im Spiel. Der Server kündigt die Session per bestehender Discovery an; direkte Verbindung zur Adresse und dem konfigurierten Port funktioniert ebenfalls. Ein ausdrücklich belegter Port führt zum Startfehler; es wird nicht unbemerkt auf einen anderen Port ausgewichen. Grafiklose Bots verbinden sich wie bisher mit `--bot-client` und warten auf Zuweisung.

Windows ist zunächst die unterstützte Plattform: Der CPU-PNG-Import verwendet das bereits vorhandene System.Drawing-Paket. Es werden keine Game1-/RTSGame-/HUD-Objekte, GraphicsDevice oder Texturatlanten erzeugt. Die Modelle werden über denselben Importer mit `loadTextures:false` geladen. PNG-Terrain, Gameplay-Marker, map-objects.json und Tiberiumzustände verwenden das vorhandene Kartenformat. Eine eigene Server.csproj ist für diese Stufe nicht nötig; die Assembly enthält weiter Grafikcode, der im Serverpfad nicht aufgerufen wird.

## Config (Schema 1)

JSON-Namen sind camelCase, unbekannte/doppelte Felder und ungültige Werte werden abgelehnt. Relative Datei-/Kartenpfade beziehen sich auf die Config-Datei, nicht das Arbeitsverzeichnis.

| Feld | Bedeutung |
| --- | --- |
| schemaVersion | Pflicht; 1 |
| mapDirectory | Pflicht; Verzeichnis einer gespeicherten Karte mit Terrain-PNGs und optionalen JSON-Komponenten |
| port | 1..65535; Default 27000 |
| sessionName | 1..32 Zeichen; Default RTS Server |
| maximumPlayers | 0..32 menschliche Slots; Default 4 |
| requiredPlayers | 0..maximumPlayers; Default 1; bei when-ready Startschwelle |
| startMode | when-ready oder manual; Default when-ready |
| allowLateJoin | Default true; Menschen dürfen nach Start beitreten; Bots dürfen für Controllerwechsel weiterhin beitreten |
| matchSeed | Default 1234; effektive KI-Profile werden daraus aufgelöst und bei Neustart beibehalten |
| aiPlayers | Liste mit name, profileId (Default balanced-assault) und team (Default 0 = eigenes freies Team); maximal 32 |
| controlFile | Optional; lokal angehängte Adminzeilen, etwa server-control.txt |
| statusPath | Optional; atomar ersetzter JSON-Zustandsbericht mit Port, Roster, Peers, Controllern, Armies und Units |
| diagnosticsPath | Optional; CPU-/Update-/KI-/Pfadsuch-/TCP-Diagnose wie beim Bot |

Control-, Status- und Diagnosepfad müssen verschieden sein und dürfen die Config nicht ersetzen. Beispiel: `"controlFile":"server-control.txt", "statusPath":"server-status.json"`. Reports werden alle fünf Sekunden und beim kontrollierten Ende geschrieben. Sie sind Diagnose, keine Spielstanddatei.

## Spielstart und Lifecycle

`when-ready` startet genau einmal, sobald genügend menschliche Teilnehmer aufgenommen wurden. Mit requiredPlayers=0 und mindestens einer KI kann ein reines KI-Spiel starten. `manual` wartet auf den lokalen Befehl start. Während der Lobby werden Simulation und Ressourcengrowth eingefroren; Netzwerk und Roster bleiben aktiv.

Start/Neustart publiziert zuerst die aktuelle Karte, dann den normalen autoritativen Start-Command. Aktive Spieler und konfigurierte KIs erhalten neue Bulldozer samt Fahrer sowie 10.000 Ressourcen; bisherige Einheiten und transienter Zustand werden über SessionStateService zurückgesetzt. KI-Profile/Seed bleiben erhalten, Controllergenerationen werden erneuert und die KIs starten auf dem Host. Nach Neustart kann wieder explizit ausgelagert werden.

Late Join erhält vollständige Spieler-, Army-, Welt-, Sicht- und Unit-Snapshots. Ein später Mensch erhält sofort eine Army, aber im laufenden Match keine zusätzlichen Startressourcen/Units; er nimmt ab dem nächsten Neustart teil. Rejoins mit derselben Peeridentität behalten ihre aktuelle Army, einschließlich Zusammenlegung. Beim Disconnect wird der menschliche Slot freigegeben; vorhandene Units bleiben bis zum regulären Neustart bestehen. Ein neuer Prozess mit neuer Peer-ID übernimmt nicht automatisch eine alte Army.

Bots sind keine zusätzlichen Spieler. Assignments, Profilbestätigung, Kapazitätsgrenze, Heartbeats, Generationen und Host-Rückfall verwenden unverändert die bestehenden Dienste. Kein automatisches Lastverteilen. Nur lokale Administration darf Start/Neustart/Zuweisung auslösen; ein Netzwerk-CommandToHost mit gleichnamigem Text ist kein Adminbefehl.

## Lokale Adminbefehle

Im startenden Terminal eine Zeile eingeben; alternativ an controlFile anhängen. Eingabe-Threads stellen nur Text in eine Queue, Ausführung erfolgt im Simulationsthread.

```text
status
start
assign AI1 RemoteBot
assign AI1 host
restart
stop
```

`assign <AI-Name|Id> <Peer-Name|Id|host>` weist ausschließlich einen bereits verbundenen Peer zu. IDs aus status können für Namen mit Leerzeichen verwendet werden. Eine volle gemeldete Bot-Kapazität wird durch das bestehende Register abgelehnt. status zeigt Spieler-/Peer-IDs und schreibt den optionalen Bericht. stop oder Strg+C beendet den Prozess kontrolliert und trennt Clients; EOF auf stdin allein beendet ihn nicht.

controlFile ist optional. Vor Prozessstart vorhandene Zeilen werden ignoriert, damit alte Neustartbefehle nicht erneut ausgeführt werden. Während des Betriebs angehängte Zeilen werden einmal gelesen; bei Kürzung beginnt die Datei wieder bei Zeile 0. Dies ist eine lokale Test-/Adminschnittstelle, keine Netzwerk-Konsole.

## Reproduzierbare Abnahme

```powershell
dotnet build RTS.csproj
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj
& tests/GridNavigationChecks/RunDedicatedServer.ps1 -Seconds 300
```

Das Skript erzeugt eine eigene flache 129×129-PNG-Karte mit vier Starts, einer Tiberiumsource und 100 Ressourcenzellen. Der **Produktionsprozess RTS.dll --dedicated-server** läuft aus einem fremden Arbeitsverzeichnis. Zwei menschliche Netzwerkidentitäten verbinden sich ohne BotOffer, verwenden NetworkClient/PlayerCommandService und rekonstruieren den normalen Sessionzustand. Der erste Spieler kauft und baut in jeder Matchphase eine Base und sendet Bewegungsbefehle. Der zweite tritt spät bei. Ein weiterer Mensch wird bei vollen Slots abgelehnt.

Zwei konfigurierte KIs spielen zunächst auf dem Server; eine wird einem echten Produktions-Bot zugewiesen. Bot-Abbruch löst Host-Rückfall aus; der neu gestartete Bot wird erneut explizit zugewiesen. Zwei Neustarts müssen auf beiden Spieler-Verbindungen ankommen und danach neue Bauaufträge erzeugen. Ein fremder Unit-Befehl wird abgelehnt. Kontrolliertes stop muss Exitcode 0 und einen abschließenden Status erzeugen. Prozesse/Logs bleiben unter dem ausgegebenen Testverzeichnis nachvollziehbar.

Die Unitchecks prüfen unter anderem Konfigurationsfehler, feste/belegte Ports, Lobbyfreeze, CPU-Kartenimport bei Globals.Game=null, Ressourcen/Startunits, neue Generationen, Army-Merge/Rejoin und Army-/Team-Updates für Headless-Empfänger. CommandCenter-Ziele sind als gemeinsame Funktion ausgezogen; Pricing verwendet denselben PricingService mit den expliziten Host-Armies. Es gibt keine zweite Gameplay- oder KI-Implementierung.

## Grenzen

Dies ist eine grafiklose TCP-Loopback-Abnahme auf einem Windows-Rechner. Die menschlichen Clients im automatisierten Test verwenden den normalen Spieler-Vertrag, aber keinen gerenderten Game1-Prozess. Grafische Bedienung, LAN/WAN, mehrstündige Sessions, OS-Service-Installation, persistente Wiederaufnahme nach Serverabsturz und Linux-Deployment sind nicht als getestet dargestellt. Kurze Läufe belegen Bau-/Produktions-/Controllerfortschritt, keine umfassende Ernte- oder Combat-Balance-Abnahme. Der Server bleibt ein einzelner autoritativer Simulationsthread; KI-Verteilung lagert nur Entscheidungen aus.

## Abnahmeergebnisse vom 06.10.2026

Build ohne Warnungen/Fehler; **1.913 Regressionchecks bestanden**. Mehrprozessläufe über 240 und 300 Sekunden erfolgreich. Pro Lauf: drei Matchphasen mit zwei Neustarts, drei durch den menschlichen Client gekaufte/konstruierte Bases und drei bestätigte Bewegungsbefehle; Bot-Abbruch, Host-Rückfall und erneute Zuweisung; später menschlicher Beitritt; fremder Unit-Befehl abgelehnt; kontrolliertes Prozessende mit Exitcode 0.

Erster Lauf: maximal 42 registrierte Units, 11 / 9 / 11 Bau-Commands in den Phasen. Zweiter Lauf: maximal 44 Units, 11 / 9 / 13 Bau-Commands; volle menschliche Slots abgewiesen und neue Army beim bestehenden Client bestätigt. Beim zweiten Lauf jeweils zwei Session-Snapshots auf den menschlichen Verbindungen; drei Start-Commands beim ersten Spieler, zwei beim spät beigetretenen. Der Produktions-Bot erhielt Generation 2 und nach Neustart seines Prozesses Generation 4 mit gleichem Profil-/Seed-/Fingerprint. Nach Matchneustarts spielten beide KIs wieder auf dem Server weiter. Unit-Anzahlen enthalten auch eingebettete Fahrer/Soldaten.

Ergebnis- und Diagnosewerte: [Dedicated-Server-2026-10-06.json](Benchmarks/Dedicated-Server-2026-10-06.json). Die beiden Testverzeichnisse enthalten die vollständigen Prozesslogs und die erzeugten Karten/Configs; Pfade stehen im Ergebnisarchiv. Die zusätzliche Army-Merge-/Rejoin-Zuordnung ist durch fokussierte Regressionchecks belegt. Keine grafische Mehrprozess- oder mehrstündige Abnahme behauptet.
## Start aus der Spielkonsole / spätere Lobby

Der Dienst `LocalDedicatedServer` startet und überwacht ausschließlich eigene Server-/Bot-Prozesse. Er enthält keine HUD-Logik und kann später direkt von einer Lobby verwendet werden. Netzwerk-/Spielzustand wird weiterhin auf dem Spielthread verarbeitet. Das Spielfenster verbindet sich erst nach der Bereitschaftsmeldung automatisch mit localhost.

```text
server-start test1
server-start test1 2
server-start test1 2 2 27001
server-start Config/AI/dedicated-server.example.json
server-status
game-start
server-bot-start AI1
server-stop
```

Kurzer Start: `server-start <map-name> [AI-count=0] [human-slots=2] [port=auto]`. Die Karte wird aus dem vorhandenen Maps-Verzeichnis geladen; Änderungen zuerst mit map-save speichern. Sie braucht mindestens so viele PlayerStart-Marker wie menschliche Slots plus KI-Slots. Die KIs heißen AI1, AI2 usw. und laufen zunächst auf dem Server. Beim kurzen Start bleibt das Spiel in der Lobby, bis der Besitzer `game-start` ausführt. Andere Spieler verbinden sich über die normalen Session-Befehle. Der Server publiziert die Karte vor dem Matchstart.

Alternativ wird eine vorhandene Server-JSON geladen (relative Pfade ab dem Programmverzeichnis); dabei bleibt deren Startregel erhalten. Private Steuer-/Statusdateien werden vom Dienst erzeugt. `server-status` meldet auch KI- und Peer-Namen. Nach Matchstart kann `server-bot-start AI1` die betreffende KI an einen eigenen lokalen Bot-Prozess übergeben. Nach einem Matchneustart liegen die Controller wieder beim Server; Bots werden nicht automatisch neu zugewiesen. Die bisherige hostseitige Konsole `bot-client-start` bleibt für normale Spielhosts bestehen.

`server-stop` fordert ein kontrolliertes Beenden an und beendet bei Bedarf nach drei Sekunden nur eigene Prozesse. Beim Schließen des Spielfensters werden ebenfalls die eigenen Kinder aufgeräumt. Das setzt momentan weiterhin eine installierte dotnet-Laufzeit voraus; ein eigenständig ausgeliefertes Serverpaket ist ein späterer Packaging-Schritt. Kein PowerShell-Befehl ist für diesen Ablauf erforderlich.

### Console-Testskript

`call aitest-server` startet Karte `test` mit zwei KIs und einem menschlichen Platz und verbindet das Spielfenster. `call aitest-server test1 3` wählt eine andere Karte und drei KIs. Vollständige Parameter: `call aitest-server [map-name] [AI-count] [human-slots] [port]`. Genügend PlayerStart-Marker vorsehen. Das Skript verwendet `server-test-start`: „when-ready“ mit einem erforderlichen menschlichen Spieler, daher beginnt das Match erst nach der Verbindung automatisch. Kein verzögertes blindes `game-start` im Skript. Mit `server-stop` beenden; anschließend kann das Skript erneut aufgerufen werden. KI-Clients können nach Matchstart über `server-bot-start AI1` gestartet werden.

Portwahl: Die kurzen `server-start`-/`server-test-start`-Aufrufe ohne Port verwenden jetzt `autoSelectPort=true`. Der Server bindet selbst den ersten freien Port zwischen 27000 und 27010; der Launcher liest den tatsächlich gebundenen Port aus der Bereitschaftsmeldung/Statusdatei und verbindet darauf. Ein bereits laufender Spielhost auf 27000 muss dafür nicht vorzeitig beendet werden. Explizite Ports und JSON-Konfigurationen bleiben standardmäßig fest; bei JSON kann `autoSelectPort: true` gewählt werden. Falls der ganze Bereich belegt ist, meldet der Server weiterhin einen Startfehler.

Fehlerdiagnose: Lokale Server-/Bot-Ausgaben werden zusätzlich dauerhaft unter `%TEMP%/RTS-ServerLogs/<session-id>.log` gespeichert. Der Pfad wird beim Start und bei `server-status` nach einem Abbruch angezeigt. Bei einem Prozessende wird zuerst die verbleibende Fehlerausgabe abgeholt; das Protokoll bleibt auch nach dem Aufräumen der privaten Steuerdateien erhalten.

Behoben am 06.10.2026: `NotifyUnitsSelected` darf auf dem Dedicated-Server keine grafischen RemoteSelections in `Globals.Game` aktualisieren. Die Auswahlmeldung wird weiterhin an Clients weitergeleitet; nur grafische Clients aktualisieren die Markierungen. Regression prüft Auswahl/Abwahl ohne Game-Instanz sowie echten menschlichen Client: Auswahlmeldung, danach Bauauftrag und tatsächliche Bulldozer-Bewegung.

Behoben am 06.10.2026: Der Launcher liest `status.json` mit `FileShare.ReadWrite | FileShare.Delete`, damit die atomare Ersetzung unter Windows trotz geöffnetem Leser funktioniert. Sämtliche Launcher-Statusleser verwenden denselben Leseweg. Nicht verfügbare Statusdateien werden beim nächsten Update erneut gelesen. Der Server behandelt IOException/UnauthorizedAccessException beim Statusreport als Diagnosefehler, behält den vorherigen vollständigen Report und läuft weiter; der nächste Report versucht erneut zu schreiben. Regression hält die Statusdatei absichtlich ohne Delete-Freigabe geöffnet, prüft fortlaufende Simulation und anschließende Wiederherstellung der Veröffentlichung.
