using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Generates smooth low-poly "lofted" meshes (stacked elliptical rings) for a procedural humanoid: head, torso,
    /// limbs, hands and shoes. Meshes are saved as assets and updated in place on rebuild (stable GUIDs).
    /// </summary>
    internal static class HumanoidMeshFactory
    {
        /// <summary>One cross-section: position along the loft axis, radii, and a sideways offset (Z, or Y for shoes).</summary>
        internal readonly struct Ring
        {
            public readonly float Along, RadiusX, RadiusZ, Offset;

            public Ring(float along, float radiusX, float radiusZ, float offset = 0f)
            {
                Along = along;
                RadiusX = radiusX;
                RadiusZ = radiusZ;
                Offset = offset;
            }
        }

        internal static Ring R(float along, float rx, float rz, float offset = 0f) => new Ring(along, rx, rz, offset);

        /// <summary>
        /// Builds a closed loft along +Y (or along +Z when <paramref name="alongZ"/>, used for shoes).
        /// Triangle winding is checked per triangle against the expected outward direction.
        /// </summary>
        internal static Mesh Loft(string name, IList<Ring> rings, int segments = 20, bool alongZ = false)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < rings.Count; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    float a = j * Mathf.PI * 2f / segments;
                    vertices.Add(new Vector3(Mathf.Cos(a) * rings[i].RadiusX, rings[i].Along, Mathf.Sin(a) * rings[i].RadiusZ + rings[i].Offset));
                }
            }
            int bottom = vertices.Count;
            vertices.Add(new Vector3(0f, rings[0].Along, rings[0].Offset));
            int top = vertices.Count;
            vertices.Add(new Vector3(0f, rings[rings.Count - 1].Along, rings[rings.Count - 1].Offset));

            for (int i = 0; i < rings.Count - 1; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int a = i * segments + j, b = i * segments + (j + 1) % segments;
                    int c = (i + 1) * segments + (j + 1) % segments, d = (i + 1) * segments + j;
                    AddOriented(vertices, triangles, a, b, c, SideNormal(vertices, rings[i].Offset, rings[i + 1].Offset, a, c));
                    AddOriented(vertices, triangles, a, c, d, SideNormal(vertices, rings[i].Offset, rings[i + 1].Offset, a, c));
                }
            }
            int last = (rings.Count - 1) * segments;
            for (int j = 0; j < segments; j++)
            {
                AddOriented(vertices, triangles, bottom, j, (j + 1) % segments, Vector3.down);
                AddOriented(vertices, triangles, top, last + j, last + (j + 1) % segments, Vector3.up);
            }

            if (alongZ)
            {
                // Swap Y and Z (a mirror), then reverse winding to keep faces outward.
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 v = vertices[i];
                    vertices[i] = new Vector3(v.x, v.z, v.y);
                }
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Outward direction for a side quad: radial from the loft axis (which may be offset per ring).</summary>
        private static Vector3 SideNormal(List<Vector3> v, float offsetA, float offsetB, int a, int c)
        {
            Vector3 mid = (v[a] + v[c]) * 0.5f;
            return new Vector3(mid.x, 0f, mid.z - (offsetA + offsetB) * 0.5f);
        }

        private static void AddOriented(List<Vector3> v, List<int> tris, int a, int b, int c, Vector3 expected)
        {
            Vector3 normal = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(normal, expected) < 0f)
            {
                (b, c) = (c, b);
            }
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        /// <summary>Saves the mesh at <paramref name="path"/>, updating an existing asset in place.</summary>
        internal static Mesh Save(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            existing.SetVertices(mesh.vertices);
            existing.SetTriangles(mesh.triangles, 0);
            existing.SetNormals(mesh.normals);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
    }
}
