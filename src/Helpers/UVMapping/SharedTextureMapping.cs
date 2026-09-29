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
                // Projection is a face property. Smooth-shaded meshes deliberately
                // have different vertex normals on one polygon; using those normals
                // can project the corners of one triangle onto different cube sides.
                for (int triangle = 0; triangle + 2 < part.Indices.Length; triangle += 3)
                {
                    int first = part.Indices[triangle];
                    int second = part.Indices[triangle + 1];
                    int third = part.Indices[triangle + 2];
                    Vector3 a = Vector3.Transform(part.Vertices[first].Position, transform);
                    Vector3 b = Vector3.Transform(part.Vertices[second].Position, transform);
                    Vector3 c = Vector3.Transform(part.Vertices[third].Position, transform);
                    Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                    if (faceNormal.LengthSquared() <= 0.0000001f) continue;
                    faceNormal.Normalize();
                    Vector3 lightingDirection =
                        Vector3.TransformNormal(part.Vertices[first].Normal, normalTransform) +
                        Vector3.TransformNormal(part.Vertices[second].Normal, normalTransform) +
                        Vector3.TransformNormal(part.Vertices[third].Normal, normalTransform);
                    if (Vector3.Dot(faceNormal, lightingDirection) < 0) faceNormal = -faceNormal;
                    MapVertex(first, a, faceNormal);
                    MapVertex(second, b, faceNormal);
                    MapVertex(third, c, faceNormal);
                }
                part.RepeatSharedTexture = true;

                void MapVertex(int index, Vector3 position, Vector3 faceNormal)
                {
                    VertexPositionColorNormalTexture vertex = part.Vertices[index];
                    Vector2 projected;
                    float x = Math.Abs(faceNormal.X), y = Math.Abs(faceNormal.Y), z = Math.Abs(faceNormal.Z);
                    if (x >= y && x >= z)
                        projected = new(faceNormal.X > 0 ? -position.Z : position.Z, -position.Y);
                    else if (y >= z)
                        projected = new(position.X, faceNormal.Y > 0 ? position.Z : -position.Z);
                    else
                        projected = new(faceNormal.Z > 0 ? -position.X : position.X, -position.Y);
                    // Keep repeats unwrapped across triangle interpolation. The shader
                    // wraps each pixel into this region, never into neighboring atlas tiles.
                    vertex.TextureCoordinate = region.RemapUV(new Vector2(
                        projected.X * pixelsPerUnit / region.Width,
                        projected.Y * pixelsPerUnit / region.Height));
                    part.Vertices[index] = vertex;
                }
            }
            foreach (MeshNode child in node.Children)
                ApplyNode(child, transform);
        }
    }
}
