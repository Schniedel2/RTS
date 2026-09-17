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
    private sealed class OverlayLayer(string clipName, float weight)
    {
        public string ClipName { get; } = clipName;
        public float Weight { get; set; } = MathHelper.Clamp(weight, 0.0f, 1.0f);
        public float TimeSeconds { get; set; }
    }

    private readonly IReadOnlyDictionary<string, MeshAnimationClip> _clips;
    private readonly List<OverlayLayer> _overlays = [];
    public string? CurrentClipName { get; private set; }
    public float TimeSeconds { get; private set; }
    public float Speed { get; set; } = 1.0f;
    private float _overlayTransitionDuration;
    private OverlayLayer? _overlayTransitionTarget = null;
    private OverlayLayer? _overlayTransitionSource = null;
    private float _overlayTransitionElapsed;

    public IReadOnlyList<string> OverlayClipNames => _overlays.Select(layer => layer.ClipName).ToArray();

    public AnimationPlayer(IReadOnlyDictionary<string, MeshAnimationClip> clips) => _clips = clips;

    public void SetRandomAnimationTime()
    {
        if (CurrentClipName is null)
            return;
        MeshAnimationClip clip = _clips[CurrentClipName];
        TimeSeconds = Random.Shared.NextSingle() * clip.DurationSeconds;
    }

    public void Play(string clipName, bool restart = false)
    {
        if (!_clips.ContainsKey(clipName))
            return;
        if (!restart && string.Equals(CurrentClipName, clipName, StringComparison.OrdinalIgnoreCase))
            return;
        CurrentClipName = clipName;
        TimeSeconds = 0.0f;
    }

    /// <summary>
    /// Adds or updates a layered pose. Tracks in this clip blend over the base
    /// pose only for the groups they actually animate.
    /// </summary>
    public bool AddOverlay(string clipName, float weight = 1.0f)
    {
        if (!_clips.ContainsKey(clipName))
            return false;
        OverlayLayer? existing = _overlays.FirstOrDefault(layer =>
            string.Equals(layer.ClipName, clipName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Weight = MathHelper.Clamp(weight, 0.0f, 1.0f);
            return true;
        }
        _overlays.Add(new OverlayLayer(clipName, weight));
        return true;
    }

    public bool RemoveOverlay(string clipName)
    {
        int index = _overlays.FindIndex(layer =>
            string.Equals(layer.ClipName, clipName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;
        _overlays.RemoveAt(index);
        return true;
    }

    public void ClearOverlays() => _overlays.Clear();

    public void AddOverlayTransition(string? to, float duration)
    {
        AddOverlayTransition(_overlays.FirstOrDefault()?.ClipName ?? null, to, duration);
    }

    public void AddOverlayTransition(string? from,string? to, float duration)
    {
        // For now, simply add the overlay with initial weight 0.0f.
        _overlayTransitionTarget = null;
        if (to is not null)
        {
            OverlayLayer? existingTarget = _overlays.FirstOrDefault(layer =>
                string.Equals(layer.ClipName, to, StringComparison.OrdinalIgnoreCase));
            if (existingTarget is not null)
                return;

            AddOverlay(to, 0.0f);
            _overlayTransitionTarget = _overlays.FirstOrDefault(layer =>
                string.Equals(layer.ClipName, to, StringComparison.OrdinalIgnoreCase));
        }

        _overlayTransitionSource = null;
        if (from is not null)
        {
            AddOverlay(from, 1.0f);
            _overlayTransitionSource = _overlays.FirstOrDefault(layer =>
                string.Equals(layer.ClipName, from, StringComparison.OrdinalIgnoreCase));
        }

        _overlayTransitionElapsed = 0.0f;
        _overlayTransitionDuration = duration;
    }

    public void UpdateOverlayTransition(float elapsedSeconds)
    {
        if ((_overlayTransitionTarget is null) && (_overlayTransitionSource is null))
            return;

        _overlayTransitionElapsed += elapsedSeconds;
        float ratio = MathHelper.Clamp(_overlayTransitionElapsed / _overlayTransitionDuration, 0.0f, 1.0f);

        float sourceWeight = 1.0f - ratio;
        float targetWeight = ratio;

        if (_overlayTransitionSource is not null)
            _overlayTransitionSource.Weight = sourceWeight;

        if (_overlayTransitionTarget is not null)
            _overlayTransitionTarget.Weight = targetWeight;

        if (_overlayTransitionElapsed >= _overlayTransitionDuration)
        {
            if (_overlayTransitionSource is not null)
                RemoveOverlay(_overlayTransitionSource.ClipName);
            _overlayTransitionTarget = null;
            _overlayTransitionSource = null;
        }
    }

    public void Update(float elapsedSeconds)
    {
        UpdateOverlayTransition(elapsedSeconds);

        float deltaSeconds = Math.Max(0.0f, elapsedSeconds) * Math.Max(0.0f, Speed);
        if (CurrentClipName is not null && _clips.TryGetValue(CurrentClipName, out MeshAnimationClip? clip))
            TimeSeconds = AdvanceTime(TimeSeconds, clip, deltaSeconds);
     
        foreach (OverlayLayer layer in _overlays)
            if (_clips.TryGetValue(layer.ClipName, out MeshAnimationClip? overlayClip))
                layer.TimeSeconds = AdvanceTime(layer.TimeSeconds, overlayClip, deltaSeconds);
    }

    public AnimationPose EvaluatePose()
    {
        AnimationPose pose = new();
        if (CurrentClipName is not null && _clips.TryGetValue(CurrentClipName, out MeshAnimationClip? clip))
            foreach ((string groupName, MeshAnimationTrack track) in clip.Tracks)
                pose.SetRotation(groupName, track.EvaluateRotation(TimeSeconds));
        foreach (OverlayLayer layer in _overlays)
        {
            if (!_clips.TryGetValue(layer.ClipName, out MeshAnimationClip? overlayClip) || layer.Weight <= 0.0f)
                continue;
            foreach ((string groupName, MeshAnimationTrack track) in overlayClip.Tracks)
            {
                Vector3 baseRotation = pose.GetRotationOrDefault(groupName);
                pose.SetRotation(groupName, Vector3.Lerp(baseRotation, track.EvaluateRotation(layer.TimeSeconds), layer.Weight));
            }
        }
        return pose;
    }

    private static float AdvanceTime(float currentTime, MeshAnimationClip clip, float deltaSeconds)
    {
        if (clip.DurationSeconds <= 0.0f)
            return 0.0f;
        float nextTime = currentTime + deltaSeconds;
        return clip.Loop ? nextTime % clip.DurationSeconds : Math.Min(nextTime, clip.DurationSeconds);
    }
}

/// <summary>Short-lived evaluated pose passed to one MeshSet draw call.</summary>
public sealed class AnimationPose
{
    private readonly Dictionary<string, Vector3> _rotationDegrees = new(StringComparer.OrdinalIgnoreCase);
    public void SetRotation(string groupName, Vector3 degrees) => _rotationDegrees[groupName] = degrees;
    public bool TryGetRotation(string groupName, out Vector3 degrees) => _rotationDegrees.TryGetValue(groupName, out degrees);
    public Vector3 GetRotationOrDefault(string groupName) =>
        _rotationDegrees.TryGetValue(groupName, out Vector3 degrees) ? degrees : Vector3.Zero;
}
