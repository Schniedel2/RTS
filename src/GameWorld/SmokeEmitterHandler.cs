using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace RTS;

/// <summary>Owns and updates non-gameplay smoke emitters in the world.</summary>
public sealed class SmokeEmitterHandler
{
    private readonly List<SmokeEmitter> _emitters = [];
    public IReadOnlyList<SmokeEmitter> Emitters => _emitters;

    public SmokeEmitter Add(SmokeEmitter emitter)
    {
        _emitters.Add(emitter);
        return emitter;
    }

    public SmokeEmitter Create(
        Vector3 position,
        SmokeEmissionSettings settings,
        float emissionsPerSecond = 2.0f,
        float? lifetime = null,
        Vector3? direction = null) =>
        Add(new SmokeEmitter(position, settings, emissionsPerSecond, lifetime, direction));

    public bool Remove(SmokeEmitter emitter) => _emitters.Remove(emitter);

    public void Update(GameTime gameTime)
    {
        for (int index = _emitters.Count - 1; index >= 0; index--)
        {
            SmokeEmitter emitter = _emitters[index];
            emitter.Update(gameTime);
            if (emitter.IsExpired)
                _emitters.RemoveAt(index);
        }
    }
}
