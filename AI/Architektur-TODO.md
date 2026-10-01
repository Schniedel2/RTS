# Architektur-TODO

Stand: 30.09.2026. Grundlage: Quelltextanalyse von Vereinheitlichung, Erweiterbarkeit und Zuständigkeiten. Laufzeitvermutungen müssen gemessen werden.

## Arbeitsweise

Auftrag: „Arbeite den nächsten offenen Punkt aus AI/Architektur-TODO.md vollständig ab.“

Je Auftrag genau eine nummerierte Aufgabe bearbeiten. Aktuellen Code und lokale Änderungen zuerst prüfen; die Analyse kann durch andere Arbeiten überholt sein. Reihenfolge und Abhängigkeiten beachten. Keine parallelen Umbauten derselben Systeme. Vorhandene Fahrzeugbewegung-TODO und AI-Spieler-TODO abgleichen, damit Änderungen nicht doppelt umgesetzt werden.

Wenn eine bereits implementierte Aufgabe ausschließlich auf eine technisch blockierte manuelle Abnahme wartet, bleibt diese offen dokumentiert. Der nächste allgemeine Auftrag kann die nächste unabhängig umsetzbare Aufgabe bearbeiten; die fehlende Abnahme gilt dadurch nicht als bestanden. Konkret wartet 02 auf Sichtprüfung, während 03 und anschließend 04 nur von den jeweils angegebenen Voraussetzungen abhängen.

Erledigt bedeutet: implementiert, passende Verhaltenstests bestanden, Dokumentation aktualisiert und Ergebnis samt verbleibenden Einschränkungen hier vermerkt. Bei Gameplay-Änderungen `dotnet build RTS.csproj` und danach `dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj` ausführen. Beide Befehle nicht gleichzeitig gegen dieselben Build-Ausgaben starten. Erforderliche manuelle Prüfungen ausdrücklich offen lassen, wenn sie nicht erfolgt sind.

Host-Autorität, geordnete Befehle, Netzwerkweg für Mensch und KI sowie Mutationen auf dem Spielthread erhalten. Keine komplette Engine-/ECS-Migration und kein pauschaler Umbau aller Globals.

## Modellempfehlungen

Die Zuordnung pro Aufgabe ist unsere Einschätzung nach Änderungsrisiko, keine Erfolgsgarantie oder Benchmark-Aussage. Verwendet werden die in dieser Codex-Sitzung angebotenen Modelle: GPT-6 Astra (`gpt-6-astra`), GPT-6 Sol (`gpt-6-sol`) und GPT-6 Luna (`gpt-6-luna`). Astra für stark vernetzte Änderungen, Sol für begrenzte Refactorings, Luna für abschließende Dokumentation. Reasoning jeweils ausdrücklich angegeben. Das Modell vor dem Auftrag in Codex auswählen; die TODO wechselt es nicht automatisch.

Offizielle Orientierung: [OpenAI-Modellleitfaden](https://developers.openai.com/api/docs/guides/latest-model). Empfehlungen sind Stand dieser Liste; spätere Modellverfügbarkeit kann abweichen.

## Aufgaben

### 01 – Befehlsreihenfolge bei aufgeschobenen Goto-Requests sichern

- [x] Erledigt. Priorität: kritisch. Modell: **GPT-6 Astra, high**. Keine Voraussetzung.
- Problem: NetworkHost reiht zusätzliche Goto-Requests hinten ein und verarbeitet nachfolgende Stop-/Angriffsbefehle vorher.
- Aufschieben so ändern, dass FIFO-Semantik erhalten bleibt. Ein späterer Stop darf nicht durch einen älteren Goto aufgehoben werden. Keine unbemerkte Umordnung von Shift-Aufträgen.
- Fertig wenn Tests mit ausgeschöpftem Budget `Goto A → Goto B → Stop`, Goto/Attack sowie angehängte Routen und mehrere Spieler abdecken. Änderung in Netzwerk-Architektur.md dokumentieren.
- Ergebnis: Host-Scheduler prüft den Queue-Kopf vor Entnahme und beendet bei ausgeschöpftem Goto-Budget die Request-Verarbeitung dieses Updates. Keine Wiedereinreihung; globale FIFO-Reihenfolge und originale Payloads bleiben erhalten. Acht neue Checks prüfen Stop, Attack, mehrere Sender, Shift-/Routenpayloads und beide Budgets. Build ohne Warnungen/Fehler; alle 540 Checks bestanden. Keine grafische Prüfung erforderlich für diese reine Queue-Änderung. Einschränkung: Nachfolgende Befehle warten bei Rückstau ebenfalls; einzelne Gruppen-Gotos bleiben synchron (Aufgabe 04).

### 02 – Gemeinsamen Fahrcontroller für Bodenfahrzeuge einführen

- [ ] Implementiert und automatisiert geprüft; optische Abnahme noch offen. Priorität: hoch. Modell: **GPT-6 Astra, xhigh**. Nach 01.
- MobileUnit und Car auf doppelte Lenk-, Rückwärts-, Geschwindigkeits- und Blockadebehandlung prüfen. Einen gemeinsamen Ablauf mit GroundSteeringProfile schaffen; separaten Car-Rangierpfad ablösen.
- CanTurnInPlace=false konsistent durchsetzen. Verhindern, dass Blockaden, Richtungswechsel oder Beschleunigung versteckte Standdrehungen erzeugen. Fahrzeugunterschiede über Profile ausdrücken.
- Fertig wenn Jeep, Harvester, Tank und Bulldozer kurze/lange Gegenrichtungsziele, Kurven, Gebäudeecken, Stop und blockierte Manöver korrekt behandeln. Mehrere Zeitschritte und Host-/Client-Zustandsübernahme testen; optisches Fahrverhalten separat prüfen. Fahrzeugbewegung-TODO abgleichen.
- Umsetzung: Car-Sonderpfad samt privatem Rangierzustand entfernt. MobileUnit.DriveGroundRoute steuert gemeinsame Vorwärts-/Rückwärtsfahrt, Beschleunigung, Endbremsung und Blockadebehandlung. Ohne Standdrehfähigkeit ist die Drehrate geschwindigkeitsabhängig; blockierte Translation nimmt die Drehung zurück. Richtungswechsel drehen nicht während der Wartephase. Kurzstrecken-Rückwärtsfahrt verwendet denselben Ablauf. Bestehende Produktionsausfahrt bleibt ein eigener Lebenszyklus und wurde nicht verändert.
- Validierung: Build ohne Warnungen/Fehler, 613 Checks bestanden. Neue profilbasierte Tests für Jeep/Harvester/Tank/Bulldozer prüfen kurze/lange Gegenrichtungsziele bei 1/60, 0,1 und 0,25 Sekunden, Stop und Zustandswiederherstellung. Bestehende Gebäudeecken-, Blockade- und verbundene Client-Tests bestehen weiterhin. Profile werden im neuen Test mit einer grafikfreien Car-Instanz geprüft, nicht mit geladenen Fahrzeugmodellen.
- Noch erforderlich: Optische Abnahme mit echten Modellen: alle vier Fahrzeugtypen an Gebäudeecken, bei kurzen/schrägen Rückwärtszielen und langen Kehrtwenden beobachten, einschließlich Host/Client. In dieser Sitzung keine native Spielbedienung verfügbar; kein visueller Test behauptet. Erst danach vollständig abhaken. Beim nächsten Auftrag zu diesem Punkt die Implementierung nicht erneut beginnen, sondern diese Abnahme fortsetzen.
- Nachprüfung nach Spieltest: Radfahrzeuge können bei einem weit entfernten Ziel zunächst zurücksetzen, wenn ihr Vorwärtsbogen direkt an einem Hindernis scheitert. Die Freiraumdistanz berücksichtigt Wendekreis und Footprint, bleibt begrenzt und wird im MobileUnitState repliziert. Ein Float-Rest nahe null wird explizit beendet, damit das Fahrzeug nicht dauerhaft im Rücksetzmodus bleibt. Neuer Reaktorecken-Test besteht; insgesamt 615 Checks.
- Abnahmestand 01.10.2026: Der Nutzer hat das Rücksetzen und anschließende Wenden des Jeep am Hindernis im Spiel ausdrücklich positiv bestätigt. Dieser konkrete Fall ist optisch abgenommen; daraus folgt keine pauschale Abnahme aller Fahrzeuge oder des Multiplayer-Verhaltens.
- Technischer Blocker der weiteren Sichtprüfung: RTS wurde über Computer Use gestartet und als Fenster erkannt. Die Aufnahme scheiterte zuerst mit `FrameArrived timed out`, nach erneutem Ermitteln/Aktivieren des Fensters mit `window capture timed out`. Daher keine weiteren visuellen Ergebnisse. Implementierung nicht erneut umbauen und Punkt nicht ohne Nachweis abhaken.
- Restliche manuelle Abnahme (vorhandene Szene `Content/Console/Batches/autorun.batch`): Jeep, Harvester, Tank und Bulldozer jeweils einzeln auf ein kurzes gerades sowie schräges Rückwärtsziel und danach auf ein langes Gegenrichtungsziel schicken. An beiden Reaktoren enge Kurven und blockierte Wendemanöver prüfen; während eines Manövers Stop geben. Erwartung: Ziel erreicht oder bei tatsächlich fehlendem Freiraum nachvollziehbar angehalten, kein Dauerkreisen/Retry ohne Fortschritt; Jeep und Harvester drehen nicht ohne Translation, Stop beendet das Manöver. Dieselben Manöver mit verbundenem Client beobachten: keine abweichende Fahrtrichtung, Sprünge oder dauerhaft abweichende Endposition. Ergebnisse je Fahrzeug und Host/Client festhalten; erst danach 02 abhaken.

### 03 – Reproduzierbare Performance-Messung für KI und Befehle schaffen

- [x] Erledigt am 01.10.2026. Priorität: hoch. Modell: **GPT-6 Sol, high**. Nach 01.
- Vorhandene Telemetrie um aussagekräftige Messbereiche für KI-Controller, Bauplatzsuche, Host-Aufträge und Pfadsuchen ergänzen. Aufrufzahlen, Dauer und Allokationen gezielt erfassen; kein Log-Spam pro Frame.
- Reproduzierbares Szenario mit zwei KI-Armeen, Gruppenbefehlen und unerreichbaren Zielen dokumentieren. Messung muss auch teure Arbeiten innerhalb eines einzelnen Requests sichtbar machen.
- Fertig wenn ein Baseline-Bericht mit Umgebung und Szenario vorliegt; unbelegte Ursachen bleiben als Hypothesen gekennzeichnet.
- Ergebnis: optionale inklusive Zeit-/Allokationsmessungen pro KI-Controller, Bauplatzsuche, Host-Request-Typ, Gruppen-Goto-Planung und tatsächlicher Pfadsuche. Aufrufzahl, Gesamt-/Mittel-/Maximalzeit sowie Gesamt-/Maximalallokationen; keine Frame-Logs. Konsolenbefehle `telemetry-start`, `telemetry-stop`, `telemetry-reset`, `telemetry-save` unterstützen Tab und Berichtexport. Bedienung, Grenzen und Reproduktion in `Performance-Messung.md`, gemessene Release-Baseline in `Performance-Baseline.md`.
- Validierung: Debug- und Release-Build ohne Warnungen/Fehler; 619 Checks bestanden, darunter ausgeschaltete Messung, verschachtelte Scopes, Exception-Abschluss, Allokationen und Reset während eines Scope. Baseline: zwei aktive KI-Armeen, acht mobile Units je Armee, erreichbare und unerreichbare Gruppenziele, Bauplatzsuche. Zwölf Gruppenaufträge erzeugen 624 Suchen; unerreichbare Gruppenaufträge bis rund 172 ms, insgesamt rund 789 MB Suchallokationen. Damit wird teure Arbeit innerhalb eines einzelnen Requests sichtbar.
- Grenzen: grafikfreie Welt und einfache Footprints, KI-Zustand „auf Basis-Ressourcen warten“, direkte Host-Planung ohne Transport/Rendering. Keine Behauptung einer vollständigen KI-Schlachtmessung; tatsächliche Controller-Kosten im ausgebauten Spiel müssen über die neuen Messbereiche erfasst werden. Zusammenhang mit den gemeldeten mehrsekündigen Pausen bleibt eine Hypothese. Aufgabe 04 kann jetzt anhand derselben Baseline bearbeitet werden; optische Abnahme von 02 bleibt offen.

### 04 – Pfadsuche mit fortsetzbarem Arbeitsbudget ausführen

- [x] Erledigt am 01.10.2026. Priorität: hoch. Modell: **GPT-6 Astra, xhigh**. Nach 01 und 03.
- Ein Goto pro Frame begrenzt nicht die Kosten eines Gruppen-Goto oder einer einzelnen A*-Suche. Teure Planung in fortsetzbare Arbeitsschritte zerlegen und ein gemeinsames Budget definieren.
- Neue Befehle/Stop, tote Units, Sessionwechsel und geänderte Hindernisse müssen veraltete Arbeit sicher entwerten. Reihenfolge und vollständige hostbestätigte Routen erhalten. Weltzugriffe nicht unkontrolliert auf Hintergrundthreads verlagern.
- Fertig wenn Gruppen- und Unerreichbarkeitsszenarien ein belegtes begrenztes Arbeitsvolumen pro Update haben, Aufträge nicht verhungern und kein veraltetes Ergebnis einen neueren Befehl überschreibt. Vorher/nachher messen.
- Umsetzung: Gemeinsamer PlanningScheduler auf dem Spielthread mit maximal 2.048 Schritten/Update, Zeitprüfung bei 2 ms und Round-Robin-Quantum von 16 Schritten. A*/Dijkstra einschließlich Rekonstruktion und Routenprüfung fortsetzbar. Alle bisherigen Laufzeit-Pfadaufrufer umgestellt, einschließlich Goto/Move Away, Bewegungswiederholung, Bauanfahrten, Harvester, Sanitäter und ältere Erdarbeit. Synchrone Methoden dienen nur noch Checks/Diagnose. Host hält eine FIFO-Grenze bis zum vollständigen Gruppenresultat; Versions-/Request-ID-Prüfungen verhindern späte Ergebnisse nach Ersatzbefehlen/Stop. Grid-Revisionsprüfung, begrenzte Neustarts und Lifecycle-Reset behandeln Weltänderungen, Tod/Entfernung und Session-/Matchwechsel.
- Validierung: 647 Checks bestanden; darunter neue Budget-, Fortschritts-, Abbruch-, Lifecycle- und tatsächliche Host-/Wire-Checks. Debug-/Release-Build ohne Warnungen/Fehler. Vergleichsszenario unverändert zwölf Gruppenaufträge mit 624 Suchen: zuvor bis rund 172 ms synchron pro Auftrag; danach maximal 2.048 Schritte und gemessene 2,001 ms pro Planungsupdate bei 543 Updates. Vorher-Bericht erhalten, Nachher-Bericht `Performance-Incremental.md`; Ablauf und Grenzen in `Fortsetzbare-Pfadplanung.md`.
- Grenzen: Zeitlimit wird zwischen Schritten geprüft und ist keine harte Echtzeitgarantie; GC/Einzelprüfungen können es überschreiten. Die gesamte Sucharbeit und rund 792 MB kumulative Allokationen bleiben im Szenario bestehen. Globale Request-FIFO kann Befehle hinter einer langen Planung verzögern; unabhängige Suchjobs werden fair fortgesetzt. Keine vollständige grafische KI-Schlacht behauptet; optische Fahrzeugabnahme von 02 bleibt offen. Nächster umsetzbarer Architekturpunkt ist 05.

### 05 – Ernteablauf aus NetworkHost in ein HarvestSystem verlagern

- [x] Erledigt am 01.10.2026. Priorität: hoch. Modell: **GPT-6 Astra, high**. Nach 01 und 04.
- HarvestJob und die Übergänge Fahren/Ernten/Rückkehr/Entladen in ein eigenes hostseitiges System verschieben. NetworkHost bleibt für Request-Annahme und Replikation zuständig.
- Zustandsbesitz, Abbruch und Lebenszyklus festlegen; keine zweite unabhängige Zustandsmaschine in Harvester erzeugen. Replikations- und Late-Join-Bedarf ausdrücklich unterscheiden.
- Fertig wenn Vollbeladung, fehlende Ressourcen, unerreichbares/zerstörtes Silo, Stop und Spielneustart geprüft sind und die bisherige Spielfunktion erhalten bleibt.
- Ergebnis: HarvestSystem besitzt sämtliche Erntejobs, Phasen, Kandidatenauswahl, Retry- und Entladezustände. NetworkHost ordnet Requests, integriert die gemeinsame fortsetzbare Routenplanung und repliziert Ergebnisse. Explizite Abhängigkeiten für Welt, Armies, Veröffentlichung, Planung und Versionszugang; keine zweite Zustandsmaschine im Harvester. Jüngste Ernte-/Ablade-Fixes bleiben erhalten. Abbruch/Reset entwerten alte Ergebnisse; fremde Goto-Requests können einen Erntejob nicht mehr abbrechen. Zerstörte Lager werden auch während Unloading abgewiesen.
- Validierung: Build ohne Warnungen/Fehler, 687 Checks bestanden. 29 neue System-/Hostchecks einschließlich Vollbeladung, Ressourcenende, erfolgloser Planung, Lagerverlust, manueller Rückkehr, Teilkapazität, einmaliger Guthabenübertragung, Stop/Reset, akzeptiertem/abgewiesenem game-start und später Callback-Ergebnisse. Vorhandene Host-/Wire-/Bewegungschecks bestehen weiter. Zustandsbesitz, Late Join gegenüber flüchtigen Hostjobs und Grenzen in `HarvestSystem-Architektur.md` dokumentiert.
- Grenzen: Systemtests ersetzen den Planner kontrolliert; vollständige grafische Erntefahrt nicht neu abgenommen. Reflection-Fixtures bleiben Aufgabe 11. Kein Savegame/Hostmigration. Optische Abnahme von 02 und Harvester-Fahrprofil-/Soak-Aufgaben bleiben offen. Nächster umsetzbarer Punkt ist 06.

### 06 – Sanitäterlogik als eigenes Spielsystem abgrenzen

- [x] Erledigt am 01.10.2026. Priorität: mittel. Modell: **GPT-6 Sol, high**. Nach 05 als Referenz.
- MedicJobs, Heilintervalle, Patientenauswahl und Befehlsabbruch aus NetworkHost herauslösen. Gemeinsame Regeln für Squad- und unabhängige Sanitäter erhalten.
- Fertig wenn Heilreichweite, tote/ungültige Patienten, Squad-Wechsel, Stop, Neustart und Host-Replikation abgedeckt sind. Keine medizinischen Spezialfälle im allgemeinen Transportcode ergänzen.
- Ergebnis: MedicSystem besitzt Patientenjobs, Haltezustände, Auswahl und gemeinsame Heilintervalle. NetworkHost ordnet Requests, erweitert Squad-Empfänger, integriert budgetierte Goto-Planung und repliziert Ergebnisse. Explizite Systemabhängigkeiten nach dem HarvestSystem-Vorbild; keine Heil-Zustandsmaschine im Medic oder Transport. Squad-Wechsel entwertet Planung und beendet eine bereits aktive eigenständige Patientenanfahrt. Entfernte Patienten verlieren ihre Heilzeitgeber.
- Validierung: Build ohne Warnungen/Fehler; 726 Checks bestanden, davon 36 neue System-/Hostchecks für Reichweite, Pulsrate, HP-Grenzen, ungültige Patienten, Squad-Wechsel, Stop/Kontrollberechtigung, Session-/Befehlswechsel, späte Ergebnisse, game-start und echte Wire-/NetworkInput-Anwendung ohne doppelte Heilung. Zuständigkeiten, Replikation/Late Join und Lebenszyklus in `MedicSystem-Architektur.md` dokumentiert.
- Grenzen: Kontrollierter Planner in den FSM-Tests, vorhandene tatsächliche Host-Routentests zusätzlich; keine grafische Abnahme. Reflection-Fixtures bleiben Aufgabe 11. Kein Savegame/Hostmigration. Offene Bewegungsfälle bleiben separat offen. Nächster umsetzbarer Punkt ist 07.

### 07 – Kampfsimulation vom Netzwerk-Host trennen

- [x] Erledigt am 01.10.2026. Priorität: mittel. Modell: **GPT-6 Astra, xhigh**. Nach 05 und 06.
- Projektilsimulation, Trefferauflösung und Schaden in ein hostseitiges Kampfsystem überführen; vorhandenes DamageSystem und Flugprofile weiterverwenden.
- Autoritative Ergebnisse von rein visuellen Effekten trennen. Keine doppelte Schadensanwendung auf Host oder Client.
- Fertig wenn Direkt-/Flächenschaden, Bodenziele/Luftziele, tote Ziele, Angriffsstopp und Replikation getestet sind. Bestehendes Balancing erhalten.
- Ergebnis: CombatSystem besitzt Raketenflug, Einschläge, automatische Verteidigungsziele, Schussanforderungen und Trefferauflösung. Direkt-/Flächenschaden teilen einen Ablauf mit DamageCalculator, SquadBenefits und einmaligem OnHit. Welt, Veröffentlichung und Verlustmeldung sind explizite Abhängigkeiten. NetworkHost behält Request-Prüfung, FIFO, Munitionserlaubnis und Transport. Clients übernehmen absolute HP und stellen bestätigte Projektile/Einschläge dar. Sessionwechsel und akzeptierter game-start setzen aktive Projektile und ausstehende Einschläge zurück; abgewiesener Start bewahrt sie.
- Validierung: Build ohne Warnungen/Fehler; 772 Checks bestanden. 36 neue System-/Hostchecks für Direkt-/Flächenschaden, bestehende Rüstungs-/Radiusregeln, tatsächliche Raketenflugbahn, Boden-/Luftkollision, tote Ziele, Stop/Cooldown, idempotente HP-/Projektil-Replikation und Session-/Matchreset. Echter Wire-/NetworkInput-Weg auf Host und separater Clientwelt. Details in `CombatSystem-Architektur.md` und Netzwerk-Architektur ergänzt.
- Grenzen: Bestehende horizontale Explosionsreichweite und sofortiger Schaden visueller ballistischer Geschosse bleiben erhalten. Late Join erhält weiterhin keine bereits laufenden Raketenflugzustände, aber spätere Einschläge/HP-Werte. Keine grafische Abnahme; Reflection-Fixtures und globale Zugriffe in verwendeten Unit-Methoden bleiben Aufgabe 11. Offene Fahrzeugabnahme von 02 bleibt offen. Nächster umsetzbarer Punkt ist 08.

### 08 – Doppelte Katalogregeln und Produktregistrierung bereinigen

- [x] Erledigt am 01.10.2026. Priorität: hoch für weitere Inhalte. Modell: **GPT-6 Sol, high**. Nach 01; regulär nach 07.
- GrantedPerk aus GameplayCatalog verwenden statt zusätzlicher Air-Technology-Sonderzuordnung. Preise, Zeiten, Voraussetzungen und Produktdaten auf weitere Doppelpflege prüfen.
- Katalog und Factories durch validierte Registrierung verbinden oder ihre Konsistenz prüfen. Unbekannte kaufbare Typen dürfen nicht stillschweigend zu kostenlosen Angeboten werden; Editor-/GenericBuilding-Ausnahmen explizit behandeln.
- Fertig wenn eine zusätzliche Test-Forschung ohne neuen Research-Switch funktioniert, unbekannte Produkte verständlich scheitern und alle vorhandenen Produkte erzeugbar sind.
- Ergebnis: GrantedPerk wird ausschließlich aus GameplayCatalog gelesen. Gemeinsame Building-Regeln liefern Forschungsdauer und filtern Forschungsaktionen nach abgeschlossenen Perks und armeeweiten Warteschlangen; Host-Forschung ist nicht mehr auf GDIBase begrenzt. Factories verwenden explizite Konstruktorregistrierungen; Katalogvalidierung prüft beide Richtungen, Produzenten, Preise, Zeiten und Forschungs-Grants frühzeitig. Unbekannte Quotes sind nicht verfügbar und zeigen einen Fehlergrund im ActionPanel; strikte Preisabfragen erklären den unbekannten Typ. Kostenlose Editor-/GenericBuilding-Ausnahmen und bestehende Aliase sind ausdrücklich registriert.
- Validierung: Build ohne Warnungen/Fehler; 834 Checks bestanden, 62 neue Checks für ungültige Registrierung/Metadaten, unbekannte Produkte, Ausnahmen, tatsächliche Konstruktion sämtlicher Katalogtypen und zentrale Building-Preise. Zusätzliche Test-Forschung mit zwei Produzenten läuft ohne neuen Switch durch Angebote, Host-Kauf, Produktionsabschluss und Wire-/NetworkInput-Grant; doppelte/pending/abgeschlossene Forschung wird blockiert. Die zusätzliche Forschung bleibt ausschließlich im Test.
- Dokumentation und Grenzen: `Katalog-Registrierung.md`, Gameplay-Katalog-und-KI-Metadaten ergänzt. Factory-Checks nutzen einfache Mesh-Fixtures, keine grafische Content-Abnahme. Reflection-Fixtures bleiben Aufgabe 11; konkrete strategische KI-Typen Aufgabe 09. Fahrzeugabnahme 02 bleibt offen. Nächster umsetzbarer Punkt ist 09.

### 09 – KI-Ziele von konkreten Einheitentypen lösen

- [x] Erledigt am 01.10.2026. Priorität: hoch vor Fraktionen. Modell: **GPT-6 Astra, high**. Nach 08.
- Strom, Speicher, Bauarbeiter und Luftunterstützung anhand von Katalogfähigkeiten/Angeboten auswählen. Konkrete Helipad-/Helicopter-/Silo-/GDI-Abfragen im strategischen Plan durch passende Abfragen ersetzen.
- Bestehende Spezialverhalten dürfen Klassen behalten; keine automatische Erfindung neuen Verhaltens allein aus Metadaten erwarten. Mehrere Produzenten und Voraussetzungen berücksichtigen.
- Fertig wenn alternative Test-Produkte und ein zweiter Produzent ohne neue Typ-Sonderfälle in der KI ausgewählt werden. Mensch und KI verwenden dieselben Angebots-/Verfügbarkeitsregeln.

- Ergebnis: AIStrategicCatalog wählt Basis, Strom, Speicher, Wirtschaft, Sichtweite, Bauarbeiter und Luftunterstützung über Fähigkeiten und erreichbare Angebote. Basis-/Wiederaufbau, Fahrzeug-/Squad-Produktion und strategische Gebäudeprioritäten verwenden keine GDI-/Silo-/Helipad-Typabfragen mehr. ProvidedPerks beschreibt Gebäude-Voraussetzungsanbieter; GDIBase liest seine unbedingten Grants daraus. Der Planer versucht alternative Produzenten und Perk-/Stromanbieter in isolierten Zweigen. Host und KI teilen Building.CanProduceUnit samt Helipad-Reservierung; Preise/Perks bleiben im PricingService. Bereits bestellte bzw. kostenlos gelieferte Lufteinheiten lösen keinen weiteren Kauf aus.
- Validierung: Build ohne Warnungen/Fehler; 866 Checks bestanden. 32 neue Checks mit alternativen Produkten, zweitem Produzenten, echter Host-Annahme und Preis-/Zeitprüfung, vollen Queues, unerreichbarer erster Alternative, Perk- und Stromabhängigkeiten, Crew, fremden/embarkten Units und Warteschlangen-/Luftzielabschluss.
- Dokumentation und Grenzen: `Strategische-Katalogplanung.md`, Katalogleitfaden aktualisiert. Spezialverhalten bleibt ausdrücklich in Laufzeitklassen; Metadaten erzeugen keine Flug-/Ernte-/Perk-Implementierung. Keine neue grafische Schlachtabnahme, keine Fraktionsverwaltung oder gemeinsame KI-Planwarteschlange. Reflection-Fixtures bleiben Aufgabe 11; optische Fahrzeugabnahme 02 bleibt offen. Nächster umsetzbarer Punkt ist 10.

- Nachkorrektur: Der erste Spieltest zeigte stillstehende KI-Armeen. `game-start` hatte Start-Bulldozer ohne Fahrer erzeugt; die neue Betriebsbereitschaftsprüfung lehnte sie deshalb ab. MatchStartAssignment enthält jetzt eine vom Host erzeugte DriverUnitId, die NetworkInput beim Spawn verwendet. Fünf zusätzliche Regressionchecks prüfen eindeutige IDs/JSON, echten Bulldozer samt Fahrer, Katalogauswahl und den tatsächlichen ersten KI-Bau-Request. Build ohne Warnungen/Fehler; jetzt 871 Checks bestanden. Keine grafische Schlachtabnahme behauptet.

### 10 – Unit-Abfragen und Snapshot-Lebensdauer vereinheitlichen

- [x] Erledigt am 01.10.2026. Priorität: mittel. Modell: **GPT-6 Sol, high**. Nach 03.
- UnitHandler.Units erzeugt pro Zugriff eine Array-Kopie. Aufrufer inventarisieren; stabile Snapshots pro Auswertung/Phase und gezielte ID-/Army-Abfragen bereitstellen.
- Spawn, Tod, Entfernen und Army-Wechsel müssen Indizes konsistent aktualisieren. Sichere Iteration erhalten; keine veränderbare Liste nach außen geben. Räumlichen Index nur bei nachgewiesenem Bedarf ergänzen.
- Fertig wenn Verhaltenstests Indexkonsistenz prüfen und Messungen weniger Kopien/Allokationen im gleichen Szenario belegen.

- Ergebnis: UnitHandler liefert gecachte schreibgeschützte Welt-/Army-Snapshots und gezielte ID-Abfragen. Gemeinsame private Registrierung pflegt Aufnahme, Entfernung, Army-Wechsel und Reset; alte Snapshots bleiben sicher iterierbar. Eigene-Army-Abfragen von KI, Strom und Ressourcen sowie Kampf-/Netzwerkphasen nutzen die passenden Zugänge. Gebäude-Fallback registriert nur einmal; doppelte Spawn-IDs werden vor Grid-Änderungen abgewiesen.
- Validierung: Build ohne Warnungen/Fehler; 905 Checks bestanden, 34 zusätzliche Konsistenz-/Lebenszyklus-/Spawnchecks. Vergleich mit denselben 512 Units und 200 Auswertungen: bisher 15.139.200 Bytes, jetzt 0 Bytes im aufgewärmten stationären Messfenster, identische Ergebnisse. Details in `Unit-Abfragen-und-Snapshots.md` und `Unit-Abfragen-Messung.md`.
- Grenzen: Snapshots frieren Mitgliedschaft, keine Unit-Zustände ein; Spielthread-Regel bleibt bestehen. Änderungen verursachen weiterhin Snapshot-/Indexkosten; keine vollständige Schlacht-/FPS-Messung und kein räumlicher Index. Reflection-Fixtures bleiben Punkt 11, optische Fahrzeugabnahme 02 offen. Nächster umsetzbarer Punkt ist 11.

### 11 – Grafikfreien Testaufbau und explizite Systemabhängigkeiten ermöglichen

- [ ] Offen. Priorität: mittel. Modell: **GPT-6 Astra, high**. Nach 05–07.
- Neue Spielsysteme erhalten Welt, Dienste und Ergebniszugang explizit. Für deren Tests reguläre kleine Welten/Units ohne Grafikgerät und Mesh-Laden aufbauen.
- Reflection und GetUninitializedObject in den betroffenen Tests durch reguläre Fixtures ersetzen. Globale Zugriffe schrittweise an den bearbeiteten Grenzen reduzieren, nicht das ganze Projekt gleichzeitig umbauen.
- Fertig wenn zwei getrennte Testwelten ohne gegenseitige Zustandsbeeinflussung ausführbar sind und die extrahierten Systeme ohne Rendering getestet werden können.

### 12 – Komplexe Netzwerkbefehle schrittweise typisieren

- [ ] Offen. Priorität: mittel. Modell: **GPT-6 Astra, high**. Nach 01 und 05–08.
- Für Goto, Build und Harvest eigene Payloads mit eindeutigen Pflichtparametern einführen. Kleine UnitActions können UnitActionContext weiterverwenden.
- Serialisierung, Eingangsprüfung, Protokollversion und Verhalten bei inkompatiblen Clients gemeinsam berücksichtigen. Einen Befehlsweg nach dem anderen migrieren.
- Fertig wenn gültige Nachrichten hin/zurück serialisieren, ungültige Kombinationen abgewiesen werden und Mensch/KI/Host/Client denselben Vertrag verwenden.

### 13 – Integrationsprüfung für Neustart, Late Join und Abbruch ergänzen

- [ ] Offen. Priorität: mittel. Modell: **GPT-6 Astra, high**. Nach 02–12.
- Neue Systeme auf Reset und Zustandsbesitz prüfen. Session-Snapshot muss den nötigen Clientzustand rekonstruieren, ohne hostinterne Planungsarbeit versehentlich auf Clients zu starten.
- Szenarien: Einstieg bei laufender Produktion/Ernte/Forschung, Stop während Planung, zerstörter Auftraggeber, wiederholtes game-start und Sessionwechsel.
- Fertig wenn Tests Zustände und Ressourcen vergleichen und dokumentiert ist, welche Daten nur Map-Startdaten bzw. laufender Sessionzustand sind. Kein vollständiges Savegame-System als Nebenprojekt einführen.

### 14 – Erweiterungsleitfaden und Architekturübersicht aktualisieren

- [ ] Offen. Priorität: abschließend. Modell: **GPT-6 Luna, high**. Nach 13.
- Tatsächlichen Endstand dokumentieren: neue Unit, Building, Forschung, Fähigkeit und KI-Angebot ergänzen; zuständige Dateien und Datenflüsse nennen.
- Bestehende Architekturtexte und TODOs auf Widersprüche prüfen. Künftige Erweiterungen sollen eine nachvollziehbare Anleitung statt zusätzlicher versteckter Switches erhalten.
- Fertig wenn Beispiele anhand des realen Codes geprüft sind und offene Einschränkungen ausdrücklich genannt werden. Keine Gameplay-Änderungen in dieser Dokumentationsaufgabe.
