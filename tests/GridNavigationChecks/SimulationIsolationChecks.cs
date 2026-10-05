using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static class SimulationIsolationChecks
{
    private sealed class Fighter : Unit
    {
        public Fighter(Vector3 position, Guid id) : base(position, id)
        { Width = Length = 1; Height = 2; AttackDamage = 20; Behavior = UnitBehavior.Passive; }
        public override bool HasDeathExplosion => false;
        public override void PlayShotEffects() { }
    }
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; }
        GameWorld savedWorld = Globals.World;
        RTSGame savedGame = Globals.Game;
        var first = SimulationFixture.World();
        var second = SimulationFixture.World();
        using var firstNetwork = new NetworkHandler();
        using var secondNetwork = new NetworkHandler();
        using var firstInput = new NetworkInput(firstNetwork, first, first.SimulationArmies);
        using var secondInput = new NetworkInput(secondNetwork, second, second.SimulationArmies);
        Guid armyId = Guid.NewGuid(), medicId = Guid.NewGuid(), patientId = Guid.NewGuid();
        Army firstArmy = first.SimulationArmies.EnsureArmy(armyId, firstNetwork.LocalPeerId);
        Army secondArmy = second.SimulationArmies.EnsureArmy(armyId, secondNetwork.LocalPeerId);
        var firstMedic = SimulationFixture.Owned(new Medic(new(5, 0, 5), medicId, loadModel: false), armyId);
        var secondMedic = SimulationFixture.Owned(new Medic(new(5, 0, 5), medicId, loadModel: false), armyId);
        var firstPatient = SimulationFixture.Owned(new Soldier(new(6, 0, 5), patientId, loadModel: false), armyId, 50);
        var secondPatient = SimulationFixture.Owned(new Soldier(new(6, 0, 5), patientId, loadModel: false), armyId, 50);
        first.Units.Register(firstMedic); first.Units.Register(firstPatient);
        second.Units.Register(secondMedic); second.Units.Register(secondPatient);
        Check(!first.GraphicsEnabled && first.Terrain.GetHeight(5, 5) == 0 && first.GameGrid.Contains(new(5, 5)),
            "Regular simulation world initializes flat terrain and grid without GPU resources");
        Check(firstMedic.EquippedWeapon == Soldier.Weapon.Medikit && firstMedic.MaxHitPoints == 100 && firstMedic.Occupancy is null,
            "Model-free medic retains regular constructor gameplay initialization");
        var firstCommands = new List<NetworkMessage>();
        var secondCommands = new List<NetworkMessage>();
        Task PublishFirst(NetworkMessage message) { firstCommands.Add(message); firstNetwork.ApplyLocalCommand(message); return Task.CompletedTask; }
        Task PublishSecond(NetworkMessage message) { secondCommands.Add(message); secondNetwork.ApplyLocalCommand(message); return Task.CompletedTask; }
        MedicSystem Medics(GameWorld world, NetworkHandler network, Func<NetworkMessage, Task> publish) =>
            new(world, world.SimulationArmies, network.LocalPeerId, publish,
                request => NetworkCommands.CreateStopCommand(network.LocalPeerId, request),
                (_, _, _, _, _) => throw new Exception("Nearby patient must not require movement planning"), () => 0, _ => 0);
        var firstHealing = Medics(first, firstNetwork, PublishFirst);
        var secondHealing = Medics(second, secondNetwork, PublishSecond);
        firstHealing.UpdateAsync(0).GetAwaiter().GetResult();
        Check(firstPatient.HitPoints == 55 && secondPatient.HitPoints == 50 && secondCommands.Count == 0,
            "First healing system and actual NetworkInput affect only its own world despite identical IDs");
        secondHealing.UpdateAsync(0).GetAwaiter().GetResult();
        Check(secondPatient.HitPoints == 55 && firstPatient.HitPoints == 55, "Second world owns an independent healing cooldown");
        firstHealing.Reset(); firstHealing.UpdateAsync(.5).GetAwaiter().GetResult();
        secondHealing.UpdateAsync(.5).GetAwaiter().GetResult();
        Check(firstPatient.HitPoints == 60 && secondPatient.HitPoints == 55, "Resetting one healing system preserves the other cooldown");
        Guid harvesterId = Guid.NewGuid();
        var firstHarvester = SimulationFixture.Owned(new Harvester(new(10.5f, 0, 10.5f), harvesterId, loadModel: false), armyId);
        var secondHarvester = SimulationFixture.Owned(new Harvester(new(10.5f, 0, 10.5f), harvesterId, loadModel: false), armyId);
        first.Units.Register(firstHarvester); second.Units.Register(secondHarvester);
        first.Tiberium.ApplySeed(new(10, 10, 0, 100, 0, 0, 1, 1, 1));
        second.Tiberium.ApplySeed(new(10, 10, 0, 100, 0, 0, 1, 1, 1));
        HarvestSystem Harvest(GameWorld world, NetworkHandler network, Func<NetworkMessage, Task> publish) =>
            new(world, world.SimulationArmies, network.LocalPeerId, publish,
                (_, _, _, _, _) => throw new Exception("Nearby resource must not require movement planning"), () => 0, _ => 0);
        var firstHarvest = Harvest(first, firstNetwork, PublishFirst);
        var secondHarvest = Harvest(second, secondNetwork, PublishSecond);
        var firstStart = firstHarvest.TryStart(new(NetworkMessageType.HarvestRequest, firstNetwork.LocalPeerId,
            UnitId: harvesterId, X: 10.5f, Z: 10.5f));
        var secondStart = secondHarvest.TryStart(new(NetworkMessageType.HarvestRequest, secondNetwork.LocalPeerId,
            UnitId: harvesterId, X: 10.5f, Z: 10.5f));
        Check(firstStart is not null && secondStart is not null, "Both regular harvesters are independently admitted by their own Army service");
        PublishFirst(firstStart!).GetAwaiter().GetResult(); PublishSecond(secondStart!).GetAwaiter().GetResult();
        firstHarvest.UpdateAsync(new GameTime(), 0).GetAwaiter().GetResult();
        firstHarvest.UpdateAsync(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), 1).GetAwaiter().GetResult();
        Check(firstHarvester.CargoAmount > 0 && secondHarvester.CargoAmount == 0 &&
            first.Tiberium.Cells[new(10, 10)].Amount < second.Tiberium.Cells[new(10, 10)].Amount,
            "Harvest cargo and resource depletion remain isolated without switching Globals.World");
        firstHarvest.Reset();
        Check(firstHarvest.ActiveJobCount == 0 && secondHarvest.ActiveJobCount == 1, "Harvest reset does not remove jobs in another world");
        firstNetwork.ApplyLocalCommand(new(NetworkMessageType.ArmyResourcesCommand, firstNetwork.LocalPeerId, ArmyId: armyId, ResourceAmount: 17));
        Check(firstArmy.Resources == 17 && secondArmy.Resources != 17, "Resource replication resolves the explicitly injected Army service");
        Guid shooterId = Guid.NewGuid(), targetId = Guid.NewGuid();
        var firstShooter = new Fighter(new(20, 0, 20), shooterId);
        var secondShooter = new Fighter(new(20, 0, 20), shooterId);
        var firstTarget = new Fighter(new(23, 0, 20), targetId);
        var secondTarget = new Fighter(new(23, 0, 20), targetId);
        first.Units.Register(firstShooter); first.Units.Register(firstTarget);
        second.Units.Register(secondShooter); second.Units.Register(secondTarget);
        var firstCombat = new CombatSystem(first, firstNetwork.LocalPeerId, PublishFirst, (_, _) => { });
        var secondCombat = new CombatSystem(second, secondNetwork.LocalPeerId, PublishSecond, (_, _) => { });
        firstCombat.ResolveAttackAsync(NetworkCommands.CreateAttackRequest(firstNetwork.LocalPeerId, [shooterId], 23, 1, 20)).GetAwaiter().GetResult();
        Check(firstTarget.HitPoints == 80 && secondTarget.HitPoints == 100, "Combat damage and wire application remain in the selected world");
        firstShooter.SetAttackTarget(targetId);
        secondShooter.SetAttackTarget(targetId);
        Check(firstShooter.TryQueueShot(10, out Unit? resolvedFirst) && ReferenceEquals(resolvedFirst, firstTarget) &&
            secondShooter.TryQueueShot(10, out Unit? resolvedSecond) && ReferenceEquals(resolvedSecond, secondTarget),
            "Unit target resolution uses registered world rather than ambient world even for equal IDs");
        first.Units.Unregister(firstTarget);
        Check(!firstShooter.TryQueueShot(20, out _) && secondShooter.TryQueueShot(20, out _),
            "Removing a target does not clear the other world's target or membership");
        bool rejected = false;
        try { second.Units.Register(firstShooter); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && second.Units.FindById(shooterId) == secondShooter, "A Unit instance cannot leak into another world's membership");
        var unique = new Fighter(new(30, 0, 30), Guid.NewGuid());
        first.Units.Register(unique);
        rejected = false;
        int secondCount = second.Units.Count;
        try { second.Units.Register(unique); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && second.Units.Count == secondCount && second.Units.FindById(unique.UnitId) is null,
            "Cross-world registration with a unique ID is rejected before mutating collection or indices");
        Guid alliedArmyId = Guid.NewGuid(), teamId = Guid.NewGuid();
        first.SimulationArmies.EnsureArmy(armyId, firstNetwork.LocalPeerId, teamId);
        first.SimulationArmies.EnsureArmy(alliedArmyId, Guid.NewGuid(), teamId);
        second.SimulationArmies.EnsureArmy(alliedArmyId, Guid.NewGuid(), Guid.NewGuid());
        firstShooter.SetArmy(armyId); unique.SetArmy(alliedArmyId);
        secondShooter.SetArmy(armyId); secondTarget.SetArmy(alliedArmyId);
        Check(firstShooter.IsSameTeam(unique) && !firstShooter.IsEnemy(unique) && secondShooter.IsEnemy(secondTarget),
            "Alliance queries use each world's Army metadata instead of unrelated global players");
        var moving = new MobileUnit(new(32.5f, 0, 32.5f), Guid.NewGuid());
        first.Units.Register(moving);
        Check(moving.TryReceiveGotoCommand(first, new GotoCommand(new Vector2(34.5f, 32.5f)), route: [new Point(34, 32)]),
            "Regular model-free movement accepts a confirmed route with its own simulation context");
        firstInput.Dispose();
        float beforeDisposal = firstPatient.HitPoints;
        firstNetwork.ApplyLocalCommand(new(NetworkMessageType.UnitHitCommand, firstNetwork.LocalPeerId,
            UnitId: patientId, HitPoints: 1));
        Check(firstPatient.HitPoints == beforeDisposal, "Disposed replication adapter unsubscribes from its network and no longer retains or mutates its world");
        Check(ReferenceEquals(Globals.World, savedWorld) && ReferenceEquals(Globals.Game, savedGame),
            "All three extracted systems and their replication run without replacing global game or world");
        return checks;
    }
}
