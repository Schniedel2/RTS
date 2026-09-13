using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

/// <summary>
/// Per-instance composition of imported meshes. A MeshSet uses a root mesh and
/// attaches child MeshSets at named, empty BBModel groups such as
/// "pivot:turret". Shared Mesh geometry remains reusable by many Units.
/// </summary>
public sealed class MeshSet
{
    private sealed class Attachment(MeshSet meshSet)
    {
        public MeshSet MeshSet { get; } = meshSet;
        public Matrix LocalTransform { get; set; } = Matrix.Identity;
    }

    private readonly Dictionary<string, Attachment> _attachments =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _parameters = [];

    public Mesh RootMesh { get; }

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
    }

    public bool RemoveAttachment(string placeholderName) => _attachments.Remove(placeholderName);

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
    /// Resolves a slash-separated placeholder path to a world transform. For
    /// example: "pivot:turret/pivot:barrel/pivot:muzzle". The final pivot
    /// need not have an attachment; this makes empty muzzle groups useful as
    /// projectile and particle spawn points.
    /// </summary>
    public bool TryGetPivotWorldTransform(
        string pivotPath,
        Matrix world,
        out Matrix pivotWorld)
    {
        pivotWorld = Matrix.Identity;
        if (string.IsNullOrWhiteSpace(pivotPath))
            return false;

        string[] path = pivotPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (path.Length == 0)
            return false;

        MeshSet current = this;
        Matrix currentWorld = world;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current.RootMesh.TryGetPivotWorldTransform(
                    path[index], currentWorld, current._parameters, out Matrix attachmentWorld))
                return false;

            if (index == path.Length - 1)
            {
                pivotWorld = attachmentWorld;
                return true;
            }

            if (!current._attachments.TryGetValue(path[index], out Attachment? attachment))
                return false;
            currentWorld = attachment.LocalTransform * attachmentWorld;
            current = attachment.MeshSet;
        }

        return false;
    }

    public bool TryGetPivotWorldPosition(string pivotPath, Matrix world, out Vector3 position)
    {
        if (TryGetPivotWorldTransform(pivotPath, world, out Matrix pivotWorld))
        {
            position = pivotWorld.Translation;
            return true;
        }

        position = Vector3.Zero;
        return false;
    }

    public void Draw(Effect effect, Matrix world) =>
        RootMesh.Draw(effect, world, _parameters,
            (placeholderName, attachmentWorld) => DrawAttachment(effect, placeholderName, attachmentWorld));

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
}
