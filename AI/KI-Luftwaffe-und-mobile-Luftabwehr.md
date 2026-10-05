# KI: Luftwaffe und mobile Luftabwehr

Stand: 05.10.2026.

Der Luftausbau war an `AIArmoredSupportController.IsReady` gekoppelt. Solange Fahrzeuge produziert oder nach Verlusten ersetzt wurden, erhielt der Infrastruktur-Controller keine regulären Updates. Die Fahrzeugplanung zählte außerdem alle Angriffsfahrzeuge zusammen: Sobald die kleine Gesamtquote erfüllt war, wurde zusätzliche Luftabwehr nicht mehr bestellt.

## Produktion

- Eine fertiggestellte Fahrzeugproduktion reicht jetzt aus, um den Infrastruktur- und Verteidigungsausbau freizugeben. Die Kernbasis muss weiterhin einsatzbereit sein. Ein bereits aktiver Infrastrukturplan wird auch während eines wartenden Verteidigungsplans aktualisiert.
- Bodenangriff und mobile Luftabwehr haben getrennte Quoten. Die KI hält grundsätzlich ein mobiles Luftabwehrfahrzeug; Luftbedrohungen erhöhen das Ziel bis auf drei. Gezählt werden lebende Einheiten und bestätigte Produktionsaufträge. Bei akuter Luftbedrohung wird fehlende Luftabwehr vor zusätzlichen Bodenfahrzeugen bestellt.
- Der Typ wird über `Defender + AntiAir`, `GroundVehicle` und den Luftabwehrwert im Katalog gewählt. Im aktuellen Katalog ist dies der Gepard. Seine Luftabwehr ersetzt keinen Tank in der Bodenquote.
- Der Infrastruktur-Controller hält zwei Angriffsflieger. Sein Katalogplan löst Forschung, Strom und Produzent auf: derzeit Air Technology → Helipad → Helicopter. Der kostenlose Heli des Helipads zählt bereits mit; laufende passende Produktionsaufträge werden übernommen. Verluste öffnen die Quote wieder.
- Preise, Voraussetzungen und Produktionszeiten kommen aus dem bestehenden Katalog/Preissystem. Bestellungen verwenden `PlayerCommandService` und die normalen Host-Requests. Die Ressourcenreserve von 800 bleibt gültig.

## Einsatz

Neue Squad-Missionen übernehmen geeignete Boden- und Luftbegleiter anhand ihrer Katalogfähigkeiten. Ein Heli kann Bodenziele mitbekämpfen. Ein Gepard folgt bei einem Gebäudeangriff dem Squad-Leader und greift nahe sichtbare Flugziele an; dafür genügt auch ein einzelner geeigneter Luftabwehrbegleiter. Angriffsbefehle enthalten nur Einheiten, die das Ziel angreifen können. Der reservierte Scout bleibt ausgenommen.

Helis beim Anflug, Start, Landen oder Versorgen sowie Helis ohne Munition oder mit weniger als 25 % Kraftstoff werden nicht für neue Angriffsbefehle übernommen. Ein versorgter gelandeter Heli darf einen neuen Einsatz beginnen. Bereits ausgewählte Begleiter können nach ihrer Versorgung wieder an der laufenden Mission teilnehmen.

## Prüfung

Grafikfreie Ablaufprüfungen prüfen echte Host-Annahme und Ressourcenabbuchung für Gepard/Heli sowie Forschungsannahme, laufende Warteschlangen, Ersatzkäufe, erhöhte Luftbedrohung, Ressourcenreserve, Luftplan ohne fertige Fahrzeugquote, Squad-Begleitung, passende Luftzielbefehle und das Freihalten von Helis beim Versorgen. Eine optische Schlachtprüfung ist damit nicht ersetzt.
