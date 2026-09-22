namespace RTS;

/// <summary>Fully resolved Tiberium seed, decided once by the host and applied identically on every peer.</summary>
public sealed record TiberiumSeedState(
    int CellX,
    int CellZ,
    double CreatedAt,
    float Amount,
    float RotationYRadians,
    int SubType,
    float GrowthFactor,
    float MaxSize);
