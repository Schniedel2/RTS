# Fensterloser KI-Bot-Client

Stand: 06.10.2026. Aufgabe 08 aus KI-Client-TODO.md, Netzwerkprotokoll 11.

## Start und Zuweisung

Der bestehende RTS-Prozess besitzt einen frühen Bot-Einstieg, bevor Game1/MonoGame gestartet wird. Ein zweites Projekt ist dafür noch nicht nötig. Der Bot ist ein normaler TCP-Client mit eigener Peer-ID; er ist kein eigener Spieler mit einer automatisch erzeugten Army und kein Dedicated-Server.

Beispiel aus dem Repository-Verzeichnis:

```powershell
dotnet run --project RTS.csproj -- --bot-client Config/AI/bot-client.example.json
```

Oder nach dem Build, unabhängig vom Arbeitsverzeichnis:

```powershell
dotnet D:\Repos\GameDev\RTS\bin\Debug\net9.0\RTS.dll --bot-client D:\Repos\GameDev\RTS\Config\AI\bot-client.example.json
```

Serveradresse/Port in einer eigenen Kopie der Beispieldatei anpassen. Modelle und maschinenlokale KI-Rechenlimits werden aus dem Ausgabeverzeichnis geladen. Das gesamte Content/Models-Verzeichnis muss vorhanden sein. Die Konfiguration enthält keine Zugangsdaten, Player-/Army-IDs oder Selbstzuweisung.

Auf dem Host erst eine Session öffnen und die gewünschte KI-Army erstellen bzw. game-start durchführen. Danach:

```text
session-members
ai-list
ai-controller-assign <AI-Name> RTS-Bot
ai-controller-list
```

Die normale Zuweisung erhält das bisherige Host-Profil samt Seed. Mit dem optionalen dritten Argument `proposed` bestätigt der Host ausdrücklich den Profilvorschlag des Bots und löst dessen Werte aus dem **Host**-Profilkatalog auf:

```text
ai-controller-assign <AI-Name> RTS-Bot proposed
```

session-members zeigt Bot-Kapazität und Profilvorschlag. Der Bot startet Entscheidungen erst nach vollständiger Snapshot-Synchronisation und zugewiesener Army. Er kauft/baut/steuert über RemoteAIRuntime, dieselben KI-Controller und PlayerCommandService wie der normale Client. Gameplay und Validierung bleiben auf dem Host. game-start vergibt zunächst wieder Host-Controller; danach bei Bedarf neu zuweisen.

## Schnelltest direkt in der Spielkonsole

Bei laufender Host-Session und vorhandener KI-Army reicht:

```text
bot-client-start AI1
```

Der Host startet einen lokalen Bot ohne Fenster, ermittelt seinen tatsächlichen TCP-Port und weist nach dem Beitritt automatisch AI1 mit dem bisherigen Profil/Seed zu. Die Konsolenbedienung wartet nicht blockierend. Logs werden begrenzt auf dem Spielthread ausgegeben. Mehrere KI-Armies lassen sich mit je einem eigenen Aufruf auslagern; doppelte Starts für dieselbe KI werden abgelehnt.

```text
bot-client-stop AI1
bot-client-stop all
```

Stop beendet ausschließlich die hier gestarteten Prozesse; der bestehende Disconnect-Weg gibt die KI wieder an den Host. Sessionwechsel und Schließen des Spiels räumen lokale Bots ebenfalls auf.

Zum vorhandenen Testskript gibt es eine Ergänzung:

```text
call aitest-bot
```

`aitest-bot.batch` ruft die unveränderte `aitest.batch` auf und startet anschließend den Bot für AI1. Alternativ am Ende der eigenen Batch-Datei `bot-client-start AI1` ergänzen. Eine Host-Session muss vorher bereits offen sein, wie bei aitest selbst.

Optional: `bot-client-start AI1 Config/AI/bot-client.example.json`. Relative Config-Pfade beziehen sich auf das Ausgabeverzeichnis. Bei diesem lokalen Start werden Adresse/Port, eindeutiger Anzeigename und reconnect=false passend zum laufenden Host gesetzt; weitere Config-Werte bleiben erhalten. Ein Profilvorschlag ändert das aktuelle Host-Profil nicht automatisch.

## Prozess-Konfiguration

SchemaVersion 1; JSON-Namen in camelCase. Unbekannte/fehlende Pflichtfelder, doppelte Felder, null, falsche Typen und ungültige Werte werden mit Datei-/Feldangabe abgelehnt.

| Feld | Bedeutung |
| --- | --- |
| schemaVersion | Pflicht; 1 |
| serverAddress | Pflicht; Adresse/DNS-Name, 1..253 Zeichen |
| port | Pflicht; TCP-Port, 1..65535; Standard-Session startet gewöhnlich bei 27000 |
| displayName | Pflicht; eindeutiger Name, 1..32 Zeichen, ohne äußere Leerzeichen |
| proposedProfileId | Optional; null oder eines der vier bekannten Profil-IDs; nur ein Vorschlag |
| maximumArmies | Optional; 1..32, Default 1; Host und lokale Laufzeit begrenzen gleichzeitige Übernahmen |
| diagnosticsPath | Optional; Dateipfad für JSON-Diagnose, Default null (aus); pro Prozess einen eigenen Pfad wählen |
| reconnect | Optional; Default true; erneuter Verbindungsversuch nach mindestens drei Sekunden |

Die Auswahl einer Config ist ausdrücklich erforderlich. Eine fehlerhafte Datei startet keinen Ersatz-Bot. Beenden mit Strg+C im startenden Terminal; Dispose räumt lokale Controller/Claims/Queues und Transport auf. Exit-Code 0 bei kontrolliertem Ende, 1 bei Start-/Laufzeitfehler, 2 bei Verbindungsende und deaktiviertem Reconnect. Der Bot erzeugt kein Spiel-Fenster; Standardausgabe/Standardfehler bleiben für Terminal oder umgeleitete Logs verfügbar.

Bei Disconnect räumt die Laufzeit ihre Entscheidungen auf. Wiederverbindung synchronisiert eine neue Welt und wartet auf eine neue Host-Zuweisung. Das Rückfallverfahren aus Aufgabe 07 erhält bestätigte Gameplay-Jobs und übernimmt auf dem Host. Prozessabbruch wird ebenfalls über TCP-Disconnect/Heartbeat erkannt.

## Modell- und Profilvertrag

BBModelLoader/MeshHandler verwenden beim Bot `loadTextures: false`. Der identische Geometrie-/Hierarchie-/Animationsimport behält Modellskalierung, Footprint/Clearance und Pivots. Er liest keine Bilddaten in GPU-Atlanten und benötigt weder GraphicsDevice, SkinHandler noch Dummy-HUD. Texturregionen dienen hier ausschließlich als CPU-Platzhalter für die vorhandene Face-Geometrie; dieser Modus ist nicht zum Rendern vorgesehen. Headless-Welten emittieren keine visuellen Partikel.

Die Weltgröße stammt aus dem Session-Snapshot. Welt, Netzwerkinput und KI laufen auf demselben Thread. Transport-Threads stellen Nachrichten weiterhin nur in die Inbox. Tiberium-Zeit, Army-Zugriff für Sicht/Perks und Netzwerkdiagnose benötigen in diesem Pfad kein RTSGame/GameConsole-Objekt mehr. Die verbliebenen Globals für Mesh-Bibliothek/Welt werden regulär initialisiert; dies ist keine vollständige Engine-/Globals-Migration.

Der Host sendet die unveränderlichen **aufgelösten** Profilwerte, Profilversion 1 und SHA-256-Fingerprint einschließlich Seed. Der Empfänger validiert Version/Fingerprint; lokale Profil-Dateien überschreiben die bestätigten Werte nicht. Fingerprint ist Konsistenzdiagnose, keine Berechtigung. Army/Akteur/Peer/Generation autorisieren weiterhin die Requests. Eine volle Bot-Kapazität lehnt der Host vor der Zuweisung ab; bestehende Übernahmen bleiben erhalten. Automatische Verteilung bleibt Aufgabe 10.

## Reproduzierbare Prüfungen

```powershell
dotnet build RTS.csproj
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj
& tests/GridNavigationChecks/RunBotClient.ps1
& tests/GridNavigationChecks/RunBotClient.ps1 -Reconnect
```

1.867 Regressionchecks bestanden (28 zusätzliche Prüfungen für Konfiguration, Fingerprint/Version, reale Modell-Metadaten, Import-Parität und deaktivierte Grafik-Emissionen).

Zwei echte Prozesse: Der Host läuft als Testfixture mit normalem NetworkHost; der **unveränderte Produktionsprozess RTS.dll --bot-client** wird aus einem fremden Arbeitsverzeichnis gestartet. Beide verwenden echte .bbmodel-Geometrie. Der Bot hat keine reflektierten RTSGame-/Console-/HUD-Objekte. Der Host weist manuell zu und führt keine parallele hochstufige KI aus.

Normaler Lauf: 35 Sekunden Echtzeit, beschleunigter Host; zwei Bauaufträge (Reaktor/Fahrzeugfabrik fertig), elf Trainingsrequests, Bewegungs-/Ernte-/Crew-/Squad-Aufträge und aktive Heartbeats. Eine zweite Army über die gemeldete Kapazität hinaus wurde abgelehnt. Nach Host-Ende beendet sich der Bot bei reconnect=false.

Reconnect-Lauf: Verbindung nach zwölf Sekunden getrennt, Host fällt auf Generation 2 zurück. Der neu synchronisierte Bot meldet controllers=0 und bleibt ohne neue KI-Requests passiv. Nach manueller erneuter Zuweisung läuft Generation 3 mit demselben Profil/Seed/Fingerprint weiter und produziert unter anderem Tank/Gepard. Das Testskript beendet den reconnect=true-Prozess am Testende zwangsweise; kontrolliertes Disconnect-Ende wird im normalen Lauf geprüft.

Grenzen: Kein grafischer Multiplayer-/Langzeittest oder Performancevergleich. Im 35-Sekunden-Szenario wurde kein positiver Cargo-Fortschritt gemessen; es belegt Bot-Prozessstart, reale Metadaten, Bau/Produktion, Netzwerksteuerung und Lifecycle, keine neue Harvester-Bewegungsabnahme. Performance und mehrere Armies folgen in 09, Dedicated-Server in 11.

Zusätzlich geprüft: Der Konsolen-Startdienst LocalBotProcesses startete den echten Bot, wies nach TCP-Beitritt automatisch zu, lehnte einen Doppelstart ab und gab nach Stop die Steuerung an den Host zurück. Bau und Produktion liefen währenddessen weiter. Der grafische Konsolenklick wurde nicht separat getestet. Reproduktion des Diensttests:

```powershell
$runDirectory = Join-Path $env:TEMP ('RTS-console-bot-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $runDirectory | Out-Null
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj -- --bot-host $runDirectory launch
```

## Opt-in-Messungen (Aufgabe 09)

Mit `"diagnosticsPath": "bot-metrics.json"` schreibt der Bot alle fünf Sekunden und beim kontrollierten Ende einen atomar ersetzten Bericht. Relative Pfade beziehen sich auf das Prozess-Arbeitsverzeichnis. Ohne dieses Feld werden keine Samples gesammelt. Gemessen werden CPU-Zeit, Working Set, TCP-Bytes, KI-/Pfadsuch-Scopes, Update-Arbeit und -Intervalle, Request-Rückmeldungen, Inbox-Spitzen sowie Controllerzahl und Snapshot-/Spielstartzahl.

Host-Testläufe setzen zusätzlich `NetworkHandler.RunDiagnostics`. Nur dann antwortet der Host auf gültige generationsgebundene Controller-Heartbeats mit einem gezielten Diagnose-Echo. Dieses liefert Anwendungs-Roundtrip einschließlich beider Update-Schleifen; es ist kein reiner TCP-Ping. Die erste Request-Antwort enthält zusätzlich lokale Auftragswarteschlange und Host-Planung. Beide Zeiten werden getrennt ausgewiesen. Keine Änderung am Berechtigungs-/Fallback-Vertrag.

Details, Vergleich und reproduzierbares Mehrprozess-Skript: [KI-Client-Performance.md](KI-Client-Performance.md). Die grafische FPS und ein mehrstündiger Spieltest bleiben separate Prüfungen.
