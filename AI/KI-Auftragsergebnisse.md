# Einheitliche KI-Auftragsergebnisse

`LocalRequestReceipt.Result` enthält ein `AIOrderResult`. Die wirtschaftliche `AIOrderQueue.Order.Result` verweist auf dasselbe Objekt. Damit unterscheiden Host, Queue und Produktionsplaner den Versand beziehungsweise das Warten, die Annahme, laufende Arbeit und deren Ende.

## Zustände

- `Requested`: lokaler Kauf-/Bauwunsch oder abgeschickter Request; noch keine Host-Annahme.
- `Accepted`: der Host hat den normalen Befehl validiert und angewendet.
- `Rejected`: der Host oder die Kauf-Vorprüfung lehnt den Wunsch ab.
- `InProgress`: die bestätigte Baustelle oder der konkrete FIFO-Auftrag läuft.
- `Completed`: die Baustelle ist fertig oder der konkrete Produktionsauftrag wurde abgeschlossen.
- `Failed`: ein angenommener Auftrag scheitert, ein wartender Wunsch läuft ab, oder Match/Session/Queue wird verworfen.

`WasAccepted` und `Transitions` erhalten die Host-Annahme auch dann, wenn das aktuelle Ergebnis längst `InProgress`, `Completed` oder `Failed` lautet. Terminale Ergebnisse werden durch spätere Rückmeldungen nicht überschrieben. Die Queue behält ihre zusätzlichen Planungszustände (`WaitingForResources`, `WaitingForWorker`, `WaitingForProducer`, `Paused`); diese sind keine erfundenen Host-Ablehnungen.

## Gründe

`Failure` enthält eine Kategorie, `Reason` einen lesbaren Text. Kategorien: `Resources`, `BuildSite`, `Producer`, `Perk`, `InvalidTarget`, `Validation`, `Cancelled`, `Timeout`, `SessionChanged`, `RecoveryCooldown`; erfolgreiche Ergebnisse verwenden `None`.

Für abgelehnte Bau-, Ausbildungs- und Forschungsrequests bestimmt der Host die Kategorie anhand der tatsächlichen Army-, Produzenten- und Quote-Daten. Geldmangel und fehlende Perks werden separat gemeldet; unbekannte Produkte bleiben Validierungsfehler. Der Fortschrittsmonitor sperrt einen Bauplatz bei Ressourcenmangel nicht mehr als ungeeignet. Construction-Requests werden nun auch auf vorhandene eigene unfertige Baustellen und kontrollierbare geeignete Arbeiter geprüft; ein nicht ausführbarer Request erhält keine Scheinbestätigung.

Eine abgelehnte Wiederaufnahme beziehungsweise Construction-Neuplanung besitzt einen eigenen Receipt. Die Queue übernimmt dessen Fehler in das Ergebnis des ursprünglichen angenommenen Auftrags, ohne dessen Annahmehistorie zu verlieren. Produzentenverlust und verschwundene Produktionsaufträge werden ebenfalls als Laufzeitfehler gemeldet. Timeout, Reset und Sessionwechsel bleiben von Host-Ablehnung unterscheidbar.

## Exakter Abschluss

`ProductionQueue` hält eine begrenzte lokale Historie der letzten 128 tatsächlich abgeschlossenen `OrderId`s. Ein fehlender FIFO-Eintrag ohne Abschlussnachweis gilt als Abbruch. Der Produktionsplaner verfolgt die konkrete Produktions-ID auch beim Übernehmen eines schon laufenden Auftrags. Eine zusätzlich auf anderem Weg gespawnte Unit desselben Typs kann den Plan nicht mehr fälschlich abschließen.

Für von der gemeinsamen Queue verwaltete Requests liest der Produktionsplaner das gemeinsame Ergebnis statt Stückzahländerungen oder kurzer Bestätigungs-Timeouts. Die bestehende Fortschrittskontrolle bleibt erforderlich: eine bestätigte, aber blockierte Baustelle beziehungsweise FIFO muss nach wie vor auf Stillstand geprüft werden.

Alle Requests laufen unverändert über `PlayerCommandService`, Hostvalidierung und normale Netzwerkcommands. Ergebnisobjekte und lokale Abschlussnachweise werden nicht auf dem Wire serialisiert und erzeugen keine zusätzliche Gameplay-Autorität. Der Receipt anderer lokaler Befehle enthält ebenfalls Versand/Annahme/Ablehnung; der gesamte Lebenszyklus der Wirtschaftsbefehle wird durch die Army-Queue geführt. Dauerhafte taktische Missionen behalten ihre bestehenden Mission-/Fortschrittscontroller.

## Prüfung

`tests/GridNavigationChecks/AIOrderResultChecks.cs` ergänzt 26 grafikfreie Checks: gemeinsame Objektidentität, echter Host-Lebenszyklus mit Annahmehistorie, terminale Stabilität, fünf Ablehnungskategorien, verschwundene FIFO, Timeout, fehlgeschlagene Construction-Neuplanung, Produzentenverlust, Sessionwechsel sowie exakter Ausbildungsabschluss und Übernahme bestehender Aufträge ohne falsche Erkennung durch zusätzliche Units.

Bestehende Katalogprüfungen bilden jetzt echte FIFO-Abschlüsse ab; die Bauplatzprüfung erwartet bei Geldmangel einen weiterhin gültigen Platz. Alle 1.276 Checks bestanden; Hauptprojekt-Build ohne Warnungen/Fehler. Eine grafische mehrminütige KI-Schlacht wurde für diese Änderung nicht durchgeführt.
