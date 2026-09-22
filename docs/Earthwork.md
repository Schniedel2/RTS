# Bulldozer earthwork

Select one occupied bulldozer, choose **Level & concrete**, and click the destination.
The host freezes the terrain height at the bulldozer's starting cell center. The bulldozer
travels straight toward the selected cell center at its work speed (up to 2.5 units/s),
leveling and concreting its full width continuously. There is no per-tile wait or progress bar.
The preview shows the drivable prefix and the blocked continuation in red. Terrain cells
intersecting the physical vehicle rectangle are included, also for diagonal travel.

The full next cross-section is validated before any terrain changes. Blocked/no-vehicle
cells, other occupants (including neighbors sharing affected vertices), map borders or a
height change exceeding `Earthwork.MaximumHeightChange` stop the order. Completed work
remains. Existing concrete can be regraded. Ordinary Stop/movement orders cancel the job.

**Remove concrete** retains its 8x8 area selection and changes concrete to dirt without
restoring previous heights. Shift queues are not implemented.

The host broadcasts ordered, absolute terrain patches and worker positions. Clients apply
each patch once and rebuild graphics once per batch. The headless regression checks cover
movement, strip width, diagonal travel, obstacle stops, ownership, cancellation and wire replay.
