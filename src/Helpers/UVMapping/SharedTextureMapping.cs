using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS.Mapping;

/// <summary>Object-space box projection with a fixed number of texture pixels per world unit.</summary>
public static class SharedTextureMapping
{
    public static void Apply(Mesh mesh, float pixelsPerUnit = 32.0f)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (!float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit));
        IReadOnlyDictionary<string, float> restParameters = new Dictionary<string, float>();
        ApplyNode(mesh.Root, mesh.LocalTransform);

        void ApplyNode(MeshNode node, Matrix parent)
        {
            Matrix transform = node.GetLocalTransform(restParameters) * parent;
            Matrix normalTransform = Matrix.Transpose(Matrix.Invert(transform));
            foreach (SubMesh part in node.SubMeshes)
            {
                if (part.SharedTextureName is null || part.TextureRegion is not TextureHandler.TextureRegion region)
                    continue;
                for (int i = 0; i < part.Vertices.Length; i++)
                {
                    var vertex = part.Vertices[i];
                    Vector3 position = Vector3.Transform(vertex.Position, transform);
                    Vector3 normal = Vector3.TransformNormal(vertex.Normal, normalTransform);
                    Vector2 projected;
                    float x = Math.Abs(normal.X), y = Math.Abs(normal.Y), z = Math.Abs(normal.Z);
                    if (x >= y && x >= z)
                        projected = new(normal.X > 0 ? -position.Z : position.Z, -position.Y);
                    else if (y >= z)
                        projected = new(position.X, normal.Y > 0 ? position.Z : -position.Z);
                    else
                        projected = new(normal.Z > 0 ? -position.X : position.X, -position.Y);
                    // Keep repeats unwrapped across triangle interpolation. The shader
                    // wraps each pixel into this region, never into neighboring atlas tiles.
                    vertex.TextureCoordinate = region.RemapUV(new Vector2(
                        projected.X * pixelsPerUnit / region.Width,
                        projected.Y * pixelsPerUnit / region.Height));
                    part.Vertices[i] = vertex;
                }
                part.RepeatSharedTexture = true;
            }
            foreach (MeshNode child in node.Children)
                ApplyNode(child, transform);
        }
    }
}
