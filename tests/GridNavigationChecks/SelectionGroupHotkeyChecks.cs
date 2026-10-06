using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RTS;

internal static class SelectionGroupHotkeyChecks
{
    private sealed class DyingMember(Vector3 position, Guid id) : MobileUnit(position, id)
    { public override bool IsDying => true; }

    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new Exception("Group hotkeys: " + message); checks++; }
        SelectionGroupHotkeys keys = new();
        SelectionGroupKey? Press(Keys key, double time, bool enabled = true) => keys.Update(new KeyboardState(key), time, enabled);
        void Release(double time) => keys.Update(new KeyboardState(), time, true);
        Check(Press(Keys.D1, 0) == new SelectionGroupKey(1, false, false), "single press selects only");
        Check(Press(Keys.D1, 0.1) is null, "held key does not repeat");
        Release(0.15);
        Check(Press(Keys.D1, 0.25) == new SelectionGroupKey(1, false, true), "second press centers");
        Release(0.26);
        Check(Press(Keys.D1, 0.27)?.CenterCamera == false, "third press starts a new pair");
        Release(0.28);
        Check(Press(Keys.D1, 0.7)?.CenterCamera == false, "late press does not center");
        Release(0.71);
        Check(Press(Keys.D2, 0.72)?.CenterCamera == false, "different group starts its own pair");
        Release(0.73);
        Check(Press(Keys.D1, 0.74)?.CenterCamera == false, "different group interrupts original pair");
        Release(0.75);
        Check(keys.Update(new KeyboardState(Keys.LeftControl, Keys.D1), 0.8, true) == new SelectionGroupKey(1, true, false), "control saves only");
        Release(0.81);
        Check(Press(Keys.D1, 0.82)?.CenterCamera == false, "save breaks double tap");
        Release(0.83);
        keys.Update(new KeyboardState(Keys.RightControl), 0.84, true);
        Release(0.85);
        Check(Press(Keys.D1, 0.86)?.CenterCamera == false, "control alone cancels pending tap");
        Release(0.87);
        Check(Press(Keys.D1, 0.88, false) is null, "disabled input cannot select or save");
        Check(Press(Keys.D1, 0.89) is null, "held key after reenable cannot trigger");
        Release(0.9);
        Check(Press(Keys.D1, 0.91)?.CenterCamera == false, "disabled interval resets pending pair");
        Release(0.92);
        Check(keys.Update(new KeyboardState(Keys.D1, Keys.D2), 0.93, true) is null, "simultaneous groups do not cause ambiguous jump");
        Release(0.94);
        Check(Press(Keys.D1, 0.95)?.CenterCamera == false, "ambiguous press cancels pair");
        keys.Reset();
        Check(Press(Keys.D1, 0.96) is null, "reset preserves held key suppression");
        Release(0.97);
        Check(Press(Keys.D1, 0.98)?.CenterCamera == false, "reset removes double tap history");
        Release(1);
        keys.DoubleTapWindow = TimeSpan.FromMilliseconds(100);
        Check(Press(Keys.D1, 1.2)?.CenterCamera == false, "configured shorter window respected");
        Release(1.21);
        Check(Press(Keys.D1, 1.25)?.CenterCamera == true, "configured window permits close tap");
        Release(1.3);
        Check(Press(Keys.D0, 1.4)?.Number == 0, "group zero supported");
        Release(1.41);
        Check(Press(Keys.D9, 1.42)?.Number == 9, "group nine supported");

        GameWorld world = new(65, 65, 1, graphicsEnabled: false);
        MobileUnit first = new(new Vector3(5.5f, 0, 10.5f), Guid.NewGuid());
        MobileUnit second = new(new Vector3(15.5f, 0, 20.5f), Guid.NewGuid());
        MobileUnit removed = new(new Vector3(60, 0, 60), Guid.NewGuid());
        world.Units.Register(first); world.Units.Register(second);
        DyingMember dying = new(new Vector3(40, 0, 40), Guid.NewGuid());
        world.Units.Register(dying);
        Check(SelectionGroupHotkeys.GetLiveMembers(world, new Unit[] { first, dying }).Count == 1, "dying registered members excluded");
        Check(SelectionGroupHotkeys.TryGetCenter(world, new[] { first, second, removed }, out Vector3 center) &&
            center == new Vector3(10.5f, 0, 15.5f), "center ignores unregistered member");
        Check(SelectionGroupHotkeys.GetLiveMembers(world, new[] { first, first, removed }).Count == 1, "members deduplicated and filtered");
        Check(!SelectionGroupHotkeys.TryGetCenter(world, Array.Empty<Unit>(), out _), "empty group has no center");
        second.Embark(first.UnitId);
        Check(SelectionGroupHotkeys.TryGetCenter(world, new[] { first, second }, out center) && center == first.Position,
            "embarked member uses container without overweighting it");
        world.Units.Unregister(first);
        Check(!SelectionGroupHotkeys.TryGetCenter(world, new[] { second }, out _), "missing container does not provide stale center");
        world.Units.Unregister(second);
        Check(SelectionGroupHotkeys.GetLiveMembers(world, new[] { first, second }).Count == 0, "removed group becomes empty");
        Camera camera = new();
        float height = camera.HeightAboveTerrain, yaw = camera.YawAngle, pitch = camera.PitchAngle;
        camera.CenterOn(new Vector3(20, 80, 20), world.Terrain);
        Check(camera.Position.Y == world.Terrain.GetSurfaceHeight(20, 20) + height, "centering follows terrain instead of flight altitude");
        Check(camera.HeightAboveTerrain == height && camera.YawAngle == yaw && camera.PitchAngle == pitch, "centering preserves height and orientation");
        return checks;
    }
}
