using System;
using System.Linq;

namespace RTS;

/// <summary>Keeps an army's spendable balance and its physical storage displays in sync.</summary>
public static class ArmyResourceService
{
    public static bool TrySpend(Army army, GameWorld world, int amount)
    {
        if (amount < 0 || army.Resources < amount)
            return false;
        if (amount == 0)
            return true;

        army.Resources -= amount;
        float remaining = amount;
        foreach (Building storage in world.Units.GetArmyUnits(army.Id).OfType<Building>()
            .Where(building => building.ArmyId == army.Id && building.IsCompleted &&
                !building.IsDying && building.ResourceCapacity > 0.0f && building.StoredResources > 0.0f)
            .OrderBy(building => building.UnitId))
        {
            remaining -= storage.WithdrawResources(remaining);
            if (remaining <= 0.001f)
                break;
        }
        return true;
    }
}
