# Fahrzeugbesatzung und Sitzposen

Ein Sitz wird im Fahrzeugmodell als leere Gruppe `pivot:seat_driver|pose:motorbike-driver` angelegt. Der Importer trennt den Pivot-Namen von der Pose: Der Pivot bleibt als `pivot:seat_driver` erreichbar, die Animation im Soldier-Modell heißt `motorbike-driver` (ohne `pose:`).

Der echte, eingestiegene Soldier wird am animierten Welttransform des Sitzes dargestellt, auch im Schattenpass. Die Sitzanimation wird bei Zeit 0 eingefroren und separat von seinen Lauf-/Kampfanimationen ausgewertet. Fehlt die angegebene Animation, wird `idle` verwendet. Fahrzeuge ohne Sitz-Pivot behalten ihre bisher unsichtbare Besatzung. `pivot:seat_passenger|pose:...` unterstützt außerdem einen sichtbaren Passagier; mehrere sichtbare Passagiersitze und Schützenrollen sind noch nicht umgesetzt.

MotorBike bleibt von Car abgeleitet und benötigt genau einen Fahrer. Ein unbemanntes Fahrzeug kann durch den bestehenden EnterUnit-Befehl übernommen werden. Bei Produktion erzeugt der vorhandene Host-Befehl eine Fahrer-ID; alle Clients erstellen damit denselben Soldier. Das einfache Motorrad bietet keinen Angriff an und kann auch nicht passiv schießen. Bewegung und Besatzung verwenden die vorhandenen Netzwerkbefehle; die Pose ist rein lokal aus den Modelldaten abgeleitet.

Checks: Import der tatsächlichen Motorrad-/Soldier-Modelle, normalisierter Pivot-Zugriff, Pose-Metadaten, gültiger Sitztransform, Fahrerpflicht und deaktivierte Bewaffnung. Die Ausrichtung der Pose im Spiel muss visuell geprüft werden; Position und Orientierung werden im Fahrzeugmodell am Sitz-Pivot eingestellt.
