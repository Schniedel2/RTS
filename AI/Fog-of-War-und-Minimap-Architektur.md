# Fog of War, Minimap und Aufklärungsdaten

Der Host bleibt autoritativ für Weltzustand, Bewegung und Kampf und verteilt weiterhin alle Units. Jeder Client berechnet die Sicht aller Armeen deterministisch aus Grid-Zellen und `SightRange`. Fog of War filtert lokal Darstellung, Auswahl, Schatten und Minimap. Ein Gegner muss beim Betreten oder Verlassen des Sichtfelds daher nicht eigens über das Netzwerk gespawnt oder entfernt werden.

Pro Armee hält ein `VisibilityGrid` für jede Zelle `Unexplored`, `Explored` oder `Visible`. Frühere Sicht bleibt als `Explored` erhalten. Eigene und verbündete Sicht werden getrennt ausgewertet; `CellVisibility` kann daher zugleich `Explored`, `Visible`, `ExploredByAlly` und `VisibleByAlly` enthalten.

`IntelligenceCapabilities` steuert unabhängig:

- `ShareExploredMinimap`: erkundetes Terrain verbündeter Armeen auf der Minimap
- `ShareVisibleMinimap`: aktuelle Ally-Sicht und Kontakte auf der Minimap
- `ShareWorldVision`: aktuelle Ally-Sicht auch in der normalen Weltansicht

Die Minimap ist eine Darstellung eines Intelligence Picture und greift nicht direkt ungefiltert auf Units zu. Kontakte tragen langfristig eine Quelle: `OwnVision`, `AlliedVision`, `Radar`, `EnemyNetwork`, `LastKnown` oder `Deception`. So kann ein Spion nach einem erfolgreichen HQ-Hack gegnerische Minimapdaten als `EnemyNetwork` einspeisen. Fälschungen werden als Kontakte ohne echte `UnitId` abgelegt, können altern und verschwinden, sobald eigene Sicht ihre Position überprüft. Sie erzeugen keine Weltobjekte und sind nicht angreifbar.

Der erste Stand berechnet kreisförmige Sicht ohne Sichtlinienblockierung. Später können Höhen, Gebäude, Wald, Radar, Tarnung, zeitlich begrenzte Hacks, Konfidenz und veraltete Kontakte ergänzt werden, ohne das Grundmodell zu ändern.

## Geplant: Aufklärungsgedächtnis und weiche Nebelrückkehr

Vereinbarte Spielidee vom 01.10.2026, noch nicht implementiert. Offener Umsetzungspunkt in [Gameplay-Ausbau-TODO.md](Gameplay-Ausbau-TODO.md), Phase 3.

Scouting soll während der ganzen Partie wertvoll bleiben: Ein Spieler erinnert sich an ein entdecktes Lager, muss aber erneut aufklären, um Änderungen zu erfahren. Erkundetes Terrain bleibt wie bisher erkundet; es wird beim Verlust aktueller Sicht nicht wieder schwarz/unbekannt.

### Vereinbartes Grundverhalten

- Gegnerische Gebäude bleiben nach Sichtverlust als abgedunkelte Darstellung ihres zuletzt gesehenen Zustands in Weltansicht und Minimap erhalten. Es handelt sich um veraltetes Wissen, nicht um ein weiterhin sichtbares echtes Gebäude.
- Außerhalb aktueller Sicht werden weder Gebäudezustand noch HP, Baufortschritt, Besitzerwechsel, Verkauf oder Zerstörung nachgeführt. Ein verborgen zerstörtes Gebäude bleibt deshalb als Erinnerung bestehen, bis sein Standort erneut aufgeklärt wird. Verdeckte Neubauten erscheinen nicht.
- Beim erneuten Aufklären werden Erinnerungen aktualisiert beziehungsweise entfernt, wenn das Gebäude dort nicht mehr vorhanden ist.
- Mobile Gegner verschwinden nach Verlust aktueller Sicht. Dauerhafte mobile Erinnerungen sind für diesen ersten Schritt nicht vorgesehen.
- Der Nebel soll innerhalb weniger Sekunden weich zurückkehren. Die genaue Dauer ist noch zu testen. Der visuelle Übergang darf nicht stillschweigend die logische Sicht verlängern oder neue verdeckte Bewegungen, Gebäude und Partikeleffekte zeigen. Automatische Zielwahl und Angriffe verwenden weiterhin echte aktuelle Sicht.

120 Sekunden vollständige Sicht nach einem Scout-Durchflug wurden als zu stark verworfen. Ein Scout soll nicht nach seinem Tod noch dauerhaft neue Informationen liefern. Eine längere echte Überwachung kann später eine bewusste, zeitlich begrenzte Fähigkeit oder ein Aufklärungs-Perk sein; Dauer, Kosten, Quelle und Verlustbedingungen bleiben dann eigene Balancingentscheidungen.

### Technische Leitplanken für die spätere Umsetzung

Das Intelligence Picture bekommt pro Army gespeicherte Gebäude-Kontakte mit Quelle `LastKnown` und Zeitpunkt der letzten Beobachtung. Darstellung und Auswahl dürfen hierfür nicht einfach das aktuelle, überall replizierte Gebäude lesen: Benötigte Darstellungsdaten wie Typ, Position, Ausrichtung und zuletzt beobachteter Zustand werden bei zulässiger Sicht festgehalten. Eine Erinnerung ist kein zusätzliches Weltobjekt, belegt keine Grid-Zellen und ist kein aktuell sichtbares Angriffsziel.

Eigene Sicht, freigegebene Ally-Sicht und gegebenenfalls spätere Überwachungsquellen bleiben unterscheidbar. Die vorhandenen Freigaben für Weltansicht und Minimap müssen respektiert werden; Wissen darf nicht automatisch zwischen allen Armeen geteilt werden. Die KI sollte für strategische Entscheidungen dieselben zulässigen aktuellen und gespeicherten Informationen verwenden können.

Ein neuer `game-start` setzt die Erinnerungen und Übergangszeiten zurück. Für Late Join muss ausdrücklich entschieden und geprüft werden, wie das bereits erworbene Army-Wissen rekonstruiert beziehungsweise übertragen wird; versteckte aktuelle Weltinformationen sind kein Ersatz für den beobachteten Altzustand.

### Abnahme und offene Details

Prüfszenario: Scout entdeckt ein Lager und verlässt es beziehungsweise stirbt. Gebäude bleiben dunkel als Erinnerung, mobile Gegner verschwinden. Der Gegner baut um oder zerstört ein Gebäude außerhalb der Sicht; die Erinnerung bleibt unverändert. Beim nächsten Besuch wird sie korrigiert. Wiederholen mit Ally-Freigaben, mehreren Clients, Late Join und Matchneustart.

Noch offen: genaue Dauer/Kurve des visuellen Übergangs, visuelle Kennzeichnung und Tooltip für veraltetes Wissen, sowie die Ausgestaltung späterer Überwachungs-Perks. Diese Notiz beauftragt noch keine Implementierung.
