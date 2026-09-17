using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

/// <summary>
/// Per-instance composition of imported meshes. A MeshSet uses a root mesh and
/// attaches child MeshSets at named, empty BBModel groups such as
/// "pivot:turret". Shared Mesh geometry remains reusable by many Units.
/// </summary>
public sealed class MeshSet
{
    public sealed record MeshSetPivot(string Name, string Path);

    private sealed class Attachment(MeshSet meshSet)
    {
        public MeshSet MeshSet { get; } = meshSet;
        public Matrix LocalTransform { get; set; } = Matrix.Identity;
        
    }

    private sealed record AttachmentStep(MeshSet Owner, Mesh.Pivot Pivot, Attachment Attachment);

    private sealed record CachedPivot(
        MeshSetPivot Description,
        MeshSet Owner,
        Mesh.Pivot Pivot,
        IReadOnlyList<AttachmentStep> Steps);

    private readonly Dictionary<string, Attachment> _attachments =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _parameters = [];
    private readonly List<CachedPivot> _cachedPivots = [];
    private readonly List<MeshSetPivot> _pivots = [];
    private CachedPivot? _exhaustPivot;
    private CachedPivot? _turretPivot;
    private CachedPivot? _barrelPivot;
    private CachedPivot? _pivotMuzzle;
    private CachedPivot? _gunPivot;
    private bool _isDirty = true;

    public Mesh RootMesh { get; }
    /// <summary>True when the cached placeholder list must be rebuilt.</summary>
    public bool IsDirty => _isDirty || _attachments.Values.Any(attachment => attachment.MeshSet.IsDirty);
    /// <summary>All discovered pivot nodes, including pivots in nested attachments.</summary>
    public IReadOnlyList<MeshSetPivot> Pivots
    {
        get
        {
            EnsurePivotCache();
            return _pivots;
        }
    }

    public MeshSet(Mesh rootMesh)
    {
        RootMesh = rootMesh ?? throw new ArgumentNullException(nameof(rootMesh));
    }

    public void SetAttachment(string placeholderName, Mesh mesh) =>
        SetAttachment(placeholderName, new MeshSet(mesh));

    public void SetAttachment(string placeholderName, MeshSet meshSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placeholderName);
        ArgumentNullException.ThrowIfNull(meshSet);
        _attachments[placeholderName] = new Attachment(meshSet);
        _isDirty = true;
    }

    public bool RemoveAttachment(string placeholderName)
    {
        bool removed = _attachments.Remove(placeholderName);
        if (removed)
            _isDirty = true;
        return removed;
    }

    public bool TryGetAttachment(string placeholderName, out MeshSet meshSet) =>
        TryGetAttachmentSet(placeholderName, out meshSet!);

    /// <summary>
    /// Replaces an attachment below this MeshSet using a slash-separated pivot
    /// path, for example "pivot:turret/pivot:barrel".
    /// </summary>
    public bool SetAttachmentPath(string placeholderPath, Mesh mesh)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placeholderPath);
        ArgumentNullException.ThrowIfNull(mesh);

        string[] path = placeholderPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (path.Length == 0)
            return false;

        MeshSet current = this;
        for (int index = 0; index < path.Length - 1; index++)
        {
            if (!current.TryGetAttachment(path[index], out MeshSet? child))
                return false;
            current = child;
        }

        current.SetAttachment(path[^1], mesh);
        return true;
    }

    /// <summary>
    /// Applies a local correction before the attachment is placed at its pivot.
    /// This is intended for effects such as barrel recoil and does not modify
    /// shared Mesh geometry.
    /// </summary>
    public bool SetAttachmentLocalTransform(string placeholderPath, Matrix localTransform)
    {
        if (!TryGetAttachmentAtPath(placeholderPath, out Attachment? attachment))
            return false;

        attachment.LocalTransform = localTransform;
        return true;
    }

    public void SetParameter(string parameterName, float value) => _parameters[parameterName] = value;

    public float GetParameter(string parameterName) =>
        _parameters.TryGetValue(parameterName, out float value) ? value : 0.0f;

    /// <summary>
    /// Returns the combined local bounds of the root mesh and all attached
    /// meshes. This is intended for setup-time sizing, not per-frame queries.
    /// </summary>
    public BoundingBox GetBounds()
    {
        bool hasBounds = false;
        Vector3 minimum = Vector3.Zero;
        Vector3 maximum = Vector3.Zero;
        IncludeBounds(this, Matrix.Identity, ref hasBounds, ref minimum, ref maximum);
        return new BoundingBox(minimum, maximum);
    }

    /// <summary>
    /// Resolves a slash-separated placeholder path to a world transform. For
    /// example: "pivot:turret/pivot:barrel/pivot:muzzle". The final pivot
    /// need not have an attachment; this makes empty muzzle groups useful as
    /// projectile and particle spawn points.
    /// </summary>
    public bool TryGetPivotWorldTransform(
        string pivotName,
        Matrix world,
        out Matrix pivotWorld)
    {
        pivotWorld = Matrix.Identity;
        if (string.IsNullOrWhiteSpace(pivotName))
            return false;

        EnsurePivotCache();
        CachedPivot? pivot = _cachedPivots.FirstOrDefault(candidate =>
            string.Equals(candidate.Description.Name, pivotName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Description.Path, pivotName, StringComparison.OrdinalIgnoreCase));
        if (pivot is null)
            return false;

        Matrix currentWorld = world;
        foreach (AttachmentStep step in pivot.Steps)
        {
            Matrix attachmentWorld = step.Owner.RootMesh.GetPivotWorldTransform(
                step.Pivot, currentWorld, step.Owner._parameters);
            currentWorld = step.Attachment.LocalTransform * attachmentWorld;
        }

        pivotWorld = pivot.Owner.RootMesh.GetPivotWorldTransform(
            pivot.Pivot, currentWorld, pivot.Owner._parameters);
        return true;
    }

    public bool TryGetPivotWorldPosition(string pivotName, Matrix world, out Vector3 position)
    {
        if (TryGetPivotWorldTransform(pivotName, world, out Matrix pivotWorld))
        {
            position = pivotWorld.Translation;
            return true;
        }

        position = Vector3.Zero;
        return false;
    }

    /// <summary>Gets the cached, single exhaust pivot without a name lookup per frame.</summary>
    public bool TryGetExhaustWorldPosition(Matrix world, out Vector3 position)
    {
        EnsurePivotCache();
        if (_exhaustPivot is null)
        {
            position = Vector3.Zero;
            return false;
        }

        Matrix currentWorld = world;
        foreach (AttachmentStep step in _exhaustPivot.Steps)
        {
            Matrix attachmentWorld = step.Owner.RootMesh.GetPivotWorldTransform(
                step.Pivot, currentWorld, step.Owner._parameters);
            currentWorld = step.Attachment.LocalTransform * attachmentWorld;
        }

        position = _exhaustPivot.Owner.RootMesh.GetPivotWorldTransform(
            _exhaustPivot.Pivot, currentWorld, _exhaustPivot.Owner._parameters).Translation;
        return true;
    }

    public void Draw(Effect effect, Matrix world, AnimationPose? pose = null) =>
        RootMesh.Draw(effect, world, _parameters,
            (placeholderName, attachmentWorld) => DrawAttachment(effect, placeholderName, attachmentWorld), pose);

    private void DrawAttachment(Effect effect, string placeholderName, Matrix attachmentWorld)
    {
        if (_attachments.TryGetValue(placeholderName, out Attachment? attachment))
            attachment.MeshSet.Draw(effect, attachment.LocalTransform * attachmentWorld);
    }

    private bool TryGetAttachmentSet(string placeholderName, out MeshSet meshSet)
    {
        if (_attachments.TryGetValue(placeholderName, out Attachment? attachment))
        {
            meshSet = attachment.MeshSet;
            return true;
        }

        meshSet = null!;
        return false;
    }

    private bool TryGetAttachmentAtPath(string placeholderPath, out Attachment attachment)
    {
        attachment = null!;
        if (string.IsNullOrWhiteSpace(placeholderPath))
            return false;

        string[] path = placeholderPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (path.Length == 0)
            return false;

        MeshSet current = this;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current._attachments.TryGetValue(path[index], out Attachment? found))
                return false;
            if (index == path.Length - 1)
            {
                attachment = found;
                return true;
            }
            current = found.MeshSet;
        }

        return false;
    }

    private void EnsurePivotCache()
    {
        if (!IsDirty)
            return;

        _cachedPivots.Clear();
        _pivots.Clear();
        _exhaustPivot = null;
        CollectPivots(this, [], "");

        foreach (var pivot in _cachedPivots)
        {
            if (pivot.Description.Name.StartsWith("pivot:", StringComparison.OrdinalIgnoreCase))
            {
                if (pivot.Description.Name.Equals("pivot:exhaust", StringComparison.OrdinalIgnoreCase))
                    _exhaustPivot = pivot;
                if (pivot.Description.Name.Equals("pivot:turret", StringComparison.OrdinalIgnoreCase))
                    _turretPivot = pivot;
                if (pivot.Description.Name.Equals("pivot:barrel", StringComparison.OrdinalIgnoreCase))
                    _barrelPivot = pivot;
                if (pivot.Description.Name.Equals("pivot:gun", StringComparison.OrdinalIgnoreCase))
                    _gunPivot = pivot;
                if (pivot.Description.Name.Equals("pivot:muzzle", StringComparison.OrdinalIgnoreCase))
                    _pivotMuzzle = pivot;
            }
        }

        ClearDirtyRecursively();
    }

    private void CollectPivots(
        MeshSet current,
        IReadOnlyList<AttachmentStep> steps,
        string pathPrefix)
    {
        foreach (Mesh.Pivot pivot in current.RootMesh.GetPivots())
        {
            string path = string.IsNullOrEmpty(pathPrefix)
                ? pivot.Name
                : $"{pathPrefix}/{pivot.Name}";
            MeshSetPivot description = new(pivot.Name, path);
            _pivots.Add(description);
            _cachedPivots.Add(new CachedPivot(description, current, pivot, [.. steps]));
        }

        foreach ((string placeholderName, Attachment attachment) in current._attachments)
        {
            Mesh.Pivot? attachmentPivot = current.RootMesh.GetPivots().FirstOrDefault(pivot =>
                string.Equals(pivot.Name, placeholderName, StringComparison.OrdinalIgnoreCase));
            if (attachmentPivot is null)
                continue;

            AttachmentStep step = new(current, attachmentPivot, attachment);
            CollectPivots(
                attachment.MeshSet,
                [.. steps, step],
                string.IsNullOrEmpty(pathPrefix) ? placeholderName : $"{pathPrefix}/{placeholderName}");
        }
    }

    private void ClearDirtyRecursively()
    {
        _isDirty = false;
        foreach (Attachment attachment in _attachments.Values)
            attachment.MeshSet.ClearDirtyRecursively();
    }

    private static void IncludeBounds(
        MeshSet current,
        Matrix world,
        ref bool hasBounds,
        ref Vector3 minimum,
        ref Vector3 maximum)
    {
        Include(current.RootMesh.GetTransformedBounds(world), ref hasBounds, ref minimum, ref maximum);

        foreach ((string placeholderName, Attachment attachment) in current._attachments)
        {
            Mesh.Pivot? pivot = current.RootMesh.GetPivots().FirstOrDefault(candidate =>
                string.Equals(candidate.Name, placeholderName, StringComparison.OrdinalIgnoreCase));
            if (pivot is null)
                continue;

            Matrix attachmentWorld = current.RootMesh.GetPivotWorldTransform(
                pivot, world, current._parameters);
            IncludeBounds(attachment.MeshSet, attachment.LocalTransform * attachmentWorld,
                ref hasBounds, ref minimum, ref maximum);
        }
    }

    private static void Include(
        BoundingBox bounds,
        ref bool hasBounds,
        ref Vector3 minimum,
        ref Vector3 maximum)
    {
        if (!hasBounds)
        {
            minimum = bounds.Min;
            maximum = bounds.Max;
            hasBounds = true;
            return;
        }

        minimum = Vector3.Min(minimum, bounds.Min);
        maximum = Vector3.Max(maximum, bounds.Max);
    }
}
