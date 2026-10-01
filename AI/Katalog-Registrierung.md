# Katalog und Produktregistrierung

Stand: 01.10.2026, Architektur-Aufgabe 08.

## Zuständigkeiten und Erweiterung

GameplayCatalog besitzt Preise, Voraussetzungen, Produktionszeiten, Produzenten und GrantedPerk. UnitFactory und BuildingFactory besitzen explizite Registrierungen von Typ-ID auf Konstruktorfunktion. Die bisherigen Konstruktorparameter, Modellnamen und Alias-Erzeugungen bleiben erhalten. Neue Units/Buildings brauchen einen Katalogeintrag und eine Factory-Registrierung.

Beim ersten Katalogzugriff prüft Validate eindeutige normalisierte IDs, Preise und Voraussetzungen, Factories, bekannte Produzenten, endliche Produktionszeiten und Forschungs-Grants. Umgekehrt braucht jede Factory-Registrierung einen Katalogeintrag oder eine ausdrückliche Sandbox-Ausnahme. IDs sind über Produktarten hinweg eindeutig, weil Produktionsaufträge Produktnamen ohne Produktart speichern. Fehler nennen das betroffene Produkt bzw. den Produzenten.

Eine neue Forschung benötigt ausschließlich einen GameplayDefinition-Eintrag mit PurchasableType.Research, GrantedPerk und Produzenten samt Zeiten. ResearchProjects.TryGetGrantedPerk liest diesen Grant; AirTechnologyId bleibt als ID-Konstante erhalten. Die gemeinsamen Building-Regeln ergänzen Forschungsaktionen für die deklarierten Produzenten und unterstützen deren Forschungsdauer. Bestehende Perks und armeeweit laufende Projekte blenden das jeweilige Angebot aus. NetworkHost prüft dieselben Katalogdaten, bucht den Preis und beendet Forschung über ResearchCompletedCommand. NetworkInput liest ebenfalls den Katalog-Grant. Es gibt keine zusätzliche Zuordnung nach Forschungsnamen.

Die Produktionszeit gilt je Produzent. BuildingMetadata bleibt die Quelle für Baupunkte, HP, Strom- und Speicherdaten. PurchasePrice einer Instanz bleibt der tatsächlich bezahlte Preis einschließlich Rabatten als Grundlage von Verkauf und Bauabbruch.

## Unbekannte Produkte und Sandbox

EconomyCatalog.GetDefinition/GetBasePrice werfen für unbekannte Produkte eine ArgumentException mit Produktart und ID. TryGetDefinition dient der Eingangsprüfung. PricingService liefert einen nicht verfügbaren PurchaseQuote mit UnavailableReason; CanAfford ist ebenfalls false. Das ActionPanel deaktiviert das Angebot und zeigt den Fehlergrund. Unbekannte Factory-IDs liefern null vor Preisauflösung oder Konstruktion.

Explizit kostenlose Sandbox-Typen sind Units `editor` und `car` sowie GenericBuildings `building-1`, `building-4x3x4` und `antenna-1`. Sie bleiben erzeugbar, gehören aber nicht zu regulären Produktions-/KI-Katalogangeboten. Die Aliase `heli` und `soldier` behalten ihre bisherigen Factory-Konstruktoren; Preise/Angebote normalisieren sie zu `helicopter` und `gunner`. TiberiumSource steht ausdrücklich mit Preis 0 und ohne Produktionsangebote im Katalog.

## Prüfung und Grenzen

Build ohne Warnungen/Fehler; 834 Checks bestanden, davon 62 neue Katalogchecks: ungültige und doppelte Einträge, fehlende Registrierung, ungültige Produzenten/Zeiten/Grants, nicht verfügbare unbekannte Produkte, kostenlose Ausnahmen und tatsächliche Erzeugung aller Unit-/Building-Katalogtypen einschließlich zentraler Building-Preise.

Eine ausschließlich im Test ergänzte Radio-Forschung mit DetailedHealth-Grant und zwei Produzenten wird ohne neuen Research-Switch angeboten, vom echten Host bezahlt und über UpdateProduction fertiggestellt. Serialisierung und NetworkInput bestätigen denselben Grant. Armeeweite Queue-Sperre, Groß-/Kleinschreibung, Whitespace und bereits abgeschlossene Perks sind geprüft. Der Testeintrag wird anschließend entfernt; kein neues Gameplay-Upgrade wurde hinzugefügt.

Factory-Checks verwenden reguläre Instanzen mit einfachen Mesh-Fixtures ohne Grafikgerät. Keine grafische Content-Abnahme behauptet. Welt-/Host-Fixtures und temporäre Katalogerweiterung verwenden weiterhin Reflection; deren Bereinigung gehört zu Aufgabe 11. Konkrete strategische KI-Zieltypen bleiben Aufgabe 09.
