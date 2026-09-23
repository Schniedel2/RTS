using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RTS;

public enum ToolShape
{
    Circle,
    Square,
    Dither
}

public class PlayerHandler
{    
    enum CurrentMode
    {
        EditTerrain,
        EditGameplayMarkers,
        SelectUnits,
        BuildPreview
    }

    private readonly GameWorld _map;
    private readonly MarkerHandler _markerHandler;
    private readonly RenderStateStack _renderStates;
    private readonly List<Unit> _selectedUnits = [];
    private MouseState _previousMouseState;
    public IReadOnlyList<Unit> SelectedUnits => _selectedUnits;
    public UnitAction? ActiveAction { get; private set; }
    private Rectangle _currentSelectionRect;
    private bool _isSelectingUnits;
    public Vector3 MouseWorldPosition { get; private set; }
    public Point MouseScreenPosition { get; private set; }
    public Vector3 PressLeftWorldPosition { get; private set; }
    public Point PressLeftScreenPosition { get; private set; }
    private bool IsMouseOnTerrain = false;
    private bool _isDrag = false;

    private int _toolSize;
    private ToolShape _toolShape;    
    private double _nextAllowedActionTime;
    public TerrainTile _currentTerrainTile = TerrainTile.Grass;
    private float _buildPreviewDegree;
    private BuildingPlacement? _buildPlacementPreview;
    private EarthworkPreview? _earthworkPreview;
    private readonly ScoutingController _scouting;

    public bool SelectAction(UnitAction action, bool alternateAction)
    {
        //  single-use actions are handled immediately / current action-selection remains unchanged
       switch (action.Type)
        {
            case UnitActionType.None:
                return false;

            case UnitActionType.IncToolSize:
                _toolSize++;
                return false;
            case UnitActionType.DecToolSize:
                _toolSize--;
                if (_toolSize < 1)
                    _toolSize = 1;
                return false;
            case UnitActionType.SelectToolCircle:
                _toolShape = ToolShape.Circle;
                return false;
            case UnitActionType.SelectToolRectangle:
                _toolShape = ToolShape.Square;
                return false;
            case UnitActionType.SelectToolDither:
                _toolShape = ToolShape.Dither;
                return false;
            case UnitActionType.TilePreview:
                if (alternateAction)
                    _currentTerrainTile--;
                else
                    _currentTerrainTile++;
                if (_currentTerrainTile > TerrainTile.Max)
                    _currentTerrainTile = 0;
                if (_currentTerrainTile < 0)
                    _currentTerrainTile = TerrainTile.Max;
                return false;
            case UnitActionType.AdjustToolSize:
                if (alternateAction)
                    _toolSize--;
                else
                    _toolSize++;
                if (_toolSize < 1)
                    _toolSize = 1;
                return false;
            case UnitActionType.TakeOff:
            case UnitActionType.ReturnToHelipad:
                foreach (Helicopter helicopter in _selectedUnits.OfType<Helicopter>())
                    _ = Globals.Game.NetworkClient.RequestHelicopterOrderAsync(helicopter.UnitId,
                        action.Type == UnitActionType.TakeOff ? HelicopterOrder.TakeOff : HelicopterOrder.ReturnToHelipad, helicopter.Position);
                return false;
            case UnitActionType.Stop:
                {
                    _scouting.Stop(_selectedUnits);
                    if (action.Type == UnitActionType.Stop)
                        _ = Globals.Game.NetworkClient.RequestStopAsync(_selectedUnits);
                    return false;
                }
            case UnitActionType.Scouting:
                _scouting.Start(_selectedUnits);
                return false;
            case UnitActionType.ReturnToStorage:
                foreach (Harvester harvester in _selectedUnits.OfType<Harvester>())
                    _ = Globals.Game.NetworkClient.RequestHarvesterReturnAsync(harvester.UnitId);
                return false;
            case UnitActionType.TrainUnit:
                {
                    Building? building = _selectedUnits.Count == 1
                        ? _selectedUnits[0] as Building
                        : null;
                    if (building is not null && !string.IsNullOrWhiteSpace(action.TargetObjectName))
                    {
                        _ = Globals.Game.NetworkClient.RequestTrainUnitAsync(
                            building.UnitId,
                            action.TargetObjectName);
                    }
                    return false;
                }
            case UnitActionType.ClearRallyPoint:
                foreach (Unit unit in _selectedUnits.Where(unit => unit.SupportsRallyPoint))
                    _ = Globals.Game.NetworkClient.RequestSetRallyPointAsync(unit.UnitId, null);
                return false;
            case UnitActionType.LeaveContainer:
                {
                    if (_selectedUnits.Count == 1 && _selectedUnits[0].Occupancy is not null)
                        _ = Globals.Game.NetworkClient.RequestLeaveContainerAsync(_selectedUnits[0].UnitId);
                    return false;
                }
            case UnitActionType.SellBuilding:
                foreach (Building building in _selectedUnits.OfType<Building>().Where(
                    building => building is not GenericBuilding &&
                        Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, building.ArmyId)))
                    _ = RequestSellBuildingAsync(building);
                return false;
        }

        ActiveAction = action;        
        return true;
    }

    private static async Task RequestSellBuildingAsync(Building building)
    {
        int occupants = building.Occupancy?.Occupants.Count ?? 0;
        for (int index = 0; index < occupants; index++)
            await Globals.Game.NetworkClient.RequestLeaveContainerAsync(building.UnitId);
        await Globals.Game.NetworkClient.RequestSellBuildingAsync(building.UnitId);
    }

    public PlayerHandler(
        GameWorld map,
        MarkerHandler markerHandler)    
    {
        _map = map;
        _markerHandler = markerHandler;
        _scouting = new ScoutingController(map);
        _renderStates = new RenderStateStack(Globals.GraphicsDevice);

        _toolSize = 8;
        _toolShape = ToolShape.Circle;
        _nextAllowedActionTime = 0;
    }

    public void Update(GameTime gameTime, Camera camera, Viewport viewport)
    {
        _scouting.Update(gameTime);
        if (_selectedUnits.RemoveAll(unit =>
                !unit.IsSelectable ||
                !Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, unit.ArmyId)) > 0)
        {
            ActiveAction = null;
            NotifySelectionChanged();
        }

        _isDrag = false;        

        MouseState mouse = Mouse.GetState();
        //  update mouse-world position
        Point screenPosition = mouse.Position;
        Ray ray = CreatePickRay(camera, viewport, screenPosition);
        if (_map.Terrain.TryGetIntersection(ray, out Vector3 target))
        {
            MouseWorldPosition = new Vector3(target.X, target.Y, target.Z);
            IsMouseOnTerrain = true;
        }

        if (GetMode() == CurrentMode.BuildPreview)
        {            
            if (IsLeftButtonPressed(mouse))
            {
                PressLeftScreenPosition = mouse.Position;
                PressLeftWorldPosition = MouseWorldPosition;                
            }

            if (mouse.LeftButton == ButtonState.Pressed)
            {
                Point drag = mouse.Position - PressLeftScreenPosition;
                if (drag.ToVector2().Length() > 8)
                {
                    _isDrag = true;
                    _buildPreviewDegree = -MathHelper.ToDegrees((float)Math.Atan2(drag.Y, drag.X));
                }
            }
        
            if (IsLeftButtonReleased(mouse))
                if (IsMouseOnTerrain)
                {
                    PerformClickAction(_selectedUnits, PressLeftWorldPosition, _buildPreviewDegree);
                    _buildPreviewDegree = 0;
                }
        }

        if (GetMode() == CurrentMode.EditGameplayMarkers)
        {
            if (IsLeftButtonPressed(mouse))
            {
                PressLeftScreenPosition = mouse.Position;
                PressLeftWorldPosition = MouseWorldPosition;
            }
            if (mouse.LeftButton == ButtonState.Pressed)
            {
                Point drag = mouse.Position - PressLeftScreenPosition;
                if (drag.ToVector2().Length() > 8)
                {
                    _isDrag = true;
                    _buildPreviewDegree = -MathHelper.ToDegrees(MathF.Atan2(drag.Y, drag.X));
                }
            }
            if (IsLeftButtonReleased(mouse) && IsMouseOnTerrain && ActiveAction is not null)
            {
                RequestAction(ActiveAction, PressLeftWorldPosition, null, _buildPreviewDegree);
                _buildPreviewDegree = 0;
            }
            if (IsRightButtonPressed(mouse))
                ActiveAction = null;
        }

        if (GetMode() == CurrentMode.SelectUnits)
        {
            if (IsLeftButtonPressed(mouse))
                PressLeftScreenPosition = mouse.Position;

            if (mouse.LeftButton == ButtonState.Pressed)
            {
                _currentSelectionRect = CreateSelectionRectangle(PressLeftScreenPosition, mouse.Position);
                if (_currentSelectionRect.Width > 8 || _currentSelectionRect.Height > 8)
                {
                    _isSelectingUnits = true;
                    _isDrag = true;
                }
            }

            if (IsLeftButtonReleased(mouse))
            {
                if (SelectedUnits.Count == 0)
                    if (!_isSelectingUnits)
                        SelectUnits(camera, viewport, _currentSelectionRect, 1);
                    
                if (_isSelectingUnits)
                    SelectUnits(camera, viewport, _currentSelectionRect, 99);
                else
                    if (IsMouseOnTerrain)
                        PerformClickAction(_selectedUnits, MouseWorldPosition, 0);

                _isSelectingUnits = false;
            }

            if (IsRightButtonPressed(mouse))
                ClearSelection();
        }
        else
        {
            if (GetMode() == CurrentMode.EditTerrain)
            {
                if (gameTime.TotalGameTime.TotalMilliseconds > _nextAllowedActionTime)
                {
                    _nextAllowedActionTime = gameTime.TotalGameTime.TotalMilliseconds + 100; // 100 ms cooldown between actions
                    if (ActiveAction is not null)
                    {
                        if (IsMouseOnTerrain)
                            if (mouse.LeftButton == ButtonState.Pressed)
                                RequestAction(ActiveAction, MouseWorldPosition, null, 0);
                        if (mouse.RightButton == ButtonState.Pressed)
                        {
                            //  alternate actions for terrain editing
                            if (ActiveAction.Type == UnitActionType.RaiseTerrain)
                            {
                                UnitAction altAction = new (UnitActionType.LowerTerrain, ActiveAction.Name, ActiveAction.IconColumn, ActiveAction.IconRow);
                                RequestAction(altAction, MouseWorldPosition, null, 0);
                                
                            }
                            if (ActiveAction.Type == UnitActionType.LowerTerrain)
                            {
                                UnitAction altAction = new (UnitActionType.RaiseTerrain, ActiveAction.Name, ActiveAction.IconColumn, ActiveAction.IconRow);
                                RequestAction(altAction, MouseWorldPosition, null, 0);
                            }
                        }
                    }
                }
            }            
        }

        _previousMouseState = mouse;
    }

    public bool IsUnitSelected(Unit unit)
    {
        return _selectedUnits.Contains(unit);
    }

    UnitActionType SuggestAction(List<Unit> selectedUnits, Vector3 mouseWorldPosition, out Unit? targetUnit)
    {
        targetUnit = null;
        if (selectedUnits.Count == 0)
            return UnitActionType.None;

        if (ActiveAction?.Type != UnitActionType.Goto)
        {
            if (ActiveAction?.Type != UnitActionType.Follow)
                return UnitActionType.None;
        }

        targetUnit = FindUnitAt(Globals._camera, Globals.GraphicsDevice.Viewport, Mouse.GetState().Position);
        KeyboardState keyboardState = Keyboard.GetState();

        if (targetUnit == null)
        {
            if (ActiveAction?.Type == UnitActionType.Follow)
                return UnitActionType.None;
            if (keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl))
                return UnitActionType.Attack;

            return UnitActionType.Goto;
        }

        if (ActiveAction?.Type == UnitActionType.Follow)
            return UnitActionType.Follow;

        if (selectedUnits.Count == 1 &&
            selectedUnits[0] is Soldier soldier &&
            targetUnit.Occupancy is OccupancyComponent occupancy &&
            !keyboardState.IsKeyDown(Keys.LeftControl) &&
            !keyboardState.IsKeyDown(Keys.RightControl) &&
            occupancy.CanEnter(soldier))
        {
            return UnitActionType.EnterUnit;
        }

        if (targetUnit.IsEnemy(selectedUnits.First()))
            return UnitActionType.Attack;

        if (targetUnit.IsSamePlayer(selectedUnits.First()) && (targetUnit is Building))
        {
            Building b = (targetUnit as Building)!;
            if (!b.IsCompleted)
                return UnitActionType.BuildConstruction;

            if (!b.IsDamaged)
                return UnitActionType.Repair;
            return UnitActionType.None;
        }

        if (keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl))
            return UnitActionType.Attack;

        if (keyboardState.IsKeyDown(Keys.LeftAlt) || keyboardState.IsKeyDown(Keys.RightAlt))
            return UnitActionType.Repair;

        return UnitActionType.None;
    }

    bool PerformClickAction(List<Unit> selectedUnits, Vector3 mouseWorldPosition, float targetAngleY)
    {
        if (ActiveAction == null)
            return false;

        Unit? targetUnit;
        UnitActionType suggestedAction = SuggestAction(selectedUnits, mouseWorldPosition, out targetUnit);
        if (suggestedAction != UnitActionType.None)
        {
            SendRequestAction(suggestedAction, mouseWorldPosition, targetUnit);
            return true;
        }
        RequestAction(ActiveAction, mouseWorldPosition, targetUnit, targetAngleY);
        return true;
    }

    void ClearSelection(bool notify = true)
    {
        foreach (Unit unit in _selectedUnits)
            unit.Select(false);
        _selectedUnits.Clear();
        ActiveAction = null;
        if (notify)
            NotifySelectionChanged();
    }

    private void SelectUnits(Camera camera, Viewport viewport, Rectangle selection, int maxUnits) 
    {
        ClearSelection(notify: false);

        foreach (Unit unit in _map.Units.Units)
        {
            if (!unit.IsSelectable)
                continue;
            Rectangle unitBounds = unit.GetScreenBounds(
                camera.View,
                camera.Projection,
                viewport);

            if (!selection.Intersects(unitBounds))
                continue;

            if (!Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, unit.ArmyId))
                continue;

            if (_selectedUnits.Count < maxUnits)
            {
                _selectedUnits.Add(unit);
                unit.Select();
            }
        }

        var commonAction = _selectedUnits
            .SelectMany(unit => unit.Actions)
            .GroupBy(action => action.Type)
            .Where(group => group.Count() == _selectedUnits.Count)
            .Select(group => group.First())
            .FirstOrDefault();
        
        ActiveAction = commonAction;
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        _ = Globals.Game.NetworkClient.NotifyUnitsSelectedAsync(
            _selectedUnits.Select(unit => unit.UnitId).ToArray());
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

        private bool SendRequestAction(UnitActionType actionType, Vector3 targetPosition, Unit? targetUnit)
        {            
            if (_selectedUnits.Count == 0)
                return false;

            if (actionType == UnitActionType.Goto)
            {
                _scouting.Stop(_selectedUnits);
                Globals.Game.NetworkClient.RequestGotoAsync(_selectedUnits, targetPosition,
                    Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift));
                return true;
            }
            if (actionType == UnitActionType.Attack)
            {
                if (targetUnit is not null)
                    _ = Globals.Game.NetworkClient.RequestAttackTargetAsync(_selectedUnits, targetUnit.UnitId);
                else
                    _ = Globals.Game.NetworkClient.RequestAttackTerrainAsync(_selectedUnits, targetPosition);
                return true;
            }
            if (actionType == UnitActionType.Follow && targetUnit is not null)
            {
                _ = Globals.Game.NetworkClient.RequestFollowAsync(_selectedUnits, targetUnit.UnitId);
                return true;
            }
            if (actionType == UnitActionType.BuildConstruction)
            {
                Globals.Game.NetworkClient.RequestBuildConstructionAsync(_selectedUnits, targetUnit?.UnitId ?? Guid.Empty);
                return true;
            }
            if (actionType == UnitActionType.EnterUnit &&
                _selectedUnits.Count == 1 &&
                _selectedUnits[0] is Soldier soldier &&
                targetUnit?.Occupancy is not null)
            {
                _ = Globals.Game.NetworkClient.RequestEnterUnitAsync(
                    soldier.UnitId,
                    targetUnit.UnitId);
                return true;
            }
            return false;
        }

        private void RequestAction(UnitAction action, Vector3 targetPosition, Unit? targetUnit, float targetAngleY)
        {            
            if (_selectedUnits.Count == 0)
                return;

            if (action.Type == UnitActionType.Land)
            {
                Helipad? pad = targetUnit as Helipad ?? _map.GameGrid.GetOccupant(_map.GameGrid.ToCell(targetPosition)) as Helipad;
                foreach (Helicopter helicopter in _selectedUnits.OfType<Helicopter>())
                    _ = Globals.Game.NetworkClient.RequestHelicopterOrderAsync(helicopter.UnitId, HelicopterOrder.Land, targetPosition, pad?.UnitId);
                ActiveAction = null;
                return;
            }
            if (action.Type == UnitActionType.PlaceGameplayMarker && action.MarkerType is GameplayMarkerType markerType)
            {
                _map.GameplayMarkers.Add(markerType, targetPosition, targetAngleY, _toolSize);
                return;
            }
            if (action.Type == UnitActionType.Harvest)
            {
                foreach (Harvester harvester in _selectedUnits.OfType<Harvester>())
                    _ = Globals.Game.NetworkClient.RequestHarvestAsync(harvester.UnitId, targetPosition);
                ActiveAction = null;
                return;
            }
            if (action.Type == UnitActionType.MoveAway)
            {
                _scouting.Stop(_selectedUnits);
                foreach (MobileUnit unit in _selectedUnits.OfType<MobileUnit>())
                    _ = Globals.Game.NetworkClient.RequestMoveAwayAsync(unit.UnitId, targetPosition);
                ActiveAction = null;
                return;
            }
            if (action.Type == UnitActionType.PlaceTiberiumSource)
            {
                _map.Units.SpawnBuilding("tiberium-source", targetPosition, targetAngleY, Guid.NewGuid(), Guid.Empty);
                return;
            }
            if (action.Type is UnitActionType.PaintTiberium or UnitActionType.RemoveTiberium or UnitActionType.SimulateTiberiumArea)
            {
                Point[] terrainCells = TerrainHelper.GetCells(_map.Terrain,
                    new Vector2(targetPosition.X, targetPosition.Z), _toolShape, _toolSize);
                HashSet<Point> cells = terrainCells.Select(cell => new Point(
                    cell.X / _map.GameGrid.CellSize, cell.Y / _map.GameGrid.CellSize))
                    .Where(_map.GameGrid.Contains).ToHashSet();
                if (action.Type == UnitActionType.PaintTiberium)
                    _map.Tiberium.Paint(cells);
                else if (action.Type == UnitActionType.RemoveTiberium)
                {
                    _map.Tiberium.Remove(cells);
                    foreach (TiberiumSource source in _map.Units.Units.OfType<TiberiumSource>()
                        .Where(source => cells.Contains(_map.GameGrid.ToCell(source.Position))).ToArray())
                        _map.Units.RemoveMapObject(source);
                }
                else
                    _map.Tiberium.SimulateArea(cells, 10.0f, _map.Units.Units.OfType<TiberiumSource>());
                return;
            }
            if (action.Type == UnitActionType.DeleteGameplayMarker)
            {
                _map.GameplayMarkers.RemoveNearest(targetPosition, Math.Max(1.0f, _toolSize * 0.5f));
                return;
            }
            if (action.Type is UnitActionType.LevelAndConcrete or UnitActionType.RemoveConcrete)
            {
                if (_selectedUnits.Count != 1 || _selectedUnits[0] is not GDIBulldozer worker) return;
                EarthworkKind kind = action.Type == UnitActionType.LevelAndConcrete ? EarthworkKind.LevelAndConcrete : EarthworkKind.RemoveConcrete;
                if (!Earthwork.Preview(_map, worker, _map.GameGrid.ToCell(targetPosition), kind).IsAllowed) return;
                _ = Globals.Game.NetworkClient.RequestEarthworkAsync(worker.UnitId, targetPosition, kind);
                ActiveAction = null;
                return;
            }
            if (action.Type == UnitActionType.SetRallyPoint)
            {
                foreach (Unit unit in _selectedUnits.Where(unit => unit.SupportsRallyPoint))
                    _ = Globals.Game.NetworkClient.RequestSetRallyPointAsync(unit.UnitId, targetPosition);
                ActiveAction = null;
                return;
            }
            if (action.Type == UnitActionType.Goto)
            {
                _scouting.Stop(_selectedUnits);
                Globals.Game.NetworkClient.RequestGotoAsync(_selectedUnits, targetPosition,
                    Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift));
            }
            if (action.Type == UnitActionType.Attack)
            {
                if (targetUnit is not null)
                    _ = Globals.Game.NetworkClient.RequestAttackTargetAsync(_selectedUnits, targetUnit.UnitId);
                else
                    _ = Globals.Game.NetworkClient.RequestAttackTerrainAsync(_selectedUnits, targetPosition);
            }
            if (action.Type == UnitActionType.Follow && targetUnit is not null)
                _ = Globals.Game.NetworkClient.RequestFollowAsync(_selectedUnits, targetUnit.UnitId);
            if (action.Type == UnitActionType.Build)
            {
                Building? preview = BuildingFactory.SpawnBuilding(action.TargetObjectName, targetPosition,
                    targetAngleY, Guid.Empty, Guid.Empty);
                if (preview is null)
                    return;
                BuildingPlacement placement = preview.EvaluatePlacement(_map, targetPosition, targetAngleY);
                if (!placement.IsAllowed)
                {
                    if (TryGetMovablePlacementBlockers(placement, out MobileUnit[] blockers))
                    {
                        _scouting.Stop(blockers);
                        foreach (MobileUnit blocker in blockers)
                            _ = Globals.Game.NetworkClient.RequestMoveAwayAsync(blocker.UnitId, targetPosition);
                    }
                    return;
                }
                Guid buildingId = Guid.NewGuid();
                Globals.Game.NetworkClient.RequestBuildAsync(action.TargetObjectName, targetPosition, targetAngleY, buildingId);
                Globals.Game.NetworkClient.RequestBuildConstructionAsync(_selectedUnits, buildingId);
                ActiveAction = null;
            }
            if ((action.Type == UnitActionType.RaiseTerrain) || 
                (action.Type == UnitActionType.FlattenTerrain) || 
                (action.Type == UnitActionType.SmoothTerrain) || 
                (action.Type == UnitActionType.LowerTerrain) ||
                (action.Type == UnitActionType.SharpenTerrain))
            {
                Globals.Game.NetworkClient.RequestToolActionAsync(action, _toolShape, _toolSize, targetPosition);
            }
            if ((action.Type == UnitActionType.SetTerrainTile) ||
                (action.Type == UnitActionType.FillTile)
                )
            {
                Globals.Game.NetworkClient.RequestToolActionAsync(action, _toolShape, _toolSize, targetPosition, _currentTerrainTile);
            }
        }

        private bool TryGetMovablePlacementBlockers(
            BuildingPlacement placement,
            out MobileUnit[] blockers) =>
            placement.TryGetMovableBlockers(_map.GameGrid,
                mobile => Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, mobile.ArmyId),
                out blockers);

        private bool IsMovablePlacementBlocker(PlacementCell cell) =>
            BuildingPlacement.IsMovableBlocker(cell, _map.GameGrid,
                mobile => Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, mobile.ArmyId));

        private void RequestBuildConstruction(Building constructionSite)
        {
            if (_selectedUnits.Count == 0)
                return;

            _ = Globals.Game.NetworkClient.RequestBuildConstructionAsync(
                _selectedUnits,
                constructionSite.UnitId);
        }

        private Building? FindBuildingAt(
            Camera camera,
            Viewport viewport,
            Point screenPosition)
        {
            return _map.Units.Units
                .OfType<Building>()
                .FirstOrDefault(site => site.GetScreenBounds(
                    camera.View,
                    camera.Projection,
                    viewport).Contains(screenPosition));
        }

        private Unit? FindUnitAt(Camera camera, Viewport viewport, Point screenPosition)
        {
            return _map.Units.Units.FirstOrDefault(unit => unit.IsSelectable &&
                _map.Visibility.IsUnitVisibleToLocalPlayer(unit) && unit.GetScreenBounds(
                camera.View, camera.Projection, viewport).Contains(screenPosition));
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

    private CurrentMode GetMode()
    {    
        if (ActiveAction is null)
            return CurrentMode.SelectUnits;

        if (IsMouseOnTerrain)
        if  (
            (ActiveAction.Type == UnitActionType.RaiseTerrain) || 
            (ActiveAction.Type == UnitActionType.LowerTerrain) || 
            (ActiveAction.Type == UnitActionType.SmoothTerrain) || 
            (ActiveAction.Type == UnitActionType.FlattenTerrain) ||
            (ActiveAction.Type == UnitActionType.SharpenTerrain) ||
            (ActiveAction.Type == UnitActionType.SetTerrainTile) ||
            (ActiveAction.Type == UnitActionType.FillTile) 
            || ActiveAction.Type == UnitActionType.PaintTiberium
            || ActiveAction.Type == UnitActionType.RemoveTiberium
            || ActiveAction.Type == UnitActionType.SimulateTiberiumArea
            )
            return CurrentMode.EditTerrain;
        
        if (ActiveAction.Type == UnitActionType.Build)
            return CurrentMode.BuildPreview;

        if (ActiveAction.Type is UnitActionType.PlaceGameplayMarker or UnitActionType.DeleteGameplayMarker or UnitActionType.PlaceTiberiumSource)
            return CurrentMode.EditGameplayMarkers;

        return CurrentMode.SelectUnits;
    }

    public void Draw3D(Camera camera)
    {
        _buildPlacementPreview = null;
        _earthworkPreview = null;
        bool editorSelected = _selectedUnits.Any(unit => unit is TerrainEditorTool);
        if (editorSelected)
            _map.GameplayMarkers.DrawEditor(camera, _map.Terrain, _map.GameGrid);
        //  render editor-tool
        if (IsMouseOnTerrain)
        {
            if (GetMode() == CurrentMode.EditTerrain)
            {
                //  render the terrain modification tool at the mouse world position
                //  1. let the current tool determine all affected terrain cells
                MouseWorldPosition = MouseWorldPosition;
                Point[] affectedCells = TerrainHelper.GetCells(Globals.World.Terrain, new Vector2(MouseWorldPosition.X, MouseWorldPosition.Z), _toolShape, _toolSize);
                foreach (Point cell in affectedCells)
                {
                    _map.Terrain.HighlightCell(camera, cell.X, cell.Y);
                }
            }

            if (ActiveAction?.Type is UnitActionType.LevelAndConcrete or UnitActionType.RemoveConcrete &&
                _selectedUnits.Count == 1 && _selectedUnits[0] is GDIBulldozer worker)
            {
                EarthworkKind kind = ActiveAction.Type == UnitActionType.LevelAndConcrete ? EarthworkKind.LevelAndConcrete : EarthworkKind.RemoveConcrete;
                _earthworkPreview = Earthwork.Preview(_map, worker, _map.GameGrid.ToCell(MouseWorldPosition), kind);
                _renderStates.PushState();
                Globals.GraphicsDevice.DepthStencilState = DepthStencilState.None;
                foreach (EarthworkCell cell in _earthworkPreview.Cells)
                    DrawWorkCell(cell.Cell, !cell.Allowed ? new Color(255, 40, 40, 140)
                        : cell.NeedsWork ? new Color(40, 220, 80, 95) : new Color(160, 160, 160, 70));
                _renderStates.PopState();

                void DrawWorkCell(Point cell, Color color)
                {
                    int size = _map.GameGrid.CellSize;
                    for (int z = cell.Y * size; z < (cell.Y + 1) * size; z++)
                        for (int x = cell.X * size; x < (cell.X + 1) * size; x++) _map.Terrain.HighlightCell(camera, x, z, color);
                }
            }
            if (ActiveAction is not null)
            {
                if (ActiveAction.Type == UnitActionType.PlaceGameplayMarker && ActiveAction.MarkerType is GameplayMarkerType markerType)
                    _map.GameplayMarkers.DrawPreview(camera, _map.Terrain, _map.GameGrid, markerType,
                        _isDrag ? PressLeftWorldPosition : MouseWorldPosition, _buildPreviewDegree, _toolSize);
                if (ActiveAction.Type == UnitActionType.PlaceTiberiumSource)
                {
                    Point sourceCell = _map.GameGrid.ToCell(_isDrag ? PressLeftWorldPosition : MouseWorldPosition);
                    int left = sourceCell.X * _map.GameGrid.CellSize;
                    int top = sourceCell.Y * _map.GameGrid.CellSize;
                    for (int z = top; z < top + _map.GameGrid.CellSize; z++)
                        for (int x = left; x < left + _map.GameGrid.CellSize; x++)
                            if (x >= 0 && z >= 0 && x < _map.Terrain.Width - 1 && z < _map.Terrain.Height - 1)
                                _map.Terrain.HighlightCell(camera, x, z, new Color(70, 255, 90, 150));
                }
                //  render the active action's visual representation at the mouse world position
                if (ActiveAction.Type == UnitActionType.Build)
                {
                    Vector3 pos = MouseWorldPosition;
                    if (_isDrag)
                        pos = PressLeftWorldPosition;
                    
                    Building? unit = BuildingFactory.SpawnBuilding(ActiveAction.TargetObjectName, pos, _buildPreviewDegree, Guid.Empty, Guid.Empty);
                    if (unit is not null)
                    {
                        _buildPlacementPreview = unit.EvaluatePlacement(_map, pos, _buildPreviewDegree);
                        GraphicsDevice graphicsDevice = Globals.GraphicsDevice;
                        _renderStates.PushState();

                        graphicsDevice.BlendState = BlendState.NonPremultiplied;
                        graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                        Globals._unitEffect.Parameters["Opacity"]?.SetValue(0.75f);
                        unit.DrawPreview(Globals._unitEffect);
                        Globals._unitEffect.Parameters["Opacity"]?.SetValue(1.0f);
                        // Render annotations after the ghost, including cells hidden by an obstacle.
                        graphicsDevice.DepthStencilState = DepthStencilState.None;
                        foreach (PlacementCell cell in _buildPlacementPreview.Cells)
                        {
                            Color tint = cell.Issues != PlacementIssue.None
                                ? IsMovablePlacementBlocker(cell)
                                    ? new Color(255, 150, 30, 150)
                                    : new Color(255, 40, 40, 150)
                                : cell.IsClearance
                                    ? new Color(50, 150, 255, 110)
                                    : new Color(40, 220, 80, 100);
                            int left = cell.Cell.X * _map.GameGrid.CellSize;
                            int top = cell.Cell.Y * _map.GameGrid.CellSize;
                            for (int z = top; z < top + _map.GameGrid.CellSize; z++)
                                for (int x = left; x < left + _map.GameGrid.CellSize; x++)
                                    _map.Terrain.HighlightCell(camera, x, z, tint);
                        }
                        _renderStates.PopState();
                    }
                }
            }
        }
    }

    public void Draw2D(
        SpriteBatch spriteBatch,
        Camera camera,
        Viewport viewport)
    {                
        if (_selectedUnits.Any(unit => unit is TerrainEditorTool))
            _map.GameplayMarkers.DrawLabels(spriteBatch, camera, viewport);
        if (ActiveAction?.Type is UnitActionType.LevelAndConcrete or UnitActionType.RemoveConcrete && _earthworkPreview is EarthworkPreview preview)
        {
            Point mouse = Mouse.GetState().Position;
            string label = preview.IsAllowed ? (preview.Order.Kind == EarthworkKind.LevelAndConcrete
                ? $"Drive & level | Height {preview.Order.TargetHeight:0.00}" : "8 x 8 | Remove concrete") : "Cannot work here";
            RenderHelper.DrawTextCentered(spriteBatch, Globals._debugFont, label, new Vector2(mouse.X, mouse.Y + 28),
                preview.IsAllowed ? Color.LimeGreen : Color.Red);
        }
        if (ActiveAction?.Type == UnitActionType.Build && _buildPlacementPreview is BuildingPlacement placement)
        {
            Point mouse = Mouse.GetState().Position;
            bool canClear = TryGetMovablePlacementBlockers(placement, out _);
            string status = placement.IsAllowed ? "Build here" : canClear ? "Click to clear area" : "Cannot build here";
            RenderHelper.DrawTextCentered(spriteBatch, Globals._debugFont, status,
                new Vector2(mouse.X, mouse.Y + 28), placement.IsAllowed ? Color.LimeGreen : canClear ? Color.Orange : Color.Red);
        }
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
            if (Globals.Debug_ShowUnitCommands)
                RenderHelper.DrawTextCentered(spriteBatch, Globals._debugFont,
                    unit.GetDebugCommandText(),
                    new Vector2(unitBounds.Center.X, unitBounds.Top - 12),
                    Color.Yellow);
        }

        foreach (Unit unit in _selectedUnits)
        {
            if (unit.RallyPoint is not Vector3 point)
                continue;
            point.Y = _map.Terrain.GetHeight((int)point.X, (int)point.Z) + 0.1f;
            Vector3 screen = viewport.Project(point, camera.Projection, camera.View, Matrix.Identity);
            if (screen.Z < 0 || screen.Z > 1)
                continue;
            Globals.RenderHelper.DrawRectangle(spriteBatch,
                new Rectangle((int)screen.X - 5, (int)screen.Y - 5, 10, 10),
                Color.Gold * 0.35f, Color.Gold, borderThickness: 2);
            RenderHelper.DrawTextCentered(spriteBatch, Globals._debugFont, "Rally point",
                new Vector2(screen.X, screen.Y - 18), Color.Gold);
        }

        if (_isSelectingUnits)
            Globals.RenderHelper.DrawRectangle(
                spriteBatch,
                _currentSelectionRect,
                Color.White * 0.1f,
                Color.LimeGreen,
                borderThickness: 2);

        if (IsMouseOnTerrain)
        {
            Unit? targetUnit;
            UnitActionType suggestedAction = SuggestAction(_selectedUnits, MouseWorldPosition, out targetUnit);
            if (suggestedAction != UnitActionType.None)
            {
                string suggestion = suggestedAction.ToString();
                RenderHelper.DrawTooltip(spriteBatch, suggestion, _previousMouseState.Position.X + 24, _previousMouseState.Position.Y + 16);
            }
        }
    }
}
