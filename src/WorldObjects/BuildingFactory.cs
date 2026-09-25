using Microsoft.Xna.Framework;
using System;

namespace RTS;

public static class BuildingFactory
{
    public static int GetPurchasePrice(string buildingTypeName) => buildingTypeName.ToLowerInvariant() switch
    {
        "gdi-barracks" => 2500,
        "gdi-base" => 2500,
        "reaktor" => 500,
        "communicationstower" => 500,
        "helipad" => 1500,
        "silo" => 1500,
        "tiberium-refinery" => 4500,
        "vehicle-factory" => 5000,
        "turret-minigun" => 50,
        _ => 0
    };

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
                building = new GDIBarracks(position, unitId, GetPurchasePrice(buildingTypeName));
                break;
            case "gdi-base":
                building = new GDIBase(position, unitId, GetPurchasePrice(buildingTypeName));
                break;
            case "reaktor":
                building = new Reaktor(position, unitId, GetPurchasePrice(buildingTypeName));
                break;
            case "turret-minigun":
                building = new Turret(position, unitId, "gatling-tower-1", GetPurchasePrice(buildingTypeName));
                break;
            case "building-4x3x4":
            case "building-1":
                building = new GenericBuilding(position, unitId, "building-1");
                break;            
            case "vehicle-factory":
                building = new VehicleFactory(position, unitId, "vehicle-factory-1", GetPurchasePrice(buildingTypeName));
                break;
            case "communicationstower":
                building = new CommunicationsTower(position, unitId, GetPurchasePrice(buildingTypeName));
                break;
            case "antenna-1":
                building = new GenericBuilding(position, unitId, "antenna-1");
                break;
            case "helipad":
                building = new Helipad(position, unitId, purchasePrice: GetPurchasePrice(buildingTypeName));
                break;
            case "silo":
                building = new Silo(position, unitId, "silo-1", GetPurchasePrice(buildingTypeName));
                break;
            case "tiberium-refinery":
                building = new TiberiumRefinery(position, unitId, "tiberium-refinery-1", GetPurchasePrice(buildingTypeName));
                break;
            case "tiberium-source":
                building = new TiberiumSource(position, unitId);
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
