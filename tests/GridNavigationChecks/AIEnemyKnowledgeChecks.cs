using System;
using Microsoft.Xna.Framework;
using RTS;

internal static partial class AIReconstructionChecks
{
    private sealed class ObservedAirUnit(Vector3 position, Guid id) : Soldier(position, id, loadModel: false)
    {
        public override string GameplayTypeId => "heli";
        public override TargetDomain Domain => TargetDomain.Air;
        public override ArmorClass Armor => ArmorClass.LightVehicle;
    }
    private static int RunEnemyKnowledgeChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using var scenario = new Scenario(false);
        Guid enemyArmy = Guid.NewGuid();
        scenario.World.SimulationArmies.EnsureArmy(enemyArmy, Guid.NewGuid(), Guid.NewGuid());
        var air = scenario.Add(new ObservedAirUnit(new(40.5f, 0, 40.5f), scenario.NextId()), enemyArmy);
        var memory = new AIThreatAssessment(scenario.Army.Id);
        var vision = scenario.World.Visibility.GetGrid(scenario.Army.Id);
        void Tick(double now) => memory.Update(new GameTime(TimeSpan.FromSeconds(now), TimeSpan.FromSeconds(1)), scenario.World);
        Tick(1);
        Check(memory.EnemyKnowledge.Count == 0, "Unseen enemies are not entered into AI memory");
        bool spectator = Globals.IsSpectator;
        try { Globals.IsSpectator = true; Tick(1.5); }
        finally { Globals.IsSpectator = spectator; }
        Check(memory.EnemyKnowledge.Count == 0, "Spectator display does not reveal enemies to AI knowledge");
        scenario.AddBuilding("gdi-base", new(37.5f, 0, 37.5f));
        vision.Reveal(new(40, 40), 0); Tick(2);
        Check(memory.EnemyKnowledge.Count == 1 && memory.EstimatedEnemyCount == 1, "Visible air enemy creates one observation and an estimated count");
        AIEnemyObservation contact = memory.EnemyKnowledge[air.UnitId];
        Check(contact.Context == AICombatContext.BaseDefense, "Observation near own buildings retains defense context");
        Check(contact.Position == air.Position && contact.LastSeen == 2 && contact.Roles != AIUnitRole.None, "Observation stores position, time and catalog roles");
        Check(memory.RememberedThreats.AntiAirNeed > AIThreatSnapshot.Baseline.AntiAirNeed, "Observed aircraft affects counter-air production needs");
        vision.BeginUpdate(); air.SetPosition(new(45.5f, 0, 45.5f)); Tick(3);
        Check(memory.EnemyKnowledge[air.UnitId].Position == contact.Position && memory.EnemyKnowledge[air.UnitId].LastSeen == 2,
            "Hidden movement does not update last-known information");
        Check(memory.RememberedThreats.AntiAirNeed > 0.58f, "A briefly hidden aircraft remains in the air threat estimate");
        Tick(32);
        Check(Math.Abs(memory.EstimatedEnemyCount - 0.5f) < 0.001f, "Unconfirmed quantities lose half their confidence after thirty seconds");
        vision.Reveal(new(45, 45), 0); Tick(33);
        Check(memory.EnemyKnowledge.Count == 1 && memory.EnemyKnowledge[air.UnitId].Position == air.Position && memory.EstimatedEnemyCount == 1,
            "Reobserving a contact refreshes position and confidence without duplicate counting");
        vision.BeginUpdate(); scenario.Remove(air); Tick(34);
        Check(memory.EnemyKnowledge.ContainsKey(air.UnitId), "Hidden disappearance or death is not omnisciently removed");
        Tick(123);
        Check(memory.EnemyKnowledge.Count == 0 && memory.RememberedThreats == AIThreatSnapshot.Baseline,
            "Expired enemy memory returns observed estimates to baseline");
        var infantry = scenario.AddVisibleEnemy(new(43.5f, 0, 43.5f)); Tick(124);
        Check(memory.EnemyKnowledge.ContainsKey(infantry.UnitId), "New observations still work after previous contacts expire");
        infantry.HitPoints = 0; Tick(125);
        Check(!memory.EnemyKnowledge.ContainsKey(infantry.UnitId), "A visibly destroyed opponent is forgotten immediately");
        Tick(0);
        Check(memory.EnemyKnowledge.Count == 0 && memory.Current == AIThreatSnapshot.Baseline, "Simulation time reset clears old match knowledge");
        return checks;
    }
}

