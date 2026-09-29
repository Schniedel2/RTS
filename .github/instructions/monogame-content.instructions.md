---
name: "RTS MonoGame Content"
description: "Use when adding or changing MonoGame pipeline assets, content loading, runtime .bbmodel meshes, or mesh registration."
applyTo:
  - "Content/**"
  - "src/Handlers/MeshHandler.cs"
  - "src/Helpers/BBModelLoader.cs"
  - "RTS.csproj"
---

# MonoGame Content Guidelines

- Effects, textures, and fonts compiled by MonoGame belong in `Content/Content.mgcb`; load them with their extensionless content name through `Content.Load<T>()`.
- `.bbmodel` files are loaded at runtime, not through MGCB. Follow the existing `BBModelLoader` and `MeshHandler` registration path, and ensure the registered key exactly matches the name passed to `SetMesh`.
- Keep runtime model files within the existing copied `Content/models` tree so they are available beside the built game. Check [RTS.csproj](../../RTS.csproj) before changing asset-copy rules.
- Build with `dotnet build RTS.csproj` after content-pipeline or asset-copy changes; this exercises the MGCB build as well as C# compilation.