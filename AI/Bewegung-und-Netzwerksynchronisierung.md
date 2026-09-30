# Bewegung und Netzwerksynchronisierung

Stand: 29.09.2026

## Verantwortlichkeiten

Der Host (bzw. ein nicht verbundenes Offline-Spiel) plant Wege und entscheidet
über Ersatzwege, Baustellen-Anfahrten, Folgen, Angriffs-Anfahrten und das
Fortsetzen einer Warteschlange. Verbundene Clients dürfen die bestätigte Route
für eine flüssige Darstellung vorhersagen. Sie suchen keine eigenen Wege und
entscheiden nicht selbst über das Ende eines Auftrags.

`MobileUnitState` enthält neben der Position eine Navigationsrevision und den
Fortschritt auf der Route. Bei Änderungen und spätestens nach drei Sekunden
wird zusätzlich `GroundNavigationState` übertragen: Restweg, aktuelles Ziel,
Warteschlange, Bau-/Einstiegszuordnung und Bewegungsstatus. Dazwischen werden
keine Routenarrays übertragen. Zustandsupdates großer Armeen werden reihum
versendet. Alle Teilnehmer müssen den gleichen aktuellen Build verwenden.

MonoGame-`Point` benötigt den expliziten Converter in `NetworkJson.Options`:
X/Y sind Felder und wurden ohne diesen Converter als `{}` serialisiert. Fehlende
Koordinaten werden jetzt abgelehnt, statt unbemerkt `(0,0)` zu erzeugen.

## Blockaden

Nach zwei Sekunden ohne Fortschritt bleibt der Auftrag erhalten und der Host
fordert über die Pathfinding-Warteschlange einen Ersatzweg an. Fehlgeschlagene
Suchen führen zum Status `Blocked`; zunächst wird nach zwei Sekunden, ab dem
dritten Versuch nach fünf Sekunden erneut versucht. Pro Update wird maximal
eine echte Suche aus dieser Warteschlange ausgeführt. Baustellen-Anfahrten
prüfen höchstens vier Kandidaten pro Versuch und wechseln bei Wiederholung
die Kandidaten. Direkte Gruppen-Goto-Suchen auf dem Host sind weiterhin synchron.

Ein permanentes Hindernis beendet den Auftrag nicht automatisch: Der sichtbare
Debugstatus bleibt `Blocked`. Stop oder ein neuer Auftrag kann ihn ersetzen.
Shift-Aufträge und der Bauauftrag gehen bei einer temporären Blockade nicht
verloren. Bewusstes Drehen auf der Stelle zählt nicht als Stillstand.

## Lenken und Darstellung

Fahrzeuge dürfen bis zu drei Wegpunkte über eine Kurve hinweg vorausschauen. Vor
der Wahl des Lenkziels prüft `GameGrid.CanTraverseDirect` den vollständigen direkten
Korridor, ohne die Belegung zu verändern. Dabei gelten Terrain- und Pathfindingregeln,
harte Belegungen, der vollständige Fahrzeug-Footprint und beide Seiten einer
diagonalen Ecke. Ist ein weiter Punkt nicht sicher erreichbar, wird schrittweise auf
einen näheren bestätigten Wegpunkt bis hin zum unmittelbar nächsten zurückgefallen.
Erreicht die geglättete Fahrt einen späteren bestätigten Routenknoten, wird der
übersprungene Fortschritt übernommen. Die letzte Zielzelle bleibt davon ausgenommen
und wird weiterhin mit ihrem genauen Ankunftsradius erreicht.

Das gewählte Lenkziel bleibt zwischen Updates gespeichert. Der Fortschritt darf nur
Wegpunkte bis zu diesem bestätigten Lenkziel verbuchen. Die tatsächlich gefahrene
Kurve darf benachbarte freie Zellen verwenden; jeder reale Zellübergang wird weiterhin
vom Host über `GameGrid.TryMove` mit vollständigem Footprint und Diagonalregeln geprüft.
Eine nur eine Zelle breite Gerade darf eine breite Einheit beim Einlenken daher nicht
festhalten. Neue Routen, Stop, Recovery und Host-Korrekturen verwerfen das lokale
Lenkziel. Die Festfahrerkennung misst dessen Annäherung, damit ausgelassene
Zellmittelpunkte keinen falschen Retry auslösen.
Die registrierte weiche Clearance folgt der tatsächlichen Position und ungerundeten
Drehung auch innerhalb einer Zelle. Ihr weicher Charakter bleibt erhalten.

Kleine angenommene Host-Korrekturen werden nur in der Darstellung weich
abgebaut (Position und Yaw). Grid und logische Position bleiben davon getrennt;
große Versetzungen werden sofort dargestellt. Kann das lokale Grid eine
Positionskorrektur wegen eines Konflikts noch nicht annehmen, bleibt dessen
bisheriges Verhalten erhalten; die neue Navigation wird trotzdem übernommen.

Vorwärts fahrende Bodenfahrzeuge beschreiben ihre Unterschiede über ein
`GroundSteeringProfile`. Drehgeschwindigkeiten und Winkelschwellen werden in
Grad beziehungsweise Grad pro Sekunde angegeben. Das Profil enthält getrennte
Lenkraten für Fahrt und Stand, den Winkel für eine optionale Standdrehung, die
minimale Kurvengeschwindigkeit sowie Rückwärtsfreigabe, -geschwindigkeit,
-winkel und -distanz. `CanTurnInPlace` aktiviert ausschließlich die Fähigkeit
zur Standdrehung; es verhindert keine Lenkung während der Fahrt. Infanterie und
Flugbewegung verwenden dieses Bodenfahrzeugprofil nicht.

Der Tank verwendet ebenfalls die gemeinsame `MobileUnit`-Wegfolge. Seine
Rückwärtsfähigkeit kommt aus dem `GroundSteeringProfile`; eine eigene
zellweise `MoveAlongPath`-Schleife existiert nicht mehr. Damit gelten
Routenvorausschau, Waypoint-Fortschritt und Blockade-Recovery unverändert auch
für den Tank.

Die Fahrgeschwindigkeit wird innerhalb einer Kurve kontinuierlich aus dem
aktuellen Richtungsfehler berechnet. Geradeaus gilt der Faktor `1`; bis zur im
Profil konfigurierten Standdrehungsgrenze sinkt er mit einer geglätteten Kurve
auf `MinimumCurveSpeedFactor`. Normale 45°- und 90°-Kurven werden damit fahrend
gelenkt. Eine Standdrehung bleibt für schärfere Wendungen vorgesehen; ein
zulässiges nahes Rückwärtsziel wird vorher als Rangiermanöver behandelt.

## Prüfen

`dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj`

Die Checks umfassen blockierte und wieder freigegebene Ziele, erhaltene
Warteschlangen, Retry-Abstände, langsames Drehen, Gebäudeecken bei verschiedenen
Zeitschritten, JSON-Routen, Client-Autorität und visuelle Korrekturen.
Eine längere Multiplayer-Partie mit vielen Harvestern und Bulldozern bleibt der
praktische Test für Verkehrsstaus und das sichtbare Fahrgefühl.
