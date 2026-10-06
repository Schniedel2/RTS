using System;
using System.Linq;

namespace RTS;

/// <summary>Army-owned timers, replicated by the host and included in session snapshots.</summary>
public sealed record SatelliteReconState(float Cooldown = 0, float ActiveSeconds = 0);

public static class SatelliteRecon
{
    public const float CooldownSeconds = 180;
    public const float ScanSeconds = 5;

    public static string? MissingRequirement(GameWorld world, Army army)
    {
        if (!army.Perks.Has(PerkType.SatelliteRecon))
            return "Research Satellite Recon in GDI Base";
        if (!ArmyPowerStatus.Calculate(world.Units.Units, army.Id).HasEnoughPower)
            return "Power required";
        if (!world.Units.GetArmyUnits(army.Id).OfType<Building>().Any(building =>
            building.GameplayTypeId == "communicationstower" && building.IsOperational &&
            building.Occupancy?.IsOperational != false))
            return "Operational Communications Tower required";
        if (!world.Units.GetArmyUnits(army.Id).OfType<Building>().Any(building =>
            building.GameplayTypeId == "gdi-base" && building.IsOperational &&
            building.Occupancy?.IsOperational != false && HasOperator(world, building)))
            return "Engineer crew in GDI Base required";
        return null;
    }

    public static bool HasOperator(GameWorld world, Building building) =>
        building.Occupancy?.Occupants.Any(occupant =>
            occupant.Role == OccupantRole.Crew &&
            world.Units.FindById(occupant.UnitId) is Unit unit && !unit.IsDying &&
            unit.ArmyId == building.ArmyId &&
            GameplayCatalog.HasAIRoles(unit.GameplayTypeId, AIUnitRole.Crew)) == true;

    public static bool Ready(GameWorld world, Army army) =>
        army.SatelliteRecon.Cooldown <= 0 && army.SatelliteRecon.ActiveSeconds <= 0 &&
        MissingRequirement(world, army) is null;

    public static SatelliteReconState Advance(GameWorld world, Army army, float seconds) => new(
        Math.Max(0, army.SatelliteRecon.Cooldown - (MissingRequirement(world, army) is null ? seconds : 0)),
        Math.Max(0, army.SatelliteRecon.ActiveSeconds - seconds));

    public static bool Valid(SatelliteReconState state) =>
        float.IsFinite(state.Cooldown) && float.IsFinite(state.ActiveSeconds) &&
        state.Cooldown >= 0 && state.Cooldown <= CooldownSeconds &&
        state.ActiveSeconds >= 0 && state.ActiveSeconds <= ScanSeconds;
}
