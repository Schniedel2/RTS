using Microsoft.Xna.Framework;
using System;

namespace RTS;

/// <summary>
/// Driving characteristics for a forward-moving ground vehicle. Angles and turn
/// rates are expressed in degrees so unit constructors remain readable.
/// Infantry and aircraft do not use this profile.
/// </summary>
public sealed record GroundSteeringProfile(
    float MovingTurnDegreesPerSecond,
    float StationaryTurnDegreesPerSecond,
    float TurnInPlaceThresholdDegrees,
    float MinimumCurveSpeedFactor,
    bool AllowReverse,
    float ReverseSpeed,
    float ReverseStartAngleDegrees,
    float ReverseAlignmentToleranceDegrees,
    float ReverseMaximumDistance)
{
    public float MovingTurnRadiansPerSecond =>
        MathHelper.ToRadians(Math.Clamp(MovingTurnDegreesPerSecond, 0.0f, 1080.0f));
    public float StationaryTurnRadiansPerSecond =>
        MathHelper.ToRadians(Math.Clamp(StationaryTurnDegreesPerSecond, 0.0f, 1080.0f));
    public float TurnInPlaceAlignment => MathF.Cos(MathHelper.ToRadians(
        Math.Clamp(TurnInPlaceThresholdDegrees, 0.0f, 180.0f)));
    public float ReverseStartAlignment => MathF.Cos(MathHelper.ToRadians(
        Math.Clamp(ReverseStartAngleDegrees, 0.0f, 180.0f)));
    public float ReverseExitAlignment => MathF.Cos(MathHelper.ToRadians(
        Math.Clamp(ReverseAlignmentToleranceDegrees, 0.0f, 180.0f)));
    public float ClampedMinimumCurveSpeedFactor =>
        Math.Clamp(MinimumCurveSpeedFactor, 0.0f, 1.0f);
    public float ClampedReverseSpeed => Math.Max(0.0f, ReverseSpeed);
    public float ClampedReverseMaximumDistance => Math.Max(0.0f, ReverseMaximumDistance);

    /// <summary>Returns a continuous 0..1 speed multiplier for the current heading error.</summary>
    public float GetCurveSpeedFactor(float alignment)
    {
        float angleDegrees = MathHelper.ToDegrees(MathF.Acos(Math.Clamp(alignment, -1.0f, 1.0f)));
        float movingRange = Math.Max(0.001f, Math.Clamp(TurnInPlaceThresholdDegrees, 0.0f, 180.0f));
        float curveAmount = Math.Clamp(angleDegrees / movingRange, 0.0f, 1.0f);
        // Smoothstep avoids a visible speed kink as the heading approaches straight ahead.
        curveAmount = curveAmount * curveAmount * (3.0f - 2.0f * curveAmount);
        return MathHelper.Lerp(1.0f, ClampedMinimumCurveSpeedFactor, curveAmount);
    }
}
