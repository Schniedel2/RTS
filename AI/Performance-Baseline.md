# Graphics-free performance baseline

UTC: 2026-10-01T04:07:42.5094284Z
OS: Microsoft Windows 10.0.19045
Runtime: .NET 10.0.9
Architecture: X64; logical CPUs: 16
GC server: False
Tiered compilation override: 0

Scenario: flat 48x48 grid, seed 12345, two active AI players waiting for base resources, 8 mobile units per army; three reachable and three disconnected group goals per army. Full-height wall at x=24 disconnects targets. Twelve building-site searches. One reachable and one unreachable unmeasured warmup per army. No movement, rendering, transport or complete match simulation. Host Goto planning is invoked directly; request dispatch is measured in-game separately.

These results establish route-planning and resource-wait baselines, not the cost of a mature AI battle.

```text
Inclusive game-thread measurements; nested rows overlap. Allocations are bytes, not retained memory.
Scope | Calls | Total ms | Mean ms | Max ms | Allocated bytes | Max bytes/call
Baseline.UnreachableGroup | 6 | 912.720 | 152.120 | 171.832 | 782619744 | 130541480
Host.GotoPlanning | 12 | 917.593 | 76.466 | 171.831 | 789223040 | 130541448
Pathfinder.Search | 624 | 916.348 | 1.469 | 6.055 | 788633904 | 1358992
Baseline.ReachableGroup | 6 | 4.900 | 0.817 | 1.125 | 6603984 | 1112112
AI.Player | 12 | 0.180 | 0.015 | 0.032 | 16760 | 1448
AI.ArmyGoals | 12 | 0.172 | 0.014 | 0.030 | 15072 | 1256
AI.BuildSiteSearch | 12 | 0.161 | 0.013 | 0.029 | 30336 | 2528
```
