using System.Collections.Generic;
using UnityEngine;

namespace DiceMania.Presentation
{
    /// <summary>Procedural low-poly meshes (no imported assets needed). Index order matches ShapeId.</summary>
    public static class MeshFactory
    {
        public static Mesh Sphere()
        {
            // Icosphere, 1 subdivision, flat-ish low poly look.
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            var tris = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                verts.Add(((verts[a] + verts[b]) * 0.5f));
                cache[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            var nt = new List<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized * 0.5f;
            return Flat("DM_Sphere", verts, nt);
        }

        public static Mesh Cube()
        {
            var v = new List<Vector3>();
            var tr = new List<int>();
            Vector3[] n = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (var f in n)
            {
                Vector3 u = Mathf.Abs(f.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 r = Vector3.Cross(u, f);
                int b = v.Count;
                v.Add((f - r - u) * 0.5f); v.Add((f - r + u) * 0.5f); v.Add((f + r + u) * 0.5f); v.Add((f + r - u) * 0.5f);
                tr.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            return Flat("DM_Cube", v, tr);
        }

        public static Mesh Diamond()
        {
            var v = new List<Vector3>
            {
                new(0, 0.6f, 0), new(0, -0.6f, 0),
                new(0.45f, 0, 0), new(0, 0, 0.45f), new(-0.45f, 0, 0), new(0, 0, -0.45f),
            };
            var tr = new List<int> { 0,3,2, 0,4,3, 0,5,4, 0,2,5, 1,2,3, 1,3,4, 1,4,5, 1,5,2 };
            return Flat("DM_Diamond", v, tr);
        }

        /// <summary>Flat disc of radius 0.5 facing up.</summary>
        public static Mesh Disc(int seg = 48)
        {
            var v = new List<Vector3> { Vector3.zero };
            var tr = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.5f);
                if (i > 0) tr.AddRange(new[] { 0, i + 1, i });
            }
            var m = new Mesh { name = "DM_Disc" };
            m.SetVertices(v);
            m.SetTriangles(tr, 0);
            var normals = new Vector3[v.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            m.normals = normals;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat ring (outer radius 0.5, inner 0.44).</summary>
        public static Mesh Ring(int seg = 64, float inner = 0.44f)
        {
            var v = new List<Vector3>();
            var tr = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * 0.5f); v.Add(d * inner);
                if (i > 0)
                {
                    int b = i * 2;
                    tr.AddRange(new[] { b - 2, b - 1, b, b - 1, b + 1, b });
                }
            }
            var m = new Mesh { name = "DM_Ring" };
            m.SetVertices(v);
            m.SetTriangles(tr, 0);
            var normals = new Vector3[v.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            m.normals = normals;
            m.RecalculateBounds();
            return m;
        }

        public static Mesh Quad(float size)
        {
            var m = new Mesh { name = "DM_Ground" };
            float h = size * 0.5f;
            m.vertices = new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Splits shared vertices so every triangle gets a flat normal (low-poly look).</summary>
        static Mesh Flat(string name, List<Vector3> verts, List<int> tris)
        {
            var v = new Vector3[tris.Count];
            var nrm = new Vector3[tris.Count];
            var idx = new int[tris.Count];
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                Vector3 fn = Vector3.Cross(b - a, c - a).normalized;
                // Make sure faces point outward.
                if (Vector3.Dot(fn, (a + b + c)) < 0f) { (b, c) = (c, b); fn = -fn; }
                v[i] = a; v[i + 1] = b; v[i + 2] = c;
                nrm[i] = nrm[i + 1] = nrm[i + 2] = fn;
                idx[i] = i; idx[i + 1] = i + 1; idx[i + 2] = i + 2;
            }
            var m = new Mesh { name = name };
            m.vertices = v;
            m.normals = nrm;
            m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }
    }
}
