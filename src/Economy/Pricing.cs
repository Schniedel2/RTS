using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum PurchasableType
{
    Building,
    Unit,
    Research
}

/// <summary>Single source for unmodified building and unit prices.</summary>
public static class EconomyCatalog
{
    private static readonly IReadOnlyList<PerkType> BaseRequired = [PerkType.BaseEstablished];
    private static readonly IReadOnlyList<PerkType> HelipadRequired =
        [PerkType.BaseEstablished, PerkType.AirTechnology];

    public static PurchaseDefinition GetDefinition(PurchasableType type, string objectTypeId)
    {
        if (string.IsNullOrWhiteSpace(objectTypeId)) return new(0, []);
        string id = objectTypeId.Trim().ToLowerInvariant();
        return type switch
        {
            PurchasableType.Building => id switch
            {
                "gdi-base" => new(1500, []),
                "gdi-barracks" => new(800, BaseRequired),
                "reaktor" => new(500, BaseRequired),
                "communicationstower" => new(500, BaseRequired),
                "helipad" => new(1500, HelipadRequired),
                "silo" => new(1500, BaseRequired),
                "tiberium-refinery" => new(1500, BaseRequired),
                "vehicle-factory" => new(2000, BaseRequired),
                "command-center" => new(0, BaseRequired),
                "turret-minigun" => new(1000, BaseRequired),
                _ => new(0, [])
            },
            PurchasableType.Unit => id switch
            {
                "soldier" or "gunner" => new(100, []),
                "rak-zero" or "engineer" => new(300, []),
                "harvester" => new(500, []),
                "helicopter" or "heli" => new(1200, []),
                _ => new(0, [])
            },
            PurchasableType.Research => id switch
            {
                ResearchProjects.AirTechnologyId => new(1000, BaseRequired),
                _ => new(0, [])
            },
            _ => new(0, [])
        };
    }

    public static int GetBasePrice(PurchasableType type, string objectTypeId)
        => GetDefinition(type, objectTypeId).BasePrice;
}

public static class ResearchProjects
{
    public const string AirTechnologyId = "air-technology";

    public static bool TryGetGrantedPerk(string projectId, out PerkType perk)
    {
        if (string.Equals(projectId, AirTechnologyId, StringComparison.OrdinalIgnoreCase))
        {
            perk = PerkType.AirTechnology;
            return true;
        }
        perk = default;
        return false;
    }
}

public sealed record PurchaseDefinition(int BasePrice, IReadOnlyList<PerkType> RequiredPerks);

public sealed record PurchaseRequest(
    PurchasableType Type,
    string ObjectTypeId,
    Guid? ArmyId = null,
    Guid? ProducerUnitId = null);

/// <param name="BasisPoints">Signed change in 1/100 percent; -1000 means a 10% discount.</param>
public sealed record PriceModifier(string Source, int BasisPoints);

public sealed record PurchaseQuote(
    int BasePrice,
    int FinalPrice,
    IReadOnlyList<PriceModifier> Modifiers,
    IReadOnlyList<PerkType> MissingPerks)
{
    public bool CanAfford(int resources) => resources >= FinalPrice;
    public bool IsAvailable => MissingPerks.Count == 0;
}

/// <summary>Optional extension point for upgrades on a particular production building.</summary>
public interface IPurchasePriceModifierProvider
{
    IEnumerable<PriceModifier> GetPriceModifiers(PurchaseRequest request);
}

/// <summary>
/// Resolves the authoritative price for UI, AI and host validation. Army perks
/// and producer upgrades are applied here so callers never reproduce pricing rules.
/// </summary>
public sealed class PricingService
{
    private readonly ArmyHandler _armies;
    private readonly Func<Guid, Unit?> _findUnit;

    public PricingService(ArmyHandler armies, Func<Guid, Unit?> findUnit)
    {
        _armies = armies;
        _findUnit = findUnit;
    }

    public PurchaseQuote GetQuote(PurchaseRequest request)
    {
        PurchaseDefinition definition = EconomyCatalog.GetDefinition(request.Type, request.ObjectTypeId);
        int basePrice = definition.BasePrice;
        List<PriceModifier> modifiers = [];
        Army? army = request.ArmyId is Guid armyId ? _armies.Find(armyId) : null;

        if (army is not null)
            AddArmyPerkModifiers(army, request, modifiers);

        if (request.ProducerUnitId is Guid producerId &&
            _findUnit(producerId) is IPurchasePriceModifierProvider provider)
            modifiers.AddRange(provider.GetPriceModifiers(request));

        int totalBasisPoints = modifiers.Sum(modifier => modifier.BasisPoints);
        int multiplier = Math.Max(0, 10000 + totalBasisPoints);
        int finalPrice = (int)(((long)basePrice * multiplier + 5000) / 10000);
        PerkType[] missingPerks = definition.RequiredPerks
            .Where(perk => army?.Perks.Has(perk) != true)
            .ToArray();
        return new PurchaseQuote(basePrice, finalPrice, modifiers, missingPerks);
    }

    private static void AddArmyPerkModifiers(Army army, PurchaseRequest request,
        ICollection<PriceModifier> modifiers)
    {
        // Global perk-based economy rules are added here when those perks are introduced.
        _ = army;
        _ = request;
        _ = modifiers;
    }
}
