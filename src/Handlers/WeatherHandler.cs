using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public sealed class WindEffect
{
    public Vector3 Position { get; }
    public float Radius { get; }
    public float Strength { get; }
    public float Lifetime { get; }
    public float Age { get; private set; }
    public bool IsExpired => Age >= Lifetime;

    public WindEffect(Vector3 position, float radius, float strength, float lifetime)
    {
        Position = position;
        Radius = Math.Max(0.01f, radius);
        Strength = strength;
        Lifetime = Math.Max(0.01f, lifetime);
    }

    internal void Update(float deltaSeconds) => Age += deltaSeconds;
    internal float CurrentStrength => Strength * MathHelper.Clamp(1.0f - Age / Lifetime, 0.0f, 1.0f);
}

/// <summary>World-wide global wind plus a rebuilt, additive local wind map.</summary>
public sealed class WeatherHandler
{
    private Vector3[] _localWindMap = [];
    private readonly List<WindEffect> _windEffects = [];
    private int _windMapWidth;
    private int _windMapHeight;
    private float _windMapUpdateElapsed;

    public float ClockHours { get; private set; } = 6.0f;
    public float SunAngleRadians { get; private set; }
    public Vector3 WindVelocity { get; private set; }
    public bool LocalWindEffectsEnabled { get; set; } = true;
    public float WindMapUpdateInterval { get; set; } = 1.0f / 20.0f;
    public IReadOnlyList<WindEffect> WindEffects => _windEffects;

    public WeatherHandler(int windMapWidth, int windMapHeight)
    {
        ResizeWindMap(windMapWidth, windMapHeight);
        SetClock(6.0f);
        SetWind(Random.Shared.NextSingle() * Vector3.One * 3.0f);
    }

    public void SetClock(float hours)
    {
        ClockHours = hours % 24.0f;
        if (ClockHours < 0.0f)
            ClockHours += 24.0f;
        SunAngleRadians = (ClockHours - 6.0f) / 24.0f * MathHelper.TwoPi;
    }

    public void SetWind(Vector3 windVelocity) => WindVelocity = windVelocity;

    public WindEffect AddExplosionWind(Vector3 position, float radius = 5.0f,
        float strength = 8.0f, float lifetime = 0.45f)
    {
        WindEffect effect = new(position, radius, strength, lifetime);
        _windEffects.Add(effect);
        RebuildLocalWindMap();
        return effect;
    }

    public void Update(GameTime gameTime)
    {
        float deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        for (int index = _windEffects.Count - 1; index >= 0; index--)
        {
            WindEffect effect = _windEffects[index];
            effect.Update(deltaSeconds);
            if (effect.IsExpired)
                _windEffects.RemoveAt(index);
        }

        _windMapUpdateElapsed += deltaSeconds;
        float interval = Math.Max(0.001f, WindMapUpdateInterval);
        if (_windMapUpdateElapsed >= interval)
        {
            _windMapUpdateElapsed %= interval;
            RebuildLocalWindMap();
        }
    }

    /// <summary>Global wind plus the local map; local effects can be disabled independently.</summary>
    public Vector3 GetWind(Vector3 worldPosition) =>
        WindVelocity + (LocalWindEffectsEnabled ? SampleLocalWind(worldPosition) : Vector3.Zero);

    public void ResizeWindMap(int width, int height)
    {
        _windMapWidth = Math.Max(1, width);
        _windMapHeight = Math.Max(1, height);
        _localWindMap = new Vector3[_windMapWidth * _windMapHeight];
        RebuildLocalWindMap();
    }

    private void RebuildLocalWindMap()
    {
        Array.Clear(_localWindMap);
        if (!LocalWindEffectsEnabled)
            return;
        foreach (WindEffect effect in _windEffects)
            AccumulateEffect(effect);
    }

    private void AccumulateEffect(WindEffect effect)
    {
        float strength = effect.CurrentStrength;
        if (strength <= 0.0f)
            return;
        int minX = Math.Max(0, (int)MathF.Floor(effect.Position.X - effect.Radius));
        int maxX = Math.Min(_windMapWidth - 1, (int)MathF.Floor(effect.Position.X + effect.Radius));
        int minZ = Math.Max(0, (int)MathF.Floor(effect.Position.Z - effect.Radius));
        int maxZ = Math.Min(_windMapHeight - 1, (int)MathF.Floor(effect.Position.Z + effect.Radius));
        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            Vector3 offset = new(x + 0.5f - effect.Position.X, 0.0f, z + 0.5f - effect.Position.Z);
            float distance = offset.Length();
            if (distance > effect.Radius || distance <= 0.0001f)
                continue;
            _localWindMap[z * _windMapWidth + x] +=
                offset / distance * (strength * (1.0f - distance / effect.Radius));
        }
    }

    private Vector3 SampleLocalWind(Vector3 position)
    {
        if (position.X < 0.0f || position.Z < 0.0f || position.X >= _windMapWidth || position.Z >= _windMapHeight)
            return Vector3.Zero;
        float x = MathHelper.Clamp(position.X - 0.5f, 0.0f, _windMapWidth - 1);
        float z = MathHelper.Clamp(position.Z - 0.5f, 0.0f, _windMapHeight - 1);
        int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
        int x1 = Math.Min(x0 + 1, _windMapWidth - 1), z1 = Math.Min(z0 + 1, _windMapHeight - 1);
        float tx = x - x0, tz = z - z0;
        Vector3 top = Vector3.Lerp(_localWindMap[z0 * _windMapWidth + x0], _localWindMap[z0 * _windMapWidth + x1], tx);
        Vector3 bottom = Vector3.Lerp(_localWindMap[z1 * _windMapWidth + x0], _localWindMap[z1 * _windMapWidth + x1], tx);
        return Vector3.Lerp(top, bottom, tz);
    }
}
