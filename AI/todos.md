RTS-Spiel:
Dies soll zuerst nur eine einfache RTS-Multiplayer-Engine werden.
- Aktionen werden in einer ActionList eingehängt - die ActionList kann dann über die Clients gesynced werden damit das Spiel synchron läuft
- Alle Einheiten/Gebäude werden zusätzlich regelmässig gesynced (aber nicht pro Frame!)

1.  MonoGame-Grundgerüst -> done
2.  Spielfeld / Grid -> done
3.  Kamera + Scrollen -> done
3.1 Tilemap
4.  Maussteuerung
5.  Terrain
6.  Einheiten
6.1. Einheiten spawnen
6.2. Spawnen über Actionlist antriggern
6.3. später: Actionlist über merhere Clients syncen
7.  Einheiten auswählen
8.  Move-Befehl (über Actionlist)
9.  Pathfinding
10. Gebäude
11. Ressourcen
12. Gegner / KI
13. Combat
14. UI
15. Sound
16. Save/Load