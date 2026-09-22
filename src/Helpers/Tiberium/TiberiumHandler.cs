using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

/// <summary>
/// Owns per-cell Tiberium growth, seeded by <see cref="TiberiumSource"/> buildings
/// and depleted by harvesters. Growth stage is a pure function of elapsed time so it
/// never needs its own network sync; only <see cref="Amount"/> is gameplay-relevant
/// and stays host-authoritative (see AI/Tiberium-Ressourcen-Architektur.md).
/// </summary>
public sealed class TiberiumHandler
{
    public const float MaximumAmount = 100.0f;
    // Linear growth: fully grown GrowthDurationSeconds after CreatedAt.
    private const float GrowthDurationSeconds = 30.0f;
    private const float GrowthPerSecond = MaximumAmount / GrowthDurationSeconds;

    // Tuned per TerrainTile: how readily Tiberium takes root there, and how fast it then grows.
    private static readonly Dictionary<TerrainTile, float> SeedChanceByTile = new()
    {
        [TerrainTile.Grass] = 0.9f,
        [TerrainTile.GrassWIthDirt] = 0.8f,
        [TerrainTile.GrassWithSoil] = 0.8f,
        [TerrainTile.Dirt] = 0.6f,
        [TerrainTile.DrySoil] = 0.5f,
        [TerrainTile.CrackedSoil] = 0.3f,
        [TerrainTile.Sand] = 0.3f,
        [TerrainTile.DirtWitHStondes] = 0.3f,
        [TerrainTile.Stones] = 0.1f,
        [TerrainTile.Stones2] = 0.1f,
        [TerrainTile.Rock] = 0.05f,
        [TerrainTile.Concrete] = 0.0f,
    };
    private static readonly Dictionary<TerrainTile, float> GrowthFactorByTile = new()
    {
        [TerrainTile.Grass] = 1.2f,
        [TerrainTile.GrassWIthDirt] = 1.1f,
        [TerrainTile.GrassWithSoil] = 1.1f,
        [TerrainTile.Dirt] = 1.0f,
        [TerrainTile.DrySoil] = 0.85f,
        [TerrainTile.CrackedSoil] = 0.75f,
        [TerrainTile.Sand] = 0.7f,
        [TerrainTile.DirtWitHStondes] = 0.65f,
        [TerrainTile.Stones] = 0.2f,
        [TerrainTile.Stones2] = 0.2f,
        [TerrainTile.Rock] = 0.1f,
        [TerrainTile.Concrete] = 0.0f,
    };

    private readonly Dictionary<Point, TiberiumCell> _cells = new Dictionary<Point, TiberiumCell>();
    public IReadOnlyDictionary<Point, TiberiumCell> Cells => _cells;

    public bool HasTiberium(Point cell) => _cells.ContainsKey(cell);

    private static TerrainTile GetTileAt(Point cell)
    {
        Vector3 center = Globals.World.GameGrid.ToWorldPosition(cell, 0.0f);
        int x = Math.Clamp((int)center.X, 0, Globals.World.Terrain.Width - 1);
        int z = Math.Clamp((int)center.Z, 0, Globals.World.Terrain.Height - 1);
        return Globals.World.Terrain.GetTile(x, z);
    }

    /// <summary>
    /// Starts growth on an empty, unblocked cell. No-op if unusable or already occupied.
    /// Whether it takes root at all, and how fast it then grows, both depend on the
    /// underlying TerrainTile.
    /// </summary>
    public void Seed(Point cell, double gameTimeSeconds, float maxSize = 1.0f, float growthFactor = 1.0f, float initialAmount = 0.0f)
    {
        GameGrid grid = Globals.World.GameGrid;
        if (_cells.ContainsKey(cell) || !grid.Contains(cell) || grid.GetCell(cell).IsBlocked)
            return;

        TerrainTile tile = GetTileAt(cell);
        if (Random.Shared.NextDouble() > SeedChanceByTile.GetValueOrDefault(tile, 0.5f))
            return;

        Random random = new Random((int)(gameTimeSeconds));
        _cells[cell] = new TiberiumCell
        {
            CreatedAt = gameTimeSeconds - initialAmount / GrowthPerSecond,
            Amount = initialAmount,
            RotationYRadians = (float)(random.NextDouble() * Math.PI * 2.0),
            SubType = random.Next(3),            
            GrowthFactor = growthFactor * GrowthFactorByTile.GetValueOrDefault(tile, 1.0f),
            MaxSize = maxSize
        };
    }

    /// <summary>Removes up to <paramref name="amount"/> and rebases the growth curve to match what's left.</summary>
    public float TryHarvest(Point cell, float amount, double gameTimeSeconds)
    {
        if (!_cells.TryGetValue(cell, out TiberiumCell? tiberium))
            return 0.0f;

        float harvested = MathF.Min(amount, tiberium.Amount);
        float remaining = tiberium.Amount - harvested;
        if (remaining <= 0.0f)
            _cells.Remove(cell);
        else
        {
            tiberium.Amount = remaining;
            tiberium.CreatedAt = gameTimeSeconds - remaining / GrowthPerSecond;
        }
        return harvested;
    }

    public void Update(GameTime gameTime)
    {
        double now = gameTime.TotalGameTime.TotalSeconds;
        foreach (TiberiumCell cell in _cells.Values)
        {
            cell.Amount = MathF.Min(MaximumAmount, MathF.Max(0.0f, (float)(now - cell.CreatedAt) * GrowthPerSecond * cell.GrowthFactor));
            cell.CurrentSize = cell.Amount / MaximumAmount;
        }
    }

    public void Draw(Effect effect)
    {
        if (_cells.Count == 0)
            return;

        Mesh mesh = Globals.MeshHandler.Meshes["tiberium-1"];
        GameGrid grid = Globals.World.GameGrid;
        foreach ((Point cell, TiberiumCell tiberium) in _cells)
        {
            if (tiberium.Amount <= 0.0f)
                continue;

            Vector3 position = grid.ToWorldPosition(cell, 0.0f);
            position.Y = Globals.World.Terrain.GetSurfaceHeight(position.X, position.Z);
            mesh.Draw(effect, Matrix.CreateRotationY(tiberium.RotationYRadians) * Matrix.CreateScale(tiberium.CurrentSize) * Matrix.CreateTranslation(position));
        }
    }
}
