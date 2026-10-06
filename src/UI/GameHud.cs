using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RTS;

public sealed class GameHud
{
    private readonly Minimap _minimap;
    private readonly ActionPanel _actionPanel;
    private readonly GameStatusPanel _statusPanel = new();
    private readonly ProductionQueuePanel _productionPanel = new();
    private readonly SatelliteReconPanel _satellitePanel = new();
    private readonly GameWorld _world;
    private float _minimapRefreshElapsed;
    private bool _showMinimap;
    private KeyboardState _previousKeyboardState;
    public SelectionGroupHotkeys GroupHotkeys { get; } = new();

    public HudLayout Layout { get; private set; }
    public Dictionary<int, SelectionGroup> SelectionGroups { get; } = new();

    public GameHud(GraphicsDevice graphicsDevice, GameWorld world, Texture2D actionIcons)
    {
        _world = world;
        _minimap = new Minimap(graphicsDevice, world, world.Visibility);
        _actionPanel = new ActionPanel(actionIcons);
    }

    public bool Update(GameTime gameTime, Camera camera, Viewport viewport,
        IReadOnlyList<Unit> selectedUnits, Army? localArmy, bool editorMode, bool inputEnabled)
    {
        Layout = HudLayout.Calculate(viewport,
            editorMode ? HudLayoutMode.Editor : HudLayoutMode.Game);
        _showMinimap = Layout.ShowMinimap &&
            (Globals.IsSpectator || editorMode || localArmy?.Perks.Has(PerkType.Minimap) == true);

        bool satelliteConsumed = _satellitePanel.Update(Layout.SatellitePanel, localArmy, _world, inputEnabled);
        bool minimapConsumed = _showMinimap &&
            _minimap.Update(camera, Layout.Minimap, inputEnabled);
        bool actionPanelConsumed = Layout.ShowActionPanel &&
            _actionPanel.Update(selectedUnits, Layout.ActionPanel, inputEnabled);

        KeyboardState keyboard = Keyboard.GetState();
        bool homeConsumed = false;
        if (inputEnabled && !editorMode && localArmy is not null &&
            keyboard.IsKeyDown(Keys.H) && !_previousKeyboardState.IsKeyDown(Keys.H) &&
            localArmy.Perks.TryGetNearestSourcePosition(PerkType.Home, camera.Position,
                out Vector3 homePosition))
        {
            camera.CenterOn(homePosition, _world.Terrain);
            homeConsumed = true;
        }

        bool groupConsumed = false;
        SelectionGroupKey? groupKey = GroupHotkeys.Update(keyboard,
            (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency,
            inputEnabled && Layout.Mode is HudLayoutMode.Game or HudLayoutMode.Editor);
        if (groupKey is SelectionGroupKey command)
        {
            groupConsumed = true;
            if (command.Save)
                RecreateSelectionGroup(command.Number, selectedUnits);
            else if (SelectionGroups.TryGetValue(command.Number, out SelectionGroup? group))
            {
                List<Unit> live = SelectionGroupHotkeys.GetLiveMembers(_world, group.Units);
                foreach (Unit removed in group.Units.Except(live))
                    removed.NotifyRemovedFromSelectionGroup(command.Number);
                group.Units.Clear();
                group.Units.AddRange(live);
                List<Unit> visible = live.Where(unit => _world.Visibility.IsUnitVisibleToLocalPlayer(unit)).ToList();
                Globals.LocalPlayer.SetUnitSelection(visible);
                if (command.CenterCamera && SelectionGroupHotkeys.TryGetCenter(_world, visible, out Vector3 center))
                    camera.CenterOn(center, _world.Terrain);
            }
        }

        _previousKeyboardState = keyboard;

        _minimapRefreshElapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_showMinimap && (Globals.IsSpectator || localArmy is not null) && _minimapRefreshElapsed >= 0.2f)
        {
            _minimapRefreshElapsed %= 0.2f;
            _minimap.Refresh(localArmy?.Id ?? Guid.Empty);
        }
        return satelliteConsumed || minimapConsumed || actionPanelConsumed || homeConsumed || groupConsumed;
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Army? localArmy, GameWorld world)
    {
        if (_showMinimap)
            _minimap.Draw(spriteBatch, camera, Layout.Minimap);
        if (Layout.ShowStatusPanel && localArmy is not null)
            _statusPanel.Draw(spriteBatch, Layout.StatusPanel, localArmy, world);
        if (Layout.ShowProductionPanel && localArmy is not null)
            _productionPanel.Draw(spriteBatch, Layout.ProductionPanel, localArmy, world);
        if (Layout.ShowActionPanel)
            _actionPanel.Draw(spriteBatch);
        if (Layout.Mode == HudLayoutMode.Game && localArmy is not null)
            _satellitePanel.Draw(spriteBatch, Layout.SatellitePanel, localArmy, world);
    }

    public void Reset()
    {
        _minimap.Reset();
        GroupHotkeys.Reset();
        foreach (int number in SelectionGroups.Keys.ToArray()) DeleteSelectionGroup(number);
        _minimapRefreshElapsed = 0.0f;
        _showMinimap = false;
    }

    private void DeleteSelectionGroup(int groupNum)
    {
        if (!SelectionGroups.TryGetValue(groupNum, out SelectionGroup? group))
            return;
        SelectionGroups.Remove(groupNum);
        foreach (var unit in group.Units)
            unit.NotifyRemovedFromSelectionGroup(groupNum);
    }
    public void RecreateSelectionGroup(int groupNum, IReadOnlyList<Unit> selectedUnits)
    {
        DeleteSelectionGroup(groupNum);
        SelectionGroups[groupNum] = new SelectionGroup(selectedUnits);
        foreach (var unit in selectedUnits)
            unit.NotifyAddedToSecetionGroup(groupNum);
    }
}
