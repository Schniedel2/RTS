using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class CommunicationsTower : Building, IPerkProvider
{
    public override string GameplayTypeId => "communicationstower";
    public const float DetailedHealthRadius = 50.0f;
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public CommunicationsTower(
        Vector3 position,
        Guid unitId,
        int purchasePrice = 0
        ) : base(
            position,
            unitId, purchasePrice)
    {
        ApplyCatalogMetadata();

        SetMesh("antenna-1", deriveDimensions: true);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    public IReadOnlyList<PerkGrant> GetProvidedPerks()
    {
        List<PerkGrant> perks = new(GetHealthPerks());
        if (IsOperational && Occupancy?.IsOperational != false && ArmyId is Guid id &&
            SimulationWorld.SimulationArmies.Find(id)?.PowerStatus.HasEnoughPower == true)
            perks.Add(new(PerkType.SatelliteUplink, PerkLifetime.WhileProviderOperational));
        return perks;
    }

    private IReadOnlyList<PerkGrant> GetHealthPerks() =>
        IsCompleted && !IsDying && Occupancy?.IsOperational != false
            ? [new PerkGrant(PerkType.DetailedHealth,
                PerkLifetime.WhileProviderOperational,
                PerkScope.Radius,
                Position,
                DetailedHealthRadius)]
            : Array.Empty<PerkGrant>();

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions = Array.Empty<UnitAction>();
    
        if (IsCompleted)
        {
            actions = 
            [
            ];
        }
        
        return WithSellAction(actions);
    }

}
