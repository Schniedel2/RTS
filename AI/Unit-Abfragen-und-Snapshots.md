# Unit-Abfragen und Snapshot-Lebensdauer

Architekturpunkt 10, umgesetzt am 01.10.2026.

## Abfragen und Aufrufer

`UnitHandler` besitzt die Mitgliedschaft seiner Welt. Zuvor erzeugte jeder Zugriff auf `Units` eine neue Array-Kopie; auch einzelne ID-Suchen kopierten dadurch die gesamte Liste. Die Bestandsaufnahme unterscheidet folgende Verbraucher:

| Verbraucher | Aktueller Zugriff |
|---|---|
| Einzelne Unit / MobileUnit anhand Netzwerk-ID | `FindById` / `FindMobileUnitById`, Dictionary-Zugriff |
| Eigene KI-Armee, Strombilanz und Ressourcenlager | `GetArmyUnits(armyId)`, gecachter Army-Snapshot |
| Gegnerbewertung, Rendering, Sichtbarkeit, UI und Editor | `Units` als kompatibler Zugang zu `GetSnapshot()` |
| Kampf- und Netzwerk-Auswertungsphasen | Ein gemeinsamer Welt-Snapshot innerhalb der Phase |

44 bekannte eigene-Army-Abfragen der KI verwenden den gezielten Army-Zugang. Zusätzliche Filter auf Betriebsbereitschaft, Tod oder Embark-Zustand bleiben beim jeweiligen Verbraucher. Sortierte oder gefilterte Arbeitslisten dürfen weiterhin eigene Arrays benötigen; eine solche Liste ist nicht bloß eine Mitgliedschaftskopie.

## Vertrag

- `GetSnapshot()` und `Units` liefern denselben gecachten, schreibgeschützten Mitgliedschaftsstand, bis eine Unit hinzugefügt, ersetzt oder entfernt wird.
- `GetArmyUnits(Guid)` liefert einen schreibgeschützten Mitgliedschaftsstand in derselben Reihenfolge wie die Weltliste. Nur betroffene Army-Caches werden bei Änderungen verworfen. Unbekannte Armies erhalten eine gemeinsame leere Ansicht.
- `FindById` verwendet den ID-Index; `FindMobileUnitById` prüft zusätzlich den Typ. `Count` benötigt keine Kopie. `MembershipRevision` steigt bei Mitgliedschafts- und Army-Wechseln.
- Ein Snapshot friert **nur die Mitgliedschaft** ein. Die enthaltenen Unit-Instanzen und deren HP, Position, ArmyId usw. bleiben lebende Objekte. Ein alter Army-Snapshot kann deshalb eine inzwischen übernommene Unit enthalten.
- Während einer Iteration darf die Welt Units hinzufügen oder entfernen; der bereits bezogene Snapshot bleibt sicher aufzählbar. Für eine neue Auswertung wird erneut abgefragt. Nach verzögerter Planung oder anderen asynchronen Grenzen müssen Existenz, Befehlsversion und Berechtigung erneut geprüft werden.
- Welt- und Unit-Zustandsänderungen bleiben auf dem Spielthread. Die Sperre um Mitgliedschaft und Indizes macht die gesamten Unit-Objekte nicht threadsicher.

## Lebenszyklus und Konsistenz

Eine private Collection führt sämtliche vorhandenen Add-/Remove-/Clear-Pfade durch gemeinsame Registrierung. Nach außen wird keine veränderbare Liste und kein direkt veränderbares Array ausgegeben.

| Ereignis | Pflege |
|---|---|
| Spawn / Aufnahme | ID- und Army-Index ergänzen, ArmyChanged abonnieren, Snapshots entwerten |
| Army-Wechsel, Übernahme oder Neutralisierung | `Unit.SetArmy` meldet den Wechsel; beide Army-Indizes und Caches werden sofort aktualisiert |
| Zerstörung | Die sterbende Unit bleibt während ihrer Darstellung registriert; Verbraucher filtern `IsDying` nach Bedarf |
| Endgültiges Entfernen / Ersetzen | Indizes bereinigen und ArmyChanged abmelden |
| Session-Snapshot / Matchreset | Collection, Indizes und Caches leeren, alle Abonnements entfernen |
| Embark / Disembark | Embarkte Units bleiben registriert; Army-Wechsel aktualisieren den Index, Embark-Filter bleiben fachliche Regeln |

Doppelte registrierte Objekte oder gültige IDs werden abgewiesen. Spawn-Einstiegspunkte prüfen vorhandene IDs vor Grid-Änderungen. Der Gebäude-Fallback von `SpawnUnit` gibt direkt das Ergebnis von `SpawnBuilding` zurück und registriert es damit genau einmal. Für ältere uninitialisierte Test-Fixtures mit `Guid.Empty` bleibt die bestehende lineare Suche erhalten; reguläre Netzwerk-IDs werden indiziert.

## Prüfung und Messung

Build ohne Warnungen/Fehler; 905 Checks bestanden gegenüber 871 vor diesem Schritt. 34 zusätzliche Checks prüfen unveränderbare und wiederverwendete Ansichten, Reihenfolge, Aufnahme/Entfernung während Iteration, Army-Wechsel, neutralisierte und wiederbesetzte echte Fahrzeuge, doppelte IDs, Gebäude-Fallback, Tod/Entfernung, Session-/Matchreset, abgemeldete Objekte und getrennte Welten.

Die [Vergleichsmessung](Unit-Abfragen-Messung.md) verwendet im selben Prozess dieselben 512 Units, zwei Armies und 200 Auswertungen mit je 16 ID-Abfragen. Das bisherige Verfahren wird nachgebildet und beide Abläufe werden aufgewärmt. Beide liefern dieselbe Prüfsumme. Allokationen im stationären Messfenster sinken von 15.139.200 auf 0 Bytes; gemessene Laufzeiten betragen 8,462 bzw. 0,185 ms. Dies ist keine FPS-Prognose für eine vollständige Schlacht.

Reproduzieren vom Repository-Verzeichnis:

```powershell
dotnet build RTS.csproj
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj -c Release -- --unit-query-report
```

Bei geänderter Mitgliedschaft kosten Snapshot-Aufbau und Indexpflege weiterhin Arbeit; diese Kosten liegen außerhalb der stationären Messung. Globale Snapshot-Erstellung bleibt O(N). Ein räumlicher Index wurde ohne gesonderten Bedarfsnachweis nicht eingeführt. Bestehende Reflection-Fixtures wurden an die private Collection angepasst; ihr regulärer grafikfreier Ersatz bleibt Architekturpunkt 11. Die offene optische Fahrzeugabnahme aus Punkt 02 bleibt offen.
