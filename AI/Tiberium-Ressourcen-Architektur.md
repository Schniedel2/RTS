# Tiberium-/Ressourcen-Architektur

Stand: Entwurfsentscheidung; noch nicht implementiert.

## Grundidee

Wie bei C&C: eine **Tiberiumpflanze** (`TiberiumSource`) breitet Tiberium in ihrer
Umgebung aus. Das Tiberium selbst wächst pro Terrain-Zelle in Stufen und kann von
Harvester-Einheiten geerntet werden. Geerntetes Tiberium verschwindet von der Karte,
kann aber später an derselben Zelle neu nachwachsen, solange die zugehörige
`TiberiumSource` weiterhin existiert (sie selbst ist nicht erntbar/verbraucht sich
nicht).

## Warum keine volle `Unit`/`Building`-Instanz pro Tiberium-Zelle

`Unit` ([src/WorldObjects/Units/Unit.cs](../src/WorldObjects/Units/Unit.cs)) ist auf
Grid-Occupancy, Netzwerk-State-Sync, Factories und Selektierbarkeit ausgelegt — das ist
für Gebäude/Einheiten richtig, aber für potenziell hunderte wachsende Tiberium-Zellen
pro Karte massiver Overhead. Als Vorbild dient stattdessen
[`DecalHandler`](../src/GameWorld/DecalHandler.cs): ein schlanker Handler, der viele
kleine, überwiegend lokale Objekte mit eigenem `Update`/`Draw` verwaltet, ohne
Grid-Occupancy und ohne vollen Unit-Overhead.

## Bausteine

1. **`TiberiumSource` (die Pflanze) → als `Building`**
   Echtes Gameplay-Objekt: platzierbar, zerstörbar, blockt eine Grid-Zelle, ist ein
   gültiges Angriffsziel. Passt in das bestehende Building-System
   (`BuildingFactory`, `UnitHandler`). Tickt periodisch und lässt Tiberium in ihrem
   Radius wachsen/sich ausbreiten. Sie selbst wird nicht abgebaut/geerntet.

2. **Ressourcendaten pro Zelle → sparse, parallel zu `GridCell`**
   Nicht `GridCell` ([src/GameWorld/GridCell.cs](../src/GameWorld/GridCell.cs)) selbst
   aufblähen, sondern eine eigene, sparse Struktur (die meisten Zellen haben kein
   Tiberium):
   ```csharp
   public sealed class TiberiumCell
   {
       public float Amount { get; set; }     // Ressourcenmenge zum Ernten
       public double GrowthBaselineTime { get; set; } // lokale Zeitbasis, s.u.
   }
   ```
   verwaltet in `Dictionary<Point, TiberiumCell>` statt vollem 2D-Array.

3. **`TiberiumHandler` (in `GameWorld/`) → analog zu `DecalHandler`**
   - Hält die `Dictionary<Point, TiberiumCell>`.
   - Simuliert Wachstum/Ausbreitung, geprüft gegen `GameGrid.GetCell(...).IsBlocked`/
     `HasTerrain`, damit sich Tiberium nicht über Gebäude, Wasser oder steile Hänge
     ausbreitet.
   - Zeichnet pro Zelle das `tiberium-1`-Mesh ([`MeshHandler`](../src/Handlers/MeshHandler.cs)),
     leicht randomisiert nach Wachstumsstufe für visuelle Varianz.
   - `TryHarvest(Point cell, float amount)`: reduziert `Amount`; bei Erschöpfung
     verschwindet die Zelle komplett von der Karte (kein Mesh mehr, kein Eintrag).
   - Erschöpfte Zellen bleiben nachwachsfähig, solange die erzeugende `TiberiumSource`
     noch existiert (sie triggert erneutes Wachstum in ihrem Radius).

4. **Reine Deko-Pflanzen (kein Gameplay)**
   Komplett wie Decals behandeln: eigener `PlantHandler` (oder Erweiterung von
   `DecalHandler`), rein lokal, nicht genetzwerkt, keine Grid-Interaktion.

## Wachstumsstufen: Vorhersage statt Polling

Wachstumsstufe ist rein kosmetisch (nur fürs Rendering relevant); die tatsächliche
Erntemenge bestimmt weiterhin ausschließlich der Host. Deshalb muss die Wachstumsstufe
nicht laufend synchronisiert werden, sondern kann pro Client lokal vorausberechnet
werden:

- **Keine synchronisierte Absoluzeit nötig.** `gameTime.TotalGameTime` läuft bei jedem
  Client ab dem eigenen Prozessstart und ist zwischen Host und Clients nicht
  vergleichbar (wird im Code aktuell nur für rein lokale Cooldowns genutzt, nie über
  das Netzwerk verglichen; das `ServerTime`-Feld in
  [`NetworkMessage.cs`](../src/Network/NetworkMessage.cs) wird für Projektile aktuell
  nicht ausgewertet).
- Stattdessen überträgt der Host bei Erzeugung/Rebase einer Zelle nur einen Skalar:
  **`elapsed`** (Sekunden seit „virtuellem" Wachstumsstart). Der Client setzt daraus
  seinen eigenen lokalen Referenzpunkt:
  `localCreatedAt = gameTime.TotalGameTime - elapsed`.
  Ab da rechnet jeder Client unabhängig mit seiner eigenen Uhr weiter:
  `stage = GrowthStageFor(gameTime.TotalGameTime - localCreatedAt)`.
- `GrowthStageFor` nutzt eine gemeinsame, statische Schwellenwert-Tabelle
  (z. B. `float[] StageThresholds = [0, 30, 90, 180]`), identisch in Host- und
  Client-Code. Es werden **keine** vorausberechneten Zukunfts-Timestamps pro Stufe
  übertragen/gespeichert — nur der eine `elapsed`-Wert, der Rest wird bei Bedarf
  (z. B. beim Draw) neu berechnet.
- **Late Joiner:** Der initiale State-Snapshot muss pro existierender Zelle das
  aktuelle `elapsed` mitschicken, sonst starten alle Zellen beim Client visuell wieder
  bei Stufe 0.
- **Rebase bei Ernte:** Wird eine Zelle angeerntet und die Menge/Wachstumsstufe sinkt,
  muss der Host ein neues (kleineres) `elapsed` nachschicken — der Client kann das
  nicht selbst herleiten, da er den Ernteertrag nicht kennt.
- **Rebase bei Neuwachstum:** Wächst eine erschöpfte Zelle später neu (ausgelöst durch
  die weiterhin existierende `TiberiumSource`), ist das technisch eine neue Zelle mit
  neuem `elapsed = 0`.
- Vorteil ggü. periodischem State-Sync (wie `Building.ConstructionProgress` über
  `StateHeartbeatInterval`, siehe [`NetworkHost.cs`](../src/Network/NetworkHost.cs)):
  nur **eine** Nachricht pro Zelle bei Erzeugung/Ernte/Neuwachstum nötig, da der
  Wachstumsverlauf vollständig deterministisch/zeitbasiert ist — kein fortlaufendes
  Sync-Polling erforderlich.

## Multiplayer/Netzwerk allgemein

Wachstum/Ausbreitung wird hostautoritativ simuliert (analog zu `Earthwork`, das über
Orders/Preview läuft). An Clients werden nur kompakte Deltas gesendet (geänderte
Zellen, neue Menge, `elapsed`) — nicht jede Tiberium-Zelle als einzelnes
Netzwerkobjekt.
