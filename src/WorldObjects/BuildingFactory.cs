using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public static class BuildingFactory
{
    private static readonly Dictionary<string, Func<Vector3, Guid, int, Building>> Creators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gdi-barracks"] = (position, id, price) => new GDIBarracks(position, id, price),
        ["gdi-base"] = (position, id, price) => new GDIBase(position, id, price),
        ["reaktor"] = (position, id, price) => new Reaktor(position, id, price),
        ["turret-minigun"] = (position, id, price) => new Turret(position, id, "gatling-tower-1", price),
        ["building-4x3x4"] = (position, id, price) => new GenericBuilding(position, id, "building-1"),
        ["building-1"] = (position, id, price) => new GenericBuilding(position, id, "building-1"),
        ["vehicle-factory"] = (position, id, price) => new VehicleFactory(position, id, "vehicle-factory-1", price),
        ["communicationstower"] = (position, id, price) => new CommunicationsTower(position, id, price),
        ["command-center"] = (position, id, price) => new CommandCenter(position, id, price),
        ["antenna-1"] = (position, id, price) => new GenericBuilding(position, id, "antenna-1"),
        ["helipad"] = (position, id, price) => new Helipad(position, id, purchasePrice: price),
        ["silo"] = (position, id, price) => new Silo(position, id, "silo-1", price),
        ["tiberium-refinery"] = (position, id, price) => new TiberiumRefinery(position, id, "tiberium-refinery-1", price),
        ["tiberium-source"] = (position, id, price) => new TiberiumSource(position, id),
    };
    public static IReadOnlyCollection<string> RegisteredTypeIds => Creators.Keys;
    public static bool CanCreate(string? typeId) => typeId is not null && Creators.ContainsKey(typeId.Trim());
    public static bool IsSandboxType(string? typeId) => typeId?.Trim().ToLowerInvariant() is "building-1" or "building-4x3x4" or "antenna-1";

    public static Building? SpawnBuilding(
        string buildingTypeName,
        Vector3 position,
        float RotateYDegrees,
        Guid unitId,
        Guid creatorPlayerId,
        int? purchasePrice = null)
    {
        if (!Creators.TryGetValue(buildingTypeName.Trim(), out var create)) return null;
        int price = Math.Max(0, purchasePrice ??
            EconomyCatalog.GetBasePrice(PurchasableType.Building, buildingTypeName));
        Building building = create(position, unitId, price);
        building.SetCreatorPlayer(creatorPlayerId);
        building.SetArmy(creatorPlayerId == Guid.Empty ? null : creatorPlayerId);
        building.SetRotationYDegrees(RotateYDegrees);
        return building;
    }
}
