using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

internal static class MedicSystemChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        {
            var world = SimulationFixture.World();
            var units = world.Units;
            var list = new SimulationFixture.Membership(world);
            var armies = new ArmyHandler();
            Guid owner = Guid.NewGuid(), army = Guid.NewGuid(), enemy = Guid.NewGuid();
            armies.EnsureArmy(army, owner);
            var medic = SimulationFixture.Owned(new Medic(Vector3.Zero, Guid.NewGuid(), loadModel: false), army, 100);
            var patient = SimulationFixture.Owned(new SimulationFixture.Patient(new Vector3(2, 0, 0), Guid.NewGuid()), army, 50);
            var other = SimulationFixture.Owned(new SimulationFixture.Patient(Vector3.Zero, Guid.NewGuid()), enemy, 1);
            list.AddRange(new Unit[] { medic, patient, other });
            var commands = new List<NetworkMessage>();
            using var healingNetwork = new NetworkHandler();
            using var healingInput = new NetworkInput(healingNetwork, world, armies);
            Task Publish(NetworkMessage message)
            {
                var wire = JsonSerializer.Deserialize<NetworkMessage>(JsonSerializer.Serialize(message, NetworkJson.Options), NetworkJson.Options)!;
                commands.Add(wire);
                if (wire.Type == NetworkMessageType.UnitHitCommand)
                    healingNetwork.ApplyLocalCommand(wire);
                if (wire.Type == NetworkMessageType.StopCommand) medic.ClearCommand();
                return Task.CompletedTask;
            }
            Func<bool>? valid = null;
            Action<NetworkMessage?>? finish = null;
            Action? abort = null;
            int plans = 0;
            void Plan(Medic unit, Vector3 target, Func<bool> isValid, Action<NetworkMessage?> complete, Action cancel)
            { plans++; valid = isValid; finish = complete; abort = cancel; }
            long session = 0, version = 0;
            var system = new MedicSystem(world, armies, owner, Publish,
                request => NetworkCommands.CreateStopCommand(owner, request), Plan, () => session, _ => version);
            void Tick(double time) => system.UpdateAsync(time).GetAwaiter().GetResult();
            NetworkMessage Stop(Guid sender) => NetworkCommands.CreateStopRequest(sender, new[] { medic.UnitId });
            Tick(0);
            Check(patient.HitPoints == 55 && other.HitPoints == 1 && commands.Count == 1,
                "Medic heals a nearby own soldier and never an enemy");
            Check(commands[0].Type == NetworkMessageType.UnitHitCommand && commands[0].Damage == -5 &&
                commands[0].HitPoints == 55 && commands[0].UnitId == patient.UnitId,
                "Healing replicates a negative damage pulse and absolute resulting health over the existing wire DTO");
            healingNetwork.ApplyLocalCommand(commands[0]);
            Check(patient.HitPoints == 55, "NetworkInput reapplies absolute healing health without a second healing pulse");
            Tick(.5);
            Check(patient.HitPoints == 55, "Medic pulse cooldown prevents healing every frame");
            Tick(1);
            Check(patient.HitPoints == 60, "Medic heals again when its pulse interval expires");
            patient.HitPoints = 99; Tick(2);
            Check(patient.HitPoints == 100, "Healing clamps at maximum health");
            Tick(3);
            Check(patient.HitPoints == 100, "Healthy patients receive no additional healing");

            system.Reset(); patient.HitPoints = 50; patient.SetPosition(new Vector3(8, 0, 0)); Tick(4);
            Check(plans == 1 && valid!() && system.ActiveJobCount == 1, "Independent medic plans a pursuit inside its search radius");
            Tick(4.2);
            Check(plans == 1, "A pending medic route is not duplicated on following updates");
            system.HandleCommandOverride(Stop(Guid.NewGuid()), new[] { medic.UnitId });
            Check(valid!() && system.ActiveJobCount == 1, "Foreign Stop cannot interrupt an own medic");
            system.HandleCommandOverride(Stop(owner), new[] { medic.UnitId });
            Check(!valid!() && system.ActiveJobCount == 0, "Own Stop invalidates pending medic pursuit");
            int published = commands.Count;
            finish!(new(NetworkMessageType.GotoCommand, owner, UnitIds: new[] { medic.UnitId }));
            Check(commands.Count == published, "An obsolete medic planning result cannot publish after Stop");
            Tick(5);
            Check(plans == 1 && patient.HitPoints == 50, "Stopped medic does not pursue a distant patient");
            patient.SetPosition(new Vector3(2, 0, 0)); Tick(6);
            Check(patient.HitPoints == 55, "Stopped medic can still heal within its local radius");

            system.Reset(); patient.SetPosition(new Vector3(8, 0, 0)); Tick(7);
            Check(valid!(), "Reset releases the hold state for future independent pursuit");
            patient.Dead = true;
            Check(!valid!(), "Dying patient invalidates pending medic planning");
            abort!(); Tick(8);
            Check(system.ActiveJobCount == 0, "Dying patient is removed from automatic pursuit");
            patient.Dead = false;
            patient.HitPoints = 0; Tick(9);
            Check(system.ActiveJobCount == 0, "Zero-health patients cannot be selected");
            patient.HitPoints = 50; Tick(10);
            patient.Embark(Guid.NewGuid());
            Check(!valid!(), "Embarking the patient invalidates pending pursuit");
            patient.Disembark(patient.Position);

            system.Reset(); Tick(11);
            var leader = SimulationFixture.Owned(new SquadLeader(Vector3.Zero, Guid.NewGuid(), loadModel: false), army, 100); list.Add(leader);
            medic.SquadLeaderId = leader.UnitId;
            Check(!valid!(), "Joining a squad invalidates an independent pending pursuit");
            Tick(12);
            Check(system.ActiveJobCount == 0 && patient.HitPoints == 50, "Squad medic drops independent jobs and does not chase outside healing radius");
            patient.SetPosition(new Vector3(1, 0, 0)); Tick(13);
            Check(patient.HitPoints == 50, "Squad medic does not heal unrelated nearby soldiers");
            patient.SquadLeaderId = leader.UnitId; Tick(14);
            Check(patient.HitPoints == 55, "Squad medic heals nearby members of its own squad");

            var secondMedic = SimulationFixture.Owned(new Medic(Vector3.Zero, Guid.NewGuid(), loadModel: false), army, 100);
            secondMedic.SquadLeaderId = leader.UnitId; list.Add(secondMedic);
            Tick(14.5);
            Check(patient.HitPoints == 55, "Multiple medics share a per-patient pulse limit");
            Tick(15);
            Check(patient.HitPoints == 60, "Two squad medics cannot double-heal the same patient in one pulse");
            list.Remove(secondMedic);
            medic.SquadLeaderId = null;
            patient.SetPosition(new Vector3(8, 0, 0)); Tick(16);
            Check(valid!(), "Leaving a squad restores independent pursuit");
            session++;
            Check(!valid!(), "Session generation invalidates pending medic pursuit");
            system.Reset(); Tick(17);
            Check(system.ActiveJobCount == 1 && valid!(), "Session reset permits a fresh valid pursuit");
            version++;
            Check(!valid!(), "Replacing command version invalidates pending medic pursuit");
            system.Reset(); Tick(18); list.Remove(patient);
            Check(!valid!(), "Removing patient invalidates its pending pursuit");
            abort!(); Tick(19);
            Check(system.ActiveJobCount == 0, "Removed patient leaves no medic job");
            list.Add(patient); system.Reset(); Tick(20); list.Remove(medic); Tick(21);
            Check(system.ActiveJobCount == 0 && !valid!(), "Removed medic leaves no pursuit state or live callback");
            list.Add(medic); system.Reset(); Tick(22);
            published = commands.Count; system.Reset();
            finish!(new(NetworkMessageType.GotoCommand, owner, UnitIds: new[] { medic.UnitId }));
            Check(system.ActiveJobCount == 0 && commands.Count == published, "Match reset rejects late medic planning completions");
            Tick(23);
            finish!(new(NetworkMessageType.GotoCommand, owner, UnitIds: new[] { medic.UnitId },
                Routes: new[] { new UnitRoute(medic.UnitId, new[] { new Point(8, 0) }) }));
            medic.TryReceiveGotoCommand(world, new GotoCommand(new Vector2(8, 0)), route: [new Point(8, 0)]);
            medic.SquadLeaderId = leader.UnitId;
            published = commands.Count; Tick(24);
            Check(system.ActiveJobCount == 0 && medic.CurrentCommand is null &&
                commands.Skip(published).Any(message => message.Type == NetworkMessageType.StopCommand),
                "Joining a squad stops an already issued independent patient pursuit through the host command gateway");
        }
        return checks;
    }
}
