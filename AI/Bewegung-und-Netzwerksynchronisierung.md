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

Fahrzeuge schauen bis zu drei Wegpunkte auf einer geraden Routensektion voraus.
An Richtungswechseln wird weiterhin der Zellmittelpunkt angefahren. Es gibt keine
ungeprüften Abkürzungen über Gebäudeecken. Die bestehenden Footprint-, Terrain-
und Diagonalprüfungen bleiben aktiv. Die letzte Zielzelle wird tatsächlich erreicht.

Kleine angenommene Host-Korrekturen werden nur in der Darstellung weich
abgebaut (Position und Yaw). Grid und logische Position bleiben davon getrennt;
große Versetzungen werden sofort dargestellt. Kann das lokale Grid eine
Positionskorrektur wegen eines Konflikts noch nicht annehmen, bleibt dessen
bisheriges Verhalten erhalten; die neue Navigation wird trotzdem übernommen.

## Prüfen

`dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj`

Die Checks umfassen blockierte und wieder freigegebene Ziele, erhaltene
Warteschlangen, Retry-Abstände, langsames Drehen, Gebäudeecken bei verschiedenen
Zeitschritten, JSON-Routen, Client-Autorität und visuelle Korrekturen.
Eine längere Multiplayer-Partie mit vielen Harvestern und Bulldozern bleibt der
praktische Test für Verkehrsstaus und das sichtbare Fahrgefühl.
