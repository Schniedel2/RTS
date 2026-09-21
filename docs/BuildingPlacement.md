# Building placement

The translucent building preview now overlays its grid footprint on the terrain:
green cells are valid, red cells are occupied, blocked, or participate in a height
difference above the permitted limit. A cursor label confirms whether the whole
site is valid, including footprints partly outside the terrain. Invalid clicks
keep the build tool active.

`Building.MaximumTerrainHeightDifference` defaults to **0.5 world units** and can
be overridden in each building constructor. The test compares the highest and
lowest terrain vertices across the entire footprint, not just each cell's slope.
It includes all shared boundary vertices and intermediate vertices for larger
grid cells. Rotated buildings and authored footprint offsets use the same cells
as occupancy. Both high and low cells participating in excessive variation are
marked red. Terrain is not automatically flattened.

`Building.EvaluatePlacement(world, position, rotation)` returns the shared
`BuildingPlacement` result with per-cell reasons and the total height difference.
Preview, click validation and actual UnitHandler placement use this result.
The host validates BuildRequest through the actual placement path and immediately
reserves accepted footprints, so simultaneous requests cannot overlap. Replicated
confirmation of an existing building is idempotent.

Headless checks cover flat ground, exact tolerance, boundary and interior
vertices, rotation, occupation, blocked cells, map edges, direct spawning and
competing host build requests:

```text
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj
```
