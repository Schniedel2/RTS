# Shared texture UV mapping

```csharp
mesh.ApplySharedTextureMapping();       // 32 pixels per world unit
mesh.ApplySharedTextureMapping(64.0f);  // 64 pixels per world unit
```

Only faces imported from a BBModel texture named `shared:...` are remapped.
Ordinary textures retain their authored UVs, even when both kinds occur in the
same Blockbench mesh or cube. The importer groups faces by material while keeping
their hierarchy and pivots. Different texture atlas indices are supported.

MeshHandler applies this mapping to loaded meshes after configuring their local
scales. The algorithm uses object-space box projection, consistent across model
sizes. At 32 pixels/unit a 256-pixel-wide texture repeats every 8 world units.
It uses the dimensions of the registered texture, including external overrides.

UVs stay unwrapped at vertices; the pixel shader wraps within the texture's own
atlas region after interpolation. Negative coordinates and faces larger than
one texture tile work without sampling neighboring atlas entries. Material masks
follow the repeated visible texture.

Set permanent Mesh.LocalTransform before calling the method. Call it again if
that transform changes. The rest-pose group transforms are included; subsequent
unit/world scaling and animation stretch the mapped surface. Box projection uses
the dominant face-normal axis, as the existing cube mapping does; sloped faces
can therefore have projection distortion. Higher pixel density makes the bricks
smaller. Invalid non-positive or non-finite values throw ArgumentOutOfRangeException.
