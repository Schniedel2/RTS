using Microsoft.Xna.Framework;
using System;

namespace RTS;

/// <summary>Shared, frame-rate-independent flight definition used by host and peers.</summary>
public sealed record ProjectileFlightProfile(
    float MotorDelay,
    float MotorBurnDuration,
    float MotorAcceleration,
    float Gravity,
    float MaximumLifetime,
    float ExplosionRadius,
    float ExhaustIgnitionDuration,
    float ExhaustFadeDuration)
{
    public static ProjectileFlightProfile For(ProjectileKind kind) => kind switch
    {
        ProjectileKind.Rocket => new(
            MotorDelay: 0.2f, // 0.08f,
            MotorBurnDuration: 0.1f, // 0.42f,
            MotorAcceleration: 60.0f, // 18.0f,
            Gravity: 9.81f, // 4.5f,
            MaximumLifetime: 8.0f,
            ExplosionRadius: 2.5f,
            ExhaustIgnitionDuration: 0.10f,
            ExhaustFadeDuration: 1.50f),
        _ => new(0.0f, 0.0f, 0.0f, 9.81f, 5.0f, 1.5f, 0.0f, 0.0f)
    };
}

public static class ProjectileTrajectory
{
    public static void Evaluate(
        Vector3 start,
        Vector3 initialVelocity,
        float age,
        ProjectileFlightProfile profile,
        out Vector3 position,
        out Vector3 velocity)
    {
        age = Math.Max(0.0f, age);
        Vector3 launchDirection = initialVelocity.LengthSquared() > 0.0001f
            ? Vector3.Normalize(initialVelocity)
            : Vector3.Forward;
        Vector3 gravity = Vector3.Down * profile.Gravity;
        position = start;
        velocity = initialVelocity;

        float delayTime = Math.Min(age, profile.MotorDelay);
        Advance(ref position, ref velocity, gravity, delayTime);
        age -= delayTime;

        float burnTime = Math.Min(age, profile.MotorBurnDuration);
        Advance(
            ref position,
            ref velocity,
            gravity + launchDirection * profile.MotorAcceleration,
            burnTime);
        age -= burnTime;

        Advance(ref position, ref velocity, gravity, age);
    }

    public static bool IsMotorBurning(float age, ProjectileFlightProfile profile) =>
        age >= profile.MotorDelay &&
        age < profile.MotorDelay + profile.MotorBurnDuration;

    /// <summary>
    /// Visual exhaust strength. It ramps up after ignition and continues to
    /// fade smoothly after physical thrust has ended.
    /// </summary>
    public static float GetExhaustStrength(float age, ProjectileFlightProfile profile)
    {
        if (age < profile.MotorDelay)
            return 0.0f;

        float poweredAge = age - profile.MotorDelay;
        if (poweredAge < profile.MotorBurnDuration)
        {
            if (profile.ExhaustIgnitionDuration <= 0.0f)
                return 1.0f;
            float ignition = MathHelper.Clamp(
                poweredAge / profile.ExhaustIgnitionDuration,
                0.0f,
                1.0f);
            return ignition * ignition * (3.0f - 2.0f * ignition);
        }

        if (profile.ExhaustFadeDuration <= 0.0f)
            return 0.0f;
        float fade = MathHelper.Clamp(
            (poweredAge - profile.MotorBurnDuration) / profile.ExhaustFadeDuration,
            0.0f,
            1.0f);
        float remaining = 1.0f - fade;
        return remaining * remaining;
    }

    private static void Advance(
        ref Vector3 position,
        ref Vector3 velocity,
        Vector3 acceleration,
        float seconds)
    {
        if (seconds <= 0.0f)
            return;
        position += velocity * seconds + acceleration * (0.5f * seconds * seconds);
        velocity += acceleration * seconds;
    }
}
