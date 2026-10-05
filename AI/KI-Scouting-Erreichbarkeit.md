# Scouting: Erreichbarkeit und Fehlzielsperren

Scouting prüft vor jedem neuen Goto, ob die ausgewählte unbekannte Zelle mit dem normalen Pathfinder erreichbar ist. Damit gelten dieselben Bewegungsprofile, Footprints, Belegungen, Pathfinding-Ausschlüsse und Diagonalregeln wie bei normalen Bodenbewegungen. Eine freie Zielzelle genügt nicht: Auch eine Verbindung vom aktuellen Standort muss existieren.

Die A*-Prüfung läuft inkrementell im gemeinsamen PlanningScheduler des Hosts. Sie blockiert den Think-Tick nicht bis zum Suchende. Lokales automatisches Scouting eines menschlichen Clients besitzt einen gemeinsamen Client-Scheduler pro Welt (maximal 512 Schritte beziehungsweise 0,5 ms je Simulationszeitpunkt), weil Clients keine autoritativen Bewegungsjobs ausführen. Erst bei Erfolg wird der normale GotoRequest gesendet; der Host bleibt für die Ausführung zuständig. Helikopter-Goto verwendet Flugnavigation und benötigt keine begehbare Bodenverbindung.

Der Sektor wird während der Prüfung reserviert. Scheitert die Suche, wird er für diesen Scout 30 Simulationssekunden gesperrt und die Reservierung freigegeben. Ablehnungen/verworfene lokale Host-Requests und 15 Sekunden ohne Positionsfortschritt lösen ebenfalls eine Sperre und Neuplanung aus. Weitere Scouts sind von der Fehlzielsperre nicht betroffen; sie können andere Bewegungsfähigkeiten haben. Nach Ablauf wird der Bereich erneut berücksichtigt.

Stop, Dispose, Entfernen, Tod, Einsteigen, Army-Wechsel, Sessionwechsel oder eine inzwischen veränderte Startzelle verwerfen alte Prüfungen. Ein zurückgesetzter Simulationszeitpunkt entfernt alte lokale Sperren. Laufende Prüfungen erneuern ihre Reservierung.

Die Sperre eines ganzen Sektors ist bewusst konservativ: Auch erreichbare Zellen desselben Sektors können vorübergehend warten. Suchbudget/Neustartgrenzen des normalen Pathfinders können ebenfalls eine zeitweilige Sperre auslösen. Eine erfolgreiche Prüfung garantiert keine dauerhaft freie Route, da später Einheiten oder Baustellen auftauchen können. Der Host prüft den normalen Bewegungsauftrag weiterhin. Zielkandidaten werden noch als Liste gesammelt; deren inkrementelle Verwaltung ist der nächste TODO-Punkt.

## Prüfung

`AIScoutingReachabilityChecks.cs` ergänzt acht grafikfreie Checks: kein Request vor Prüfungsabschluss, fehlgeschlagenes Ziel ohne Goto, zeitlich gesperrter Sektor, erreichbares Ersatzziel über Netzwerk, drive-only Barriere für Infantry, Freigabe erschöpfter Reservierungen, Wiederaufnahme nach Ablauf und Öffnen der Barriere sowie Stop während der Suche. Bestehende Sektorprüfungen warten nun auf den inkrementellen Prüfungsabschluss.

Alle 1.299 Checks bestehen. Build: keine Warnungen oder Fehler. Eine neue grafische Mehrminuten-Schlacht wurde nicht getestet.
