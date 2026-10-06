# KI-Client-TODO: verteilte Controller und Verhaltenskonfiguration

Stand: 06.10.2026. Vom Benutzer als derzeit wichtigster Umbau priorisiert. Aufgaben 01 bis 09 und 11 sind implementiert. Aufgabe 10 bleibt mangels ausreichendem Nutzenbefund zurückgestellt.

## Ziel und Grenzen

Ein KI-Controller soll dieselbe Army wahlweise auf dem Host oder einem zugewiesenen Client steuern können. Ein menschlicher Client kann zusätzlich KI-Armies übernehmen; später kann ein eigener Bot-Prozess ohne Fenster dieselbe Logik ausführen. Verhalten und Variation werden über JSON-Profile konfiguriert.

Der Host bleibt für Gameplay, Ressourcen, Bewegung, Kampf, Produktion und gültige Befehle zuständig. Verteilt wird die KI-Entscheidungsarbeit, nicht die gesamte Simulation. Hostseitige Pfadsuche wird dadurch nicht automatisch billiger. Kein Anspruch auf identische Entscheidungsfolgen bei unterschiedlicher Netzlatenz; ein gleicher Seed macht Profilwahl und Variation reproduzierbar, nicht alle Abläufe im verteilten Spiel.

Dedicated-Server-Projektaufteilung, automatische Lastverteilung und Hot-Reload der Profile folgen erst nach einem funktionierenden einzelnen entfernten KI-Controller.

## Arbeitsweise

Auftrag: „Arbeite den nächsten offenen Punkt aus AI/KI-Client-TODO.md vollständig ab.“

Je Auftrag eine nummerierte Aufgabe abschließen. Aktuellen Code und lokale Änderungen prüfen, bestehende Architektur-/AI-Spieler-TODOs abgleichen und gemeinsame Änderungen nur einmal umsetzen. Vorhandene KI bis zum ausdrücklich aktivierten Remote-Test weiter auf dem Host betreiben. Alle Controller verwenden denselben Befehlsweg, dieselben Katalogdaten und Regeln.

Erledigt heißt implementiert, angemessen geprüft und mit Ergebnis/Grenzen hier dokumentiert. Nach Codeänderungen zuerst `dotnet build RTS.csproj`, dann `dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj`. Notwendige Multiplayer-/Langzeittests gesondert dokumentieren; nicht durch reine Headless-Checks als bestanden ausgeben. Mutationen und KI-Weltzugriffe bleiben auf dem Spielthread; keine unkontrollierten Hintergrundzugriffe als Performance-Abkürzung.

## Befund im vorhandenen Code

- AIStrategyProfile liefert bereits BalancedAssault, InfantryCompany, AntiArmor und FastRecon, abgeleitet aus Match-Seed und ArmyId.
- Vorhandene Parameter: RequiredGunners, RequiredRakZero, RequiredTanks, RetreatHealthFraction, AttackReadinessSeconds, DefenseRadiusInCells und AssaultStallTimeoutSeconds.
- AIController und AIOrderQueue enthalten IsHost-Sperren. AIOrderQueue verwendet LocalRequestReceipt und lokale Ausführungsrückmeldungen; Controller lesen teilweise Globals.Game für Ressourcen, Pricing und Army-Daten.
- GameplayCatalog enthält Produkt-/KI-Metadaten. Preise, Strom, Produktionszeiten, Perks und Produzenten gehören weiterhin dorthin und werden nicht in KI-Profilen dupliziert.
- Vorhandene Planungsscheduler, KI-Aufgabenverwaltung, Budgets und Diagnose wiederverwenden. Eine räumlich entfernte KI darf keine ausschließlich hostlokalen Dienste direkt voraussetzen.

## Aufgaben in Reihenfolge

### 01 – KI-Kontext und Host-Abhängigkeiten isolieren

- [x] Erledigt am 06.10.2026. Priorität: höchst. Keine Abhängigkeit.
- Eigenen KI-Laufzeitkontext einführen: ArmyId, Controller-/Akteuridentität, Welt-/Army-Zugriff, Pricing/Katalog, Army-Sicht, Simulationszeit und Befehlsdienst.
- Direkte Abhängigkeiten von menschlicher Selektion, lokaler Spieler-Army, Kamera, HUD und Globals.Game schrittweise aus KI-Entscheidungen entfernen. Keine vollständige Globals-/Engine-Migration.
- Sicht muss immer zur gesteuerten Army gehören und darf weder durch Spectator-Modus noch durch menschliche Sicht erweitert werden.
- IsHost-Sperren und LocalRequestReceipt-Abhängigkeiten vollständig inventarisieren. Serverdienste wie Ernten/Kampf von hochstufigen KI-Entscheidungen abgrenzen; einzelne Agenten/Aufgabenclaims und Pfadprüfungen auf lokale replizierte Daten versus Host-Autorität prüfen.
- Zunächst gleicher Host-Betrieb über den neuen Kontext. Noch keine entfernten Controller durch bloßes Entfernen von IsHost erlauben.
- Fertig wenn vorhandene KI-Szenarien weiter funktionieren und die verbleibenden Remote-Blocker konkret dokumentiert sind.
- Ergebnis: AIContext bindet Akteur/Army, Welt, Pricing/Katalog, Army-Sicht, Zeit, Sessiongeneration und Befehlsdienst. KI-Planer und Teilcontroller beziehen Ressourcen/Perks/Preise aus ihrer Simulationswelt. Direkte Globals.Game-/Globals.World-Kopplungen in src/AI entfernt; Scouting/Verteidigung/Angriff verwenden Anzeige-unabhängige Army-Sicht. Host-Sperren bleiben erhalten.
- Validierung: Build ohne Warnungen/Fehler; 1.506 Checks bestanden, einschließlich 24 neuer Kontextprüfungen und Host-Entscheidungsupdate ohne Globals.Game. Bestehende KI-Szenarien bleiben erfolgreich. Kein Remote-Client oder vollständiger grafikfreier Spielstart behauptet.
- Vertrag, Abgrenzung der Host-Dienste und konkrete Resthürden in [KI-Laufzeitkontext.md](KI-Laufzeitkontext.md). Nächster offener Punkt: 02.

### 02 – Versionierte JSON-Verhaltensprofile laden und validieren

- [x] Erledigt am 06.10.2026. Priorität: höchst. Nach 01.
- Kleine typisierte Konfiguration mit SchemaVersion und ProfileId einführen. Vorschlag: `Config/AI/Profiles/*.json`; genauer Laufzeit-/Ausgabepfad bei Implementierung festlegen und Kopieren beim Build berücksichtigen.
- Zuerst vorhandene AIStrategyProfile-Werte konfigurieren: Truppen-Sollzahlen, Rückzugsgrenze, Angriffsbereitschaftszeit, Verteidigungsradius und Stillstandstimeout. Vier vorhandene Profile als Beispieldateien bereitstellen; bisheriges Verhalten als Default erhalten.
- Typauswahl weiterhin aus GameplayCatalog/Metadaten beziehen. Vorhandene typbezogene Sollzahlen zunächst kompatibel übernehmen; neue rollenbezogene Gewichte erst hinzufügen, wenn ein Entscheider sie tatsächlich auswertet. Keine dekorativen Config-Werte ohne Wirkung.
- Einheiten und Grenzen explizit dokumentieren: Sekunden, Grid-Zellen, Welteinheiten, Verhältnis 0..1. Endliche Zahlen, gültige IDs/Enums, sinnvolle Maximalwerte und unbekannte/fehlerhafte Felder prüfen. Fehler mit Datei und Feld melden; keine teilweise angewendeten Profile.
- Fehlende optionale Config nutzt den dokumentierten Default. Eine ausdrücklich angegebene ungültige Config darf nicht stillschweigend durch einen anderen Bot ersetzt werden.
- Aufgelöstes Profil unveränderlich für den Lauf/Match bereitstellen; keine Datei-/JSON-Arbeit pro Update. Keine komplexe Vererbung oder Hot-Reload im ersten Stand.
- Fertig wenn Beispielprofile das bestehende Verhalten reproduzieren und ungültige/null/nichtendliche Werte sowie falsche Versionen verlässlich behandelt werden.
- Ergebnis: AIProfileConfig/AIProfileCatalog laden vier versionierte Profile einmalig aus dem Ausgabeverzeichnis. Die bestehende Profilwahl bleibt erhalten; Controller erhalten unveränderliche Werte. Optional fehlende Dateien verwenden Originaldefaults, vorhandene ungültige Dateien werden mit Datei-/Feldangabe abgelehnt. Striktes Laden verlangt alle vier Profile. game-start validiert vor dem Welt-Reset.
- Validierung: Build ohne Warnungen/Fehler, alle 1.574 Checks erfolgreich (68 neue Profilchecks: Default-Gleichheit, tatsächliche Anwendung, Seed-Stabilität, Validierung, Fallback, atomare Fehlerbehandlung und unveränderliche Kataloge). Kein grafischer Multiplayer-/Langzeittest durchgeführt; Remote-Betrieb bleibt gesperrt.
- Pfad, Felder, Grenzen und Fehlerbeispiele: [Profil-Dokumentation](../Config/AI/Profiles/README.md). Nächster offener Punkt: 03.

### 03 – Profile, Variation und Rechenbudget getrennt konfigurierbar machen

- [x] Erledigt am 06.10.2026. Priorität: hoch. Nach 02.
- Match-stabile Profilwahl aus Match-Seed + ArmyId erhalten; alternativ ein festes ProfileId. Für Zufallsauswahl konfigurierbare Profilgewichte, eindeutige Seed-Priorität und protokollierten effektiven Seed vorsehen.
- Auf tatsächlich vorhandene Entscheider begrenzt weitere Parameter einführen: Scouting-Budget, Entscheidungsintervalle, Wirtschaft/Verteidigung/Angriff-Gewichtung bzw. Ressourcenreserve. Vor Einführung prüfen, welche davon schon an anderer Stelle existieren und zentralisiert werden sollten.
- Verhaltensprofil von maschinenlokalen Rechenlimits trennen: ein langsamer Rechner darf Arbeitsbudgets reduzieren, ohne ein anderes Charakterprofil vorzuspielen. Fairness zwischen mehreren lokal ausgeführten KI-Controllern erhalten.
- Keine künstlichen Ressourcen-/Sichtvorteile als Nebenwirkung einer Schwierigkeitsstufe. Ein benannter Difficulty-Preset erst dann, wenn seine Parameter konkret definiert sind.
- Diagnose zeigt aktives Profil, Seed, Budget und relevante Entscheidungen. Vergleichsspiel mit gleichem Seed durchführen.
- Fertig wenn Parameter erkennbare, geprüfte Wirkung haben und Default-Profil sowie Update-Kosten stabil bleiben.
- Ergebnis: selection.json steuert feste/gewichtete Profilwahl und optionalen Match-Seed (vor erzeugtem Seed, danach ArmyId-Hash). Profile konfigurieren konkrete Denkintervalle, Ressourcenreserve und Scouting-Neubewertung. compute.json begrenzt separat die gemeinsame faire Planungswarteschlange, ohne Ressourcen-/Sichtvorteile. ai-list zeigt effektives Profil, Seed, Verhalten, Limits und Entscheidungen. Keine Datei-/JSON-Arbeit im Update.
- Validierung: Build ohne Warnungen/Fehler; 1.739 Headless-Checks erfolgreich. Default-Auswahl für 100 Seeds gegen bisherigen Algorithmus verglichen, Seed-Priorität/Gewichte, Validierung, Reservewirkung und gemeinsame Budget-Fairness geprüft. Zwei gleiche KI-Ausgangsszenarien über 30 Simulationsschritte verglichen (Profil, Zielzustand, Ressourcen, Requesttypen). Ein erster Gesamtlauf scheiterte im bestehenden Scout-Reservierungstest ohne Ziel; der erneute vollständige Lauf bestand. Kein grafischer Langzeit-/Multiplayervergleich und keine Frame-Zeitgarantie behauptet.
- Konfiguration und Grenzen: [KI-Konfiguration](../Config/AI/README.md). Nächster offener Punkt: 04.

### 04 – Gemeinsame Request-IDs und Netzwerk-Rückmeldungen

- [x] Erledigt am 06.10.2026. Priorität: höchst. Nach 01; Profile aus 02/03 können schon lokal genutzt werden.
- Für KI-relevante Requests stabile IDs und eine lokale/entfernte gemeinsame Rückmeldeschnittstelle einführen. Bestehende LocalRequestReceipt-Semantik erhalten, ohne sie im Remote-Controller vorauszusetzen.
- Gesendet, angenommen, abgelehnt und später ausgeführt/fehlgeschlagen unterscheiden. Annahme einer Produktion ist nicht deren Fertigstellung. Bestätigte Zustands-/Produktionsbefehle mit auslösenden Requests/OrderIds verknüpfen.
- Rückmeldungen dem ursprünglichen Controller zuordnen. Duplikate, Timeouts, verzögerte Antworten, Abbruch und Sessionwechsel behandeln. Bei Timeout vor erneuter Bestellung prüfen, ob der Auftrag bereits angenommen wurde; keine Doppelkäufe.
- AIOrderQueue/Budgetreservierungen auf diesen gemeinsamen Vertrag umstellen. Menschen und KI behalten denselben Host-Validierungsweg; keine zweite Remote-Befehlspipeline.
- Fertig wenn lokale und entfernte Auftragstests dieselben Ergebnisse liefern, Ablehnungen Reservierungen freigeben und verlorene/späte Rückmeldungen keinen doppelten Auftrag erzeugen.
- Ergebnis: Gemeinsamer RequestReceipt mit stabilen Wire-IDs und gezielten Host-Rückmeldungen (Protokoll 8), getrennte Annahme/Ausführung, konkrete Produktions-/Baustellenkorrelation und Host-Deduplizierung. Client fragt bei Antwort-Timeout mit derselben ID nach; späte Antworten und Sessionwechsel können terminale Aufträge nicht wiederbeleben. KI-Queue/Planexecutor/Scouting verwenden den gemeinsamen Vertrag; Remote-KI-Sperren bleiben bestehen.
- Validierung: Build ohne Warnungen/Fehler; 1.763 Checks erfolgreich, einschließlich 24 neuer Rückmeldungschecks und echter TCP-Loopback-Prüfung für Annahme, Ablehnung, Abschluss und Timeout-/Duplikatwiederholung. Bestehende Kauf-/Reservierungs-/Produktionschecks bleiben erfolgreich. Keine Mehrprozess-KI-Abnahme behauptet.
- Vertrag und Grenzen: [Request-Rueckmeldungen.md](Request-Rueckmeldungen.md). Nächster offener Punkt: 05.

### 05 – Hostautorisierte Controller-Zuweisung mit Generation

- [x] Erledigt am 06.10.2026. Priorität: höchst. Nach 04.
- Pro KI-Army genau einen aktiven Controller erfassen: ArmyId, Controller-Peer, Zuweisungsgeneration und effektives Profil. Army/Team von der Rechner-/Peeridentität trennen.
- Host weist zu bzw. widerruft. Client kann eine Anfrage stellen, aber weder Army, Profil noch Befehlsberechtigung selbst beanspruchen. Eine Peer-Verbindung darf mehrere eindeutig getrennte KI-Kontexte tragen.
- KI-Requests müssen Army und Controllergeneration eindeutig zuordenbar sein. Host prüft die aktive Zuweisung zusätzlich zu normalen Gameplay-Regeln, einschließlich aller Empfänger bei Gruppenbefehlen. Keine pauschale Berechtigung über fremde Armies vergeben.
- Eine veraltete KI darf nach Wechsel nicht über gewöhnliche Player-Requests weiterkommandieren. Menschliche Befehle/granted permissions und KI-Zuweisung explizit unterscheiden, damit Kontrollfreigaben die Generation nicht umgehen.
- Zuweisungen in Session-/Late-Join-Zustand integrieren; game-start und Sessionwechsel nach klarer Regel behandeln. Erst nach Synchronisation startet ein Remote-Controller.
- Fertig wenn mehrere Armies eines Peers sauber getrennt sind und doppelte/alte/unberechtigte Controller keine Befehle auslösen.
- Ergebnis: Hostregister mit Army/Akteur/Peer, monotoner Generation und aufgelöstem Profil; Wire-Identitäten getrennt, Peer vom Transport gebunden. Alle Befehlsempfänger werden geprüft, wartende Requests und Goto-Planung erneut validiert. Unmarkierte Befehle/Kontrollfreigaben umgehen keine KI-Zuweisung. Register per Host-Command und Late-Join-Snapshot repliziert; game-start vergibt neue Hostgenerationen, Sessionwechsel leert das Register. Kein automatischer Remote-Start.
- Validierung: Build ohne Warnungen/Fehler, 1.790 Checks bestanden (27 neue Zuweisungs-/Transport-/Snapshotchecks). Ein Gesamtlauf scheiterte im bereits zuvor intermittierenden Scout-Reservierungstest ohne Ziel; der anschließende vollständige Lauf bestand. Keine Mehrprozess-KI-Abnahme behauptet.
- Vertrag und Grenzen: [KI-Controller-Zuweisung.md](KI-Controller-Zuweisung.md). Nächster offener Punkt: 06.

### 06 – Einen KI-Controller auf einem normalen zweiten Client betreiben

- [x] Erledigt am 06.10.2026. Priorität: höchst. Nach 03, 04 und 05.
- Manuelle Zuweisung per Tab-vervollständigbarem Konsolenbefehl ermöglichen; konkrete Namen bei Umsetzung mit vorhandenen Befehlen abstimmen. Statusbefehl zeigt Controller, Army, Profil und Generation.
- KI auf Client startet erst mit vollständiger replizierter Welt und Army-Kontext. Der Host führt für dieselbe Army keine parallele hochstufige KI aus; weiterhin notwendige Gameplay-Dienste laufen weiter.
- Lokale KI-Aufgaben/Planung von hostbestätigten Bewegungs-/Ernte-/Kampfzuständen trennen. Kein Direktzugriff auf Host-Queues oder hostlokale Task-Objekte voraussetzen. Blockierende Vorprüfungen mit vorhandenen Budgets ausführen.
- Konfiguriertes Verhalten auf Host und Client über dieselben Controllerklassen betreiben. Client liest Preise/Perks/Produzenten aus denselben Daten; Requests über PlayerCommandService.
- Fertig wenn eine entfernte KI wirtschaftet, baut, produziert, scoutet und kämpft und Annahmen/Ablehnungen korrekt verarbeitet. Zwei-Prozess-Test mit Host und Client dokumentieren; reine Einzelprozess-Checks reichen nicht als vollständige Abnahme.

- Ergebnis: Host-Konsole ai-controller-assign / ai-controller-list, synchronisierte RemoteAIRuntime mit unverändertem Host-Profil, dieselben Controller und PlayerCommandService-Requests. Client-Queue verarbeitet Host-Rückmeldungen statt hostlokaler Produktionsobjekte; lokale Claims/Planung bleiben getrennt. Remote-Bauplatzvorprüfungen teilen das begrenzte Scouting-Planungsbudget. Baubestätigungen registrieren explizit die Host-Army.
- Validierung: Build ohne Warnungen/Fehler und vollständige Regression erfolgreich. Echter Zwei-Prozess-TCP-Test mit Host ohne hochstufige KI: 3.066 zusätzlich erkundete Zellen, geerntete/abgeladene Ressourcen, 65 Kampfschaden, Reaktor-/Fabrik-/Helipadbau und produzierte Soldaten/Tank/Gepard/Heli. Zwölf Kauf-/Forschungsaufträge mit Annahme, Fortschritt und Abschluss; fremder Army-Befehl abgelehnt. Grafiklose Test-Meshes und beschleunigte Simulation; kein grafischer Langzeit-/Performancevergleich behauptet.
- Bedienung, reproduzierbarer Test und Grenzen: [KI-Remote-Client.md](KI-Remote-Client.md). Automatischer Disconnect-Rückfall folgt in 07.

### 07 – Controllerwechsel und Rückfall auf den Host

- [x] Erledigt am 06.10.2026. Priorität: höchst. Nach 06.
- Disconnect und fehlenden KI-Fortschritt getrennt erkennen. Eine vorhandene Verbindung allein beweist keinen aktiven KI-Controller; z.B. Controller-Heartbeat auf realer Zeit und Generation vorsehen, getrennt von Spielzeit/Performancebudgets.
- Host widerruft die alte Generation und übernimmt kontrolliert. Bestehende Gameplay-Aufträge bleiben bestehen; noch nicht angenommene Requests der alten KI verwerfen und Reservierungen bereinigen. Reihenfolge mit bereits angenommenen Host-Aufträgen definieren.
- Neue KI rekonstruiert Planung aus aktuellem Welt-/Auftragszustand, Profil und Seed. Nicht sämtliche internen Planungsobjekte übertragen. Bereits gekaufte/forschende/produzierende Aufträge berücksichtigen.
- Wiederverbindung nicht automatisch mit neuer/alter KI gleichzeitig aktivieren. Profil und Seed bei Übergabe erhalten; explizite erneute Zuweisung ermöglichen.
- Fertig wenn Disconnect, Neustart, verspätete Requests und Wechsel während Bau/Produktion ohne Doppelcontroller, Doppelkäufe oder Stillstand funktionieren.

- Ergebnis: Generationsgebundene Heartbeats mit abgeschlossenen Update-Sequenzen auf monotoner Echtzeit; getrennte Diagnose für Disconnect, fehlenden Start und ausbleibende Aktivität. Host vergibt neue Generation mit erhaltenem Profil/Seed, verwirft unbestätigte alte Requests/Pfadsuchen und übernimmt die Entscheidungen. Bestätigte Gameplay-Jobs bleiben aktiv; neue Planer übernehmen bestehende Bau-, Ernte-, Produktions- und Forschungszustände. Reconnect/Neustart bleibt bis zu expliziter neuer Zuweisung passiv. Protokoll 10.
- Validierung: Build ohne Warnungen/Fehler; 1.839 Checks bestanden (27 neue Wechsel-/Rückfallchecks). Drei echte Zwei-Prozess-Läufe: normal, Disconnect mit Wiederverbindung und Stillstand bei weiterhin verbundener TCP-Verbindung. Bezahlte Baustelle fertiggestellt, Forschung nicht doppelt bestellt, Host spielt weiter und alte Tokens abgelehnt. Grafiklose beschleunigte Testwelt, kein grafischer Langzeit-/Performancevergleich behauptet.
- Vertrag, Bedienung und reproduzierbare Tests: [KI-Controller-Wechsel.md](KI-Controller-Wechsel.md). Nächster offener Punkt: 08. Dedicated-Server bleibt separat in 11 festgehalten.

### 08 – Bot-Client-Konfiguration und Start ohne Fenster

- [x] Erledigt am 06.10.2026. Priorität: hoch. Nach 07.
- Zusätzlich zu Verhaltensprofilen eigene Prozess-/Verbindungskonfiguration vorsehen, z.B. `Config/AI/bot-client.example.json`: Serveradresse, Port, Anzeigename, Profilvorschlag und maximal gleichzeitig übernommene KI-Armies. Keine fest verdrahteten Player-/Army-IDs oder Selbstzuweisung.
- Host bestätigt ein Profil und übermittelt dessen aufgelöste Werte/Version/Fingerprint bei Zuweisung, damit verschiedene lokale Dateien keinen unbemerkten Unterschied bewirken. Fingerprint dient Diagnose; er ersetzt keine Berechtigung.
- Beispiel ohne Zugangsdaten ausliefern. Falls später Session-Secrets benötigt werden, nicht in Beispiel/Logs speichern.
- Startargument für ausdrücklich gewählte Config definieren. Ohne Fenster verbinden, Session synchronisieren und nur zugewiesene Controller starten. Modellabhängige Footprints/Pivots und Katalogdaten müssen ohne GraphicsDevice verfügbar sein; kein Renderer oder Dummy-HUD als Voraussetzung.
- Verbindungstrennung und Prozessende räumen Controller sauber auf. Eine reconnectende Instanz wartet auf neue Host-Zuweisung.
- Fertig wenn ein eigenständiger Bot-Prozess eine Army über den normalen Netzwerkweg steuert. Separate Projekte erst dort ausziehen, wo die Abhängigkeiten tatsächlich gelöst sind.

- Ergebnis: Produktionsprozess RTS.dll --bot-client <config.json>, strikte versionierte Verbindungsconfig und echte CPU-Modellimporte ohne Fenster/GraphicsDevice/Dummy-HUD. Snapshot bestimmt die Weltgröße. Host begrenzt Bot-Kapazität, bestätigt optional den Profilvorschlag und repliziert aufgelöste Werte samt Version/Fingerprint. Reconnect wartet passiv auf neue Generation; bestehende Controller werden sauber beendet. Protokoll 11.
- Validierung: Build ohne Warnungen/Fehler, 1.867 Regressionchecks erfolgreich. Zwei echte Zwei-Prozess-Tests mit dem Produktions-Bot und realen Modellen: Reaktor/Fabrik fertiggebaut, Soldaten/Fahrzeuge produziert; Kapazitätsüberschreitung abgelehnt. Disconnect/Reconnect bleibt bis manueller Neuzuweisung passiv und startet Generation 3 mit erhaltenem Profil. Keine grafische Langzeit-/Performanceabnahme; kein gemessener Cargo-Fortschritt im kurzen Prozessszenario.
- Bedienung, Config und Testgrenzen: [KI-Bot-Client.md](KI-Bot-Client.md). Nächster offener Punkt: 09. Dedicated-Server bleibt in 11.

### 09 – Performance und Multiplayer-Robustheit vergleichen

- [x] Erledigt am 06.10.2026. Priorität: hoch. Nach 08.
- Gleiche Karte, Seed, Profile und vergleichbare Spielphasen messen: alle KI auf Host; eine remote; mehrere remote. Entscheidungszeiten, Frame-Ausreißer, Host-Pfadsuche, Requestaufkommen, Netzlatenz und Kosten auf KI-Rechnern getrennt ausweisen.
- Mehrere Armies pro Client mit gemeinsamen Arbeitsbudgets testen. Gesamte Welt wird weiterhin repliziert; zusätzliche Clients verursachen Netzwerk-/Replikationskosten.
- Langzeittests mit Disconnect, wiederholtem game-start, spätem Beitritt, Request-Ablehnungen und veränderten Gebäuden/Perks durchführen. Neben Performance auch tatsächlichen Spielfortschritt prüfen.
- Fertig wenn ein reproduzierbarer Vergleich mit Grenzen vorliegt. Erst danach entscheiden, ob automatische Verteilung ausreichend Nutzen bringt.

- Ergebnis: Reproduzierbares Mehrprozess-Benchmarkskript mit realen Modellen und Produktions-Bots, opt-in JSON-Messungen für Update-/KI-/Pfadsucharbeit, CPU/TCP, Requestantworten und separaten Heartbeat-Roundtrip. Zwei Armies teilen auf einem Bot das vorhandene Vorplanungsbudget. Vier Varianten zweimal mit gleicher Karte/Profil/Seed geprüft; tatsächlicher Bau-/Produktionsfortschritt gemessen.
- Validierung: Build ohne Warnungen/Fehler, 1.881 Checks bestanden. Stressläufe über 300 und 180 Sekunden: Disconnect/passiver Reconnect/Neuzuweisung, später Observer-Beitritt, zwei Spielneustarts, abgelehnter Token-/Army-Befehl und zerstörte Gebäude/Perk-Provider; neue Bauaufträge in jeder Phase, replizierte Neustarts bestätigt.
- Befund: Ein gemeinsamer Bot senkt Host-Entscheidungsarbeit; vollständige Weltkopien erhöhen Gesamt-CPU und TCP-Kosten. Kein ausreichender positiver Nutzenbefund für automatische Verteilung. Loopback/grafiklose Messung, kein mehrstündiger Test oder FPS-Versprechen; Cargo blieb 0. Ergebnisse, Rohdaten und Reproduktion: [KI-Client-Performance.md](KI-Client-Performance.md).

### 10 – Optionale automatische Zuweisung

- [ ] Zurückgestellt. Priorität: später. Nach 09 und positivem Nutzenbefund; aktuell nicht ausreichend belegt. Manuelle Zuweisung bleibt bestehen. Aufgabe 11 ist davon unabhängig.
- Clients melden freiwillige KI-Kapazität; Host verteilt freie Controller mit stabiler Zuweisung und Rückfall. Kein häufiges Migrieren jeder kleinen Laständerung.
- Nutzer kann Übernahme deaktivieren oder begrenzen. Gemeldete Kapazität ist eine Planungshilfe; Host bleibt Entscheider. Langsame/überlastete Clients nicht beliebig mit weiteren Armies belasten.
- Fertig wenn Zuweisung nachvollziehbar ist und alle Sicherheits-/Lifecycle-Regeln aus 05/07 weiter gelten.

### 11 – Dedicated-Server ohne Grafik bereitstellen

- [x] Erledigt am 06.10.2026. Priorität: anschließend. Nach 08 und 09; unabhängig von der optionalen automatischen Zuweisung in 10. Vom Benutzer ausdrücklich als späteres Ziel festgehalten.
- Einen konkreten Dedicated-Server-Arbeitsplan erstellen und anschließend umsetzen: eigenständiger Host-Prozess ohne Fenster, GraphicsDevice, Renderer oder HUD. Vom Bot-Client unterscheiden: Der Server betreibt die autoritative Simulation, der Bot-Client nur zugewiesene KI-Entscheidungen.
- Gemeinsame Simulation, Kataloge, Modell-Metadaten und Netzwerkregeln wiederverwenden; keine zweite Gameplay-Implementierung. Projektaufteilung erst anhand der dann verbleibenden Grafik-/Globals-Abhängigkeiten festlegen.
- Serverkonfiguration und Startargumente für Karte, Port, Spieler-/KI-Slots und Spielstartregeln vorsehen. Kartenladen, Session-Lifecycle, Late Join, Disconnect sowie kontrolliertes Beenden und Diagnose ohne interaktive Spielkonsole ermöglichen.
- Hostlokale und entfernte KI unterstützen; vorhandene Controller-Zuweisung und Rückfallregeln übernehmen.
- Fertig wenn ein separater Server-Prozess ein Multiplayer-Spiel mit menschlichen Clients und KI zuverlässig betreibt. Mehrprozess- und Langzeittests dokumentieren.

- Ergebnis: Eigenständiger autoritativer Prozess `RTS.dll --dedicated-server <config.json>` ohne Game1/RTSGame, GraphicsDevice, Renderer oder HUD. Versionierte Serverconfig für PNG-/JSON-Karte, festen Port, menschliche Slots, KI-Profile und Startregel; lokale stdin-/Datei-Adminbefehle, Snapshots/Late Join, Lobbyfreeze, map-publish vor Matchreset, kontrolliertes Beenden und Diagnose. Gemeinsame Host-/Pricing-/Import-/KI-Dienste; CommandCenter-Ziele gemeinsam ausgezogen. Eigene Projektaufteilung bewusst erst später, kein zweites Gameplay.
- Validierung: Build ohne Warnungen/Fehler, 1.913 Checks erfolgreich. Produktions-Server + normaler Spieler-Vertrag + Produktions-Bot in echten Prozessen über 240 und 300 Sekunden: tatsächlicher Bau/Produktion und Bewegung, spätes Join, volle Slots, fremde Befehle, Bot-Abbruch/Host-Rückfall/erneute Zuweisung sowie zwei Matchneustarts und sauberes Stop. In jeder Matchphase neue Bauaufträge, Profil/Seed/Fingerprint erhalten.
- Betrieb, Arbeitsplan, Grenzen und Rohdaten: [Dedicated-Server.md](Dedicated-Server.md). Windows/Loopback/grafiklose Spieler-Verbindungen; keine grafische oder mehrstündige LAN-/WAN-Abnahme. Optionale automatische Verteilung bleibt zurückgestellt.

## Erste Config-Dateien als Ziel für Aufgabe 02

- `balanced-assault.json`, `infantry-company.json`, `anti-armor.json`, `fast-recon.json` mit den bisherigen Profilwerten.
- Kurze Feld-/Versionierungsdokumentation und Validierungsbeispiele neben den Dateien.
- Bot-Verbindungsdatei erst in 08, weil Verhalten und Netzwerkstart unterschiedliche Zuständigkeiten sind.

## Nächster Auftrag

„Arbeite den nächsten offenen Punkt aus AI/KI-Client-TODO.md vollständig ab.“ hat aktuell keinen weiteren umsetzbaren offenen Punkt. Punkt 10 bleibt bis zu einem positiven Nutzenbefund zurückgestellt; Dedicated-Server (11) ist abgeschlossen. Die Profile werden bereits in 02 auf dem bestehenden Host-KI-Betrieb nutzbar; sie müssen nicht bis zum fertigen Bot-Prozess warten.
