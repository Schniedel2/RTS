# Typisierte Netzwerkbefehle

Stand: 05.10.2026; Architektur-Aufgabe 12.

## Vertrag und Zuständigkeiten

Goto, Build und Harvest besitzen jeweils einen eigenen Request- und Command-Payload in `src/Network/ComplexCommandPayloads.cs`. `NetworkCommands` erzeugt diese Verträge; `PlayerCommandService` ist weiterhin der gemeinsame Zugang für menschliche Spieler und Host-KI. Kleine UnitActions behalten `UnitActionContext`.

| Payload | Pflichtdaten | Optionale Daten |
| --- | --- | --- |
| GotoRequestPayload | UnitIds, Target (X/Y/Z) | AppendToQueue, Routes, FormationFacingDegrees |
| GotoCommandPayload | PlayerId, UnitIds, Target, Routes | AppendToQueue, FormationFacingDegrees, EarthworkOrderId |
| BuildRequestPayload | BuildingTypeId, Position, RotationDegrees | BuildingId, WorkerIds |
| BuildCommandPayload | PlayerId, ArmyId, BuildingId, BuildingTypeId, Position, RotationDegrees, PurchasePrice, RemainingResources | WorkerIds |
| HarvestRequestPayload | HarvesterId, Target | keine |
| HarvestCommandPayload | HarvesterId, Phase, CargoAmount | keine |

Auf dem Draht gibt es für diese sechs Nachrichtentypen ausschließlich die Hülle `type`, `senderId`, `serverTime` und `payload`. Der numerische Nachrichtentyp bestimmt eindeutig den Payload. Pflichtfelder tragen `JsonRequired`; unbekannte oder doppelte Felder und zusätzliche flache Koordinaten werden abgewiesen. `serverTime` darf beim Lesen fehlen und wird dann null Sekunden. Positionen enthalten immer alle drei Komponenten.

`ComplexCommandJsonConverter` bildet die Payloads zentral auf den vorhandenen internen `NetworkMessage` ab. Dieser Adapter erhält den bisherigen Dispatcher und die noch nicht migrierten Nachrichten. Der interne flache Nachrichtentyp wurde also nicht vollständig abgeschafft; für die sechs migrierten Typen gibt es jedoch keinen alternativen alten JSON-Vertrag. Häufige unveränderte Zustandsnachrichten verwenden weiterhin den direkten Serializer ohne zusätzlichen JSON-Dokumentbaum.

## Prüfung und Autorität

`ComplexCommandPayloads.TryValidate` prüft denselben Vertrag für lokal erzeugte und über TCP empfangene Nachrichten. Factories weisen Programmierfehler mit `JsonException` ab. NetworkHandler prüft vor lokalem Einreihen/Anwenden und Senden, NetworkHost und NetworkInput zusätzlich vor Verarbeitung. Ungültige lokale Nachrichten werden mit Diagnose verworfen. Fehlerhaftes JSON beendet über die bestehende Transportfehlerbehandlung die betroffene Verbindung.

- Sender- und benötigte Objekt-IDs müssen gültig sein; eine Request-PlayerId darf keine andere Identität als den Sender behaupten. Die bestehende Authentifizierung des Transports bleibt zuständig für die tatsächliche Senderzuordnung.
- Koordinaten, Zeiten und Winkel müssen endlich sein; Ladung und bestätigte Preise/Ressourcen dürfen nicht negativ sein. HarvestPhase muss definiert sein.
- Empfänger-IDs sind eindeutig, maximal 4.096. Routen gehören ausschließlich zu den Empfängern, mit maximal 262.144 Zellen insgesamt. Zellen benötigen ganzzahlige X/Y-Werte. Optionale Endkoordinaten stehen paarweise.
- Ein Goto-Request darf seine Route auslassen; dann plant der Host. Ein Goto-Command muss für jeden Empfänger einen Routeneintrag enthalten. Eine explizit leere Route bleibt als Halt-/Unerreichbarkeitsresultat zulässig. Clients übernehmen bestätigte Routen unverändert.
- Ein Build-Request kann weder Kaufpreis noch Army oder Ressourcenstand vorgeben. Diese Daten ergänzt der Host nach seinen bestehenden Prüfungen. WorkerIds bleiben Teil desselben Bauauftrags.
- Der Spielereingang behandelt eine leere Goto-Auswahl als wirkungslose Eingabe und entfernt doppelte Empfänger; der Netzwerkvertrag selbst bleibt strikt.

Diese Strukturprüfung ersetzt keine Spielregeln: Eigentum, Verfügbarkeit, Finanzierung, Grid, Route und gültige Ziele werden weiterhin vom jeweils zuständigen Hostablauf geprüft. HarvestSystem bleibt alleiniger Besitzer des Ernteablaufs; sein Command repliziert Phase und Ladung.

## Kompatibilität

`NetworkHandler.ProtocolVersion` wurde mit Aufgabe 12 auf **5** erhöht (vorher 4). Seit Aufgabe 13 ist die aktuelle Version **6**, wegen der erweiterten Session-Snapshots; siehe [Session-Lifecycle und Late Join](Session-Lifecycle-und-Late-Join.md). Ein inkompatibler Client wird schon beim Beitritt mit einer Meldung einschließlich Host-/Clientversion abgewiesen. Auch der Client prüft die bestätigte Hostversion. Alle Teilnehmer müssen den neuen Build verwenden; alte flache Goto-/Build-/Harvest-Nachrichten werden nicht stillschweigend akzeptiert. Map-Dateien sind von dieser Wire-Änderung nicht betroffen.

## Validierung und Grenzen

`tests/GridNavigationChecks/ComplexCommandChecks.cs` ergänzt 71 Prüfungen: Roundtrips aller sechs Verträge, fehlende/zusätzliche/doppelte Felder, ungültige Zahlen und Routen, lokale Eingangsprüfung, leere Auswahl sowie echte TCP-Verbindungen. Letztere prüfen Versionsablehnung vor Aufnahme, menschliche Requests, dieselben Verträge der Host-KI und Routenübernahme in einer separaten Clientwelt ohne lokale Neuplanung. Bestehende Host-/Ernte-/Planungschecks laufen zusätzlich.

Build ohne Warnungen/Fehler und insgesamt 999 erfolgreiche Checks. Keine neue grafische Multiplayer-Abnahme. Laufende Sessionzustände/Snapshots wurden nicht durch diese Payloads ersetzt; deren Lifecycle-Integration ist inzwischen in [Architektur-Aufgabe 13](Session-Lifecycle-und-Late-Join.md) geprüft. Die optische Fahrzeugabnahme aus Aufgabe 02 bleibt offen.
