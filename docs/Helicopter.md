# Helicopter and helipad

Spawn with `spawn helicopter` (alias `spawn heli`), or create the `Helicopter` class.
The initial vehicle has an integrated pilot and starts landed. Its model is `vehicles/heli-1.bbmodel`.
The bulldozer can build a **Helipad**; the existing `Helipad` factory name also remains available.
Only completed, friendly helipads can service aircraft.

## Controls

- **Fly to** / movement command: take off and fly directly to the selected location.
- **Take off**: climb and hover.
- **Land**: click a helipad or ground location. Unsafe/busy sites fall back to a nearby free ground site.
- **Return to helipad**: reserve the nearest available friendly pad. If none is free, land on nearby suitable ground.
- **Stop / hover**: cancel movement or landing. An airborne helicopter stays in the air and continues consuming fuel.
- **Attack** and **Follow** use air movement and can replace an approach order.

The host simulates flight, supply use and landing reservations, then broadcasts position, flight phase,
fuel, ammunition and the reserved landing site. Aircraft ignore ground pathfinding while airborne and
climb above terrain/buildings. Ground landings require a clear, nearly level footprint; their cells are
reserved during approach. A pad can be reserved by only one helicopter. Landing on it never overwrites
the building's grid occupancy. Pad loss or ownership changes invalidate the approach.

## Initial balance values

Properties on `Helicopter` are configurable: speed 10 units/s, climb/descent 4 units/s, cruise clearance
8 units, rotation 180 degrees/s, fuel 120 with consumption 1/s while airborne, ammunition 40,
range 18, damage 12 and shot interval 0.2 s. Fuel and ammunition are shown for selected helicopters.
The weapon uses the existing hitscan attack system and muzzle flash, without recoil.

At 20% fuel or an empty magazine the helicopter returns for service. At an available completed pad,
fuel replenishes at 20/s and ammunition at 10/s. Ground landing provides no supplies.
Empty fuel forces a descent; an unsafe emergency touchdown destroys the aircraft. It cannot take off
again without fuel. Supply exhaustion does not provide unlimited hovering.

## Model pivots and future transport

- Optional `pivot:rotor_main`: rotates around local Y.
- Optional `pivot:rotor_rear`: rotates around local X (`RearRotorAxis` can be configured).
- `pivot:turret`: uses the existing turret aiming parameter.
- Optional `pivot:muzzle`: exact muzzle-flash origin; otherwise the turret position is used.
- Optional helipad `pivot:landing`: exact landing point. Without it, `LandingLocalPosition`
  defaults to `(2.4, 0.1, -0.8)`, the center/top of the current model's landing slab.

The current helicopter model has a main rotor and turret pivot, but no named rear-rotor pivot.
Missing pivots are harmless. Rotor animation parameters are per instance.
`Helicopter(..., passengerCapacity: 4, meshName: "...")` enables the existing passenger component
for a future transport variant. Boarding and unloading are allowed only while landed; the initial
combat helicopter has no passenger slots. Helipad production and a separate transport unit type
are not added by this change.

Verification: `dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj`.
Headless checks cover the real model importer, flight, supplies, reservations, emergency landings,
ownership, shot consumption/cooldown and serialization. In-game visual and live multiplayer QA remain manual.
