using Microsoft.Xna.Framework;
using System;

namespace RTS;

public static class UnitFactory
{
    public static MobileUnit? SpawnUnit(
        string unitTypeName,
        Vector3 position,
        float RotateYDegrees,
        Guid unitId,
        Guid creatorPlayerId)
    {
        MobileUnit? unit;            
        switch (unitTypeName.ToLower())
        {
            case "soldier":
                unit = new Soldier(position, unitId);
                break;
            case "car":
                unit = new Car(position, unitId);
                break;
            case "tank":
                unit = new Tank(position, unitId);
                break;
            case "jeep":
                unit = new Jeep(position, unitId);
                break;
            case "editor":
                unit = new TerrainEditorTool(position, unitId);
                break;
            case "gdi-bulldozer":
                unit = new GDIBulldozer(position, unitId);
                break;
            default:            
                return null;
        }
        unit.SetRotationYDegrees(RotateYDegrees);
        unit.SetCreatorPlayer(creatorPlayerId);
        unit.SetArmy(creatorPlayerId == Guid.Empty ? null : creatorPlayerId);
        return unit;
    }
}
