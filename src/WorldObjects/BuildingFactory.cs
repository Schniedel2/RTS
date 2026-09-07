using Microsoft.Xna.Framework;
using System;

namespace RTS;

public static class BuildingFactory
{
    public static Building SpawnBuilding(
        string buildingTypeName,
        Vector3 position,
        Guid unitId,
        Guid creatorPlayerId)
    {
        Building building;            
        switch (buildingTypeName.ToLower())
        {
            case "gdi-barracks":
                building = new ConstructionSite(position, unitId, buildingTypeName, 10000);
                break;
            case "gdi-base":
                building = new ConstructionSite(position, unitId, buildingTypeName, 30000);
                break;
            default:
                throw new ArgumentException($"Unknown building type: {buildingTypeName}");
        }
        building.SetCreatorPlayer(creatorPlayerId);
        return (Building)building;
    }
}
