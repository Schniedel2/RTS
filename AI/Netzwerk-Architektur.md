# Netzwerk-Architektur

Die Host-Simulation bleibt autoritativ. Clients senden Requests; der Host prüft und verarbeitet sie auf dem Spiel-Thread und verteilt daraus erzeugte Commands in einer festen Reihenfolge. Hintergrund-Tasks dürfen nur TCP lesen oder schreiben und Nachrichten in die Inbox legen. Sie greifen nicht auf `GameWorld`, Units, Armies oder andere Gameplay-Daten zu.

Jede Verbindung besitzt genau einen Writer mit einer begrenzten Sendewarteschlange. Dadurch bleiben Nachrichten pro Empfänger geordnet und ein langsamer Client blockiert weder den Host noch andere Clients. Zu große Frames, volle Queues und Schreib-Timeouts beenden nur die betroffene Verbindung und werden über den Netzwerkstatus gemeldet.

`NetworkHandler.Update()` verarbeitet pro Frame höchstens 128 Nachrichten beziehungsweise vier Millisekunden Arbeit. Normale Commands bleiben FIFO erhalten. Ersetzbare reine Positions-Snapshots einer Unit werden innerhalb eines commandfreien Abschnitts zusammengefasst; Navigationswechsel, Commands, Verbindungsereignisse und Snapshots bilden Ordnungsgrenzen.

Beim Beitritt erhält ein Client in dieser Reihenfolge `JoinAccepted`, Spielerdaten, einen `SessionSnapshot`, die vorhandenen Mitglieder und `SessionReady`. Der Snapshot umfasst Terrain und Map-Objekte, Armies samt Ressourcen, Besitzern, Rechten und Perks, aktive Units samt Zustand und Besatzung sowie Fog-of-War-Daten. Während `Synchronizing` pausiert der Client seine Simulation. Erst `SessionReady` schaltet auf `Connected`, sodass nie eine teilweise geladene Welt gespielt wird.

Jede Sitzung hat eine Generation. Beim Trennen oder Neuverbinden werden Listener, Verbindungen, Mitglieder, Fehlerzustand und ausstehende Daten bereinigt. Spät eintreffende Nachrichten einer alten Generation werden ignoriert. Der `NetworkHost` setzt zugleich laufende Erdarbeiten, Projektile, Requests und Zeitgeber zurück.

Neue Netzwerkfunktionen sollten deshalb immer diesem Weg folgen: Client-Request, Host-Verarbeitung auf dem Spiel-Thread, autoritativer Command, geordnete Verteilung. Laufende Zustände, die ein später Client benötigt, gehören zusätzlich in den `SessionSnapshot`.
