using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class GDIBase : Building, IPerkProvider
{
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();
    private float RadarAngleDegree = 0.0f;
    private float RadarSpeedDegreePerSecond = 180.0f;
    private float RadarSpeedFactor = 0.0f;

    public GDIBase(
        Vector3 position,
        Guid unitId,
        int purchasePrice = 0
        ) : base(
            position,
            unitId, purchasePrice)
    {
        SetMesh("gdi-base", deriveDimensions: true);

        TotalBuildingPointsNeeded = 2500;
        HitPoints = MaxHitPoints = 2500;
        PowerConsumption = 40;
    }

    public override void Draw(Effect effect)
    {
        _meshSet?.SetParameter("pivot:radar", MathHelper.ToRadians(RadarAngleDegree));
        base.Draw(effect);
    }

    public IReadOnlyList<PerkGrant> GetProvidedPerks()
    {
        if (!IsOperational || Occupancy?.IsOperational == false)
            return Array.Empty<PerkGrant>();

        List<PerkGrant> perks =
        [
            new PerkGrant(PerkType.Home,
                PerkLifetime.WhileProviderOperational,
                PerkScope.Global,
                Position),
            new PerkGrant(PerkType.BaseEstablished,
                PerkLifetime.WhileProviderOperational)
        ];
        if (ArmyId is Guid armyId &&
            Globals.Game.Armies.Find(armyId)?.PowerStatus.HasEnoughPower == true)
        {
            perks.Add(new PerkGrant(PerkType.Minimap,
                PerkLifetime.WhileProviderOperational));
        }
        return perks;
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions = Array.Empty<UnitAction>();
    
        if (IsCompleted)
        {
            List<UnitAction> completedActions =
            [
                new(UnitActionType.TrainUnit, "Bulldozer", 6, 1),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1)
            ];
            bool researched = ArmyId is Guid armyId &&
                Globals.Game.Armies.Find(armyId)?.Perks.Has(PerkType.AirTechnology) == true;
            bool queued = ProductionQueue.Orders.Any(order => string.Equals(
                order.UnitTypeId, ResearchProjects.AirTechnologyId, StringComparison.OrdinalIgnoreCase));
            if (!researched && !queued)
                completedActions.Add(new(UnitActionType.Research, "Research Air Technology", 4, 4,
                    ResearchProjects.AirTechnologyId, RequiresTarget: false));
            actions = completedActions;
        }
        
        return WithSellAction(actions);
    }

    public override bool TryGetProductionDuration(string unitTypeId, out float durationSeconds)
    {
        durationSeconds = string.Equals(unitTypeId, ResearchProjects.AirTechnologyId,
            StringComparison.OrdinalIgnoreCase) ? 15.0f : 0.0f;
        return durationSeconds > 0.0f;
    }

    public override void Update(GameTime gameTime)
    {
        if (IsCompleted)
        {
            Army? army = ArmyId is Guid armyId ? Globals.Game.Armies.Find(armyId) : null;

            if (army?.PowerStatus.HasEnoughPower == true)
                RadarSpeedFactor += 0.01f;
            else
                RadarSpeedFactor *= 0.99f; // decrease by 1% if not enough power
                
            if (RadarSpeedFactor > 1.0f)
                RadarSpeedFactor = 1.0f;

           RadarAngleDegree += (float)gameTime.ElapsedGameTime.TotalSeconds * RadarSpeedDegreePerSecond * RadarSpeedFactor;
        }
        base.Update(gameTime);
    }
}
