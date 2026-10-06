# KI-Controllerwechsel und Rückfall auf den Host

Stand: 06.10.2026, KI-Client-TODO Punkt 07. Netzwerkprotokoll 10; alle Teilnehmer müssen denselben Build verwenden.

## Erkennung auf echter Zeit

Ein synchronisierter RemoteAIRuntime meldet pro zugewiesener Army etwa einmal pro echter Sekunde einen Heartbeat. Er enthält Army, virtuellen Actor, Controllergeneration und die Sequenz vollständig abgeschlossener AIController.Update-Aufrufe. Die Sequenz steigt erst nach Rückkehr aus dem Controller. Ein bloß verbundener oder nur Netzwerknachrichten pumpender Prozess gilt damit nicht als arbeitende KI.

Der Host bindet den Peer an die tatsächliche TCP-Verbindung und akzeptiert nur Heartbeats der aktuellen Zuweisung. Alte Generationen, fremde Peers und rückläufige Sequenzen erneuern keinen Lease. Gleiche Sequenzen bestätigen zwar Netzwerkaktivität, erneuern aber nicht den Fortschrittstimer.

- Erkanntes Disconnect: Rückfall im selben Network.Update, ohne weitere Wartezeit.
- Noch kein erster Heartbeat: 15 Sekunden Startfrist.
- Nach erstem Heartbeat: 10 Sekunden ohne Heartbeat oder ohne steigende Update-Sequenz führen zum Rückfall.

Die Zeiten verwenden die monotone lokale Uhr des Hosts, nicht Spielzeit oder time-factor. Die Startfrist ist für den ersten Controllerstart gedacht, nicht für eine noch unvollständige Netzwerksynchronisation. Die Grenzwerte sind vorerst Konstanten in NetworkHandler. `ai-controller-list` zeigt auf dem Host Heartbeat-/Fortschrittsalter und den letzten Rückfallgrund; die Konsole meldet den Rückfall.

Das Signal misst Controlleraktivität, nicht einen militärischen Erfolg oder Bewegung in jedem Update. Eine KI darf auf Ressourcen oder Produktion warten. Gameplay-Stillstände beurteilt weiterhin der vorhandene AIOrderProgressMonitor; eine intern falsche Entscheidung trotz regelmäßig abgeschlossener Updates wird durch den Heartbeat allein nicht erkannt.

## Übergabe und Reihenfolge

Der Host setzt zuerst eine neue Controllergeneration mit unverändertem aufgelöstem Profil und Seed. Dadurch werden alte Gateways und bereits wartende Requests ungültig. NetworkHost entfernt noch nicht angenommene Requests der alten Generation aus seiner Queue, bestätigt ihre Ablehnung und löst eine laufende, noch unbestätigte Pfadsuche samt Planning-Markierung. Ihre späteren Scheduler-Callbacks können keine Route mehr veröffentlichen. Bereits vom Host bestätigte Bewegung, Ernte, Konstruktion, Produktion und Forschung werden nicht durch die Übergabe abgebrochen.

Lokale unbestätigte Receipts werden aufgegeben; alte Planungsqueues geben ihre Budgetreservierungen und lokalen Unit-Claims frei. Der neue AIController erzeugt seine Befehlsdienste mit der neuen Generation. AIContext prüft zusätzlich Session- und Controllergeneration, damit ein alter PlayerCommandService nicht versehentlich im neuen Kontext weiterverwendet wird.

Die KI rekonstruiert aus den aktuellen replizierten bzw. autoritativen Daten:

- vorhandene Grundgebäude und unfertige Baustellen statt erneut zu kaufen;
- vorhandene/auf eine Baustelle fahrende Arbeiter;
- laufende Ernte statt den Harvest-Auftrag neu zu starten;
- vorhandene Produktions-/Forschungsorders und bereits verliehene Perks;
- bestehende Soldaten, Crew, Fahrzeuge und Squad-Mitgliedschaften.

Interne Entscheidungsobjekte werden nicht über das Netz kopiert. Die Strategie plant ihre nächste Entscheidung neu, während bestätigte Gameplay-Jobs weiterlaufen. Produzierte Units bestätigen jetzt auch ihre ProductionOrderId auf dem Client: übernommene Produktionspläne können den Abschluss eindeutig erkennen. Übernommene Forschung kann durch den bestätigten Perk abgeschlossen werden, selbst wenn der neue Controller kein Receipt des ursprünglichen Käufers besitzt.

## Wiederverbindung und Bedienung

Ein wiederverbundener oder neu gestarteter Peer erhält den aktuellen Snapshot und bleibt passiv, solange die Army dem Host gehört. Weder ein alter Token noch verspätete Heartbeats übernehmen die Army zurück. Erneute Remote-Ausführung benötigt eine ausdrückliche Host-Zuweisung:

```text
ai-controller-assign <KI> <Client>
ai-controller-assign <KI> host
ai-controller-list
```

Damit ist auch ein manueller Wechsel während Bau/Produktion möglich. `game-start` verwendet weiterhin neue Hostgenerationen und das Matchprofil. Eine explizite Widerrufszuteilung mit Peer=null bleibt widerrufen; der Watchdog interpretiert dies nicht als ausgefallenen Remote-Client.

## Prüfung

```powershell
dotnet build RTS.csproj
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj
& ./tests/GridNavigationChecks/RunRemoteAI.ps1 -Mode normal
& ./tests/GridNavigationChecks/RunRemoteAI.ps1 -Mode disconnect
& ./tests/GridNavigationChecks/RunRemoteAI.ps1 -Mode stall
```

Build ohne Warnungen/Fehler; 1.839 Checks bestanden, davon 27 neue Wechsel-/Rückfallchecks. Die neuen Prüfungen nutzen echte TCP-Verbindungen mit kontrollierter monotoner Testuhr: Timeout, steigende/stagnierende Heartbeats, Startfrist, unabhängige Armies, ausstehender/angenommener Kauf, späte Requests, Rekonstruktion von Bau/Ernte/Produktion/Forschung, abgebrochene Pfadsuche und Neustart mit expliziter Neuzuweisung.

Zusätzlich bestanden drei echte Zwei-Prozess-Läufe. In den Ausfallvarianten werden zuerst bezahlte Forschung und Ausbildung bestellt; der Client fällt während einer aktiven Reaktorbaustelle aus. Der Host übernimmt mit erhaltenem Profil, stellt dieselbe Baustelle fertig und wirtschaftet, produziert, erkundet und kämpft weiter. Ein reconnectender Client bleibt ohne Controller und ein absichtlich gesendeter alter Token wird abgelehnt. In beiden Läufen gab es genau einen Air-Technology-Forschungsrequest. Beim Stillstand bleibt die TCP-Verbindung aktiv, während die Remote-KI und ihre Heartbeats gestoppt werden.

Beobachtungen der Abnahme: Disconnect-Lauf 292 Hostrequests nach 5 Remoterequests, 65 geerntete Ressourcen und 77,5 Kampfschaden; Stillstand-Lauf 236 Hostrequests nach 5 Remoterequests, 100 geerntete Ressourcen und 90 Kampfschaden. Diese Zahlen sind Diagnosewerte, keine deterministisch geforderten Requestmengen.

Die Prozesse laufen höchstens 38/40 Sekunden in einer beschleunigten grafiklosen Testwelt mit einfachen Meshes und teils Testgebäuden. Keine Aussage über grafische Langzeitstabilität oder Performanceverteilung; diese Vergleiche bleiben Punkt 09. Fensterloser produktiver Bot folgt in 08, Dedicated-Server bleibt 11.
