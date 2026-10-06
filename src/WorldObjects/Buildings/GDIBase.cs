using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class GDIBase : Building, IPerkProvider
{
    public override string GameplayTypeId => "gdi-base";
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

        ApplyCatalogMetadata();
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

        List<PerkGrant> perks = (GameplayCatalog.Find(PurchasableType.Building, GameplayTypeId)?.ProvidedPerks ?? [])
            .Select(perk => perk == PerkType.Home
                ? new PerkGrant(perk, PerkLifetime.WhileProviderOperational, PerkScope.Global, Position)
                : new PerkGrant(perk, PerkLifetime.WhileProviderOperational))
            .ToList();
        if (ArmyId is Guid armyId &&
            SimulationWorld.SimulationArmies.Find(armyId)?.PowerStatus.HasEnoughPower == true)
        {
            if (SatelliteRecon.HasOperator(Globals.World, this))
                perks.Add(new PerkGrant(PerkType.SatelliteOperator, PerkLifetime.WhileProviderOperational));
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
                .. GameplayCatalog.CreateProductionActions(GameplayTypeId),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1)
            ];
            actions = completedActions;
        }
        
        return WithSellAction(actions);
    }

    public override void Update(GameTime gameTime)
    {
        if (IsCompleted)
        {
            Army? army = ArmyId is Guid armyId ? SimulationWorld.SimulationArmies.Find(armyId) : null;

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
