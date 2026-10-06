# KI-Verhaltensprofile (Schema 1)

Die bestehende Host-KI verwendet die vier JSON-Dateien in `Config/AI/Profiles`.
Der Build und Publish kopieren sie nach `<Ausgabeverzeichnis>/Config/AI/Profiles`.
Zur Laufzeit zählt ausschließlich dieser Pfad relativ zu `AppContext.BaseDirectory`, nicht das Arbeitsverzeichnis.
Für dauerhafte Änderungen die Quelldateien bearbeiten und erneut bauen; Änderungen direkt im Ausgabeverzeichnis gelten beim nächsten Prozessstart.

Die Dateien werden einmal pro Prozess geladen. Ein Match erhält ein unveränderliches `AIStrategyProfile`.
Kein Datei-/JSON-Zugriff im Update, kein Hot-Reload. Profilwahl und Seed aus Match-Seed und ArmyId bleiben unverändert (40/25/25/10 Prozent).
Die Beispieldateien enthalten genau die bisherigen Werte. Preise, Strom, Perks und Produzenten bleiben im GameplayCatalog.

| Feld | Bedeutung / erlaubte Werte |
|---|---|
| schemaVersion | Ganzzahl, derzeit genau 1 |
| profileId | Feste ID passend zu type, siehe unten |
| type | BalancedAssault, InfantryCompany, AntiArmor oder FastRecon |
| displayName | Nicht leer/blank, maximal 80 Zeichen |
| requiredGunners | Ganzzahl 0..8 |
| requiredRakZero | Ganzzahl 0..8; zusammen mit requiredGunners höchstens 8 (Platz für Leader und Medic) |
| requiredTanks | Ganzzahl 0..20 |
| retreatHealthFraction | Verhältnis 0..1 |
| attackReadinessSeconds | Simulationssekunden 0..600 |
| defenseRadiusInCells | Grid-Zellen 1..256 |
| assaultStallTimeoutSeconds | Simulationssekunden 1..600 |

Feste IDs: `balanced-assault` / BalancedAssault, `infantry-company` / InfantryCompany,
`anti-armor` / AntiArmor und `fast-recon` / FastRecon. Dateinamen sind frei; pro Type darf nur eine Datei vorkommen.
Alle oben aufgelisteten Felder sind erforderlich; die drei später ergänzten Verhaltensfelder sind optional. Feldnamen verwenden exakt camelCase. Zahlen müssen endlich sein.
Unbekannte Felder, doppelte Felder, null, numerische/ungültige Enums, falsche Versionen und Werte außerhalb der Grenzen werden abgelehnt.
Zum Beispiel ergibt `"retreatHealthFraction": 1.5` einen Fehler mit Dateipfad und `$.retreatHealthFraction`.

Der automatisch verwendete Laufzeitpfad ist optional: fehlt das Verzeichnis oder eine einzelne Profil-Datei, gelten dafür die eingebauten Originalwerte.
Eine vorhandene ungültige Datei wird auch im optionalen Modus abgelehnt, niemals still durch Defaults ersetzt.
`AIProfileCatalog.LoadDirectory(path)` ist der explizite strikte Loader: Verzeichnis und alle vier Profile müssen vorhanden und gültig sein.
Ein Katalog wird erst nach erfolgreicher Prüfung aller Dateien veröffentlicht; vorhandene Kataloge bleiben bei Fehlern unverändert.
`game-start` prüft vor dem Welt-Reset die Profile und meldet Fehler in der Konsole. Nach einer Korrektur neu starten, da auch das Ergebnis des einmaligen Ladevorgangs pro Prozess stabil bleibt.

Feste Profilwahl, einstellbare Auswahlgewichte, zusätzliche Verhaltensfelder und getrennte Rechenbudgets sind in [KI-Konfiguration](../README.md) beschrieben. Remote-Zuweisung und Bot-Verbindung folgen später.
