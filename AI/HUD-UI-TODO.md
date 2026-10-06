# HUD/UI-TODO

Stand: 06.10.2026. Ziel: schnellere Bedienung per Tastatur, klarere Selektion und hilfreiche Ereignismeldungen. Diese Datei beschreibt geplante Arbeit; die offenen Punkte sind noch nicht implementiert.

## Arbeitsweise

Auftrag: „Arbeite den nächsten offenen Punkt aus AI/HUD-UI-TODO.md vollständig ab.“

Je Auftrag eine nummerierte Aufgabe umsetzen. Vorher aktuellen Code, lokale Änderungen und vorhandene TODOs prüfen. Bestehende Eingabe-, Selektions- und Netzwerkwege erweitern. Kamerabewegung und Selektion bleiben lokal; Gameplay-Befehle laufen weiterhin über PlayerCommandService und den Host. Keine automatische Standard-Action bei Selektion wieder einführen.

Erledigt bedeutet: implementiert, angemessen geprüft und Ergebnis hier dokumentiert. Bei Codeänderungen erst `dotnet build RTS.csproj`, anschließend `dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj`. Optische Abnahme im Spiel gesondert festhalten; ohne Sichtprüfung nicht als erfolgt ausgeben.

## Bereits vorhanden – erhalten

- Strg + Ziffer speichert die aktuelle Selektion als Gruppe; Ziffer selektiert diese Gruppe.
- H springt zur Basis/Home-Quelle.
- Shift hängt geeignete Befehle an die Befehlswarteschlange an.
- Ohne explizite UnitAction werden sinnvolle kontextabhängige Befehle vorgeschlagen.
- Mehrfachselektion zeigt auch Actions an, die nur ein Teil der ausgewählten Units unterstützt.
- Produktionsübersicht sowie Satellite-Recon-Anzeige sind vorhanden.

## Nächste Aufgaben

### 01 – Doppeltippen einer Gruppentaste springt zur Gruppe

- [x] Implementiert am 06.10.2026. Priorität: hoch. Keine Abhängigkeit. Manuelle Abnahme im Spiel noch offen.
- Einfaches Drücken einer Ziffer selektiert wie bisher. Zwei getrennte Tastendrücke derselben Ziffer innerhalb eines kurzen Fensters selektieren die Gruppe und zentrieren die Kamera auf ihr.
- Als Startwert 300 ms verwenden, zentral einstellbar. Gehaltene Tasten und Betriebssystem-Tastenwiederholung zählen nicht als Doppeltippen. Strg + Ziffer speichert ausschließlich; andere Gruppentasten unterbrechen die Folge.
- Zentrum aus noch gültigen Gruppenmitgliedern bestimmen. Tote/entfernte Units ausschließen; für eingeschiffte Mitglieder eine sinnvolle Containerposition berücksichtigen. Leere Gruppen bewirken keinen Kamerasprung. Kamerahöhen-/Terrainregeln erhalten.
- Konsole, Texteingaben, fehlender Fensterfokus und unzulässiger Modus sperren die Eingabe. Vorhandene Gruppenbehandlung dabei auch auf diese Eingabesicherheit prüfen.
- Fertig wenn Einzeltippen, Doppeltippen, Halten, Strg-Speichern und leere/teilweise entfernte Gruppen korrekt funktionieren.
- Ergebnis: SelectionGroupHotkeys erkennt getrennte Tastendrücke mit zentral einstellbarem 300-ms-Fenster. Die HUD-Zeitbasis ist Stopwatch und damit unabhängig von Simulationsbeschleunigung. Einfachdruck selektiert, Doppeltippen zentriert über Camera.CenterOn mit bestehender Terrainhöhe und Orientierung; Strg speichert ausschließlich. Gehaltene Tasten, mehrdeutige gleichzeitige Gruppentasten, andere Gruppentasten, Strg, gesperrte Eingabe und HUD-Reset unterbrechen Doppeltippfolgen.
- Ungültige/sterbende Gruppenmitglieder werden beim Abruf entfernt; eingeschiffte Mitglieder liefern die Position eines noch gültigen Containers. Mehrere Insassen zählen beim Zentrum nicht mehrfach. Nicht sichtbare fremde Mitglieder erzeugen keinen Kamerasprung. Bei Session-/Match-Reset werden Gruppen samt Mitgliedsmarkierungen geleert.
- Fensterfokus wird von Game1 bis zum HUD weitergegeben. Die bestehende Konsolensperre gilt nun auch für Gruppenselektion und Speicherung; Game- und Editor-Layout erlauben Gruppen, andere Layouts nicht.
- Validierung: Build ohne Warnungen/Fehler; 1.437 Headless-Prüfungen bestanden, darunter 30 neue Gruppenprüfungen zu Tastendruckfolgen, Sperren, einstellbarem Zeitfenster, Mitgliedern, Containern und Erhalt von Kamerahöhe/Orientierung.
- Manuell noch prüfen: Strg+1 speichern, 1 einfach bzw. zweimal schnell drücken, Taste halten, in geöffneter Konsole tippen und Fensterfokus wechseln. Zusätzlich Gruppe mit entfernten Mitgliedern/eingeschifften Soldaten und beschleunigtes Spiel ausprobieren. Keine grafische Abnahme behauptet.

### 02 – Selektionsmarkierung am Boden und passende Klick-Erkennung

- [x] Implementiert am 06.10.2026. Priorität: hoch. Vor der Doppelklick-Selektion aus 03. Manuelle Abnahme im Spiel noch offen.
- Screen-Boundingbox als normale Selektionsmarkierung durch eine Weltmarkierung ersetzen. Bestehende Squad-Leader-Markierung als Ausgangspunkt prüfen und Rendering möglichst vereinheitlichen; Leader-Hervorhebung und eigentliche Selektion unterscheidbar halten.
- Bodeneinheiten mit Ring oder kompakter Kontur auf dem Terrain unter der Unit kennzeichnen. Terrainhöhe/Neigung berücksichtigen und Z-Fighting vermeiden. Gebäude entlang ihrer tatsächlichen Footprint-Fläche markieren, auch bei L-Formen. Gameplay-Footprints dabei nicht verändern.
- Für Point-and-Click auf Bodeneinheiten den Kamerastrahl mit dem Terrain schneiden und am Treffpunkt anhand der aktuellen Footprint-Fläche samt kleiner, zentral einstellbarer Klicktoleranz suchen. Keine veraltete reine Grid-Belegung verwenden, wenn die gerenderte Unit bereits zwischen Zellen steht. MobileUnits behalten Vorrang vor Gebäuden.
- Lufteinheiten direkt am sichtbaren Fluggerät über den Kamerastrahl und geeignete räumliche Bounds treffen. Selektionsmarkierung auf Flughöhe anzeigen; zusätzlich einen dezenten Bodenring mit Verbindungslinie vorsehen, damit die Position über dem Terrain verständlich ist. Bei gelandeten Helis überflüssige doppelte Markierungen vermeiden.
- Trefferregel: tatsächlich vom Strahl getroffene Lufteinheit vor Bodeneinheit am Terrain-Treffpunkt, danach Gebäude. Bei mehreren Lufttreffern den nächsten geeigneten Treffer verwenden. Gelandeten Heli auf dem Helipad ausdrücklich prüfen; Flug-/Landestatus darf keine Selektionslücke erzeugen.
- Einen gemeinsamen Trefferweg für Selektion und kontextabhängiges Hover/Klicken verwenden, damit Tooltip und tatsächlicher Klick dieselbe Unit meinen. Aktive Zielaktionen, HUD-Eingaben und die bestehende Trennung von Selektion und Befehlsvergabe erhalten.
- Fog of War, eingeschiffte/sterbende Units sowie Besitz-/Debug-Regeln weiter beachten. Fremde Debug-Selektion erteilt keine Befehlsberechtigung. Bodenring und Verbindungslinie dürfen keine unsichtbaren Lufteinheiten verraten.
- Auswahlkasten und Healthbars dürfen weiterhin im Bildschirmraum arbeiten. Screen-Bounds für Debug-Anzeige und erforderliche Treffer-/Auswahlberechnungen erhalten; nicht pauschal entfernen.
- Fertig wenn Bodenunits, L-förmige Gebäude, fliegende und gelandete Helis eindeutig markiert und anklickbar sind. Verschiedene Kamerawinkel, Terrainneigung, bewegte Units und überlappende Einheiten im Spiel prüfen; Sichtprüfung gesondert dokumentieren.
- Ergebnis: UnitSelectionGeometry vereinheitlicht Terrain-Treffer und Weltmarkierungen. Bodentreffer verwenden aktuelle Position, Rotation und importierte Footprint-Regionen oder die Größenfläche, unabhängig von gespeicherter Grid-Belegung; ClickTolerance ist zentral mit 0,2 Welteinheiten voreingestellt. Einzelauswahl und Kontext-Hover/Klicken verwenden PlayerHandler.FindUnitAt. Auswahlkasten und Healthbars behalten Bildschirmgeometrie.
- Helis werden auch im gelandeten Zustand direkt per Modell-Bounds getroffen; zusätzliche Bodenprüfung vermeidet eine Selektionslücke. Bei Lufttreffern gewinnt der nächste Treffer vor dem Terrain, danach MobileUnits vor Gebäuden. IsSelectable und lokale Sichtbarkeit filtern alle Kandidaten. Bestehende Kontrollberechtigungen bleiben erhalten.
- Normale weiße Selektionsringe werden im Weltpass gezeichnet; Gebäude erhalten nur die Außenkanten ihrer Grid-Footprint-Maske. Terrainlinien sind höhenangepasst und leicht angehoben. Erhöhte Lufteinheiten erhalten Flugring, dezenten Bodenring und Verbindungslinie; bodennahe Units nur einen Ring. Squad-Leader bleiben cyan hervorgehoben mit größerem Radius als der weiße Selektionsring. Rendering verwendet DepthRead und restauriert Blend-/Depth-Zustand. Screen-Rechtecke sind nur mit Debug_ShowUnitBounds aktiv.
- Validierung: Build ohne Warnungen/Fehler; 1.459 Headless-Prüfungen bestanden, darunter 22 neue Prüfungen zu aktuellen Weltpositionen, Toleranz, Mobile-/Luftpriorität, nächsten Lufttreffern, Terrainverdeckung, unsichtbaren/sterbenden/eingeschifften Kandidaten, rotierten L-Regionen und Markerlinien einschließlich Flughöhe und Terrainoffset.
- Manuell noch prüfen: Selektion/Hover bei mehreren Kamerawinkeln, Heli auf Helipad und in der Luft, Fahrzeuge während der Fahrt, L-Gebäude und unebenes Terrain. Bei ausgewähltem Squad-Mitglied die größere cyanfarbene Leader-Markierung mit der weißen Selektion vergleichen. Keine grafische Abnahme behauptet.

### 03 – Doppelklick auf eine Unit selektiert den Typ im Umkreis

- [x] Implementiert am 06.10.2026. Priorität: hoch. Nach 02. Manuelle Abnahme im Spiel noch offen.
- Ein Doppelklick auf dieselbe getroffene Unit selektiert eigene, selektierbare Units desselben GameplayTypeId in ihrer Umgebung. Keine Klassennamen-Heuristik, damit etwa Engineer und Gunner getrennte Typen bleiben.
- Vorschlag für den ersten Stand: Radius 25 Welteinheiten, zentral einstellbar; Entfernung in X/Z. Der Radius ist ein Abstimmungswert, keine bereits festgelegte Spielregel.
- Bestehende Trefferpriorität für MobileUnits erhalten. Zwei nahe Klicks auf unterschiedliche Units oder ein Auswahlkasten zählen nicht als Doppelklick. Kleine Mausbewegung tolerieren.
- Shift + Doppelklick ergänzt die Selektion; ohne Shift wird sie ersetzt. Versteckte, sterbende und eingeschiffte Units ausschließen. Fremde Debug-Selektion nicht versehentlich in eigene kommandierbare Gruppen aufnehmen.
- Bei aktiver Zielwahl wird weiter ein Befehl vergeben, keine Typselektion. Eingabe auf HUD/Minimap oder bei aktivem kontextabhängigem Befehlsmodus darf nicht zusätzlich selektieren. Den Wechsel zwischen Selektions- und Befehlsabsicht ausdrücklich prüfen.
- Fertig wenn Radius, Typ, Besitz, Sichtbarkeit und Shift stimmen und kein Doppelklick gleichzeitig einen Weltbefehl auslöst.
- Ergebnis: UnitTypeSelection erkennt zwei Klicks auf dieselbe Unit innerhalb von 300 ms echter Zeit bei bis zu sechs Pixeln Mausbewegung. Zeitfenster, Maustoleranz und Auswahlradius (25 Welteinheiten) sind zentral einstellbar. Auswahl erfolgt über GameplayTypeId und X/Z-Abstand mit eigenen, sichtbaren, selektierbaren Units; tote/eingeschiffte Units und fremde Debug-Mitglieder werden nicht übernommen. Shift ergänzt ohne Duplikate, ohne Shift ersetzt die Typauswahl die bisherige Auswahl.
- Klickabsicht: initiale Auswahl, erneuter Klick auf eine bereits ausgewählte eigene Unit oder Shift-Klick auf eine eigene Unit sind Selektionsgesten. Ein Klick auf ein anderes Ziel bei vorhandener Auswahl bleibt ein Kontextbefehl; explizite Zielaktionen haben immer Vorrang. Damit kann der zweite Klick nach initialer Unit-Selektion keinen Goto-/EnterUnit-Befehl erzeugen. Auswahlkasten, andere Units, Befehle, externe Auswahländerungen und Action-Wahl unterbrechen Klickfolgen.
- Eingabesicherheit: PlayerHandler.SuspendPointerInput verwirft laufende Gesten beim HUD-/Minimap-Wechsel, in der Konsole und ohne Fensterfokus. Beim Zurückkehren wird eine noch gehaltene Maustaste erst nach Loslassen freigegeben, damit HUD-Klicks keine Weltbefehle auslösen. Vorhandene Welt-Trefferpriorität aus 02 bleibt erhalten; keine neuen Gameplay-Requests für Typselektion.
- Validierung: Build ohne Warnungen/Fehler; 1.482 Headless-Prüfungen bestanden, darunter 23 neue Prüfungen zu Klickfolgen, Maus-/Zeitgrenzen, Radius, unterschiedlichen Gameplay-Typen, Eigentum, Sichtbarkeit, Shift-Zusammenführung sowie exklusiver Selektions-/Befehlsabsicht.
- Manuell noch prüfen: eigene Unit doppelklicken, mit Shift einen weiteren Typ ergänzen, eigene unbemannte Fahrzeuge bei bestehender Auswahl weiterhin per Kontextbefehl betreten und explizite Angriffs-/Goto-Ziele doppelklicken. Ebenfalls Auswahlkasten, HUD/Minimap, Konsole und Fokuswechsel während einer Maustaste testen. Keine grafische Abnahme behauptet.

### 04 – Zentrale Action-Hotkeys mit sichtbaren Kürzeln

- [ ] Offen. Priorität: hoch. Keine Abhängigkeit.
- Hotkey-Zuordnung zentral verwalten und Actions/semantischen Befehlen zuordnen. Neue Units sollen vorhandene Action-Hotkeys ohne eigene Eingabelogik nutzen können. Benennbare, später umbelegbare Zuordnung vorbereiten; eine Einstellungsoberfläche ist noch nicht nötig.
- Startbelegung als Vorschlag: S = Stop, A = Angriffsziel wählen, G = Goto-Ziel wählen, E = Einsteigen-Ziel wählen, R = Rückkehr/Abladen bzw. Sammelpunkt bei passenden Gebäuden. Bestehende Tasten, Editor und Kamera vor Vergabe auf Konflikte prüfen.
- R darf bei gemischter Selektion nicht gleichzeitig unterschiedliche Aktionen auslösen. Einen nachvollziehbaren Kontext bestimmen oder die mehrdeutige Taste deaktivieren; die gewählte Belegung im Panel zeigen.
- Zielaktionen aktivieren dieselbe Zielwahl wie ein Panelklick; unmittelbare Aktionen nutzen denselben Request-Weg. Nur geeignete kontrollierbare Empfänger verwenden. Preise, Perks, Verfügbarkeit und SingleActor-Regeln genauso prüfen wie beim Panel.
- Kürzel im ActionPanel/Tooltip zeigen. Tastendruck einmalig behandeln; Shift-Queue erhalten. Konsole, Texteingaben und Fensterfokus beachten.
- Esc zuerst die aktuelle Zielwahl/Platzierung abbrechen, erst beim nächsten Druck ohne Zielwahl die Selektion aufheben. Keine Stop-Requests durch Esc senden.
- Fertig wenn Maus und Tastatur dieselben Actions, Empfänger und Netzwerkbefehle auslösen und Konflikte bei Mehrfachselektion eindeutig behandelt werden.

### 05 – Ereignismeldungen für den Spieler einführen

- [ ] Offen. Priorität: hoch. Voraussetzung für 06.
- Lokalen Ereignisspeicher mit Typ, Zeitpunkt, Position, optionaler UnitId, Text und Priorität einführen. Präsentation von Kampf-, Produktions- und Stromlogik trennen; Ereignisse an bestätigten Zustandsänderungen ableiten.
- Erste Ereignisse: eigene Unit/eigenes Gebäude wird angegriffen, eigene Unit/eigenes Gebäude zerstört, Produktion abgeschlossen und Stromversorgung ausgefallen/wiederhergestellt.
- Angriffsmeldungen nicht für jeden Treffer erzeugen: nahe Angriffe zu einer Meldung zusammenfassen und Wiederholungen zeitlich begrenzen. Startvorschlag: räumliche Gruppierung und fünf Sekunden Meldungspause; große unabhängige Angriffe getrennt melden.
- Meldungen müssen auch beim besitzenden Client ankommen, nicht nur beim Host. Vorhandene replizierte Kampf-/Zustands-/Produktionsbefehle nutzen; nur bei fehlender Information einen zusätzlichen Host-Event vorsehen. Eigene Trefferposition darf gemeldet werden, aber unsichtbare Angreifer nicht mit Identität/Position verraten.
- Begrenzte Historie, z.B. 100 Einträge. Bei game-start und Sessionwechsel leeren; später beitretende Spieler erhalten keine alten Meldungsfluten. Allied/Spectator-/Editor-Verhalten ausdrücklich definieren.
- Fertig wenn Meldungen im HUD erscheinen, Multiplayer-Besitzer sie zuverlässig sehen und Dauerfeuer keine Meldungsflut erzeugt.

### 06 – Ereignisse per Klick und Leertaste anspringen

- [ ] Offen. Priorität: mittel. Nach 05; bestehende Leertastenbelegung vorher prüfen.
- Klick auf eine Meldung zentriert die Kamera an ihrer Position. Leertaste springt zum neuesten relevanten Ereignis; weitere getrennte Tastendrücke innerhalb eines kurzen Fensters gehen durch die jüngeren Ereignisse.
- Position zum Ereigniszeitpunkt speichern, damit zerstörte/entfernte Units weiterhin angesprungen werden können. Kamerasprung allein ändert die Selektion nicht.
- Kurzer Marker an der Meldungsposition, unter Beachtung der Sichtbarkeitsregeln. Konsole/Fokus, leere Historie und abgelaufene Ereignisse behandeln.
- Fertig wenn Klick und Tastatur zum selben Ort führen und Halten keine Sprungfolge auslöst.

### 07 – Selektionsübersicht und Squad-Zugriff

- [ ] Offen. Priorität: mittel. Nach 03; unabhängig von 05/06.
- Kompakte Typübersicht der aktuellen Selektion mit Symbol, Anzahl und Health-Zusammenfassung. Basic/Detailed-Health und entsprechende Perks respektieren.
- Klick auf einen Typ filtert die aktuelle Selektion auf diesen Typ; Shift ergänzt/entfernt nach einer einheitlichen Regel. Das ergänzt die weltweite Umkreis-Selektion aus 03.
- Squad-Leader in der Übersicht kennzeichnen und einen direkten Zugriff anbieten. Keine automatische Umleitung jeder Mitgliedsselektion auf den Leader.
- Tab als optionalen Vorschlag prüfen: durch selektierte Units bzw. Typen wechseln. Verbindliche Semantik vor Umsetzung festlegen; keine zufällige Änderung der gesamten Gruppenselektion.
- Fertig wenn gemischte Gruppen verständlich sind und Leader ohne Suche im Weltbild ausgewählt werden können.

### 08 – Aktuellen Befehl und Befehlswarteschlange verständlich anzeigen

- [ ] Offen. Priorität: mittel. Nach 04.
- Aktive Zielwahl deutlich im Panel und am Cursor anzeigen, z.B. „Angriff: Ziel auswählen“. Aktive Action markieren; kontextabhängigen Vorschlag davon unterscheidbar machen.
- Für ausgewählte eigene Units optional nummerierte Weg-/Befehlsziele anzeigen, besonders für Shift-Queues. Hostbestätigte Ziele von noch ausstehenden Requests unterscheiden, ohne interne Details im normalen HUD anzuzeigen.
- Eine kleine Statuszeile für eine einzelne Unit ergänzen: Bewegen, Ernten, Zurückkehren, Abladen, Bauen, Warten. Vorhandene Debug-Befehlsanzeige nicht durch eine zweite Gameplay-Zustandsmaschine ersetzen.
- Fertig wenn der Spieler erkennt, welcher Befehl gerade aktiv ist und was anschließend folgt.

### 09 – Produktionsübersicht als Navigation und Abbruchbedienung nutzen

- [ ] Offen. Priorität: mittel. Keine Abhängigkeit.
- Klick auf einen Produktionseintrag springt zum Produzenten; eine getrennte eindeutige Interaktion selektiert ihn.
- Anzahl wartender Aufträge und pausierte Produktion verständlich anzeigen, z.B. fehlender Strom. Bestehende Übersicht erweitern.
- Produktionsabbruch nur anbieten, wenn Host-Befehl und Erstattungsregel vorhanden bzw. ausdrücklich ergänzt sind. Keine Ressourcenmutation allein im HUD. Achtung: Gebäude-Bauabbruch und Produktionsabbruch sind unterschiedliche Vorgänge.
- Fertig wenn Navigation funktioniert und eventuelle Abbrüche dieselben Regeln auf Host und Clients verwenden.

## Weitere Ideen für später

- Minimap-Pings für Angriffe und später einen bewusst vom Spieler ausgelösten Team-Ping. Teilen benötigt einen eigenen autorisierten Netzwerkbefehl; keine zusätzlichen Gegnerinformationen übertragen.
- „Auswahl zurück“: letzte sinnvolle Selektion wiederherstellen, z.B. nach dem Blick auf eine Alarmmeldung.
- Hotkey-Hilfe als eingeblendete Übersicht der aktuell verfügbaren Befehle; später frei belegbare Tasten.
- Kurzes Feedback bei abgelehntem Befehl: fehlende Ressourcen, fehlender Perk, ungültige Position. Vorhandene Host-Ablehnungsgründe für den eigenen Spieler nutzbar machen.
- Kleine Gruppenleiste für 0–9 mit Anzahl und markierter aktueller Gruppe; tote Mitglieder entfernen, ohne verbleibende Gruppe zu verlieren.
- Optional „untätigen Bulldozer auswählen“ als Shortcut. Erst festlegen, welche Zustände als untätig gelten, damit autonome Bau-/Ernteabläufe nicht unterbrochen werden.

## Abgrenzung

Kein vollständiger HUD-Neubau auf einmal. Keine globalen Produktions-Hotkeys ohne eindeutigen Produzenten. Keine neuen Befehle an fremde Units durch die Debug-Selektion. Sichtbarkeits- und Health-Informationsregeln bleiben auch in Übersicht, Markern und Meldungen gültig.
