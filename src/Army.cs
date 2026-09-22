using System;
using System.Collections.Generic;
using System.Linq;

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
    public int Resources { get; set; }
    public HashSet<Guid> OwnerPlayerIds { get; } = [];
    public Dictionary<Guid, ArmyPermission> GrantedPermissions { get; } = [];
    public IntelligenceCapabilities Intelligence { get; } = new();

    public Army(Guid id, Guid ownerPlayerId, Guid? teamId = null)
    {
        Id = id;
        TeamId = teamId;
        OwnerPlayerIds.Add(ownerPlayerId);
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
        foreach ((Guid playerId, ArmyPermission permission) in first.GrantedPermissions.Concat(second.GrantedPermissions))
            merged.GrantedPermissions[playerId] = permission;

        _armies.Remove(firstArmyId);
        _armies.Remove(secondArmyId);
        _armies.Add(merged.Id, merged);
        return merged;
    }
}
