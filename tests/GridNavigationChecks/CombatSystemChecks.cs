using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static class CombatSystemChecks
{
    private static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object target, Type type, string name, object? value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private sealed class Fighter(Vector3 position, Guid id) : Unit(position, id)
    {
        public bool Rocket;
        public bool Disabled;
        public bool Dead;
        public bool Air;
        public ArmorClass ArmorType = ArmorClass.Infantry;
        public int HitCalls;
        public override ArmorClass Armor => ArmorType;
        public override TargetDomain Domain => Air ? TargetDomain.Air : TargetDomain.Ground;
        public override bool CanFireWeapon => !Disabled;
        public override bool IsDying => Dead;
        public override bool HasDeathExplosion => false;
        public override bool UsesHitscanWeapon => !Rocket;
        public override ProjectileKind ProjectileKind => Rocket ? ProjectileKind.Rocket : ProjectileKind.None;
        public override bool IsReadyToShoot(double time) => !Dead && !Disabled;
        public override void PlayShotEffects() { }
        public override bool OnHit(HitInfo hit) { HitCalls++; return base.OnHit(hit); }
        public override bool BeginDeathSequence() { Dead = true; return true; }
    }

    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var savedWorld = Globals.World;
        var savedGame = Globals.Game;
        try
        {
            var terrain = Empty<Terrain>();
            Set(terrain, typeof(Terrain), "<Width>k__BackingField", 41);
            Set(terrain, typeof(Terrain), "<Height>k__BackingField", 41);
            Set(terrain, typeof(Terrain), "HeightMap", new float[41 * 41]);
            var world = Empty<GameWorld>();
            var units = new UnitHandler();
            var grid = new GameGrid(40, 40, 1); grid.BindTerrain(terrain);
            Set(world, typeof(GameWorld), "_terrain", terrain);
            Set(world, typeof(GameWorld), "<Units>k__BackingField", units);
            Set(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
            Set(world, typeof(GameWorld), "<Projectiles>k__BackingField", new ProjectileHandler());
            Globals.World = world;
            var list = (IList<Unit>)typeof(UnitHandler).GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(units)!;
            Fighter Add(Vector3 pos, Guid? id = null)
            {
                var unit = new Fighter(pos, id ?? Guid.NewGuid()) { Behavior = UnitBehavior.Passive };
                Set(unit, typeof(Unit), "<Height>k__BackingField", 2f);
                Set(unit, typeof(Unit), "<Width>k__BackingField", 1);
                Set(unit, typeof(Unit), "<Length>k__BackingField", 1);
                list.Add(unit); return unit;
            }
            using var network = new NetworkHandler();
            var input = new NetworkInput(network);
            var commands = new List<NetworkMessage>();
            int losses = 0;
            Task Publish(NetworkMessage command)
            {
                var wire = JsonSerializer.Deserialize<NetworkMessage>(JsonSerializer.Serialize(command, NetworkJson.Options), NetworkJson.Options)!;
                commands.Add(wire);
                if (wire.Type is NetworkMessageType.UnitHitCommand or NetworkMessageType.ProjectileSpawnCommand or NetworkMessageType.ProjectileImpactCommand)
                    network.ApplyLocalCommand(wire);
                return Task.CompletedTask;
            }
            var combat = new CombatSystem(world, network.LocalPeerId, Publish, (_, _) => losses++);
            NetworkMessage Shot(Fighter attacker, Vector3 position) => NetworkCommands.CreateAttackRequest(
                network.LocalPeerId, [attacker.UnitId], position.X, position.Y, position.Z);
            void Fire(Fighter attacker, Vector3 position) => combat.ResolveAttackAsync(Shot(attacker, position)).GetAwaiter().GetResult();
            var shooter = Add(new(5, 0, 5));
            var target = Add(new(8, 0, 5));
            shooter.AttackDamage = 20;
            Fire(shooter, target.Position + Vector3.Up);
            Check(target.HitPoints == 80 && target.HitCalls == 1, "Direct damage mutates host health exactly once despite local command application");
            Check(commands.Select(c => c.Type).SequenceEqual(new[] { NetworkMessageType.BulletImpactCommand, NetworkMessageType.UnitHitCommand }),
                "Hitscan impact visualization precedes authoritative hit confirmation");
            network.ApplyLocalCommand(commands.Last());
            Check(target.HitPoints == 80 && target.HitCalls == 1, "Repeated absolute hit confirmation does not subtract damage twice");
            var clientWorld = Empty<GameWorld>();
            var clientUnits = new UnitHandler();
            var clientTarget = new Fighter(target.Position, target.UnitId);
            var clientList = (IList<Unit>)typeof(UnitHandler).GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(clientUnits)!;
            clientList.Add(clientTarget);
            Set(clientWorld, typeof(GameWorld), "<Units>k__BackingField", clientUnits);
            using (var clientNetwork = new NetworkHandler())
            {
                var clientInput = new NetworkInput(clientNetwork);
                Globals.World = clientWorld;
                try
                {
                    clientNetwork.ApplyLocalCommand(commands.Last());
                    clientNetwork.ApplyLocalCommand(commands.Last());
                    Check(clientTarget.HitPoints == 80 && clientTarget.HitCalls == 0,
                        "Separate client receives absolute host HP over actual NetworkInput without running damage logic");
                }
                finally { Globals.World = world; }
            }
            target.ArmorType = ArmorClass.HeavyVehicle;
            Fire(shooter, target.Position + Vector3.Up);
            Check(target.HitPoints == 79, "Combat retains small arms versus heavy armor balancing");
            target.ArmorType = ArmorClass.Infantry;
            var aircraft = Add(new(8, 10, 5)); aircraft.Air = true;
            Check(combat.FindImpactTarget(shooter.UnitId, aircraft.Position + Vector3.Up) == aircraft,
                "Air impact chooses elevated aircraft instead of ground unit below it");
            Check(combat.FindImpactTarget(shooter.UnitId, target.Position + Vector3.Up) == target,
                "Ground impact excludes aircraft above it");
            shooter.SetAttackTarget(aircraft.UnitId);
            Check(!shooter.TryQueueShot(0, out _), "Ground-only weapon cannot schedule a shot at an air target");
            shooter.AllowedTargetDomains = TargetDomain.Ground | TargetDomain.Air;
            shooter.SetAttackTarget(aircraft.UnitId);
            var shots = new List<NetworkMessage>();
            combat.Update(1, .1f, shots.Add);
            Check(shots.Count == 1 && shots[0].Y == aircraft.Position.Y + aircraft.Height * .5f,
                "Air-capable weapon schedules target's actual altitude through an attack request");
            combat.Update(1.1, .1f, shots.Add);
            Check(shots.Count == 1, "Host shot scheduler preserves weapon cooldown");
            network.ApplyLocalCommand(NetworkCommands.CreateStopCommand(network.LocalPeerId,
                NetworkCommands.CreateStopRequest(network.LocalPeerId, [shooter.UnitId])));
            combat.Update(2, .1f, shots.Add);
            Check(shots.Count == 1 && !shooter.HasCombatTarget, "Replicated Stop prevents further explicit attacks");
            shooter.SetAttackGroundTarget(new(9, 0, 5)); combat.Update(3, .1f, shots.Add);
            Check(shots.Count == 2, "Terrain attack follows the same host request queue");
            shooter.Stop(); combat.Update(4, .1f, shots.Add);
            Check(shots.Count == 2, "Stop also ends repeated terrain attacks");
            int before = commands.Count;
            shooter.Disabled = true; Fire(shooter, target.Position);
            Check(commands.Count == before, "Disabled weapon produces neither visual shot nor damage");
            shooter.Disabled = false;
            target.Dead = true; Fire(shooter, target.Position + Vector3.Up);
            Check(target.HitCalls == 2, "Dead target cannot take new direct damage");
            target.Dead = false; target.HitPoints = 10; Fire(shooter, target.Position + Vector3.Up);
            Check(target.Dead && target.HitPoints == 0 && losses == 1, "Lethal damage destroys target and reports combat loss exactly once");
            Check(commands[^2].Type == NetworkMessageType.UnitHitCommand && commands[^1].Type == NetworkMessageType.DestroyUnitCommand,
                "Lethal hit confirmation is ordered before destruction");
            Fire(shooter, target.Position + Vector3.Up);
            Check(losses == 1 && target.HitCalls == 3, "Death ghost cannot generate duplicate loss or damage");

            // Actual rocket trajectory and collision, not a replacement planner or fake impact.
            list.Clear(); commands.Clear(); combat.Reset(); world.Projectiles.Clear();
            shooter = Add(new(5, 3, 5)); shooter.Rocket = true; shooter.AttackDamage = 40;
            shooter.AttackDamageType = DamageType.Explosive;
            target = Add(new(5, 3, 8));
            var neighbor = Add(new(6, 3, 8));
            var distant = Add(new(12, 3, 8));
            var dead = Add(new(5.5f, 3, 8)); dead.Dead = true;
            Fire(shooter, target.Position + Vector3.Up);
            Check(combat.ActiveProjectileCount == 1 && target.HitPoints == 100,
                "Rocket launch creates authoritative flight without applying immediate damage");
            Check(commands.Single().Type == NetworkMessageType.ProjectileSpawnCommand && world.Projectiles.ActiveProjectileCount == 1,
                "Rocket spawn uses actual wire command and visual ProjectileHandler");
            network.ApplyLocalCommand(commands.Single());
            Check(world.Projectiles.ActiveProjectileCount == 1, "Repeated rocket spawn is visually idempotent");
            shooter.Stop();
            for (int i = 0; i < 100 && combat.ActiveProjectileCount > 0; i++) combat.SimulateProjectiles(.05f);
            Check(combat.PendingImpactCount == 1 && combat.ActiveProjectileCount == 0,
                "Launched rocket continues after Stop and resolves collision once");
            combat.PublishImpactsAsync().GetAwaiter().GetResult();
            Check(target.HitPoints < 100 && neighbor.HitPoints < 100 && distant.HitPoints == 100 && dead.HitCalls == 0,
                "Explosion damages nearby live units with original radius and excludes distant or dead targets");
            Check(target.HitCalls == 1 && neighbor.HitCalls == 1,
                "Splash targets receive exactly one authoritative hit each");
            float explosionRadius = ProjectileFlightProfile.For(ProjectileKind.Rocket).ExplosionRadius;
            var impact = commands.Single(c => c.Type == NetworkMessageType.ProjectileImpactCommand);
            float distance = Vector2.Distance(new(target.Position.X, target.Position.Z), new(impact.X, impact.Z));
            float expectedDamage = DamageCalculator.Calculate(40 * MathHelper.Lerp(1, .25f,
                MathHelper.Clamp(distance / explosionRadius, 0, 1)), DamageType.Explosive, ArmorClass.Infantry);
            Check(Math.Abs(target.HitPoints - (100 - expectedDamage)) < .0001f,
                "Explosion retains exact radius falloff and armor multiplier from existing balancing");
            Check(commands[1].Type == NetworkMessageType.ProjectileImpactCommand && commands[2].Type == NetworkMessageType.UnitHitCommand,
                "Projectile impact visualization precedes splash health results");
            int published = commands.Count;
            combat.PublishImpactsAsync().GetAwaiter().GetResult();
            Check(commands.Count == published, "Flushing impacts twice never reapplies explosion damage");
            Fire(shooter, distant.Position + Vector3.Up); combat.Reset();
            Check(combat.ActiveProjectileCount == 0 && combat.PendingImpactCount == 0, "Reset discards active rockets and pending impacts");
            combat.SimulateProjectiles(10); combat.PublishImpactsAsync().GetAwaiter().GetResult();
            Check(distant.HitPoints == 100, "Discarded old-session projectile cannot damage the next match");
            Fire(shooter, target.Position + Vector3.Up);
            for (int i = 0; i < 100 && combat.ActiveProjectileCount > 0; i++) combat.SimulateProjectiles(.05f);
            Check(combat.PendingImpactCount == 1, "Collision can await publication");
            combat.Reset(); published = commands.Count; combat.PublishImpactsAsync().GetAwaiter().GetResult();
            Check(commands.Count == published, "Reset also invalidates completed but unpublished old impacts");

            list.Clear(); commands.Clear(); world.Projectiles.Clear();
            shooter = Add(new(5, 10, 5)); shooter.Rocket = true;
            aircraft = Add(new(5, 10, 8)); aircraft.Air = true;
            target = Add(new(5, 0, 8));
            Fire(shooter, aircraft.Position + Vector3.Up);
            for (int i = 0; i < 100 && combat.ActiveProjectileCount > 0; i++) combat.SimulateProjectiles(.05f);
            combat.PublishImpactsAsync().GetAwaiter().GetResult();
            Check(commands.Single(c => c.Type == NetworkMessageType.ProjectileImpactCommand).TargetId == aircraft.UnitId,
                "Actual rocket collision resolves the elevated aircraft instead of ground terrain beneath it");

            // Verify the real host's lifecycle gateway, not only CombatSystem.Reset in isolation.
            var host = new NetworkHost(network, input, world);
            var hostCombat = (CombatSystem)typeof(NetworkHost).GetField("_combat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            typeof(NetworkHost).GetMethod("EnsureSessionGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
            hostCombat.ResolveAttackAsync(Shot(shooter, aircraft.Position + Vector3.Up)).GetAwaiter().GetResult();
            Check(hostCombat.ActiveProjectileCount == 1, "Host delegates rocket flight state to its combat system");
            Set(host, typeof(NetworkHost), "_sessionGeneration", network.SessionGeneration - 1);
            typeof(NetworkHost).GetMethod("EnsureSessionGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
            Check(hostCombat.ActiveProjectileCount == 0 && hostCombat.PendingImpactCount == 0,
                "Real host session lifecycle clears combat projectiles and pending impacts");
            var game = Empty<RTSGame>();
            var armyId = Guid.NewGuid();
            var armies = new ArmyHandler(); armies.EnsureArmy(armyId, network.LocalPeerId);
            Set(game, typeof(RTSGame), "<World>k__BackingField", world);
            Set(game, typeof(RTSGame), "<Armies>k__BackingField", armies);
            Set(game, typeof(RTSGame), "_players", new List<Player> { new(network.LocalPeerId, "host", armyId: armyId) });
            Set(game, typeof(RTSGame), "_aiPlayers", new Dictionary<Guid, AIPlayer>());
            Globals.Game = game;
            var starts = new GameplayMarkerHandler(); starts.Add(GameplayMarkerType.PlayerStart, new(3, 0, 3), 0);
            Set(world, typeof(GameWorld), "<GameplayMarkers>k__BackingField", starts);
            hostCombat.ResolveAttackAsync(Shot(shooter, aircraft.Position + Vector3.Up)).GetAwaiter().GetResult();
            var startMethod = typeof(NetworkHost).GetMethod("TryCreateStartMultiplayerGameCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check(startMethod.Invoke(host, [NetworkCommands.CreateStartMultiplayerGameRequest(Guid.NewGuid())]) is null && hostCombat.ActiveProjectileCount == 1,
                "Rejected game-start preserves authoritative projectile state");
            Check(startMethod.Invoke(host, [NetworkCommands.CreateStartMultiplayerGameRequest(network.LocalPeerId)]) is NetworkMessage && hostCombat.ActiveProjectileCount == 0,
                "Accepted game-start resets authoritative projectile state before the new match");
        }
        finally { Globals.World = savedWorld; Globals.Game = savedGame; }
        return checks;
    }
}
