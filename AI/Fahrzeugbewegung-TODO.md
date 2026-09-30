# TODO: Flüssige Fahrzeugbewegung

Diese Liste wird in Reihenfolge abgearbeitet. Ein Punkt gilt erst als erledigt, wenn das Verhalten implementiert und mit den beschriebenen Checks sowie einem sichtbaren Testspiel geprüft wurde.

`CanTurnInPlace` beschreibt ausschließlich eine zusätzliche Fähigkeit: Das Fahrzeug kann seine Fahrtrichtung im Stand ändern. Fahrzeuge mit dieser Fähigkeit dürfen und sollen sich trotzdem während der Fahrt drehen. Das Flag darf daher nicht allgemein als Bedingung zum Anhalten vor einer Kurve verwendet werden.

Die Modellangaben beziehen sich auf Codex-Aufgaben. `gpt-6-sol` ist für begrenzte Implementierungen geeignet; `gpt-6-astra` empfiehlt sich für die gemeinsame Lenkarchitektur, Footprint-Sicherheit und schwierige Bewegungsfehler.

Abarbeiten mit dem Chat-Befehl: Arbeite den nächsten offenen Punkt aus "Fahrzeugbewegung-TODO.md" vollständig ab.

## Phase 1: Bedeutung und gemeinsame Grundlagen

- [x] **Semantik von `CanTurnInPlace` korrigieren**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - `CanTurnInPlace` erlaubt eine Drehung ohne Vorwärts- oder Rückwärtsbewegung.
  - Das Flag verhindert niemals das Lenken während der Fahrt.
  - Eine getrennte Entscheidung bestimmt, wann eine Kurve fahrend und wann im Stand ausgeführt wird.
  - Fertig, wenn ein Fahrzeug mit `CanTurnInPlace = true` eine normale 45°-Kurve ohne vollständigen Halt durchfährt.

- [x] **Gemeinsame Fahrzeug-Lenkparameter einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `medium` Reasoning.
  - Parameter für maximalen Lenkwinkel während der Fahrt, Schwelle für Standdrehung, Kurvengeschwindigkeit und erlaubte Rückwärtsfahrt einführen.
  - Namen und Einheiten werden dokumentiert; Winkel werden nicht mehr indirekt über schwer lesbare Dot-Grenzen konfiguriert.
  - Soldaten und Helicopter bleiben von den Bodenfahrzeugparametern unabhängig.
  - Fertig, wenn Tank, Auto, Harvester und Bulldozer ihre Unterschiede über Parameter statt eigener Grundlogik ausdrücken können.

- [x] **Tank auf die gemeinsame Routenvorausschau umstellen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Die Tank-Sonderlogik steuert nicht länger ausschließlich den Mittelpunkt der jeweils nächsten Zelle an.
  - Rückwärtsfahrt und Kettenfahrzeug-Drehung bleiben als Tank-Fähigkeiten erhalten.
  - Gemeinsames Waypoint-Handling, Fortschrittsüberwachung und Blockadebehandlung werden verwendet.
  - Fertig, wenn der Tank gerade Strecken und freie Kurven ohne Stop-and-go abfährt.

## Phase 2: Flüssige und sichere Kurven

- [x] **Geschwindigkeit abhängig vom Lenkwinkel regeln**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Auf gerader Strecke wird die volle Geschwindigkeit erreicht.
  - In leichten und mittleren Kurven fährt das Fahrzeug weiter und reduziert seine Geschwindigkeit stufenlos.
  - Nur bei einer echten Kehrtwende oder unzureichendem Platz wird angehalten beziehungsweise rückwärts manövriert.
  - Fertig, wenn 45°- und 90°-Kurven ohne abruptes Anhalten durchfahren werden und enge Wendungen kontrolliert bleiben.

- [x] **Sichere Kurvenvorausschau über mehrere Wegpunkte einbauen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Ein Fahrzeug darf einen weiter entfernten Wegpunkt als Lenkziel verwenden, wenn die Verbindung für seinen vollständigen Footprint frei ist.
  - Die Prüfung berücksichtigt Terrainregeln, Gebäude, andere harte Blockaden und diagonale Ecken.
  - Ist die Verbindung nicht sicher, bleibt der nächste bestätigte Wegpunkt verbindlich.
  - Fertig, wenn freie Kurven sichtbar geglättet werden, ohne dass Fahrzeuge Gebäudeecken oder gesperrte Zellen schneiden.

- [ ] **Routenkorridor und tatsächliche Bewegung abstimmen**
  - **Zwischenstand 30.09.2026:** Das bestätigte Lenkziel wird zwischen Updates festgehalten und begrenzt den Routenfortschritt. Die tatsächliche Kurve darf benachbarte freie Zellen verwenden; jeder Zellübergang bleibt durch `GameGrid.TryMove`, den vollständigen harten Footprint und Diagonalregeln geschützt. Die weiche Clearance folgt kontinuierlicher Position und freier Drehung, die Festfahrerkennung dem Lenkziel. Eine Regression für eine zwölf Zellen lange freie Fahrt mit 3x4-Footprint ist enthalten. Build und 521 Headless-Checks erfolgreich. Sichtbare Wiederholungsprüfung von Tank, Harvester und Bulldozer steht aus.
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Eine geglättete Fahrkurve bleibt innerhalb eines vom Host geprüften Korridors.
  - Das GameGrid registriert weiterhin eine konsistente harte Belegung und eine zur Rotation passende weiche Clearance.
  - Der Fortschritt auf der ursprünglichen Route bleibt eindeutig, auch wenn nicht jeder Zellmittelpunkt exakt berührt wird.
  - Fertig, wenn lange oder breite Fahrzeuge keine optischen Überschneidungen erzeugen und nicht fälschlich als festgefahren gelten.

- [ ] **Beschleunigung und Bremsen ergänzen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Fahrzeuge besitzen aktuelle Geschwindigkeit, Beschleunigung und Bremsverzögerung.
  - Vor engen Kurven und dem Endpunkt wird rechtzeitig verzögert.
  - Netzwerkzustände bleiben endlich und reproduzierbar; NaN und Infinity werden ausgeschlossen.
  - Fertig, wenn Fahrzeuge nicht sofort auf Höchstgeschwindigkeit springen und am Ziel nicht sichtbar überschießen.

## Phase 3: Fahrzeugtypen abstimmen

- [ ] **Tank-Fahrprofil abstimmen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `medium` Reasoning.
  - Der Tank fährt normale Kurven, kann für enge Wendungen auf der Stelle drehen und nutzt bei passenden Zielen seine Rückwärtsfahrt.
  - Fertig, wenn eine Teststrecke mit Gerade, 45°, 90° und Kehrtwende glaubwürdig gefahren wird.

- [ ] **Auto- und Jeep-Fahrprofil abstimmen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `medium` Reasoning.
  - Radfahrzeuge bevorzugen weite Kurven und drehen regulär nicht auf der Stelle.
  - Falls eine Route fahrphysikalisch nicht erreichbar ist, wird neu geplant oder kontrolliert rangiert.
  - Fertig, wenn Autos nicht seitlich versetzen und an engen Zielen nicht endlos kreisen.

- [ ] **Harvester- und Bulldozer-Fahrprofil abstimmen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Schwere Arbeitsfahrzeuge fahren langsamere, stabile Kurven.
  - Bulldozer dürfen beim präzisen Bauen und Planieren auf der Stelle drehen.
  - Harvester behalten zuverlässige Zufahrt zu Raffinerie und Ressourcenfeldern.
  - Fertig, wenn beide Fahrzeugtypen ihre Arbeitsziele erreichen, ohne an Gebäudeecken zu pendeln oder im Kreis zu fahren.

## Phase 4: Netzwerk und Robustheit

- [ ] **Host-Autorität für geglättete Bewegung absichern**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Der Host entscheidet weiterhin über Route, Blockaden, Neuplanung und Auftragsende.
  - Clients stellen die Bewegung flüssig dar, ohne einen abweichenden Wegfortschritt zu erzeugen.
  - Navigationsrevisionen und Korrekturen bleiben mit Kurvenvorausschau und Geschwindigkeit kompatibel.
  - Fertig, wenn Host und Client nach langen Kurvenfahrten dieselbe Zelle, Route und Befehlswarteschlange melden.

- [ ] **Festfahr-Erkennung an kontinuierliche Kurven anpassen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Langsames Fahren und bewusstes Rangieren gelten als Fortschritt.
  - Kreisen, Pendeln und Drehen ohne Ortsfortschritt werden weiterhin erkannt.
  - Neuplanung übernimmt Ziel und Shift-Warteschlange unverändert.
  - Fertig, wenn eine langsame enge Kurve keinen falschen Retry auslöst und ein wirklich blockiertes Fahrzeug weiterhin neu plant.

## Phase 5: Tests und Diagnose

- [ ] **Deterministische Lenkungschecks erweitern**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Tests für freie 45°- und 90°-Kurven, Kehrtwende, Rückwärtsziel, schmale Gebäudeecke sowie verschiedene Frame-Zeiten ergänzen.
  - Geprüft werden Ankunft, erlaubter Korridor, Anzahl vollständiger Halte, Wegfortschritt und fehlende Endlosschleifen.
  - Fertig, wenn alte und neue Bewegungsregressionen im Headless-Check reproduzierbar auffallen.

- [ ] **Bewegungsdiagnose für Testfahrten ergänzen**
  - **Empfohlenes Modell:** `gpt-6-luna` mit `medium` Reasoning.
  - Der Debugtext zeigt Fahrmodus, Geschwindigkeit, Lenkwinkel, gewählten Vorausschaupunkt und Grund einer Standdrehung.
  - Optional kann die bestätigte Route zusammen mit dem aktuellen Lenkziel visualisiert werden.
  - Fertig, wenn sich eine eckige oder festgefahrene Fahrt ohne Debugger nachvollziehen lässt.

- [ ] **Mehrminütigen Fahrzeug-Soak-Test durchführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Mehrere Tanks, Jeeps, Harvester und Bulldozer bewegen sich gleichzeitig durch eine bebaute Basis.
  - Geprüft werden Stillstände, Kreisen, Host-/Client-Abweichungen, Pathfinding-Last und Footprint-Konflikte.
  - Fertig, wenn der Test wiederholbar ohne dauerhaft verlorene Fahrzeuge läuft.

## Empfohlene unmittelbare Reihenfolge

1. Semantik von `CanTurnInPlace` korrigieren.
2. Gemeinsame Fahrzeug-Lenkparameter einführen.
3. Tank an die gemeinsame Routenvorausschau anbinden.
4. Geschwindigkeit abhängig vom Lenkwinkel regeln.
5. Sichere Kurvenvorausschau einbauen.
6. Routenkorridor und GameGrid-Belegung absichern.
7. Fahrzeugprofile einzeln abstimmen.
8. Netzwerk- und Festfahr-Erkennung prüfen.
9. Diagnose und Soak-Test ergänzen.
