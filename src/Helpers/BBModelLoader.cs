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
    private sealed record ImportedTexture(
        TextureHandler.TextureRegion Visible,
        TextureHandler.TextureRegion? MaterialMask);

    public static Mesh Load(string path, Color? color = null)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        Color vertexColor = color ?? Color.SteelBlue;
        (float Width, float Height) resolution = ReadResolution(root);
        Dictionary<int, ImportedTexture> textureRegions = ReadTextureRegions(root, path);

        Dictionary<string, SubMesh> elementsByUuid = [];
        if (root.TryGetProperty("elements", out JsonElement elements))
        {
            foreach (JsonElement element in elements.EnumerateArray())
            {
                if (!element.TryGetProperty("type", out JsonElement type) || type.GetString() != "mesh")
                    continue;
                elementsByUuid[element.GetProperty("uuid").GetString()!] = LoadMeshElement(
                    element, vertexColor, resolution, textureRegions);
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
        if (!isGroup || !node.Name.Contains("wheel", StringComparison.OrdinalIgnoreCase))
            return;

        node.RotationParameter = Mesh.WheelAngle;
        node.RotationAxis = Vector3.Right;
    }

    private static (float Width, float Height) ReadResolution(JsonElement root)
    {
        // Blockbench's root resolution is not always the UV canvas actually
        // used by an embedded texture. Workshop models often retain 16x16
        // here while their texture declares uv_width/uv_height = 128.
        if (root.TryGetProperty("textures", out JsonElement textures))
        {
            foreach (JsonElement texture in textures.EnumerateArray())
            {
                if (texture.TryGetProperty("uv_width", out JsonElement uvWidth) &&
                    texture.TryGetProperty("uv_height", out JsonElement uvHeight) &&
                    uvWidth.TryGetSingle(out float width) && uvHeight.TryGetSingle(out float height) &&
                    width > 0.0f && height > 0.0f)
                    return (width, height);
            }
        }

        if (root.TryGetProperty("resolution", out JsonElement resolution))
        {
            float width = resolution.GetProperty("width").GetSingle();
            float height = resolution.GetProperty("height").GetSingle();
            if (width > 0 && height > 0)
                return (width, height);
        }
        return (32f, 32f);
    }

    private static SubMesh LoadMeshElement(
        JsonElement element,
        Color vertexColor,
        (float Width, float Height) resolution,
        IReadOnlyDictionary<int, ImportedTexture> textureRegions)
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
        int? textureAtlasIndex = null;
        ImportedTexture? subMeshTexture = null;

        foreach (JsonProperty face in element.GetProperty("faces").EnumerateObject())
        {
            string[] faceVertexKeys = face.Value.GetProperty("vertices").EnumerateArray()
                .Select(item => item.GetString()!).ToArray();
            if (faceVertexKeys.Length < 3)
                continue;

            Vector3[] facePositions = faceVertexKeys.Select(key => positions[key]).ToArray();
            ImportedTexture? importedTexture = ReadFaceTextureRegion(face.Value, textureRegions);
            TextureHandler.TextureRegion? textureRegion = importedTexture?.Visible;
            subMeshTexture ??= importedTexture;
            if (textureRegion is not null)
            {
                if (textureAtlasIndex is int existingAtlas && existingAtlas != textureRegion.AtlasIndex)
                    throw new InvalidDataException(
                        $"Mesh element '{name}' uses textures from multiple atlases. " +
                        "Split it into separate Blockbench mesh elements.");
                textureAtlasIndex = textureRegion.AtlasIndex;
            }
            Vector2[] faceUvs = ReadFaceUv(face.Value, faceVertexKeys, resolution, textureRegion);

            for (int index = 1; index < facePositions.Length - 1; index++)
            {
                Vector3 a = facePositions[0], b = facePositions[index], c = facePositions[index + 1];
                // The reversed submitted winding below compensates for the
                // rasterizer convention. The Blockbench order itself still
                // describes the outward-facing surface normal used by lighting.
                Vector3 normal = Vector3.Cross(b - a, c - a);
                normal = normal.LengthSquared() > 0.0f ? Vector3.Normalize(normal) : Vector3.Up;

                int first = vertices.Count;
                vertices.Add(new(b * ModelScale, vertexColor, normal, faceUvs[index]));
                vertices.Add(new(a * ModelScale, vertexColor, normal, faceUvs[0]));
                vertices.Add(new(c * ModelScale, vertexColor, normal, faceUvs[index + 1]));
                indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
            }
        }

        SubMesh submesh = new SubMesh(
            name, vertices.ToArray(), indices.ToArray(), pivot, textureAtlasIndex,
            textureRegion: subMeshTexture?.Visible,
            materialMaskRegion: subMeshTexture?.MaterialMask);
        return submesh;
    }

    private static Vector2[] ReadFaceUv(
        JsonElement face,
        string[] faceVertexKeys,
        (float Width, float Height) resolution,
        TextureHandler.TextureRegion? textureRegion)
    {
        if (!face.TryGetProperty("uv", out JsonElement uv))
            return faceVertexKeys.Select(_ => Vector2.Zero).ToArray();

        return faceVertexKeys.Select(key =>
        {
            if (!uv.TryGetProperty(key, out JsonElement uvValue))
                return Vector2.Zero;
            float u = uvValue[0].GetSingle();
            float v = uvValue[1].GetSingle();
            Vector2 sourceUv = new(u / resolution.Width, v / resolution.Height);
            return textureRegion?.RemapUV(sourceUv) ?? sourceUv;
        }).ToArray();
    }

    private static Dictionary<int, ImportedTexture> ReadTextureRegions(JsonElement root, string modelPath)
    {
        Dictionary<int, ImportedTexture> regions = [];
        if (!root.TryGetProperty("textures", out JsonElement textures))
            return regions;

        string modelDirectory = Path.GetDirectoryName(Path.GetFullPath(modelPath))!;
        int index = 0;
        foreach (JsonElement texture in textures.EnumerateArray())
        {
            string? relativePath = texture.TryGetProperty("relative_path", out JsonElement relativePathElement)
                ? relativePathElement.GetString()
                : null;
            string? name = texture.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString()
                : null;
            string? textureFileName = string.IsNullOrWhiteSpace(relativePath) ? name : relativePath;
            if (string.IsNullOrWhiteSpace(textureFileName))
            {
                index++;
                continue;
            }

            string texturePath = Path.GetFullPath(Path.Combine(modelDirectory, textureFileName));
            string? embeddedSource = texture.TryGetProperty("source", out JsonElement sourceElement)
                ? sourceElement.GetString()
                : null;
            bool hasEmbeddedSource = !string.IsNullOrWhiteSpace(embeddedSource) &&
                embeddedSource.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);

            if (!hasEmbeddedSource && !File.Exists(texturePath))
                throw new FileNotFoundException(
                    $"BBModel texture '{textureFileName}' is neither embedded nor found next to '{Path.GetFileName(modelPath)}'.",
                    texturePath);

            TextureHandler.TextureRegion region;
            if (hasEmbeddedSource)
            {
                // A stable key prevents the same embedded image from occupying
                // the atlas again when a BBModel is loaded more than once.
                string embeddedKey = $"bbmodel:{Path.GetFullPath(modelPath)}:texture:{index}";
                region = Globals.TextureHandler.TryGetTextureRegion(embeddedKey, out TextureHandler.TextureRegion existing)
                    ? existing
                    : Globals.TextureHandler.AddTextureFromDataUri(embeddedKey, embeddedSource!);
            }
            else
            {
                region = Globals.TextureHandler.TryGetTextureRegion(texturePath, out TextureHandler.TextureRegion existing)
                    ? existing
                    : Globals.TextureHandler.AddTexture(texturePath);
            }
            string maskPath = Path.Combine(
                Path.GetDirectoryName(texturePath)!,
                $"{Path.GetFileNameWithoutExtension(texturePath)}-MaterialMask.png");
            TextureHandler.TextureRegion? mask = File.Exists(maskPath)
                ? (Globals.TextureHandler.TryGetTextureRegion(maskPath, out TextureHandler.TextureRegion existingMask)
                    ? existingMask
                    : Globals.TextureHandler.AddTexture(maskPath))
                : null;
            ImportedTexture importedTexture = new(region, mask);
            regions[index] = importedTexture;

            if (texture.TryGetProperty("id", out JsonElement idElement) &&
                int.TryParse(idElement.GetString(), out int id))
                regions[id] = importedTexture;
            index++;
        }
        return regions;
    }

    private static ImportedTexture? ReadFaceTextureRegion(
        JsonElement face,
        IReadOnlyDictionary<int, ImportedTexture> textureRegions)
    {
        if (!face.TryGetProperty("texture", out JsonElement texture) ||
            texture.ValueKind != JsonValueKind.Number ||
            !texture.TryGetInt32(out int textureIndex))
            return null;

        return textureRegions.TryGetValue(textureIndex, out ImportedTexture? region)
            ? region
            : null;
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
