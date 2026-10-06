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
    int Seed,
    float DecisionIntervalSeconds = 1,
    int ResourceReserve = 800,
    float ScoutReconsiderSeconds = 8)
{
    public static AIStrategyProfile Create(int matchSeed, Guid armyId, AIProfileCatalog? profiles = null)
    {
        return AIRuntimeSettings.Default.Select(matchSeed, armyId, profiles ?? AIProfileCatalog.Default);
    }

    internal static int EffectiveSeed(int matchSeed, Guid armyId) => CombineSeed(matchSeed, armyId);

    private static int CombineSeed(int matchSeed, Guid armyId)
    {
        byte[] bytes = armyId.ToByteArray();
        uint hash = unchecked((uint)matchSeed) ^ 2166136261u;
        foreach (byte value in bytes)
            hash = unchecked((hash ^ value) * 16777619u);
        return unchecked((int)hash);
    }
}
