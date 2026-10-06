# KI-Laufzeitkontext und verbleibende Remote-Hürden

Stand: 06.10.2026. Umsetzung von KI-Client-TODO, Aufgabe 01.

## Implementierter Vertrag

AIContext bindet einen Controller ausdrücklich an Player/Akteur-ID, ArmyId, GameWorld und NetworkHandler. Es stellt Army-Zugriff, weltbezogenes Pricing, Katalogzugriff, PlayerCommandService, Army-Sicht, Simulationszeit und Sessiongeneration bereit. Die Identität ist fest gebunden: nach Armywechsel muss ein neuer Kontext erzeugt werden. AIController akzeptiert den Kontext direkt; der bestehende Aufruf mit Player/Welt/Netzwerk baut bzw. verwendet ihn wieder. ArmyGoalController nutzt denselben Kontext und dessen Befehlsdienst. BeginMatch verwirft den bisherigen Kontext.

Die Teilcontroller und statischen KI-Planer beziehen Ressourcen, Perks und Preise aus GameWorld.SimulationArmies/SimulationPricing statt aus Globals.Game. Der Client-Pricing-Facade verwendet im normalen Spiel dieselbe weltbezogene Pricing-Instanz. ConfigureSimulation entwertet den Pricing-Cache beim Wechsel des Army-Kontexts. Es gibt keine Kopie der Preis-/Perkregeln in der KI.

VisibilitySystem.GetSimulationVisibility liest Army-Sicht und optional ausdrücklich geteilte verbündete Weltsicht. Es berücksichtigt keine Kamera, menschliche Auswahl, Spectator-Anzeige, deaktivierten Fog of War oder Editor-Darstellung. Scouting, Kandidatensuche, Verteidigung, Angriffszielwahl und Gegnerbeobachtung verwenden diesen Weg. Verbündete Sicht wird aus dem Welt-/Army-Kontext bestimmt, nicht aus dem lokalen Anzeigecache.

Scouting versendet auch im normalen Spielerbetrieb über PlayerCommandService mit dem konfigurierten Simulationsnetzwerk; die KI verwendet weiterhin ihre ausdrücklich angegebene Akteur-ID. Sessiongenerationen für KI-Leases und Erinnerungen kommen aus dem Simulationsnetzwerk. Keine Globals.Game-/Globals.World-Referenzen verbleiben unmittelbar in src/AI. Globale Messinstrumente in AIPlayer/PerformanceMeasurements sind davon getrennt; sie wählen weder Army noch Befehlsakteur.

## Was weiterhin auf dem Host läuft

ArmyGoalController.Update und AIController.Update behalten ihre IsHost-Sperren. AIOrderQueue.Dispatch verwirft sich auf einem Nicht-Host oder bei Generationwechsel. AIOrderProgressMonitor.Update benötigt weiterhin einen Host. Das ist beabsichtigt: ein Kontext ist noch keine Remote-Zuweisung.

Gameplay-Dienste bleiben in NetworkHost und den zugehörigen Host-Diensten: Bewegung/Flug, Produktion, Baubearbeitung, Erntezustandsübergänge, Kampf, Heilung, Ressourcenabbuchung und bestätigte Befehle. Diese Dienste werden nicht mit der hochstufigen KI abgeschaltet oder auf deren Client verlagert.

## Konkrete Hürden nach diesem Schritt

| Bereich | Aktuelle Kopplung | Geplante Aufgabe |
|---|---|---|
| AIOrderQueue.Order / Dispatch | LocalRequestReceipt und ExecutionReceipt; lokale Router-/Dispatch-/Abandon-APIs des NetworkHandler | 04: gemeinsamer lokaler/entfernter Rückmeldevertrag |
| AIProductionPlanExecutor | _receipt, Pending/Rejected/Abandoned und lokale Auftragsannahme | 04: Request-/Order-IDs und Rückmeldung ersetzen lokale Annahmen |
| AIOrderProgressMonitor | GetLastLocalRejection, Host-Sperre und lokale Wiederaufnahme | 04, anschließend 06 |
| ScoutingController | Lokale LastRequest-Receipt für Ablehnung/Stillstand | 04; menschliches Client-Scouting bleibt weiterhin nutzbar |
| Netzwerk-Akteur | Router ist derzeit nach Player-ID registriert; Peer-Sender und KI-Akteur sind beim entfernten Client noch nicht getrennt | 05: Army-/Controllergeneration, autorisierte Zuweisung; nicht einfach mehrere Bots unter derselben Actor-ID starten |
| Hochstufige Controller | Host-Sperren und Host-Aufruf in RTSGame | 05/06: erst nach gültiger Zuweisung Remote-Betrieb zulassen |
| AIUnitTasks / ScoutingTargets | Lokale Claims/Leases, Welt-Registrierung und Sessiongeneration | 06: controllerlokale Planung von autoritativen Gameplay-Aufträgen trennen; nicht hostlokale Objekte übertragen |
| Pfad-/Bauplatzvorprüfung | Weltbasierte Scheduler/Grid-/Modellmetadaten | 06: auf replizierten Daten mit Budget, finale Hostprüfung bleibt erhalten |
| Verlustmeldungen | RecordCombatLoss wird bislang über Host-Spielabläufe zugeführt | 06: ausreichende bestätigte Informationen für entfernte Erinnerung/Auswertung sicherstellen |
| Modell-/Factory-/Provider-Pfade | Einige Aufrufe außerhalb src/AI brauchen noch Globals, MeshHandler, geladenes Content oder RTSGame; Sicht-Rendering hat weiter Präsentationszugriffe | 08: Bot-Prozess ohne Fenster. Ein einzelner erfolgreicher Kontext-Test beweist noch keinen vollständigen grafikfreien Spielstart |
| Controllerübergabe | Interne Pläne und Pending-Requests sind noch nicht migrationsfähig | 07: Generation widerrufen, Weltzustand rekonstruieren, Doppelbestellungen verhindern |

## Grenzen und Prüfung

Dieser Schritt aktiviert keinen entfernten KI-Client und verändert keine Befehlsberechtigungen. Hostautorität und bestehende Netzwerkwege bleiben erhalten. Eine Simulationswelt muss ihren Army-/Netzwerkkontext explizit konfigurieren; alte Headless-Tests mit nicht initialisierten Weltobjekten wurden entsprechend angepasst, statt im Produktionscode auf den menschlichen Client zurückzufallen.

Build ohne Warnungen/Fehler und 1.506 Headless-Prüfungen bestanden. 24 neue Kontextprüfungen sichern Identität, weltbezogene Preise/Perks/Ressourcen, Zeit, Sessiongeneration, andere Armies/Welten, globale Anzeige-Schalter, explizite Allied-Sicht und Armywechsel. Ein Host-Entscheidungsupdate sowie Pricing/Katalog/Sicht werden ohne Globals.Game geprüft. Die bestehenden KI-Szenarien prüfen weiter Aufbau, Wiederaufbau, Produktion, taktische Abläufe und Auftragsverwaltung. Kein vollständiger Dedicated-Server-/Remote-/grafischer Langzeittest wird behauptet.

Nächster Schritt: versionierte JSON-Verhaltensprofile gemäß Aufgabe 02; Netzwerk-Rückmeldungen folgen in 04.
