using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public static class UnitFactory
{
    private static readonly Dictionary<string, Func<Vector3, Guid, MobileUnit>> Creators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["soldier"] = (position, id) => new Soldier(position, id),
        ["gunner"] = (position, id) => new Gunner(position, id),
        ["rak-zero"] = (position, id) => new RakZero(position, id),
        ["engineer"] = (position, id) => new Engineer(position, id),
        ["medic"] = (position, id) => new Medic(position, id),
        ["squad-leader"] = (position, id) => new SquadLeader(position, id),
        ["car"] = (position, id) => new Car(position, id),
        ["helicopter"] = (position, id) => new Helicopter(position, id),
        ["heli"] = (position, id) => new Helicopter(position, id),
        ["tank"] = (position, id) => new Tank(position, id),
        ["gepard"] = (position, id) => new Gepard(position, id),
        ["jeep"] = (position, id) => new Jeep(position, id),
        ["motorbike"] = (position, id) => new MotorBike(position, id),
        ["editor"] = (position, id) => new TerrainEditorTool(position, id),
        ["gdi-bulldozer"] = (position, id) => new GDIBulldozer(position, id),
        ["harvester"] = (position, id) => new Harvester(position, id),
    };
    public static IReadOnlyCollection<string> RegisteredTypeIds => Creators.Keys;
    public static bool CanCreate(string? typeId) => typeId is not null && Creators.ContainsKey(typeId.Trim());
    public static bool IsSandboxType(string? typeId) => typeId?.Trim().ToLowerInvariant() is "car" or "editor";

    public static MobileUnit? SpawnUnit(
        string unitTypeName,
        Vector3 position,
        float RotateYDegrees,
        Guid unitId,
        Guid creatorPlayerId)
    {
        if (!Creators.TryGetValue(unitTypeName.Trim(), out var create)) return null;
        MobileUnit unit = create(position, unitId);
        unit.SetRotationYDegrees(RotateYDegrees);
        unit.SetCreatorPlayer(creatorPlayerId);
        unit.SetArmy(creatorPlayerId == Guid.Empty ? null : creatorPlayerId);
        return unit;
    }
}
