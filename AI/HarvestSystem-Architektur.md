# Hostseitiges HarvestSystem

Stand: 01.10.2026, Architektur-TODO 05.

`src/Systems/HarvestSystem.cs` besitzt die Ernteaufträge und die Phasen DrivingToField, Harvesting, ReturningToSilo und Unloading. NetworkHost besitzt eine Instanz, nimmt Requests in seiner bisherigen FIFO an und ruft das System ausschließlich während der autoritativen Simulation auf dem Spielthread auf. Das System bekommt Welt, ArmyHandler, Peer-ID, Veröffentlichung, Bewegungsplanung sowie Session- und Befehlsversionszugang explizit im Konstruktor. Es hat keine direkte Globals-Abhängigkeit; bestehende Unit-Methoden verwenden weiterhin ihre bisherigen globalen Dienste.

## Zuständigkeiten

- HarvestSystem prüft Start-/Rückkehraufträge, wählt Ressourcen und Lager, verwaltet Lade-/Entladezeit, Retry-Intervalle, gescheiterte Abladepositionen und Auftragsende. Die bisherigen Preise, Mengen, Zeiten und Bewegungsregeln bleiben erhalten.
- NetworkHost ordnet Requests, ruft Start/Return/Cancel auf, stellt über `QueueHarvestRoute` die gemeinsame fortsetzbare Goto-Planung bereit und veröffentlicht sämtliche Ergebnisse über `PublishAsync`: lokale Anwendung, danach geordneter Broadcast. Die Routenprüfung und das gemeinsame Arbeitsbudget bleiben unverändert.
- Harvester hält replizierte Phase/Ladung, Darstellung und den normalen Fahrcontroller. Seine Update-Methode führt keine zweite Ernte-Zustandsmaschine aus. Harvesting/Unloading beenden die replizierte Anfahrt, wie bei den jüngsten Fehlerkorrekturen.
- TiberiumHandler besitzt weiterhin Ressourcenmenge/Wachstum; Building besitzt Lagerinhalt; Army besitzt den Kontostand. Nur angenommene Ladung wird gutgeschrieben. Teilvolle Lager lassen den übrigen Cargo im Harvester.

Die Systemgrenze verwendet vorerst vorhandene NetworkMessage-DTOs und einen Planungsdelegate. Sie benötigt keine neuen Nachrichtentypen. Eine spätere Payload-Typisierung ist Aufgabe 12; dieser Umbau führt keine zweite Routenplanung ein.

## Abbruch und Lebenszyklus

`Cancel` entfernt die Ernteauftragszuständigkeit. Der Host veröffentlicht separat Stop oder den ersetzenden Befehl. Stop und UnitAction.Stop verwenden den bestehenden gemeinsamen Host-Pfad. Goto/Move Away heben den Auftrag nur für autorisierte Empfänger auf; fremde Requests können ihn nicht entfernen.

Planungsjobs prüfen Auftragsidentität, Unit-Identität, Request-ID, Befehlsversion, Sessiongeneration, Phase und Lagergültigkeit. Nach Cancel, Ersatzauftrag, Tod/Entfernung oder Reset gilt ein altes Resultat nicht mehr. Auch der Ergebniscallback prüft seine Gültigkeit nochmals. Ein abgebrochener Callback verändert höchstens sein abgetrenntes Jobobjekt und kann keinen Auftrag neu anlegen.

Sessionwechsel setzen das System mit den übrigen Host-Systemen zurück. Ein erfolgreich vorbereiteter game-start verwirft die bisherigen Erntejobs, bevor neue Einheiten starten; ein abgewiesener game-start verändert sie nicht. World-/Map-Austausch entwertet Planung über den bestehenden Scheduler-Reset; entfernte Units verlieren ihren Auftrag beim nächsten Systemupdate. Ein zerstörtes oder entferntes Lager darf auch während der Entladezeit keine Ladung oder Ressourcen mehr annehmen.

## Replikation, Late Join und Mapdaten

HarvestCommand überträgt Phase/Ladung, GotoCommand vollständige Routen, TiberiumHarvestCommand die verbleibende Ressourcenmenge und ArmyResourcesCommand das Guthaben. Lagerinhalt bleibt Teil des bisherigen Building-Zustands.

`RTSGame.CaptureSessionSnapshot` und `ApplySessionSnapshot` übertragen weiterhin Harvester-Phase/Cargo und Unit-/Building-Zustände, Terrain/Tiberium und Army-Ressourcen. Ein beitretender Client startet keine HarvestSystem-Jobs: Der vorhandene Host setzt die Arbeit fort und sendet die folgenden Befehle. FieldCenter, gewähltes Lager, Retry-Zeiten, Kandidaten und Planungsiteratoren sind hostinterner flüchtiger Zustand. Es gibt dadurch weder Hostmigration noch ein vollständiges Savegame für laufende Ernteaufträge. Map-Startdaten sind davon getrennt und speichern wie bisher Tiberium/Mapobjekte.

## Validierung

Build ohne Warnungen/Fehler und 687 Checks bestanden. Der neue grafikfreie Harness `tests/GridNavigationChecks/HarvestSystemChecks.cs` ergänzt 27 Checks: fremde Requests, leeres Feld, Harvesting, Vollbeladung, Rückkehr, erfolglose Anfahrten/Retry, späte Resultate nach Reset, Entladezeit, einmaliger Ressourcentransfer, manuelle Rückkehr, verschwundenes Lager, Stop, Session-/Befehlswechsel, Ressourcenende, Teilkapazität und Entfernen des Harvesters. Veröffentlichte Zustände werden durch den echten NetworkInput angewandt. Der Planner ist für die Zustandsmaschinentests kontrolliert ersetzt; tatsächliche Host-/Routen-/Wire-Checks aus Aufgabe 04 und die bisherigen Lageranfahrt-Checks laufen zusätzlich.

Zwei zusätzliche tatsächliche Host-game-start-Checks bestätigen, dass ein abgewiesener Neustart Jobs erhält und ein akzeptierter Neustart sie entfernt. Bestehende Tests für eine mehrfach blockierte Lageranfahrt greifen jetzt auf den Systemzustand statt auf Host-HarvestJobs zu. Reflection bleibt für den grafikfreien Weltaufbau erforderlich und wird separat in Aufgabe 11 behandelt. Kein optischer Test einer kompletten Erntefahrt und keine neue Behauptung zur Lösung aller Gebäudeecken-Fälle. Fahrzeugprofil-/Soak-Aufgaben sowie die optische Abnahme von Architekturpunkt 02 bleiben offen.

Nächster umsetzbarer Architekturpunkt: 06, Sanitäterlogik nach demselben Zuständigkeitsprinzip abgrenzen.
