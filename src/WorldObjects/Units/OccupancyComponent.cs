using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum OccupantRole
{
    Driver,
    Crew,
    Passenger,
    Garrison
}

public enum OccupancyOwnershipMode
{
    PreserveOwnership,
    ControllerDefinesOwnership,
    CaptureOnEntry
}

public sealed record OccupantSlot(OccupantRole Role, int Capacity, int MinimumRequired = 0);
public sealed record OccupantAssignment(Guid UnitId, OccupantRole Role);

/// <summary>
/// Optional composition component for vehicles and buildings which can contain
/// other units. It deliberately owns neither movement nor rendering behavior.
/// </summary>
public sealed class OccupancyComponent
{
    private readonly Unit _container;
    private readonly List<OccupantSlot> _slots;
    private readonly List<OccupantAssignment> _occupants = [];
    private readonly Dictionary<Guid, OccupantRole> _reservations = [];

    public IReadOnlyList<OccupantSlot> Slots => _slots;
    public IReadOnlyList<OccupantAssignment> Occupants => _occupants;
    public OccupancyOwnershipMode OwnershipMode { get; }
    public OccupantRole? ControllerRole { get; }
    public bool BecomeNeutralWithoutController { get; }
    public bool EntryEnabled { get; set; } = true;

    public bool IsOperational => _slots.All(slot =>
        _occupants.Count(item => item.Role == slot.Role) >= slot.MinimumRequired);

    public int Count(OccupantRole role) => _occupants.Count(item => item.Role == role);

    public float GetFillRatio(OccupantRole role)
    {
        int capacity = _slots.Where(slot => slot.Role == role).Sum(slot => slot.Capacity);
        return capacity == 0 ? 0.0f : Math.Clamp((float)Count(role) / capacity, 0.0f, 1.0f);
    }

    /// <summary>0..1 fulfillment of required slots; one when no crew is required.</summary>
    public float OperationalEfficiency
    {
        get
        {
            int required = _slots.Sum(slot => slot.MinimumRequired);
            if (required == 0)
                return 1.0f;
            int filled = _slots.Sum(slot => Math.Min(
                slot.MinimumRequired,
                _occupants.Count(item => item.Role == slot.Role)));
            return (float)filled / required;
        }
    }

    public OccupancyComponent(
        Unit container,
        IEnumerable<OccupantSlot> slots,
        OccupancyOwnershipMode ownershipMode,
        OccupantRole? controllerRole = null,
        bool becomeNeutralWithoutController = false)
    {
        _container = container;
        _slots = slots.Where(slot => slot.Capacity > 0).ToList();
        OwnershipMode = ownershipMode;
        ControllerRole = controllerRole;
        BecomeNeutralWithoutController = becomeNeutralWithoutController;
    }

    public bool CanEnter(Unit unit, OccupantRole? requestedRole = null) =>
        TryResolveRole(unit, requestedRole, includeReservations: true, out _);

    public bool TryReserve(Unit unit, OccupantRole? requestedRole, out OccupantRole role)
    {
        if (!TryResolveRole(unit, requestedRole, includeReservations: true, out role))
            return false;
        _reservations[unit.UnitId] = role;
        return true;
    }

    public bool TryAdd(Unit unit, OccupantRole? requestedRole, out OccupantRole role)
    {
        OccupantRole? reservedRole = _reservations.TryGetValue(unit.UnitId, out OccupantRole reserved)
            ? reserved
            : requestedRole;
        if (!TryResolveRole(unit, reservedRole, includeReservations: false, out role))
            return false;

        _reservations.Remove(unit.UnitId);
        _occupants.Add(new OccupantAssignment(unit.UnitId, role));

        if (OwnershipMode == OccupancyOwnershipMode.CaptureOnEntry && _container.ArmyId is null)
            _container.SetArmy(unit.ArmyId);
        else if (OwnershipMode == OccupancyOwnershipMode.ControllerDefinesOwnership &&
                 (ControllerRole is null || role == ControllerRole))
            _container.SetArmy(unit.ArmyId);
        return true;
    }

    public bool TryRemove(Guid unitId, out OccupantAssignment? assignment)
    {
        assignment = _occupants.FirstOrDefault(item => item.UnitId == unitId);
        if (assignment is null)
            return false;
        _occupants.Remove(assignment);

        if (OwnershipMode == OccupancyOwnershipMode.ControllerDefinesOwnership &&
            BecomeNeutralWithoutController &&
            (ControllerRole is null || !_occupants.Any(item => item.Role == ControllerRole)))
        {
            _container.Stop();
            _container.SetArmy(null);
            _container.Behavior = UnitBehavior.Passive;
        }
        return true;
    }

    public void ClearReservation(Guid unitId) => _reservations.Remove(unitId);

    public OccupantAssignment? GetController() => ControllerRole is OccupantRole role
        ? _occupants.FirstOrDefault(item => item.Role == role)
        : null;

    public Guid? GetPreferredOccupantToLeave() =>
        GetController()?.UnitId ?? _occupants.FirstOrDefault()?.UnitId;

    public bool IsReservedBy(Guid unitId) => _reservations.ContainsKey(unitId);
    public bool TryGetReservedRole(Guid unitId, out OccupantRole role) =>
        _reservations.TryGetValue(unitId, out role);

    private bool TryResolveRole(
        Unit unit,
        OccupantRole? requestedRole,
        bool includeReservations,
        out OccupantRole role)
    {
        role = default;
        if (!EntryEnabled || _container.IsDying || unit.IsDying || unit.IsEmbarked || unit.ArmyId is null ||
            _occupants.Any(item => item.UnitId == unit.UnitId))
            return false;

        // Neutral containers may be captured. Owned containers only accept
        // members of their own Army; hostile takeover can later be a separate rule.
        if (_container.ArmyId is Guid ownerArmy && unit.ArmyId != ownerArmy)
            return false;

        IEnumerable<OccupantSlot> candidates = requestedRole is OccupantRole requested
            ? _slots.Where(slot => slot.Role == requested)
            : _slots;
        foreach (OccupantSlot slot in candidates)
        {
            int used = _occupants.Count(item => item.Role == slot.Role);
            if (includeReservations)
                used += _reservations.Count(item => item.Key != unit.UnitId && item.Value == slot.Role);
            if (used < slot.Capacity)
            {
                role = slot.Role;
                return true;
            }
        }
        return false;
    }
}
