# KI-Client: Performance und Robustheit

Stand: 06.10.2026; Abnahme von KI-Client-TODO 09.

## Ergebnis und Entscheidung

Ein gemeinsamer Bot kann zwei KI-Armies mit demselben Controllercode und gemeinsamem Planungsbudget betreiben. Das verringert die Entscheidungsarbeit des Hosts. Er benötigt aber eine vollständige Weltkopie; zusätzliche Bot-Prozesse erhöhen CPU- und Replikationskosten. Deshalb bleibt die Zuweisung vorerst manuell. Der Nutzen einer automatischen Verteilung (Aufgabe 10) ist auf dieser Messbasis nicht ausreichend belegt. Dedicated-Server (11) bleibt unabhängig davon vorgesehen.

## Reproduktion

Im Repository-Verzeichnis in PowerShell:

```powershell
dotnet build RTS.csproj
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj
& tests/GridNavigationChecks/RunAIClientBenchmark.ps1 -OutputDirectory "$env:TEMP/RTS-ai-comparison"
& tests/GridNavigationChecks/ExportAIClientBenchmark.ps1 `
    -InputDirectory "$env:TEMP/RTS-ai-comparison" `
    -OutputFile "$env:TEMP/RTS-ai-comparison-summary.json"
```

Standard: vier Vergleiche à 75 Sekunden, anschließend 300 Sekunden Stress. Einzelvarianten lassen sich mit `-Modes host,shared`, Dauer mit `-Seconds` (mindestens 60) und `-StressSeconds` (mindestens 180) wählen. `-SkipBuild` setzt aktuelle Builds voraus. Jeder Lauf erzeugt result.json, Host-/Bot-Messberichte und Logs in einem eigenen Unterverzeichnis. Der Export bewahrt diese Werte samt CPU-Daten; Ergebnisse dürfen nicht durch eine einzelne FPS-Zahl ersetzt werden.

Der grafiklose Testhost verwendet den normalen NetworkHost, SessionStateService, Army-/Weltupdates und echte CPU-importierte bbmodel-Metadaten. Entfernte Teilnehmer sind unveränderte Produktionsprozesse `RTS.dll --bot-client`, ohne Fenster, Dummy-HUD oder reflektiertes RTSGame. Nur die Hostfixture initialisiert ihren vorhandenen Test-RTSGame-Kontext.

Vergleichswelt: flaches 129×129-Rock-Terrain, zwei gegnerische Armies, gleiche versetzte Startausstattung (Base, Refinery, Barracks, Bulldozer, Harvester, 8×8 Ressourcenfeld), 30.000 Ressourcen. Profil BalancedAssault, Seed 1829856032, Match-Seed 1234. Normale Echtzeit und Produktionsgeschwindigkeit; game-start im Stresslauf setzt regulär auf neue Bulldozer und 10.000 Ressourcen zurück. Keine künstliche Zeitbeschleunigung. Compute-Config: 2 ms / 2.048 Planungsschritte pro Update; Remote-Vorplanung teilt 0,5 ms / 512 Schritte einmal pro Clientupdate über alle seine Armies.

Varianten: `host` = beide lokal; `one` = eine lokal/eine auf einem Bot; `shared` = beide auf einem Bot; `split` = je eine auf zwei Bots. Verschiedene Netz-/Thread-Timings dürfen bei identischem Seed unterschiedliche Entscheidungen und Requestzahlen ergeben. Die tatsächlichen Fortschritte werden mitgemessen.

## Messvertrag

- Update-Arbeit umfasst Netzwerkinput, Welt-/Army-Simulation, KI und Hostverarbeitung. Grafik und Sleep sind ausgeschlossen; Update-Intervalle enthalten Warte-/Schedulerzeiten. Erste fünf Sekunden sind in den Update-/KI-Quantilen ausgeschlossen. Das sind keine gerenderten Frames und keine FPS-Prognose.
- `AI.Player` und `Path.PlanningUpdate` weisen inklusive Scopes getrennt aus. Verschachtelte Werte überlappen und dürfen nicht addiert werden. Host-AI-Aufrufe bleiben auch bei Remote-Zuweisung bestehen, verlassen dann aber die Entscheidungslogik früh. Scopes und gesamte Prozess-CPU enthalten Warm-up; CPU zusätzlich Modellimport und Start.
- CPU-Sekunden sind Prozessarbeit über alle Threads, nicht Wandzeit. Working Set ist ein momentaner Wert, kein Peak. TCP-Zähler umfassen übertragene Nutzdaten einschließlich JSON/Newline, keine Ethernet-/IP-Header oder UDP-Discovery.
- `heartbeatRoundTripMs` misst einen generationsgebundenen Diagnose-Echo-Roundtrip einschließlich beider Update-Schleifen. Host antwortet nur bei aktivierter RunDiagnostics und gültiger Zuweisung. Keine zusätzliche Gameplay-Freigabe, kein reiner TCP-Ping.
- `requestReplyMs` misst von Receipt-Anlage bis zur ersten gültigen Host-Rückmeldung: einschließlich lokaler KI-Auftragswarteschlange und Host-Planung. Mehrsekündige Werte können Budget-/Queue-Wartezeit sein; sie belegen keine mehrsekündige Netzlatenz. Fertige Produktion wird separat gezählt.
- Diagnose ist opt-in. Reports alle fünf Sekunden und bei kontrolliertem Ende; atomarer Dateiaustausch. Quantile verwenden maximal die ersten 100.000 Samples, Mittel/Maximum alle Samples. Unbeantwortete Requests/Heartbeats sind auf 4.096/512 Einträge begrenzt; eviktierte Einträge liefern keine Latenzprobe. Erzwungener Prozessabbruch hinterlässt den letzten Zwischenbericht.

## Messergebnisse vom 06.10.2026

Zweiter Durchlauf, jeweils 75 Sekunden; fünf Sekunden Update-Warm-up ausgeschlossen. Zeiten in Millisekunden, CPU in Sekunden, Netzvolumen in MiB. Rohdaten: [zweiter Durchlauf](Benchmarks/KI-Client-2026-10-06.json), [erster Durchlauf einschließlich 300-Sekunden-Stress](Benchmarks/KI-Client-2026-10-06-first.json).

| Variante | Host Update Ø / P99 / Max | Host KI gesamt | Host Pfadplanung gesamt | Host CPU | Bot CPU gesamt | Host TCP hinaus | Requests / max. Units |
| --- | --- | --- | --- | --- | --- | --- | --- |
| host | 3.90 / 13.11 / 60.73 | 1233.7 | 1243.7 | 12.64 | 0.00 | 0.00 | 93 / 50 |
| one | 3.32 / 10.10 / 42.20 | 861.3 | 489.7 | 14.78 | 13.83 | 25.53 | 71 / 48 |
| shared | 2.96 / 8.12 / 34.84 | 50.1 | 347.9 | 11.45 | 12.59 | 25.30 | 79 / 46 |
| split | 3.13 / 7.97 / 32.72 | 45.3 | 398.1 | 13.80 | 28.28 | 50.87 | 103 / 48 |

| Bot / Variante | Update Ø / P99 / Max | Anwendungs-RTT Ø / P95 | Erste Request-Antwort Ø / P95 | Controller-Spitze |
| --- | --- | --- | --- | --- |
| BenchBot0-metrics / one | 3.10 / 8.61 / 42.42 | 44.78 / 61.25 | 64.28 / 117.24 | 1 |
| BenchBot0-metrics / shared | 3.16 / 10.12 / 42.36 | 43.56 / 61.70 | 71.05 / 189.51 | 2 |
| BenchBot0-metrics / split | 3.15 / 8.96 / 38.79 | 41.90 / 61.25 | 60.11 / 105.59 | 1 |
| BenchBot1-metrics / split | 3.13 / 8.91 / 53.05 | 44.98 / 61.00 | 67.59 / 116.99 | 1 |

Die Unit-Anzahlen umfassen auch die intern enthaltenen Fahrer/Soldaten. Die vier Varianten schlossen Produktions-/Forschungsaufträge ab und bauten zusätzliche Gebäude. Der erste Durchlauf zeigte dieselbe Richtung: Host-Update-Mittel 3,84 / 3,83 / 3,42 / 3,46 ms für host / one / shared / split; ausgehendes TCP-Volumen 0 / 25,98 / 25,54 / 51,30 MiB. Im ersten Lauf enthielten die Bot-Update-Maxima noch den Snapshot-Warm-up; deshalb dient für direkte Update-Quantile die zweite Tabelle.

## Robustheitsabnahme

Stress testet zwei Armies auf einem gemeinsamen Bot: abgelehnter Befehl mit unpassendem Controller-/Army-Kontext; TCP-Abbruch nach 30 Sekunden; Host-Rückfall; passive Wiederverbindung und explizite neue Generation; später Observer-Beitritt nach 45 Sekunden; Zerstörung eines Reaktors und einer Base als Perk-Provider; zwei autoritative Spielneustarts nach 70/130 Sekunden. Jede der drei Phasen muss neue Requests und Baufortschritt erzeugen. Neue Controller behalten Profil, Seed und Fingerprint.

Spielneustarts verwenden die normale autoritative Start-Command-Erzeugung und Replica-Rekonstruktion. Der Test umgeht die menschliche Console-Request-Prüfung seiner KI-Hostfixture und weist danach explizit Controller zu. Grafischer Konsolenweg, map-publish und HUD sind hier nicht erneut getestet. Der Observer erhält einen vollständigen Snapshot und bleibt ohne Zuweisung passiv.

Abnahmeergebnis: erster Stresslauf 300 Sekunden, 319 Requests, 50 Produktions-/Forschungsabschlüsse, maximal 47 Units; Baufortschritt in allen Phasen (6 / 8 / 10 neue Bau-Commands). Zweiter Stresslauf 180 Sekunden, 134 Requests, 30 Abschlüsse, ebenfalls maximal 47 Units; Phasen 71 / 36 / 27 Requests und 6 / 8 / 8 Bau-Commands. Jeweils Disconnect, passiver Reconnect, neue Zuweisung, später Beitritt, zwei Neustarts und beide Gebäude-/Perk-Veränderungen erfolgreich. Am Ende Generation 8 bei beiden Armies, unveränderter Fingerprint.

Im zweiten Lauf meldete der aktive Bot zwei Snapshots und zwei Spielstarts; der Observer einen Snapshot, zwei Spielstarts und null Controller. Die gezielte Ablehnungsprobe wurde bestätigt. Im ersten Rohdatensatz ist dafür `metrics.Rejections=1` maßgeblich (das damalige result.rejections erfasste die intern konsumierte Rückmeldung nicht); im zweiten Export ist auch result.rejections korrigiert. Beide Läufe enthalten keine Bot-Fehlerlogs. Build ohne Warnungen/Fehler und **1.881 Regressionchecks** bestanden, darunter Statistik-/Grenzwert-, Transportzähler-, Echo-/Generations- und Berichtsschreibprüfungen.

## Grenzen

Alle Prozesse liefen auf demselben Windows-Rechner über TCP-Loopback (Xeon W-2145, 8 Kerne/16 logische Prozessoren). Dies ist kein LAN-/WAN-Vergleich, kein Test schwacher fremder Rechner, kein Dedicated-Server und keine grafische FPS-Abnahme. Zwei Messdurchläufe erlauben eine Richtungsaussage, keine statistisch abgesicherte Leistungszusage. Andere Karten, mehr Armies und deutlich größere Armeen können andere Engpässe zeigen.

Bau, Forschung/Produktion und Befehlsfortschritt sind belegt. Die realen Modellfixtures maßen weiterhin keinen positiven Harvester-Cargo-Wert; diese Abnahme behauptet deshalb keinen funktionierenden Erntekreislauf. Fünf Minuten Belastung und ein wiederholter kürzerer Lauf ersetzen keinen mehrstündigen Multiplayer-Test. Sichtbare Deadlocks, Harvester-Navigation und Rendering müssen separat im Spiel geprüft werden.

Die hostseitige Pfadsuche bleibt beim Host. Auslagerung ist kein Ersatz für begrenzte Pfadsuchjobs oder schlankere Weltreplikation. Vor automatischer Verteilung sollten repräsentative grafische Spielszenarien auf zwei Rechnern gemessen werden; die jetzigen reproduzierbaren Werkzeuge liefern dafür eine Ausgangsbasis.
