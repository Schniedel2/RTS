using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public readonly record struct ArmyPowerStatus(int Produced, int Required)
{
    public int Balance => Produced - Required;
    public bool HasEnoughPower => Balance >= 0;

    public static ArmyPowerStatus Calculate(IEnumerable<Unit> units, Guid armyId)
    {
        Unit[] active = units.Where(unit =>
            unit.ArmyId == armyId && !unit.IsDying &&
            (unit is not Building building || building.IsCompleted && building.IsEnabled)).ToArray();
        return new ArmyPowerStatus(
            active.Sum(unit => Math.Max(0, unit.EffectivePowerProduction)),
            active.Sum(unit => Math.Max(0, unit.PowerConsumption)));
    }
}
