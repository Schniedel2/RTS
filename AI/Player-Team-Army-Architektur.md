# Player-, Team- und Army-Architektur

Stand: Entwurfsentscheidung; diese Architektur ist noch nicht vollständig implementiert.

## Begriffe und Zuständigkeiten

`Player` ist ein menschlicher Spieler oder eine AI-Instanz.

`Team` beschreibt Diplomatie und Spielziel. Ein Player kann von Anfang an einem Team
angehören, je nach Spielmodus aber auch während der Partie einem Team beitreten oder
es verlassen. Teamzugehörigkeit allein erlaubt **keine** Kontrolle fremder Units.

`Army` beschreibt Besitz, Steuerrechte und Ressourcen. Eine Army kann von einem oder
mehreren Playern gesteuert werden. Ressourcen gehören immer der Army, nicht einem
einzelnen Player und nicht dem Team.

Eine Unit bzw. ein Gebäude besitzt eine nullable `ArmyId`:

- `ArmyId != null`: aktueller Besitz und Steuerrecht werden über diese Army bestimmt.
- `ArmyId == null`: neutrales Objekt, zum Beispiel ein unbemanntes Fahrzeug. Es kann
  erst nach Übernahme/Bemannen/Hacken einer Army zugewiesen werden.

`CreatorPlayerId` bleibt als optionale historische Information erhalten: Wer hat die
Unit ursprünglich erzeugt? Für aktuellen Besitz oder Befehlsrechte darf diese ID nicht
verwendet werden.

## Team, Freigabe, Geschenk und Fusion

Es gibt vier bewusst getrennte Mechaniken:

1. **Team**: Diplomatie und Siegbedingungen. Teammitglieder bleiben zunächst Besitzer
   eigener Armeen.
2. **CommandUnits-Freigabe**: Ein Army-Besitzer kann einem Teamkollegen zeitweise
   erlauben, die Units dieser Army zu befehligen. Die Freigabe ist widerrufbar und
   erlaubt anfänglich ausschließlich Unit-Befehle — kein Bauen, Verkaufen,
   Ressourcen-Ausgeben, Diplomatie oder Produktionsverwaltung.
3. **Schenken/Transfer**: Einzelne Units oder Gebäude wechseln dauerhaft in die Army
   eines Teamkollegen. `CreatorPlayerId` bleibt unverändert, `ArmyId` wird geändert.
4. **Fusion**: Zwei oder mehr Armeen werden dauerhaft zu einer gemeinsamen Army
   zusammengeführt. Alle Units, Gebäude, Ressourcen und Besitzer/Controller werden
   übernommen. Eine Fusion wird absichtlich nicht wieder aufgeteilt. Sie eignet sich
   etwa für zwei Menschen, die bei ungerader Spielerzahl zusammen eine Armee spielen.

Eine mögliche spätere Rechtebasis:

```csharp
[Flags]
public enum ArmyPermission
{
    None = 0,
    CommandUnits = 1,
    Build = 2,
    UseProduction = 4,
    SpendResources = 8,
    SellBuildings = 16
}
```

Für den ersten Schritt genügt `CommandUnits`.

## Empfohlenes Datenmodell

```csharp
public sealed class Team
{
    public Guid Id { get; init; }
    public string Name { get; set; } = "";
    public HashSet<Guid> PlayerIds { get; } = [];
}

public sealed class Army
{
    public Guid Id { get; init; }
    public Guid? TeamId { get; set; }
    public HashSet<Guid> OwnerPlayerIds { get; } = [];
    public Dictionary<Guid, ArmyPermission> GrantedPermissions { get; } = [];
    public int Resources { get; set; }
}

public sealed class Player
{
    public Guid Id { get; init; }
    public Guid? TeamId { get; set; }
    public Guid ArmyId { get; set; }
}
```

Die zentrale Prüfung muss immer Army-basiert erfolgen:

```csharp
bool CanControl(Guid playerId, Army army) =>
    army.OwnerPlayerIds.Contains(playerId) ||
    army.GrantedPermissions.TryGetValue(playerId, out ArmyPermission rights) &&
    rights.HasFlag(ArmyPermission.CommandUnits);
```

## Netzwerk und Autorität

Teamwechsel, Freigaben, Geschenke und Fusionen sind Host-Operationen. Der Host
erzeugt hierfür zustandsändernde Commands und verteilt sie an alle Clients.

Unit-Befehle können von mehreren berechtigten Playern gleichzeitig kommen. Der Host
verarbeitet die gültigen Befehle in Empfangsreihenfolge; der zuletzt verarbeitete
Befehl bestimmt den aktuellen Unit-Command.

## Sichtbare Auswahl von Mitspielern

Eine Auswahl ist kein autoritativer Spielzustand und gibt keinerlei Rechte. Sie dient
nur als UI-Präsenzinformation, besonders bei fusionierten oder freigegebenen Armeen.

Vorgesehene Nachricht:

```csharp
NotifyUnitsSelected(Guid playerId, Guid[] unitIds, uint selectionRevision)
```

- Sie wird ausschließlich bei einer tatsächlichen Auswahländerung gesendet.
- Ein leeres `unitIds`-Array bedeutet: Auswahl geleert.
- Der Client sendet an den Host, der sie wie andere Nachrichten broadcastet.
- Empfänger entscheiden lokal, ob sie diese Auswahl darstellen möchten. Üblich ist:
  nur bei Armeen, die der lokale Player ebenfalls steuern darf.
- Remote-Auswahl darf nicht `Unit.IsSelected` verändern. Lokale Auswahl bleibt lokal;
  Remote-Auswahlen werden etwa als `Dictionary<Guid, HashSet<Guid>>` nach Player-ID
  gespeichert und mit einer zusätzlichen Player-Farbe gerendert.
- Beim Disconnect eines Players wird dessen Remote-Auswahl lokal entfernt.

Broadcast ist bewusst der einfache Weg: Es gibt keine Sonder-Routing-Logik und keine
Autorisierung für diese reine UI-Nachricht. Spätere Fog-of-War- oder Zuschauerregeln
können die Darstellung einschränken.
