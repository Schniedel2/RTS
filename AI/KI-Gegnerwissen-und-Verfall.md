# KI-Gegnerwissen und Verfall

`AIThreatAssessment` besitzt pro Army eine matchlokale Sammlung von `AIEnemyObservation`-Einträgen. Eine sichtbare, angreifbare gegnerische Unit liefert ID, Army, Typ, Katalogrollen, beobachtete Domain/Panzerung/Bewaffnung, letzte Position, Sichtungszeit und Kontext. Die ID verhindert doppelte Zählung bei wiederholter Sicht. Die gespeicherten Werte sind Beobachtungen und werden nicht nachträglich über die verdeckte Unit aktualisiert.

Ohne neue Sichtung sinkt das Vertrauen exponentiell: Halbwertszeit 30 Simulationssekunden, vollständiger Verfall nach 90 Sekunden. `EstimatedEnemyCount` summiert diese Gewichte. Die geschätzten Kategorien beeinflussen `RememberedThreats` und damit die bisherige geglättete Bewertung `Current`, welche Verteidigungs- und Produktionscontroller bereits verwenden. Ein kurz verschwundener Heli bleibt somit ein Anlass für Luftabwehr. Sichtung und bisherige Verlustauswertung sind getrennte Beiträge; deren vorhandene Kontextgewichtung bleibt erhalten.

Sichtbarkeit stammt aus dem Army-Sichtgrid, inklusive freigeschalteter verbündeter Welt-Sicht. Spectator und deaktivierte Darstellung des Fog of War gewähren der KI keine zusätzlichen Beobachtungen. Gegnerbeziehungen werden für diese Bewertung über den Welt-/Army-Service geprüft und benötigen keine zufällig erste eigene Unit.

Ein sichtbar zerstörter oder nicht mehr angreifbarer Gegner wird bei der nächsten Beobachtungsrunde entfernt. Eine verdeckte Entfernung oder Zerstörung bleibt bis zur nächsten tatsächlichen Beobachtung beziehungsweise bis zum Verfall unbekannt. Ein inzwischen verbündeter Gegner wird entfernt. Sessionwechsel oder zurückgesetzte Simulationszeit löschen Erinnerungen und Bewertungen. Der vorhandene BeginMatch-Weg erstellt außerdem eine neue Bewertung.

Der Sichtungskontext ist bewusst einfach: innerhalb von 30 Welteinheiten zu eigenen Gebäuden `BaseDefense`, sonst nahe eigenem Harvester `ResourceOperation`, sonst `Scouting`. Eigene Standortlisten werden einmal pro Beobachtungsrunde gesammelt, nicht erneut für jede gegnerische Unit. Die bisherige genauere Kontextzuordnung eigener Kampfverluste bleibt unverändert.

## Grenzen und Prüfung

Dies ist Produktionswissen, keine Freigabe zum Beschießen verdeckter Ziele und keine neue taktische Wegverfolgung. Letzte Positionen sind historische Punkte. Die KI erhält keine genauen aktuellen Mengen, sondern vertrauensgewichtete Sichtungen. Auch ein leeres ehemaliges Gebäudefeld entfernt einen nicht mehr vorhandenen Eintrag derzeit nicht vorzeitig; dieser verfällt regulär. Die Erinnerungen sind nicht netzwerkserialisiert: Sie gehören zum hostlokalen KI-Controller, werden bei einem neuen Match zurückgesetzt und ändern keine normalen Gameplay-Requests.

15 grafikfreie Checks in `AIEnemyKnowledgeChecks.cs` prüfen Sichtgrenzen, Spectator, Metadaten/Kontext, Luftbedrohung nach Sichtverlust, Halbwertszeit, Aktualisierung ohne Doppelzählung, verdeckte Entfernung, Ablauf, sichtbaren Tod und Reset. Alle 1.333 Checks bestehen; Build ohne Warnungen oder Fehler. Kein neues grafisches Mehrminutenspiel wurde geprüft.
