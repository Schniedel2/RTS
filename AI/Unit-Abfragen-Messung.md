# Unit-Abfragen: Vergleichsmessung

UTC: 2026-10-01T21:13:19.3253502Z

Runtime: .NET 10.0.9; Microsoft Windows 10.0.19045; X64.

Szenario: 512 reguläre grafikfreie Units, zwei Armies, 200 Auswertungen; je Auswertung ein Welt-Snapshot, 16 ID-Abfragen und eine Army-Abfrage. Gleiche Reihenfolge, IDs und Ergebnis-Prüfsumme; beide vollständigen Abläufe vorher aufgewärmt. Keine Mitgliedschaftsänderungen innerhalb des Messfensters.

| Verfahren | Allokationen auf dem Aufrufthread | Laufzeit | Ergebnis |
|---|---:|---:|---:|
| Bisher: ToArray je Snapshot/ID-/Army-Abfrage | 15.139.200 Bytes | 8,462 ms | 156800 |
| Jetzt: gecachter Snapshot, ID-Index, Army-Snapshot | 0 Bytes | 0,185 ms | 156800 |

Das bisherige Verfahren wird im selben Prozess nachgebildet; kein Vergleich unterschiedlicher Rechner oder Spielstände. Snapshot-Aufbau und Indexpflege bei Spawn/Army-Wechsel sind nicht kostenlos und liegen außerhalb dieser stationären Messung. Sichere Mitgliedschaft, Wechsel und Lebenszyklus werden zusätzlich durch Verhaltenstests geprüft. Zeitwerte sind Einzelmessungen ohne Echtzeitgarantie; dies ist kein FPS- oder vollständiger KI-Schlachtbenchmark.
