using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public sealed class RenderStateStack(GraphicsDevice graphicsDevice)
{
    private readonly record struct State(
        BlendState Blend,
        DepthStencilState DepthStencil,
        RasterizerState Rasterizer);

    private readonly Stack<State> _states = [];

    public void PushState()
    {
        _states.Push(new State(
            graphicsDevice.BlendState,
            graphicsDevice.DepthStencilState,
            graphicsDevice.RasterizerState));
    }

    public void PopState()
    {
        if (!_states.TryPop(out State state))
            throw new InvalidOperationException("No render state has been pushed.");

        graphicsDevice.BlendState = state.Blend;
        graphicsDevice.DepthStencilState = state.DepthStencil;
        graphicsDevice.RasterizerState = state.Rasterizer;
    }
}