RTS-Spiel:
Dies soll zuerst nur eine einfache RTS-Multiplayer-Engine werden.
- Aktionen werden in einer ActionList eingehängt - die ActionList kann dann über die Clients gesynced werden damit das Spiel synchron läuft
- Alle Einheiten/Gebäude werden zusätzlich regelmässig gesynced (aber nicht pro Frame!)

1.  MonoGame-Grundgerüst -> done
2.  Spielfeld / Grid -> done
3.  Kamera + Scrollen -> done
3.1 Tilemap -> done
4.  Maussteuerung -> done (muss noch verbessert werden))
5.  Terrain -> done
6.  Einheiten -> done
6.1. Einheiten spawnen -> done
6.2. Spawnen über Actionlist antriggern -> done (muss noch verbessert werden + Shortcuts)
6.3. später: Actionlist über merhere Clients syncen -> done
7.  Einheiten auswählen -> done
8.  Move-Befehl (über Actionlist) -> done
9.  Pathfinding -> done
10. Gebäude -> done
11. Ressourcen
12. Gegner / KI
13. Combat -> done
14. UI -> done
15. Sound
16. Save/Load


Ideen:
Ressource: wie Tiberium (C&C) + Man kann Tiberumpflanzen ausgraben und woanders wieder eingraben

Beim Bauen: Gebäude drehen und Vorschau (trasnparent) anzeigen
Gebäude müssen den Boden/Zellen blocken
zerstörte Gebäude müssen den Boden/Zellen freigeben