using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RTS;

/// <summary>Loads Blockbench ".bbmodel" mesh exports into the shared <see cref="Mesh"/> representation.</summary>
public static class BBModelLoader
{
    private const float ModelScale = 0.1f;

    public static Mesh Load(string path, Color? color = null)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        Color vertexColor = color ?? Color.SteelBlue;
        (float Width, float Height) resolution = ReadResolution(root);

        Dictionary<string, SubMesh> elementsByUuid = [];
        if (root.TryGetProperty("elements", out JsonElement elements))
        {
            foreach (JsonElement element in elements.EnumerateArray())
            {
                if (!element.TryGetProperty("type", out JsonElement type) || type.GetString() != "mesh")
                    continue;
                elementsByUuid[element.GetProperty("uuid").GetString()!] = LoadMeshElement(element, vertexColor, resolution);
            }
        }

        if (elementsByUuid.Count == 0)
            throw new InvalidDataException($"BBModel contains no mesh elements: {path}");

        string meshName = root.TryGetProperty("name", out JsonElement modelName)
            ? modelName.GetString() ?? Path.GetFileNameWithoutExtension(path)
            : Path.GetFileNameWithoutExtension(path);

        MeshNode? hierarchy = BuildHierarchy(root, meshName, elementsByUuid);
        return hierarchy is not null ? new Mesh(meshName, hierarchy) : new Mesh(meshName, [.. elementsByUuid.Values]);
    }

    /// <summary>Rebuilds the Blockbench "outliner" tree so whole groups (turret, wheels, ...) can be transformed at once.</summary>
    private static MeshNode? BuildHierarchy(JsonElement root, string meshName, Dictionary<string, SubMesh> elementsByUuid)
    {
        if (!root.TryGetProperty("outliner", out JsonElement outliner))
            return null;

        Dictionary<string, JsonElement> groupsByUuid = [];
        if (root.TryGetProperty("groups", out JsonElement groups))
        {
            foreach (JsonElement group in groups.EnumerateArray())
                groupsByUuid[group.GetProperty("uuid").GetString()!] = group;
        }

        MeshNode meshRoot = new(meshName, Vector3.Zero);
        foreach (JsonElement item in outliner.EnumerateArray())
        {
            MeshNode? node = BuildNode(item, groupsByUuid, elementsByUuid);
            if (node is not null)
                meshRoot.Children.Add(node);
        }
        return meshRoot.Children.Count > 0 ? meshRoot : null;
    }

    private static MeshNode? BuildNode(JsonElement item, Dictionary<string, JsonElement> groupsByUuid, Dictionary<string, SubMesh> elementsByUuid)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            if (!elementsByUuid.TryGetValue(item.GetString()!, out SubMesh? subMesh))
                return null;
            MeshNode leaf = new(subMesh.Name, subMesh.Pivot);
            leaf.SubMeshes.Add(subMesh);
            ApplyRotationParameter(leaf, isGroup: false);
            return leaf;
        }

        string uuid = item.GetProperty("uuid").GetString()!;
        if (!groupsByUuid.TryGetValue(uuid, out JsonElement group))
            return null;

        string name = group.TryGetProperty("name", out JsonElement groupName) ? groupName.GetString() ?? uuid : uuid;
        MeshNode node = new(name, ReadVector3(group.GetProperty("origin")) * ModelScale);
        ApplyRotationParameter(node, isGroup: true);

        if (item.TryGetProperty("children", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                MeshNode? childNode = BuildNode(child, groupsByUuid, elementsByUuid);
                if (childNode is not null)
                    node.Children.Add(childNode);
            }
        }
        return node;
    }

    /// <summary>Wheel groups roll around their local X axis.</summary>
    private static void ApplyRotationParameter(MeshNode node, bool isGroup)
    {
        node.RotationParameter = node.Name;
        node.RotationAxis = Vector3.Up;

        if (node.Name.Contains("turret", StringComparison.OrdinalIgnoreCase))
        {
            node.RotationParameter = Mesh.TurretAngle;
            node.RotationAxis = Vector3.Up;
            return;
        }
        if (!isGroup || !node.Name.StartsWith("wheel", StringComparison.OrdinalIgnoreCase))
            return;

        node.RotationParameter = Mesh.WheelAngle;
        node.RotationAxis = Vector3.Right;
    }

    private static (float Width, float Height) ReadResolution(JsonElement root)
    {
        if (root.TryGetProperty("resolution", out JsonElement resolution))
        {
            float width = resolution.GetProperty("width").GetSingle();
            float height = resolution.GetProperty("height").GetSingle();
            if (width > 0 && height > 0)
                return (width, height);
        }
        return (32f, 32f);
    }

    private static SubMesh LoadMeshElement(JsonElement element, Color vertexColor, (float Width, float Height) resolution)
    {
        string name = element.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? "mesh" : "mesh";
        // The element's "origin" is the pivot point Blockbench rotates/animates this part around.
        Vector3 origin = ReadVector3(element.GetProperty("origin"));
        Vector3 pivot = origin * ModelScale;
        // Blockbench stores mesh vertices relative to the origin and unrotated, so both have to be applied here.
        Matrix elementTransform = ReadRotation(element) * Matrix.CreateTranslation(origin);

        Dictionary<string, Vector3> positions = [];
        foreach (JsonProperty vertex in element.GetProperty("vertices").EnumerateObject())
            positions[vertex.Name] = Vector3.Transform(ReadVector3(vertex.Value), elementTransform);

        List<VertexPositionColorNormalTexture> vertices = [];
        List<int> indices = [];

        foreach (JsonProperty face in element.GetProperty("faces").EnumerateObject())
        {
            string[] faceVertexKeys = face.Value.GetProperty("vertices").EnumerateArray()
                .Select(item => item.GetString()!).ToArray();
            if (faceVertexKeys.Length < 3)
                continue;

            Vector3[] facePositions = faceVertexKeys.Select(key => positions[key]).ToArray();
            Vector2[] faceUvs = ReadFaceUv(face.Value, faceVertexKeys, resolution);

            for (int index = 1; index < facePositions.Length - 1; index++)
            {
                Vector3 a = facePositions[0], b = facePositions[index], c = facePositions[index + 1];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                normal = normal.LengthSquared() > 0.0f ? Vector3.Normalize(normal) : Vector3.Up;

                int first = vertices.Count;
                vertices.Add(new(b * ModelScale, vertexColor, normal, faceUvs[index]));
                vertices.Add(new(a * ModelScale, vertexColor, normal, faceUvs[0]));
                vertices.Add(new(c * ModelScale, vertexColor, normal, faceUvs[index + 1]));
                indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
            }
        }

        SubMesh submesh = new SubMesh(name, vertices.ToArray(), indices.ToArray(), pivot);
        return submesh;
    }

    private static Vector2[] ReadFaceUv(JsonElement face, string[] faceVertexKeys, (float Width, float Height) resolution)
    {
        if (!face.TryGetProperty("uv", out JsonElement uv))
            return faceVertexKeys.Select(_ => Vector2.Zero).ToArray();

        return faceVertexKeys.Select(key =>
        {
            if (!uv.TryGetProperty(key, out JsonElement uvValue))
                return Vector2.Zero;
            float u = uvValue[0].GetSingle();
            float v = uvValue[1].GetSingle();
            return new Vector2(u / resolution.Width, v / resolution.Height);
        }).ToArray();
    }

    private static Vector3 ReadVector3(JsonElement array)
    {
        JsonElement[] items = array.EnumerateArray().ToArray();
        return new Vector3(items[0].GetSingle(), items[1].GetSingle(), items[2].GetSingle());
    }

    private static Matrix ReadRotation(JsonElement element)
    {
        if (!element.TryGetProperty("rotation", out JsonElement rotation))
            return Matrix.Identity;
        Vector3 angles = ReadVector3(rotation);
        return Matrix.CreateRotationX(MathHelper.ToRadians(angles.X))
             * Matrix.CreateRotationY(MathHelper.ToRadians(angles.Y))
             * Matrix.CreateRotationZ(MathHelper.ToRadians(angles.Z));
    }
}
