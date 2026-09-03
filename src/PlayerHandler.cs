using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System;
using System.Linq;

namespace RTS;

public enum ToolShape
{
    Circle,
    Square
}

public class PlayerHandler
{    
    private readonly GameWorld _map;
    private readonly MarkerHandler _markerHandler;
    private readonly List<Unit> _selectedUnits = [];
    private MouseState _previousMouseState;
    private Point _selectionStart;
    private UnitAction? _activeAction;

    public IReadOnlyList<Unit> SelectedUnits => _selectedUnits;
    public UnitAction? ActiveAction => _activeAction;
    private Rectangle _currentSelectionRect;
    private bool _isSelectingUnits;
    public Vector3 MouseWorldPosition { get; private set; }
    private bool MouseIsOnTerrain = false;

    private int _toolSize;
    private ToolShape _toolShape;    
    private double _nextAllowedActionTime;

    public void SelectAction(UnitAction action)
    {
        if (_selectedUnits.Any(unit => unit.Actions.All(
                unitAction => unitAction.Type != action.Type)))
            return;

        //  single-use actions are handled immediately / current action-selection remains unchanged
        switch (action.Type)
        {
            case UnitActionType.IncToolSize:
                _toolSize++;
                return;
            case UnitActionType.DecToolSize:
                _toolSize--;
                if (_toolSize < 1)
                    _toolSize = 1;
                return;
            case UnitActionType.SelectToolCircle:
                _toolShape = ToolShape.Circle;
                return;
            case UnitActionType.SelectToolRectangle:
                _toolShape = ToolShape.Square;
                return;
        }

        _activeAction = action;        
    }

    public PlayerHandler(
        GameWorld map,
        MarkerHandler markerHandler)    
    {
        _map = map;
        _markerHandler = markerHandler;

        _toolSize = 8;
        _toolShape = ToolShape.Circle;
        _nextAllowedActionTime = 0;
    }

    public void Update(GameTime gameTime, Camera camera, Viewport viewport)
    {
        MouseState mouse = Mouse.GetState();
        //  update mouse-world position
        Point screenPosition = mouse.Position;
        Ray ray = CreatePickRay(camera, viewport, screenPosition);
        if (_map.Terrain.TryGetIntersection(ray, out Vector3 target))
        {
            MouseWorldPosition = new Vector3(target.X, target.Y, target.Z);
            MouseIsOnTerrain = true;
        }

        if (IsUnitSelectionEnabled())
        {
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
                if (MouseIsOnTerrain)
                    if (_activeAction != null)
                        RequestAction(_activeAction.Type, MouseWorldPosition);

                _isSelectingUnits = false;
            }

            if (IsRightButtonPressed(mouse))
                ClearSelection();
        }
        else
        {
            if (IsTerrainEditingEnabled())
            {
                if (gameTime.TotalGameTime.TotalMilliseconds > _nextAllowedActionTime)
                {
                    _nextAllowedActionTime = gameTime.TotalGameTime.TotalMilliseconds + 100; // 100 ms cooldown between actions
                    if (_activeAction != null)
                    {
                        if (MouseIsOnTerrain)
                            if (mouse.LeftButton == ButtonState.Pressed)
                                RequestAction(_activeAction.Type, MouseWorldPosition);
                        if (mouse.RightButton == ButtonState.Pressed)
                        {
                            //  alternate actions for terrain editing
                            if (_activeAction.Type == UnitActionType.RaiseTerrain)
                                RequestAction(UnitActionType.LowerTerrain, MouseWorldPosition);
                            if (_activeAction.Type == UnitActionType.LowerTerrain)
                                RequestAction(UnitActionType.RaiseTerrain, MouseWorldPosition);
                        }
                    }
                }
            }            
        }

        _previousMouseState = mouse;
    }

    void ClearSelection()
    {
        foreach (Unit unit in _selectedUnits)
            unit.Select(false);
        _selectedUnits.Clear();
        _activeAction = null;
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

        _activeAction = _selectedUnits
            .SelectMany(unit => unit.Actions)
            .GroupBy(action => action.Type)
            .Where(group => group.Count() == _selectedUnits.Count)
            .Select(group => group.First())
            .FirstOrDefault();
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

        private void RequestAction(UnitActionType actionType, Vector3 target)
        {
            if (_selectedUnits.Count == 0)
                return;

            if (actionType == UnitActionType.Goto)
            {
                Globals.Game.NetworkClient.RequestGotoAsync(_selectedUnits, target);
            }
            if ((actionType == UnitActionType.RaiseTerrain) || 
                (actionType == UnitActionType.FlattenTerrain) || 
                (actionType == UnitActionType.SmoothTerrain) || 
                (actionType == UnitActionType.LowerTerrain))
            {
                Globals.Game.NetworkClient.RequestToolActionAsync(actionType, _toolShape, _toolSize, target);
            }
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

    public bool IsTerrainEditingEnabled()
    {
        return MouseIsOnTerrain && (
            (_activeAction?.Type == UnitActionType.RaiseTerrain) || 
            (_activeAction?.Type == UnitActionType.LowerTerrain) || 
            (_activeAction?.Type == UnitActionType.SmoothTerrain) || 
            (_activeAction?.Type == UnitActionType.FlattenTerrain));
    }

    public void Draw3D(GraphicsDevice graphicsDevice, Camera camera)
    {
       //  render editor-tool
        if (MouseIsOnTerrain)
        {
            if (IsTerrainEditingEnabled())
            {
                //  render the terrain modification tool at the mouse world position
                //  1. let the current tool determine all affected terrain cells
                MouseWorldPosition = MouseWorldPosition;
                Point[] affectedCells = TerrainHelper.GetCells(new Vector2(MouseWorldPosition.X, MouseWorldPosition.Z), _toolShape, _toolSize);
                foreach (Point cell in affectedCells)
                {
                    _map.Terrain.HighlightCell(camera, cell.X, cell.Y);
                }
            }
        }
    }

    public void Draw2D(
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

            Globals.RenderHelper.DrawRectangle(
                spriteBatch,
                unitBounds,
                Color.Transparent,
                Color.White,
                borderThickness: 2);
        }

        if (_isSelectingUnits)
            Globals.RenderHelper.DrawRectangle(
                spriteBatch,
                _currentSelectionRect,
                Color.White * 0.1f,
                Color.LimeGreen,
                borderThickness: 2);
    }

    public bool IsUnitSelectionEnabled()
    {
        return !IsTerrainEditingEnabled();
    }
}