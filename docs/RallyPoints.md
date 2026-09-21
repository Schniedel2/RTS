# Rally points

Select a GDIBarracks, choose **Set rally point**, then left-click the terrain.
The selected barracks displays its confirmed point with a gold marker.
**Clear rally point** restores the default behavior of stopping at the exit.
Points can already be set during construction.

The client sends SetRallyPointRequest. The host checks army command permission,
the unit capability, finite coordinates, map boundaries and target cell rules.
Blocked/excluded cells and cells occupied by buildings are rejected. Temporary
mobile occupancy is allowed. Confirmation is broadcast as SetRallyPointCommand.
Coordinates and revisions also travel in the building state heartbeat; stale
rally updates cannot overwrite newer ones. Actual reachability is checked by the
produced unit's pathfinding, since capabilities and occupancy can change.

The production SpawnCommand captures the rally point at production completion.
All peers give that recruit the same destination after it reaches pivot:exit and
registers in the grid. If the point is occupied, it searches for a free allowed
cell within six grid cells. Normal pathfinding handles the resulting route.
Manual movement or Stop cancels the pending automatic rally order. Later rally
point changes affect subsequent production, not recruits already spawned.

The capability lives on Unit:

```csharp
public override bool SupportsRallyPoint => true;
```

Add SetRallyPoint and ClearRallyPoint to that type's Actions to expose the UI.
The request/confirmation path supports any opted-in Unit; building state and
production handling are already wired up. A future non-building producer must
include GetRallyPointState/ApplyRallyPointState in its specialized state payload
if it needs periodic state synchronization, and define how it dispatches recruits.

Headless tests include host permission checks, invalid destinations, JSON message
round trips, client application, state restoration, clearing, stale revisions,
production snapshots, exit ordering, manual cancellation and occupied targets:

```text
dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj
```
