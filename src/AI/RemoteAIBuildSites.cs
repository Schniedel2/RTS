using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace RTS;

/// <summary>Client prechecks share the same bounded scheduler as scouting. Host placement remains final.</summary>
internal sealed class RemoteAIBuildSites(GameWorld world)
{
    private sealed class Search(Building preview, Point center, int minimum, int maximum, long session, long assignment)
    {
        public Building Preview = preview;
        public Point Center = center;
        public int Minimum = minimum, Maximum = maximum;
        public long Session = session, Assignment = assignment;
        public bool Complete;
        public Vector3? Result;
    }
    private readonly Dictionary<(Guid Army, string Type), Search> _searches = [];
    public void Clear(Guid army)
    {
        foreach (var key in new List<(Guid Army, string Type)>(_searches.Keys))
            if (key.Army == army) _searches.Remove(key);
    }
    public bool TryFind(Building preview, Vector3 origin, int minimum, int maximum, out Vector3 position)
    {
        position = default;
        if (preview.ArmyId is not Guid army || world.SimulationNetwork is not { } network) return false;
        var key = (army, preview.GameplayTypeId);
        Point center = world.GameGrid.ToCell(origin);
        long generation = network.AIControllers.Find(army)?.Generation ?? -1;
        if (_searches.TryGetValue(key, out Search? current) && current.Center == center &&
            current.Minimum == minimum && current.Maximum == maximum && current.Session == network.SessionGeneration && current.Assignment == generation)
        {
            if (!current.Complete) return false;
            _searches.Remove(key);
            if (current.Result is Vector3 found && Allowed(preview, found)) { position = found; return true; }
        }
        var search = new Search(preview, center, minimum, maximum, network.SessionGeneration, generation);
        _searches[key] = search;
        world.ScoutingTargets.ClientPlanning.Enqueue(Work(search),
            () => _searches.GetValueOrDefault(key) == search && network.CanRunAI(army) &&
                network.SessionGeneration == search.Session && network.AIControllers.Find(army)?.Generation == search.Assignment,
            () => search.Complete = true,
            () => { if (_searches.GetValueOrDefault(key) == search) _searches.Remove(key); }, "AI.BuildSiteSlice");
        return false;
    }
    private bool Allowed(Building preview, Vector3 position) => preview.EvaluatePlacement(world, position, 0).IsAllowed &&
        ArmyGoalController.HasBuildingSpacing(world, preview, position, 0, ArmyGoalController.MinimumBuildingSpacingCells);
    private IEnumerable<int> Work(Search search)
    {
        foreach (Point cell in ArmyGoalController.CandidateCells(search.Center, search.Minimum, search.Maximum))
        {
            yield return 1;
            if (!world.GameGrid.Contains(cell) || search.Preview.ArmyId is Guid army &&
                world.AIOrderMonitors.TryGetValue(army, out var monitor) && monitor.AvoidSite(cell)) continue;
            Vector3 candidate = world.GameGrid.ToWorldPosition(cell, 0);
            candidate.Y = world.Terrain.GetSurfaceHeight(candidate.X, candidate.Z);
            if (Allowed(search.Preview, candidate)) { search.Result = candidate; yield break; }
        }
    }
}
