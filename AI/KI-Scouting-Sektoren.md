# Scouting-Sektoren und Zielreservierungen

`GameWorld.ScoutingTargets` koordiniert die lokalen automatischen Scouts pro Army. Die Karte wird in Sektoren von **16 × 16 GameGrid-Zellen** unterteilt. Ein aktives Ziel reserviert seinen Sektor für einen Scout; andere Scouts derselben Army wählen einen anderen Sektor. Verschiedene Scouting-Controller derselben lokalen Spielwelt verwenden denselben Dienst. Verschiedene Armies besitzen getrennte Reservierungen.

`ScoutingController` sucht wie bisher im Umfeld eines Scouts nach unbekannten, nicht blockierten Terrain-Zellen. Kandidaten werden nun nach Sektoren gruppiert. Die Bewertung berücksichtigt den Anteil noch unbekannter Kandidaten, Entfernung und eine Richtungsstrafe für Ziele anderer Scouts. Dadurch verteilen sich nahe Scouts in unterschiedliche Richtungen. Innerhalb des besten freien Sektors wird weiterhin eine zufällige unbekannte Zelle gewählt. Erkundete Zellen sind keine Zielkandidaten, bereits reservierte Sektoren sind ausgeschlossen.

Die Reservierung bleibt bei unverändertem Ziel erhalten und wird während aktiver Bewegung erneuert. Ein wiederholtes `Start` setzt vorhandene Scout-Aufträge nicht zurück. Beim Wechsel des Ziels, Stop, Entfernen/Tod, Einsteigen oder Army-Wechsel wird die Reservierung freigegeben. `Dispose` gibt die Ziele des betreffenden Controllers frei; `AIController.BeginMatch` entsorgt seinen bisherigen Scouting-Controller. Verlassene Reservierungen laufen nach zwölf Sekunden aus. Sessionwechsel oder ein zurückgesetzter Simulationstakt entfernen alte Reservierungen ebenfalls.

Ohne freies unbekanntes Ziel wartet der Scout auf eine erneute Suche. Es wird weder eine bekannte Zufallszelle angesteuert noch ein reservierter Sektor erzwungen. Die Sektorgröße ist vorerst eine zentrale Konstante in `ScoutingTargets.SectorSize`.

Bewegungsaufträge verwenden unverändert `PlayerCommandService.GotoAsync` beziehungsweise den bestehenden NetworkClient-Weg für menschliche Scouts. Der Reservierungsdienst verändert keine Unit-Positionen und keine Hostregeln. Die Reservierungen sind lokale Planungsdaten, keine neuen Multiplayer-Snapshot-Daten. Über mehrere Rechner verteilte Controller derselben gemeinsam kontrollierten Army teilen diese lokalen Planungsdaten nicht; die normalen Goto-Befehle bleiben hostgeführt.

## Weitere Schritte

Die vorhandene Kandidatenliste bleibt bestehen. Die inkrementelle Sektor-/Frontier-Suche ohne große Listen ist ein eigener nachfolgender TODO-Punkt. Ebenso prüft diese Änderung noch keine vollständige Pfaderreichbarkeit und führt keine neue Sperre für unerreichbare Scouting-Ziele ein; dies ist der unmittelbar nächste TODO-Punkt. Die KI erzeugt hier auch keine zusätzlichen Scouts: die gemeinsame Zielvergabe wirkt auf die bereits aktivierten Scouts, einschließlich manueller AI:Scouting-Aktionen.

## Prüfung

`tests/GridNavigationChecks/AIScoutingReservationChecks.cs`: 15 neue grafikfreie Checks für gemeinsame Army-Reservierungen über zwei Controller, unterschiedliche Abmarschrichtungen, unbekannte Ziele, normale Netzwerkrequests, wiederholtes Start, Stop, fremde Controller-Leases, Dispose, entfernte Scouts, Sektorkonflikte, Army-Trennung, Ablauf, zurückgesetzte Zeit und eine vollständig erkundete Karte ohne neue Goto-Befehle.

Alle 1.291 Checks bestanden; Hauptprojekt-Build ohne Warnungen/Fehler. Die Richtungsprüfung verwendet benachbarte Scouts auf flachem unbekanntem Terrain und prüft die angeforderten Ziele; eine neue grafische Schlachtabnahme wurde nicht durchgeführt.
