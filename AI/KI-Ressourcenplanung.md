# Gemeinsame Ressourcenplanung der KI

`AIOrderQueue.Budget` besitzt pro Army einen `AIResourcePlanner`. Nur dort liegt die Reservepolitik. `AIProductionPlanExecutor`, Economy/Kernaufbau, Squad-Ausbildung, Scout-Ersatz, Fahrzeugunterstützung und Verteidigungsplanung verwenden `CanPropose` statt eigener Kontostands- und Reserveprüfungen.

Mit einer registrierten Queue darf ein Controller auch einen aktuell unbezahlbaren, ansonsten gültigen Wunsch melden. Das ist notwendig, damit die gemeinsame Planung diesen Wunsch überhaupt kennt. Ohne Queue verwendet `CanPropose` dieselbe zentrale Reservepolitik als direkte Bezahlbarkeitsprüfung. Die Auswahl von Produkten, Abhängigkeiten und Bauplätzen bleibt bei den jeweiligen Planern; die Kaufentscheidung trifft die gemeinsame Queue erst nach dem Sammeln der Vorschläge.

## Reserve und Reihenfolge

Die Reserve beträgt derzeit zentral **800 Ressourcen**. Produktion, Forschung und Ausbau müssen sie übriglassen. Existenzsicherung, Strom, Harvester/Wirtschaft und Verteidigung dürfen sie verwenden. Die Vergabe erfolgt in dieser Reihenfolge, innerhalb derselben Priorität in Vorschlagsreihenfolge. Während des Kernwiederaufbaus bleiben bereits vorgemerkte optionale Käufe gesperrt; bestätigte Produktion und taktische Befehle laufen weiter.

Eine beobachtete Luftbedrohung gibt der mobilen Flugabwehr Verteidigungspriorität. Die Ausbildung von Crew zur Stromversorgung hat Strompriorität. Ohne aktuellen Luftalarm bleibt der vorsorgliche Gepard-Nachkauf reguläre Produktion. Katalogisierte Harvester und Lager besitzen mindestens Wirtschaftspriorität.

Kann ein vorrangiger Auftrag noch nicht bezahlt werden, schützt die Planung sein angespartes Geld vor nachfolgenden Käufen. Beispiel: Eine Factory kostet 2.000, der Kontostand ist 1.500. Trotz günstigerer Ausbildungsangebote spart die KI für die Factory samt Reserve, wenn deren Auftrag vorher mit gleicher oder höherer Priorität vorgeschlagen wurde. Ein neuer dringender Strom-/Verteidigungsauftrag darf diese Ersparnisse beim nächsten Planungsdurchlauf übernehmen; die Ersparnis ist keine dauerhaft eingefrorene Ressource.

Auch ein vorübergehend gebundener Arbeiter/Produzent schützt den Preis seines vorgemerkten Auftrags. Ein gescheiterter, ungültiger oder nach 60 Sekunden verworfener Vorschlag gibt diese Bindung wieder frei. Wenn der Kernaufbau stattdessen eine vorhandene Ersatzbaustelle übernimmt, verwirft die Queue den passenden unbezahlten Neubauvorschlag. Dadurch wird der Ersatz nicht doppelt gekauft.

## Abrechnung

- `InFlightExpenses`: bereits zum Host geschickte, noch unbestätigte Käufe; sie dürfen nicht erneut ausgegeben werden.
- `ProtectedResources`: aktuell für vorrangige wartende Wünsche geschützte Ersparnisse.
- `PlannedExpenses`: die in diesem Vergabedurchlauf berücksichtigten Kaufwünsche, anhand aktueller Quotes.
- `RunningProductionOrders`: tatsächlich in den Gebäude-FIFOs vorhandene, schon bezahlte Produktions- und Forschungsaufträge.

Quotes kommen unverändert aus `PricingService`, einschließlich Perks und Produzenten-Upgrades. Wartende Wünsche erhalten vor einer Vergabe eine neue Quote. Nach der Hostbestätigung ist der Kaufpreis bereits vom Army-Konto abgezogen; die vorläufige Reservierung wird freigegeben. Bestehende Produktions-FIFOs werden beobachtet, ihre bereits bezahlten Aufträge werden **nicht noch einmal** vom verfügbaren Geld abgezogen. Session-/Matchwechsel und das Entsorgen der Queue setzen die Budgetdaten zurück. Die tatsächliche Ausgabe bleibt ausschließlich beim normalen Hostweg.

Der KI-Status (`AIController.LastDecision`) enthält zusätzlich die Queue-/Budgetdiagnose mit aktiven Aufträgen, ausstehenden Käufen, Ersparnissen, geplanten Ausgaben und laufender Produktion. Detaildaten liegen in `AIController.OrderQueue.Budget` und `Orders`.

## Umfang und weitere Schritte

Berücksichtigt werden konkrete Kaufwünsche für den jeweils ausführbaren nächsten Schritt eines Plans. Spätere Schritte mit noch fehlenden Voraussetzungen bekommen erst bei ihrer Ausführbarkeit einen Kaufvorschlag. Hier werden weder komplette zukünftige Ausbauketten vorfinanziert noch zusätzliche Gebühren für laufende Produktion erfunden. Die strategische Reservierung von Produzenten/Arbeitern und detaillierte gemeinsame Auftragsergebnisse bleiben die nächsten separaten TODO-Punkte.

## Prüfung

`tests/GridNavigationChecks/AIResourcePlanningChecks.cs` ergänzt 32 grafikfreie Regressionen: Reservepolitik aller Prioritäten, Sparen trotz eines bezahlbaren günstigeren Angebots, wiederholte Planung, Hostannahme/-ablehnung, bereits bezahlte FIFOs ohne doppelte Abbuchung, genaue Reservegrenze, nachträgliche Produzentenrabatte, Wiederaufbau-Sperre und Wiederaufnahme, Reset sowie echte Delegation eines Produktionsplaners bei Geldmangel.

Alle 1.229 Checks bestanden; Hauptprojekt-Build ohne Warnungen/Fehler. Die bisherigen Rekonstruktionsszenarien registrieren jetzt auch Platzhalter für Gebäudepreviews und die Spielerzuordnung, weil gültige Bauwünsche bereits vor ihrer Finanzierung geprüft werden. Unfinanzierte Bootstrap-Wünsche werden vor den jeweils unabhängigen Testszenarien entsorgt. Eine grafische mehrminütige Schlachtabnahme wurde nicht durchgeführt.
