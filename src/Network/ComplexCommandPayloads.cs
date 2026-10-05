using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RTS.Network;

public abstract record ComplexCommandPayload;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CommandPosition(
    [property: JsonRequired] float X,
    [property: JsonRequired] float Y,
    [property: JsonRequired] float Z);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GotoRequestPayload(
    [property: JsonRequired] Guid[] UnitIds,
    [property: JsonRequired] CommandPosition Target,
    bool AppendToQueue = false, UnitRoute[]? Routes = null, float? FormationFacingDegrees = null) : ComplexCommandPayload;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GotoCommandPayload(
    [property: JsonRequired] Guid PlayerId,
    [property: JsonRequired] Guid[] UnitIds,
    [property: JsonRequired] CommandPosition Target,
    [property: JsonRequired] UnitRoute[] Routes,
    bool AppendToQueue = false, float? FormationFacingDegrees = null, Guid? EarthworkOrderId = null) : ComplexCommandPayload;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BuildRequestPayload(
    [property: JsonRequired] string BuildingTypeId,
    [property: JsonRequired] CommandPosition Position,
    [property: JsonRequired] float RotationDegrees,
    Guid? BuildingId = null, Guid[]? WorkerIds = null) : ComplexCommandPayload;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BuildCommandPayload(
    [property: JsonRequired] Guid PlayerId,
    [property: JsonRequired] Guid ArmyId,
    [property: JsonRequired] Guid BuildingId,
    [property: JsonRequired] string BuildingTypeId,
    [property: JsonRequired] CommandPosition Position,
    [property: JsonRequired] float RotationDegrees,
    [property: JsonRequired] int PurchasePrice,
    [property: JsonRequired] int RemainingResources,
    Guid[]? WorkerIds = null) : ComplexCommandPayload;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HarvestRequestPayload(
    [property: JsonRequired] Guid HarvesterId,
    [property: JsonRequired] CommandPosition Target) : ComplexCommandPayload;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HarvestCommandPayload(
    [property: JsonRequired] Guid HarvesterId,
    [property: JsonRequired] HarvestPhase Phase,
    [property: JsonRequired] float CargoAmount) : ComplexCommandPayload;

/// <summary>
/// One typed wire contract for remote and local commands. The existing dispatcher
/// keeps its internal NetworkMessage projection; ambiguous flat JSON is never accepted.
/// </summary>
public static class ComplexCommandPayloads
{
    public const int MaximumUnits = 4096;
    public const int MaximumRouteCells = 262144;
    public static bool Handles(NetworkMessageType type) => type is
        NetworkMessageType.GotoRequest or NetworkMessageType.GotoCommand or
        NetworkMessageType.BuildRequest or NetworkMessageType.BuildCommand or
        NetworkMessageType.HarvestRequest or NetworkMessageType.HarvestCommand;

    public static Type PayloadType(NetworkMessageType type) => type switch
    {
        NetworkMessageType.GotoRequest => typeof(GotoRequestPayload),
        NetworkMessageType.GotoCommand => typeof(GotoCommandPayload),
        NetworkMessageType.BuildRequest => typeof(BuildRequestPayload),
        NetworkMessageType.BuildCommand => typeof(BuildCommandPayload),
        NetworkMessageType.HarvestRequest => typeof(HarvestRequestPayload),
        NetworkMessageType.HarvestCommand => typeof(HarvestCommandPayload),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static ComplexCommandPayload GetPayload(NetworkMessage m)
    {
        if (!TryValidate(m, out string error)) throw new JsonException(error);
        var position = new CommandPosition(m.X, m.Y, m.Z);
        return m.Type switch
        {
            NetworkMessageType.GotoRequest => new GotoRequestPayload(m.UnitIds!, position, m.AppendToQueue, m.Routes, m.FormationFacingDegrees),
            NetworkMessageType.GotoCommand => new GotoCommandPayload(m.PlayerId ?? m.SenderId, m.UnitIds!, position, m.Routes!, m.AppendToQueue, m.FormationFacingDegrees, m.EarthworkOrderId),
            NetworkMessageType.BuildRequest => new BuildRequestPayload(m.UnitTypeId!, position, m.TargetAngleY, m.UnitId, m.UnitIds),
            NetworkMessageType.BuildCommand => new BuildCommandPayload(m.PlayerId!.Value, m.ArmyId!.Value, m.UnitId!.Value, m.UnitTypeId!, position, m.TargetAngleY, m.PurchasePrice, m.ResourceAmount, m.UnitIds),
            NetworkMessageType.HarvestRequest => new HarvestRequestPayload(m.UnitId!.Value, position),
            NetworkMessageType.HarvestCommand => new HarvestCommandPayload(m.UnitId!.Value, m.HarvestPhase!.Value, m.CargoAmount),
            _ => throw new ArgumentOutOfRangeException(nameof(m))
        };
    }

    public static NetworkMessage Create(Guid sender, ComplexCommandPayload payload, double serverTime = 0)
    {
        NetworkMessage message = payload switch
        {
            GotoRequestPayload p when p.Target is not null => new(NetworkMessageType.GotoRequest, sender, PlayerId: sender, UnitIds: p.UnitIds,
                X: p.Target.X, Y: p.Target.Y, Z: p.Target.Z, AppendToQueue: p.AppendToQueue, Routes: p.Routes, FormationFacingDegrees: p.FormationFacingDegrees),
            GotoCommandPayload p when p.Target is not null => new(NetworkMessageType.GotoCommand, sender, PlayerId: p.PlayerId, UnitIds: p.UnitIds,
                X: p.Target.X, Y: p.Target.Y, Z: p.Target.Z, AppendToQueue: p.AppendToQueue, Routes: p.Routes, FormationFacingDegrees: p.FormationFacingDegrees, EarthworkOrderId: p.EarthworkOrderId),
            BuildRequestPayload p when p.Position is not null => new(NetworkMessageType.BuildRequest, sender, PlayerId: sender, UnitTypeId: p.BuildingTypeId,
                UnitId: p.BuildingId, UnitIds: p.WorkerIds, X: p.Position.X, Y: p.Position.Y, Z: p.Position.Z, TargetAngleY: p.RotationDegrees),
            BuildCommandPayload p when p.Position is not null => new(NetworkMessageType.BuildCommand, sender, PlayerId: p.PlayerId, ArmyId: p.ArmyId,
                UnitId: p.BuildingId, UnitTypeId: p.BuildingTypeId, UnitIds: p.WorkerIds, X: p.Position.X, Y: p.Position.Y, Z: p.Position.Z,
                TargetAngleY: p.RotationDegrees, PurchasePrice: p.PurchasePrice, ResourceAmount: p.RemainingResources),
            HarvestRequestPayload p when p.Target is not null => new(NetworkMessageType.HarvestRequest, sender, PlayerId: sender, UnitId: p.HarvesterId,
                X: p.Target.X, Y: p.Target.Y, Z: p.Target.Z),
            HarvestCommandPayload p => new(NetworkMessageType.HarvestCommand, sender, UnitId: p.HarvesterId, HarvestPhase: p.Phase, CargoAmount: p.CargoAmount),
            _ => throw new JsonException("Missing or unsupported complex command payload.")
        };
        message = message with { ServerTime = serverTime };
        if (!TryValidate(message, out string error)) throw new JsonException(error);
        return message;
    }

    public static bool TryValidate(NetworkMessage m, out string error)
    {
        error = "Invalid " + m.Type + " payload.";
        if (!Handles(m.Type)) { error = string.Empty; return true; }
        if (m.SenderId == Guid.Empty || !double.IsFinite(m.ServerTime)) return false;
        bool request = m.Type is NetworkMessageType.GotoRequest or NetworkMessageType.BuildRequest or NetworkMessageType.HarvestRequest;
        if (request && m.PlayerId is Guid player && player != m.SenderId) return false;
        bool Position() => float.IsFinite(m.X) && float.IsFinite(m.Y) && float.IsFinite(m.Z);
        bool Id(Guid? id) => id is Guid value && value != Guid.Empty;
        bool Ids(Guid[]? ids, bool required) => ids is null ? !required :
            (!required || ids.Length > 0) && ids.Length <= MaximumUnits &&
            ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Length;
        bool Building() => Position() && float.IsFinite(m.TargetAngleY) &&
            !string.IsNullOrWhiteSpace(m.UnitTypeId) && m.UnitTypeId.Length <= 128 && Ids(m.UnitIds, false);
        bool valid = m.Type switch
        {
            NetworkMessageType.GotoRequest or NetworkMessageType.GotoCommand =>
                Position() && Ids(m.UnitIds, true) && (m.FormationFacingDegrees is null || float.IsFinite(m.FormationFacingDegrees.Value)) &&
                (m.PlayerId is null || Id(m.PlayerId)) && (m.EarthworkOrderId is null || Id(m.EarthworkOrderId)) && ValidRoutes(m),
            NetworkMessageType.BuildRequest => Building() && (m.UnitId is null || Id(m.UnitId)),
            NetworkMessageType.BuildCommand => Building() && Id(m.UnitId) && Id(m.ArmyId) && Id(m.PlayerId) && m.PurchasePrice >= 0 && m.ResourceAmount >= 0,
            NetworkMessageType.HarvestRequest => Position() && Id(m.UnitId),
            NetworkMessageType.HarvestCommand => Id(m.UnitId) && m.HarvestPhase is HarvestPhase phase && Enum.IsDefined(phase) && float.IsFinite(m.CargoAmount) && m.CargoAmount >= 0,
            _ => false
        };
        if (!valid) return false;
        // Reject unrelated/contradictory data also on the local, non-JSON path.
        NetworkMessage remainder = m.Type switch
        {
            NetworkMessageType.GotoRequest => m with { PlayerId = null, UnitIds = null, X = 0, Y = 0, Z = 0, Routes = null, AppendToQueue = false, FormationFacingDegrees = null },
            NetworkMessageType.GotoCommand => m with { PlayerId = null, UnitIds = null, X = 0, Y = 0, Z = 0, Routes = null, AppendToQueue = false, FormationFacingDegrees = null, EarthworkOrderId = null },
            NetworkMessageType.BuildRequest => m with { PlayerId = null, UnitId = null, UnitIds = null, UnitTypeId = null, X = 0, Y = 0, Z = 0, TargetAngleY = 0 },
            NetworkMessageType.BuildCommand => m with { PlayerId = null, UnitId = null, UnitIds = null, UnitTypeId = null, X = 0, Y = 0, Z = 0, TargetAngleY = 0, ArmyId = null, PurchasePrice = 0, ResourceAmount = 0 },
            NetworkMessageType.HarvestRequest => m with { PlayerId = null, UnitId = null, X = 0, Y = 0, Z = 0 },
            NetworkMessageType.HarvestCommand => m with { UnitId = null, HarvestPhase = null, CargoAmount = 0 },
            _ => m
        };
        if (remainder with { ServerTime = 0 } != new NetworkMessage(m.Type, m.SenderId))
        { error = "Unexpected fields in " + m.Type + " payload."; return false; }
        error = string.Empty;
        return true;
    }

    private static bool ValidRoutes(NetworkMessage message)
    {
        if (message.Routes is null) return message.Type == NetworkMessageType.GotoRequest;
        if (message.Routes.Length > MaximumUnits) return false;
        var units = new HashSet<Guid>(message.UnitIds!);
        var seen = new HashSet<Guid>();
        int total = 0;
        foreach (UnitRoute route in message.Routes)
        {
            if (route is null || !units.Contains(route.UnitId) || !seen.Add(route.UnitId) || route.Cells is null ||
                route.Cells.Length > MaximumRouteCells || (total += route.Cells.Length) > MaximumRouteCells ||
                (route.TargetX is null) != (route.TargetZ is null) ||
                route.TargetX is float x && !float.IsFinite(x) || route.TargetZ is float z && !float.IsFinite(z)) return false;
        }
        return message.Type == NetworkMessageType.GotoRequest || seen.Count == units.Count;
    }
}
