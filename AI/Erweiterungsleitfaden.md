# Erweiterungsleitfaden

Stand: 05.10.2026. Dieser Leitfaden beschreibt die tatsächlich vorhandenen Erweiterungspunkte. Der Gameplay-Katalog vereinheitlicht Produktdaten und generische Auswahl; er ersetzt keine Laufzeitlogik für neue Fähigkeiten.

## Datenfluss im Überblick

```text
UnitFactory / BuildingFactory ── Typ-ID → Konstruktor
GameplayCatalog ── Preis, Perks, Produzenten, Zeiten, Gebäude- und KI-Metadaten
        ├── ActionPanel / Produktionsaktionen
        ├── PricingService / Host-Kaufprüfung
        └── AIUnitSelector / AIStrategicCatalog / AIProductionPlanner

Spieler oder KI → PlayerCommandService → Request → NetworkHost
                                      → hostautoritatives Command → NetworkInput
                                      → OnHostAction / replizierter Zustand
```

Die Factories und der Gameplay-Katalog werden beim ersten Katalogzugriff gegenseitig validiert. Eine Katalogdefinition macht einen Typ aber nicht erzeugbar, und ein Factory-Eintrag allein macht ihn nicht zu einem kaufbaren oder für die KI sichtbaren Produkt.

## Neue produzierte Unit

1. Lege die Laufzeitklasse unter `src/WorldObjects/Units/` an. Setze eine stabile `GameplayTypeId`, Modell, Bewegungs-/Kampfwerte und die für Spieler verfügbaren `Actions`. Falls die Unit zusätzliche Zustände hat, lege fest, wer sie autoritativ ändert und wie Clients sie rekonstruieren.
2. Ergänze die Konstruktorfunktion in `src/WorldObjects/UnitFactory.cs`. Reguläre Units brauchen einen Eintrag in `GameplayCatalog`; nur ausdrücklich als Sandbox markierte Typen dürfen ohne Katalogangebot bleiben.
3. Ergänze in `src/Gameplay/GameplayCatalog.cs` eine `Unit(...)`-Definition: ID, Anzeigename, Grundpreis, jeden zulässigen Produzenten mit dessen Produktionszeit und `AIUnitMetadata`. Produzenten müssen selbst als Gebäude oder Unit im Katalog stehen. Mehrere Produzenten sind unterstützt und können verschiedene Zeiten haben.
4. Trage Rollen und Werte passend ein: `AIUnitRole`, `AIMovementDomain`, Stärken gegen Infanterie/Fahrzeuge/Gebäude/Luft, Verteidigung, Mobilität, Aufklärung und bevorzugte Höchstzahl. Diese Daten speisen Auswahl, Bedrohungs-/Rollenbewertung und generische Produktionsplanung.
5. Prüfe, ob eine Gebäudeaktion schon aus `GameplayCatalog.CreateProductionActions` entsteht. Für spezielle Ausfahrt, Einsteigen, Besatzung oder enthaltene Units braucht es weiterhin Laufzeitcode am Gebäude/System.

Konkretes Beispiel im aktuellen Stand: `motorbike` ist in `UnitFactory` registriert und im Katalog sowohl `vehicle-factory` als auch `gdi-barracks` mit je vier Sekunden Produktionszeit zugeordnet. Seine Rollen (Scout/Support), Bewegung (GroundVehicle), Mobilität und Aufklärungswert sind Katalogdaten. Dass das Motorrad Fahrer-Soldaten einbettet oder autonom kämpft, folgt daraus nicht; dafür wäre eigenständiger Runtime-/Rendercode nötig.

## Neues Gebäude

1. Erstelle die Building-Klasse unter `src/WorldObjects/Buildings/`, setze `GameplayTypeId` und registriere Konstruktion samt Modellargumenten in `src/WorldObjects/BuildingFactory.cs`.
2. Füge `Building(...)` in `GameplayCatalog` hinzu. `BuildingMetadata` enthält maximale HP, Baupunkte, Stromproduktion/-verbrauch, Speicherkapazität, Crewplätze, Strombonus je Crewmitglied und Sichtweite. Diese Werte werden teils in die Laufzeitfelder kopiert; Spezialverhalten bleibt in der Building-Klasse oder einem Spielsystem.
3. Deklariere Produzenten und Produktionszeiten. Bau-Angebote sind beim Bulldozer mit Zeit `0` eingetragen; Unit- und Forschungszeiten müssen größer als null sein.
4. Füge `AIUnitMetadata` nur dann hinzu, wenn das Gebäude als strategisches Angebot bzw. Verteidiger bewertet werden soll. Die vorhandenen generischen Kriterien in `AIStrategicCatalog` leiten Basis, Strom, Lager, Sicht, Wirtschaft, Infanterie- und Fahrzeugproduktion aus Metadaten ab.
5. Ergänze eigene Aktionen/Bedienung nur dort, wo das Gebäude vom gemeinsamen `Building`-Verhalten abweicht. Preise werden beim Erzeugen über `BuildingFactory` aus dem Katalog geholt; eine bezahlte Instanz behält ihren tatsächlichen Kaufpreis.

`Validate()` verlangt eindeutige kanonische IDs und gültige Produzenten. Unbekannte Kaufprodukte sind keine kostenlosen Angebote. Sandbox-Ausnahmen sind explizit in `UnitFactory.IsSandboxType` und `BuildingFactory.IsSandboxType` zu pflegen.

## Forschung und Perks

Eine neue Forschung kommt als `GameplayDefinition` mit `PurchasableType.Research`, stabiler Projekt-ID, Preis, Voraussetzungen, Produzenten samt Zeiten und `GrantedPerk` in den Gameplay-Katalog. Das Beispiel `air-technology` grantet `PerkType.AirTechnology`. `ResearchProjects.TryGetGrantedPerk` liest den Grant aus dem Katalog; keine zweite Zuordnung nach Forschungsnamen anlegen.

Ein neues Perk braucht zusätzlich einen Wert in `src/Perks.cs` (`PerkType`) sowie eine klare Quelle und Lebensdauer. Gebäude oder Units können `IPerkProvider` implementieren und `PerkGrant` mit `Permanent`, `WhileProviderExists` oder `WhileProviderOperational` zurückgeben. `PerkScope` unterstützt globalen oder Radius-Effekt. Dauerhafte Forschungs-Perks werden als eigene Army-Quelle gespeichert; temporäre Provider werden aus dem Zustand ihrer Quelle abgeleitet. Der Katalog allein implementiert den konkreten Effekt nicht: dafür ist der zuständige Verbraucher (z.B. UI, Pricing, Sicht oder Fähigkeit) anzupassen.

`ProvidedPerks` beschreibt Gebäudefähigkeiten, die der Produktionsplaner als Abhängigkeit verwenden kann, etwa Basis/Startort. Für einen neuen Perk ist zu prüfen, ob `AIProductionPlanner` dessen Anbieter rekursiv findet und ob der konkrete Verbraucher die Wirkung auswertet. Perk-Namen in `AIStrategicCatalog` oder anderen Stellen können derzeit noch explizite Sonderregeln benötigen.

## Neue UnitAction oder Fähigkeit

Die Definition der Action liegt bei der Unit/Building (`Actions` bzw. `WithSellAction`/gemeinsame Building-Aktionen). Ein neuer Action-Typ wird in `src/UI/UnitAction.cs` als `UnitActionType` ergänzt. Kontextparameter gehören in das bestehende `UnitActionContext`-Objekt; die Aktion läuft über `PlayerCommandService`, `UnitActionRequest`, `NetworkHost.TryCreateUnitActionCommand` und das bestätigte `UnitActionCommand` zu `NetworkInput`, wo `OnHostAction` aufgerufen wird.

Der Host prüft beim generischen UnitAction-Weg Senderkontrolle, Ziel-IDs und gültige Kontextwerte, aber aktuell nicht, ob die Unit diese Action in ihrer UI-Liste anbietet. Das ist eine bewusste Client-UI-Zuständigkeit. Neue Gameplay-Wirkungen müssen trotzdem hostautoritativ umgesetzt werden; Clientanwendung darf keinen zweiten Effekt auslösen. Wenn der Zustand längerfristig sichtbar oder für Late Join nötig ist, muss er in die Unit-/Building-Zustandsserialisierung und gegebenenfalls den Session-Snapshot.

Ein rein clientlokales UI-Verhalten braucht keinen Netzwerktyp. Eine neue hostseitige Simulation mit eigenem fortlaufendem Zustand kann ein dediziertes System wie `HarvestSystem`, `MedicSystem` oder `CombatSystem` brauchen; dort Zustandsbesitz, Abbruch, Neustart und Snapshotbedarf explizit festlegen. Nur die kleinen Action-Requests laufen als generische UnitAction. Goto, Build und Harvest haben eigene typisierte Payloads und sind kein Muster, das man für jede kleine Action kopieren sollte.

## KI-Angebot und KI-Verhalten

Damit die generischen KI-Auswahlen eine neue Unit berücksichtigen, braucht sie einen Katalogeintrag mit sinnvollen Rollen-/Stärkewerten und einem Produzenten. `AIUnitSelector` bewertet Kandidaten, `AIStrategicCatalog` prüft Produzenten/Planbarkeit und `AIProductionPlanner` löst Gebäude-, Forschungs-, Strom- und Unit-Abhängigkeiten auf. Der `AIProductionPlanExecutor` führt die Schritte über denselben `PlayerCommandService` aus, den auch menschliche Spieler nutzen. Keine Preis-, Bauzeit- oder Perkregeln im AI-Controller duplizieren.

Metadaten reichen für generische Auswahl und Produktion, aber nicht für eine neue Strategie oder Fähigkeit. Beispielsweise benötigt eine neue Kampfrolle noch passende Ziele/Controller oder generische Verhaltensunterstützung; eine neue Gebäude-Sonderaktion benötigt eine Planungsentscheidung im zuständigen Controller. Die existierenden spezialisierten Verhaltensweisen (Ernten, Squadführung, Heilung, Besatzung, Landen) haben bewusst Runtime-Systeme. Diese Grenze ist eine offene Erweiterungsstelle, kein versteckter Katalogschalter.

## Zuständigkeiten und Änderungs-Checkliste

| Änderung | Hauptdateien / Besitzer |
|---|---|
| Unit oder Building erzeugbar machen | `src/WorldObjects/UnitFactory.cs`, `src/WorldObjects/BuildingFactory.cs`, Laufzeitklasse |
| Produkt, Kosten, Produzenten, Produktionszeit, Voraussetzung | `src/Gameplay/GameplayCatalog.cs` |
| Gebäudewerte für Energie, Lager, Crew, Sicht | `GameplayCatalog.cs`, jeweilige `Building`-Laufzeitklasse |
| Forschung/Perk | `GameplayCatalog.cs`, `src/Perks.cs`, Verbraucher/Provider |
| Spieleraktion/Parameter | `src/UI/UnitAction.cs`, jeweilige `Actions`, `UnitActionContext`, `OnHostAction` |
| Neue dauerhafte Netzwerkdaten | zuständiger Zustandstyp, `NetworkInput`, Snapshot-/Session-Code, Protokollversion falls Vertrag geändert |
| Generische KI-Produktauswahl | `AIUnitMetadata`, `AIUnitSelector`, `AIStrategicCatalog`, `AIProductionPlanner` |
| Neue KI-Strategie oder kontinuierliches Verhalten | passender AI-Controller bzw. hostseitiges Spielsystem; Commands weiterhin über `PlayerCommandService` |

Bei jeder Erweiterung prüfen: Ist die ID kanonisch und eindeutig? Erzeugt die Factory den Typ? Ist Preis/Bauzeit/Perk nur einmal definiert? Funktionieren UI, Host-Quote und KI-Planung mit demselben Eintrag? Ist eine Fähigkeit hostautoritativ und im Multiplayer sichtbar? Braucht Late Join den Zustand? Erzeugt die KI nur dann ein Angebot, wenn Laufzeitverhalten dafür vorhanden ist? Dokumentiere bewusst verbleibende Sonderfälle in der betroffenen Architekturdatei.

## Geprüfte Grenzen

- `GameplayCatalog.Validate()` prüft Katalog/Factory-Konsistenz, Produzenten, Preise, Zeiten, IDs sowie Forschungs-Grants und Gebäudefähigkeiten. Es validiert nicht, dass jede taktische Metadatenkombination spielerisch sinnvoll ist.
- Factory-Registrierungen sind explizite Wörterbücher; eine neue Laufzeitklasse registriert sich nicht automatisch.
- Produktionsaktionen sind für mehrere Produzenten kataloggeneriert. Andere Actions und Aktions-Verfügbarkeit sind nicht kataloggesteuert.
- AI-Rollen, Stärken und Gebäudemerkmale unterstützen vorhandene generische Selektoren. Neue strategische Reaktionen oder Fähigkeiten erfordern eventuell weiterhin Code.
- Hostvalidierung des generischen `UnitActionRequest` prüft Kontrolle und Payloadform, nicht die Action-Liste des Ziels. Spezielle Berechtigungsregeln müssen im passenden Hostpfad ergänzt werden.
- Snapshot/Late Join ist für serialisierte Laufzeit- und Army-Daten vorgesehen, nicht für flüchtige AI-Entscheidungen oder laufende hostinterne Sucharbeit.
- Dieses Dokument ist ein Erweiterungsleitfaden, keine Bestätigung grafischer Assets oder Balancewerte. Für Gameplay-Änderungen gelten die passenden Build-, Check- und manuellen Spieltests.
