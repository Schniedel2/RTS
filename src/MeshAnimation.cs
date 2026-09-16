using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

/// <summary>Immutable animation data imported once from a BBModel.</summary>
public sealed class MeshAnimationClip(string name, float durationSeconds, bool loop)
{
    public string Name { get; } = name;
    public float DurationSeconds { get; } = Math.Max(0.0f, durationSeconds);
    public bool Loop { get; } = loop;
    public Dictionary<string, MeshAnimationTrack> Tracks { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MeshAnimationTrack
{
    public List<MeshAnimationKeyframe> RotationKeys { get; } = [];
    // Read now to preserve the BBModel data model. Rendering support for
    // translation/scale can be enabled as soon as a model needs it.
    public List<MeshAnimationKeyframe> PositionKeys { get; } = [];
    public List<MeshAnimationKeyframe> ScaleKeys { get; } = [];

    public Vector3 EvaluateRotation(float timeSeconds) => Evaluate(RotationKeys, timeSeconds, Vector3.Zero);

    private static Vector3 Evaluate(List<MeshAnimationKeyframe> keys, float time, Vector3 fallback)
    {
        if (keys.Count == 0)
            return fallback;
        if (keys.Count == 1 || time <= keys[0].TimeSeconds)
            return keys[0].Value;
        for (int index = 1; index < keys.Count; index++)
        {
            MeshAnimationKeyframe next = keys[index];
            if (time > next.TimeSeconds)
                continue;
            MeshAnimationKeyframe previous = keys[index - 1];
            float range = next.TimeSeconds - previous.TimeSeconds;
            float amount = range <= 0.00001f ? 1.0f : (time - previous.TimeSeconds) / range;
            return Vector3.Lerp(previous.Value, next.Value, MathHelper.Clamp(amount, 0.0f, 1.0f));
        }
        return keys[^1].Value;
    }
}

public readonly record struct MeshAnimationKeyframe(float TimeSeconds, Vector3 Value);

/// <summary>Per-instance animation clock. It never mutates shared Mesh data.</summary>
public sealed class AnimationPlayer
{
    private readonly IReadOnlyDictionary<string, MeshAnimationClip> _clips;
    public string? CurrentClipName { get; private set; }
    public float TimeSeconds { get; private set; }
    public float Speed { get; set; } = 1.0f;

    public AnimationPlayer(IReadOnlyDictionary<string, MeshAnimationClip> clips) => _clips = clips;

    public void Play(string clipName, bool restart = false)
    {
        if (!_clips.ContainsKey(clipName))
            return;
        if (!restart && string.Equals(CurrentClipName, clipName, StringComparison.OrdinalIgnoreCase))
            return;
        CurrentClipName = clipName;
        TimeSeconds = 0.0f;
    }

    public void Update(float elapsedSeconds)
    {
        if (CurrentClipName is null || !_clips.TryGetValue(CurrentClipName, out MeshAnimationClip? clip))
            return;
        TimeSeconds += Math.Max(0.0f, elapsedSeconds) * Math.Max(0.0f, Speed);
        if (clip.DurationSeconds <= 0.0f)
        {
            TimeSeconds = 0.0f;
            return;
        }
        TimeSeconds = clip.Loop ? TimeSeconds % clip.DurationSeconds : Math.Min(TimeSeconds, clip.DurationSeconds);
    }

    public AnimationPose EvaluatePose()
    {
        AnimationPose pose = new();
        if (CurrentClipName is null || !_clips.TryGetValue(CurrentClipName, out MeshAnimationClip? clip))
            return pose;
        foreach ((string groupName, MeshAnimationTrack track) in clip.Tracks)
            pose.SetRotation(groupName, track.EvaluateRotation(TimeSeconds));
        return pose;
    }
}

/// <summary>Short-lived evaluated pose passed to one MeshSet draw call.</summary>
public sealed class AnimationPose
{
    private readonly Dictionary<string, Vector3> _rotationDegrees = new(StringComparer.OrdinalIgnoreCase);
    public void SetRotation(string groupName, Vector3 degrees) => _rotationDegrees[groupName] = degrees;
    public bool TryGetRotation(string groupName, out Vector3 degrees) => _rotationDegrees.TryGetValue(groupName, out degrees);
}
