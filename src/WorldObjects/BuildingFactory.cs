using Microsoft.Xna.Framework;
using System;

namespace RTS;

public static class BuildingFactory
{
    public static Building? SpawnBuilding(
        string buildingTypeName,
        Vector3 position,
        float RotateYDegrees,
        Guid unitId,
        Guid creatorPlayerId)
    {
        Building building;            
        switch (buildingTypeName.ToLower())
        {
            case "gdi-barracks":
                building = new GDIBarracks(position, unitId);
                break;
            case "gdi-base":
                building = new GDIBase(position, unitId);
                break;
            case "reaktor":
                building = new Reaktor(position, unitId);
                break;
            default:
                return null;
        }
        building.SetCreatorPlayer(creatorPlayerId);
        building.SetArmy(creatorPlayerId == Guid.Empty ? null : creatorPlayerId);
        building.SetRotationYDegrees(RotateYDegrees);
        return (Building)building;
    }
}
