namespace RTS;

/// <summary>Growth state of Tiberium on a single grid cell.</summary>
public sealed class TiberiumCell
{
    /// <summary>
    /// Local GameTime baseline this cell grows from. Rebased by
    /// <see cref="TiberiumHandler.TryHarvest"/> so a linear re-evaluation from
    /// <c>CreatedAt</c> always reproduces the current <see cref="Amount"/>.
    /// </summary>
    public double CreatedAt { get; set; }
    public float Amount { get; set; }
    public float RotationYRadians { get; set; }
    public int SubType { get; set; }
    public float GrowthFactor { get; set; }
    public float MaxSize { get; set; }
    public float CurrentSize { get; set; }
}
