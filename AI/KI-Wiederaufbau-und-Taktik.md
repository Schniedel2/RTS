# KI: Wiederaufbau und taktische Steuerung

Stand: 05.10.2026, AI-Spieler-TODO Phase 1, Punkt 3.

## Zuständigkeiten

`ArmyGoalController` in `src/AI/AIController.cs` führt den ersten Kernaufbau und dessen Instandhaltung aus. `AIController` koordiniert zusätzlich Bedrohungsbewertung, Scout, Basisverteidigung, Fahrzeugproduktion, Infrastruktur, Gegenmaßnahmen und den Squad-Zyklus. Der Aufbauzustand beschreibt dabei den Wirtschaftsplan, nicht die Einsatzfähigkeit jeder vorhandenen Kampftruppe.

Zuvor beendete `AIController.Update` das gesamte Update, sobald der Kernplan nicht mehr `BaseDefenseReady` meldete. Ein Raffinerie- oder Kasernenverlust konnte damit auch alle taktischen Entscheidungen abschalten, bis Ersatzbau, Ernteauftrag und Basisverteidiger wieder bereit waren.

## Ablauf nach der Änderung

Der erste erfolgreiche Kernaufbau setzt `_tacticsActivated`. Dieser Zustand bleibt während des Matches erhalten. Erst `BeginMatch` setzt ihn zusammen mit den Controllerinstanzen und Squad-Missionen zurück. Die anfängliche Reihenfolge bleibt damit erhalten; späterer Wiederaufbau friert bestehende Taktik nicht mehr ein.

Während `ArmyGoalController` wieder am Kernaufbau arbeitet:

- Bedrohungen werden weiter bewertet und vorhandene Scouts erkunden weiter.
- Die Basisverteidigung kann Gegner abfangen und ihre Verteidiger nach dem Alarm zurückschicken.
- Der vorhandene Squad-Zyklus bleibt erhalten; laufende Angriffe und Rückzüge werden weiter aktualisiert. Eine bereits aktive Erholungsphase bewertet weiter die Gesundheit und bestätigte Auflösung des Squads.
- Neue Scoutkäufe, optionale Fahrzeug-/Squadproduktion und Ausbau-/Gegenmaßnahmenpläne erhalten keine neuen Controllerupdates. Kernaufbau und Ersatz seiner notwendigen Units haben Vorrang.
- Bestätigte Produktions-, Forschungs-, Bau- und andere Host-Aufträge werden dadurch nicht abgebrochen; ihre Simulation läuft im zuständigen Hostsystem weiter.

Sobald der Kernplan erneut `BaseDefenseReady` erreicht, werden die pausierten Controllerupdates fortgesetzt. Taktische Instanzen, Ziele und Phasen werden nicht neu erstellt. Die Statusausgabe zeigt während des Kernwiederaufbaus dessen Entscheidung zusammen mit der taktischen Entscheidung.

Ein alleiniger Reaktorverlust wird im heutigen Stand über `AIInfrastructureController.RequiresImmediatePower` erkannt. Solange der Kernplan bereit ist, läuft diese dringende Stromplanung weiterhin vor dem optionalen Ausbau; auch hier bleiben die taktischen Updates aktiv. Es wurde keine zweite Strom-Zustandsmaschine eingeführt.

## Befehlsweg und Grenzen

Alle neuen Entscheidungen verwenden weiterhin `PlayerCommandService` und die normalen Requests. Der Host entscheidet über Ausführung und verteilt bestätigte Commands. Die Aktivierung ist hostinterner KI-Zustand und benötigt keinen Client-Snapshot.

Diese Änderung ist noch keine zentrale Ressourcen-/Arbeiterreservierung. Bereits bestätigte Aufträge können weiter Ressourcen oder Arbeiter binden; die späteren TODOs für eine koordinierte AI-Auftragswarteschlange bleiben offen. Die vorhandene Priorität der Basisverteidigung gegenüber der offensiven Squad-Steuerung bleibt bestehen. Bei fehlendem Produzenten kann eine neue Ausbildung weiterhin warten; kontinuierliche Auftragsdiagnose ist der nächste offene TODO-Punkt.

## Nachweis

`tests/GridNavigationChecks/AIReconstructionChecks.cs` durchläuft den normalen `AIController.Update` mit regulären grafikfreien Welten, Units und Netzwerkinstanzen. Der RTSGame-Rahmen für die noch global verwendete Economy-Fassade wird als Testhülle aufgebaut; interne KI-Phasen werden nicht per Reflection gesetzt.

Die Szenarien prüfen Gebäudeentfernung und Ersatzbaustellen, Verteidigungsrequests samt echter Host-Anwendung, Rückkehr nach Ende des Alarms, pausierte optionale Aufträge und Wiederaufnahme. Ein regulär vorbereiteter Squad startet einen Angriff, setzt ihn nach Raffinerieverlust fort, zieht sich bei Schaden zurück und durchläuft Erholung trotz späterem Kasernenverlust. Zusätzlich werden anfänglicher Aufbau, Matchreset, Idle und Nicht-Host geprüft.

Build ohne Warnungen/Fehler; 1.096 Checks bestanden, davon 39 neue. Die Tests bestätigen Controller-/Requestverhalten und Host-Anwendung; sie ersetzen keine optische Prüfung einer mehrminütigen KI-Schlacht oder eine Messung ihrer Bildrate.
