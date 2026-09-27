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
            case "gunner":
                unit = new Gunner(position, unitId);
                break;
            case "rak-zero":
                unit = new RakZero(position, unitId);
                break;
            case "engineer":
                unit = new Engineer(position, unitId);
                break;
            case "medic":
                unit = new Medic(position, unitId);
                break;
            case "squad-leader":
                unit = new SquadLeader(position, unitId);
                break;
            case "car":
                unit = new Car(position, unitId);
                break;
            case "helicopter":
            case "heli":
                unit = new Helicopter(position, unitId);
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
            case "harvester":
                unit = new Harvester(position, unitId);
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
