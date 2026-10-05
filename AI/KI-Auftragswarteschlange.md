# Zentrale KI-Auftragswarteschlange

Pro Army besitzt `AIController` eine hostlokale `AIOrderQueue`. Die Controller schlagen ihre Bau-, Bauarbeiter-, Ausbildungs- und Forschungsaufträge weiterhin über `PlayerCommandService` vor. Dessen Router sammelt diese Requests während des KI-Updates. Erst danach vergibt die Queue Ressourcen, Produzenten und Arbeiter nach Priorität. Menschliche Spieler und taktische Bewegungs-/Angriffsbefehle verwenden weiterhin direkt den normalen Requestweg.

Priorität: Existenzsicherung, Strom, Wirtschaft, Verteidigung, Produktion, Forschung, Ausbau. Der Kernaufbau hat Existenzsicherungspriorität, der dringende Stromauftrag Strompriorität. Katalogisierte Lager und Harvester werden mindestens als Wirtschaft eingeordnet. Forschung ist normalerweise nachrangig. Innerhalb derselben Priorität gilt die Reihenfolge der Vorschläge.

Jeder Eintrag enthält den ursprünglichen Request mit stabiler Gebäude-/Produktions-ID, lokale Host-Rückmeldung, Priorität, Status, Ressourcenreservierung und Arbeiter-/Produzentenbedarf. Ausstehende Käufe reservieren ihren aktuellen Preis aus `PricingService`; die Host-Bestätigung löst diese Reservierung auf, weil der Host den Kaufpreis bereits abgebucht hat. Verschiedene Produzenten können parallel bedient werden. Derselbe Produzent erhält erst nach der Host-Rückmeldung den nächsten Kauf; anschließend übernimmt seine bestehende Produktions-FIFO.

Ein Bauauftrag behält seine Arbeiter bis zur Fertigstellung beziehungsweise zum Verlust der Baustelle. Ein höher priorisierter Auftrag darf bestätigte Bauarbeit unterbrechen. Die alte, bereits bezahlte Baustelle bleibt bestehen und erhält den Status `Paused`. Sobald die dringende Arbeit abgeschlossen ist, sendet die Queue einen normalen `BuildConstructionRequest` zur Wiederaufnahme. Wiederholte Requests eines pausierten Controllers können den reservierten Arbeiter nicht zurückholen. Fortschrittsmonitor und Produktionsplaner unterscheiden diese bewusste Pause von einem Stillstand.

Unbezahlte Vorschläge, die 60 Simulationssekunden keinen Arbeiter beziehungsweise kein Geld bekommen, werden verworfen, damit ihre Controller neu planen können. Host-Ablehnung, verschwundene Baustelle, fehlender Arbeiter und Sessionwechsel lösen Reservierungen auf. Beim Match-Neustart wird die Queue entsorgt. Die Diagnose ist über `AIController.OrderQueue.Orders` verfügbar; abgeschlossene Historie ist auf 128 Einträge begrenzt.

Die Queue ist keine zweite Autorität: sie verändert weder Ressourcen noch Grid, Baustellen oder Produktions-FIFOs direkt. Auch Unterbrechung und Wiederaufnahme laufen über Hostvalidierung und Netzwerkbefehle. Ihr lokaler Planungszustand gehört deshalb nicht in den Multiplayer-Snapshot.

## Grenzen und nächste Schritte

Die bisherigen strategischen Ressourcenreserven der Controller bleiben vorerst erhalten. Deren gemeinsame Budgetpolitik ist der nächste TODO-Punkt. Produzenten werden hier bis zur Hostannahme reserviert; eine weitergehende strategische Reservierung kompletter Produktionspläne folgt separat. Bei Produktion bedeutet `Completed` hier die erfolgreiche Übergabe an die Gebäude-FIFO; den tatsächlichen Abschluss überwachen weiterhin Produktionsplaner und Fortschrittsmonitor. Gemeinsame Auftragsergebnisse und Fehlerkategorien sind inzwischen umgesetzt: [KI-Auftragsergebnisse.md](KI-Auftragsergebnisse.md). Die taktische Zuordnung von Scouts, Verteidigern und Squads wird ebenfalls nicht durch diese wirtschaftliche Queue ersetzt.

## Prüfung

Grafikfreie Regressionen in `tests/GridNavigationChecks/AIOrderQueueChecks.cs`: Prioritätsvergabe nach dem Sammeln, Ressourcen- und Produzentenkonflikte, parallele Produzenten, echte Hostannahme/-ablehnung, Bauunterbrechung und Wiederaufnahme, Schutz vor konkurrierenden Construction-Retries, Schutz pausierter Baustellen vor Stillstandsabbruch, begrenztes Ressourcenwarten sowie Match-/Session-Lebenszyklus. Ein mehrminütiges grafisches KI-Spiel ist weiterhin eine separate Abnahme.
