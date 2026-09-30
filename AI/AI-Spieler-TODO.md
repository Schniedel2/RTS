# TODO: AI-Spieler

Diese Liste wird in Reihenfolge abgearbeitet. Ein Punkt gilt erst als erledigt, wenn das beschriebene Verhalten implementiert und mit einem passenden Regressionstest oder einem reproduzierbaren Testspiel geprüft wurde.

Die Modellangaben beziehen sich auf Codex-Aufgaben. `gpt-6-luna` eignet sich für kleine, klar begrenzte Änderungen, `gpt-6-sol` für normale Implementierungs- und Refactoring-Aufgaben und `gpt-6-astra` für Architektur, schwierige Fehlersuche sowie systemweite Tests. Der angegebene Reasoning-Aufwand ist ein sinnvoller Startwert und kann bei unerwartet komplexem Code erhöht werden.

## Phase 1: Konkrete Fehler und Stillstände

- [x] **KI ohne Scout weiterlaufen lassen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `medium` Reasoning.
  - Ein fehlender oder zerstörter Scout darf nur das Scouting anhalten.
  - Wirtschaft, Infrastruktur, Verteidigung, Produktion und Squad-Aufbau laufen weiter.
  - Die KI fordert bei Bedarf selbst einen neuen Scout an.
  - Fertig, wenn eine KI nach dem Verlust aller Scouts weiterhin baut und produziert.

- [x] **Verteidigungseinheiten vollständig zurückschicken**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `medium` Reasoning.
  - Nach einem Verteidigungseinsatz erhalten auch Tanks und andere zugewiesene Unit-Typen einen Rückkehrbefehl.
  - Ursprüngliche Aufgabe beziehungsweise Sammelposition soll nach Möglichkeit wiederhergestellt werden.
  - Fertig, wenn keine Verteidigungseinheit nach Ende des Alarms am ehemaligen Ziel stehen bleibt.

- [ ] **Taktische KI während des Wiederaufbaus aktiv halten**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Der Verlust von Reaktor, Raffinerie oder Kaserne startet den Wiederaufbau, ohne Verteidigung, Rückzug und vorhandene Kampftruppen abzuschalten.
  - Fertig, wenn die KI während eines Infrastrukturverlusts weiterhin auf Angriffe reagiert.

- [ ] **Festgefahrene und unerreichbare AI-Aufträge erkennen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Jeder länger laufende Bau-, Produktions-, Bewegungs- und Angriffsauftrag erhält Fortschrittskontrolle und einen definierten Abbruch- oder Neuplanungsweg.
  - Ablehnungen des Hosts werden von bloßem Warten unterschieden.
  - Fertig, wenn ein absichtlich blockierter Auftrag die KI nicht dauerhaft anhält.

## Phase 2: Gemeinsame Planung und Ressourcen

- [ ] **Zentrale AI-Auftragswarteschlange einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Eine Instanz koordiniert Wirtschaft, Strom, Infrastruktur, Verteidigung, Einheitenproduktion und Forschung pro Army.
  - Aufträge besitzen Priorität, Status, reservierte Ressourcen und benötigte Produzenten beziehungsweise Arbeiter.
  - Dringende Aufträge dürfen weniger wichtige Pläne pausieren oder verdrängen.
  - Fertig, wenn nicht mehr mehrere Controller unabhängig um denselben Bulldozer oder dieselben Ressourcen konkurrieren.

- [ ] **Globale Ressourcenplanung verwenden**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Nur eine Stelle entscheidet über die gemeinsame Ressourcenreserve.
  - Bereits geplante Ausgaben und laufende Produktionsaufträge werden berücksichtigt.
  - Prioritätsreihenfolge zunächst: Existenzsicherung, Strom, Harvester/Wirtschaft, Verteidigung, Produktion, Forschung, Ausbau.
  - Fertig, wenn parallele Controller keine sich gegenseitig blockierenden Kaufversuche mehr erzeugen.

- [ ] **Produzenten und Arbeiter reservieren**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Bulldozer, Kaserne, Factory, Helipad und andere Produzenten können für einen AI-Auftrag reserviert werden.
  - Verteidigung darf Reservierungen nur bei ausreichend hoher Dringlichkeit aufheben.
  - Fertig, wenn ein Bulldozer nicht abwechselnd mehrere Baustellen anfährt.

- [ ] **Einheitliches Auftragsergebnis einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - AI-Aufträge unterscheiden mindestens `Requested`, `Accepted`, `Rejected`, `InProgress`, `Completed` und `Failed`.
  - Ablehnungsgründe wie Ressourcen, Bauplatz, Produzent, Perk und ungültiges Ziel werden gespeichert.
  - Die normalen Host-/Netzwerkbefehle bleiben der einzige Ausführungsweg.
  - Fertig, wenn Controller nicht mehr allein anhand kurzer Timeouts raten müssen, was passiert ist.

## Phase 3: Scouting und Kartenwissen

- [ ] **Scouting-Sektoren und Zielreservierungen einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Die Karte wird in Sektoren oder Frontier-Bereiche aufgeteilt.
  - Mehrere Scouts erhalten unterschiedliche reservierte Ziele.
  - Bereits erkundete oder reservierte Bereiche werden niedriger bewertet.
  - Fertig, wenn mehrere Scouts sichtbar in verschiedene Richtungen aufbrechen.

- [ ] **Erreichbarkeit von Scouting-Zielen prüfen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `medium` Reasoning.
  - Die Auswahl berücksichtigt Bewegungsprofil und zusammenhängende erreichbare Gebiete.
  - Fehlgeschlagene Ziele erhalten eine zeitlich begrenzte Sperre.
  - Fertig, wenn Scouts nicht wiederholt freie, aber unerreichbare Zellen auswählen.

- [ ] **Scouting-Suche ohne große Kandidatenlisten ausführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Keine vollständige neue `List<Point>` pro Scout und Entscheidung.
  - Frontier-Zellen oder Sektoren werden inkrementell verwaltet.
  - Fertig, wenn viele Scouts keine auffälligen CPU- oder Allokationsspitzen erzeugen.

- [ ] **Gegnerwissen mit Verfallszeit speichern**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Gesehene Unit-Rollen, ungefähre Mengen und letzte Positionen bleiben zeitlich begrenzt bekannt.
  - Das Vertrauen sinkt mit der Zeit.
  - Produktion reagiert nicht sofort auf das Verschwinden eines Gegners aus der aktuellen Sicht.
  - Fertig, wenn ein kurz verschwundener Heli nicht unmittelbar aus der Luftbedrohungsbewertung verschwindet.

## Phase 4: Verteidigung und Gefechtssteuerung

- [ ] **Benötigte Verteidigungsstärke berechnen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Die KI wählt nur so viele passende Verteidiger wie für die erkannte Bedrohung nötig.
  - Entfernung, Gesundheit, Zieltypen und Gegenwirkung fließen in die Auswahl ein.
  - Fertig, wenn ein einzelner Angreifer nicht mehr die komplette Armee von ihrer Aufgabe abzieht.

- [ ] **Unit-Aufgaben und Verfügbarkeit zentral kennzeichnen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Mindestens: Scout, Basisverteidiger, Squad-Mitglied, Eskorte, Reserve, Reparatur/Heilung und ungebunden.
  - Controller dürfen fremd gebundene Units nicht ohne Prioritätsentscheidung übernehmen.
  - Fertig, wenn Verteidigung und Angriff keine widersprüchlichen Befehle mehr an dieselbe Unit senden.

- [ ] **Mehrere Bedrohungen getrennt behandeln**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Angriffe auf verschiedene Seiten der Basis werden als getrennte Vorfälle bewertet.
  - Verteidiger werden räumlich passend verteilt.
  - Fertig, wenn zwei gleichzeitige Angriffe nicht als ein einzelnes Ziel behandelt werden.

- [ ] **Rückzug und Neuformierung verbessern**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Gesundheit, verlorene Rollen, Munition, Heli-Treibstoff und Entfernung zur Basis fließen ein.
  - Beschädigte Verbände ziehen sich koordiniert zurück und werden ergänzt.
  - Fertig, wenn ein Squad nach Verlusten wieder vollständig und kampfbereit ausrückt.

## Phase 5: Katalogbasierte und fraktionsfähige KI

- [ ] **Alten Economy-Aufbau auf den Produktionsplaner umstellen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Basis, Strom, Raffinerie, Harvester und Kaserne werden aus Katalogmetadaten geplant.
  - `ArmyGoalController` kennt keine konkreten GDI-Klassen mehr, soweit keine echte Laufzeitmechanik dies verlangt.
  - Fertig, wenn eine neue Fraktion ihren Startaufbau nur über Katalogdaten beschreiben kann.

- [ ] **Gegnerbeziehungen über Army-/Team-Service prüfen**
  - **Empfohlenes Modell:** `gpt-6-luna` mit `medium` Reasoning.
  - Keine Gegnerprüfung mehr über die zufällig erste eigene Unit.
  - Eine gemeinsame Funktion beantwortet die Beziehung zwischen zwei Armies.
  - Fertig, wenn die KI Gegner auch ohne aktuell vorhandene eigene Unit korrekt einordnen kann.

- [ ] **Restliche konkrete Unit-Klassen aus taktischer Auswahl entfernen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Basisverteidigung und andere Selektoren verwenden Rollen, Domain und Fähigkeiten aus dem Katalog.
  - Konkrete Klassen bleiben nur für einzigartige Laufzeitmechaniken erforderlich.
  - Fertig, wenn ein neuer katalogisierter Tank oder Soldat ohne neuen AI-Sonderfall eingesetzt wird.

## Phase 6: Basenbau und strategische Variation

- [ ] **Funktionale Basiszonen planen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Wirtschaft in Richtung Ressourcenfeld, Produktion mit freien Ausgängen, Verteidigung am Rand und Stromversorgung im geschützten Bereich.
  - Hauptwege und Harvester-Korridore werden dauerhaft reserviert.
  - Fertig, wenn Harvester und Bulldozer auch in einer großen AI-Basis verlässlich fahren können.

- [ ] **Bauplatzbewertung statt erster gültiger Zelle verwenden**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Kandidaten erhalten Punkte für Entfernung, Gelände, Verkehr, Verteidigung, Ressourcen und Erweiterbarkeit.
  - Fehlgeschlagene Bereiche werden vorübergehend gemieden.
  - Fertig, wenn die Basisstruktur nachvollziehbar und nicht nur ringförmig zufällig wirkt.

- [ ] **Strategieprofile um echte Entscheidungen erweitern**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Beispielsweise Frühangriff, defensive Expansion, Luftfokus, Fahrzeugfokus, zweite Raffinerie oder Harvester-Überfall.
  - Profile setzen Gewichtungen und Ziele statt ausschließlich feste Stückzahlen.
  - Fertig, wenn zwei Profile im gleichen Match sichtbar unterschiedliche Prioritäten verfolgen.

- [ ] **Strategiewechsel aus Verlusten und Gegnerwissen ableiten**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Die KI erkennt wiederkehrende Schwächen und passt Produktion und Einsatz an.
  - Kontext wie Basisverteidigung, Angriff und Ressourcenbetrieb bleibt erhalten.
  - Fertig, wenn unterschiedliche Gegnerzusammensetzungen zu unterschiedlichen Gegenmaßnahmen führen.

## Phase 7: Diagnose und automatisierte Tests

- [ ] **Strukturierte AI-Entscheidungshistorie einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `medium` Reasoning.
  - Pro Army werden die letzten Entscheidungen mit Zeit, Priorität, Ziel, Ergebnis und Ablehnungsgrund gespeichert.
  - Konsolenbefehle zeigen Status und Historie mehrzeilig an.
  - Fertig, wenn ein Stillstand ohne Debugger nachvollzogen werden kann.

- [ ] **Deterministische AI-Simulationstests aufbauen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Fester Seed, definierte Karte und reproduzierbare Anfangslage.
  - Tests für Scout-Verlust, Stromausfall, zerstörte Raffinerie, blockierten Bauplatz, Squad-Verluste und mehrere Bedrohungen.
  - Fertig, wenn zentrale Entscheidungen ohne grafische Spielinstanz geprüft werden.

- [ ] **Mehrminütigen AI-Soak-Test einbauen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Zwei oder mehr KIs spielen mit festem Seed gegeneinander.
  - Geprüft werden Fortschritt, ausstehende Aufträge, Pathfinding-Last, Ressourcenfluss und fehlende Endlosschleifen.
  - Fertig, wenn der Test wiederholbar läuft und bei Stillstand eine verwertbare Diagnose erzeugt.

- [ ] **AI-Leistungsbudget messen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Messwerte für Think-Zeit, Pfadsuchen, Bauplatzprüfungen, Zielsuche und erzeugte Netzwerkbefehle pro Army.
  - Fertig, wenn eine festgelegte Zahl von KIs ohne auffällige Frame-Spitzen simuliert werden kann.

## Empfohlene unmittelbare Reihenfolge

1. KI ohne Scout weiterlaufen lassen.
2. Tanks und andere Verteidiger korrekt zurückschicken.
3. Taktische Controller während des Wiederaufbaus aktiv halten.
4. Festgefahrene Aufträge diagnostizieren und abbrechen.
5. Zentrale AI-Auftragswarteschlange einführen.
6. Ressourcen und Produzenten zentral reservieren.
7. Scouting-Sektoren und erreichbare Ziele einführen.
8. Verteidigungsstärke und Unit-Aufgaben koordinieren.
9. Economy-Aufbau vollständig auf den Gameplay-Katalog umstellen.
10. Danach Basisplanung, Strategievariation und Soak-Tests ausbauen.
