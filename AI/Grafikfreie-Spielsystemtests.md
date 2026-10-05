# Grafikfreie Spielsystemtests und explizite Abhängigkeiten

Architekturpunkt 11, umgesetzt am 01.10.2026.

## Reguläre Konstruktion

`GameWorld(width, height, cellSize, graphicsEnabled: false)` initialisiert Terrain-Höhen und Tiles, GameGrid, UnitHandler, Tiberium, Marker, Pfadplanung, Sichtbarkeit und Effekt-Handler mit ihren regulären Konstruktoren. Terrain erzeugt dabei keine GPU-Textur. Die Welt schreibt keine globalen Map-Verzeichnisse. Projektilzustände und replizierte Flugbahnen bleiben aktiv; ihre Explosionen und andere visuelle Emissionen benötigen in diesem Modus keine Sprite-Assets.

Soldier, Medic, SquadLeader, Harvester und TiberiumRefinery unterstützen `loadModel: false`. Das überspringt Mesh-/Attachment-Laden, erhält aber IDs, Collections, HP, Bewegungsprofile, Waffenwerte, Occupancy, Produktion und Katalogmetadaten. Der Soldier erhält einen regulären AnimationPlayer mit leerer Clip-Sammlung. Präsentation und Import werden hier nicht getestet; meshabhängige Ausmaße/Pivots müssen bei entsprechenden Tests als ausdrückliche logische Fixture-Daten vorliegen.

Die bisherigen Konstruktoraufrufe laden weiterhin Modelle bzw. initialisieren Grafik. Es gibt keinen globalen Testmodus und kein GetUninitializedObject in diesem Konstruktionspfad.

## Welt und Dienste

Eine reguläre Welt besitzt `SimulationArmies` und einen explizit konfigurierbaren Netzwerk-/Allianzkontext. RTSGame verbindet diese mit seinen tatsächlichen Armies, seinem NetworkHandler und der bisherigen Player-Team-Regel. Registrierte Units sind an ihre Welt gebunden: Zielauflösung, Bewegung, Footprints und Pfadplanung verwenden diese Welt. Ein einzelnes Unit-Objekt darf nicht in zwei Welten registriert werden; die Prüfung erfolgt vor einer Mitgliedschaftsänderung. Separate Objekte dürfen in verschiedenen Welten dieselbe Netzwerk-ID tragen.

`NetworkInput(network, world, armies)` wendet Unit-, Ressourcen-, Ernte- und Projektilbefehle auf die übergebenen Instanzen an. Sein Dispose meldet den Nachrichtenempfänger ab. Der normale RTSGame-Aufbau verwendet diesen expliziten Konstruktor. Der kompatible Konstruktor ohne Welt bleibt für noch nicht migrierte Aufrufer erhalten.

NetworkHost erhält zusätzlich ArmyHandler, Player-/AI-Player-Anbieter und die Verlustmeldung als Abhängigkeiten. Der normale Spielaufbau übergibt diese direkt. HarvestSystem, MedicSystem und CombatSystem behalten ihre bestehenden expliziten Verträge für Welt, Veröffentlichung, Planung, Generation und Befehlsversion; es gibt keinen separaten Testablauf für ihre Fachlogik.

Die Checks-Assembly hat über InternalsVisibleTo Zugang zu vorhandenen hostautoritativen Mutationsmethoden. Kleine Fixture-Helfer registrieren reguläre Units über interne Mitgliedschaftsmethoden, setzen Army/Embark/Squad-Zustände über ihre Methoden und prüfen Host-Lifecycle-Grenzen direkt. Keine veränderbare Unit-Liste wird öffentlich angeboten. Registrierung allein ersetzt keine Grid-Platzierungsprüfung; Tests, die Kollisionen benötigen, müssen auch das GameGrid belegen.

## Migrierte Tests

`HarvestSystemChecks`, `MedicSystemChecks` und `CombatSystemChecks` verwenden weder Reflection noch uninitialisierte Objekte oder globale Weltumschaltung. Die bisherigen Tests für Stop, Tod, Entfernen, Ernten/Entladen, Heilintervalle, Projektilflug, Schaden, Replikation und Session-/Matchreset bleiben erhalten. Sanitäter-Squad-Wechsel und Embark verwenden reguläre Zustandsübergänge; bestätigte Bewegungsrouten werden über TryReceiveGotoCommand übernommen. Eine minimale reguläre Patient-Unterklasse ermöglicht das gezielte Umschalten des testbaren Todeszustands, ohne private Felder zu ändern.

`SimulationFixture` baut kleine flache Welten und normale Modell-freie Units auf. `SimulationIsolationChecks` betreibt zwei Welten gleichzeitig mit eigenen Netzwerkadaptern und eigenen Systemen, einschließlich identischer Unit-/Army-IDs. Die 18 neuen Checks prüfen:

- getrennte Heilimpulse, Cooldowns und Reset;
- getrennte Erntejobs, Cargo, Ressourcenfelder und Ressourcenreplikation;
- getrennten Schaden und eigene Zielauflösung trotz identischer IDs;
- unabhängige Army-Allianzen und bestätigte Bewegung;
- atomare Ablehnung einer Unit aus einer anderen Welt;
- Abmeldung des Replikationsadapters und unveränderte globale Welt-/Game-Referenzen.

Validierung: Debug-Build ohne Warnungen/Fehler; 928 Checks im Release-Lauf bestanden gegenüber 910 vor diesem Schritt.

```powershell
dotnet build RTS.csproj
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj -c Release
```

## Verbleibende Grenzen

Der gesamte grafische RTSGame-/HUD-/Content-Lebenszyklus wird nicht als Headless-Anwendung ersetzt. Weitere Netzwerkaktionen, Rendering, Editor- und Legacy-Tests enthalten weiterhin globale Zugriffe beziehungsweise Reflection; sie liegen außerhalb der drei hier migrierten Systemtests. Kompatible Fallbacks bleiben für ältere Aufrufer und noch uninitialisierte Legacy-Fixtures erhalten. Kein neuer Savegame-Vertrag, kein Hostwechsel und keine neue grafische Abnahme. Die optische Fahrzeugabnahme aus Architekturpunkt 02 bleibt offen.
