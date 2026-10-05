# Session-Lifecycle und Late Join

Stand: 05.10.2026; Architektur-Aufgabe 13.

## Zustandsbesitz

`SessionStateService` kapselt die bisher in RTSGame enthaltene Snapshot-Aufnahme und Wiederherstellung. RTSGame übernimmt danach Player-Zuordnung und HUD/Fog-Textur; NetworkInput verwendet denselben Dienst auch in grafikfreien Integrationsprüfungen. Alle Aufrufe erfolgen auf dem Spielthread. Ein Client erhält Zustand und bestätigte Routen, keine hostinternen Jobs.

| Daten | Map-Dateien / WorldData | Laufender SessionSnapshot | Besitzer der Fortschreibung |
| --- | --- | --- | --- |
| Terrain, Tiles, GameplayMarker, Tiberium-Quellen | ja | über WorldData | Host / Editor |
| Tiberium-Zellen und Wachstumswerte | ja, als Ausgangszustand | aktueller Stand über WorldData | Host |
| Armies, Besitz, Freigaben, Ressourcen, Intel-Einstellungen und Perk-Quellen | nein | ja | Host |
| Laufende Gebäude/Units, HP, Army, bezahlter Gebäudepreis, Verhalten | nein | ja | Host |
| Baufortschritt, Speicherinhalt, RallyPoint, aktive und wartende Produktion/Forschung | nein | BuildingState mit Auftrags-ID, Dauer und verstrichener Zeit | Host |
| Fahrzeug-/Soldatenroute, wartende Goto-Aufträge und Squad-Zuordnung | nein | MobileUnitState | Host; Client stellt bestätigte Bewegung dar |
| Besatzung und Rolle, ausgerüstete Soldatenwaffe | nein | RuntimeUnitSnapshot | Host |
| Fabrikausfahrt (Herkunft und Exitposition) | nein | BuildingExitSnapshot | Host / bestätigte lokale Darstellung |
| Harvester-Phase und Ladung | nein | RuntimeUnitSnapshot | HarvestSystem auf Host |
| Planiermodus und zuletzt angewandte Sequenz | nein | RuntimeUnitSnapshot | EarthworkController auf Host |
| Sicht und erkundete Zellen | nein | VisibilitySnapshot | aktive Sicht lokal, dauerhaft erkundet hostautorisiert |
| Suchiteratoren, Retry-/Heil-/Entladezeitgeber und FIFO der Host-Requests | nein | nein | ausschließlich Host |
| Partikel, Decals, laufende visuelle Geschosse und UI-Marker | nein | nein | lokale Darstellung |

WorldData ist kein komplettes Savegame. Beim Map-Save werden insbesondere keine laufenden Armeen oder Produktionsaufträge gespeichert. Ein Session-Snapshot überträgt den aktuellen Weltstand; er lädt nicht erneut die ursprünglichen Ressourcenwerte einer Map. Wiederholtes game-start bewahrt die publizierte Welt samt Tiberium und Markern.

## Aufnahme und Anwendung

Der Transport erfasst auf dem Spielthread JoinAccepted, Player-Daten, SessionSnapshot und SessionReady, bevor der neue Peer reguläre Broadcasts empfängt. NetworkHandler.Protokollversion ist jetzt **6**: Die erweiterten Snapshot-Felder verlangen bei allen Teilnehmern diesen Build. Die Versionsprüfung aus Aufgabe 12 bleibt erhalten.

Wiederherstellung:

1. Planungsarbeit verwerfen, bisherige Mitgliedschaft entfernen, Army- und Weltdaten anwenden.
2. Alle Runtime-Identitäten über die registrierten Factories erzeugen und der Welt zuordnen. Es wird kein normaler Spawn mit zufälliger Ausweichposition durchgeführt.
3. Zustand einschließlich Ausfahrt, Planieren, Waffe, HP, Produktion und Cargo anwenden.
4. Besatzung mit ihren Rollen wiederherstellen; danach nur die tatsächlich aktiven äußeren Footprints registrieren. Fahrer und Crew haben keine eigenen belegten Zellen. Fahrzeuge im Fabrikausgang überschreiben das Gebäude nicht.
5. Autoritatives Verhalten und Sicht anwenden; lokale Effekte zurücksetzen. Es entstehen weder Client-Pfadsuchen noch Ernte-/Heiljobs.

Das übernimmt beispielsweise einen Harvester mit 284 Ladung beim Entladen. Der Entladezeitgeber bleibt auf dem bestehenden Host. Nach Ablauf veröffentlicht der Host absolute Cargo-, Speicher- und Army-Ressourcenwerte; ein beitretender Client schreibt keine Ressourcen selbst gut. Genauso werden laufende Forschungsaufträge übernommen und ihr späterer Abschluss als permanenter Perk bestätigt.

## Abbruch und Neustart

Ein Stop oder zerstörter Auftraggeber entwertet ausstehende Routen; späte Ergebnisse dürfen die Bewegung nicht wieder starten. Zerstörung eines Produktionsgebäudes leert seine Queue sofort. Bereits bezahlte Forschung wird dadurch nicht abgeschlossen oder zusätzlich erstattet.

Ein abgewiesener game-start ändert bestehende Planung und Jobs nicht. Erst nach erfolgreicher Startplatzzuweisung verwirft NetworkHost die bisherige Request-FIFO, Goto-Endpunkte, Versionszuordnungen, Suchjobs, Erdarbeiten, Ernte-/Heiljobs und autoritative Geschosse. Earthwork.Reset beendet auch den Modus der bisherigen Arbeiter und stellt deren Fahrparameter wieder her.

Der bestätigte Start verwendet auf Host und Client `SessionStateService.ApplyMatchStart`: bisherige Match-Units entfernen (bestehende GenericBuilding-/TiberiumSource-Ausnahmen erhalten), Perks und Sicht löschen, lokale Effekte leeren, Ressourcen setzen und jedem Teilnehmer einen neuen Bulldozer mit derselben hostbestimmten Fahrer-ID geben. UI und Startkamera folgen anschließend. Der bestehende Konsolenablauf mit map-publish vor game-start bleibt erhalten.

SessionGeneration-Wechsel verwirft Host-Requests, Systeme, Planungen und deren temporäre Bewegungsabsicht; die Hostzeit beginnt wieder bei null. Ein Disconnect ist kein automatisches neues Match und lädt keine ursprüngliche Map. Die bisherige Welt bleibt bestehen, bis sie publiziert, ersetzt oder per game-start neu aufgestellt wird. Es wird kein Hostwechsel aus einem Client-Snapshot unterstützt.

## Prüfung

`tests/GridNavigationChecks/SessionLifecycleChecks.cs` enthält 58 zusätzliche Integrationsprüfungen ohne Reflection oder uninitialisierte Objekte. Kleine regulär konstruierte Welten laufen über NetworkHandler, NetworkInput, NetworkHost, den tatsächlichen Planungsscheduler und SessionStateService. Die Factory-Abhängigkeit erzeugt Modell-freie Testinstanzen; Base/Factory verwenden Building-Unterklassen mit den realen Katalogdaten und der unveränderten BuildingState-/Produktionslogik.

Geprüft sind insbesondere:

- echte TCP-Beitritte bei laufender Produktion/Forschung/Ernte und beim zeitgesteuerten Entladen;
- Zustands-, Ressourcen-, Speicher-, Rollen-, Waffen-, Queue-, Marker- und Sichtgleichheit; Wiederanwendung ohne doppelte Kosten oder Units;
- Forschungsschluss und einmalige Entladegutschrift nach dem Beitritt;
- Stop und Tod während budgetierter Host-Planung, Tod eines Produktionsgebäudes;
- abgewiesener Start bei fehlenden Markern und zwei erfolgreiche Neustarts mit neuen IDs, 10.000 Ressourcen und zurückgesetzten Perks/Jobs;
- tatsächlich aktive Heil-/Ernte-/Erdarbeitsjobs und Raketen vor Reset; Entfernung lokaler Geschosse und vorheriger Sicht;
- Sessionwechsel während Planung und fehlende Veröffentlichung alter Callbacks.

Validierung: `dotnet build RTS.csproj` ohne Warnungen/Fehler; insgesamt **1.057** Checks bestanden; `git diff --check` sauber.

## Grenzen

Keine neue grafische Multiplayer-Schlacht oder optische Fahrzeugabnahme. Aufgabe 02 bleibt ausdrücklich offen. GPU-Effekte und HUD werden über ihre bestehenden Reset-Methoden geleert; die neue Integration prüft exemplarisch Geschossdarstellung und Sicht, keine Pixelbilder aller Effekte. Laufende visuelle Raketen vor dem Beitritt und lokale Todesanimationen werden weiterhin nicht als flüchtige Darstellung nachgeladen; spätere Einschläge/HP werden repliziert. Der Snapshot ist kein Hostmigration-/Savegame-Vertrag und serialisiert keine Scheduler oder Host-FSMs. Erweiterungen des Snapshots müssen erneut über echte Anwendung und Versionsprüfung geprüft werden.
