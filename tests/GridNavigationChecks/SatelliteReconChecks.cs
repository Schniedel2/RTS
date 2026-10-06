using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private sealed class ReconEngineer : Soldier
    {
        public override string GameplayTypeId => "engineer";
        public ReconEngineer(Vector3 position, Guid id) : base(position, id, loadModel: false)
        { IsCrewMember = true; Width = Length = 1; }
    }

    public static int RunSatelliteReconChecks()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Satellite Recon: " + message);
            checks++;
        }
        using Scenario scenario = new(false);
        Army army = scenario.Army;
        GameWorld world = scenario.World;
        var project = GameplayCatalog.Find(PurchasableType.Research, ResearchProjects.SatelliteReconId)!;
        Check(project.BasePrice == 1500, "research catalog price");
        Check(!SatelliteRecon.Ready(world, army), "research required");
        army.Resources = 2000;
        PlayerCommandService research = new(scenario.Network, scenario.Network.LocalPeerId);
        research.ResearchAsync(scenario.Home.UnitId, ResearchProjects.SatelliteReconId).GetAwaiter().GetResult();
        scenario.Network.Update();
        scenario.PumpHost();
        Check(army.Resources == 500, "research spends catalog price through host");
        Check(scenario.Home.ProductionQueue.ActiveOrder?.DurationSeconds == 20, "research queued with catalog duration");
        for (int i = 1; i <= 25; i++)
        {
            scenario.Host.Update(new GameTime(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(1)));
            scenario.Network.Update();
        }
        Check(army.Perks.Has(PerkType.SatelliteRecon), "research completion grants perk through network");
        Check(!SatelliteRecon.Ready(world, army), "tower required");
        var tower = scenario.AddBuilding("communicationstower", new(30.5f, 0, 5.5f));
        scenario.AddBuilding("reaktor", new(34.5f, 0, 5.5f));
        Check(!SatelliteRecon.Ready(world, army), "operator required");
        var engineer = scenario.Add(new ReconEngineer(new(23.5f, 0, 20.5f), scenario.NextId()));
        Check(world.Units.EmbarkUnit(engineer.UnitId, scenario.Home.UnitId, OccupantRole.Crew), "engineer enters base");
        Check(SatelliteRecon.Ready(world, army), "first scan immediately ready");
        var request = new NetworkMessage(NetworkMessageType.SatelliteReconRequest, scenario.Network.LocalPeerId, ArmyId: army.Id);
        Check(scenario.Host.TryCreateSatelliteReconCommand(request with { SenderId = Guid.NewGuid() }) is null, "foreign actor rejected");
        NetworkMessage command = scenario.Host.TryCreateSatelliteReconCommand(request)!;
        Check(command is not null, "host accepts eligible scan");
        PlayerCommandService orders = new(scenario.Network, scenario.Network.LocalPeerId);
        orders.SatelliteReconAsync(army.Id).GetAwaiter().GetResult();
        scenario.Network.Update();
        scenario.PumpHost();
        Check(army.SatelliteRecon == new SatelliteReconState(180, 5), "host applies timers");
        Check(scenario.Host.TryCreateSatelliteReconCommand(request) is null, "duplicate activation rejected");
        Point corner = new(63, 63);
        Check(world.Visibility.GetGrid(army.Id)[corner] == VisibilityState.Visible, "scan reveals distant terrain");
        Check(world.Visibility.GetExploredSnapshots().Single(s => s.ArmyId == army.Id).Bits.Any(b => b != 0), "scan records exploration");
        Guid otherId = Guid.NewGuid();
        world.SimulationArmies.EnsureArmy(otherId, Guid.NewGuid());
        Check(world.Visibility.GetGrid(otherId)[corner] == VisibilityState.Unexplored, "unrelated army receives no scan");
        VisibilityGrid joinedGrid = new(world.GameGrid.Width, world.GameGrid.Height);
        joinedGrid.ApplySnapshot(world.Visibility.GetGrid(army.Id).GetSnapshot());
        Check(joinedGrid[corner] == VisibilityState.Visible, "join during scan restores visible cells");
        joinedGrid.BeginUpdate();
        Check(joinedGrid[corner] == VisibilityState.Explored, "joined visibility expires after scan");
        Check(SatelliteRecon.Advance(world, army, 500) == new SatelliteReconState(), "long tick clamps timers at zero");
        scenario.Home.Occupancy!.TryRemove(engineer.UnitId, out _);
        Check(SatelliteRecon.Advance(world, army, 2) == new SatelliteReconState(180, 3), "missing operator pauses recharge but scan completes");
        Check(scenario.Home.Occupancy.TryAdd(scenario.Add(new ReconEngineer(new(24.5f, 0, 20.5f), scenario.NextId())),
            OccupantRole.Crew, out _), "replacement operator accepted");
        SatelliteReconState next = SatelliteRecon.Advance(world, army, 5);
        Check(next == new SatelliteReconState(175, 0), "active scan and cooldown advance");
        army.SatelliteRecon = next;
        world.Visibility.Update();
        Check(world.Visibility.GetGrid(army.Id)[corner] == VisibilityState.Explored, "terrain remains explored after scan");
        scenario.Remove(tower);
        Check(SatelliteRecon.Advance(world, army, 10).Cooldown == 175, "missing tower pauses cooldown");
        scenario.AddBuilding("communicationstower", new(30.5f, 0, 5.5f));
        Check(SatelliteRecon.Advance(world, army, 10).Cooldown == 165, "replacement tower resumes without resetting");
        ArmySnapshot snapshot = world.SimulationArmies.GetSnapshot().Single(s => s.Id == army.Id);
        ArmyHandler restored = new();
        restored.ApplySnapshot(JsonSerializer.Deserialize<ArmySnapshot[]>(JsonSerializer.Serialize(new[] { snapshot }))!);
        Check(restored.Find(army.Id)!.SatelliteRecon == army.SatelliteRecon, "late join preserves cooldown");
        Check(!SatelliteRecon.Valid(new(float.NaN, 0)) && !SatelliteRecon.Valid(new(0, 6)), "invalid timers rejected");
        scenario.Network.ApplyLocalCommand(command! with { SatelliteRecon = new(0, 6) });
        Check(army.SatelliteRecon == next, "invalid network state does not overwrite timers");
        Check(!SatelliteRecon.Ready(world, army), "cooldown blocks further activation");
        scenario.Remove(scenario.Reactor);
        foreach (Building reactor in world.Units.GetArmyUnits(army.Id).OfType<Building>().Where(b => b.GameplayTypeId == "reaktor").ToArray())
            scenario.Remove(reactor);
        Check(SatelliteRecon.Advance(world, army, 10).Cooldown == 175, "power loss pauses recharge");
        Check(SatelliteRecon.MissingRequirement(world, army) == "Power required", "HUD reports power loss");
        army.SatelliteRecon = new(175, 1);
        world.Visibility.Update();
        scenario.Host.Update(new GameTime(TimeSpan.FromSeconds(26), TimeSpan.FromSeconds(1)));
        scenario.Network.Update();
        Check(army.SatelliteRecon == new SatelliteReconState(175, 0), "host ends scan while powerless cooldown remains paused");
        Check(world.Visibility.GetGrid(army.Id)[corner] == VisibilityState.Explored, "host scan-end command withdraws visibility");
        new SessionStateService(world, world.SimulationArmies).ApplyMatchStart(
            new(NetworkMessageType.StartMultiplayerGameCommand, scenario.Network.LocalPeerId), scenario.Network.LocalPeerId);
        Check(army.SatelliteRecon == new SatelliteReconState(), "game-start clears timers");
        Check(!army.Perks.Has(PerkType.SatelliteRecon), "game-start clears research");
        Check(world.Visibility.GetGrid(army.Id)[corner] == VisibilityState.Unexplored, "game-start clears explored map");
        return checks;
    }
}
