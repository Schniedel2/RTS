using Microsoft.Xna.Framework;
using RTS;

internal static class SoldierMovementChecks
{
    private sealed class Recruit(Vector3 position) : Soldier(position, Guid.NewGuid(), loadModel: false)
    {
        public UnitActionState AnimationState => _currentUnitState;
    }

    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new Exception(message); checks++; }
        var world = SimulationFixture.World();
        var soldier = new Recruit(new Vector3(5.5f, 0, 5.5f));
        world.Units.Register(soldier);
        Check(world.GameGrid.TryMove(soldier, new Point(5, 5)), "Recruit registers on the movement grid");
        soldier.TryReceiveGotoCommand(world, new GotoCommand(new Vector2(8.5f, 5.5f)),
            route: [new Point(6, 5), new Point(7, 5), new Point(8, 5)]);
        double time = 0;
        void Tick()
        {
            time += 0.05;
            soldier.Update(new GameTime(TimeSpan.FromSeconds(time), TimeSpan.FromSeconds(0.05)));
            world.PathfindingManager.Update();
        }
        Tick();
        Check(soldier.AnimationState == RTS.Unit.UnitActionState.Moving && soldier.Position.X > 5.5f,
            "Recruit runs while actually travelling");
        world.GameGrid.GetCell(6, 5).IsBlocked = true;
        Tick(); Tick(); Tick();
        Check(soldier.AnimationState == RTS.Unit.UnitActionState.Moving,
            "Brief movement pauses preserve the run loop");
        for (int frame = 0; frame < 15; frame++) Tick();
        Check(soldier.CurrentCommand is not null && soldier.AnimationState == RTS.Unit.UnitActionState.Idle,
            "A blocked recruit retains its order without running in place");
        for (int frame = 0; frame < 250 && soldier.CurrentCommand is not null; frame++) Tick();
        Check(soldier.CurrentCommand is null && world.GameGrid.ToCell(soldier.Position) == new Point(8, 5),
            "Recruit replans around an obstacle and completes its movement order");
        soldier.MarkHostPlanning(new Vector2(12.5f, 5.5f));
        for (int frame = 0; frame < 5; frame++) Tick();
        Check(soldier.AnimationState == RTS.Unit.UnitActionState.Idle,
            "A recruit awaiting a planned route does not run in place");
        soldier.Stop(); Tick();
        Check(soldier.AnimationState == RTS.Unit.UnitActionState.Idle,
            "Stop clears the run animation immediately");
        return checks;
    }
}


