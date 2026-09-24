float4x4 World;
float4x4 View;
float4x4 Projection;
float4x4 LightView;
float4x4 LightProjection;
float2 ShadowTexelSize;
float3 LightDirection;
texture ShadowTexture;
texture TerrainTilesTexture;
texture TileMapTexture;
texture FogTexture;
float MapWidth;
float MapHeight;
float FogWidth;
float FogHeight;
float HideUnexploredTerrain;
int DebugMode;

float Noise(float2 p)
{
    float n = dot(p, float2(12.9898, 78.233));

    return frac(
        sin(n) * 43758.5453);
}

sampler TerrainTilesSampler = sampler_state
{
    Texture = <TerrainTilesTexture>;

    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;

    AddressU = Wrap;
    AddressV = Wrap;
};

sampler TileMapSampler = sampler_state
{
    Texture = <TileMapTexture>;

    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = None;

    AddressU = Clamp;
    AddressV = Clamp;
};

sampler ShadowSampler = sampler_state
{
    Texture = <ShadowTexture>;

    MinFilter = Point;
    MagFilter = Point;
    MipFilter = None;

    AddressU = Clamp;
    AddressV = Clamp;
};

sampler FogSampler = sampler_state
{
    Texture = <FogTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = None;
    AddressU = Clamp;
    AddressV = Clamp;
};


struct VertexShaderInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float4 Color    : COLOR0;
};


struct VertexShaderOutput
{
    float4 Position      : SV_POSITION;
    float3 Normal        : TEXCOORD0;
    float4 Color         : COLOR0;
    float4 LightPosition : TEXCOORD1;
    float3 WorldPosition : TEXCOORD2;
};

float4 SampleMaterial(float tileId, float2 uv)
{
    float tileWidth = 1.0 / 4.0;
    float tileHeight = 1.0 / 4.0;
    int tileX = int(tileId) % 4;
    int tileY = int(tileId) / 4;

    float2 terrainUV;

    terrainUV.x =
        tileX * tileWidth +
        uv.x * tileWidth;

    terrainUV.y =
        tileY * tileHeight +
        uv.y * tileHeight;

    return tex2D(
        TerrainTilesSampler,
        terrainUV);
}

float CalculateShadow(float2 shadowUV, float currentDepth, float bias)
{
    float litSamples = 0.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float sampledDepth = tex2D(ShadowSampler, shadowUV + float2(x, y) * ShadowTexelSize).r;
            litSamples += currentDepth - bias <= sampledDepth ? 1.0 : 0.0;
        }
    }
    return litSamples / 9.0;
}

VertexShaderOutput VertexShaderFunction(
    VertexShaderInput input)
{
    VertexShaderOutput output;

    float4 worldPosition =
        mul(input.Position, World);

    output.WorldPosition =
        worldPosition.xyz;

    // Normale Kamera
    output.Position =
        mul(worldPosition, View);

    output.Position =
        mul(output.Position, Projection);

    // Normale
    output.Normal =
        mul(input.Normal, (float3x3)World);

    output.Color =
        input.Color;

    // Position aus Sicht der Sonne
    output.LightPosition =
        mul(worldPosition, LightView);

    output.LightPosition =
        mul(output.LightPosition, LightProjection);

    return output;
}

float4 PixelShaderFunction(
    VertexShaderOutput input) : COLOR0
{
    // ============================================================
    // ShadowMap Position
    // ============================================================

    float3 shadowPosition =
        input.LightPosition.xyz /
        input.LightPosition.w;

    float2 shadowUV =
        shadowPosition.xy * 0.5 + 0.5;

    shadowUV.y =
        1.0 - shadowUV.y;


    // ============================================================
    // Tilemap
    // ============================================================

    float2 tilePosition =
        input.WorldPosition.xz;

    float2 tileCoordinate =
        floor(tilePosition);

    float2 localUV =
        frac(tilePosition);

    float2 tileMapSize =
        float2(MapWidth, MapHeight);

    float2 tileMapUV =
        (tileCoordinate + 0.5) /
        tileMapSize;


    // ============================================================
    // Aktuelles Tile
    // ============================================================

    float tileId =
        tex2D(
            TileMapSampler,
            tileMapUV).r;

    tileId *= 255.0;


    // ============================================================
    // Nachbar-Tiles
    // ============================================================

    float2 rightTileCoordinate =
        tileCoordinate + float2(1.0, 0.0);

    float2 bottomTileCoordinate =
        tileCoordinate + float2(0.0, 1.0);

    float2 cornerTileCoordinate =
        tileCoordinate + float2(1.0, 1.0);


    float rightTileId =
        tex2D(
            TileMapSampler,
            (rightTileCoordinate + 0.5) /
            tileMapSize).r;

    float bottomTileId =
        tex2D(
            TileMapSampler,
            (bottomTileCoordinate + 0.5) /
            tileMapSize).r;

    float cornerTileId =
        tex2D(
            TileMapSampler,
            (cornerTileCoordinate + 0.5) /
            tileMapSize).r;


    rightTileId *= 255.0;
    bottomTileId *= 255.0;
    cornerTileId *= 255.0;


    // ============================================================
    // Texturkoordinaten innerhalb des Materials
    // ============================================================

    float2 textureUV =
        input.WorldPosition.xz / 32.0; // wrap aound fter 32 terrain-tiles

    textureUV =
        frac(textureUV);


    // ============================================================
    // Materialien
    // ============================================================

    float4 terrainColor =
        SampleMaterial(
            tileId,
            textureUV);

    float4 rightColor =
        SampleMaterial(
            rightTileId,
            textureUV);

    float4 bottomColor =
        SampleMaterial(
            bottomTileId,
            textureUV);

    float4 cornerColor =
        SampleMaterial(
            cornerTileId,
            textureUV);


    // ============================================================
    // Übergangsbreite
    // ============================================================

    float transitionWidth = 1.0;

    // ============================================================
    // Übergang nach RECHTS
    // ============================================================

    float rightBlend = 0.0;

    if (rightTileId != tileId)
    {
        float noise =
            Noise(
                input.WorldPosition.xz *
                0.35);

        float edge =
            1.0 -
                transitionWidth;

        edge +=
            (noise - 0.5) * 0.15;

        rightBlend =
            smoothstep(
                edge,
                1.0,
                localUV.x);
    }


    // ============================================================
    // Übergang nach UNTEN
    // ============================================================

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
                transitionWidth;

        edge +=
            (noise - 0.5) * 0.15;

        bottomBlend =
            smoothstep(
                edge,
                1.0,
                localUV.y);
    }


    // ============================================================
    // ECKEN-GEWICHT
    // ============================================================

    // Nur dort aktiv, wo beide Übergänge zusammentreffen.
    float cornerBlend =
        rightBlend *
        bottomBlend;


    // ============================================================
    // Farben mischen
    // ============================================================

    float3 finalColor =
        terrainColor.rgb;


    // ------------------------------------------------------------
    // Normale Übergänge
    // ------------------------------------------------------------

    if (rightBlend > 0.0)
    {
        finalColor =
            lerp(
                finalColor,
                rightColor.rgb,
                rightBlend);
    }

    if (bottomBlend > 0.0)
    {
        finalColor =
            lerp(
                finalColor,
                bottomColor.rgb,
                bottomBlend);
    }


    // ============================================================
    // Diagonales Eck-Tile
    // ============================================================

    if (cornerBlend > 0.0)
    {
        finalColor =
            lerp(
                finalColor,
                cornerColor.rgb,
                cornerBlend);
    }

    // ============================================================
    // ShadowMap
    // ============================================================

    float shadow = CalculateShadow(shadowUV, shadowPosition.z, 0.001);


    // ============================================================
    // Beleuchtung
    // ============================================================

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

    finalColor *=
        lighting;

    float2 fogUV = (input.WorldPosition.xz + 0.5) / float2(FogWidth, FogHeight);
    float visibility = tex2D(FogSampler, fogUV).r;
    if (HideUnexploredTerrain > 0.5 && visibility < 0.2)
        return float4(0.0, 0.0, 0.0, 1.0);
    finalColor *= lerp(0.035, 1.0, visibility);


    // ============================================================
    // Ausgabe
    // ============================================================

    return float4(
        finalColor,
        1.0);
}

technique TerrainTechnique
{
    pass Pass1
    {
        VertexShader =
            compile vs_3_0 VertexShaderFunction();

        PixelShader =
            compile ps_3_0 PixelShaderFunction();

        ZEnable = TRUE;
        ZWriteEnable = TRUE;
        ZFunc = LessEqual;
    }
}
