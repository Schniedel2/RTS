float4x4 World;
float4x4 View;
float4x4 Projection;
float4x4 LightView;
float4x4 LightProjection;
float3 LightDirection;
texture ShadowTexture;
int DebugMode; // 1 == ignore shadowmap, 2 == full lighting

sampler ShadowSampler = sampler_state
{
    Texture = <ShadowTexture>;

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
};

VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output;

    float4 worldPosition = mul(input.Position, World);

    output.Position = mul(worldPosition, View);
    output.Position = mul(output.Position, Projection);
    output.Normal = normalize(mul(input.Normal, (float3x3)World));
    output.Color = input.Color;

    output.LightPosition = mul(worldPosition, LightView);
    output.LightPosition = mul(output.LightPosition, LightProjection);

    return output;
}

float4 PixelShaderFunction(VertexShaderOutput input) : COLOR0
{
    float3 shadowPosition =
        input.LightPosition.xyz / input.LightPosition.w;

    float2 shadowUV = shadowPosition.xy * 0.5 + 0.5;
    shadowUV.y = 1.0 - shadowUV.y;

    float shadowDepth = tex2D(ShadowSampler, shadowUV).r;
    float shadow = 1.0;

    if (shadowPosition.z - 0.005 > shadowDepth)
        shadow = 0.0;

    if (DebugMode == 1)
        shadow = 1.0;

    float diffuse = max(
        0.0,
        dot(normalize(input.Normal), normalize(-LightDirection)));
    float lighting = 0.15 + diffuse * shadow;

    if (DebugMode == 2)
        lighting = 1.0;
        
    return float4(input.Color.rgb * lighting, input.Color.a);
}

technique UnitTechnique
{
    pass Pass1
    {
        VertexShader = compile vs_3_0 VertexShaderFunction();
        PixelShader = compile ps_3_0 PixelShaderFunction();

        ZEnable = TRUE;
        ZWriteEnable = TRUE;
        ZFunc = LessEqual;
    }
}