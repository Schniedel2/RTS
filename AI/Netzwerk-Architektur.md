# Netzwerk-Architektur

Die Host-Simulation bleibt autoritativ. Clients senden Requests; der Host prüft und verarbeitet sie auf dem Spiel-Thread und verteilt daraus erzeugte Commands in einer festen Reihenfolge. Hintergrund-Tasks dürfen nur TCP lesen oder schreiben und Nachrichten in die Inbox legen. Sie greifen nicht auf `GameWorld`, Units, Armies oder andere Gameplay-Daten zu.

Jede Verbindung besitzt genau einen Writer mit einer begrenzten Sendewarteschlange. Dadurch bleiben Nachrichten pro Empfänger geordnet und ein langsamer Client blockiert weder den Host noch andere Clients. Zu große Frames, volle Queues und Schreib-Timeouts beenden nur die betroffene Verbindung und werden über den Netzwerkstatus gemeldet.

`NetworkHandler.Update()` verarbeitet pro Frame höchstens 128 Nachrichten beziehungsweise vier Millisekunden Arbeit. Normale Commands bleiben FIFO erhalten. Ersetzbare reine Positions-Snapshots einer Unit werden innerhalb eines commandfreien Abschnitts zusammengefasst; Navigationswechsel, Commands, Verbindungsereignisse und Snapshots bilden Ordnungsgrenzen.

Beim Beitritt erhält ein Client in dieser Reihenfolge `JoinAccepted`, Spielerdaten, einen `SessionSnapshot`, die vorhandenen Mitglieder und `SessionReady`. Der Snapshot umfasst Terrain und Map-Objekte, Armies samt Ressourcen, Besitzern, Rechten und Perks, aktive Units samt Zustand und Besatzung sowie Fog-of-War-Daten. Während `Synchronizing` pausiert der Client seine Simulation. Erst `SessionReady` schaltet auf `Connected`, sodass nie eine teilweise geladene Welt gespielt wird.

Jede Sitzung hat eine Generation. Beim Trennen oder Neuverbinden werden Listener, Verbindungen, Mitglieder, Fehlerzustand und ausstehende Daten bereinigt. Spät eintreffende Nachrichten einer alten Generation werden ignoriert. Der `NetworkHost` setzt zugleich laufende Erdarbeiten, Projektile, Requests und Zeitgeber zurück.

Neue Netzwerkfunktionen sollten deshalb immer diesem Weg folgen: Client-Request, Host-Verarbeitung auf dem Spiel-Thread, autoritativer Command, geordnete Verteilung. Laufende Zustände, die ein später Client benötigt, gehören zusätzlich in den `SessionSnapshot`.

## Synchronisationsdiagnose

Der Host kann mit `network-sync-start` regelmäßige Vergleiche aktivieren. Optional setzt beispielsweise `network-sync-start 10` ein Intervall von zehn Sekunden. `network-sync-check` startet sofort einen einzelnen Vergleich, `network-sync-status` zeigt Zähler und Zustand und `network-sync-stop` beendet die Diagnose auf allen Teilnehmern. Abweichungen werden nach Welt, Armies, Units und Sichtdaten aufgeteilt; soweit möglich nennt die Meldung die betroffene Army- oder Unit-ID. Kontinuierlich wachsende Tiberiumwerte und kleine Bewegungsabweichungen werden zeitlich tolerant behandelt. Eine Unit-Position wird erst ab drei Welt-Einheiten Abstand oder mehr als 60 Grad Drehabweichung als `movement:<id>` gemeldet.

Der aktuell sichtbare Fog-of-War-Bereich wird weiterhin lokal aus den replizierten Unit-Positionen berechnet. Dauerhaft erkundete Zellen sind dagegen Host-autoritiv: Der Host verteilt einmal pro Sekunde eine bitgepackte Explored-Maske mit einem Bit pro Zelle und Army. Dadurch können kleine Bewegungsabweichungen keine dauerhaft unterschiedlichen Karteninformationen erzeugen.
