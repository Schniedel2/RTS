# Terrain heightmap

Place an 8-bit grayscale PNG named `terrain-height.png` in this folder.

The current terrain is `512 x 512`, so the image must have exactly that size.
Black pixels produce the lowest terrain and white pixels produce the highest
terrain. The height range is controlled by the terrain `HeightScale` value.

The game loads the PNG directly from the output `Content/Heightmaps` folder,
so no MGCB entry is required. The game keeps using its procedural heightmap
until the PNG is available and has the expected dimensions.