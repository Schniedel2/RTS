namespace RTS;

/// <summary>Scalar coordinates keep the rally point compatible with network JSON serialization.</summary>
public readonly record struct RallyPointState(uint Revision, bool HasPosition, float X = 0, float Y = 0, float Z = 0);
