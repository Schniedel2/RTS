# Gemeinsame Request-Rückmeldungen (KI-Client Schritt 04)

`PlayerCommandService.LastRequest` und `NetworkHandler.TrackRequest` liefern den gemeinsamen `RequestReceipt`.
`LocalRequestReceipt` bleibt als Kompatibilitätsname erhalten. Der Zustand ist auf lokalen und entfernten Verbindungen derselbe.
`RequestId` ist eine stabile GUID; `RequestGeneration` bezeichnet die lokale Verbindungsgeneration des Absenders und wird vom Host unverändert zurückgegeben.
`WasSent` trennt einen in der KI-Warteschlange vorbereiteten Auftrag von einem an den Host gesendeten Request.
`State` unterscheidet Pending, Accepted, Rejected und Abandoned; `Result` unterscheidet zusätzlich InProgress, Completed und Failed.

## Transport und Autorität

Protokollversion war in Schritt 04 zunächst 8 (seit Schritt 05: 9): alle Teilnehmer benötigen denselben Build.
Auch die typisierten JSON-Envelopes für Build/Goto/Harvest übertragen die Korrelationsdaten.
Veröffentlichte Build-/Produktionsbefehle sowie fertig geplante Goto-Befehle tragen die Request-ID.
`RequestFeedbackCommand` geht gezielt an den ursprünglichen Akteur; Produktion und Bau werden zusätzlich mit ProductionOrderId bzw. ConstructionSiteId verknüpft.
Identität wird weiterhin vom authentifizierten Verbindungsweg übernommen, und die normalen Host-Validierungen bleiben erhalten.
Keine Remote-KI wird durch diesen Schritt aktiviert; die Controller-/Army-Zuweisung und deren Generation folgen in 05.

Der Host merkt pro Session (ActorId, RequestId). Ein zweites Exemplar derselben ID wird nie erneut ausgeführt:
bei laufender Prüfung wartet es, bei vorhandener Antwort wird die letzte Antwort erneut gesendet.
Ergebnisse bleiben für die gesamte Session erhalten, maximal 32768 Einträge; danach werden neue korrelierte Requests abgelehnt statt alte Kauf-IDs zu vergessen.
Die lokalen Receipt-Indizes räumen abgeschlossene Historie auf; offene Aufträge werden nicht durch diese Bereinigung entfernt.

## Annahme und Ausführung

Eine Annahme ist keine Fertigstellung. Der Host beobachtet angenommene Bau-/Produktions-/Forschungsaufträge anhand ihrer konkreten IDs:
Baustelle fertig bzw. ProductionQueue.WasCompleted meldet Completed; verlorene Baustelle/Produzent oder abgebrochene Produktion meldet Failed mit Grund.
Zustandsänderungen werden einmal gesendet, nicht in jedem Frame. KI-OrderQueue nutzt denselben Ergebnisvertrag für Reservierungen und Ausführung.
Andere unmittelbare Befehle gelten nach Anwendung als ausgeführt; Completed eines Goto bestätigt den angewendeten Bewegungsbefehl, nicht das Erreichen des Zielpunkts.
Bewegungs-/Harvest-Fortschritt bleibt weiterhin in den replizierten Unit-Zuständen.

## Verzögerung, Abbruch und Sessionwechsel

Ein verbundener Client ohne terminales Ergebnis fragt nach fünf realen Sekunden erneut nach, indem er denselben Request mit derselben ID sendet.
Damit ist sowohl ein verlorener ursprünglicher Request als auch eine verlorene Antwort behandelbar. Ein Timeout erzeugt ausdrücklich keinen neuen Kauf und gibt eine ungewisse Reservierung nicht frei.
Die letzte Host-Antwort wird vor erneuter Zielvalidierung wiederholt, auch wenn der ursprüngliche Produzent inzwischen verschwunden ist.
Ablehnung/Fehlschlag gibt die KI-Reservierung frei; Erfolg beendet den Auftrag. Eine verspätete Annahme kann InProgress nicht zurücksetzen und keine terminale Rückmeldung überschreiben.
Lokales Abandon beendet das Warten; es widerruft keinen bereits angenommenen Gameplay-Auftrag. Gameplay-Abbrüche benötigen weiterhin ihre normalen Stop-/Cancel-Befehle.
Disconnect/Sessionwechsel invalidiert offene Receipts und leert die Korrelationshistorie; alte Antworten dürfen diese Aufträge nicht wiederbeleben.

## Prüfung und Grenzen

Bestehende Host-/OrderQueue-Checks prüfen weiterhin echte Käufe, Ablehnungen, Reservierungen und Bau-/Produktionsabschluss.
Neue Checks prüfen JSON-Korrelation einschließlich typisierter Envelopes, lokale/entfernte Receipts, doppelte und späte Antworten, Abbruch und Sessionwechsel.
Zusätzlich wird eine echte TCP-Loopback-Verbindung aufgebaut und Annahme, Ausführungsabschluss, Ablehnung und die Timeout-Abfrage mit derselben ID geprüft.
Diese Prüfung ist ein Zwei-Endpunkte-Test in einem Prozess; ein vollständiger entfernter KI-Spieler und dessen Mehrprozess-Abnahme folgen in 06.
