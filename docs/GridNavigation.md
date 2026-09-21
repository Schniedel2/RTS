# Grid navigation

`GameGrid.GetCell(x, y)` exposes terrain rules separately from occupancy.
`GetOccupant(x, y)` returns the current Unit (including buildings), or null.
Read its `UnitId` for an identifier; removing an occupant preserves terrain rules.

```csharp
GameGrid grid = Globals.World.GameGrid;
GridCell cell = grid.GetCell(10, 20);
cell.AllowedMovement = MovementModes.Walk | MovementModes.Climb; // No vehicles
cell.AllowedMovement = MovementModes.Climb; // Climbing only
cell.IsBlocked = true; // No access, regardless of capabilities
cell.ExcludeFromPathfinding = true; // No automatic routes; explicit movement remains possible
cell.MovementCost = 3.0f; // Prefer cheaper routes
Unit? occupant = grid.GetOccupant(10, 20);
```

These are independent properties; clear `IsBlocked` to reopen a cell. Defaults
are all movement modes, no blocking/exclusion and cost 1. Costs must be finite
and at least 1. They affect route selection, not animation or movement speed.
For units spanning multiple cells, the highest covered cost is used.

`GroundMovementProfile` defaults to driving with a 35-degree slope limit.
Soldiers default to walking with a 50-degree limit. Override per unit through
the existing constructor argument, for example:

```csharp
var climber = new GroundMovementProfile(MovementModes.Walk | MovementModes.Climb, 50, 85);
var motorcycle = new GroundMovementProfile(MovementModes.Drive | MovementModes.SteepDrive, 35, 80);
```

Normal and special modes are alternatives. A mode must be allowed by the cell
and supported by the unit, and its slope limit must hold. Climbing here enables
navigation only; dedicated animations and motorcycle types are separate work.

Maximum slope comes from the two actual rendered terrain triangles per tile;
larger grid cells use the maximum of every covered tile. Terrain edits and map
replacement invalidate the cached slopes. Access always goes through GetCell
to refresh derived values. Cells without a complete underlying terrain surface
are inaccessible. Slope is independent of travel direction; no CanTraverse is needed.

Placement, turning and movement validate the entire footprint. Diagonal paths
cannot cut blocked corners. Registered movement also checks intervening cells
on long steps. Spawn/disembark registration validates only the destination.
Pathfinding exclusions are applied to the footprint; units may leave an excluded
initial footprint but cannot plan a destination within an exclusion.

Custom cell rules are currently in-memory configuration. They are not yet part
of map files or network WorldData; configure them consistently on participating
worlds. Terrain slopes are derived from the existing saved/transmitted heightmap.
No terrain-painting UI or selection behavior has been changed.

Run headless regression checks with:

```text
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj
```
