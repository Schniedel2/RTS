using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RTS;

/// <summary>
/// Owns per-cell Tiberium growth, seeded by <see cref="TiberiumSource"/> buildings
/// and depleted by harvesters. Growth stage is a pure function of elapsed time so it
/// never needs its own network sync; only <see cref="Amount"/> is gameplay-relevant
/// and stays host-authoritative (see AI/Tiberium-Ressourcen-Architektur.md).
/// </summary>
public sealed class TiberiumHandler
{
    private sealed class RenderChunk
    {
        public HashSet<Point> Cells { get; } = [];
        public BoundingBox Bounds;
        public long TerrainRevision = -1;
        public bool BoundsDirty = true;
    }

    private const int RenderChunkSize = 16;
    public const string FileName = "tiberium-cells.json";
    public const float MaximumAmount = 100.0f;
    // Linear growth: fully grown GrowthDurationSeconds after CreatedAt.
    private const float GrowthDurationSeconds = 30.0f;
    private const float GrowthPerSecond = MaximumAmount / GrowthDurationSeconds;
    private const float EmissivePulseStrength = 0.65f;
    private const float EmissivePulseSpeed = 2.4f;

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
    private readonly Dictionary<Point, RenderChunk> _renderChunks = [];
    private float _visualTimeSeconds;
    public IReadOnlyDictionary<Point, TiberiumCell> Cells => _cells;
    public int RenderChunkCount => _renderChunks.Count;
    public int LastVisibleChunkCount { get; private set; }
    public int LastDrawnCellCount { get; private set; }

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
        AddToRenderChunk(cell);
    }

    /// <summary>Removes up to <paramref name="amount"/> and rebases the growth curve to match what's left.</summary>
    public float TryHarvest(Point cell, float amount, double gameTimeSeconds)
    {
        if (!_cells.TryGetValue(cell, out TiberiumCell? tiberium))
            return 0.0f;

        float harvested = MathF.Min(amount, tiberium.Amount);
        float remaining = tiberium.Amount - harvested;
        if (remaining <= 0.0f)
        {
            _cells.Remove(cell);
            RemoveFromRenderChunk(cell);
        }
        else
        {
            tiberium.Amount = remaining;
            tiberium.CreatedAt = gameTimeSeconds - remaining / GrowthPerSecond;
        }
        return harvested;
    }

    /// <summary>Applies the host-authoritative amount remaining after a harvest tick.</summary>
    public void ApplyHarvest(Point cell, float remainingAmount, double gameTimeSeconds)
    {
        if (remainingAmount <= 0.0f)
        {
            if (_cells.Remove(cell)) RemoveFromRenderChunk(cell);
            return;
        }
        if (!_cells.TryGetValue(cell, out TiberiumCell? tiberium)) return;
        tiberium.Amount = remainingAmount;
        tiberium.CreatedAt = gameTimeSeconds - remainingAmount /
            (GrowthPerSecond * Math.Max(0.0001f, tiberium.GrowthFactor));
        tiberium.CurrentSize = remainingAmount / MaximumAmount * tiberium.MaxSize;
    }

    public void Update(GameTime gameTime, bool allowGrowth = true)
    {
        // The visual pulse keeps running while growth is paused in editor mode.
        _visualTimeSeconds = (_visualTimeSeconds + (float)gameTime.ElapsedGameTime.TotalSeconds) % 10000.0f;

        // Not gameTime.TotalGameTime: CreatedAt is stamped in the host's time frame (see
        // TiberiumSeedState), which only NetworkHandler.EstimatedHostTime matches on clients too.
        double now = Globals.Game.Network.EstimatedHostTime;
        foreach (TiberiumCell cell in _cells.Values)
        {
            if (allowGrowth)
                cell.Amount = MathF.Min(MaximumAmount, MathF.Max(0.0f, (float)(now - cell.CreatedAt) * GrowthPerSecond * cell.GrowthFactor));
            else
                RebaseGrowth(cell, now);
            cell.CurrentSize = cell.Amount / MaximumAmount * cell.MaxSize;
        }
    }

    public void Paint(IEnumerable<Point> cells, float amount = MaximumAmount * 0.25f)
    {
        double now = Globals.Game.Network.EstimatedHostTime;
        foreach (Point point in cells.Where(Globals.World.GameGrid.Contains))
        {
            if (_cells.TryGetValue(point, out TiberiumCell? existing))
            {
                existing.Amount = Math.Clamp(amount, 0, MaximumAmount);
                RebaseGrowth(existing, now);
                existing.CurrentSize = existing.Amount / MaximumAmount * existing.MaxSize;
                continue;
            }
            TerrainTile tile = GetTileAt(point);
            if (GrowthFactorByTile.GetValueOrDefault(tile, 0) <= 0) continue;
            int hash = HashCode.Combine(point.X, point.Y);
            TiberiumCell cell = new()
            {
                Amount = Math.Clamp(amount, 0, MaximumAmount),
                RotationYRadians = MathHelper.TwoPi * (Math.Abs(hash % 1000) / 1000.0f),
                SubType = Math.Abs(hash % 3),
                GrowthFactor = GrowthFactorByTile.GetValueOrDefault(tile, 1),
                MaxSize = 1
            };
            RebaseGrowth(cell, now);
            cell.CurrentSize = cell.Amount / MaximumAmount;
            _cells[point] = cell;
            AddToRenderChunk(point);
        }
    }

    public void Remove(IEnumerable<Point> cells)
    {
        foreach (Point point in cells)
            if (_cells.Remove(point)) RemoveFromRenderChunk(point);
    }

    /// <summary>Advances only selected resource cells and sources; spreading cannot leave the selection.</summary>
    public void SimulateArea(IReadOnlySet<Point> area, float seconds, IEnumerable<TiberiumSource> sources)
    {
        if (seconds <= 0 || area.Count == 0) return;
        double now = Globals.Game.Network.EstimatedHostTime;
        foreach ((Point point, TiberiumCell cell) in _cells.Where(pair => area.Contains(pair.Key)).ToArray())
        {
            cell.Amount = MathF.Min(MaximumAmount, cell.Amount + seconds * GrowthPerSecond * cell.GrowthFactor);
            RebaseGrowth(cell, now);
            cell.CurrentSize = cell.Amount / MaximumAmount * cell.MaxSize;
        }

        int attempts = Math.Max(1, (int)Math.Floor(seconds / TiberiumSource.SpreadIntervalSeconds));
        foreach (TiberiumSource source in sources)
        {
            Point center = Globals.World.GameGrid.ToCell(source.Position);
            if (!area.Contains(center)) continue;
            List<Point> candidates = area.Where(cell =>
                Math.Abs(cell.X - center.X) <= TiberiumSource.SpreadRadius &&
                Math.Abs(cell.Y - center.Y) <= TiberiumSource.SpreadRadius &&
                !_cells.ContainsKey(cell)).ToList();
            for (int attempt = 0; attempt < attempts && candidates.Count > 0; attempt++)
            {
                int candidateIndex = Random.Shared.Next(candidates.Count);
                Point candidate = candidates[candidateIndex];
                candidates.RemoveAt(candidateIndex);
                if (TryEditorSeed(candidate, now) && _cells.TryGetValue(candidate, out TiberiumCell? seeded))
                {
                    float remainingSeconds = Math.Max(0, seconds - (float)((attempt + 1) * TiberiumSource.SpreadIntervalSeconds));
                    seeded.Amount = MathF.Min(MaximumAmount, remainingSeconds * GrowthPerSecond * seeded.GrowthFactor);
                    RebaseGrowth(seeded, now);
                    seeded.CurrentSize = seeded.Amount / MaximumAmount * seeded.MaxSize;
                }
            }
        }
    }

    public TiberiumSeedState[] GetStates()
    {
        double now = Globals.Game.Network.EstimatedHostTime;
        return _cells.Select(pair => new TiberiumSeedState(pair.Key.X, pair.Key.Y,
            now - pair.Value.Amount / (GrowthPerSecond * Math.Max(0.0001f, pair.Value.GrowthFactor)),
            pair.Value.Amount, pair.Value.RotationYRadians, pair.Value.SubType,
            pair.Value.GrowthFactor, pair.Value.MaxSize)).ToArray();
    }

    public void ApplyMapStates(IEnumerable<TiberiumSeedState>? states)
    {
        _cells.Clear();
        _renderChunks.Clear();
        if (states is null) return;
        double now = Globals.Game.Network.EstimatedHostTime;
        foreach (TiberiumSeedState state in states)
        {
            if (!Globals.World.GameGrid.Contains(new Point(state.CellX, state.CellZ))) continue;
            ApplySeed(state with { CreatedAt = now - state.Amount / (GrowthPerSecond * Math.Max(0.0001f, state.GrowthFactor)) });
        }
        Update(new GameTime(), false);
    }

    public void Save(string mapDirectory)
    {
        Directory.CreateDirectory(mapDirectory);
        File.WriteAllText(Path.Combine(mapDirectory, FileName), JsonSerializer.Serialize(GetStates(), new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Load(string mapDirectory)
    {
        string path = Path.Combine(mapDirectory, FileName);
        ApplyMapStates(File.Exists(path) ? JsonSerializer.Deserialize<TiberiumSeedState[]>(File.ReadAllText(path)) : null);
    }

    private bool TryEditorSeed(Point cell, double now)
    {
        if (_cells.ContainsKey(cell) || !Globals.World.GameGrid.Contains(cell) || Globals.World.GameGrid.GetCell(cell).IsBlocked) return false;
        TerrainTile tile = GetTileAt(cell);
        if (Random.Shared.NextDouble() > SeedChanceByTile.GetValueOrDefault(tile, 0.5f)) return false;
        int hash = HashCode.Combine(cell.X, cell.Y, (int)now);
        TiberiumCell seeded = new()
        {
            Amount = 0,
            CreatedAt = now,
            RotationYRadians = MathHelper.TwoPi * (Math.Abs(hash % 1000) / 1000.0f),
            SubType = Math.Abs(hash % 3),
            GrowthFactor = GrowthFactorByTile.GetValueOrDefault(tile, 1),
            MaxSize = 1
        };
        _cells[cell] = seeded;
        AddToRenderChunk(cell);
        return true;
    }

    private static void RebaseGrowth(TiberiumCell cell, double now)
    {
        cell.CreatedAt = now - cell.Amount / (GrowthPerSecond * Math.Max(0.0001f, cell.GrowthFactor));
    }

    public void Draw(Effect effect, BoundingFrustum? frustum = null)
    {
        if (_cells.Count == 0)
            return;

        Mesh mesh = Globals.MeshHandler.Meshes["tiberium-1"];
        GameGrid grid = Globals.World.GameGrid;
        Terrain terrain = Globals.World.Terrain;
        LastVisibleChunkCount = 0;
        LastDrawnCellCount = 0;
        effect.Parameters["EmissivePulseTime"]?.SetValue(_visualTimeSeconds);
        effect.Parameters["EmissivePulseSpeed"]?.SetValue(EmissivePulseSpeed);
        effect.Parameters["EmissivePulseStrength"]?.SetValue(EmissivePulseStrength);
        try
        {
            foreach (RenderChunk chunk in _renderChunks.Values)
            {
                if (chunk.BoundsDirty || chunk.TerrainRevision != terrain.HeightRevision)
                    UpdateChunkBounds(chunk, mesh, grid, terrain);
                if (frustum is not null && frustum.Contains(chunk.Bounds) == ContainmentType.Disjoint)
                    continue;

                LastVisibleChunkCount++;
                foreach (Point cell in chunk.Cells)
                {
                    if (!_cells.TryGetValue(cell, out TiberiumCell? tiberium) || tiberium.Amount <= 0.0f)
                        continue;
                    Vector3 position = grid.ToWorldPosition(cell, 0.0f);
                    position.Y = terrain.GetSurfaceHeight(position.X, position.Z);
                    effect.Parameters["EmissivePulsePhase"]?.SetValue(tiberium.RotationYRadians * 1.7f);
                    mesh.Draw(effect, Matrix.CreateRotationY(tiberium.RotationYRadians) * Matrix.CreateScale(tiberium.CurrentSize) * Matrix.CreateTranslation(position));
                    LastDrawnCellCount++;
                }
            }
        }
        finally
        {
            // The unit effect is shared by all world meshes; do not animate their emissive masks.
            effect.Parameters["EmissivePulseStrength"]?.SetValue(0.0f);
            effect.Parameters["EmissivePulsePhase"]?.SetValue(0.0f);
        }
    }

    private void AddToRenderChunk(Point cell)
    {
        Point key = ChunkKey(cell);
        if (!_renderChunks.TryGetValue(key, out RenderChunk? chunk))
            _renderChunks.Add(key, chunk = new RenderChunk());
        if (chunk.Cells.Add(cell)) chunk.BoundsDirty = true;
    }

    private void RemoveFromRenderChunk(Point cell)
    {
        Point key = ChunkKey(cell);
        if (!_renderChunks.TryGetValue(key, out RenderChunk? chunk)) return;
        chunk.Cells.Remove(cell);
        if (chunk.Cells.Count == 0) _renderChunks.Remove(key);
        else chunk.BoundsDirty = true;
    }

    private static Point ChunkKey(Point cell) => new(cell.X / RenderChunkSize, cell.Y / RenderChunkSize);

    private void UpdateChunkBounds(RenderChunk chunk, Mesh mesh, GameGrid grid, Terrain terrain)
    {
        float minimumX = float.PositiveInfinity, minimumZ = float.PositiveInfinity;
        float maximumX = float.NegativeInfinity, maximumZ = float.NegativeInfinity;
        float minimumY = float.PositiveInfinity, maximumY = float.NegativeInfinity;
        float maximumScale = 1.0f;
        foreach (Point cell in chunk.Cells)
        {
            Vector3 center = grid.ToWorldPosition(cell, 0);
            minimumX = Math.Min(minimumX, center.X);
            maximumX = Math.Max(maximumX, center.X);
            minimumZ = Math.Min(minimumZ, center.Z);
            maximumZ = Math.Max(maximumZ, center.Z);
            float height = terrain.GetSurfaceHeight(center.X, center.Z);
            minimumY = Math.Min(minimumY, height);
            maximumY = Math.Max(maximumY, height);
            if (_cells.TryGetValue(cell, out TiberiumCell? tiberium))
                maximumScale = Math.Max(maximumScale, tiberium.MaxSize);
        }

        (Vector3 meshMinimum, Vector3 meshMaximum) = mesh.GetBounds();
        float horizontalRadius = Math.Max(
            Math.Max(Math.Abs(meshMinimum.X), Math.Abs(meshMaximum.X)),
            Math.Max(Math.Abs(meshMinimum.Z), Math.Abs(meshMaximum.Z))) * maximumScale;
        chunk.Bounds = new BoundingBox(
            new Vector3(minimumX - horizontalRadius, minimumY + meshMinimum.Y * maximumScale, minimumZ - horizontalRadius),
            new Vector3(maximumX + horizontalRadius, maximumY + meshMaximum.Y * maximumScale, maximumZ + horizontalRadius));
        chunk.TerrainRevision = terrain.HeightRevision;
        chunk.BoundsDirty = false;
    }
}
