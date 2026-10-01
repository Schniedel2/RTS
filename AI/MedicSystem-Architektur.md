# Hostseitiges MedicSystem

Stand: 01.10.2026, Architektur-TODO 06.

`src/Systems/MedicSystem.cs` besitzt Patientenauswahl, automatische Patientenanfahrt, Stop-/Haltezustand und Heilintervalle. NetworkHost besitzt eine Instanz und aktualisiert sie ausschließlich innerhalb der autoritativen Simulation auf dem Spielthread. Welt, Armies, Peer-ID, Veröffentlichung, Stop-Erzeugung, Routenplanung sowie Session-/Befehlsversionszugang werden im Konstruktor übergeben. Das System verwendet keine Globals direkt; bestehende Unit-Methoden behalten ihre bisherigen globalen Dienste.

## Verhalten und Zuständigkeiten

- Ein eigenständiger Sanitäter sucht verletzte Soldaten seiner Army innerhalb von 12 Grid-Zellen. Er heilt innerhalb von 2,5 Zellen, andernfalls plant er eine Anfahrt durch den bestehenden Host-Goto-Planer.
- Ein Sanitäter mit gültigem Squad-Leader heilt nur nahe Mitglieder dieses Squads einschließlich des Leaders. Er startet keine separate Patientenverfolgung. Beim Eintritt in ein Squad wird eine bereits ausgeführte eigenständige Patientenanfahrt per Host-Stop beendet; eine noch offene Planung wird entwertet.
- Pro Patient gilt ein gemeinsames Intervall von einer Sekunde für alle Sanitäter. Ein Puls heilt maximal fünf HP bis zum Maximalwert. Patienten werden nach relativem Füllstand, Entfernung und UnitId sortiert. Feindliche, tote, eingeschiffte oder vollständig geheilte Soldaten sind ausgeschlossen.
- Stop beendet eine automatische Patientenanfahrt und unterbindet weitere Verfolgung. Nahe Soldaten dürfen weiterhin geheilt werden. Ein weiterer autorisierter Bewegungs-/Angriffsbefehl hebt den Haltezustand auf; während eines ausdrücklichen Fahrbefehls wird keine selbstständige Patientenanfahrt gestartet.

NetworkHost übernimmt weiterhin Request-FIFO, Squad-Erweiterung, Kontrollprüfung am allgemeinen Befehlsweg und geordnete Veröffentlichung. `HandleCommandOverride` prüft die Empfängerberechtigung zusätzlich. `QueueMedicRoute` stellt das gemeinsame fortsetzbare Goto-Budget und dessen vollständige Route bereit. Das System besitzt keinen eigenen Pathfinder und keinen Transport. Die bestehenden NetworkMessage-DTOs bleiben an dieser Grenze erhalten; Typisierung ist eine spätere Aufgabe.

Die Heilung mutiert HP einmal autoritativ über `Unit.Heal`. Danach veröffentlicht der Host den vorhandenen UnitHitCommand mit negativem Damage und absoluten neuen HP. NetworkInput setzt diesen absoluten Wert auf Host und Clients; wiederholte Anwendung erzeugt keinen zweiten Heilpuls. Medic/Soldier enthalten keine zweite Heil-Zustandsmaschine.

## Abbruch und Reset

Offene Planungen prüfen Job- und Unit-Identität, Patientengültigkeit, Request-ID, Befehlsversion, Sessiongeneration und Squad-Zugehörigkeit. Auch der Ergebniscallback prüft nochmals seine Gültigkeit. Stop, Squad-Wechsel, Patientenverlust, Entfernung, Ersatzauftrag oder Reset können daher keine späte Bewegung wieder starten. Ein abgebrochener Callback verändert höchstens sein abgetrenntes Jobobjekt.

Ungültige Sanitäter verlieren Jobs und Haltezustand; entfernte/tote/eingeschiffte Patienten verlieren ihren Heilzeitgeber. Sessionwechsel und ein akzeptierter game-start setzen Jobs, Haltezustände und Heilintervalle zurück. Ein abgewiesener game-start verändert sie nicht. Welt-/Map-Austausch nutzt weiterhin Scheduler-Reset und Unit-Entfernung; spätere Ergebnisse scheitern an ihren Identitätsprüfungen.

## Replikation und Late Join

HP, Squad-Zugehörigkeit und normale Unit-Bewegungszustände bleiben Teil der vorhandenen Session-Snapshots und Befehle. Patientenwahl, Pulszeitgeber, Haltezustand und Planungsjobs sind flüchtiger Host-Zustand. Beitretende Clients starten keine MedicSystem-Simulation; der bestehende Host setzt sie fort und sendet die nächsten Bewegungs-/HP-Ergebnisse. Es werden keine zusätzlichen Protokollfelder benötigt. Hostmigration oder vollständige Savegames für diese internen Jobs sind damit nicht eingeführt.

## Validierung und Grenzen

Build ohne Warnungen/Fehler, 726 Checks bestanden. 36 neue System-/Hostchecks prüfen lokale Heilreichweite und Suchradius, Pulsrate, Maximalwert, feindliche/gesunde/tote/entfernte/eingeschiffte Patienten, gemeinsame Patientenintervalle, Squad-Eintritt/-Austritt, Stop und fremde Requests, Session-/Befehlswechsel, späte Ergebnisse nach Reset sowie akzeptierten und abgewiesenen game-start. Der Wire-Test nutzt Serialisierung und tatsächliche NetworkInput-Anwendung des Heilbefehls einschließlich wiederholter Anwendung.

Die neuen FSM-Tests ersetzen den Routenplaner kontrolliert; vorhandene tatsächliche Host-/Routen-/Wire-Tests aus Aufgabe 04 laufen zusätzlich. Grafikfreie Soldier-Fixtures verwenden weiterhin Reflection; deren Ablösung bleibt Aufgabe 11. Keine grafische Heil-/Squadfahrt oder neue Bewegungsabnahme behauptet. Die offenen Fahrzeug-/Squad-Festfahrerfälle werden durch dieses Refactoring nicht pauschal als gelöst betrachtet.

Nächster Architekturpunkt: 07, hostseitige Kampfsimulation vom Netzwerk-Host trennen.
