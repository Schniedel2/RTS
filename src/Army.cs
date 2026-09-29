using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS.Network;

namespace RTS;

[Flags]
public enum ArmyPermission
{
    None = 0,
    CommandUnits = 1
}

public sealed class IntelligenceCapabilities
{
    public bool ShareExploredMinimap { get; set; }
    public bool ShareVisibleMinimap { get; set; }
    public bool ShareWorldVision { get; set; }
}

/// <summary>Ownership, resources and unit-command permissions for one army.</summary>
public sealed class Army
{
    public Guid Id { get; }
    public Guid? TeamId { get; internal set; }
    public int Resources { get; set; } = 10000;
    public HashSet<Guid> OwnerPlayerIds { get; } = [];
    public Dictionary<Guid, ArmyPermission> GrantedPermissions { get; } = [];
    public IntelligenceCapabilities Intelligence { get; } = new();
    public ArmyPerkState Perks { get; } = new();
    public ArmyPowerStatus PowerStatus { get; set; } = new();

    public Army(Guid id, Guid ownerPlayerId, Guid? teamId = null)
    {
        Id = id;
        TeamId = teamId;
        OwnerPlayerIds.Add(ownerPlayerId);
    }

    public void Update(GameTime gameTime)
    {
        // Implement any periodic updates for the army here.
        PowerStatus = ArmyPowerStatus.Calculate(Globals.World.Units.Units, Id);        
    }
}

/// <summary>Central authority for army ownership and temporary command sharing.</summary>
public sealed class ArmyHandler
{
    private readonly Dictionary<Guid, Army> _armies = [];
    public IReadOnlyCollection<Army> Armies => _armies.Values;

    public Army EnsureArmy(Guid armyId, Guid ownerPlayerId, Guid? teamId = null)
    {
        if (!_armies.TryGetValue(armyId, out Army? army))
        {
            army = new Army(armyId, ownerPlayerId, teamId);
            _armies.Add(armyId, army);
        }
        else
        {
            army.OwnerPlayerIds.Add(ownerPlayerId);
            if (teamId is not null)
                army.TeamId = teamId;
        }
        return army;
    }

    public Army? Find(Guid armyId) => _armies.GetValueOrDefault(armyId);

    public ArmySnapshot[] GetSnapshot() => _armies.Values.OrderBy(army => army.Id).Select(army => new ArmySnapshot(
        army.Id, army.TeamId, army.Resources, army.OwnerPlayerIds.Order().ToArray(),
        army.GrantedPermissions.OrderBy(item => item.Key)
            .ToDictionary(item => item.Key, item => item.Value),
        new IntelligenceCapabilities
        {
            ShareExploredMinimap = army.Intelligence.ShareExploredMinimap,
            ShareVisibleMinimap = army.Intelligence.ShareVisibleMinimap,
            ShareWorldVision = army.Intelligence.ShareWorldVision
        }, army.Perks.GetSnapshot())).ToArray();

    public void ApplySnapshot(IEnumerable<ArmySnapshot>? states)
    {
        _armies.Clear();
        foreach (ArmySnapshot state in states ?? [])
        {
            Guid owner = state.Owners.FirstOrDefault();
            Army army = new(state.Id, owner, state.TeamId) { Resources = state.Resources };
            army.OwnerPlayerIds.Clear();
            army.OwnerPlayerIds.UnionWith(state.Owners);
            foreach ((Guid playerId, ArmyPermission permission) in state.Permissions)
                army.GrantedPermissions[playerId] = permission;
            army.Intelligence.ShareExploredMinimap = state.Intelligence.ShareExploredMinimap;
            army.Intelligence.ShareVisibleMinimap = state.Intelligence.ShareVisibleMinimap;
            army.Intelligence.ShareWorldVision = state.Intelligence.ShareWorldVision;
            army.Perks.ApplySnapshot(state.Perks);
            _armies[state.Id] = army;
        }
    }

    public bool CanControl(Guid playerId, Guid? armyId)
    {
        if (armyId is not Guid id || !_armies.TryGetValue(id, out Army? army))
            return false;
        return army.OwnerPlayerIds.Contains(playerId) ||
            army.GrantedPermissions.TryGetValue(playerId, out ArmyPermission permission) &&
            permission.HasFlag(ArmyPermission.CommandUnits);
    }

    public bool GrantCommandUnits(Guid armyId, Guid ownerPlayerId, Guid recipientPlayerId)
    {
        if (!_armies.TryGetValue(armyId, out Army? army) || !army.OwnerPlayerIds.Contains(ownerPlayerId))
            return false;
        army.GrantedPermissions[recipientPlayerId] = ArmyPermission.CommandUnits;
        return true;
    }

    public bool RevokeCommandUnits(Guid armyId, Guid ownerPlayerId, Guid recipientPlayerId)
    {
        if (!_armies.TryGetValue(armyId, out Army? army) || !army.OwnerPlayerIds.Contains(ownerPlayerId))
            return false;
        return army.GrantedPermissions.Remove(recipientPlayerId);
    }

    /// <summary>
    /// Permanently joins two armies. The returned army is new; callers must
    /// transfer all Unit.ArmyId references to it as part of the host command.
    /// </summary>
    public Army? Merge(Guid firstArmyId, Guid secondArmyId, Guid mergedArmyId)
    {
        if (firstArmyId == secondArmyId || _armies.ContainsKey(mergedArmyId) ||
            !_armies.TryGetValue(firstArmyId, out Army? first) ||
            !_armies.TryGetValue(secondArmyId, out Army? second))
        {
            return null;
        }

        Guid owner = first.OwnerPlayerIds.First();
        Army merged = new(mergedArmyId, owner, first.TeamId ?? second.TeamId)
        {
            Resources = first.Resources + second.Resources
        };
        merged.OwnerPlayerIds.UnionWith(first.OwnerPlayerIds);
        merged.OwnerPlayerIds.UnionWith(second.OwnerPlayerIds);
        merged.Perks.CopyPermanentFrom(first.Perks);
        merged.Perks.CopyPermanentFrom(second.Perks);
        foreach ((Guid playerId, ArmyPermission permission) in first.GrantedPermissions.Concat(second.GrantedPermissions))
            merged.GrantedPermissions[playerId] = permission;

        _armies.Remove(firstArmyId);
        _armies.Remove(secondArmyId);
        _armies.Add(merged.Id, merged);
        return merged;
    }

    public void ClearPerks()
    {
        foreach (Army army in _armies.Values)
            army.Perks.Clear();
    }

    public void Update(GameTime gameTime)
    {
        foreach (Army army in _armies.Values)
        {
            army.Update(gameTime);
        }
    }
}
