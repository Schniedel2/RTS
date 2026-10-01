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
    private float? _formationFacingDegrees;
    private bool _formationPlacementActive;

    private int _toolSize;
    private ToolShape _toolShape;    
    private double _nextAllowedActionTime;
    public TerrainTile _currentTerrainTile = TerrainTile.Grass;
    private float _buildPreviewDegree;
    private BuildingPlacement? _buildPlacementPreview;
    private EarthworkPreview? _earthworkPreview;
    private readonly ScoutingController _scouting;

    public static bool SingleActor(UnitAction a) => a.Type is UnitActionType.TrainUnit or UnitActionType.Research or UnitActionType.AssembleSquad or UnitActionType.DisbandSquad or UnitActionType.LeaveContainer;
    public static List<Unit> Recipients(IEnumerable<Unit> units, UnitAction a) => units.Where(u => u.Actions.Any(b => b.Type == a.Type && b.TargetObjectName == a.TargetObjectName && b.MarkerType == a.MarkerType)).ToList();
    public static List<Unit> ControllableUnits(IEnumerable<Unit> units) => units
        .Where(unit => !Globals.IsSpectator)
        .Where(unit => Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, unit.ArmyId)).ToList();

    public void SetSpectator(bool enabled)
    {
        Globals.IsSpectator = enabled;
        ActiveAction = null;
        _formationPlacementActive = false;
        _scouting.Stop(_map.Units.GetSnapshot());
    }

    public bool SelectAction(UnitAction action, bool alternateAction)
    {
        var recipients = Recipients(ControllableUnits(_selectedUnits), action);
        if (recipients.Count == 0 || SingleActor(action) && recipients.Count != 1) return false;
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
                foreach (Helicopter helicopter in recipients.OfType<Helicopter>())
                    _ = Globals.Game.NetworkClient.RequestHelicopterOrderAsync(helicopter.UnitId,
                        action.Type == UnitActionType.TakeOff ? HelicopterOrder.TakeOff : HelicopterOrder.ReturnToHelipad, helicopter.Position);
                return false;
            case UnitActionType.Stop:
                {
                    _scouting.Stop(recipients);
                    if (action.Type == UnitActionType.Stop)
                        _ = Globals.Game.NetworkClient.RequestStopAsync(recipients);
                    return false;
                }
            case UnitActionType.Scouting:
                _scouting.Start(recipients);
                return false;
            case UnitActionType.ReturnToStorage:
                foreach (Harvester harvester in recipients.OfType<Harvester>())
                    _ = Globals.Game.NetworkClient.RequestHarvesterReturnAsync(harvester.UnitId);
                return false;
            case UnitActionType.TrainUnit:
                {
                    Building? building = recipients.Count == 1
                        ? recipients[0] as Building
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
                foreach (Unit unit in recipients.Where(unit => unit.SupportsRallyPoint))
                    _ = Globals.Game.NetworkClient.RequestSetRallyPointAsync(unit.UnitId, null);
                return false;
            case UnitActionType.LeaveContainer:
                {
                    if (recipients.Count == 1 && recipients[0].Occupancy is not null)
                        _ = Globals.Game.NetworkClient.RequestLeaveContainerAsync(recipients[0].UnitId);
                    return false;
                }
            case UnitActionType.Research:
                {
                    Building? building = recipients.Count == 1
                        ? recipients[0] as Building
                        : null;
                    if (building is not null && !string.IsNullOrWhiteSpace(action.TargetObjectName))
                        _ = Globals.Game.NetworkClient.RequestResearchAsync(
                            building.UnitId, action.TargetObjectName);
                    return false;
                }
            case UnitActionType.SellBuilding:
                foreach (Building building in recipients.OfType<Building>().Where(
                    building => building is not GenericBuilding &&
                        Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, building.ArmyId)))
                    _ = RequestSellBuildingAsync(building);
                return false;
            case UnitActionType.CancelConstruction:
                foreach (Building building in recipients.OfType<Building>().Where(
                    building => !building.IsCompleted && building is not GenericBuilding &&
                        Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, building.ArmyId)))
                    _ = Globals.Game.NetworkClient.RequestCancelConstructionAsync(building.UnitId);
                return false;
            case UnitActionType.Destroy:
                foreach (Building building in recipients.OfType<Building>().Where(
                    building => Globals.Game.Armies.CanControl(
                        Globals.Game.Network.LocalPeerId, building.ArmyId)))
                    _ = Globals.Game.NetworkClient.RequestDestroyBuildingAsync(building.UnitId);
                return false;
        }

        if (!action.RequiresTarget)
        {
            _ = Globals.Game.NetworkClient.RequestUnitActionAsync(
                recipients, action.Type, new UnitActionContext(Alternate: alternateAction));
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
        if (!Globals.IsSpectator) _scouting.Update(gameTime);
        if (_selectedUnits.RemoveAll(unit =>
                !unit.IsSelectable ||
                !_map.Visibility.IsUnitVisibleToLocalPlayer(unit)) > 0)
        {
            ActiveAction = null;
            NotifySelectionChanged();
        }

        if (Keyboard.GetState().IsKeyDown(Keys.Escape)) ActiveAction = null;
        _isDrag = false;        

        MouseState mouse = Mouse.GetState();
        //  update mouse-world position
        Point screenPosition = mouse.Position;
        Ray ray = CreatePickRay(camera, viewport, screenPosition);
        IsMouseOnTerrain = false;
        if (_map.Terrain.TryGetIntersection(ray, out Vector3 target) &&
            float.IsFinite(target.X) && float.IsFinite(target.Y) && float.IsFinite(target.Z))
        {
            MouseWorldPosition = new Vector3(target.X, target.Y, target.Z);
            IsMouseOnTerrain = true;
        }

        if (IsRightButtonPressed(mouse) && ActiveAction is not null)
        {
            ActiveAction = null;
            _formationPlacementActive = false;
            _previousMouseState = mouse;
            return;
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
            SquadLeader? formationLeader = GetSelectedSquadLeader();
            bool formationGoto = ActiveAction?.Type == UnitActionType.Goto && formationLeader is not null;
            if (formationGoto)
            {
                if (IsLeftButtonPressed(mouse) && IsMouseOnTerrain)
                {
                    PressLeftScreenPosition = mouse.Position;
                    PressLeftWorldPosition = MouseWorldPosition;
                    _formationFacingDegrees = null;
                    _formationPlacementActive = true;
                }
                if (_formationPlacementActive && mouse.LeftButton == ButtonState.Pressed && IsMouseOnTerrain &&
                    !IsClick(PressLeftScreenPosition, mouse.Position))
                {
                    _isDrag = true;
                    Vector2 direction = new(
                        MouseWorldPosition.X - PressLeftWorldPosition.X,
                        MouseWorldPosition.Z - PressLeftWorldPosition.Z);
                    if (direction.LengthSquared() > 0.01f)
                        _formationFacingDegrees = MathHelper.ToDegrees(
                            MathF.Atan2(-direction.X, -direction.Y));
                }
                if (IsLeftButtonReleased(mouse))
                {
                    if (_formationPlacementActive && IsMouseOnTerrain)
                    {
                        _scouting.Stop(_selectedUnits);
                        Globals.Game.NetworkClient.RequestGotoAsync(_selectedUnits, PressLeftWorldPosition,
                            Keyboard.GetState().IsKeyDown(Keys.LeftShift) ||
                            Keyboard.GetState().IsKeyDown(Keys.RightShift),
                            formationFacingDegrees: _formationFacingDegrees);
                    }
                    _formationPlacementActive = false;
                }
            }
            else
            {
                _formationPlacementActive = false;
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
                    bool firstSelection = ControllableUnits(_selectedUnits).Count == 0;
                    if (firstSelection && !_isSelectingUnits)
                        SelectUnits(camera, viewport, _currentSelectionRect, 1);

                    if (_isSelectingUnits)
                        SelectUnits(camera, viewport, _currentSelectionRect, 99);
                    else if (!firstSelection && IsMouseOnTerrain)
                        PerformClickAction(_selectedUnits, MouseWorldPosition, 0);

                    _isSelectingUnits = false;
                }
            }

            if (IsRightButtonPressed(mouse))
            {
                if (ActiveAction is not null) ActiveAction = null;
                else ClearSelection();
            }
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

    private (UnitAction Action, Unit? Target, List<Unit> Units) ResolveAction(Vector3 position)
    {
        Unit? target = FindUnitAt(Globals._camera, Globals.GraphicsDevice.Viewport, Mouse.GetState().Position);
        var controlled = ControllableUnits(_selectedUnits);
        var selection = ActiveAction is null && controlled.Any(u => u is MobileUnit)
            ? controlled.Where(u => u is MobileUnit).ToList() : controlled;
        UnitActionType type = UnitActionType.None;
        if (selection.Count > 0)
        {
            if (target is not null && selection.Any(u => u.IsEnemy(target))) type = UnitActionType.Attack;
            else if (target?.Occupancy is not null && selection.OfType<Soldier>().Any(u => target.Occupancy.CanEnter(u))) type = UnitActionType.EnterUnit;
            else if (target is Building { IsCompleted: false } && selection.Any(u => u.IsSamePlayer(target) && u is MobileUnit { BuildRate: > 0 })) type = UnitActionType.BuildConstruction;
            else if (target is Helipad && selection.Any(u => u is Helicopter)) type = UnitActionType.Land;
            else if (_map.Tiberium.Cells.ContainsKey(_map.GameGrid.ToCell(position)) && selection.Any(u => u is Harvester)) type = UnitActionType.Harvest;
            else type = selection.Any(u => u is MobileUnit) ? UnitActionType.Goto : UnitActionType.SetRallyPoint;
        }
        UnitAction action = ActiveAction ?? new UnitAction(type, type.ToString(), 0, 0);
        var recipients = action.Type == UnitActionType.EnterUnit
            ? selection.Where(u => u is Soldier soldier && target?.Occupancy?.CanEnter(soldier) == true).ToList()
            : Recipients(selection, action);
        if (action.Type == UnitActionType.Attack) recipients = recipients.Where(u => u.CanFireWeapon && (target is null || u.CanAttackTarget(target))).ToList();
        if (action.Type == UnitActionType.BuildConstruction && (target is not Building { IsCompleted: false } || !recipients.Any(u => u.IsSamePlayer(target)))) recipients.Clear();
        if (action.Type == UnitActionType.Follow && target is null) recipients.Clear();
        if (SingleActor(action) && recipients.Count != 1) recipients.Clear();
        return (action, target, recipients);
    }

    bool PerformClickAction(List<Unit> selectedUnits, Vector3 mouseWorldPosition, float targetAngleY)
    {
        var result = ResolveAction(mouseWorldPosition);
        if (result.Units.Count == 0) return false;
        if (result.Action.Type == UnitActionType.EnterUnit)
        {
            foreach (Soldier soldier in result.Units.OfType<Soldier>())
                _ = Globals.Game.NetworkClient.RequestEnterUnitAsync(soldier.UnitId, result.Target!.UnitId);
        }
        else if (result.Action.Type == UnitActionType.BuildConstruction)
            _ = Globals.Game.NetworkClient.RequestBuildConstructionAsync(result.Units, result.Target!.UnitId);
        else RequestAction(result.Action, mouseWorldPosition, result.Target, targetAngleY, result.Units);
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

        Unit[] candidates = _map.Units.Units
            .Where(unit => unit.IsSelectable &&
                _map.Visibility.IsUnitVisibleToLocalPlayer(unit) &&
                selection.Intersects(unit.GetScreenBounds(camera.View, camera.Projection, viewport)))
            .ToArray();

        // A mobile unit standing on a building (for example a helicopter on a
        // helipad) must remain directly selectable. The same rule keeps drag
        // selection from creating accidental mixed unit/building groups.
        IEnumerable<Unit> prioritized = PrioritizeMobileUnits(candidates);
        foreach (Unit unit in prioritized.Take(maxUnits))
        {
            _selectedUnits.Add(unit);
            unit.Select();
        }

        ActiveAction = null;
        NotifySelectionChanged();
    }

    public void SetUnitSelection(IReadOnlyList<Unit> units)
    {
        ClearSelection(notify: false);
        foreach (Unit unit in units)
        {
            if (!unit.IsSelectable || !_map.Visibility.IsUnitVisibleToLocalPlayer(unit)) continue;
            _selectedUnits.Add(unit);
            unit.Select();
        }
        ActiveAction = null;
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        _ = Globals.Game.NetworkClient.NotifyUnitsSelectedAsync(
            _selectedUnits.Select(unit => unit.UnitId).ToArray());
    }

    private SquadLeader? GetSelectedSquadLeader()
    {
        if (_selectedUnits.Count != 1 || _selectedUnits[0] is not SquadLeader leader)
            return null;
        return _map.Units.Units.OfType<Soldier>().Any(unit =>
            unit != leader && unit.SquadLeaderId == leader.UnitId && !unit.IsDying)
            ? leader
            : null;
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

        private void RequestAction(UnitAction action, Vector3 targetPosition, Unit? targetUnit, float targetAngleY, List<Unit>? recipients = null)
        {
            if (Globals.IsSpectator) return;
            recipients ??= Recipients(_selectedUnits, action);
            recipients = ControllableUnits(recipients);
            if (recipients.Count == 0)
                return;

            if (action.Type == UnitActionType.Land)
            {
                Helipad? pad = targetUnit as Helipad ?? _map.GameGrid.GetOccupant(_map.GameGrid.ToCell(targetPosition)) as Helipad;
                foreach (Helicopter helicopter in recipients.OfType<Helicopter>())
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
                foreach (Harvester harvester in recipients.OfType<Harvester>())
                    _ = Globals.Game.NetworkClient.RequestHarvestAsync(harvester.UnitId, targetPosition);
                ActiveAction = null;
                return;
            }
            if (action.Type == UnitActionType.MoveAway)
            {
                _scouting.Stop(recipients);
                foreach (MobileUnit unit in recipients.OfType<MobileUnit>())
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
                if (recipients.Count != 1 || recipients[0] is not GDIBulldozer worker) return;
                EarthworkKind kind = action.Type == UnitActionType.LevelAndConcrete ? EarthworkKind.LevelAndConcrete : EarthworkKind.RemoveConcrete;
                if (!Earthwork.Preview(_map, worker, _map.GameGrid.ToCell(targetPosition), kind).IsAllowed) return;
                _ = Globals.Game.NetworkClient.RequestEarthworkAsync(worker.UnitId, targetPosition, kind);
                ActiveAction = null;
                return;
            }
            if (action.Type == UnitActionType.SetRallyPoint)
            {
                foreach (Unit unit in recipients.Where(unit => unit.SupportsRallyPoint))
                    _ = Globals.Game.NetworkClient.RequestSetRallyPointAsync(unit.UnitId, targetPosition);
                ActiveAction = null;
                return;
            }
            if (action.Type == UnitActionType.Goto)
            {
                _scouting.Stop(recipients);
                Globals.Game.NetworkClient.RequestGotoAsync(recipients, targetPosition,
                    Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift));
            }
            if (action.Type == UnitActionType.Attack)
            {
                if (targetUnit is not null)
                    _ = Globals.Game.NetworkClient.RequestAttackTargetAsync(recipients, targetUnit.UnitId);
                else
                    _ = Globals.Game.NetworkClient.RequestAttackTerrainAsync(recipients, targetPosition);
            }
            if (action.Type == UnitActionType.Follow && targetUnit is not null)
                _ = Globals.Game.NetworkClient.RequestFollowAsync(recipients, targetUnit.UnitId);
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
                _scouting.Stop(recipients);
                _ = Globals.Game.NetworkClient.RequestBuildAndConstructAsync(
                    action.TargetObjectName, targetPosition, targetAngleY, buildingId, recipients);
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
            if (Globals.IsSpectator || _selectedUnits.Count == 0)
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
            Ray ray = CreatePickRay(camera, viewport, screenPosition);
            float terrainDistance = _map.Terrain.TryGetIntersection(ray, out Vector3 terrainHit)
                ? Vector3.Distance(ray.Position, terrainHit) + 0.01f
                : float.PositiveInfinity;
            var hits = _map.Units.Units
                .Where(unit => unit.IsSelectable && _map.Visibility.IsUnitVisibleToLocalPlayer(unit))
                .Select(unit => (Unit: unit, Distance: unit.IntersectSelectionRay(ray)))
                .Where(hit => hit.Distance is float distance && distance <= terrainDistance)
                .OrderBy(hit => hit.Distance)
                .Select(hit => hit.Unit).ToArray();
            return PrioritizeMobileUnits(hits).FirstOrDefault();
        }

        private static IEnumerable<Unit> PrioritizeMobileUnits(IReadOnlyList<Unit> candidates) =>
            candidates.Any(unit => unit is MobileUnit)
                ? candidates.Where(unit => unit is MobileUnit)
                : candidates;

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
        if (Globals.IsSpectator || ActiveAction is null)
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
            if (ActiveAction?.Type == UnitActionType.Goto &&
                GetSelectedSquadLeader() is SquadLeader formationLeader)
            {
                Vector3 anchorWorld = _isDrag ? PressLeftWorldPosition : MouseWorldPosition;
                Vector2 anchor = new(anchorWorld.X, anchorWorld.Z);
                Soldier[] members = _map.Units.Units.OfType<Soldier>()
                    .Where(unit => unit != formationLeader &&
                        unit.SquadLeaderId == formationLeader.UnitId && !unit.IsDying)
                    .ToArray();
                foreach (Vector2 position in SquadFormation.CreateAssignments(
                    formationLeader, members, anchor,
                    _isDrag ? _formationFacingDegrees : null, _map.GameGrid.CellSize).Values)
                {
                    Point cell = _map.GameGrid.ToCell(new Vector3(position.X, 0.0f, position.Y));
                    if (!_map.GameGrid.Contains(cell))
                        continue;
                    int size = _map.GameGrid.CellSize;
                    for (int z = cell.Y * size; z < (cell.Y + 1) * size; z++)
                        for (int x = cell.X * size; x < (cell.X + 1) * size; x++)
                            _map.Terrain.HighlightCell(camera, x, z,
                                new Color(70, 180, 255, 120));
                }
            }
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
                if (ActiveAction.Type == UnitActionType.DeleteGameplayMarker)
                    _map.GameplayMarkers.DrawRemovalPreview(camera, _map.Terrain, _map.GameGrid,
                        MouseWorldPosition, Math.Max(1.0f, _toolSize * 0.5f));
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
            //  show selection group numbers
            
            if (unit.MemberOfSelectionGroups.Count > 0)
            {
                string groupNumbers = string.Join(", ", unit.MemberOfSelectionGroups);
                RenderHelper.DrawTextCentered(spriteBatch, Globals._debugFont,
                    $"{groupNumbers}",
                    new Vector2(unitBounds.Center.X, unitBounds.Top - 24),
                    Color.Cyan);
            }
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
            var result = ResolveAction(MouseWorldPosition);
            if (_selectedUnits.Count > 0)
            {
                string suggestion = result.Units.Count > 0
                    ? $"{result.Action.Name} ({result.Units.Count}/{_selectedUnits.Count})"
                    : "No suitable action for this target";
                RenderHelper.DrawTooltip(spriteBatch, suggestion, _previousMouseState.Position.X + 24, _previousMouseState.Position.Y + 16);
            }
        }
    }
}
