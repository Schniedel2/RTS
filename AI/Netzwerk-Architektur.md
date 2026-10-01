# Netzwerk-Architektur

Die Kampfsimulation ist seit Architektur-Aufgabe 07 im hostseitigen `CombatSystem` abgegrenzt. Der Host behält Request-Prüfung, FIFO und Veröffentlichung; das System besitzt Zielsuche, Schussanforderungen, Raketenflug, Trefferauflösung und Schaden. Host und Clients übernehmen UnitHitCommands als absolute HP-Werte, ohne Schaden erneut zu berechnen. Sessionwechsel und akzeptierter game-start setzen aktive Raketen und ausstehende Einschläge zurück. Zuständigkeiten, unveränderte Trefferregeln und Late-Join-Grenzen stehen in [CombatSystem-Architektur.md](CombatSystem-Architektur.md).

Die Host-Simulation bleibt autoritativ. Clients senden Requests; der Host prüft und verarbeitet sie auf dem Spiel-Thread und verteilt daraus erzeugte Commands in einer festen Reihenfolge. Hintergrund-Tasks dürfen nur TCP lesen oder schreiben und Nachrichten in die Inbox legen. Sie greifen nicht auf `GameWorld`, Units, Armies oder andere Gameplay-Daten zu.

Jede Verbindung besitzt genau einen Writer mit einer begrenzten Sendewarteschlange. Dadurch bleiben Nachrichten pro Empfänger geordnet und ein langsamer Client blockiert weder den Host noch andere Clients. Zu große Frames, volle Queues und Schreib-Timeouts beenden nur die betroffene Verbindung und werden über den Netzwerkstatus gemeldet.

`NetworkHandler.Update()` verarbeitet pro Frame höchstens 128 Nachrichten beziehungsweise vier Millisekunden Arbeit. Normale Commands bleiben FIFO erhalten. Ersetzbare reine Positions-Snapshots einer Unit werden innerhalb eines commandfreien Abschnitts zusammengefasst; Navigationswechsel, Commands, Verbindungsereignisse und Snapshots bilden Ordnungsgrenzen.

Beim Beitritt erhält ein Client in dieser Reihenfolge `JoinAccepted`, Spielerdaten, einen `SessionSnapshot`, die vorhandenen Mitglieder und `SessionReady`. Der Snapshot umfasst Terrain und Map-Objekte, Armies samt Ressourcen, Besitzern, Rechten und Perks, aktive Units samt Zustand und Besatzung sowie Fog-of-War-Daten. Während `Synchronizing` pausiert der Client seine Simulation. Erst `SessionReady` schaltet auf `Connected`, sodass nie eine teilweise geladene Welt gespielt wird.

Jede Sitzung hat eine Generation. Beim Trennen oder Neuverbinden werden Listener, Verbindungen, Mitglieder, Fehlerzustand und ausstehende Daten bereinigt. Spät eintreffende Nachrichten einer alten Generation werden ignoriert. Der `NetworkHost` setzt zugleich laufende Erdarbeiten, Projektile, Requests und Zeitgeber zurück.

Neue Netzwerkfunktionen sollten deshalb immer diesem Weg folgen: Client-Request, Host-Verarbeitung auf dem Spiel-Thread, autoritativer Command, geordnete Verteilung. Laufende Zustände, die ein später Client benötigt, gehören zusätzlich in den `SessionSnapshot`.

## Budget und Reihenfolge der Host-Requests

`NetworkHost` verarbeitet höchstens 32 Requests und höchstens einen Goto-Request pro Update. `TakeRequestsForUpdate` prüft den Queue-Kopf vor der Entnahme. Ist dort ein weiterer Goto und dessen Budget ausgeschöpft, endet die Request-Verarbeitung für dieses Update. Der Request bleibt an seiner ursprünglichen Stelle; auch nachfolgende Stop-, Angriffs- und Shift-Aufträge warten. Die Reihenfolge gilt über alle Sender hinweg entsprechend ihrer Aufnahme in die Host-Queue. Beispielsweise bleibt `Goto A → Goto B → Stop` über mehrere Updates erhalten. Aufgeschobene Requests werden niemals hinten wieder eingereiht.

Die Queue hat einen entnehmenden Consumer auf dem Spielthread; nachgelagerte Host-Prüfung und lokale Anwendung/Broadcast bleiben unverändert. Ein Sessionwechsel leert wie bisher die Queue. Der Scheduler nimmt Requests einzeln während der Verarbeitung, nicht vorab als entkoppelten Batch. Das allgemeine Limit wird anhand der Queue-Länge zu Beginn des Durchlaufs begrenzt.

Gruppen-Goto und Move Away werden inzwischen über den gemeinsamen `PlanningScheduler` fortgesetzt (Architektur-Aufgabe 04). Der entnommene Request bleibt bis zum vollständigen Resultat eine FIFO-Grenze. Ein bestätigter Stop hält bei einem normalen Goto die betroffenen Units während der Planung an; Shift erhält die laufende Fahrt. Autorisierte Stop-/Ersatzbefehle entwerten ältere Planung schon bei der Aufnahme über Unit-Versionen. Nach erfolgreicher Planung werden vollständige Routen in einem Gruppen-Command veröffentlicht. Details, Lebenszyklus, Budget und Messung stehen in `Fortsetzbare-Pfadplanung.md`. Die globale FIFO kann nachfolgende Requests verzögern; das Schrittbudget ist begrenzt, die maximale gesamte Framezeit wird nicht garantiert.

Regressionen im GridNavigationChecks-Harness prüfen die tatsächliche Scheduler-Methode über mehrere Updates: Goto/Stop, Goto/Attack, interleavte Spieler, unveränderte Shift-Routen und die Limits von einem Goto bzw. 32 Requests.

## Synchronisationsdiagnose

Der Host kann mit `network-sync-start` regelmäßige Vergleiche aktivieren. Optional setzt beispielsweise `network-sync-start 10` ein Intervall von zehn Sekunden. `network-sync-check` startet sofort einen einzelnen Vergleich, `network-sync-status` zeigt Zähler und Zustand und `network-sync-stop` beendet die Diagnose auf allen Teilnehmern. Abweichungen werden nach Welt, Armies, Units und Sichtdaten aufgeteilt; soweit möglich nennt die Meldung die betroffene Army- oder Unit-ID. Kontinuierlich wachsende Tiberiumwerte und kleine Bewegungsabweichungen werden zeitlich tolerant behandelt. Eine Unit-Position wird erst ab drei Welt-Einheiten Abstand oder mehr als 60 Grad Drehabweichung als `movement:<id>` gemeldet.

Der aktuell sichtbare Fog-of-War-Bereich wird weiterhin lokal aus den replizierten Unit-Positionen berechnet. Dauerhaft erkundete Zellen sind dagegen Host-autoritiv: Der Host verteilt einmal pro Sekunde eine bitgepackte Explored-Maske mit einem Bit pro Zelle und Army. Dadurch können kleine Bewegungsabweichungen keine dauerhaft unterschiedlichen Karteninformationen erzeugen.

## Ernte-System (01.10.2026)

HarvestSystem besitzt die hostseitigen Erntejobs und Phasen. NetworkHost übernimmt Request-Reihenfolge, gemeinsame budgetierte Routenplanung und Veröffentlichung. Sessionwechsel und erfolgreicher game-start setzen die Erntejobs zurück; Stop/Ersatzaufträge entwerten ausstehende Planung. Replizierte Phase, Cargo und Lager-/Ressourcenzustände bleiben im bestehenden Protokoll und Session-Snapshot. Details und Prüfgrenzen: HarvestSystem-Architektur.md.

## Sanitäter-System (01.10.2026)

MedicSystem besitzt Patientenwahl, automatische Anfahrt, Haltezustand und Heilintervalle. Der Host übernimmt weiterhin geordnete Requests, Squad-Empfänger, gemeinsame Routenplanung und Veröffentlichung. Heilung wird einmal hostseitig angewandt und als absoluter HP-Wert im bestehenden UnitHitCommand repliziert. Sessionwechsel und akzeptierter game-start setzen das System zurück. Details und Prüfgrenzen: MedicSystem-Architektur.md.
