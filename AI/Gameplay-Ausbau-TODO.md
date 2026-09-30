# TODO: Gameplay-Ausbau, Fraktionen und strategische Vielfalt

Diese Roadmap zielt auf abwechslungsreiche Multiplayer-Partien mit einer überschaubaren Zahl klar unterscheidbarer Einheiten. Ein Punkt gilt erst als erledigt, wenn Gameplay, Hostvalidierung, Netzwerkzustand, UI, AI-Metadaten und ein reproduzierbarer Test abgedeckt sind.

Neue Einheiten und Gebäude werden zuerst im `GameplayCatalog` beschrieben. Spieler und AI verwenden dieselben Produktions-, Preis-, Perk- und Rolleninformationen. Neue Sonderlogik soll nur dort entstehen, wo sich das Verhalten tatsächlich von bestehenden Rollen unterscheidet.

Die Modellangaben beziehen sich auf Codex-Aufgaben. `gpt-6-sol` eignet sich für begrenzte Einheiten-, Gebäude- und UI-Arbeiten. `gpt-6-astra` empfiehlt sich für neue systemweite Mechaniken, Fraktionsarchitektur, Flugbewegung und größere Netzwerkänderungen.

## Phase 1: Fehlende Gegenrollen schließen

- [ ] **Mobiles Luftabwehrfahrzeug einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Rolle: `Defender`, `AntiAir`, `Support`, `GroundVehicle`.
  - Stark gegen Helicopter und spätere Flugzeuge, schwach gegen Panzer und Gebäude.
  - Zielauswahl, Projektil beziehungsweise Rakete und passende Produktionsmetadaten ergänzen.
  - AI erkennt Luftbedrohungen und hält mobile Luftabwehr bei wichtigen Verbänden oder der Basis.
  - Fertig, wenn Helicopter ohne Begleitschutz wirksam abgewehrt werden, das Fahrzeug aber keine universelle Kampfeinheit ist.

- [ ] **APC mit Soldatentransport einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Transportiert einen kleinen Squad oder mehrere einzelne Soldaten.
  - Einsteigen, Aussteigen, Zerstörung mit Insassen, Zielauswahl und Netzwerkzustand festlegen.
  - Squad Leader, Engineer und Medic können gemeinsam transportiert werden.
  - AI nutzt den APC für längere Wege und geschützte Annäherungen.
  - Fertig, wenn ein kompletter kleiner Squad synchron einsteigen, fahren und an einem erreichbaren Punkt aussteigen kann.

- [ ] **Artilleriefahrzeug einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Große Reichweite, Mindestreichweite und geringe Nahkampfstärke.
  - Schießen nur im Stand; Auf- und Abbauzeit bei Bedarf vorbereiten.
  - Aufklärung durch verbündete Units oder Datenverbindung berücksichtigen.
  - AI schützt Artillerie und wählt bevorzugt Gebäude oder stationäre Verteidigung als Ziel.
  - Fertig, wenn Artillerie eine befestigte Stellung aufbrechen kann, aber durch schnelle Angreifer zuverlässig konterbar bleibt.

- [ ] **Reparaturrolle einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Zunächst zwischen Repair Depot und Reparaturfahrzeug entscheiden; nur eine Variante zuerst umsetzen.
  - Reparatur benötigt Zeit und bei Bedarf Ressourcen oder Strom.
  - Reparatur wird im Kampf unterbrochen oder deutlich verlangsamt.
  - AI führt beschädigte wertvolle Fahrzeuge zur Reparatur zurück.
  - Fertig, wenn Reparatur eine strategische Investition ist und keinen unendlichen Kampf-Heilkreislauf erzeugt.

## Phase 2: Forschung und Entscheidungen statt Einheitenmenge

- [ ] **Technology Center einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Eigenes Gebäude mit Strombedarf, Kaufpreis und Forschungswarteschlange.
  - Fortgeschrittene Forschung aus der Basis in das Technology Center verschieben, soweit sinnvoll.
  - Zerstörung deaktiviert gebäudegebundene Vorteile entsprechend der Perk-Herkunft.
  - Fertig, wenn Bau, Forschung, Stromausfall, Verkauf und Zerstörung synchron funktionieren.

- [ ] **Doktrin-System mit exklusiver Auswahl einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Zunächst drei Doktrinen: `Armored Doctrine`, `Air Superiority` und `Infantry Doctrine`.
  - Eine Army kann nur eine dieser Doktrinen gleichzeitig aktivieren.
  - Doktrinen verändern Gewichtungen, Freischaltungen und wenige gut sichtbare Werte.
  - Host validiert Exklusivität; Snapshot und Late Join übertragen die Entscheidung.
  - AI wählt eine Doktrin passend zu Strategieprofil, Karte und erkannten Gegnern.
  - Fertig, wenn zwei ansonsten gleiche Armies nach ihrer Wahl sichtbar unterschiedliche Produktions- und Einsatzmöglichkeiten besitzen.

- [ ] **Erste doktrinabhängige Upgrades umsetzen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Armored: beispielsweise günstigere Reparatur oder robustere Fahrzeuge.
  - Air: beispielsweise kürzere Aufmunitionierung oder bessere Luftaufklärung.
  - Infantry: beispielsweise bessere Squad-Boni oder Medic-Wirkung.
  - Preisänderungen laufen ausschließlich über die zentrale Preislogik.
  - Fertig, wenn UI und AI Verfügbarkeit, Preis und Wirkung aus denselben Katalogdaten ableiten.

- [ ] **Weitere kleine Forschungsentscheidungen katalogisieren**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `medium` Reasoning.
  - Kandidaten: Advanced Optics, Target Data Link, Reactive Armor, Field Repairs, Tiberium Processing und Efficient Reactors.
  - Nur Forschungen mit klarer Gegenwirkung oder erkennbarer strategischer Funktion aufnehmen.
  - Keine Forschung darf lediglich eine unsichtbare Sammlung kleiner Prozentboni sein.
  - Fertig, wenn für jede Forschung Nutzen, Gegenmaßnahme, Quelle und Verlustbedingung dokumentiert sind.

## Phase 3: Aufklärung, Täuschung und Sabotage

- [ ] **Spion- beziehungsweise Saboteur-Unit einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Kann ausgewählte gegnerische Gebäude betreten oder aus kurzer Distanz hacken.
  - Erste Fähigkeiten begrenzen auf eine kleine Auswahl: Produktion ausspähen, Strom kurz deaktivieren oder Minimap-Daten stehlen.
  - Sichtbarkeit, Entdeckung, Abbruch und Gefangennahme beziehungsweise Tod werden hostseitig entschieden.
  - AI kennt wertvolle Sabotageziele und schützt wichtige eigene Gebäude.
  - Fertig, wenn eine erfolgreiche Infiltration einen deutlichen, zeitlich begrenzten Informations- oder Infrastrukturvorteil erzeugt.

- [ ] **Sensor- und Jammer-System einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Communication Tower und neue Jammer-Fähigkeiten verwenden lokale Wirkbereiche.
  - Sensoren können versteckte Einheiten erkennen oder genauere Informationen liefern.
  - Jammer können Minimap-Kontakte ausblenden, verzögern oder später falsche Kontakte erzeugen.
  - Wirkung auf direkte Weltsicht und Minimap wird ausdrücklich getrennt.
  - Fertig, wenn Sensor und Jammer im Multiplayer deterministisch dieselben Informationen anzeigen.

- [ ] **Target Data Link für indirektes Feuer einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Artillerie kann Ziele verwenden, die durch verbündete Sensoren oder Units aktuell aufgeklärt sind.
  - Verlust von Sicht oder Datenverbindung beeinflusst Genauigkeit oder weitere Schüsse.
  - Geteilte Ally-Sicht und eigene Sicht bleiben unterscheidbar.
  - Fertig, wenn ein Scout ein Artillerieziel freigeben kann, ohne dass die Artillerie selbst Sichtkontakt hat.

## Phase 4: Basisgestaltung und Kartenkontrolle

- [ ] **Defensive Hindernisse einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Zunächst Mauer, Tor und Panzersperre; Minenfelder erst anschließend bewerten.
  - Footprint, Clearance, Pathfinding und Move-Away-Preview berücksichtigen die Hindernisse.
  - Tore kennen Besitzer, Verbündete und Öffnungszustand.
  - AI lässt Hauptwege und Harvester-Korridore frei und baut Hindernisse nur an sinnvollen Engstellen.
  - Fertig, wenn Verteidigungsanlagen Wege verändern, ohne eigene Wirtschaft dauerhaft einzuschließen.

- [ ] **Neutrale strategische Gebäude einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Beispiele: Radarstation, Reparaturstation, Tiberium-Verarbeitung oder Beobachtungsposten.
  - Kontrolle erfolgt durch Besetzung, Hacken oder Aufenthalt in einem Eroberungsbereich.
  - Vorteile gelten nur solange die Kontrolle besteht.
  - Marker und Map-Publish übertragen Position und Typ.
  - Fertig, wenn neutrale Ziele Spieler zu Konflikten außerhalb ihrer Basen bewegen.

- [ ] **Alternative Multiplayer-Ziele vorbereiten**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Neben Vernichtung zunächst einen kontrollbasierten Spielmodus definieren.
  - Siegfortschritt, Unterbrechung, Teams und Late Join werden hostseitig verwaltet.
  - GameplayMarker beschreiben relevante Kartenziele.
  - Fertig, wenn eine Partie ohne vollständige Vernichtung entschieden werden kann.

## Phase 5: Zweite Fraktion

- [ ] **Fraktionsdefinition in den Gameplay-Katalog aufnehmen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Eine Fraktion definiert Start-Units, Bauoptionen, Produzenten, Forschungen, UI-Darstellung und AI-Strategiegewichtungen.
  - Rollen bleiben fraktionsübergreifend vergleichbar; konkrete Typ-IDs dürfen unterschiedlich sein.
  - Keine zentrale Gameplay- oder AI-Logik setzt mehr automatisch GDI-Typen voraus.
  - Fertig, wenn eine minimale Testfraktion ausschließlich über Katalogdaten starten und ihre Grundwirtschaft aufbauen kann.

- [ ] **Zweite Fraktion als mobiles/verdecktes Gegenmodell entwerfen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Schwerpunkt: schnelle leichte Fahrzeuge, Tarnung, Sabotage, Täuschung und flexible Basen.
  - Für Builder, Ressourcenabbau, Infanterie, Panzerabwehr, Luftabwehr und Aufklärung jeweils eine Lösung definieren.
  - Die Fraktion muss nicht dieselbe Anzahl oder dieselben Produzenten wie GDI besitzen.
  - Vor der Implementierung Rollenmatrix, Stärken, Schwächen und Gegenmaßnahmen dokumentieren.
  - Fertig, wenn jede Kernrolle vorhanden ist und mindestens drei Mechaniken deutlich anders funktionieren.

- [ ] **Grundwirtschaft der zweiten Fraktion implementieren**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Startzustand, Baukette, Strom beziehungsweise alternatives Versorgungssystem und Ressourcentransport implementieren.
  - Player-UI und AI verwenden Katalogdaten statt Fraktions-Sonderfälle.
  - Fertig, wenn ein menschlicher Spieler und eine AI mit der zweiten Fraktion Ressourcen gewinnen und eine Basis aufbauen können.

- [ ] **Kernarmee der zweiten Fraktion implementieren**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Nur die notwendigen Rollen für ein vollständiges Match umsetzen.
  - Einheiten sollen bestehende Combat-, Damage-, Squad- und Netzwerkmechaniken wiederverwenden.
  - Fertig, wenn Fraktion eins gegen Fraktion zwei ohne Platzhalter eine vollständige Partie spielen kann.

- [ ] **Fraktionsübergreifende Balance- und AI-Checks ergänzen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Feste Seeds, gespiegelte Startpositionen und vertauschte Fraktionen verwenden.
  - Wirtschaftsentwicklung, Armee-Wert, Schadensarten, Siegzeit und AI-Stillstände erfassen.
  - Fertig, wenn keine Fraktion allein durch fehlende Gegenrollen oder fehlerhafte AI dauerhaft dominiert.

## Phase 6: Airfield und Flugzeuge

- [ ] **Airfield und Landebahn-Konzept implementieren**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Das Airfield besitzt einen langen, beim Preview sichtbaren Landebahn-Korridor.
  - Platzierung prüft Fläche, Höhenunterschiede, Hindernisse und Einheiten.
  - Die Landebahn ist eine identifizierbare Gameplay-Zone und nicht nur zufällig vorhandenes Concrete.
  - Zustände mindestens: Preparing, Ready, ReservedForTakeoff, ReservedForLanding, Blocked und Damaged.
  - Fertig, wenn Start und Landung dieselbe Bahn konfliktfrei reservieren können.

- [ ] **Landebahn automatisch durch Bulldozer vorbereiten**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Das vorhandene Einebnungs- und Concrete-System wird wiederverwendet.
  - Der Airfield-Bau erzeugt automatisch passende Earthwork-Aufträge für den Bulldozer.
  - Zielhöhe, zulässige Erdbewegung und Abbruchbedingungen werden vor Baubeginn berechnet.
  - Der Spieler muss die Bahn nicht Zelle für Zelle planieren.
  - Fertig, wenn ein Bulldozer eine geeignete unebene Fläche automatisch vorbereitet und eine ungeeignete Fläche verständlich abgelehnt wird.

- [ ] **Gemeinsame Flugzeugbewegung entwickeln**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Flugzeuge müssen vorwärts fliegen, besitzen Mindestgeschwindigkeit und großen Wendekreis.
  - Start, Steigflug, Anflug, Landung, Durchstarten und Warteschleife werden als klare Zustände modelliert.
  - Treibstoff, Munition und beschädigte Landebahn werden berücksichtigt.
  - Host bleibt für Flugzustand und Treffer autoritativ; Clients interpolieren die Darstellung.
  - Fertig, wenn ein Flugzeug zuverlässig startet, ein Zielgebiet erreicht und auf seiner reservierten Bahn landet.

- [ ] **Interceptor als erstes Flugzeug einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Schnelle Luft-Luft-Einheit gegen Helicopter und spätere Bomber.
  - Begrenzte Raketen, Treibstoff und Aufmunitionierung am Airfield.
  - Schwach beziehungsweise wirkungslos gegen Gebäude und schwere Bodenziele.
  - Fertig, wenn Luftüberlegenheit einen klaren Nutzen und mobile/stationäre Luftabwehr eine wirksame Gegenmaßnahme besitzt.

- [ ] **Strike Aircraft als Sortie einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Der Spieler bestimmt Ziel oder Zielgebiet; Anflug, Angriff und Rückkehr laufen autonom.
  - Wirksam gegen ausgewählte Bodenrollen oder Gebäude, aber verwundbar gegen Interceptor und Luftabwehr.
  - Produktionswarteschlange zeigt Bau, Bewaffnung und Verfügbarkeit verständlich an.
  - Fertig, wenn der Einsatz wenig Micromanagement benötigt und dennoch durch Aufklärung sowie Luftabwehr beeinflusst wird.

- [ ] **Airfield und Flugzeuge in die AI integrieren**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - AI bewertet benötigte Landebahnfläche, Kosten, Strom und gegnerische Luftabwehr.
  - Sorties wählen Ziele aus aktuellem und gespeichertem Gegnerwissen.
  - Beschädigte oder blockierte Airfields lösen Reparatur, Ausweichlandung oder Produktionsstopp aus.
  - Fertig, wenn die AI Luftwaffe gezielt einsetzt und bei starker Gegenwehr ihre Strategie anpasst.

## Phase 7: Variation und Langzeittests

- [ ] **Kartenabhängige strategische Faktoren katalogisieren**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Ressourcenverteilung, Engstellen, neutrale Ziele, Airfield-Flächen und Sichtlinien erfassen.
  - Daten stehen Spieler-Preview und AI zur Verfügung.
  - Fertig, wenn die AI auf zwei verschieden aufgebauten Karten nachweislich andere Ausbauprioritäten wählt.

- [ ] **Match-Regeln und optionale Varianten einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Beispiele: knappe Ressourcen, schnelles Tiberiumwachstum, eingeschränkte Technologie oder zufällige neutrale Ziele.
  - Regeln werden beim Spielstart festgelegt und an alle Clients übertragen.
  - Fertig, wenn dieselbe Karte mit unterschiedlichen Regeln sichtbar andere Entscheidungen verlangt.

- [ ] **Multiplayer-Soak-Test mit Fraktionen und Doktrinen einbauen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Mehrere AI-Spieler verwenden unterschiedliche Fraktionen, Doktrinen und Strategieprofile.
  - Ressourcen, Strom, Produktion, Netzwerkzustand, Pathfinding und Entscheidungsstillstände werden überwacht.
  - Fertig, wenn wiederholte längere Partien reproduzierbare Diagnosen liefern und keine Kombination systematisch hängen bleibt.

## Empfohlene unmittelbare Reihenfolge

1. Mobiles Luftabwehrfahrzeug.
2. APC.
3. Technology Center.
4. Doktrin-System und erste drei Doktrinen.
5. Artillerie und Target Data Link.
6. Reparaturrolle.
7. Spion/Saboteur sowie Sensor/Jammer.
8. Fraktionsdefinition im Gameplay-Katalog.
9. Zweite Fraktion mit Grundwirtschaft und Kernarmee.
10. Neutrale Ziele und alternative Siegbedingung.
11. Airfield mit automatisch planierter Landebahn.
12. Interceptor und Strike Aircraft.
13. Karten- und Matchvarianten sowie Soak-Tests.
