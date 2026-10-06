# Inkrementelle Scouting-Suche

Die Kandidatensuche sammelt keine vollständige Zellliste mehr. `ScoutingCandidateSearch` verarbeitet pro Planungsschritt eine Zelle und behält je 16×16-Sektor nur eine Anzahl und einen zufällig gewählten Kandidaten. Reservoir Sampling wählt diesen Kandidaten gleichverteilt aus den gültigen Zellen. Der Dictionary-Speicher wird zwischen Entscheidungen wiederverwendet.

Nach dem Zellscan werden die Sektoren schrittweise nach den bisherigen Kriterien bewertet: unbekannte Fläche, Entfernung und Richtung anderer Scouts. Reservierte oder vorübergehend gesperrte Sektoren werden gemieden. Scheitert die Reservierung durch einen konkurrierenden Scout, werden die verbleibenden Zusammenfassungen erneut bewertet, ohne den Zellbereich noch einmal zu scannen. Sichtbarkeit und Terrain des endgültigen Kandidaten werden erneut geprüft.

Zielsuche und Erreichbarkeit sind ein gemeinsamer Scheduler-Auftrag. Der Host verwendet weiterhin das globale Pathfinding-Budget (standardmäßig 2.048 Schritte beziehungsweise 2 ms je Update); menschliches Client-Scouting verwendet den vorhandenen lokalen Budgetweg (512 Schritte beziehungsweise 0,5 ms je Simulationszeitpunkt). Goto wird erst nach erfolgreicher Prüfung über die normale Netzwerkschnittstelle gesendet. Auch Helis durchlaufen die inkrementelle Zielsuche, benötigen jedoch keine Bodenpfadprüfung.

Während der Zielsuche gibt es noch keine Sektorreservierung. Nach der Auswahl wird sie atomar beansprucht und während der Pfadprüfung erneuert. Stop, Tod, Einsteigen, Army-/Sessionwechsel oder eine veränderte Startzelle verwerfen den gesamten Auftrag. Viele Scouts erhalten faire Suchscheiben im bestehenden Scheduler. Eine Zeitgrenze wird zwischen Schritten geprüft, ist also keine harte Echtzeitgarantie für einen einzelnen Schritt.

Die Sichtabfrage verwendet bitweise Flags und eine indizierte Ally-Schleife, um pro Zelle Enum-Boxing beziehungsweise Interface-Enumerator-Allokationen zu vermeiden. Ihre bisherigen Sichtregeln bleiben bestehen.

## Prüfung und Grenzen

Zwölf neue Checks in `AIStreamingScoutingChecks.cs` prüfen kurze Suchscheiben, gültige Ziele, Sektorspeicher, Konkurrenz bei Reservierungen, Wiederverwendung, Ausschlüsse, Stop sowie 16 parallel aktive Scouts mit insgesamt höchstens 64 Planungsschritten in einer begrenzten Testscheibe. Auf der 65×65-Testkarte bleiben höchstens 25 Sektor-Zusammenfassungen statt Tausender Zellkandidaten erhalten. Eine wiederholte reine Kandidatensuche allokiert im Check weniger als 16 KiB; dies umfasst keine anschließende A*-Pfadsuche.

Die vorhandenen Erreichbarkeitsprüfungen warten jetzt auch auf die inkrementelle Kandidatenauswahl. Alle 1.318 Checks bestehen, Build ohne Warnungen oder Fehler. Keine neue grafische FPS-Messung oder mehrminütige Schlacht wurde durchgeführt.

Der Gesamtaufwand einer Entscheidung bleibt proportional zum lokalen Suchgebiet. Er ist jetzt auf Planungsupdates verteilt; ein dauerhaftes Army-weites Frontier-Cache ist nicht erforderlich. Sehr viele Scouts können die Wartezeit bis zu einem neuen Ziel erhöhen, halten aber die gemeinsame Schritt-/Zeitgrenze ein. Sektorwerte entstehen über mehrere Frames und sind deshalb kein atomarer Snapshot; Endprüfung und normaler Hostweg behandeln inzwischen veränderte Ziele.
