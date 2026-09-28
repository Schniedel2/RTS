using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Linq;

namespace RTS;

public enum AISquadPreparationState
{
    WaitingForBarracks,
    TrainingLeader,
    TrainingMedic,
    TrainingGunners,
    TrainingRocketSoldiers,
    Gathering,
    Assembling,
    Ready
}

/// <summary>Produces and assembles the AI's first squad without assigning an offensive mission.</summary>
public sealed class AISquadPreparationController(
    GameWorld world,
    Guid playerId,
    Guid armyId,
    NetworkHandler network,
    AIStrategyProfile? strategyProfile = null,
    AIThreatAssessment? threatAssessment = null)
{
    private static readonly AIProductionNeed LeaderNeed = new(
        AIUnitRole.Leader, AIMovementDomain.Infantry, Defense: 0.3f, Mobility: 0.2f);
    private static readonly AIProductionNeed HealerNeed = new(
        AIUnitRole.Healer, AIMovementDomain.Infantry, Mobility: 0.2f);
    public const int RequiredGunners = 3;
    private const float ThinkIntervalSeconds = 1.0f;
    private const float OrderRetrySeconds = 3.0f;

    private readonly PlayerCommandService _commands = new(network, playerId);
    private readonly AIStrategyProfile _profile = strategyProfile ??
        AIStrategyProfile.Create(0, armyId);
    private readonly AIThreatAssessment _threat = threatAssessment ?? new(armyId);
    private float _thinkElapsed;
    private float _orderElapsed = OrderRetrySeconds;

    public AISquadPreparationState State { get; private set; } = AISquadPreparationState.WaitingForBarracks;
    public bool IsReady => State == AISquadPreparationState.Ready;
    public string LastDecision { get; private set; } = "Waiting for a completed barracks.";

    public void BeginReinforcement()
    {
        State = AISquadPreparationState.WaitingForBarracks;
        _thinkElapsed = ThinkIntervalSeconds;
        _orderElapsed = OrderRetrySeconds;
        LastDecision = "Assessing squad losses and preparing replacements.";
    }

    public void Update(GameTime gameTime, Guid? reservedScoutId)
    {
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += elapsed;
        _orderElapsed += elapsed;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        GDIBarracks? barracks = world.Units.Units.OfType<GDIBarracks>()
            .Where(building => building.ArmyId == armyId && building.IsCompleted && !building.IsDying)
            .OrderBy(building => building.UnitId)
            .FirstOrDefault();
        if (barracks is null)
        {
            State = AISquadPreparationState.WaitingForBarracks;
            LastDecision = "Waiting for a completed barracks before preparing the first squad.";
            return;
        }

        SquadLeader? leader = world.Units.Units.OfType<SquadLeader>()
            .Where(unit => IsAvailable(unit) && HasRole(unit, AIUnitRole.Leader))
            .OrderBy(unit => unit.UnitId).FirstOrDefault();
        Soldier? healer = world.Units.Units.OfType<Soldier>()
            .Where(unit => HasRole(unit, AIUnitRole.Healer))
            .Where(unit => IsAvailableForLeader(unit, leader)).OrderBy(unit => unit.UnitId).FirstOrDefault();
        int requiredCombatSoldiers = _profile.RequiredGunners + _profile.RequiredRakZero;
        Soldier[] combatSoldiers = world.Units.Units.OfType<Soldier>()
            .Where(unit => HasRole(unit, AIUnitRole.Attacker) &&
                !HasRole(unit, AIUnitRole.Leader) && !HasRole(unit, AIUnitRole.Healer))
            .Where(unit => IsAvailableForLeader(unit, leader) && unit.UnitId != reservedScoutId)
            .OrderBy(unit => unit.UnitId).Take(requiredCombatSoldiers).ToArray();

        if (leader is null)
        {
            State = AISquadPreparationState.TrainingLeader;
            TryOrderRole(barracks, LeaderNeed, "squad leader");
            return;
        }
        if (healer is null)
        {
            State = AISquadPreparationState.TrainingMedic;
            TryOrderRole(barracks, HealerNeed, "healer");
            return;
        }
        if (combatSoldiers.Length < requiredCombatSoldiers)
        {
            float infantryBias = _profile.RequiredGunners / (float)Math.Max(1, requiredCombatSoldiers) * 0.5f;
            float vehicleBias = _profile.RequiredRakZero / (float)Math.Max(1, requiredCombatSoldiers) * 0.5f;
            AIProductionNeed combatNeed = _threat.CreateInfantryCombatNeed(infantryBias, vehicleBias);
            State = _threat.Current.AntiVehicleNeed + vehicleBias >
                    _threat.Current.AntiInfantryNeed + infantryBias
                ? AISquadPreparationState.TrainingRocketSoldiers
                : AISquadPreparationState.TrainingGunners;
            TryOrderRole(barracks, combatNeed,
                $"combat soldier {combatSoldiers.Length + 1}/{requiredCombatSoldiers}");
            return;
        }

        if (healer.SquadLeaderId == leader.UnitId &&
            combatSoldiers.All(soldier => soldier.SquadLeaderId == leader.UnitId))
        {
            State = AISquadPreparationState.Ready;
            LastDecision = $"{_profile.DisplayName} squad ready: leader, medic, " +
                $"{requiredCombatSoldiers} threat-adapted combat soldier(s).";
            return;
        }

        Soldier[] members = [healer, .. combatSoldiers];
        float assembleRadius = SquadFormation.AssembleRadiusInCells * world.GameGrid.CellSize;
        bool gathered = members.All(member =>
            HorizontalDistanceSquared(member.Position, leader.Position) <= assembleRadius * assembleRadius);
        if (!gathered)
        {
            State = AISquadPreparationState.Gathering;
            LastDecision = $"Gathering the {_profile.DisplayName} squad at the barracks rally point.";
            if (_orderElapsed >= OrderRetrySeconds)
            {
                Vector3 rallyPoint = barracks.RallyPoint ?? FindFallbackRallyPoint(barracks);
                _ = _commands.GotoAsync([leader.UnitId, .. members.Select(member => member.UnitId)], rallyPoint);
                _orderElapsed = 0.0f;
            }
            return;
        }

        State = AISquadPreparationState.Assembling;
        LastDecision = "Squad members are gathered; waiting for host-confirmed squad assembly.";
        if (_orderElapsed >= OrderRetrySeconds)
        {
            _ = _commands.ExecuteActionAsync([leader.UnitId], UnitActionType.AssembleSquad,
                new UnitActionContext(TargetUnitId: leader.UnitId));
            _orderElapsed = 0.0f;
        }
    }

    private bool IsAvailable(Soldier unit) =>
        unit.ArmyId == armyId && !unit.IsDying && !unit.IsEmbarked;

    private bool IsAvailableForLeader(Soldier unit, SquadLeader? leader) =>
        IsAvailable(unit) && (unit.SquadLeaderId is null || unit.SquadLeaderId == leader?.UnitId ||
            world.Units.FindById(unit.SquadLeaderId.Value) is not SquadLeader previousLeader ||
            previousLeader.IsDying || previousLeader.ArmyId != armyId);

    private void TryOrderRole(GDIBarracks barracks, AIProductionNeed need, string description)
    {
        GameplayDefinition? selected = AIUnitSelector.SelectBest(
            barracks.GameplayTypeId, need, CountOwnedCatalogUnits());
        if (selected is null)
        {
            LastDecision = $"Barracks has no catalog unit for the squad's {description}.";
            return;
        }
        string unitTypeId = selected.TypeId;
        int queued = barracks.ProductionQueue.Orders.Count(order =>
            string.Equals(order.UnitTypeId, unitTypeId, StringComparison.OrdinalIgnoreCase));
        if (queued > 0)
        {
            LastDecision = $"Barracks is training the first squad's {description}.";
            return;
        }

        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Unit, unitTypeId, armyId, barracks.UnitId));
        Army? army = Globals.Game.Armies.Find(armyId);
        if (!quote.IsAvailable || army is null || !quote.CanAfford(army.Resources))
        {
            LastDecision = $"Waiting for {quote.FinalPrice} resources for the first squad's {description}.";
            return;
        }
        if (_orderElapsed < OrderRetrySeconds)
        {
            LastDecision = $"Waiting for confirmation of the first squad's {description}.";
            return;
        }

        _ = _commands.TrainUnitAsync(barracks.UnitId, unitTypeId);
        _orderElapsed = 0.0f;
        LastDecision = $"Ordered the first squad's {description}.";
    }

    private System.Collections.Generic.IReadOnlyDictionary<string, int> CountOwnedCatalogUnits() =>
        world.Units.Units
            .Where(unit => unit.ArmyId == armyId && !unit.IsDying &&
                !string.IsNullOrWhiteSpace(unit.GameplayTypeId))
            .GroupBy(unit => unit.GameplayTypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static bool HasRole(Unit unit, AIUnitRole roles) =>
        GameplayCatalog.Find(PurchasableType.Unit, unit.GameplayTypeId)?.AI is AIUnitMetadata ai &&
        (ai.Roles & roles) == roles;

    private Vector3 FindFallbackRallyPoint(GDIBarracks barracks)
    {
        Vector3 forward = barracks.Transform.Forward;
        forward.Y = 0.0f;
        if (forward.LengthSquared() < 0.001f)
            forward = -Vector3.UnitZ;
        forward.Normalize();
        Vector3 position = barracks.Position + forward * world.GameGrid.CellSize * 5.0f;
        position.Y = world.Terrain.GetSurfaceHeight(position.X, position.Z);
        return position;
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.X - second.X;
        float z = first.Z - second.Z;
        return x * x + z * z;
    }
}
