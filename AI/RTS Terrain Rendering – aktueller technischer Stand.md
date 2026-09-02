# RTS Terrain Rendering – aktueller technischer Stand

Ich entwickle mit C# und MonoGame ein RTS-Spiel. Das Terrain besteht aus einer `GameMap`, einer `TerrainTileMap` und einem Heightmap-basierten `Terrain`.

## GameMap

Aktuelle Konstruktion:

```csharp
_gameMap = new GameMap(
    graphicsDevice: GraphicsDevice,
    effect: _terrainEffect,
    width: 128,
    height: 128,
    tileSize: 16,
    cellsPerTile: 4);
```

`GameMap` enthält:

- `TerrainTileMap`
- `Terrain`
- `TileSize`
- `TerrainCellsPerTile`

Die Tilemap ist also das Gameplay-Raster, während das Terrain eine feinere Heightmap-Geometrie besitzt.

Beispiel:

```text
128 × 128 Gameplay-Tiles
4 × 4 Heightmap-Zellen pro Tile

→ 512 × 512 Terrain-Raster
```

Bei `128 × 128` Tiles und `4 × 4` Heightmap-Zellen entstehen ungefähr 524.000 Dreiecke.

Die Karte läuft aktuell mit ungefähr 60 FPS. Debug-Grids sind deaktiviert, weil sie die Performance deutlich verschlechtern.

---

# Terrain-Geometrie

Das Terrain wird als Mesh gerendert und besitzt Heightmap-Werte.

Die Tilemap und das Terrain hängen räumlich zusammen:

```text
GameMap
 ├── TerrainTileMap
 │    └── 128 × 128 Gameplay-Tiles
 │
 └── Terrain
      └── feinere Heightmap-Geometrie
```

Das Terrain besitzt eine `GetHeight(x, y)`-Funktion bzw. entsprechende Heightmap-Werte.

---

# Tile-System

Wir verwenden Variante:

**Tilesheet + Tilemap-Texture**

Die Tilemap wird als Texture auf die GPU übertragen.

Die Tilemap speichert pro Pixel eine Tile-ID:

```text
R = Tile-ID / 255
```

Im Shader:

```hlsl
float tileId =
    tex2D(
        TileMapSampler,
        tileMapUV).r;

tileId *= 255.0;
```

Das Tilesheet enthält mehrere Materialien nebeneinander.

Aktuell:

```text
Tile 0 = Grass
Tile 1 = Dirt
Tile 2 = Rock
Tile 3 = Sand
```

Die Tile-Größe im Tilesheet ist 32×32 Pixel.

Das Tilesheet soll später möglichst große, wiederholbare Texturen enthalten. Es ist momentan noch nicht perfekt seamless/wrap-fähig.

---

# Terrain.fx

Wichtige Parameter:

```hlsl
float4x4 World;
float4x4 View;
float4x4 Projection;

float4x4 LightView;
float4x4 LightProjection;

float3 LightDirection;

texture ShadowTexture;
texture TerrainTilesTexture;
texture TileMapTexture;

float TileSize;
float MapWidth;
float MapHeight;
```

Tilemap-Sampler:

```hlsl
sampler TileMapSampler = sampler_state
{
    Texture = <TileMapTexture>;

    MinFilter = Point;
    MagFilter = Point;
    MipFilter = None;

    AddressU = Clamp;
    AddressV = Clamp;
};
```

Tilesheet-Sampler:

```hlsl
sampler TerrainTilesSampler = sampler_state
{
    Texture = <TerrainTilesTexture>;

    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;

    AddressU = Wrap;
    AddressV = Wrap;
};
```

Shadowmap:

```hlsl
sampler ShadowSampler = sampler_state
{
    Texture = <ShadowTexture>;

    MinFilter = Point;
    MagFilter = Point;
    MipFilter = None;

    AddressU = Clamp;
    AddressV = Clamp;
};
```

---

# Vertex Shader

Der Vertex Shader erzeugt sowohl die normale Kamera-Position als auch die Position aus Sicht der Sonne:

```hlsl
VertexShaderOutput VertexShaderFunction(
    VertexShaderInput input)
{
    VertexShaderOutput output;

    float4 worldPosition =
        mul(input.Position, World);

    output.WorldPosition =
        worldPosition.xyz;

    output.Position =
        mul(worldPosition, View);

    output.Position =
        mul(output.Position, Projection);

    output.Normal =
        mul(input.Normal, (float3x3)World);

    output.Color =
        input.Color;

    output.LightPosition =
        mul(worldPosition, LightView);

    output.LightPosition =
        mul(output.LightPosition, LightProjection);

    return output;
}
```

`VertexShaderOutput`:

```hlsl
struct VertexShaderOutput
{
    float4 Position      : SV_POSITION;
    float3 Normal        : TEXCOORD0;
    float4 Color         : COLOR0;
    float4 LightPosition : TEXCOORD1;
    float3 WorldPosition : TEXCOORD2;
};
```

---

# Shadow Mapping

Eine funktionierende Shadowmap existiert bereits.

Es gab vorher Probleme mit der Ausrichtung der Light-Camera und einem Flip der Y-Achse. Der aktuelle Stand funktioniert visuell.

Shadowmap-UV:

```hlsl
float3 shadowPosition =
    input.LightPosition.xyz /
    input.LightPosition.w;

float2 shadowUV =
    shadowPosition.xy * 0.5 + 0.5;

shadowUV.y =
    1.0 - shadowUV.y;
```

Shadow-Test:

```hlsl
float shadowDepth =
    tex2D(
        ShadowSampler,
        shadowUV).r;

float currentDepth =
    shadowPosition.z;

float bias =
    0.005;

float shadow =
    1.0;

if (currentDepth - bias > shadowDepth)
{
    shadow = 0.0;
}
```

Danach normale diffuse Beleuchtung:

```hlsl
float3 normal =
    normalize(input.Normal);

float3 lightDirection =
    normalize(-LightDirection);

float diffuse =
    max(
        0.0,
        dot(
            normal,
            lightDirection));

float lighting =
    0.15 +
    diffuse * shadow;

finalColor *= lighting;
```

Die Shadowmap ist aktuell nicht zwingend der Performance-Flaschenhals. Bei 128×128 Tiles mit 4×4 Heightmap-Zellen sind ungefähr 60 FPS möglich.

---

# Aktuelles Problem: Tile-Transitions

Wir möchten unterschiedliche Bodenmaterialien ohne harte Tile-Grenzen mischen.

Beispiel:

```text
Grass | Dirt
------+-----
Grass | Dirt
```

Der Übergang soll organisch aussehen.

Wir verwenden momentan eine Noise-Funktion:

```hlsl
float Noise(float2 p)
{
    float n =
        dot(
            p,
            float2(12.9898, 78.233));

    return frac(
        sin(n) * 43758.5453);
}
```

Die horizontale Transition funktioniert bereits gut.

Die Übergangsbreite wird momentan relativ zur Tilegröße definiert:

```hlsl
float transitionWidth =
    TileSize * 0.25;
```

---

# Wichtige Designentscheidung für Transitions

Wir wollen NICHT mehr alle vier Nachbarn betrachten.

Stattdessen besitzt jedes Tile nur die Verantwortung für:

```text
aktuelles Tile
       │
       ├── rechts
       └── unten
```

Also:

```text
A | B | C
--+---+--
D | E | F
--+---+--
G | H | I
```

A erzeugt:

```text
A → B
A → D
```

B erzeugt:

```text
B → C
B → E
```

usw.

Dadurch wird jeder Übergang genau einmal erzeugt.

Das spart gegenüber der alten 5-Tile-Version Texture-Samples und macht die Logik eindeutiger.

---

# Corner-Problem

Dabei entsteht ein Sonderfall.

Beispiel:

```text
0 | 1
--+--
1 | 2
```

Das aktuelle Tile `0` besitzt:

```text
rechts = 1
unten  = 1
```

Das diagonal rechts-unten liegende Tile ist aber:

```text
2
```

Wenn wir nur 3 Tiles betrachten, wissen wir davon nichts.

Daher wurde entschieden, zusätzlich das diagonale Tile zu berücksichtigen:

```text
aktuelles
rechts
unten
rechts-unten
```

Also insgesamt **4 Tilemap-Samples**.

Das diagonale Tile soll aber ausschließlich im Eckbereich verwendet werden:

```hlsl
float cornerBlend =
    rightBlend *
    bottomBlend;
```

Dadurch beeinflusst das diagonale Tile nur den Bereich, in dem beide Übergänge gleichzeitig aktiv sind.

---

# Aktueller Transition-Ansatz

Rechts:

```hlsl
float rightBlend = 0.0;

if (rightTileId != tileId)
{
    float noise =
        Noise(
            input.WorldPosition.xz *
            0.35);

    float edge =
        1.0 -
        transitionWidth / TileSize;

    edge +=
        (noise - 0.5) * 0.15;

    rightBlend =
        smoothstep(
            edge,
            1.0,
            localUV.x);
}
```

Unten analog:

```hlsl
float bottomBlend = 0.0;

if (bottomTileId != tileId)
{
    float noise =
        Noise(
            input.WorldPosition.xz *
            0.35 +
            31.0);

    float edge =
        1.0 -
        transitionWidth / TileSize;

    edge +=
        (noise - 0.5) * 0.15;

    bottomBlend =
        smoothstep(
            edge,
            1.0,
            localUV.y);
}
```

Wichtig:

Am rechten bzw. unteren Rand soll der Blend-Faktor garantiert 1.0 sein.

Also:

```text
localUV.x = 1 → rightBlend = 1
localUV.y = 1 → bottomBlend = 1
```

---

# Material-Sampling

```hlsl
float4 SampleMaterial(float tileId, float2 uv)
{
    float tileWidth = 1.0 / 4.0;

    float2 terrainUV;

    terrainUV.x =
        tileId * tileWidth +
        uv.x * tileWidth;

    terrainUV.y =
        uv.y;

    return tex2D(
        TerrainTilesSampler,
        terrainUV);
}
```

Die Texturkoordinaten werden momentan so erzeugt:

```hlsl
float2 textureUV =
    input.WorldPosition.xz / 32.0;

textureUV =
    frac(textureUV);
```

---

# Performance

Aktuell sind ungefähr 60 FPS erreichbar.

Der Pixelshader ist allerdings relativ aufwendig.

Die alte Variante hatte:

```text
1 aktuelles Tile
+ 4 Nachbarn
= 5 Tilemap-Samples

5 Tilesheet-Samples
Noise-Berechnungen
Shadowmap-Sample
Lighting
```

Die neue Variante soll reduzieren auf:

```text
1 aktuelles Tile
+ rechts
+ unten
+ diagonal
= 4 Tilemap-Samples

4 Tilesheet-Samples
Noise nur für rechts/unten
Shadowmap
Lighting
```

Mögliche zukünftige Optimierungen:

1. Transition-Berechnung nur nahe Tile-Grenzen durchführen.
2. Noise durch eine kleine Noise-Texture ersetzen.
3. Shadowmap nur neu berechnen, wenn sich Sonne/Terrain/Objekte ändern.
4. Terrain später in Chunks aufteilen.
5. Eventuell LOD für entfernte Terrainbereiche.

Aktuell sollen diese Optimierungen aber noch nicht umgesetzt werden, solange die Performance bei ca. 60 FPS liegt.

---

# Aktueller Entwicklungsfokus

Bitte zunächst die **Tile-Transition-Logik und insbesondere die Eckfälle** analysieren.

Die grundsätzliche Idee soll erhalten bleiben:

> Jedes Tile ist ausschließlich für Übergänge nach rechts und unten verantwortlich.

Zusätzlich darf das diagonal rechts-unten liegende Tile nur zur korrekten Behandlung von Ecken verwendet werden.

Bitte nicht sofort eine komplett andere Renderingarchitektur vorschlagen. Erst prüfen, ob die aktuelle Lösung mathematisch/visuell sauber verbessert werden kann.