using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Linq;

namespace RTS;

public enum AIInfrastructureState
{
    MonitoringStorage,
    BuildingSilo,
    BuildingPower,
    ResearchingAirTechnology,
    BuildingHelipad,
    WaitingForHelicopter,
    AirSupportReady
}

/// <summary>Expands storage when needed and establishes the AI's first air-support base.</summary>
public sealed class AIInfrastructureController(
    GameWorld world,
    Player actor,
    NetworkHandler network)
{
    public const float StorageFreeFractionThreshold = 0.15f;
    public const int ResourceReserve = 800;
    public const int HelipadPowerHeadroom = 15;
    private const float ThinkIntervalSeconds = 1.0f;
    private const float RequestTimeoutSeconds = 3.0f;
    private readonly PlayerCommandService _commands = new(network, actor.Id);
    private float _thinkElapsed;
    private float _requestElapsed;
    private Guid? _requestedBuildingId;
    private string? _requestedBuildingType;

    public AIInfrastructureState State { get; private set; } = AIInfrastructureState.MonitoringStorage;
    public bool IsAirSupportReady => State == AIInfrastructureState.AirSupportReady;
    public string LastDecision { get; private set; } = "Monitoring storage and air-technology requirements.";

    public void Update(GameTime gameTime)
    {
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += elapsed;
        if (_requestedBuildingId is not null)
            _requestElapsed += elapsed;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        Building? requested = _requestedBuildingId is Guid requestedId
            ? world.Units.FindById(requestedId) as Building
            : null;
        if (requested is not null && !requested.IsCompleted)
        {
            LastDecision = $"Constructing {_requestedBuildingType} ({requested.ConstructionPercentage * 100.0f:0}%).";
            return;
        }
        if (requested?.IsCompleted == true)
            ClearBuildRequest();
        else if (_requestedBuildingId is not null && _requestElapsed < RequestTimeoutSeconds)
            return;
        else if (_requestedBuildingId is not null)
            ClearBuildRequest();

        if (NeedsAdditionalStorage())
        {
            State = AIInfrastructureState.BuildingSilo;
            TryBuild("silo");
            return;
        }

        ArmyPowerStatus power = ArmyPowerStatus.Calculate(world.Units.Units, actor.ArmyId);
        if (power.Balance < HelipadPowerHeadroom)
        {
            State = AIInfrastructureState.BuildingPower;
            TryBuild("reaktor");
            return;
        }

        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (army?.Perks.Has(PerkType.AirTechnology) != true)
        {
            State = AIInfrastructureState.ResearchingAirTechnology;
            ResearchAirTechnology(army);
            return;
        }

        Helipad? helipad = world.Units.Units.OfType<Helipad>()
            .FirstOrDefault(building => building.ArmyId == actor.ArmyId && !building.IsDying);
        if (helipad is null)
        {
            State = AIInfrastructureState.BuildingHelipad;
            TryBuild("helipad");
            return;
        }
        if (!helipad.IsCompleted)
        {
            State = AIInfrastructureState.BuildingHelipad;
            LastDecision = $"Constructing helipad ({helipad.ConstructionPercentage * 100.0f:0}%).";
            return;
        }

        Helicopter? helicopter = world.Units.Units.OfType<Helicopter>()
            .FirstOrDefault(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying);
        if (helicopter is null)
        {
            State = AIInfrastructureState.WaitingForHelicopter;
            LastDecision = "Helipad complete; waiting for its included helicopter delivery.";
            return;
        }

        State = AIInfrastructureState.AirSupportReady;
        LastDecision = "Air Technology, powered helipad and first helicopter are ready.";
    }

    private bool NeedsAdditionalStorage()
    {
        Building[] storage = world.Units.Units.OfType<Building>()
            .Where(building => building.ArmyId == actor.ArmyId && building.IsCompleted &&
                !building.IsDying && building.ResourceCapacity > 0.0f)
            .ToArray();
        if (storage.Length == 0)
            return false;
        float capacity = storage.Sum(building => building.ResourceCapacity);
        float free = storage.Sum(building => building.AvailableResourceCapacity);
        bool siloUnderConstruction = world.Units.Units.OfType<Silo>()
            .Any(silo => silo.ArmyId == actor.ArmyId && !silo.IsDying && !silo.IsCompleted);
        return !siloUnderConstruction && free <= Math.Max(
            Harvester.DefaultCargoCapacity, capacity * StorageFreeFractionThreshold);
    }

    private void ResearchAirTechnology(Army? army)
    {
        GDIBase? home = world.Units.Units.OfType<GDIBase>()
            .FirstOrDefault(building => building.ArmyId == actor.ArmyId && building.IsCompleted && !building.IsDying);
        if (home is null || army is null)
        {
            LastDecision = "Air Technology requires an operational base.";
            return;
        }
        bool queued = home.ProductionQueue.Orders.Any(order => string.Equals(
            order.UnitTypeId, ResearchProjects.AirTechnologyId, StringComparison.OrdinalIgnoreCase));
        if (queued)
        {
            LastDecision = "Researching Air Technology.";
            return;
        }
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Research, ResearchProjects.AirTechnologyId, actor.ArmyId, home.UnitId));
        if (!quote.IsAvailable || army.Resources < quote.FinalPrice + ResourceReserve)
        {
            LastDecision = $"Waiting for Air Technology cost plus {ResourceReserve} reserve resources.";
            return;
        }
        _ = _commands.ResearchAsync(home.UnitId, ResearchProjects.AirTechnologyId);
        LastDecision = "Requested Air Technology research.";
    }

    private void TryBuild(string typeId)
    {
        if (_requestedBuildingId is not null)
            return;
        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Building, typeId, actor.ArmyId));
        if (!quote.IsAvailable || army is null || army.Resources < quote.FinalPrice + ResourceReserve)
        {
            LastDecision = $"Waiting for {typeId} cost plus {ResourceReserve} reserve resources.";
            return;
        }
        GDIBulldozer? bulldozer = ArmyGoalController.FindBulldozer(world, actor.ArmyId);
        if (bulldozer is null)
        {
            LastDecision = $"Cannot build {typeId}: no usable bulldozer.";
            return;
        }

        Building preview = typeId switch
        {
            "silo" => new Silo(Vector3.Zero, Guid.NewGuid(), "silo-1", quote.FinalPrice),
            "reaktor" => new Reaktor(Vector3.Zero, Guid.NewGuid(), quote.FinalPrice),
            "helipad" => new Helipad(Vector3.Zero, Guid.NewGuid(), purchasePrice: quote.FinalPrice),
            _ => throw new ArgumentOutOfRangeException(nameof(typeId))
        };
        ArmyGoalController.PreparePreview(preview, actor);
        Building? home = world.Units.Units.OfType<GDIBase>()
            .FirstOrDefault(building => building.ArmyId == actor.ArmyId && !building.IsDying);
        Vector3 origin = home?.Position ?? bulldozer.Position;
        if (!ArmyGoalController.TryFindBuildingSite(world, preview, origin, 7, 24, out Vector3 position))
        {
            LastDecision = $"No valid {typeId} site found near the base; retrying.";
            return;
        }

        _requestedBuildingId = Guid.NewGuid();
        _requestedBuildingType = typeId;
        _requestElapsed = 0.0f;
        _ = _commands.BuildAndConstructAsync(typeId, position, 0.0f,
            [bulldozer.UnitId], _requestedBuildingId.Value);
        LastDecision = $"Requested {typeId} at ({position.X:0.0}, {position.Z:0.0}).";
    }

    private void ClearBuildRequest()
    {
        _requestedBuildingId = null;
        _requestedBuildingType = null;
        _requestElapsed = 0.0f;
    }
}
