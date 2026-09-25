RTS-Spiel:
Dies soll zuerst nur eine einfache RTS-Multiplayer-Engine werden.
- Aktionen werden in einer ActionList eingehängt - die ActionList kann dann über die Clients gesynced werden damit das Spiel synchron läuft
- Alle Einheiten/Gebäude werden zusätzlich regelmässig gesynced (aber nicht pro Frame!)

1.  MonoGame-Grundgerüst -> done
2.  Spielfeld / Grid -> done
3.  Kamera + Scrollen -> done
3.1 Tilemap -> done
4.  Maussteuerung -> done (muss noch verbessert werden)
5.  Terrain -> done
6.  Einheiten -> done
6.1. Einheiten spawnen -> done
6.2. Spawnen über Actionlist antriggern -> done (muss noch verbessert werden + Shortcuts)
6.3. später: Actionlist über merhere Clients syncen -> done
7.  Einheiten auswählen -> done
8.  Move-Befehl (über Actionlist) -> done
9.  Pathfinding -> done
10. Gebäude -> done
11. Ressourcen -> done (buggy)
12. Gegner / KI
13. Combat -> done
14. UI -> done
15. Sound
16. Save/Load


Ideen:
Ressource: wie Tiberium (C&C) + Man kann Tiberumpflanzen ausgraben und woanders wieder eingraben

Beim Bauen: Gebäude drehen und Vorschau (transparent) anzeigen -> done
Gebäude müssen den Boden/Zellen blocken -> done
zerstörte Gebäude müssen den Boden/Zellen freigeben -> done
beim Platzieren: pivot:exit anzeigen - evtl. Pfeil von "pivot:spawn" -> "pivot:exit"
Soldiers: AutoAngriff nur auf sichtbare Ziele
Soldiers: Medic -> heilt verwundete Soldaten automatisch im Bereich -> bewegt sich zu den Patienten
Soldiers: Truppenführer einbauen mit Truppbefehlen wie "verteilen", "antreten" + Formationen
Truppenführer hat nur Pistole und steht auch hinten bei Formationen
Truppenführer: allokiert "ungeführte Soldaten" im Umkreis per Action
Truppenführer: Formationen z.B. MG vorne und Sniper/RPG/Medic hinten 
Truppenführer: bei Goto muss eine Ausrichtung angegeben werden (für die Formation)
Geschütztürme
  - begrenz Munition (?) -> Updates mehr Ammo
  - auto reload - speed über Upgrades
  - update: can target sky-units (?)
