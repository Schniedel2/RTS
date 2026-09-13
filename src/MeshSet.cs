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
    private readonly Dictionary<string, MeshSet> _attachments =
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
        _attachments[placeholderName] = meshSet;
    }

    public bool RemoveAttachment(string placeholderName) => _attachments.Remove(placeholderName);

    public bool TryGetAttachment(string placeholderName, out MeshSet meshSet) =>
        _attachments.TryGetValue(placeholderName, out meshSet!);

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

    public void SetParameter(string parameterName, float value) => _parameters[parameterName] = value;

    public float GetParameter(string parameterName) =>
        _parameters.TryGetValue(parameterName, out float value) ? value : 0.0f;

    public void Draw(Effect effect, Matrix world) =>
        RootMesh.Draw(effect, world, _parameters,
            (placeholderName, attachmentWorld) => DrawAttachment(effect, placeholderName, attachmentWorld));

    private void DrawAttachment(Effect effect, string placeholderName, Matrix attachmentWorld)
    {
        if (_attachments.TryGetValue(placeholderName, out MeshSet? attachment))
            attachment.Draw(effect, attachmentWorld);
    }
}
