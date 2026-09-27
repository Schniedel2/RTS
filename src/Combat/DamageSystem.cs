using System;

namespace RTS;

[Flags]
public enum TargetDomain
{
    None = 0,
    Ground = 1,
    Air = 2
}

public enum ArmorClass
{
    Infantry,
    LightVehicle,
    HeavyVehicle,
    Building
}

public enum DamageType
{
    SmallArms,
    ArmorPiercing,
    Explosive,
    AntiTank
}

public readonly record struct WeaponProfile(
    float Damage,
    DamageType DamageType,
    TargetDomain AllowedTargets);

public static class DamageCalculator
{
    public static float Calculate(float baseDamage, DamageType damageType, ArmorClass armor)
    {
        if (!float.IsFinite(baseDamage) || baseDamage <= 0.0f)
            return 0.0f;

        float multiplier = (damageType, armor) switch
        {
            (DamageType.SmallArms, ArmorClass.Infantry) => 1.00f,
            (DamageType.SmallArms, ArmorClass.LightVehicle) => 0.25f,
            (DamageType.SmallArms, ArmorClass.HeavyVehicle) => 0.05f,
            (DamageType.SmallArms, ArmorClass.Building) => 0.10f,

            (DamageType.ArmorPiercing, ArmorClass.Infantry) => 1.00f,
            (DamageType.ArmorPiercing, ArmorClass.LightVehicle) => 0.70f,
            (DamageType.ArmorPiercing, ArmorClass.HeavyVehicle) => 0.35f,
            (DamageType.ArmorPiercing, ArmorClass.Building) => 0.25f,

            (DamageType.Explosive, ArmorClass.Infantry) => 1.25f,
            (DamageType.Explosive, ArmorClass.LightVehicle) => 0.80f,
            (DamageType.Explosive, ArmorClass.HeavyVehicle) => 0.50f,
            (DamageType.Explosive, ArmorClass.Building) => 1.00f,

            (DamageType.AntiTank, ArmorClass.Infantry) => 0.50f,
            (DamageType.AntiTank, ArmorClass.LightVehicle) => 1.25f,
            (DamageType.AntiTank, ArmorClass.HeavyVehicle) => 1.50f,
            (DamageType.AntiTank, ArmorClass.Building) => 0.80f,
            _ => 1.0f
        };
        return baseDamage * multiplier;
    }
}
