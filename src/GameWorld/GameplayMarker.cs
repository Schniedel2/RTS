using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public enum GameplayMarkerType
{
    PlayerStart,
    InitialCamera,
    ExpansionSite,
    DefensivePosition,
    ObservationPoint,
    LandingZone,
    ResourceField,
    RallyPoint,
    SpawnPoint,
    Objective,
    TriggerPoint,
    ChokePoint,
    NoBuildZone,
    NoFlyZone,
    AIRegion
}

public enum GameplayMarkerShape
{
    Point,
    Circle,
    Rectangle,
    Path
}

public sealed class GameplayMarker
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public GameplayMarkerType Type { get; set; }
    public Vector3 Position { get; set; }
    public float RotationDegrees { get; set; }
    public GameplayMarkerShape Shape { get; set; }
    public Vector2 Size { get; set; } = Vector2.One;
    public List<Vector3> Points { get; } = [];
    public int? PlayerSlot { get; set; }
    public int? TeamId { get; set; }
    public HashSet<string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record GameplayMarkerState(
    Guid Id,
    string Name,
    GameplayMarkerType Type,
    float X,
    float Y,
    float Z,
    float RotationDegrees,
    GameplayMarkerShape Shape,
    float Width,
    float Height,
    int? PlayerSlot,
    int? TeamId,
    string[] Tags,
    Dictionary<string, string> Properties,
    float[] PathPoints);
