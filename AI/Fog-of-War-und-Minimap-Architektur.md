# Fog of War, Minimap und Aufklärungsdaten

Der Host bleibt autoritativ für Weltzustand, Bewegung und Kampf und verteilt weiterhin alle Units. Jeder Client berechnet die Sicht aller Armeen deterministisch aus Grid-Zellen und `SightRange`. Fog of War filtert lokal Darstellung, Auswahl, Schatten und Minimap. Ein Gegner muss beim Betreten oder Verlassen des Sichtfelds daher nicht eigens über das Netzwerk gespawnt oder entfernt werden.

Pro Armee hält ein `VisibilityGrid` für jede Zelle `Unexplored`, `Explored` oder `Visible`. Frühere Sicht bleibt als `Explored` erhalten. Eigene und verbündete Sicht werden getrennt ausgewertet; `CellVisibility` kann daher zugleich `Explored`, `Visible`, `ExploredByAlly` und `VisibleByAlly` enthalten.

`IntelligenceCapabilities` steuert unabhängig:

- `ShareExploredMinimap`: erkundetes Terrain verbündeter Armeen auf der Minimap
- `ShareVisibleMinimap`: aktuelle Ally-Sicht und Kontakte auf der Minimap
- `ShareWorldVision`: aktuelle Ally-Sicht auch in der normalen Weltansicht

Die Minimap ist eine Darstellung eines Intelligence Picture und greift nicht direkt ungefiltert auf Units zu. Kontakte tragen langfristig eine Quelle: `OwnVision`, `AlliedVision`, `Radar`, `EnemyNetwork`, `LastKnown` oder `Deception`. So kann ein Spion nach einem erfolgreichen HQ-Hack gegnerische Minimapdaten als `EnemyNetwork` einspeisen. Fälschungen werden als Kontakte ohne echte `UnitId` abgelegt, können altern und verschwinden, sobald eigene Sicht ihre Position überprüft. Sie erzeugen keine Weltobjekte und sind nicht angreifbar.

Der erste Stand berechnet kreisförmige Sicht ohne Sichtlinienblockierung. Später können Höhen, Gebäude, Wald, Radar, Tarnung, zeitlich begrenzte Hacks, Konfidenz und veraltete Kontakte ergänzt werden, ohne das Grundmodell zu ändern.
