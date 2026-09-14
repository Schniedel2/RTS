float4x4 World;
float4x4 View;
float4x4 Projection;
float4x4 LightView;
float4x4 LightProjection;
float2 ShadowTexelSize;

float3 LightDirection;

texture ShadowTexture;
texture UnitTexture;
texture PlayerSkinTexture;

// MaterialMask:
// R = PlayerColorMask
// G = MetallicMask
// B = EmissiveMask
// A = Reserved
texture MaterialMaskTexture;

// Per-unit offsets into the respective texture atlases. They are intentionally
// independent: a mesh variant may use a different material-mask layout.
float2 UnitTextureUVOffset;
float2 MaterialMaskUVOffset;
float2 MaterialMaskSourceUVOffset;
float2 MaterialMaskUVScale;
float MaterialMaskUseTexture;
float MaterialMaskDefaultPlayerMask;

float2 PlayerSkinUVOffset;
float2 PlayerSkinUVScale;
float  PlayerSkinUVRepeat;
float  PlayerSkinStrength;

// 0 == untextured (vertex color only), 1 == fully textured
float TextureStrength;
// 1 == render textured sprites without directional light or shadow sampling.
float Unlit;

int DebugMode; // 1 == ignore shadowmap, 2 == full lighting

//  note: texture-Atlas used for UnitTexture and MaterialMaskTexture
//  size: 32 x 32 pixels, order: Left (0,0), Top (32,0), Right (64,0), Front (0, 32), Bottom (32, 32), Back (64, 32)

sampler ShadowSampler = sampler_state
{
    Texture = <ShadowTexture>;

    MinFilter = Point;
    MagFilter = Point;
    MipFilter = None;

    AddressU = Clamp;
    AddressV = Clamp;
};


sampler UnitTextureSampler = sampler_state
{
    Texture = <UnitTexture>;

    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;

    AddressU = Wrap;
    AddressV = Wrap;
};

sampler PlayerSkinSampler = sampler_state
{
    Texture = <PlayerSkinTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};


sampler MaterialMaskSampler = sampler_state
{
    Texture = <MaterialMaskTexture>;

    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;

    AddressU = Wrap;
    AddressV = Wrap;
};


struct VertexShaderInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};


struct VertexShaderOutput
{
    float4 Position      : SV_POSITION;
    float3 Normal        : TEXCOORD0;
    float4 Color         : COLOR0;
    float4 LightPosition : TEXCOORD1;
    float2 TexCoord      : TEXCOORD2;
};

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


VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output;

    float4 worldPosition =
        mul(input.Position, World);

    output.Position =
        mul(worldPosition, View);

    output.Position =
        mul(output.Position, Projection);

    output.Normal =
        normalize(
            mul(input.Normal, (float3x3)World));

    output.Color =
        input.Color;

    output.LightPosition =
        mul(worldPosition, LightView);

    output.LightPosition =
        mul(output.LightPosition, LightProjection);

    output.TexCoord =
        input.TexCoord;

    return output;
}


float4 PixelShaderFunction(VertexShaderOutput input) : COLOR0
{
    // -------------------------------------------------
    // Shadow
    // -------------------------------------------------

    float3 shadowPosition =
        input.LightPosition.xyz /
        input.LightPosition.w;

    float2 shadowUV =
        shadowPosition.xy * 0.5 + 0.5;

    shadowUV.y =
        1.0 - shadowUV.y;

    float shadow = CalculateShadow(shadowUV, shadowPosition.z, 0.001);

    if (DebugMode == 1)
        shadow = 1.0;


    // -------------------------------------------------
    // Unit texture
    // -------------------------------------------------

    float2 unitTextureUv = input.TexCoord + UnitTextureUVOffset;
    float2 materialMaskUv = MaterialMaskUVOffset +
        (input.TexCoord + UnitTextureUVOffset - MaterialMaskSourceUVOffset) * MaterialMaskUVScale;
    float2 localSkinUv = frac(input.TexCoord * PlayerSkinUVRepeat);
    float2 playerSkinUv = PlayerSkinUVOffset + localSkinUv * PlayerSkinUVScale;

    float4 texColor =
        lerp(
            float4(1.0, 1.0, 1.0, 1.0),
            tex2D(UnitTextureSampler, unitTextureUv),
            TextureStrength);


    // -------------------------------------------------
    // Material mask
    // -------------------------------------------------

    float4 materialMask = lerp(
        float4(MaterialMaskDefaultPlayerMask, 0.0, 0.0, 1.0),
        tex2D(MaterialMaskSampler, materialMaskUv),
        MaterialMaskUseTexture) * TextureStrength;
    float4 playerSkin = tex2D(PlayerSkinSampler, playerSkinUv);

    float playerMask =
        materialMask.r;

    float metallic =
        materialMask.g;

    float emissive =
        materialMask.b;


    // -------------------------------------------------
    // Base color
    // -------------------------------------------------

    float4 baseColor =
        texColor * input.Color;


    // -------------------------------------------------
    // Player color
    // -------------------------------------------------

    float playerBlend = saturate(playerMask * PlayerSkinStrength);
    baseColor.rgb = lerp(baseColor.rgb, playerSkin.rgb, playerBlend);


    // -------------------------------------------------
    // Lighting
    // -------------------------------------------------

    float diffuse =
        max(
            0.0,
            dot(
                normalize(input.Normal),
                normalize(-LightDirection)));

    float lighting =
        0.15 + diffuse * shadow;

    lighting = lerp(lighting, 1.0, saturate(Unlit));

    if (DebugMode == 2)
        lighting = 1.0;


    // -------------------------------------------------
    // Emissive
    // -------------------------------------------------

    float3 litColor =
        baseColor.rgb * lighting;

    float3 finalColor =
        lerp(
            litColor,
            baseColor.rgb,
            emissive);


    return float4(
        finalColor,
        baseColor.a);
}


technique UnitTechnique
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
