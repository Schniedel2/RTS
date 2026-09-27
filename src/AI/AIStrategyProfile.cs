using System;

namespace RTS;

public enum AIStrategyProfileType
{
    BalancedAssault,
    InfantryCompany,
    AntiArmor,
    FastRecon
}

/// <summary>A match-stable set of small AI variations derived from the match seed and army id.</summary>
public sealed record AIStrategyProfile(
    AIStrategyProfileType Type,
    string DisplayName,
    int RequiredGunners,
    int RequiredRakZero,
    int RequiredTanks,
    float RetreatHealthFraction,
    float AttackReadinessSeconds,
    float DefenseRadiusInCells,
    float AssaultStallTimeoutSeconds,
    int Seed)
{
    public static AIStrategyProfile Create(int matchSeed, Guid armyId)
    {
        int seed = CombineSeed(matchSeed, armyId);
        var random = new Random(seed);
        int roll = random.Next(100);
        AIStrategyProfile profile = roll switch
        {
            < 40 => new(AIStrategyProfileType.BalancedAssault, "Balanced Assault",
                3, 1, 1, 0.45f, 5.0f, 36.0f, 60.0f, seed),
            < 65 => new(AIStrategyProfileType.InfantryCompany, "Infantry Company",
                4, 0, 1, 0.50f, 7.0f, 40.0f, 70.0f, seed),
            < 90 => new(AIStrategyProfileType.AntiArmor, "Anti-Armor",
                2, 2, 2, 0.42f, 6.0f, 34.0f, 65.0f, seed),
            _ => new(AIStrategyProfileType.FastRecon, "Fast Recon",
                2, 1, 1, 0.36f, 2.0f, 30.0f, 45.0f, seed)
        };
        return profile;
    }

    private static int CombineSeed(int matchSeed, Guid armyId)
    {
        byte[] bytes = armyId.ToByteArray();
        uint hash = unchecked((uint)matchSeed) ^ 2166136261u;
        foreach (byte value in bytes)
            hash = unchecked((hash ^ value) * 16777619u);
        return unchecked((int)hash);
    }
}
