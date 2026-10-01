# Fortsetzbare Pfadplanung

Stand: 01.10.2026. Architektur-Aufgabe 04. Alle Planung und Weltzugriffe bleiben auf dem Spielthread.

## Gemeinsames Budget

`PathfindingManager` besitzt einen `PlanningScheduler`. Ein reguläres Welt-Update führt ihn genau einmal aus: höchstens **2.048 Arbeitsschritte**, zusätzlich ein Zeitlimit von **2 ms**. Ein Arbeitsschritt ist ein fortgesetzter Iterator-Schritt: Suchinitialisierung, ein A*-Knoten mit bis zu acht Nachbarn, ein Rekonstruktions-/Prüfschritt oder ein Zielkandidat. Auch veraltete Jobs werden nur in einer begrenzten Schleife verworfen.

Der Scheduler verteilt jeweils höchstens 16 Schritte an einen Job und reiht ihn danach hinten ein. Kleine unabhängige Suchen können deshalb fertig werden, während eine unerreichbare Suche noch läuft. Alle Jobs teilen das eine Budget; pro Unit wird kein weiteres Framebudget vergeben. Die Zeit wird zwischen Schritten geprüft. Einzelne Weltprüfungen, Allokationen, GC, Betriebssystempausen und Ergebnisanwendung können das Zeitlimit überschreiten; es ist keine harte Echtzeitgarantie. Das Schrittlimit ist deterministisch.

Der gemeinsame `Pathfinder.Search` enthält dieselbe A*/Dijkstra-Logik wie die synchronen Diagnosemethoden. Suchfront, Kosten, Vorgänger und lokale Caches bleiben über Updates erhalten. Auch Rückverfolgung und Endprüfung der Route sind fortsetzbar. Das bestehende Limit von 25.000 entnommenen Suchknoten pro Versuch bleibt erhalten.

Im Laufzeitcode verwenden Gruppen-Goto und Move Away, lokale Bewegungskorrekturen, Bauplatz-Anfahrten, Harvester-Anfahrten, Sanitäterwege sowie die ältere flächenweise Erdarbeit den Scheduler. Die aktuellen Planierfahrten verändern Terrain direkt während der Fahrt und benötigen keine zusätzliche A*-Suche. Synchrone Pfadmethoden bleiben für bestehende grafikfreie Checks und Diagnosewerkzeuge erhalten; neue Laufzeitaufrufer sollen `CreateSearch(...).Work()` mit Gültigkeitsprüfung und Ergebnis-/Abbruchcallback einreihen.

## Host-Reihenfolge und Veröffentlichung

Ein entnommener Goto-/Move-Away-Request bildet bis zum Planungsende eine FIFO-Grenze im Host. Spätere Requests werden erst danach entnommen. Für einen normalen ersetzenden Goto hält der Host die betreffenden mobilen Units zunächst mit einem bestätigten Stop an; beim Planen bleibt ihre Ausgangsposition dadurch stabil. Shift verändert die laufende Fahrt nicht und plant ab dem zuletzt bestätigten Warteschlangen-Endpunkt.

Die Planung sammelt zunächst sämtliche Unit-Routen und prüft sie erneut. Es gibt keine Teilveröffentlichung eines Gruppen-Goto. Der fertige Command enthält für jede akzeptierte Unit eine Route und deren Ziel; ein unerreichbares Ziel ergibt eine leere Route am aktuellen Standort. Die Endpunkte für folgende Shift-Aufträge werden erst bei tatsächlicher Veröffentlichung übernommen. Client-Routen behalten die vorhandene minimale Plausibilitätsprüfung; die Ausführung prüft weiterhin Kollisionen. Beim Beitritt müssen Iteratoren nicht übertragen werden: Sie sind vorübergehende Host-Arbeit, danach erhalten alle Clients den vollständigen Command.

Bei der Aufnahme eines autorisierten ersetzenden Bewegungsauftrags merkt sich der Host eine Version pro betroffener Unit. Ein späterer normaler Goto, Stop, Move Away, Bau-/Einsteige-/Ernteauftrag entwertet ältere Planung dieser Units bereits vor der späteren Ausführung. Die Versionsaufnahme bleibt auch beim Kopieren/Filtern eines Requests erhalten. Shift sowie ergänzende Angriffs-/Follow-Aufträge bleiben FIFO und entwerten den vorherigen Goto nicht vorsorglich. Ein fremder Stop oder ein nicht endlicher/out-of-bounds Goto verwirft keine erlaubte Planung.

Vor Ergebnisanwendung werden Version, Existenz, Besatzungszustand, Tod und Steuerberechtigung erneut geprüft. Lokale Unit-Suchen verwenden zusätzlich `_pathRequestId` und das aktuelle Befehlsziel. Harvester-/Sanitäter-/Erdarbeitsjobs prüfen ihren noch aktiven Job und dessen Ziel bzw. Patienten. Ein Abbruch gibt die Iteratoren frei und veröffentlicht kein Ergebnis.

## Weltänderungen und Lebenszyklus

Das Grid besitzt eine Navigationsrevision. Änderungen an Blockierung, erlaubten Bewegungsarten, Wegkosten, Planungs-Ausschluss, Terrainhöhe und festen Belegungen entwerten Suchcaches. Eine Suche startet höchstens drei Versuche; dauernde Änderungen halten sie nicht unbegrenzt aktiv. Vor Gruppenveröffentlichung werden die erzeugten Routen erneut geprüft; Änderungen während dieser Prüfung erlauben ebenfalls nur begrenzte Wiederholung.

Mobile Bewegung erhöht nicht bei jedem Schritt die globale Revision, sonst würden aktive Armeen sämtliche Suchen ständig neu starten. Mobile Blockaden werden bei der Endprüfung und weiterhin während der tatsächlichen Bewegung berücksichtigt. Eine gerade frei gewordene mobile Sperre kann daher einen weiteren Bewegungs-/Planungsversuch benötigen. Terrain-/Gebäudeänderungen zwischen Abschluss und Veröffentlichung verwerfen das alte Ergebnis.

Sessionwechsel, Map-Load, übertragene WorldData und Matchstart setzen den gemeinsamen Scheduler zurück. Der Host löst dabei auch seine Planungs-FIFO-Grenze. Unit-Aufträge können nach einem Weltreset entsprechend ihrer vorhandenen Wiederholungslogik neu planen; alte Iteratoren können nichts mehr anwenden. Abbruchcallbacks verhindern, dass ein erhaltenes Arbeitsziel dauerhaft im Zustand „Planning“ hängen bleibt.

## Prüfung und Messung

`IncrementalPlanningChecks` testet Budget und Gleichheit zur synchronen Route, Round-Robin-Fortschritt, unerreichbare Ziele, Hindernis-/Kostenänderungen, begrenzte Neustarts, Iterator-Freigabe, lokale Stop-/Ersatzbefehle, echte Host-Aufnahme und lokale Anwendung, komplette Routen im Wire-Payload, gleichzeitig eintreffende Goto A/Goto B/Stop, Shift, Goto/Attack, fremde/ungültige Requests, Unit-Entfernung sowie Welt-/Sessionreset. Zusammen mit den bestehenden Bewegungs- und Multiplayer-Checks bestehen **647 Checks**. Debug- und Release-Build ohne Warnungen oder Fehler.

Nachher-Messung unter derselben Umgebung und demselben grafikfreien Szenario wie Aufgabe 03:

```powershell
dotnet build RTS.csproj -c Release
$env:DOTNET_TieredCompilation = '0'
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj -c Release -- --incremental-baseline AI/Performance-Incremental.md
Remove-Item Env:DOTNET_TieredCompilation
```

Die alte `Performance-Baseline.md` bleibt als Vorher-Bericht erhalten. Die neue Messung führt dieselben zwölf Gruppenaufträge und **624 tatsächliche Suchen** aus. Vorher konnte ein Gruppenauftrag rund **172 ms in einem Update** beanspruchen. Nachher verteilt der Scheduler die Planung auf **543 Updates**, mit höchstens **2.048 Schritten** und gemessenen **2,001 ms** pro Planungsupdate. Gesamte kumulative Planungsallokationen liegen weiterhin bei rund **792 MB** gegenüber rund 789 MB im Vorher-Bericht. Die Gesamtarbeit wird verteilt, nicht beseitigt; die Reduktion wiederholter Suchen und Allokationen bleibt weitere Arbeit.

Die Schleife des Harness enthält keine Framepausen. Gesamtzeiten pro Gruppe sind somit CPU-Arbeit einschließlich Planungskontrollen, keine Zusage einer Befehlslatenz im Spiel. Ein vollständiger KI-Kampf mit Rendering und echten Fahrzeugmodellen wurde nicht optisch abgenommen. Der Bericht belegt das begrenzte Planungsvolumen und die Vermeidung des synchronen Planungsausreißers im Vergleichsszenario; andere Ursachen von Spielpausen sind damit nicht ausgeschlossen.

Im Spiel zeigen `telemetry` und `telemetry-save` die neuen Bereiche `Path.PlanningUpdate`, `Path.JobSlice`, `Host.GotoPlanningSlice` und `Host.GotoPublish`. Keine Messung bleibt während einer Pause zwischen Updates geöffnet. Die bisherigen synchronen Zeitfelder sind als Diagnosewerte gekennzeichnet.

Nachprüfung Harvester: Der replizierte Wechsel nach `Unloading` beendet jetzt die Anfahrt einschließlich angehängter Fahrbefehle und entwertet offene Bewegungsplanung auf Host und Clients. Zuvor konnte der Harvester trotz akzeptierter Abladeposition weiter zum exakten Wegpunkt rangieren. Drei zusätzliche Wire-/Zustandschecks prüfen Halt, Request-ID-Entwertung und wiederholte Cargo-Updates; insgesamt 650 Checks. Der konkrete Rangierfall an einer modellierten Refinery wurde nicht grafisch reproduziert.

Weitere Nachprüfung: Bei der Rückfahrt zum Lager wird nach drei Bewegungswiederholungen die festgefahrene Anfahrposition verworfen. Der Host repliziert Stop und erneut den Rückkehrzustand, erhält den Ernteauftrag und wählt nach dem Retry-Intervall eine andere freie Position. Auch erfolglose geplante Anfahrten werden ausgeschlossen. Sind alle Kandidaten erschöpft, werden sie nach einer Wartezeit erneut berücksichtigt, damit vorübergehende Blockaden nicht dauerhaft ausschließen. Kollisionsregeln bleiben erhalten; das garantiert keinen Ausweg aus einem vollständig zugebauten Lager. Vier zusätzliche Checks prüfen tatsächlichen Host-Abbruch mit erhaltenem Auftrag, Ausschluss des gescheiterten Ziels, alternative freie Platzierung und fehlende Alternativen; insgesamt 654 Checks. Die gemeldeten Screenshot-Fälle wurden nicht grafisch reproduziert und ihre Erntephase ist unbekannt.

Nachprüfung DrivingToField: Die Ernte-Reichweite berücksichtigt jetzt dieselbe Ankunftstoleranz wie der letzte Fahrwegpunkt (2 Einheiten plus maximal 0,35 Grid-Zellengrößen). Anfahrzellen genau am Rand der bisherigen Reichweite konnten zuvor wiederholt angefahren werden, ohne die strengere Ernteprüfung zu erfüllen. Der replizierte Harvesting-Zustand beendet die Bewegung und entwertet offene Fahrplanungen. Zwei zusätzliche Checks für Reichweitengrenzen und replizierten Halt bestehen; insgesamt 658 Checks. Der Screenshot-Fall wurde nicht grafisch reproduziert.

Nachprüfung Squad/Planungszustand: Nach dem bestätigten Stop vor einem normalen Host-Goto bleibt das angenommene Ziel lokal als CurrentCommand im Status Planning sichtbar. So behandeln KI-Controller wartende Units nicht als idle und ersetzen deren Suche nicht ständig. SquadPreparation wartet ebenfalls auf laufende Planungen. Veröffentlichung, Abbruch, Scheduler-Reset und Sessionwechsel lösen den temporären Zustand auf, ohne neuere Befehle zu löschen. Zwei neue Hostchecks prüfen Auftragsbesitz während Planung und Freigabe nach Reset. Ein zusätzlicher Fahrcheck prüft kollisionsgeprüftes Rücksetzen nach wiederholter Blockade auch bei weitgehend zum Weg ausgerichtetem Radfahrzeug. Insgesamt 690 Checks; konkrete Screenshot-Fälle nicht grafisch reproduziert.
