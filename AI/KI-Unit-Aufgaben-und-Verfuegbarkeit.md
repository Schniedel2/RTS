# KI-Unit-Aufgaben und Verfügbarkeit

`GameWorld.UnitTasks` ist die gemeinsame lokale Zuständigkeitsverwaltung. `AIUnitAssignment` enthält Unit/Army, Aufgabe, Besitzer, Priorität, Ablauf und gegebenenfalls die verdrängte Zuordnung. Aufgaben sind `Unbound`, `Reserve`, `Scout`, `BaseDefender`, `SquadMember`, `Escort`, `Recovery` und `Economy`.

Die Besitzer sind Army-spezifisch. Vorbereitung, Angriff und Erholung teilen bewusst die Zuständigkeit der Squad-Pipeline; deren Phasen laufen im AIController nacheinander. Scouting-Controller besitzen getrennte IDs. Dadurch verlängert ein aktiver Scout-Controller nicht die verlassenen Zuordnungen eines anderen Controllers.

## Prioritäten und Befehle

- Scout: 30.
- Squad und Eskorte: 40.
- Basisverteidigung: 60.
- Erholung/Heilung: 80.
- Bestehende Wirtschafts-/Bau-/Produzentenreservierungen: 100.

Gleiche Priorität berechtigt nur den bisherigen Besitzer zum weiteren Steuern. Höhere Priorität darf ausdrücklich übernehmen. `AIUnitTaskAgent` prüft komplette Empfängergruppen vor dem Beanspruchen. Bei einem Squad-Leader oder Mitglied wird auch die lebende Squad-Gruppe geprüft, damit eine einzelne Unit nicht unabhängig die Formation wieder unter Angriffskontrolle bringt. Gruppenübernahmen sind atomar. Die normale Basisverteidigung zieht keine einzelnen gebundenen Squad-Follower heraus; verfügbare Kampf-Eskorten können nach Priorität übernommen werden.

Taktische `PlayerCommandService`-Instanzen besitzen einen expliziten Agent. Seine Zuständigkeit gilt auch nach asynchronen Fortsetzungen und ist kein kurzlebiger globaler Scope. Bewegung, Follow, Stop, UnitActions, Terrain-/Unit-Angriff, Einsteigen und MoveAway werden vor dem Versand geprüft. Verweigerte Requests erhalten eine lokale Ablehnungs-Rückmeldung. Akzeptierte Befehle benutzen weiterhin die normalen Host-/Netzwerkwege. Gewöhnliche menschliche Services haben keinen Agent und behalten ihre bisherige Steuerung.

Auswahl und Ausführung sind beide geschützt: Squad-Vorbereitung verwendet verfügbare Soldaten, der Angriff pausiert bei höherer Bindung seiner Gruppe und lässt übernommene Eskorte aus, Erholung wartet bei einer höher priorisierten Aufgabe, und Scouting pausiert beziehungsweise verwirft laufende Planungsjobs. Verteidigung stellt nur für weiterhin selbst besessene Units alte Rückkehrbefehle her.

## Lebenszyklus und vorhandene Reservierungen

Aktive Controller verlängern explizite Zuordnungen. Verlassene Einträge verfallen nach 15 Simulationssekunden. Stop Scouting, Dispose, Verteidigungsrückkehr/Reset, Missionsende sowie Erholungsende/Reset geben ihre Zuständigkeiten explizit frei. Die vorherige Zuordnung wird bei einer temporären Übernahme wiederhergestellt. Gruppenfreigabe eines Leaders stellt auch seine untergeordneten Zuordnungen wieder her.

Tod/Entfernen, Einsteigen, Army-Wechsel, Sessionwechsel und rückwärts gesetzte Simulationszeit räumen veraltete Einträge auf. Hostbestätigte Squad-Zugehörigkeit liefert auch ohne expliziten Eintrag eine Squad-Zuordnung. Freie katalogisierte Defender erscheinen als Reserve; andere freie Units als ungebunden. Harvester und Reservierungen der bestehenden `AIOrderQueue` erscheinen als Wirtschaftsaufgaben. Diese Reservierungen haben Vorrang vor einer taktischen Zuordnung; die Wirtschaftsplanung wird nicht als zweite unabhängige Warteschlange dupliziert.

Dies ist KI-Planungszustand auf dem Game-Thread, kein zusätzliches Gameplay-Kommando und kein Snapshot-/Netzwerkformat. Die Ausführung und Validierung bleiben beim Host. Neue taktische Controller müssen einen passenden Agent verwenden, anstatt einen ungebundenen menschlichen `PlayerCommandService` anzulegen. Eine neue Reparatur-KI kann dieselbe Recovery-Kategorie verwenden; es wird kein neues Reparaturverhalten eingeführt.

## Prüfung

21 neue Checks in `AIUnitTaskChecks.cs` prüfen erste Initialisierung, freie Reserve, Claim, Gleich-/Höherpriorität, abgewiesene Bewegungs-/Terrain-Angriffsrequests, gewöhnlichen Versand des Besitzers, Wiederherstellung, atomare Empfängergruppen, Ablauf, Army-/Zeitreset, Scout-Start/Stop, Erholungsschutz gegenüber Verteidigung, bestätigte Squad-Zugehörigkeit, Gruppenübernahme/-freigabe und entfernte Units.

Alle 1.371 Checks bestehen; Build ohne Warnungen oder Fehler. Die vorhandenen Squad-, Scouting-, Verteidigungs- und Wiederaufbauchecks laufen ebenfalls weiter. Keine zusätzliche grafische Schlachtabnahme wurde vorgenommen.
