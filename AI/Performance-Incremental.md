# Graphics-free performance baseline

UTC: 2026-10-01T04:50:58.1679093Z
OS: Microsoft Windows 10.0.19045
Runtime: .NET 10.0.9
Architecture: X64; logical CPUs: 16
GC server: False
Tiered compilation override: 0
Actual searches started during measured workload: 624

Incremental mode: 543 planning updates; maximum 2048 steps/update; maximum 2.001 ms/update. Default budget: 2048 steps and 2 ms; the time check occurs between individual work steps. Updates are advanced without frame sleeps, so group totals are CPU work, not in-game delivery latency.

Scenario: flat 48x48 grid, seed 12345, two active AI players waiting for base resources, 8 mobile units per army; three reachable and three disconnected group goals per army. Full-height wall at x=24 disconnects targets. Twelve building-site searches. One reachable and one unreachable unmeasured warmup per army. No movement, rendering, transport or complete match simulation. Host Goto planning is invoked directly; request dispatch is measured in-game separately.

These results establish route-planning and resource-wait baselines, not the cost of a mature AI battle.

```text
Inclusive game-thread measurements; nested rows overlap. Allocations are bytes, not retained memory.
Scope | Calls | Total ms | Mean ms | Max ms | Allocated bytes | Max bytes/call
Baseline.UnreachableGroup | 6 | 820.203 | 136.700 | 139.643 | 782967840 | 130599496
Path.PlanningUpdate | 543 | 826.087 | 1.521 | 2.001 | 792203072 | 1712768
Baseline.ReachableGroup | 6 | 6.304 | 1.051 | 1.281 | 9293296 | 1622888
Host.GotoPlanningSlice | 68546 | 819.596 | 0.012 | 0.856 | 792203016 | 161744
AI.Player | 12 | 0.133 | 0.011 | 0.018 | 16760 | 1448
AI.ArmyGoals | 12 | 0.127 | 0.011 | 0.018 | 15072 | 1256
AI.BuildSiteSearch | 12 | 0.114 | 0.009 | 0.014 | 30336 | 2528
```
