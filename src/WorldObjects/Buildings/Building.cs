using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Building : MobileUnit
{
    public Building(
        Vector3 position,
        Guid unitId
        ) : base(
            position,
            length: 8,
            width: 8,
            height: 3.0f,
            unitId)
    {
    }

    public override void Update(GameTime gameTime)
    {
        UpdateTargetAngle(gameTime);
    }
}
