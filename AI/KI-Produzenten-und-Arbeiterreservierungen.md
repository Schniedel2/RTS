# Reservierungen von KI-Produzenten und Bauarbeitern

Die hostlokale `AIOrderQueue` verwaltet die Reservierungen pro Army. `ReservationFor(unitId)` liefert den Besitzerauftrag; `CanUse(unitId, siteId)` berücksichtigt die aktuelle Planungspriorität und eine ausdrücklich akute Verteidigungsanforderung. Die Katalogauswahl sowie der Produktionsplaner bevorzugen freie kompatible Produzenten beziehungsweise Bauarbeiter. Sind alle Kandidaten gebunden, darf weiterhin ein wartender Wunsch entstehen; die Queue verhindert dessen konkurrierende Ausführung.

## Bauarbeiter

Ein zum Host geschickter Auftrag reserviert seine Arbeiter. Bei bestätigter Bauarbeit bleibt die Reservierung bis zur Fertigstellung, zum Verlust der Baustelle oder zum Reset bestehen. Ein wiederholter Construction-Request für dieselbe Baustelle übernimmt deren vorhandenen Auftrag; er erzeugt keine zweite Besitzzuordnung.

Eine höhere Priorität alleine reicht zum Unterbrechen nicht aus. Existenzsicherung und Strom dürfen nachrangige bestätigte Bauarbeit pausieren. Verteidigung benötigt sowohl höhere Priorität als auch `urgent: true`. Die laufende Bedrohungsbewertung setzt dieses Flag bei einer beobachteten Luftbedrohung über dem bestehenden Alarm-Schwellwert. Routinemäßiger Verteidigungsausbau, Wirtschaft, Produktion, Forschung und Ausbau dürfen einen reservierten Arbeiter nicht abziehen.

Unterbrechungen laufen über die normalen Host-Construction-Befehle. Die bezahlte Baustelle bleibt erhalten und pausiert; Fortschrittsmonitor und Produktionsplaner behandeln dies nicht als Fehler. Nach Ende des dringenden Auftrags erhält der höchste wartende Auftrag den Arbeiter, danach wird auch die ursprüngliche Baustelle fortgesetzt. Eine inzwischen akut gewordene, noch nicht ausgeführte Bauanforderung darf ihre Priorität/Dringlichkeit erhöhen.

Wird ein Arbeiter zerstört, kann ein gültiger freier Ersatzarbeiter dieselbe Baustelle über einen Construction-Request übernehmen. Die Besitzerzuordnung wird auf die neuen Arbeiter übertragen. Gebundene Arbeiter anderer Baustellen bleiben dabei geschützt. Es wird kein Gebäude erneut bezahlt.

## Produzenten

Kaserne, Factory, Helipad, Forschungsgebäude und weitere Katalogproduzenten bleiben nach der Host-Annahme reserviert, solange der **konkrete `ProductionOrderId`** in ihrer Produktions-/Forschungs-FIFO vorhanden ist. Teilfortschritt gibt die Reservierung nicht frei. `InProgress` bezeichnet deshalb jetzt auch bestätigte, laufende Produktion; `Completed` folgt erst, wenn dieser FIFO-Eintrag beendet ist.

Ein weiterer KI-Auftrag wartet im Status `WaitingForProducer` oder verwendet einen freien alternativen Katalogproduzenten. Auch ein dringender Auftrag storniert keine bereits bezahlte Produktion. Sobald der FIFO-Eintrag endet, wird die Reservierung freigegeben und der nächste wartende Wunsch kann über den Host ausgeführt werden. Zerstörung oder Verlust der eigenen Produzentenzuordnung beendet die Reservierung als Fehler.

Die Budgetplanung zählt den bezahlten FIFO-Eintrag weiterhin als laufende Produktion, ohne dessen Preis erneut abzuziehen. Reservierungszustand bleibt hostlokal; die echte Baustelle und Produktions-FIFO laufen wie zuvor synchronisiert über die Netzwerkbefehle. Die vorhandene Queue-/Budgetdiagnose und `Orders` zeigen den gebundenen Produzenten beziehungsweise die Arbeiter.

## Prüfung und Grenzen

`tests/GridNavigationChecks/AIReservationChecks.cs` ergänzt 21 grafikfreie Prüfungen mit echter Host-Anwendung: Reservierung bis FIFO-Ende, freie Alternativproduzenten, gebundene Kaserne/Forschung/Factory/Helipad, Teilfortschritt, Produzentenverlust, wiederholte konkurrierende Baustellenbefehle, freie Bulldozer-Auswahl, Routine-Verteidigung gegenüber akutem Alarm, Wiederaufnahme, Ersatzarbeiter und Reset. Vorhandene Queue- und Budgetprüfungen berücksichtigen die verlängerte Produzentenbindung. Alle 1.250 Checks bestanden; Build ohne Warnungen/Fehler.

Das ist eine Auftragsreservierung für konkrete Arbeit, keine Vorabreservierung aller Produzenten einer vollständigen zukünftigen Technologiekette. Produktionsfehler und -abschlüsse werden weiterhin durch die vorhandene FIFO und Fortschrittsüberwachung erkannt; ihre detaillierten gemeinsamen Ergebnisgründe bleiben der nächste TODO-Punkt. Eine grafische mehrminütige KI-Schlacht wurde für diese Änderung nicht durchgeführt.
