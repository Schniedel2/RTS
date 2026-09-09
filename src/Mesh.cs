using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace RTS;

/// <summary>Geometry of a single named part of a <see cref="Mesh"/>.</summary>
public sealed class SubMesh(string name, VertexPositionColorNormalTexture[] vertices, int[] indices, Vector3 pivot)
{
    public string Name { get; } = name;
    public VertexPositionColorNormalTexture[] Vertices { get; } = vertices;
    public int[] Indices { get; } = indices;
    public Vector3 Pivot { get; } = pivot;

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

    public void Draw(Effect effect, Matrix parentWorld, IReadOnlyDictionary<string, float> parameters)
    {
        Matrix world = GetLocalTransform(parameters) * parentWorld;
        foreach (SubMesh subMesh in SubMeshes)
        {
            effect.Parameters["World"]?.SetValue(world);
            RenderHelper.DrawMesh(effect, subMesh.Vertices, subMesh.Indices);
        }
        foreach (MeshNode child in Children)
            child.Draw(effect, world, parameters);
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

    public void SetParameter(string parameterName, float value) => parameters[parameterName] = value;

    public float GetParameter(string parameterName) => parameters.TryGetValue(parameterName, out float value) ? value : 0.0f;

    public void ClearParameters() => parameters.Clear();

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

    public void Draw(Effect effect, Matrix world) => Root.Draw(effect, world, parameters);

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