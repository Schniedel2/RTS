# KI: Proportionale Verteidigungsstärke

`AIBaseDefenseController` wählt weiterhin den nächsten sichtbaren Gegner im Basisradius als Primärziel. Für diesen lokalen Vorfall werden auch sichtbare Gegner innerhalb von zwölf Grid-Zellen um das Primärziel berücksichtigt. Andere Basisseiten werden nicht in diesen Bedarf summiert; getrennte gleichzeitige Vorfälle sind ein späterer TODO-Punkt.

`AIDefenseForceSelector` berechnet eine einfache benötigte Feuerleistung: Summe der gegnerischen Trefferpunkte / 10 Sekunden plus die Hälfte der gegnerischen Feuerleistung gegen Infantry, mindestens 10. Die zehn Sekunden sind ein Schätzhorizont, kein Timer und keine garantierte Kampfzeit. Bewaffnungslose Gegner tragen nur ihre Trefferpunkte bei.

Die verfügbare Stärke eines Verteidigers ist sein Schaden pro Sekunde gegen die tatsächliche Zielpanzerung, gewichtet mit seinem verbliebenen Health-Anteil. Die Waffenwirkung kommt aus dem vorhandenen `DamageCalculator`. Die Auswahlpriorität berücksichtigt zusätzlich Anfahrentfernung außerhalb der Waffenreichweite und Gegenschaden gegen die eigene Panzerung/Domain. Bereits eingesetzte Verteidiger erhalten 15 Prozent Prioritätsbonus, um kleinere Positionsänderungen nicht ständig mit neuen Zuweisungen zu beantworten. Identische Werte werden nach Unit-ID entschieden, die resultierenden IDs bleiben stabil sortiert.

Es werden nur mobile, lebende, nicht eingebettete, operative Units mit Defender-Katalogrolle berücksichtigt. Der Scout bleibt ausgeschlossen. Ungeeignete Ziel-Domains oder deaktivierte Waffen kommen nicht in die Auswahl. Die Gruppe wird nur bis zum geschätzten Bedarf ergänzt; bei unzureichender Stärke können alle passenden verfügbaren Verteidiger eingesetzt werden. Benötigte und zugewiesene Stärke sind als Properties und im Entscheidungsstatus verfügbar.

Wenn die Bedrohung wächst oder Verteidiger verloren/geschwächt werden, wird die Auswahl erneut berechnet. Überschüssige Units erhalten Stop und die vor dem Einsatz gespeicherte Bewegung, Follow-, Attack- oder Terrain-Attack-Aufgabe zurück. Verbleibende Verteidiger behalten ihren gespeicherten Rückkehrzustand. Bei Alarmende wird die Restgruppe zurückgeschickt. Auch nach Verlust des letzten Gebäudes können vorhandene Rückkehrziele genutzt werden. Sämtliche Aktionen gehen durch `PlayerCommandService` und die normale Host-Verarbeitung.

## Grenzen und Prüfung

Dies ist eine heuristische Mengenwahl, keine Gefechtssimulation. Sichtbare Gegner eines Vorfalls bestimmen den Bedarf; die wirksame Waffenstärke wird gegen dessen Primärziel bewertet. Gemischte Bodentruppen/Luftangriffe, getrennte Einsätze, Wegzeit, Deckung, Projektiltrefferwahrscheinlichkeit und Munitionsnachschub sind nicht vollständig modelliert. Fremde Controller-Aufgaben sind noch nicht zentral reserviert; dafür folgt der nächste TODO-Punkt. Stationäre Türme reduzieren den mobilen Bedarf noch nicht.

17 neue Checks in `AIDefenseStrengthChecks.cs` prüfen kleine/große Angriffe, Gesundheit, Entfernung, Sortierung, Panzerabwehr, Luftziele, fehlende Kräfte, gewöhnliche Netzwerkrequests, echte Host-Anwendung, Verstärkung sowie Rückgabe der überschüssigen und zuletzt verbleibenden Units. Die Wiederaufbauchecks erwarten ebenfalls nur noch die notwendige Teilgruppe. Alle 1.350 Checks bestehen; Build ohne Warnungen oder Fehler. Keine neue grafische Schlachtabnahme wurde durchgeführt.
