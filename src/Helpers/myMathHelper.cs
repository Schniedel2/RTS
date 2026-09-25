#pragma warning disable CA1416

using System;

namespace RTS;

public static class myMathHelper
{
    public static float WrapAngleDegrees(float angle)
    {
        if (angle > -180f && angle <= 180f)
        {
            return angle;
        }

        angle %= 360f;
        if (angle <= -180f)
        {
            return angle + 360f;
        }

        if (angle > 180f)
        {
            return angle - 360f;
        }

        return angle;
    }
}