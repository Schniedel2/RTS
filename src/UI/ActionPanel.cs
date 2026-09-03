using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public sealed class ActionPanel
{
    private const int IconSize = 64;
    private const int ButtonSize = 64;
    private const int Padding = 12;
    private const int ButtonsPerRow = 8;

    private readonly Texture2D _iconSheet;
    private readonly Texture2D _pixel;
    private readonly List<(Rectangle Bounds, UnitAction Action)> _buttons = [];
    private MouseState _previousMouseState;
    private UnitActionType? _activeActionType;
    private UnitAction? _hoverAction;

    public ActionPanel(GraphicsDevice graphicsDevice, Texture2D iconSheet)
    {
        _iconSheet = iconSheet;
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
    }

    public event Action<UnitAction>? ActionSelected;

    public bool Update(IReadOnlyList<Unit> selectedUnits, Viewport viewport)
    {
        _buttons.Clear();
        if (selectedUnits.Count == 0)
        {
            _activeActionType = null;
            _previousMouseState = Mouse.GetState();
            return false;
        }

        IEnumerable<UnitAction> actions = selectedUnits
            .SelectMany(unit => unit.Actions)
            .GroupBy(action => action.Type)
            .Where(group => group.Count() == selectedUnits.Count)
            .Select(group => group.First());

        List<UnitAction> availableActions = actions.ToList();
        if (_activeActionType is null ||
            availableActions.All(action => action.Type != _activeActionType.Value))
            _activeActionType = availableActions.FirstOrDefault()?.Type;

        int index = 0;
        foreach (UnitAction action in availableActions)
        {
            int row = index / ButtonsPerRow;
            int column = index % ButtonsPerRow;
            _buttons.Add((new Rectangle(
                Padding + column * ButtonSize,
                Padding + row * ButtonSize,
                ButtonSize,
                ButtonSize), action));
            index++;
        }

        MouseState mouse = Mouse.GetState();

        int panelHeight = Padding * 2 + ((_buttons.Count + ButtonsPerRow - 1) / ButtonsPerRow) * ButtonSize;
        Rectangle panel = new(0, viewport.Height - panelHeight, Padding * 2 + ButtonsPerRow * ButtonSize, panelHeight);
        if ((mouse.Position.X < panel.Left) || (mouse.Position.X > panel.Right) ||
            (mouse.Position.Y < panel.Top) || (mouse.Position.Y > panel.Bottom))
        {
            _previousMouseState = mouse;
            return false;
        }

        Point panelPosition = new(0, viewport.Height - panelHeight);
        
        _hoverAction = null;
        foreach ((Rectangle bounds, UnitAction action) in _buttons)
        {
            Rectangle screenBounds = bounds;
            screenBounds.Offset(panelPosition);
            if (screenBounds.Contains(mouse.Position))
                _hoverAction = action;
        }

        if (_hoverAction is not null)
        {
            if (mouse.LeftButton == ButtonState.Pressed &&
                _previousMouseState.LeftButton == ButtonState.Released)
            {
                    _activeActionType = _hoverAction?.Type;
                    ActionSelected?.Invoke(_hoverAction!);
            }
        }

        _previousMouseState = mouse;
        return true;
    }

    public void Draw(SpriteBatch spriteBatch, Viewport viewport)
    {
        if (_buttons.Count == 0)
            return;

        int panelHeight = Padding * 2 + ((_buttons.Count + ButtonsPerRow - 1) / ButtonsPerRow) * ButtonSize;
        Rectangle panel = new(0, viewport.Height - panelHeight, Padding * 2 + ButtonsPerRow * ButtonSize, panelHeight);
        spriteBatch.Draw(_pixel, panel, Color.Black * 0.75f);

        foreach ((Rectangle localBounds, UnitAction action) in _buttons)
        {
            Rectangle bounds = new(
                localBounds.X,
                viewport.Height - panelHeight + localBounds.Y,
                localBounds.Width,
                localBounds.Height);
            Rectangle iconBounds = new(
                bounds.X + (ButtonSize - IconSize) / 2,
                bounds.Y + (ButtonSize - IconSize) / 2,
                IconSize,
                IconSize);
            Rectangle source = new(
                action.IconColumn * IconSize,
                action.IconRow * IconSize,
                IconSize,
                IconSize);

            // TODO: if action is disabled: spriteBatch.Draw(_pixel, bounds, Color.DimGray);
            spriteBatch.Draw(_iconSheet, iconBounds, source, Color.White);

            if (action.Type == _activeActionType)
            {
                //  render "selected"-Border
                Rectangle source2 = new(
                    action.IconColumn * 1,
                    action.IconRow * 0,
                    IconSize,
                    IconSize);
                spriteBatch.Draw(_iconSheet, iconBounds, source2, Color.White);
            }
            
        }

        if (_hoverAction is not null)
            RenderHelper.DrawTooltip(spriteBatch, _hoverAction!.Name, _previousMouseState.Position.X + 24, _previousMouseState.Position.Y + 16);
    }
}