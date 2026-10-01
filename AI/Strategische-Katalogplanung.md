# Strategische KI-Auswahl aus dem Gameplay-Katalog

Stand: 01.10.2026, Architektur-TODO 09.

## Zuständigkeiten

`AIStrategicCatalog` übersetzt Bedürfnisse in durchführbare Katalogangebote. `AIProductionPlanner` löst Voraussetzungen auf; `AIProductionPlanExecutor` führt die Schritte über `PlayerCommandService` aus. Der Host prüft und bezahlt weiterhin jeden Auftrag. Keine Hintergrundthread-Weltzugriffe oder neue Netzwerkzustände.

| Bedarf | Katalogdaten |
|---|---|
| Basis | `ProvidedPerks` enthält `BaseEstablished` |
| Strom | `PowerProduction > PowerConsumption` |
| Speicher | `ResourceCapacity > 0` |
| Wirtschaft | Speicher und Produktionsangebot mit Rolle `Harvester` |
| Aufklärung | `VisionRange > 0` |
| Infanterieproduktion | Angebot mit `Defender` und Bewegungsdomäne `Infantry` |
| Fahrzeugproduktion | Angebot mit `Attacker` und `GroundVehicle` |
| Bauarbeiter | Rolle `Builder`, tatsächlich positive `BuildRate`, betriebsbereite Besatzung |
| Luftunterstützung | Rolle `Attacker`, Bewegungsdomäne `Air` |
| Stromcrew | Rolle `Crew`, freie passende Laufzeitplätze, `PowerProductionPerCrew` |

Strom, Speicher und Sichtweite bestimmen die fachliche Rangfolge; anschließend entscheiden die aktuellen Kosten des Abhängigkeitsplans und eine stabile TypeId-Reihenfolge. Unit-Auswahl verwendet die bisherigen Rollengewichte und gegebenenfalls Bestandsgrenzen. Quotes kommen aus demselben `PricingService` wie beim Spieler; Produzentenrabatte fließen bei vorhandenen Instanzen ein. Fehlendes Geld führt zum Warten, nicht zur Erfindung eines kostenlosen Angebots. Der Executor behält seine Reserve von 800 Ressourcen.

## Voraussetzungen und mehrere Produzenten

Der Planer bevorzugt vorhandene Produzenten, danach kürzere Produktionszeiten und stabile IDs. Scheitert ein Produzent, versucht er die weiteren Einträge. Jede Alternative verwendet einen eigenen Planungszweig: gescheiterte Forschung, Gebäude oder Stromschritte gelangen nicht in das Ergebnis. Zyklische oder unerreichbare Voraussetzungen ergeben einen Fehler.

Erforderliche Perks können über `GrantedPerk` einer Forschung oder `ProvidedPerks` eines Gebäudes erreicht werden. Fehlender Strom wird durch erreichbare Kraftwerke aufgelöst, mit begrenzten Wiederholungen. Die Basis kann ohne Stromreserve gebaut werden, damit der Aufbau nicht an der Voraussetzung für sein eigenes Kraftwerk scheitert.

`ProvidedPerks` beschreibt die bei Betriebsbereitschaft zugesagten globalen Fähigkeiten für die Planung. Es erzeugt kein neues Perk-Verhalten automatisch: ein Gebäude muss die tatsächlichen Grants über `IPerkProvider` liefern. `GDIBase` liest Home/BaseEstablished aus dieser Liste; ihr stromabhängiger Minimap-Grant bleibt Laufzeitlogik. Bedingte lokale Sicht-/Crew-/Stromperks gehören nicht als bedingungslose Bauvoraussetzung in diese Liste.

Im ursprünglichen Basis-Aufbau werden unmittelbar verfügbare Gebäudeangebote und passende vorhandene Bauarbeiter gewählt. Infrastruktur und adaptive Verteidigung verwenden vollständige Abhängigkeitspläne. Der Bauplatz bleibt Sache der vorhandenen Platzierungsprüfung; Metadaten garantieren keinen verfügbaren Bauplatz.

Der Executor kann einen anderen fertiggestellten Produzenten desselben Produkts nutzen, wenn der geplante nicht verfügbar ist. Bereits bestellte Produkte werden bevorzugt und abgewartet, auch ohne Geld für einen weiteren Kauf. `Building.CanProduceUnit` ist die gemeinsame Produktionszulässigkeit für Host und KI; Helipads ergänzen dort ihre Liefer-/Landeplatzreservierung. Angebote und Produktionszeiten stammen weiterhin aus dem Katalog.

Eine automatisch gelieferte erste Lufteinheit beendet das Luftziel, bevor eine zusätzliche bezahlte Bestellung ausgeführt wird. Stromcrew wird auch vor einer für den Luft-/Speicher-/Sichtaufbau geplanten Kraftwerksbestellung als Alternative geprüft.

## Betroffene KI-Abläufe

Basisaufbau und Wiederaufbau, Ersatzbauarbeiter/Ernter, Infrastruktur, Stromcrew, Fahrzeugproduktion, Squad-Produktion, Heimkehranker und Gebäudeprioritäten verwenden Fähigkeiten/Angebote statt GDI-, Silo-, Helipad- oder Reaktor-Klassen zur strategischen Auswahl. Statusnamen der Infrastruktur sind entsprechend allgemein.

Ernten und Squad-Mechanik behalten ihre spezialisierten Laufzeittypen (`Harvester`, `Soldier`, `SquadLeader`). Fliegen, Landen, Besatzung und kostenlose Lieferungen bleiben implementiertes Unit-/Gebäudeverhalten. Ein beliebiger `MobileUnit` wird durch ein Air-Label allein nicht flugfähig; ein Harvester-Angebot braucht eine kompatible Ernteimplementierung. Bestehende Strategieprofile, Zielbestände und Ressourcenreserven bleiben erhalten. Eine allgemeine Fraktionsverwaltung oder eine gemeinsame priorisierte Planwarteschlange ist nicht Teil dieser Änderung.

## Neue Inhalte ergänzen

1. Laufzeitklasse und Factory-Erzeuger registrieren.
2. Katalogeintrag mit Preis, Voraussetzungen, Produzenten und Fähigkeiten hinzufügen; alle zulässigen Produzenten samt Zeiten eintragen.
3. Laufzeitverhalten, Slots und gegebenenfalls Perk-Grants entsprechend implementieren. Metadaten müssen das tatsächlich implementierte Verhalten beschreiben.
4. Keine neue strategische Typabfrage hinzufügen, solange die vorhandenen Fähigkeiten den Bedarf abbilden. Ein neues Verhalten benötigt weiterhin eine ausdrückliche Implementierung.

## Validierung und Grenzen

`tests/GridNavigationChecks/AIStrategicCatalogChecks.cs` ergänzt 32 Checks mit regulären kleinen Test-Units, temporären Katalogeinträgen und Factory-Registrierungen: alternative Basis, Kraftwerk, Speicher, Bauarbeiter, Crew, Luftprodukt, Forschung und mehrere Produzenten. Geprüft werden echte Host-Produktionsannahme/Preis/Zeiten, volle Warteschlangen, Perks, unerreichbare Alternativen ohne Planreste, Bootstrap, Crew-Bonus, fremde/embarkte Einheiten und die Vermeidung doppelter Luftbestellungen.

Gesamter ausführbarer Check-Harness: 866 Checks bestanden. Debug-Build ohne Warnungen/Fehler. Keine neue grafische KI-Schlacht abgenommen. Welt-/Dienste-Fixtures verwenden weiter Reflection; das bleibt Architektur-TODO 11. Snapshot-Kopien und wiederholte Katalogauswertungen werden in Aufgabe 10 getrennt behandelt.

## Start-Besatzung: Regressionkorrektur

Die neue Betriebsbereitschaftsprüfung machte einen Fehler im Matchstart sichtbar: Start-Bulldozer hatten keinen Fahrer. Der Host vergibt jetzt zusätzlich zur BulldozerId eine eindeutige DriverUnitId in jedem MatchStartAssignment; der Client-/Host-Anwendungsweg übergibt sie an UnitHandler.SpawnUnit. Fahrer und Besatzungsstatus sind so auf allen Peers identisch. Die Prüfung bleibt erhalten, damit tatsächlich unbemannte Bauarbeiter nicht als verfügbar gelten.

Fünf zusätzliche Checks decken ID-Vergabe/JSON, tatsächlichen Start-Bulldozer und Fahrer, strategische Auswahl sowie den ersten vom ArmyGoalController gesendeten Basis-Bauauftrag ab. Gesamtstand nach Korrektur: 871 Checks bestanden, Build ohne Warnungen/Fehler. Der ursprüngliche Checkaufbau mit einfachen Testbauarbeitern hatte diese echte Besatzungsabhängigkeit nicht abgedeckt.
