# Gemeinsamer Gameplay-Katalog und KI-Metadaten

Seit Architektur-Aufgabe 08 werden Katalog und Factory-Registrierungen beim ersten Katalogzugriff auf Konsistenz geprüft. Forschungs-Grants, Zeiten und Angebote stammen aus dem Katalog; gemeinsame Building-Regeln unterstützen mehrere Forschungsproduzenten. Unbekannte Produkte sind nicht verfügbare Quotes mit erklärendem Fehlergrund; kostenlose Sandbox-Typen sind ausdrücklich aufgelistet. Erweiterung und Prüfgrenzen stehen in [Katalog-Registrierung.md](Katalog-Registrierung.md).

## Ziel

Spieleroberfläche, Host und KI sollen dieselben statischen Spieldaten verwenden. Eine neue Einheit soll nicht an mehreren Stellen mit Preis, Bauzeit und Produktionsgebäude eingetragen werden müssen. Der gemeinsame Einstiegspunkt ist `GameplayCatalog` in `src/Gameplay/GameplayCatalog.cs`.

Der Katalog ist keine KI-Sonderlösung. Er beschreibt allgemein kaufbare Gebäude, Units und Forschungen. Die taktischen KI-Angaben sind ein zusätzlicher Teil einer Unit-Definition.

## Was in den Katalog gehört

- stabile `TypeId` und Anzeigename
- Grundpreis und benötigte Perks
- mögliche Produzenten und die jeweilige Produktionsdauer
- Darstellung der Produktionsaktion, beispielsweise die Position im Icon-Sheet
- statische taktische Eigenschaften für die KI

Ein Produkt besitzt eine Liste von `ProducerDefinition`. Dadurch kann dieselbe Unit später in mehreren Gebäuden oder von verschiedenen Fraktionen produziert werden. Auch unterschiedliche Produktionszeiten pro Gebäude sind möglich.

```csharp
Unit("gunner", "Gunner", 100,
    [
        Producer("gdi-barracks", 4),
        Producer("nod-barracks", 5)
    ],
    aiMetadata);
```

## Was nicht in den Katalog gehört

Veränderliche Werte bleiben an der Laufzeitinstanz oder im zuständigen Service:

- aktuelle Trefferpunkte, Munition und Position
- verfügbare Ressourcen und momentane Strombilanz
- Produktionsfortschritt
- army- oder gebäudespezifische Rabatte
- aktuelle Verfügbarkeit eines Bauplatzes

Der Katalog enthält den Grundpreis. `PricingService` erzeugt daraus ein `PurchaseQuote` für eine konkrete Army und einen konkreten Produzenten. UI, KI und Host verwenden dieses Quote.

## Aktueller Stand

Der erste Schritt zentralisiert:

- sämtliche bisher in `EconomyCatalog` gespeicherten Grundpreise und Perk-Voraussetzungen
- Produktionszeiten von Kaserne, Fahrzeugfabrik, Raffinerie und Helipad
- die Produktionsaktionen dieser Gebäude
- Produzentenbeziehungen für die vorhandenen produzierbaren Units
- erste Rollen- und Stärkewerte für die spätere KI-Auswahl
- Trefferpunkte und benötigte Baupunkte der konkreten Gebäude
- Stromproduktion und Stromverbrauch
- Lagerkapazität von Silo und Tiberium-Raffinerie

`EconomyCatalog` bleibt vorerst als kompatible Fassade bestehen. Seine Daten kommen bereits aus dem `GameplayCatalog`. Der Host prüft Produktionsanfragen über dieselbe Produzentenbeziehung und Bauzeit, aus der auch das ActionPanel seine Aktionen erhält.

`AIUnitSelector` bewertet die von einem konkreten Gebäude produzierbaren Units gegen ein `AIProductionNeed`. Der Fahrzeug-Controller beschreibt inzwischen nur noch seinen Bedarf an mobiler Bodenunterstützung. Er kennt die konkrete Klasse `Tank` nicht mehr. Ein neuer Fahrzeugtyp kann dadurch gewählt werden, wenn er:

- im Katalog die Fahrzeugfabrik als Produzenten nennt,
- als angreifende Bodeneinheit gekennzeichnet ist,
- und mit seinen Stärkewerten den höchsten Nutzen für den aktuellen Bedarf erreicht.

Die ausgewählten Fahrzeuge werden über ihre `GameplayTypeId` gezählt und vom Angriffstrupp als Eskorte verwendet. Damit endet die Katalogintegration nicht bei der Produktion; die produzierte Unit erhält auch einen passenden ersten Einsatz.

Die Squad-Vorbereitung verwendet ebenfalls keine fest eingetragenen Produkt-IDs mehr. Sie fordert nacheinander Rollen an:

- `Leader`
- `Healer`
- `Attacker + AntiInfantry`
- `Attacker + AntiVehicle`

Bereitschaftsprüfung, Ersatzproduktion, Rückzug und Erholung erkennen Squad-Mitglieder anhand derselben Metadaten. Die existierende Squad-Mechanik benötigt weiterhin eine `SquadLeader`-kompatible Laufzeitklasse und eine tatsächlich heilende Unit; die strategische Auswahl des konkreten Typs kommt jedoch aus dem Katalog.

Auch die drei ersten Basisverteidiger und die Auswahl des Scouts verwenden Rollen. Im AI-Ordner gibt es damit keine fest eingetragenen Produkt-IDs oder `OfType<Tank/Gunner/RakZero/Medic>`-Abfragen mehr. Konkrete Klassen bleiben nur dort relevant, wo sie eine echte Laufzeitmechanik darstellen, beispielsweise die Formation am `SquadLeader`.

## Bedrohungs- und Verlustanalyse

`AIThreatAssessment` beobachtet für jede KI-Armee ausschließlich derzeit sichtbare Gegner. Infanterie, Fahrzeuge, Luftziele und bewaffnete Gebäude erhöhen unterschiedliche Bedarfswerte. Eigene Verluste verstärken den Bedarf abhängig von ihrem Kontext:

- Basisverteidigung zählt besonders stark,
- Verluste im Ressourcenbetrieb ebenfalls stärker,
- offensive Verluste normal,
- verlorene Scouts etwas schwächer.

Die Verlustwirkung besitzt eine Halbwertszeit von 60 Sekunden. Beobachtungen werden geglättet. Die KI stellt ihre Produktion dadurch schrittweise um und reagiert nicht mit einem vollständigen Strategiewechsel auf eine einzelne Einheit.

Fahrzeug- und Kampfinfanterieproduktion erhalten ihre Gewichte aus diesem Bedrohungsprofil. Die Strategieprofile liefern weiterhin kleine Grundtendenzen. Der Konsolenstatus einer KI zeigt die momentanen Werte für Infanterie-, Fahrzeug- und Luftbedrohung an.

## Erster Abhängigkeitsplan: Luftverteidigung

`AIDefensePlanner` übersetzt einen ausreichend hohen Luftbedrohungswert in konkrete Bauschritte. Er sucht über `AIStrategicCatalog` ein erreichbares Gebäudeangebot mit `Defender + AntiAir` und stationärer Bewegungsdomäne. Derzeit findet er dadurch den Gatling-Turret.

Der Planer:

1. berechnet abhängig von der Bedrohung ein bis drei benötigte Verteidigungsanlagen,
2. prüft vorhandene und bereits im Bau befindliche Anlagen,
3. prüft Grundpreis, Perks und Ressourcenreserve,
4. berücksichtigt den Stromverbrauch des gewählten Gebäudes,
5. plant bei fehlender Leistungsreserve Stromcrew oder ein erreichbares Kraftwerk,
6. sucht anschließend einen gültigen Bauplatz nahe der Basis,
7. sendet den Bauauftrag über den normalen `PlayerCommandService` an den Host.

Adaptive Verteidigung und allgemeiner Infrastrukturaufbau teilen sich den Bulldozer geordnet. Während ein Gegenmaßnahmenplan aktiv ist, startet der Infrastruktur-Controller keinen konkurrierenden Bauauftrag.

## Allgemeiner Produktionsplaner

`AIProductionPlanner` löst die Abhängigkeiten eines gewünschten Katalogeintrags rekursiv auf. Das Ergebnis ist eine geordnete Liste aus `BuildBuilding`, `Research` und `TrainUnit`. Ein Controller kann zusätzlich einen konkreten `AssignCrew`-Schritt ergänzen.

Beispiele:

```text
Gatling-Turret bei zu wenig Strom
→ Reaktor bauen
→ Gatling-Turret bauen

Helipad ohne Air Technology
→ Air Technology in der Basis erforschen
→ Helipad bauen

Tank ohne Fahrzeugfabrik
→ Fahrzeugfabrik bauen
→ Tank produzieren
```

Der Planer erkennt unbekannte Produzenten, fehlende Forschungseinträge und zyklische Abhängigkeiten als nicht ausführbaren Plan. Mehrere nötige Kraftwerke können als wiederholte Schritte geplant werden. Gebäude nennen den Bulldozer als Produzenten; der Bulldozer selbst wird von der Basis produziert. Die Basisaktion und ihre Produktionsdauer kommen ebenfalls aus dem gemeinsamen Katalog.

`AIProductionPlanExecutor` führt diese Pläne über den normalen `PlayerCommandService` aus. Er kann Gebäude bauen, Units ausbilden, Forschung starten und eine konkrete Crew einem Gebäude zuweisen. Dabei wartet er auf Ressourcen und Produzenten, hält eine Ressourcenreserve von 800, beobachtet die Bestätigung des Hosts und fährt erst nach dem tatsächlichen Abschluss mit dem nächsten Schritt fort. Bleibt eine Hostbestätigung aus, wiederholt er die Anfrage nach einer kurzen Wartezeit.

Der Luftverteidigungs- und der Infrastruktur-Controller verwenden den Executor bereits für ihre vollständigen Abhängigkeitspläne. Ein Auftrag für einen Gatling-Turret kann damit beispielsweise zuerst einen Reaktor bauen und danach selbständig den Turret errichten. Der Infrastruktur-Controller verwendet denselben Ablauf für Silos, Reaktoren, Air Technology, Helipads sowie Ausbildung und Zuweisung eines Engineers. Andere KI-Controller können den Executor schrittweise übernehmen, ohne Forschung, Produktion und Bauablauf erneut zu implementieren.

## Engineer als Stromalternative

Die Reaktor-Definition enthält nun auch vier Crew-Plätze und `PowerProductionPerCrew = 30`. Der Engineer trägt die Rolle `Crew`. `Reaktor.EffectivePowerProduction` liest denselben Bonus aus dem Katalog, den die KI für ihre Planung verwendet.

`AICrewPowerPlanner` entscheidet bei zusätzlichem Strombedarf in dieser Reihenfolge:

1. Vorhandene freie Crew in einen passenden Stromproduzenten mit freiem Laufzeitplatz schicken.
2. Deckt dessen Katalogbonus das Defizit, Crew über ein verfügbares Produktionsangebot ausbilden.
3. Ansonsten ein erreichbares Kraftwerk planen. Für die heutigen Reaktoren entspricht ein Crew-Mitglied weiterhin 30 Strom.

Ein bereits laufender Eintritt oder Ausbildungsauftrag wird abgewartet. Das Betreten läuft über `PlayerCommandService.EnterUnitAsync` und damit über dieselbe Hostvalidierung wie bei einem menschlichen Spieler. Der Helipad-Infrastrukturplan und die adaptive Luftverteidigung verwenden beide diese Entscheidung.

## Vorgesehene nächste Schritte

1. Fahrzeug- und Infanterieproduktion schrittweise auf den gemeinsamen Executor umstellen.
2. Die getrennten Executor-Instanzen später durch eine priorisierte Planwarteschlange pro KI-Armee koordinieren.
3. Weitere statische Unit-Werte und allgemeine Aktionen schrittweise übernehmen.
4. Neue Produkte über die bereits validierten Factory-Erzeuger registrieren.

Die Host-Autorität bleibt unverändert: Der Katalog hilft bei Auswahl und Darstellung, aber der Host validiert und bezahlt jeden Auftrag weiterhin über die normalen Netzwerkbefehle.

## Strategische Auswahl ohne konkrete Gebäudetypen

Architektur-Aufgabe 09 ist umgesetzt: Bedürfnisse, mehrere Produzenten, Gebäude-Perkanbieter und Crew-Boni werden aus dem Katalog gewählt. Details, Erweiterungsregeln und geprüfte Grenzen in [Strategische-Katalogplanung.md](Strategische-Katalogplanung.md). Laufzeitverhalten wie Ernten, Squadführung oder Landen bleibt explizit implementiert.
