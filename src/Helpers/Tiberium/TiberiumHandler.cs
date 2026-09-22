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
    /// Host-only: rolls eligibility and growth randomness for a candidate cell and, on success,
    /// applies it locally. The caller (NetworkHost) then broadcasts the returned state so every
    /// peer applies the exact same result via <see cref="ApplySeed"/> instead of re-rolling -
    /// otherwise host and clients could disagree on whether/how a cell took root.
    /// </summary>
    public bool TryHostSeed(
        Point cell,
        double gameTimeSeconds,
        out TiberiumSeedState state,
        float maxSize = 1.0f,
        float growthFactor = 1.0f,
        float initialAmount = 0.0f)
    {
        state = null!;
        if (!Globals.Game.Network.IsHost)
            return false;

        GameGrid grid = Globals.World.GameGrid;
        if (_cells.ContainsKey(cell) || !grid.Contains(cell) || grid.GetCell(cell).IsBlocked)
            return false;

        TerrainTile tile = GetTileAt(cell);
        if (Random.Shared.NextDouble() > SeedChanceByTile.GetValueOrDefault(tile, 0.5f))
            return false;

        Random random = new Random((int)gameTimeSeconds);
        state = new TiberiumSeedState(
            cell.X,
            cell.Y,
            gameTimeSeconds - initialAmount / GrowthPerSecond,
            initialAmount,
            (float)(random.NextDouble() * Math.PI * 2.0),
            random.Next(3),
            growthFactor * GrowthFactorByTile.GetValueOrDefault(tile, 1.0f),
            maxSize);
        ApplySeed(state);
        return true;
    }

    /// <summary>Applies an already host-decided seed. Idempotent, so the host's own network echo is harmless.</summary>
    public void ApplySeed(TiberiumSeedState state)
    {
        Point cell = new(state.CellX, state.CellZ);
        if (_cells.ContainsKey(cell))
            return;

        _cells[cell] = new TiberiumCell
        {
            CreatedAt = state.CreatedAt,
            Amount = state.Amount,
            RotationYRadians = state.RotationYRadians,
            SubType = state.SubType,
            GrowthFactor = state.GrowthFactor,
            MaxSize = state.MaxSize
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
        // Not gameTime.TotalGameTime: CreatedAt is stamped in the host's time frame (see
        // TiberiumSeedState), which only NetworkHandler.EstimatedHostTime matches on clients too.
        double now = Globals.Game.Network.EstimatedHostTime;
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
