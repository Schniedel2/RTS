# KI-Auftragsfortschritt und Wiederherstellung

Stand: 05.10.2026. Umsetzung des vierten Punkts aus `AI-Spieler-TODO.md`.

## Warum die Kontrolle nötig ist

Ein gesendeter Request ist noch keine Host-Bestätigung. Eine bestätigte Baustelle oder Produktion kann außerdem dauerhaft hängen bleiben. Kurze Wiederholungsintervalle allein erkennen diesen Unterschied nicht und können zusätzliche Käufe erzeugen. Beim Angriff waren Sammeln und Rückzug bisher nicht gegen unbegrenztes Warten abgesichert.

## Host-Rückmeldung

`PlayerCommandService.LastRequest` enthält für hostlokale Befehle eine `LocalRequestReceipt` mit `Pending`, `Accepted`, `Rejected` oder `Abandoned` und einem Erklärungstext. Die KI läuft auf dem Host; deshalb braucht diese Rückmeldung keine neue Netzwerknachricht und keinen Snapshot-Eintrag. Befehle durchlaufen weiterhin die normale Request-Queue, Validierung, lokale Anwendung und Broadcasts.

Die Bestätigung eines Goto erfolgt erst nach Abschluss der inkrementellen Routenplanung. Kopien des Requests behalten für die Rückmeldung die ursprüngliche Referenz. Sessionwechsel und Match-Neustart verwerfen ausstehende Rückmeldungen ausdrücklich. Auch die lokale Payload-Prüfung meldet Ablehnungen.

Identische ausstehende KI-Käufe werden zusammengefasst: Training/Forschung nach Actor, Produzent und Produkt; Baustellen nach Actor, Typ, Position und Drehung. Bei Baustellen wird die ursprüngliche Building-ID weiterverwendet. Nach Host-Verarbeitung darf wieder ein zusätzlicher Auftrag angefordert werden. Für menschliche Spieler wird die Zusammenfassung nicht aktiviert.

`Accepted` bestätigt den angewandten Host-Befehl, nicht den Abschluss der Aufgabe. Die bisherige Host-Validierung liefert teilweise gemeinsame Kategorien statt eines exakt typisierten Ablehnungsgrunds. Vollständige Auftragsergebnisse, Ressourcenreservierungen und eine Entscheidungshistorie bleiben eigene spätere TODO-Punkte.

## Fortschritt und Wiederherstellung

`AIProgressWatch` misst kumulative Verbesserungen am gleichen Ziel. Befehlswiederholungen und Vor-/Zurückfahren setzen die Uhr nicht zurück. Eine neue Erholungsfrist behält den besten bereits erreichten Fortschritt bei. Die Zeiten sind Simulationssekunden und folgen der Spielgeschwindigkeit.

`AIOrderProgressMonitor` arbeitet einmal pro Simulationssekunde pro aktiver KI-Army. Er beobachtet auch Aufträge aus dem älteren Economy-Controller, nicht nur katalogbasierte Produktionspläne.

| Auftrag | Fortschritt | Verhalten bei Stillstand |
| --- | --- | --- |
| Baustelle | Baupunkte oder messbares Annähern des zugewiesenen Arbeiters | Nach 20 Sekunden erneut Construct anfordern; nach zwei erfolglosen Wiederherstellungen über den Host abbrechen. Der vorhandene vollständige Bauabbruch-Refund gilt. |
| Bewegung, Einstieg, Folgen, Angriff | Annähern ans Ziel beziehungsweise Schaden am Angriffsziel | Nach 15 Sekunden neu planen; bei erneutem Stillstand Stop und 60 Sekunden Sperre für dieses Unit/Ziel-Paar. Echter Fortschritt erneuert das Wiederherstellungsbudget. |
| Erntebewegung | Annähern ans Bewegungsziel | Über Harvest/ReturnHarvester neu planen, damit der autonome Ernteauftrag erhalten bleibt. Nach Abbruch werden nahe fehlgeschlagene Ernteziele vorübergehend gemieden; die Economy startet einen idle Harvester wieder. |
| Produktion/Forschung | Fortschritt des aktiven FIFO-Auftrags | Nach 60 Sekunden Produzenten vorübergehend meiden; bezahlte Warteschlangen behalten. Bei deaktivierten Produzenten einmalig die normale ToggleEnabled-Action anfordern. Tatsächlicher Fortschritt hebt die Sperre auf. |
| Produktionsplan | Baupunkte, FIFO-Fortschritt oder Crew-Annäherung | Nach 60 Sekunden ohne Fortschritt `Failed` und den Controller freigeben. Ablehnung beziehungsweise Verlust eines bestätigten Auftrags wird früher erkannt. |
| Squad-Vorbereitung | Annähern an den Sammelpunkt | Nach 60 Sekunden einen unerreichbaren Rekruten vorübergehend aus der Auswahl nehmen und Ersatz planen. |
| Squad-Sammeln, Angriff, Rückzug | Annäherung beziehungsweise Schaden | Profilabhängige Stillstandsfrist (normal 45–70 Sekunden). Sammeln/Angriff führen zum Rückzug; ein unmöglicher Rückzug beendet die Mission und gibt die Überlebenden zur Neuformierung frei. |
| Squad-Heilung | Durchschnittliche Gesundheit | Fehlender Medic oder 60 Sekunden ohne Heilfortschritt beenden die Phase mit `HasFailed`; dies wird ausdrücklich nicht als erfolgreiche Heilung markiert. |

Fehlgeschlagene oder bereits vom Host abgelehnte Bauplätze werden einschließlich einer Umgebung von drei Zellen für 60 Sekunden aus der gemeinsamen Bauplatzsuche ausgeschlossen. Sammel- und Angriffsziele werden ebenfalls vorübergehend statt dauerhaft gesperrt.

Der Planexecutor übernimmt vorhandene unfertige Baustellen und bereits bezahlte Produktion/Forschung. FIFO-Wechsel gelten als Fortschritt; eine lange Produktion wird nicht allein wegen ihrer Gesamtdauer abgebrochen. Ein noch ausstehender Host-Request wird nicht nach drei Sekunden erneut gekauft.

Infrastrukturpläne erhalten nach Fehlern eine kurze Neuplanungsfrist. Fehlgeschlagene optionale Ausbauziele werden vorübergehend übersprungen, damit beispielsweise ein unmöglicher Tower-Plan nicht ständig die Lufttechnologie blockiert. Stromreparatur darf einen wartenden Forschungs-/Ausbildungsplan freigeben; dessen bezahlte Queue bleibt erhalten.

## Grenzen und Diagnose

Die Kontrolle macht unpassierbares Terrain nicht passierbar und garantiert keine erfolgreiche Fertigstellung bei fehlenden Ressourcen, Arbeitern oder Produzenten. Sie beendet beziehungsweise pausiert erfolglose Aufgaben und lässt die übrige KI weiterarbeiten. Bereits bezahlte Produktion wird nicht vernichtet oder ein zweites Mal bezahlt, nur um einen Timeout zu beseitigen.

Die aktuelle Wiederherstellung beziehungsweise letzte Ablehnung erscheint im vorhandenen `AIController.LastDecision`. `AI.OrderProgress` ist über die bestehenden Performance-Messungen erfassbar. Die Beobachtungen und Sperren sind hostlokale, vorübergehende KI-Daten; `BeginMatch` entfernt sie.

## Prüfung

- `dotnet build RTS.csproj`: erfolgreich, keine Warnungen oder Fehler.
- `dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj`: 1.174 Checks erfolgreich, davon 51 neue Auftragsfortschrittsprüfungen in `AIOrderProgressChecks.cs`.
- Regressionen prüfen echte Host-Ablehnung/Anwendung, verzögerte Routenplanung, doppelte Käufe, Queue-Erhalt, verlorene Produzenten, blockierte Baustellen und die gemeinsame Bauplatzsuche, langsamen Fortschritt, Bewegung/Stop/Cooldown, Squad-Sammeln/Angriff/Rückzug, verlorenen Medic, Ernte-Recovery und Sessionwechsel.
- Grafikfreie Szenarien; kein neuer mehrminütiger visueller KI-Soak-Test behauptet.
