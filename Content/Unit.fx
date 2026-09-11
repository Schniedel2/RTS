float4x4 World;
float4x4 View;
float4x4 Projection;
float4x4 LightView;
float4x4 LightProjection;

float3 LightDirection;

texture ShadowTexture;
texture UnitTexture;

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

float3 PlayerColor;
float  PlayerColorStrength;

// 0 == untextured (vertex color only), 1 == fully textured
float TextureStrength;

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

    float shadowDepth =
        tex2D(ShadowSampler, shadowUV).r;

    float shadow = 1.0;

    if (shadowPosition.z - 0.005 > shadowDepth)
        shadow = 0.0;

    if (DebugMode == 1)
        shadow = 1.0;


    // -------------------------------------------------
    // Unit texture
    // -------------------------------------------------

    float2 unitTextureUv = input.TexCoord + UnitTextureUVOffset;
    float2 materialMaskUv = input.TexCoord + MaterialMaskUVOffset;

    float4 texColor =
        lerp(
            float4(1.0, 1.0, 1.0, 1.0),
            tex2D(UnitTextureSampler, unitTextureUv),
            TextureStrength);


    // -------------------------------------------------
    // Material mask
    // -------------------------------------------------

    float4 materialMask =
        tex2D(MaterialMaskSampler, materialMaskUv) * TextureStrength;

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

    float playerBlend =
        saturate(
            playerMask * PlayerColorStrength);

    float3 playerColored =
        baseColor.rgb * PlayerColor * 2.0;

    baseColor.rgb =
        lerp(
            baseColor.rgb,
            playerColored,
            playerBlend);


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
