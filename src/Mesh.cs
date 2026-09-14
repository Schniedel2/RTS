using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using RTS.Mapping;

namespace RTS;

/// <summary>Geometry of a single named part of a <see cref="Mesh"/>.</summary>
public sealed class SubMesh(
    string name,
    VertexPositionColorNormalTexture[] vertices,
    int[] indices,
    Vector3 pivot,
    int? textureAtlasIndex = null,
    TextureHandler.TextureRegion? textureRegion = null,
    TextureHandler.TextureRegion? materialMaskRegion = null)
{
    public string Name { get; } = name;
    public VertexPositionColorNormalTexture[] Vertices { get; } = vertices;
    public int[] Indices { get; } = indices;
    public Vector3 Pivot { get; } = pivot;
    /// <summary>Optional TextureHandler atlas used by UVs imported from a BBModel texture.</summary>
    public int? TextureAtlasIndex { get; } = textureAtlasIndex;
    public TextureHandler.TextureRegion? TextureRegion { get; } = textureRegion;
    public TextureHandler.TextureRegion? MaterialMaskRegion { get; private set; } = materialMaskRegion;
    public bool UsesFullSkinMask { get; private set; }

    public void SetMaterialMask(TextureHandler.TextureRegion materialMaskRegion)
    {
        MaterialMaskRegion = materialMaskRegion;
        UsesFullSkinMask = false;
    }

    public void SetFullSkinMask()
    {
        MaterialMaskRegion = null;
        UsesFullSkinMask = true;
    }

    public (Vector3 Min, Vector3 Max) GetBounds()
    {
        Vector3 min = Vertices[0].Position;
        Vector3 max = min;
        foreach (VertexPositionColorNormalTexture vertex in Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }
        return (min, max);
    }

    /// <summary>
    /// Replaces this sub-mesh's UV coordinates using its own bounds and the
    /// supplied cube texture-atlas layout.
    /// </summary>
    public void ApplyCubeMapping(RTS.Mapping.UVMapping mapping) =>
        CubeMapping.Apply(this, mapping);
}

/// <summary>Node of the mesh hierarchy; transforms itself and all its children around its pivot.</summary>
public sealed class MeshNode(string name, Vector3 pivot)
{
    public string Name { get; } = name;
    public Vector3 Pivot { get; } = pivot;
    public List<MeshNode> Children { get; } = [];
    public List<SubMesh> SubMeshes { get; } = [];

    /// <summary>Name of the mesh parameter that rotates this node around its pivot, e.g. <see cref="Mesh.TurretAngle"/>.</summary>
    public string? RotationParameter { get; set; }
    public Vector3 RotationAxis { get; set; } = Vector3.Up;

    public Matrix GetLocalTransform(IReadOnlyDictionary<string, float> parameters)
    {
        if (RotationParameter is null || !parameters.TryGetValue(RotationParameter, out float angle) || angle == 0.0f)
            return Matrix.Identity;
        return Matrix.CreateTranslation(-Pivot) * Matrix.CreateFromAxisAngle(RotationAxis, angle) * Matrix.CreateTranslation(Pivot);
    }

    public void Draw(
        Effect effect,
        Matrix parentWorld,
        IReadOnlyDictionary<string, float> parameters,
        Action<string, Matrix>? drawAttachments = null)
    {
        Matrix world = GetLocalTransform(parameters) * parentWorld;
        foreach (SubMesh subMesh in SubMeshes)
        {
            effect.Parameters["World"]?.SetValue(world);
            if (subMesh.TextureAtlasIndex is int atlasIndex)
                effect.Parameters["UnitTexture"]?.SetValue(Globals.TextureHandler.GetAtlas(atlasIndex));
            ApplyMaterialMask(effect, subMesh);
            RenderHelper.DrawMesh(effect, subMesh.Vertices, subMesh.Indices);
        }
        drawAttachments?.Invoke(Name, GetAttachmentWorld(parentWorld, parameters));
        foreach (MeshNode child in Children)
            child.Draw(effect, world, parameters, drawAttachments);
    }

    private static void ApplyMaterialMask(Effect effect, SubMesh subMesh)
    {
        if (subMesh.MaterialMaskRegion is not TextureHandler.TextureRegion mask ||
            subMesh.TextureRegion is not TextureHandler.TextureRegion texture)
        {
            effect.Parameters["MaterialMaskUseTexture"]?.SetValue(0.0f);
            effect.Parameters["MaterialMaskDefaultPlayerMask"]?.SetValue(subMesh.UsesFullSkinMask ? 1.0f : 0.0f);
            return;
        }

        effect.Parameters["MaterialMaskTexture"]?.SetValue(Globals.TextureHandler.GetAtlas(mask.AtlasIndex));
        effect.Parameters["MaterialMaskSourceUVOffset"]?.SetValue(texture.UVOffset);
        effect.Parameters["MaterialMaskUVOffset"]?.SetValue(mask.UVOffset);
        effect.Parameters["MaterialMaskUVScale"]?.SetValue(new Vector2(
            mask.UVScale.X / texture.UVScale.X,
            mask.UVScale.Y / texture.UVScale.Y));
        effect.Parameters["MaterialMaskUseTexture"]?.SetValue(1.0f);
        effect.Parameters["MaterialMaskDefaultPlayerMask"]?.SetValue(0.0f);
    }

    /// <summary>
    /// Returns the world matrix for a separately authored mesh whose local
    /// origin should be placed at this node's pivot. Unlike child geometry of
    /// the original BBModel, an attachment starts around local coordinate 0.
    /// </summary>
    private Matrix GetAttachmentWorld(
        Matrix parentWorld,
        IReadOnlyDictionary<string, float> parameters)
    {
        if (RotationParameter is null || !parameters.TryGetValue(RotationParameter, out float angle) || angle == 0.0f)
            return Matrix.CreateTranslation(Pivot) * parentWorld;

        return Matrix.CreateFromAxisAngle(RotationAxis, angle) *
            Matrix.CreateTranslation(Pivot) * parentWorld;
    }

    internal static Matrix GetAttachmentWorldFromPath(
        IReadOnlyList<MeshNode> nodePath,
        Matrix meshWorld,
        IReadOnlyDictionary<string, float> parameters)
    {
        Matrix parentWorld = meshWorld;
        for (int index = 0; index < nodePath.Count; index++)
        {
            MeshNode node = nodePath[index];
            if (index == nodePath.Count - 1)
                return node.GetAttachmentWorld(parentWorld, parameters);
            parentWorld = node.GetLocalTransform(parameters) * parentWorld;
        }

        return meshWorld;
    }

    public MeshNode? FindNode(string nodeName)
    {
        if (Name == nodeName)
            return this;
        foreach (MeshNode child in Children)
        {
            MeshNode? match = child.FindNode(nodeName);
            if (match is not null)
                return match;
        }
        return null;
    }

    /// <summary>
    /// Finds a placeholder node and returns the transform that places an
    /// attached mesh's local origin at that node's pivot.
    /// </summary>
    public bool TryGetAttachmentWorldTransform(
        string nodeName,
        Matrix parentWorld,
        IReadOnlyDictionary<string, float> parameters,
        out Matrix attachmentWorld)
    {
        Matrix world = GetLocalTransform(parameters) * parentWorld;
        if (string.Equals(Name, nodeName, StringComparison.OrdinalIgnoreCase))
        {
            attachmentWorld = GetAttachmentWorld(parentWorld, parameters);
            return true;
        }

        foreach (MeshNode child in Children)
        {
            if (child.TryGetAttachmentWorldTransform(nodeName, world, parameters, out attachmentWorld))
                return true;
        }

        attachmentWorld = Matrix.Identity;
        return false;
    }

    public IEnumerable<SubMesh> EnumerateSubMeshes()
    {
        foreach (SubMesh subMesh in SubMeshes)
            yield return subMesh;
        foreach (MeshNode child in Children)
        {
            foreach (SubMesh subMesh in child.EnumerateSubMeshes())
                yield return subMesh;
        }
    }
}

/// <summary>A named, renderable model built from a <see cref="MeshNode"/> hierarchy and a set of render parameters.</summary>
public class Mesh
{
    public sealed record Pivot(string Name, IReadOnlyList<MeshNode> NodePath);
    public const string TurretAngle = "TurretAngle";
    public const string WheelAngleY = "HullAngleY";
    public const string WheelAngle = "WheelAngle";

    private readonly Dictionary<string, float> parameters = [];

    public Mesh(string name, MeshNode root)
    {
        Name = name;
        Root = root;
        SubMeshes = [.. root.EnumerateSubMeshes()];
    }

    public Mesh(string name, IReadOnlyList<SubMesh> subMeshes)
        : this(name, CreateFlatRoot(name, subMeshes))
    {
    }

    public string Name { get; }
    public MeshNode Root { get; }
    public IReadOnlyList<SubMesh> SubMeshes { get; }
    /// <summary>
    /// Permanent local correction applied before the Unit or attachment world
    /// transform. Use it for import-time scale, rotation or origin tuning.
    /// It also affects all placeholder pivots and nested MeshSet attachments.
    /// </summary>
    public Matrix LocalTransform { get; set; } = Matrix.Identity;

    public void SetParameter(string parameterName, float value) => parameters[parameterName] = value;

    public float GetParameter(string parameterName) => parameters.TryGetValue(parameterName, out float value) ? value : 0.0f;

    public void ClearParameters() => parameters.Clear();

    public void SetLocalScale(float uniformScale) =>
        LocalTransform = Matrix.CreateScale(uniformScale);

    public void SetLocalScale(Vector3 scale) =>
        LocalTransform = Matrix.CreateScale(scale);

    /// <summary>Uses one authored material mask for every part of this mesh.</summary>
    public void SetMaterialMask(TextureHandler.TextureRegion materialMaskRegion)
    {
        foreach (SubMesh subMesh in SubMeshes)
            subMesh.SetMaterialMask(materialMaskRegion);
    }

    /// <summary>Marks all mesh parts as player-skin material without a PNG mask.</summary>
    public void SetFullSkinMaterialMask()
    {
        foreach (SubMesh subMesh in SubMeshes)
            subMesh.SetFullSkinMask();
    }

    public MeshNode? FindNode(string nodeName) => Root.FindNode(nodeName);

    public bool TryGetSubMesh(string subMeshName, out SubMesh subMesh)
    {
        foreach (SubMesh candidate in SubMeshes)
        {
            if (candidate.Name == subMeshName)
            {
                subMesh = candidate;
                return true;
            }
        }
        subMesh = null!;
        return false;
    }

    public (Vector3 Min, Vector3 Max) GetBounds()
    {
        (Vector3 min, Vector3 max) = SubMeshes[0].GetBounds();
        foreach (SubMesh subMesh in SubMeshes)
        {
            (Vector3 subMin, Vector3 subMax) = subMesh.GetBounds();
            min = Vector3.Min(min, subMin);
            max = Vector3.Max(max, subMax);
        }
        return (min, max);
    }

    /// <summary>
    /// Replaces UV coordinates on every sub-mesh using one shared set of mesh
    /// bounds. Use <see cref="SubMesh.ApplyCubeMapping"/> for independent UV
    /// projection per individual part.
    /// </summary>
    public void ApplyCubeMapping(UVMapping mapping) =>
        CubeMapping.Apply(this, mapping);

    public void Draw(Effect effect, Matrix world) => Draw(effect, world, parameters);

    /// <summary>
    /// Draws with externally owned parameters and placeholder callbacks. This
    /// keeps shared imported meshes immutable while each Unit can own its own
    /// composition and animation state through a MeshSet.
    /// </summary>
    public void Draw(
        Effect effect,
        Matrix world,
        IReadOnlyDictionary<string, float> drawParameters,
        Action<string, Matrix>? drawAttachments = null) =>
        Root.Draw(effect, LocalTransform * world, drawParameters, drawAttachments);

    public bool TryGetPivotWorldTransform(
        string pivotName,
        Matrix world,
        IReadOnlyDictionary<string, float> drawParameters,
        out Matrix pivotWorld) =>
        Root.TryGetAttachmentWorldTransform(
            pivotName,
            LocalTransform * world,
            drawParameters,
            out pivotWorld);

    public IReadOnlyList<Pivot> GetPivots()
    {
        List<Pivot> pivots = [];
        CollectPivots(Root, [], pivots);
        return pivots;
    }

    internal Matrix GetPivotWorldTransform(
        Pivot pivot,
        Matrix world,
        IReadOnlyDictionary<string, float> drawParameters) =>
        MeshNode.GetAttachmentWorldFromPath(
            pivot.NodePath,
            LocalTransform * world,
            drawParameters);

    private static void CollectPivots(MeshNode node, List<MeshNode> ancestors, List<Pivot> pivots)
    {
        List<MeshNode> path = [.. ancestors, node];
        if (node.Name.StartsWith("pivot:", StringComparison.OrdinalIgnoreCase))
            pivots.Add(new Pivot(node.Name, path));
        foreach (MeshNode child in node.Children)
            CollectPivots(child, path, pivots);
    }

    private static MeshNode CreateFlatRoot(string name, IReadOnlyList<SubMesh> subMeshes)
    {
        MeshNode root = new(name, Vector3.Zero);
        foreach (SubMesh subMesh in subMeshes)
        {
            MeshNode node = new(subMesh.Name, subMesh.Pivot);
            node.SubMeshes.Add(subMesh);
            root.Children.Add(node);
        }
        return root;
    }
}
