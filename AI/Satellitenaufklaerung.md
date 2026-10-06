# Satellitenaufklärung

Implementiert: 6. Oktober 2026.

## Freischaltung und Bedienung

In der GDI-Base wird „Satellite Recon“ für 1.500 Ressourcen erforscht (20 Sekunden reguläre Forschungszeit; der vorhandene Testmodus beschleunigt Forschung weiterhin).
Die Army benötigt zusätzlich einen fertigen, eingeschalteten CommunicationsTower, ausreichenden Strom und einen Engineer in der Crew einer fertigen, eingeschalteten GDI-Base. Gewöhnliche Garnisonssoldaten zählen nicht als Operator. Crew-Eignung wird über die Katalogrolle `AIUnitRole.Crew` bestimmt, damit spätere Engineer-Varianten ebenfalls funktionieren.

Im Game-HUD erscheint dauerhaft „Satellite Recon“ unter der Ressourcen-/Stromanzeige. Die Anzeige zeigt Locked, Ready, Active oder den verbleibenden Cooldown. Beim Hover wird der fehlende Voraussetzungstext angezeigt. Ein Klick aktiviert einen verfügbaren Scan ohne Gebäudeselektion.

## Verhalten

- Der erste Scan ist sofort verfügbar, wenn die Voraussetzungen erfüllt sind.
- Ein Scan deckt fünf Sekunden lang die gesamte Karte einschließlich aktuell vorhandener Gegner auf.
- Danach bleibt das Terrain erkundet. Erinnerungsbilder zuletzt beobachteter Gegner sind ein eigenes späteres Feature und werden hier nicht ergänzt.
- Der Cooldown beträgt 180 Sekunden und gehört der Army, nicht einem einzelnen Gebäude.
- Fehlender Strom, Tower oder Operator pausiert den Cooldown. Ein laufender Scan endet trotzdem nach fünf Sekunden.
- Mehrere Tower verkürzen den Cooldown nicht. Abriss/Neubau oder Operatorwechsel setzen ihn nicht zurück.
- Bestehende Regeln zum Teilen von Sicht mit Verbündeten gelten auch für den Scan.

## Architektur

Forschung und Preis liegen im GameplayCatalog. Forschung gewährt den permanenten Perk SatelliteRecon. Versorgte Gebäude stellen SatelliteUplink bzw. SatelliteOperator als Provider-Perks bereit. Die Aktivierung prüft die realen Voraussetzungen erneut auf dem Host.

PlayerCommandService sendet SatelliteReconRequest für die Army. Der Host prüft Kontrollberechtigung, Voraussetzungen und Cooldown und verteilt SatelliteReconCommand geordnet. Zeit läuft in der Simulation; der Host synchronisiert die Timer einmal pro Simulationssekunde und sofort beim Scanende bzw. beim Ende des Cooldowns. Clients entscheiden nicht über die Aktivierung. Der HUD-Timer wird daher sekündlich aktualisiert.

ArmySnapshot enthält die Timer für spätere Beitritte. game-start löscht Forschung und Timer zusammen mit dem bisherigen Matchzustand. Die Netzwerk-Protokollversion wurde auf 7 erhöht; Host und Clients benötigen denselben Stand.

VisibilityGrid nutzt eine Vollsicht-Überlagerung. Der erste Scan markiert alle Zellen einmalig als erkundet; anschließend ist keine vollständige Zellen-Neuberechnung pro Frame nötig. Sichtbare Snapshot-Zellen werden beim nächsten Sichtupdate korrekt zurückgestuft.

Die Headless-Prüfungen stehen in tests/GridNavigationChecks/SatelliteReconChecks.cs und laufen mit dem vorhandenen GridNavigationChecks-Programm. KI-Nutzung der neuen HUD-Fähigkeit ist noch kein Bestandteil dieses Features.
