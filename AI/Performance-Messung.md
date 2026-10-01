# Performance-Messung für KI und Host-Aufträge

Stand: 01.10.2026. Architektur-Aufgabe 03; Grundlage für das Arbeitsbudget in Aufgabe 04. Die grafische Fahrzeugabnahme aus Aufgabe 02 bleibt separat offen. Diese Instrumentierung ergänzt den Punkt „AI-Leistungsbudget messen“ der AI-Spieler-TODO. Aufgabe 04 ergänzt inzwischen ein gemeinsames Arbeitsbudget für die Pfadplanung; andere KI-Arbeiten bleiben separat zu bewerten.

## Bedienung im Spiel

- `telemetry-start`: bisherige Werte löschen und detaillierte Messungen einschalten.
- `telemetry`: Bericht in der Konsole anzeigen.
- `telemetry-stop`: Aufzeichnung beenden, Ergebnisse erhalten.
- `telemetry-save`: Ergebnisse samt UTC-Zeit, Laufzeit, Betriebssystem, Unit- und Framezahl unter `Diagnostics/performance-<Zeit>.txt` neben der ausführbaren Datei speichern. Der konkrete Pfad wird ausgegeben.
- `telemetry-reset`: Werte löschen; Einschaltzustand erhalten.

Die Namen sind per Tab vervollständigbar. Die entsprechenden Parameter des bestehenden `telemetry`-Befehls funktionieren ebenfalls. Detaillierte Messungen sind standardmäßig ausgeschaltet. Die bereits vorhandenen einfachen Globals-Telemetriezähler bleiben verfügbar. Es gibt keine Ausgabe pro Frame.

## Messgrenzen

`AI.Player` umfasst einen KI-Update einschließlich der aufgerufenen Controller. Separate Bereiche existieren für ArmyGoals, BaseDefense, ArmoredSupport, Infrastructure, DefensePlanner, ProductionPlan, ThreatAssessment, SquadPreparation, SquadRecovery, SquadAssault und Scouting. Auch frühe Rückgaben zählen als Aufruf; der Mittelwert enthält somit die kurzen Updates zwischen Denkintervallen. Der Maximalwert zeigt einzelne teure Aufrufe.

`AI.BuildSiteSearch` umfasst die gemeinsame Kandidatensuche einschließlich Platzierungs- und Abstandskontrolle. `Host.Request.<Typ>` umfasst Verarbeitung, lokale Anwendung und Einreihung der Broadcasts eines tatsächlich entnommenen Auftrags. Wartezeit in der Eingangsqueue und spätere Socket-I/O liegen außerhalb. Bei fortsetzbaren Gotos umfasst der Request-Scope nur Annahme, Anhalten und Einreihen der Planung. `Path.PlanningUpdate` misst das gemeinsame Planungsbudget, `Host.GotoPlanningSlice` dessen Gruppen-Goto-Arbeit und `Path.JobSlice` die übrigen Suchjobs. `Host.GotoPublish` misst die abschließende Veröffentlichung. `Host.GotoPlanning` und `Pathfinder.Search` bleiben für synchrone Diagnoseaufrufe erhalten. Der separate Suchzähler zählt begonnene Suchen aller Modi.

Jede Zeile enthält Aufrufzahl, Gesamtzeit, Mittelwert, Maximalzeit, insgesamt allokierte Bytes und maximale Bytes pro Aufruf. Zeiten und Allokationen sind **inklusiv**: verschachtelte Zeilen überlappen und dürfen nicht addiert werden. Bytes bezeichnen verwaltete Allokationen auf dem Spielthread, weder dauerhaft belegten Speicher noch Grafik-/Netzwerkspeicher. GC-Pausen können in der Zeit enthalten sein. Scopes werden auch bei Exceptions beendet; ein Reset entwertet laufende Scopes. Alle Zugriffe bleiben auf dem Spielthread. Ein Wechsel des Threads innerhalb eines Scope wird als Fehler erkannt. Messungen verändern keine Befehlsreihenfolge oder Spielentscheidungen.

## Reproduzierbare Baseline

Vom Repository-Verzeichnis, in PowerShell, nacheinander ausführen:

```powershell
dotnet build RTS.csproj -c Release
$env:DOTNET_TieredCompilation = '0'
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj -c Release -- --incremental-baseline AI/Performance-Incremental.md
Remove-Item Env:DOTNET_TieredCompilation
```

Der Harness führt zuerst die Verhaltenstests aus und anschließend das Szenario. Für Vergleiche dieselbe Konfiguration, Laufzeit und Umgebung verwenden. Der gespeicherte Bericht beschreibt die Welt, Gruppengröße, Wiederholungen und Warmup. Der veröffentlichte Lauf verwendete Release, Intel Xeon W-2145 @ 3,70 GHz, 16 logische CPUs, Windows 10, .NET 10.0.9 (Projektziel net9.0, Harness erlaubt Major-Rollforward), Workstation-GC und deaktiviertes Tiered Compilation.

Zwei aktive KI-Spieler mit festem Seed und je acht mobilen Units erhalten je drei erreichbare und drei durch eine durchgehende Wand unerreichbare Gruppen-Gotos. Der Harness setzt die Host-Planungsiteratoren mit dem gemeinsamen Scheduler fort; echte KI-Updates im Zustand „auf Basis-Ressourcen warten“ sowie zwölf Bauplatzsuchen werden ebenfalls gemessen. Die grafikfreien Units verwenden einfache Footprints. Das Szenario misst weder einen kompletten Kampf noch Bewegung, Rendering oder Transport. Es ist eine gezielte, wiederholbare Belastung der Routenplanung; die Ergebnisse der Ressourcen-Warte-KI sagen nichts über eine ausgebaute KI-Basis aus.

Für eine ergänzende Messung im echten Spiel: zwei KIs auf derselben gespeicherten Map mit denselben Startplätzen starten, nach vergleichbarem Basisaufbau `telemetry-start` ausführen, Gruppenaufträge in freie sowie abgetrennte Bereiche geben, anschließend `telemetry-stop` und `telemetry-save`. Map, Startplätze, Seed, Spielzeit, Units, Simulationsgeschwindigkeit und Aufträge mit dem Bericht festhalten. Die dort aktiven Controller-Zeilen zeigen, welche KI-Arbeit tatsächlich teuer war. Ein solcher vollständiger Spiel-/Soak-Lauf wurde hier nicht behauptet.

## Befund der gespeicherten Baseline

Siehe `Performance-Baseline.md`: sechs erreichbare Gruppenplanungen benötigen zusammen rund 4,9 ms, sechs unerreichbare rund 913 ms; der langsamste unerreichbare Gruppenauftrag rund 172 ms. Insgesamt 624 Pfadsuchen werden innerhalb zwölf Gruppenaufträgen ausgeführt. Die Suchen allokieren zusammen rund 789 MB (kumulativ, kein gleichzeitiger Speicherbedarf). Die KI-Updates und einfachen Bauplatzsuchen sind in dieser kleinen Welt kurz.

Damit ist belegt, dass ein synchroner Gruppenauftrag trotz „ein Goto pro Update“ das Framebudget deutlich überschreiten kann. Ob dies die beobachteten mehrsekündigen Pausen in großen KI-Schlachten vollständig erklärt, bleibt eine Hypothese. Aufgabe 04 hat dasselbe Szenario erneut gemessen: 543 Planungsupdates, maximal 2.048 Schritte und gemessene 2,001 ms pro Update; siehe `Performance-Incremental.md` und `Fortsetzbare-Pfadplanung.md`. Das Zeitbudget wird zwischen Schritten geprüft und ist keine harte Echtzeitgarantie. Der historische Vorher-Bericht bleibt in `Performance-Baseline.md` erhalten. Für einen erneuten synchronen Diagnosevergleich kann `--performance-baseline` mit einem anderen Ausgabepfad verwendet werden. Große Bauplatzsuchen, ausgereifte Kampf-Controller und Snapshot-Kopien müssen zusätzlich anhand echter Spielberichte bewertet werden.
