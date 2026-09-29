# RTS Project Guidelines

## Code and Ownership
- The game targets .NET 9 and MonoGame DesktopGL. Nullable is enabled; the main project does not enable implicit usings, so follow its explicit-using style.
- `Game1` owns the MonoGame lifecycle and graphics resources; `RTSGame` coordinates gameplay systems. Keep world and gameplay mutations on the game thread.
- In multiplayer, clients send requests and the host validates and applies gameplay changes before broadcasting ordered commands. Network background tasks only handle transport and enqueue messages. Add persistent state needed by late joiners to the session snapshot.
- Keep static product data in `GameplayCatalog`; mutable runtime state belongs to instances or their owning services.

## Content
- Declare MonoGame pipeline assets in [Content/Content.mgcb](Content/Content.mgcb) and load them by their extensionless content name.
- Runtime `.bbmodel` meshes use `MeshHandler`; register mesh names before units call `SetMesh`.

## Build and Checks
- Build with `dotnet build RTS.csproj`.
- Run the headless grid-navigation checks with `dotnet run --project tests/GridNavigationChecks/GridNavigationChecks.csproj`. This is an executable check harness, not a `dotnet test` suite; the main project excludes `tests/**/*.cs`.
- Run the game with `dotnet run --project RTS.csproj` only when a graphics/window environment is available.
- For gameplay changes, run the focused checks that cover the affected behavior in addition to building.

## Design References
- [Networking authority and lifecycle](AI/Netzwerk-Architektur.md)
- [Movement and network synchronization](AI/Bewegung-und-Netzwerksynchronisierung.md)
- [Gameplay catalog and AI metadata](AI/Gameplay-Katalog-und-KI-Metadaten.md)
- [Player, team, and army ownership](AI/Player-Team-Army-Architektur.md)
- [Fog of war and minimap](AI/Fog-of-War-und-Minimap-Architektur.md)
- [Grid navigation](docs/GridNavigation.md), [building placement](docs/BuildingPlacement.md), and [earthwork](docs/Earthwork.md)