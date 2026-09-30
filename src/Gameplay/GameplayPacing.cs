using System;

namespace RTS;

/// <summary>Central gameplay timing rules that differ between setup/testing and a running match.</summary>
public static class GameplayPacing
{
    public const float PreMatchWorkMultiplier = 10.0f;

    public static float ScaleWork(float amount, bool isMatchStarted)
    {
        if (!float.IsFinite(amount) || amount <= 0.0f)
            return 0.0f;
        return amount * (isMatchStarted ? 1.0f : PreMatchWorkMultiplier);
    }
}
