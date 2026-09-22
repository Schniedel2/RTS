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
        Building? building = null;
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
            case "building-4x3x4":
            case "building-1":
                building = new GenericBuilding(position, unitId, "building-1");
                break;
            case "antenna-1":
                building = new GenericBuilding(position, unitId, "antenna-1");
                break;
            case "helipad":
                building = new Helipad(position, unitId, "helipad-1");
                break;
            default:
                return null;
        }
        if (building is null)
            return null;
            
        building.SetCreatorPlayer(creatorPlayerId);
        building.SetArmy(creatorPlayerId == Guid.Empty ? null : creatorPlayerId);
        building.SetRotationYDegrees(RotateYDegrees);
        return (Building)building;
    }
}
