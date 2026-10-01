# CombatSystem

Stand: 01.10.2026, Architektur-Aufgabe 07.

## Zuständigkeiten

`src/Systems/CombatSystem.cs` besitzt die autoritativen Raketen, deren Flugzeit und ausstehende Einschläge. Es übernimmt automatische Verteidigungsziele, Schussanforderungen, Projektilkollisionen, Trefferwahl und den gemeinsamen Ablauf für Direkt- und Flächenschaden. `DamageCalculator`, `SquadBenefits` und `ProjectileFlightProfile` bleiben die bestehenden Regeln für Schadensmodifikatoren und Flugverhalten.

Das System erhält Welt, Host-ID, Veröffentlichungsfunktion und eine Rückmeldung für Kampfverluste ausdrücklich im Konstruktor. Es greift selbst nicht auf NetworkHandler oder Globals zu. Aufruf und Mutation erfolgen auf dem Spielthread des Hosts. Einige verwendete Unit-/UnitHandler-Methoden greifen weiterhin auf Globals zu; deren Entkopplung und reguläre grafikfreie Fixtures gehören zu Aufgabe 11.

`NetworkHost` prüft Requests, autorisiert Hubschraubermunition, ordnet Befehle und veröffentlicht Ergebnisse. Alle 0,1 Simulationssekunden ruft er `CombatSystem.Update` mit seiner Simulationszeit auf. Vom System erzeugte AttackRequests gehen in dieselbe Host-FIFO wie bisher. Nach einem angenommenen AttackRequest veröffentlicht der Host zunächst den AttackCommand und ruft dann `ResolveAttackAsync` auf. Nach dem Simulationsschritt veröffentlicht `PublishImpactsAsync` entstandene Raketen-Einschläge. Transport und Hintergrundthreads simulieren keinen Kampf.

## Schaden und Darstellung

Bei Raketen enthält ProjectileSpawnCommand die tatsächlich berechnete Startposition und Geschwindigkeit. Der Host simuliert die Flugbahn und entscheidet über Terrain-/Unit-Kollision oder Ablauf der Lebensdauer. ProjectileImpactCommand beschreibt den autoritativen Einschlag; danach folgen UnitHitCommands für die betroffenen Units.

Hitscan-Angriffe erzeugen BulletImpactCommand und anschließend UnitHitCommand. Die bisherigen ballistischen Geschosse bleiben visuelle Flugkörper; ihr Schaden wird wie bisher bei der Auflösung des AttackRequest angewandt. Eine Umstellung ihrer Flugzeit wäre eine eigene Gameplay-Änderung.

Direkt- und Explosionsschaden verwenden gemeinsam `ApplyDamageAsync`: Rüstungsmodifikator, Squad-Bonus, genau ein OnHit-Aufruf, Veröffentlichung absoluter Hitpoints, bei tödlichem Treffer Verlustmeldung und DestroyUnitCommand. NetworkInput übernimmt lediglich den bestätigten HP-Wert und führt OnHit weder auf dem Host noch auf Clients erneut aus. Projektil-/Einschlagdarstellung verändert keine Hitpoints. Tote und eingestiegene Ziele sind weiterhin ausgeschlossen; ausgeschaltete, tote oder eingestiegene Schützen lösen keine neuen Angriffe auf.

Die bisherigen Treffergeometrien und das Balancing bleiben erhalten: Direktangriffe berücksichtigen die Zielhöhe, Raketen prüfen Terrain und Unit-Kollisionskugeln, Explosionsreichweite und Schadensabfall verwenden horizontalen Abstand. Letzteres ist eine vorhandene Vereinfachung: Auch eine hoch fliegende Unit innerhalb des horizontalen Explosionsradius kann Flächenschaden erhalten. Dieses Refactoring führt keine neue räumliche Explosionsregel ein.

## Stop, Lebenszyklus und Late Join

Stop löscht über den bestehenden hostbestätigten Befehlsweg explizite Angriffsziele. Automatische Verteidigung bleibt vom UnitBehavior abhängig. Bereits abgefeuerte Raketen fliegen weiter und verwenden ihre beim Abschuss gespeicherten Schadensdaten, auch wenn der Schütze inzwischen entfernt wurde.

Ein Sessionwechsel und ein akzeptierter game-start leeren aktive Projektile und ausstehende Einschläge; abgewiesene Start-Requests lassen sie bestehen. CombatSystem.Reset verändert keine Unit-/Client-Partikellisten: Diese werden über den bestehenden Session-/Match-Lebenszyklus zurückgesetzt.

Der SessionSnapshot liefert die bestehenden Unit-Zustände einschließlich HP und Zielzustand. Laufende Host-Projektile werden weiterhin nicht als Flugzustand für Late Join gespeichert. Ein später beitretender Client erhält nachfolgende Einschläge und HP-Bestätigungen, aber gegebenenfalls nicht den vor seinem Beitritt gestarteten sichtbaren Raketenflug. Kein Savegame oder Hostmigration wurde ergänzt.

## Prüfung

Build ohne Warnungen und Fehler; insgesamt 772 Checks bestanden. 36 neue CombatSystem-/Hostchecks prüfen direkten Schaden, Rüstungsmultiplikator, getrennte Boden-/Luft-Treffer, Zielfähigkeiten, Schussintervall, Stop für Unit-/Terrainangriffe, inaktive Waffen, tote Ziele und einmalige Verlustmeldung. Tatsächliche Raketenflugbahn und Kollision prüfen Flächenschaden, Radius/Abfall, Einmaligkeit, Flug nach Stop und Reset vor/nach Einschlag. Serialisierung und echter NetworkInput prüfen absolute HP auf Host und separater Clientwelt sowie idempotenten visuellen Raketen-Spawn. Die echte NetworkHost-Integration prüft Sessionreset und akzeptierten/abgewiesenen game-start.

Die vorhandenen Tests wurden weiterverwendet; reflektionsbasierte Welt-/Host-Fixtures bleiben bis Aufgabe 11 erhalten. Keine neue grafische Multiplayer-Abnahme oder Langzeit-Schlacht behauptet.
