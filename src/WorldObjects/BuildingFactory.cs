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
        Guid creatorPlayerId,
        int? purchasePrice = null)
    {
        int price = Math.Max(0, purchasePrice ??
            EconomyCatalog.GetBasePrice(PurchasableType.Building, buildingTypeName));
        Building? building = null;
        switch (buildingTypeName.ToLower())
        {
            case "gdi-barracks":
                building = new GDIBarracks(position, unitId, price);
                break;
            case "gdi-base":
                building = new GDIBase(position, unitId, price);
                break;
            case "reaktor":
                building = new Reaktor(position, unitId, price);
                break;
            case "turret-minigun":
                building = new Turret(position, unitId, "gatling-tower-1", price);
                break;
            case "building-4x3x4":
            case "building-1":
                building = new GenericBuilding(position, unitId, "building-1");
                break;            
            case "vehicle-factory":
                building = new VehicleFactory(position, unitId, "vehicle-factory-1", price);
                break;
            case "communicationstower":
                building = new CommunicationsTower(position, unitId, price);
                break;
            case "command-center":
                building = new CommandCenter(position, unitId, price);
                break;
            case "antenna-1":
                building = new GenericBuilding(position, unitId, "antenna-1");
                break;
            case "helipad":
                building = new Helipad(position, unitId, purchasePrice: price);
                break;
            case "silo":
                building = new Silo(position, unitId, "silo-1", price);
                break;
            case "tiberium-refinery":
                building = new TiberiumRefinery(position, unitId, "tiberium-refinery-1", price);
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
