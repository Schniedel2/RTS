using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace RTS;

public class PlayerHandler
{
    private readonly GameWorld _map;
    private readonly MarkerHandler _markerHandler;
    private readonly RenderHelper _renderHelper;
    private readonly List<Unit> _selectedUnits = [];
    private MouseState _previousMouseState;
    private Point _selectionStart;

    public IReadOnlyList<Unit> SelectedUnits => _selectedUnits;
    private Rectangle _currentSelectionRect;
    private bool _isSelectingUnits;
    public Vector2 MouseWorldPosition { get; private set; }

    public PlayerHandler(
        GameWorld map,
        MarkerHandler markerHandler,
        RenderHelper renderHelper)
    {
        _map = map;
        _markerHandler = markerHandler;
        _renderHelper = renderHelper;
    }

    public void Update(Camera camera, Viewport viewport)
    {
        MouseState mouse = Mouse.GetState();

        //  update mouse-world position
        Point screenPosition = mouse.Position;
        Ray ray = CreatePickRay(camera, viewport, screenPosition);
        if (_map.Terrain.TryGetIntersection(ray, out Vector3 target))
            MouseWorldPosition = new Vector2(target.X, target.Z);

        if (IsLeftButtonPressed(mouse))
            _selectionStart = mouse.Position;

        if (mouse.LeftButton == ButtonState.Pressed)
        {
            _currentSelectionRect = CreateSelectionRectangle(_selectionStart, mouse.Position);
            if (_currentSelectionRect.Width > 8 || _currentSelectionRect.Height > 8)
                _isSelectingUnits = true;
        }

        if (IsLeftButtonReleased(mouse))
        {
            if (SelectedUnits.Count == 0)
                if (!_isSelectingUnits)
                    SelectUnits(camera, viewport, _currentSelectionRect, 1);
                
            if (_isSelectingUnits)
                SelectUnits(camera, viewport, _currentSelectionRect, 99);
            else
                IssueGotoCommand(camera, viewport, mouse.Position);

            _isSelectingUnits = false;
        }

        if (IsRightButtonPressed(mouse))
            ClearSelection();

        _previousMouseState = mouse;
    }

    void ClearSelection()
    {
        foreach (Unit unit in _selectedUnits)
            unit.Select(false);
        _selectedUnits.Clear();
    }

    private void SelectUnits(Camera camera, Viewport viewport, Rectangle selection, int maxUnits) 
    {
        ClearSelection();

        foreach (Unit unit in _map.Units.Units)
        {
            Rectangle unitBounds = unit.GetScreenBounds(
                camera.View,
                camera.Projection,
                viewport);

            if (!selection.Intersects(unitBounds))
                continue;

            if (_selectedUnits.Count < maxUnits)
            {
                _selectedUnits.Add(unit);
                unit.Select();
            }
        }
    }

    private bool IsLeftButtonPressed(MouseState mouse)
    {
        return mouse.LeftButton == ButtonState.Pressed &&
            _previousMouseState.LeftButton == ButtonState.Released;
    }

    private bool IsLeftButtonReleased(MouseState mouse)
    {
        return mouse.LeftButton == ButtonState.Released &&
            _previousMouseState.LeftButton == ButtonState.Pressed;
    }

        private bool IsRightButtonPressed(MouseState mouse)
        {
            return mouse.RightButton == ButtonState.Pressed &&
                _previousMouseState.RightButton == ButtonState.Released;
        }

        private bool IsRightButtonReleased(MouseState mouse)
        {
            return mouse.RightButton == ButtonState.Released &&
                _previousMouseState.RightButton == ButtonState.Pressed;
        }

        private void IssueGotoCommand(
            Camera camera,
            Viewport viewport,
            Point screenPosition)
        {
            if (_selectedUnits.Count == 0)
                return;

            Ray ray = CreatePickRay(camera, viewport, screenPosition);

            if (!_map.Terrain.TryGetIntersection(ray, out Vector3 target))
                return;

            GotoCommand command = new(new Vector2(target.X, target.Z));

            foreach (Unit unit in _selectedUnits)
                unit.TryReceiveGotoCommand(_map, command);

            _markerHandler.ShowGotoMarker(target);
        }

        private static Ray CreatePickRay(
            Camera camera,
            Viewport viewport,
            Point screenPosition)
        {
            Vector3 nearPoint = viewport.Unproject(
                new Vector3(screenPosition.X, screenPosition.Y, 0.0f),
                camera.Projection,
                camera.View,
                Matrix.Identity);
            Vector3 farPoint = viewport.Unproject(
                new Vector3(screenPosition.X, screenPosition.Y, 1.0f),
                camera.Projection,
                camera.View,
                Matrix.Identity);

            return new Ray(
                nearPoint,
                Vector3.Normalize(farPoint - nearPoint));
        }

        private static bool IsClick(Point start, Point end)
        {
            const int dragThreshold = 8;

            return Math.Abs(end.X - start.X) <= dragThreshold &&
                Math.Abs(end.Y - start.Y) <= dragThreshold;
        }

    private static Rectangle CreateSelectionRectangle(Point start, Point end)
    {
        int left = Math.Min(start.X, end.X);
        int top = Math.Min(start.Y, end.Y);
        int right = Math.Max(start.X, end.X);
        int bottom = Math.Max(start.Y, end.Y);

        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    public void Draw(
        SpriteBatch spriteBatch,
        Camera camera,
        Viewport viewport)
    {
        foreach (Unit unit in _selectedUnits)
        {
            Rectangle unitBounds = unit.GetScreenBounds(
                camera.View,
                camera.Projection,
                viewport);

            _renderHelper.DrawRectangle(
                spriteBatch,
                unitBounds,
                Color.Transparent,
                Color.White,
                borderThickness: 2);
        }

        if (_isSelectingUnits)
            _renderHelper.DrawRectangle(
                spriteBatch,
                _currentSelectionRect,
                Color.White * 0.1f,
                Color.LimeGreen,
                borderThickness: 2);
    }
}