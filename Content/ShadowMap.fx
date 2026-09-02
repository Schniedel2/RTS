float4x4 World;
float4x4 View;
float4x4 Projection;
int DebugMode;

struct VertexShaderInput
{
    float4 Position : POSITION0;
};


struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float Depth     : TEXCOORD0;
};


VertexShaderOutput VertexShaderFunction(
    VertexShaderInput input)
{
    VertexShaderOutput output;

    float4 position =
        mul(input.Position, World);

    position =
        mul(position, View);

    position =
        mul(position, Projection);

    output.Position =
        position;

    output.Depth =
        position.z / position.w;

    return output;
}


float4 PixelShaderFunction(
    VertexShaderOutput input) : COLOR0
{
    float depth =
        input.Depth;

    return float4(
        depth,
        depth,
        depth,
        1.0);
}


technique ShadowTechnique
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