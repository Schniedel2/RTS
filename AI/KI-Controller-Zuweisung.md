# Hostautorisierte KI-Controller-Zuweisung

KI-Client-TODO 05 führt `AIControllerAssignment` ein: ArmyId, virtueller ActorId, ControllerPeerId (null = widerrufen), Generation und aufgelöstes unveränderliches AIStrategyProfile einschließlich effektivem Seed.
Ein Peer kann mehrere Armies steuern, jede Army besitzt genau einen aktuellen Eintrag. ActorIds gehören eindeutig zu einer Army.
`NetworkHandler.AssignAIController` ist nur auf dem Host und Spielthread zulässig. Ein Ziel-Peer muss der Host oder ein verbundener Teilnehmer sein.
Jede Zuweisung und jeder Widerruf erhöht die Generation; alte Einträge werden nicht durch Löschen reaktivierbar.

## Befehlsvertrag

`PlayerCommandService` hält die zum Erstellungszeitpunkt gültige Zuweisung fest.
Er versieht KI-Requests mit ArmyId, ActorId und Generation. Ein bestehender Befehlsdienst übernimmt bei einem Controllerwechsel keine neue Generation.
Der Wire-Sender ist der tatsächliche Peer; die KI-Identität bleibt getrennt im Envelope.
Der Host ersetzt ControllerPeerId durch die Identität der Verbindung, validiert die Zuweisung und verwendet erst danach den virtuellen ActorId für die gewöhnlichen Army-/Gameplay-Prüfungen.
Somit wird keine pauschale Kontrolle über fremde Armies an einen Peer vergeben.

Der Host prüft alle befehligten UnitIds und ein zusätzliches UnitId, explizite ArmyId und Bauziel.
Gemischte Gruppen mit einer fremden Army werden vollständig abgelehnt. KI darf keine Army-Kontrollrechte vergeben, Armies verschmelzen, Units übertragen, Editor-/Spawn-Befehle oder game-start auslösen.
Zielgegner einer Attack-Aktion sind keine eigenen Befehlsempfänger und werden weiter durch die vorhandenen Kampfregeln geprüft.
Requests werden beim Eingang und erneut bei der Ausführung geprüft; inkrementelle Goto-Planung prüft die Generation ebenfalls vor Veröffentlichung.
Auch gespeicherte Request-Rückmeldungen dürfen keine neue Ausführung einer alten Controllergeneration auslösen.

Ordentliche Spielerberechtigungen bleiben für menschliche Armies bestehen. Sobald eine Army im KI-Register geführt wird, sind gewöhnliche unmarkierte Player-Requests für diese Army gesperrt, einschließlich normaler Kontrollfreigaben.
Dies ist die bewusst strikte erste Regel: ein alter KI-Peer darf die Controllergeneration nicht über die menschliche Befehlspipeline umgehen.
Eine gesonderte manuelle menschliche Übernahme einer KI-Army ist damit noch nicht eingeführt.

## Lifecycle und Replikation

Erstellung einer KI in RTSGame registriert zunächst den Host. game-start widerruft vorherige Einträge und weist die teilnehmenden KI-Armies erneut dem Host zu, mit neuen Generationen und dem aktuellen Match-Profil.
BeginMatch verwirft die bisherigen Planungsobjekte; neue Befehlsdienste bekommen den neuen Token.
Bei Widerruf/Transfer stoppt die bisherige hochstufige Host-KI und räumt Queue, Monitor, Scout- und Angriffszuordnungen auf. Bereits angenommene Gameplay-Jobs werden durch diesen Wechsel nicht pauschal abgebrochen.
Disconnect/Sessionwechsel leert das Register. Automatische Übernahme nach Ausfall eines entfernten Peers folgt in 07.

`AIControllerAssignmentCommand` verteilt Änderungen vom Host. Alte/doppelte Generationen werden nicht angewendet.
RTSGame.CaptureSessionSnapshot nimmt das gesamte Register einschließlich widerrufener Generationen auf; Late Join stellt es vor SessionReady wieder her.
Ein entfernter Befehlsdienst darf erst bei Connected senden. Die KI-Controller selbst bleiben in diesem Schritt hostgebunden; weder Snapshot noch Zuweisung startet bereits einen Remote-Bot.
Protokollversion ist 9: alle Teilnehmer benötigen denselben Build.

## Prüfung und nächster Schritt

27 neue Checks prüfen Host-Autorität, falsche Identitäten/Generationen, Widerruf, unveränderte alte Gateways, mehrere Armies pro Peer, sämtliche Gruppenempfänger, atomare Snapshot-Rekonstruktion und Session-Reset.
TCP-Loopback prüft Live-Zuweisung, Widerruf, Wire-Trennung von Peer und KI-Akteur; Late Join prüft Profil-/Generationsübernahme vor Bereitschaft.
Alle 1.790 Checks und der Build bestehen. Ein Lauf traf den schon zuvor gelegentlich fehlgeschlagenen Scout-Reservierungstest ohne Ziel; der anschließende vollständige Lauf bestand.
Keine Mehrprozess-KI-Abnahme: die manuelle Zuweisung per Konsole, tatsächliche entfernte Ausführung und Rekonstruktion ihrer Planer folgen in 06; kontrollierter Ausfall/Wechsel in 07.

## Ergänzung aus Punkt 06

Manuelle Console-Zuweisung und tatsächliche Ausführung auf einem synchronisierten normalen Client sind jetzt implementiert. Bedienung, Queue-/Budgetvertrag und reproduzierbarer Zwei-Prozess-Test: [KI-Remote-Client.md](KI-Remote-Client.md). Die oben beschriebene Hostbindung war der Zwischenstand von Punkt 05; automatischer Rückfall bleibt Punkt 07.

## Ergänzung aus Punkt 07

Protokoll 10 ergänzt generationsgebundene Heartbeats und automatischen Host-Rückfall. Rekonstruktion bezahlter Jobs, Reihenfolge und Prüfungen: [KI-Controller-Wechsel.md](KI-Controller-Wechsel.md). Die Aussagen zu Protokoll 9 und dem noch ausstehenden Rückfall oben dokumentieren den Zwischenstand der Punkte 05/06.
