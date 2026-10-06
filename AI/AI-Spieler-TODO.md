# TODO: AI-Spieler

Diese Liste wird in Reihenfolge abgearbeitet. Ein Punkt gilt erst als erledigt, wenn das beschriebene Verhalten implementiert und mit einem passenden Regressionstest oder einem reproduzierbaren Testspiel geprüft wurde.

Die Modellangaben beziehen sich auf Codex-Aufgaben. `gpt-6-luna` eignet sich für kleine, klar begrenzte Änderungen, `gpt-6-sol` für normale Implementierungs- und Refactoring-Aufgaben und `gpt-6-astra` für Architektur, schwierige Fehlersuche sowie systemweite Tests. Der angegebene Reasoning-Aufwand ist ein sinnvoller Startwert und kann bei unerwartet komplexem Code erhöht werden.

## Aktuell priorisierter Umbau

Seit 06.10.2026 ist die Entkopplung für verteilte, konfigurierbare KI-Controller vom Benutzer als wichtigste nächste Arbeit priorisiert. Der schrittweise Plan steht in [KI-Client-TODO.md](KI-Client-TODO.md). Dort zuerst den nächsten offenen Punkt bearbeiten, wenn der Auftrag dieses Vorhaben betrifft; offene Aufgaben dieser Liste werden dadurch nicht automatisch erledigt.

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

- [x] **Taktische KI während des Wiederaufbaus aktiv halten**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Der Verlust von Reaktor, Raffinerie oder Kaserne startet den Wiederaufbau, ohne Verteidigung, Rückzug und vorhandene Kampftruppen abzuschalten.
  - Fertig, wenn die KI während eines Infrastrukturverlusts weiterhin auf Angriffe reagiert.
  - **Ergebnis (05.10.2026):** Die erste abgeschlossene Basisvorbereitung aktiviert die taktischen Controller bis zum nächsten Match-Neustart. Erneuter Kernaufbau beendet deren Updates nicht mehr: Bedrohungsbewertung, vorhandene Scouts, Basisverteidigung, laufende Squad-Missionen/Rückzüge und aktive Erholung bleiben erhalten. Neue optionale Ausbildung, Scout-Nachkauf und Ausbau warten während des Kernwiederaufbaus; bestätigte Host-Aufträge laufen weiter. Status zeigt Wiederaufbau und taktische Entscheidung gemeinsam. Strommangel wird weiterhin durch den Infrastruktur-Controller behandelt. Ablauf und Grenzen: [KI-Wiederaufbau-und-Taktik.md](KI-Wiederaufbau-und-Taktik.md).
  - **Prüfung:** Build ohne Warnungen/Fehler; alle 1.096 Checks bestanden, davon 39 neue in `tests/GridNavigationChecks/AIReconstructionChecks.cs`. Reaktor-/Raffinerie-/Kasernenverlust, Verteidigungsrequests samt echter Host-Anwendung und Rückkehr, laufender Bau, Ressourcenpriorität, Squad-Angriff/Rückzug/Erholung, Wiederaufnahme, Match-Neustart, Idle und Host-Grenze geprüft. Grafikfreie Szenarien; keine neue optische Schlachtabnahme behauptet.

- [x] **Festgefahrene und unerreichbare AI-Aufträge erkennen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Jeder länger laufende Bau-, Produktions-, Bewegungs- und Angriffsauftrag erhält Fortschrittskontrolle und einen definierten Abbruch- oder Neuplanungsweg.
  - Ablehnungen des Hosts werden von bloßem Warten unterschieden.
  - Fertig, wenn ein absichtlich blockierter Auftrag die KI nicht dauerhaft anhält.
  - **Ergebnis (05.10.2026):** Hostlokale Rückmeldungen unterscheiden ausstehende, bestätigte, abgelehnte und verworfene Requests; identische ausstehende KI-Käufe werden zusammengefasst. Eine Army-weite Fortschrittskontrolle sichert auch den bisherigen Economy-Aufbau ab. Bau, Produktion/Forschung, Bewegung/Einstieg, Ernte und Squad-Phasen besitzen Neuplanungs-/Abbruchwege und zeitlich begrenzte Sperren. Bezahlte Queues und echter langsamer Fortschritt bleiben erhalten. Details und Grenzen: [KI-Auftragsfortschritt-und-Wiederherstellung.md](KI-Auftragsfortschritt-und-Wiederherstellung.md).
  - **Prüfung:** Build ohne Warnungen/Fehler; alle 1.174 Checks bestanden, davon 51 neue in `tests/GridNavigationChecks/AIOrderProgressChecks.cs`. Echte Host-Anwendung/Ablehnung, blockierte Aufträge, verzögerte Planung ohne doppelte Käufe, Wiederaufnahme und Sessionwechsel geprüft. Grafikfreie Regressionen; der spätere mehrminütige Soak-Test bleibt offen.

## Phase 2: Gemeinsame Planung und Ressourcen

- [x] **Zentrale AI-Auftragswarteschlange einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `xhigh` Reasoning.
  - Eine Instanz koordiniert Wirtschaft, Strom, Infrastruktur, Verteidigung, Einheitenproduktion und Forschung pro Army.
  - Aufträge besitzen Priorität, Status, reservierte Ressourcen und benötigte Produzenten beziehungsweise Arbeiter.
  - Dringende Aufträge dürfen weniger wichtige Pläne pausieren oder verdrängen.
  - Fertig, wenn nicht mehr mehrere Controller unabhängig um denselben Bulldozer oder dieselben Ressourcen konkurrieren.

- [x] **Globale Ressourcenplanung verwenden**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Nur eine Stelle entscheidet über die gemeinsame Ressourcenreserve.
  - Bereits geplante Ausgaben und laufende Produktionsaufträge werden berücksichtigt.
  - Prioritätsreihenfolge zunächst: Existenzsicherung, Strom, Harvester/Wirtschaft, Verteidigung, Produktion, Forschung, Ausbau.
  - Fertig, wenn parallele Controller keine sich gegenseitig blockierenden Kaufversuche mehr erzeugen.
  - **Ergebnis (05.10.2026):** Ein `AIResourcePlanner` pro Army entscheidet zentral über die Reserve (800 für reguläre Produktion/Forschung/Ausbau; dringende Existenzsicherung, Strom, Wirtschaft und Verteidigung dürfen sie verwenden). Controller melden auch noch unbezahlbare Wünsche. Priorisierte Ersparnisse, ausstehende Host-Käufe, aktuelle Preise/Rabatte und bereits bezahlte Produktions-FIFOs werden gemeinsam berücksichtigt, ohne Doppelabbuchung. Wiederaufbau sperrt alte optionale Wünsche; eine vorhandene Ersatzbaustelle verdrängt ihren unbezahlten Neubauvorschlag. Budgetdiagnose im KI-Status. Details: [KI-Ressourcenplanung.md](KI-Ressourcenplanung.md).

- [x] **Produzenten und Arbeiter reservieren**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Bulldozer, Kaserne, Factory, Helipad und andere Produzenten können für einen AI-Auftrag reserviert werden.
  - Verteidigung darf Reservierungen nur bei ausreichend hoher Dringlichkeit aufheben.
  - Fertig, wenn ein Bulldozer nicht abwechselnd mehrere Baustellen anfährt.

- [x] **Einheitliches Auftragsergebnis einführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - AI-Aufträge unterscheiden mindestens `Requested`, `Accepted`, `Rejected`, `InProgress`, `Completed` und `Failed`.
  - Ablehnungsgründe wie Ressourcen, Bauplatz, Produzent, Perk und ungültiges Ziel werden gespeichert.
  - Die normalen Host-/Netzwerkbefehle bleiben der einzige Ausführungsweg.
  - Fertig, wenn Controller nicht mehr allein anhand kurzer Timeouts raten müssen, was passiert ist.

## Phase 3: Scouting und Kartenwissen

- [x] **Scouting-Sektoren und Zielreservierungen einführen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Die Karte wird in Sektoren oder Frontier-Bereiche aufgeteilt.
  - Mehrere Scouts erhalten unterschiedliche reservierte Ziele.
  - Bereits erkundete oder reservierte Bereiche werden niedriger bewertet.
  - Fertig, wenn mehrere Scouts sichtbar in verschiedene Richtungen aufbrechen.
  - **Ergebnis (05.10.2026):** Gemeinsame lokale Army-Zielvergabe in 16×16-GameGrid-Sektoren. Unbekannte Zellen und Richtungsverteilung bestimmen die Auswahl; reservierte Sektoren werden gemieden. Controller teilen ihre Reservierungen; Stop, Tod/Entfernen, Einsteigen, Army-Wechsel, Dispose, Ablauf und Session-/Zeitreset geben Ziele frei. Goto bleibt im normalen Netzwerkweg. Details und Grenzen: [KI-Scouting-Sektoren.md](KI-Scouting-Sektoren.md).

- [x] **Erreichbarkeit von Scouting-Zielen prüfen**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `medium` Reasoning.
  - Die Auswahl berücksichtigt Bewegungsprofil und zusammenhängende erreichbare Gebiete.
  - Fehlgeschlagene Ziele erhalten eine zeitlich begrenzte Sperre.
  - Fertig, wenn Scouts nicht wiederholt freie, aber unerreichbare Zellen auswählen.
  - **Ergebnis (05.10.2026):** Vor Goto wird die Verbindung mit dem normalen inkrementellen Pathfinder geprüft, einschließlich Bewegungsprofil, Footprint und Diagonalsperren. Fehlgeschlagene Sektoren werden pro Scout 30 Sekunden gemieden. Ablehnung und ausbleibende Bewegung führen zur Neuplanung. Stop, Entfernung, Sessionwechsel und verschobene Startposition verwerfen laufende Prüfungen. Helis verwenden weiterhin Flugnavigation. Details: [KI-Scouting-Erreichbarkeit.md](KI-Scouting-Erreichbarkeit.md).
  - **Prüfung:** Build ohne Warnungen/Fehler; alle 1.299 Checks bestanden, darunter acht neue grafikfreie Regressionen für verzögerte Befehlsausgabe, unerreichbare Ziele, Sektorsperren, Bewegungsprofil-Barrieren, Ablauf/Wiederaufnahme und Stop während der Suche.

- [x] **Scouting-Suche ohne große Kandidatenlisten ausführen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Keine vollständige neue `List<Point>` pro Scout und Entscheidung.
  - Frontier-Zellen oder Sektoren werden inkrementell verwaltet.
  - Fertig, wenn viele Scouts keine auffälligen CPU- oder Allokationsspitzen erzeugen.
  - **Ergebnis (06.10.2026):** Kandidatenauswahl und Pfadprüfung laufen als ein inkrementeller Planungsauftrag. Pro Scout bleiben nur wiederverwendbare Sektor-Zusammenfassungen mit Anzahl und Reservoir-Kandidat erhalten; Zelllisten, GroupBy, Sortierung und Gruppenarrays entfallen. Konkurrierende Reservierungen nutzen die vorhandenen Zusammenfassungen für Ersatzziele. Sichtabfragen vermeiden Enum-Boxing und Interface-Enumerator-Allokationen. Details: [KI-Scouting-Inkrementelle-Suche.md](KI-Scouting-Inkrementelle-Suche.md).
  - **Prüfung:** Build ohne Warnungen/Fehler; alle 1.318 Checks bestanden, davon zwölf neue für kurze Suchscheiben, Sektorspeicher, Reservierungskonflikte ohne erneuten Zellscan, wiederverwendete Daten, Allokationsgrenze, 16 gleichzeitige Scouts und Stop. Die Tests belegen Budget und Speicherverhalten; keine grafische FPS-/Soak-Abnahme behauptet.

- [x] **Gegnerwissen mit Verfallszeit speichern**
  - **Empfohlenes Modell:** `gpt-6-sol` mit `high` Reasoning.
  - Gesehene Unit-Rollen, ungefähre Mengen und letzte Positionen bleiben zeitlich begrenzt bekannt.
  - Das Vertrauen sinkt mit der Zeit.
  - Produktion reagiert nicht sofort auf das Verschwinden eines Gegners aus der aktuellen Sicht.
  - Fertig, wenn ein kurz verschwundener Heli nicht unmittelbar aus der Luftbedrohungsbewertung verschwindet.
  - **Ergebnis (06.10.2026):** Army-lokale Sichtungseinträge speichern Unit-ID, Typ/Rollen, Domain, Panzerung, Bewaffnung, letzte Position, Zeitpunkt und Kontext. Unbestätigtes Wissen hat 30 Sekunden Vertrauens-Halbwertszeit und verfällt nach 90 Sekunden. Die vorhandenen Produktionsbedarfe verwenden gewichtete Erinnerungen statt nur aktuell sichtbarer Gegner. Verdeckte Bewegung/Entfernung verändert keine Sichtung; erneute Sicht aktualisiert sie ohne Doppelzählung. Sichtbare Verluste, Bündnisse und Match-/Zeitreset räumen Einträge auf. Spectator-Anzeige beeinflusst das KI-Kartenwissen nicht. Details: [KI-Gegnerwissen-und-Verfall.md](KI-Gegnerwissen-und-Verfall.md).
  - **Prüfung:** Build ohne Warnungen/Fehler; alle 1.333 Checks bestanden, darunter 15 neue für unbekannte Gegner, Spectator, gespeicherte Rollen/Position/Kontext, verdeckte Luftbedrohung, Vertrauensabnahme, erneute Sicht, verdeckte Entfernung, Verfall, sichtbaren Verlust und Zeitreset.

## Phase 4: Verteidigung und Gefechtssteuerung

- [x] **Benötigte Verteidigungsstärke berechnen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Die KI wählt nur so viele passende Verteidiger wie für die erkannte Bedrohung nötig.
  - Entfernung, Gesundheit, Zieltypen und Gegenwirkung fließen in die Auswahl ein.
  - Fertig, wenn ein einzelner Angreifer nicht mehr die komplette Armee von ihrer Aufgabe abzieht.
  - **Ergebnis (06.10.2026):** Ein katalogunabhängiger Kampfkraft-Selektor schätzt den Bedarf des lokalen Vorfalls aus gegnerischen Trefferpunkten und Feuerleistung. Verteidiger werden nach wirksamem Schaden gegen Zielpanzerung, eigener Gesundheit, Entfernung/Reichweite und Gegenschaden priorisiert; unpassende Ziel-Domains bleiben ausgeschlossen. Die Auswahl endet bei ausreichender Stärke, bestehende Zuweisungen erhalten einen kleinen Stabilitätsbonus. Wachsende Bedrohung ruft Verstärkung, sinkende Bedrohung gibt überschüssige Units mit ihrer vorherigen Aufgabe frei. Normale Player-/Host-Requests bleiben der Ausführungsweg. Diagnose zeigt zugewiesene/benötigte Stärke. Details: [KI-Verteidigungsstaerke.md](KI-Verteidigungsstaerke.md).
  - **Prüfung:** Build ohne Warnungen/Fehler; alle 1.350 Checks bestanden, darunter 17 neue für proportionale Auswahl, Verstärkung, Gesundheit, Entfernung, stabile Sortierung, Panzer-/Luftgegenwirkung, fehlende Kräfte, echte Host-Anwendung und Rückgabe überschüssiger Verteidiger. Bestehende Wiederaufbauchecks prüfen jetzt die kleinere statt der vollständigen Verteidigergruppe.

- [x] **Unit-Aufgaben und Verfügbarkeit zentral kennzeichnen**
  - **Empfohlenes Modell:** `gpt-6-astra` mit `high` Reasoning.
  - Mindestens: Scout, Basisverteidiger, Squad-Mitglied, Eskorte, Reserve, Reparatur/Heilung und ungebunden.
  - Controller dürfen fremd gebundene Units nicht ohne Prioritätsentscheidung übernehmen.
  - Fertig, wenn Verteidigung und Angriff keine widersprüchlichen Befehle mehr an dieselbe Unit senden.
  - **Ergebnis (06.10.2026):** `GameWorld.UnitTasks` führt Zuständigkeit, Rolle, Priorität und Ablauf pro Unit zentral. Scout, Basisverteidigung, Squad/Eskorte und Erholung verwenden explizite Task-Agents zur Auswahl und zur Befehlsfreigabe in `PlayerCommandService`; normale Spielerbefehle bleiben unverändert. Höhere Priorität darf übernehmen, gleiche/niedrigere Priorität wird abgewiesen. Squad-Gruppen werden atomar beansprucht, verdrängte Zuordnungen bei Freigabe wiederhergestellt. Bau-/Produktionsreservierungen bleiben führend als Wirtschaftsaufgaben. Stop, Missionsende, Reset, Tod/Entfernen, Army-Wechsel und Ablauf räumen Zuständigkeiten auf. Details: [KI-Unit-Aufgaben-und-Verfuegbarkeit.md](KI-Unit-Aufgaben-und-Verfuegbarkeit.md).
  - **Prüfung:** Build ohne Warnungen/Fehler; alle 1.371 Checks bestanden, davon 21 neue für Prioritäten, Request-Ablehnung, Rückgabe, atomare Gruppen, Scout-Lebenszyklus, Erholungsschutz, Ablauf, Army-/Zeitreset und Entfernung. Bestehende Scouting-, Wiederaufbau-, Verteidigungs- und Squad-Regressionsprüfungen bestehen weiterhin.

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
