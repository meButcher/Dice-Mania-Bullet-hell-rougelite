using System.Collections.Generic;
using UnityEngine;

namespace DiceMania.Presentation
{
    /// <summary>
    /// The HUD dice, like the original game: real 3D cubes rendered by a hidden camera into a RenderTexture that the HUD
    /// draws in the bottom-right glass circle. Each face is a tile in its ability colour with the pip number (underlined,
    /// as on the original's die). Rolling tumbles the cubes; when the result lands they settle on the rolled face with a
    /// squash and a flash. Opposite faces add up to 7 like a real die (1-6, 2-5, 3-4).
    /// </summary>
    public class DiceView : MonoBehaviour
    {
        public const int Layer = 31;
        const int MaxDice = 3, Tile = 128;
        // Dice sit 2 units apart so each one can be cut out of the texture on its own (a 1.9-unit square around it).
        const float Spacing = 2f, OrthoSize = 0.95f, RtAspect = 3f;
        static readonly Vector3 Origin = new Vector3(0f, -2000f, 0f);
        // Seen from slightly above and to the right, like a die on a table: the top and right faces show.
        static readonly Quaternion Tilt = Quaternion.Euler(-16f, 22f, 0f);

        // Slot s (pip s+1): outward normal and the face's "up" (so the number reads upright when it faces the camera).
        static readonly Vector3[] Normals = { Vector3.back, Vector3.right, Vector3.up, Vector3.down, Vector3.left, Vector3.forward };
        static readonly Vector3[] Ups = { Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up };

        public RenderTexture Texture { get; private set; }
        public int Count { get; private set; }
        public bool Rolling => rolling;

        /// <summary>The square part of the texture that shows die <paramref name="i"/> (for GUI.DrawTextureWithTexCoords).</summary>
        public Rect DieUv(int i)
        {
            float w = 1f / RtAspect;
            float u = 0.5f + DieX(i) / (OrthoSize * 2f * RtAspect);
            return new Rect(u - w * 0.5f, 0f, w, 1f);
        }

        float DieX(int i) => ((Count - 1) * 0.5f - i) * Spacing;

        class Cube
        {
            public GameObject Go;
            public Material Mat;
            public Texture2D Atlas;
            public string Key;
            public Quaternion Rot = Quaternion.identity;
            public Vector3 Spin;
            public int Slot = -1;
            public float Punch, Hop;
        }

        readonly Cube[] cubes = new Cube[MaxDice];
        bool rolling;
        float rollTime, rollDur = 0.45f;

        public static DiceView Create(Transform parent)
        {
            var go = new GameObject("DiceView");
            go.transform.SetParent(parent, false);
            return go.AddComponent<DiceView>();
        }

        void Awake()
        {
            var shader = Resources.Load<Shader>("Shaders/DMDice") ?? Shader.Find("DiceMania/Dice");
            var mesh = BuildCubeMesh();
            Texture = new RenderTexture(768, 256, 16, RenderTextureFormat.ARGB32) { name = "DM_DiceRT", antiAliasing = 4 };
            Texture.Create();

            var camGo = new GameObject("DiceCam");
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = OrthoSize;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            cam.targetTexture = Texture;
            cam.depth = -50;
            cam.allowHDR = false;
            camGo.transform.position = Origin + new Vector3(0f, 0f, -6f);
            camGo.transform.rotation = Quaternion.identity;
            var main = Camera.main;
            if (main != null) main.cullingMask &= ~(1 << Layer);

            for (int i = 0; i < MaxDice; i++)
            {
                var go = new GameObject("Die" + (i + 1)) { layer = Layer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var mat = shader != null ? new Material(shader) { name = "DM_Die" + (i + 1) } : null;
                mr.sharedMaterial = mat;
                go.SetActive(false);
                cubes[i] = new Cube { Go = go, Mat = mat, Rot = Quaternion.Euler(30f * i, 45f, 0f) };
            }
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }

        /// <summary>Match the cubes to the run's dice (rebuilds a face atlas only when that die's faces change).</summary>
        public void Sync(IList<DiceMania.Game.Die> dice)
        {
            Count = dice == null ? 0 : Mathf.Min(MaxDice, dice.Count);
            for (int i = 0; i < MaxDice; i++)
            {
                var c = cubes[i];
                bool on = i < Count;
                if (c.Go.activeSelf != on) c.Go.SetActive(on);
                if (!on) continue;
                var die = dice[i];
                string key = "";
                for (int s = 0; s < 6; s++) key += die.Slots[s].Face.Id + ";";
                if (key == c.Key) continue;
                c.Key = key;
                BuildAtlas(c, die);
            }
        }

        /// <summary>Show these slots without animation (e.g. last roll after a menu).</summary>
        public void Show(IList<int> slots)
        {
            for (int i = 0; i < MaxDice; i++) cubes[i].Slot = slots != null && i < slots.Count ? slots[i] : -1;
        }

        public void BeginRoll(IList<int> slots, float duration)
        {
            rolling = true;
            rollTime = 0f;
            rollDur = Mathf.Max(0.2f, duration);
            for (int i = 0; i < MaxDice; i++)
            {
                cubes[i].Slot = slots != null && i < slots.Count ? slots[i] : -1;
                cubes[i].Spin = Random.onUnitSphere * Random.Range(720f, 1150f);
            }
        }

        public void Land()
        {
            rolling = false;
            for (int i = 0; i < MaxDice; i++) cubes[i].Punch = 1f;
        }

        static Quaternion FaceToCamera(int slot)
        {
            var b = Quaternion.LookRotation(Normals[slot], Ups[slot]);
            var c = Quaternion.LookRotation(Vector3.back, Vector3.up);
            return Tilt * (c * Quaternion.Inverse(b));
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (rolling)
            {
                rollTime += dt;
                if (rollTime > rollDur + 0.4f) rolling = false; // safety if Land() never comes
            }
            float time = Time.unscaledTime;
            for (int i = 0; i < Count; i++)
            {
                var c = cubes[i];
                Quaternion target = c.Slot >= 0 ? FaceToCamera(c.Slot) : Quaternion.Euler(time * 35f + i * 40f, time * 55f, 0f);
                if (rolling)
                {
                    float t = Mathf.Clamp01(rollTime / rollDur);
                    c.Rot = Quaternion.Euler(c.Spin * (dt * (1f - 0.65f * t))) * c.Rot;
                    if (t > 0.6f) c.Rot = Quaternion.Slerp(c.Rot, target, (t - 0.6f) / 0.4f * 0.55f);
                    c.Hop = Mathf.Sin(t * Mathf.PI) * 0.22f;
                }
                else
                {
                    c.Rot = c.Slot >= 0 ? Quaternion.Slerp(c.Rot, target, 1f - Mathf.Exp(-16f * dt)) : target;
                    c.Hop = Mathf.MoveTowards(c.Hop, 0f, dt * 2f);
                }
                c.Punch = Mathf.MoveTowards(c.Punch, 0f, dt * 3.2f);
                float squash = Mathf.Sin(c.Punch * Mathf.PI * 3f) * c.Punch * 0.2f;
                float bob = rolling ? 0f : Mathf.Sin(time * 2.2f + i * 1.7f) * 0.025f;
                var tr = c.Go.transform;
                tr.position = Origin + new Vector3(DieX(i), c.Hop + bob - 0.02f, 0f);
                tr.rotation = c.Rot;
                float grow = rolling ? 1.08f : 1f;
                tr.localScale = new Vector3(1f + squash, 1f - squash, 1f + squash) * grow;
                if (c.Mat != null) c.Mat.SetFloat("_Flash", c.Punch * 0.55f);
            }
        }

        // ------------------------------------------------------------------ mesh + face atlas

        static Mesh BuildCubeMesh()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            for (int s = 0; s < 6; s++)
            {
                Vector3 nn = Normals[s], u = Ups[s], r = Vector3.Cross(u, -nn);
                Vector3 c = nn * 0.5f;
                int b = v.Count;
                v.Add(c + (-r - u) * 0.5f); v.Add(c + (-r + u) * 0.5f); v.Add(c + (r + u) * 0.5f); v.Add(c + (r - u) * 0.5f);
                for (int k = 0; k < 4; k++) n.Add(nn);
                float u0 = (s % 3) / 3f, v0 = (s / 3) / 2f, du = 1f / 3f, dv = 0.5f, pad = 0.004f;
                uv.Add(new Vector2(u0 + pad, v0 + pad));
                uv.Add(new Vector2(u0 + pad, v0 + dv - pad));
                uv.Add(new Vector2(u0 + du - pad, v0 + dv - pad));
                uv.Add(new Vector2(u0 + du - pad, v0 + pad));
                tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var m = new Mesh { name = "DM_DieCube" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        // 5x7 digit glyphs (top row first), drawn with round "ink" so they look smooth when scaled.
        static readonly string[] Digits =
        {
            "01110100011001110101110011000101110", "00100011000010000100001000010001110", "01110100010000100010001000100011111",
            "11110000010000101110000010000111110", "00010001100101010010111110001000010", "11111100001111000001000011000101110",
            "00110010001000011110100011000101110", "11111000010001000100010000100001000", "01110100011000101110100011000101110",
            "01110100011000101111000010001001100",
        };

        void BuildAtlas(Cube c, DiceMania.Game.Die die)
        {
            if (c.Mat == null) return;
            if (c.Atlas == null)
            {
                c.Atlas = new Texture2D(Tile * 3, Tile * 2, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "DM_DieAtlas",
                };
                c.Mat.mainTexture = c.Atlas;
            }
            int w = Tile * 3;
            var px = new Color32[w * Tile * 2];
            for (int s = 0; s < 6; s++) DrawTile(px, w, (s % 3) * Tile, (s / 3) * Tile, die.Slots[s].Face.Color, s + 1);
            c.Atlas.SetPixels32(px);
            c.Atlas.Apply(false);
        }

        /// <summary>
        /// The digit as pen strokes: each lit cell of the 5x7 glyph is joined to its lit neighbours (diagonals only where
        /// the corner is open), so the number reads as a smooth rounded line instead of a row of beads.
        /// </summary>
        static List<Vector4> Strokes(string glyph, float gx0, float gyTop, float cell)
        {
            bool On(int gx, int gy) => gx >= 0 && gx < 5 && gy >= 0 && gy < 7 && glyph[gy * 5 + gx] == '1';
            Vector2 P(int gx, int gy) => new Vector2(gx0 + (gx + 0.5f) * cell, gyTop - (gy + 0.5f) * cell);
            var list = new List<Vector4>();
            void Add(int ax, int ay, int bx, int by) { var a = P(ax, ay); var b = P(bx, by); list.Add(new Vector4(a.x, a.y, b.x, b.y)); }
            for (int gy = 0; gy < 7; gy++)
            for (int gx = 0; gx < 5; gx++)
            {
                if (!On(gx, gy)) continue;
                bool linked = false;
                if (On(gx + 1, gy)) { Add(gx, gy, gx + 1, gy); linked = true; }
                if (On(gx, gy + 1)) { Add(gx, gy, gx, gy + 1); linked = true; }
                if (On(gx + 1, gy + 1) && !On(gx + 1, gy) && !On(gx, gy + 1)) { Add(gx, gy, gx + 1, gy + 1); linked = true; }
                if (On(gx - 1, gy + 1) && !On(gx - 1, gy) && !On(gx, gy + 1)) { Add(gx, gy, gx - 1, gy + 1); linked = true; }
                if (!linked) Add(gx, gy, gx, gy); // a lone dot (or the end of a stroke drawn from above / the left)
            }
            return list;
        }

        static float SegmentDistance(float x, float y, Vector4 s)
        {
            float vx = s.z - s.x, vy = s.w - s.y, wx = x - s.x, wy = y - s.y;
            float len2 = vx * vx + vy * vy;
            float t = len2 > 0f ? Mathf.Clamp01((wx * vx + wy * vy) / len2) : 0f;
            float dx = wx - vx * t, dy = wy - vy * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static void DrawTile(Color32[] px, int stride, int ox, int oy, Color face, int number)
        {
            const float cell = 10.5f, ink = 0.56f * 10.5f;
            string glyph = Digits[Mathf.Clamp(number, 0, 9)];
            float gx0 = Tile * 0.5f - 2.5f * cell, gyTop = Tile * 0.5f + 3.5f * cell + 7f; // glyph box (texture y grows upward)
            var strokes = Strokes(glyph, gx0, gyTop, cell);
            float bx0 = gx0 - ink - 1f, bx1 = gx0 + 5f * cell + ink + 1f, by0 = gyTop - 7f * cell - ink - 1f, by1 = gyTop + ink + 1f;
            var dark = new Color(0.07f, 0.07f, 0.11f);
            for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
            {
                Color col = face;
                // Darker rim so neighbouring faces separate.
                float edge = Mathf.Min(Mathf.Min(x, Tile - 1 - x), Mathf.Min(y, Tile - 1 - y));
                if (edge < 6f) col = Color.Lerp(face * 0.55f, face, edge / 6f);
                // A faint glassy ring, like the original's die tile.
                float dx = x - Tile * 0.5f + 0.5f, dy = y - Tile * 0.5f + 0.5f;
                float rr = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(rr - Tile * 0.36f) / 2.2f);
                col = Color.Lerp(col, Color.white, ring * 0.22f);
                // Digit ink: distance to the nearest stroke, anti-aliased over one pixel.
                float cover = 0f;
                if (x >= bx0 && x <= bx1 && y >= by0 && y <= by1)
                    foreach (var s in strokes)
                        cover = Mathf.Max(cover, Mathf.Clamp01(ink - SegmentDistance(x, y, s) + 0.5f));
                // Underline.
                float ux = Mathf.Abs(x - Tile * 0.5f + 0.5f), uy = y - (gyTop - 7f * cell - 9f);
                if (ux < 25f && uy > -3.5f && uy < 3.5f) cover = Mathf.Max(cover, Mathf.Clamp01(25f - ux));
                col = Color.Lerp(col, dark, cover);
                col.a = 1f;
                px[(oy + y) * stride + ox + x] = col;
            }
        }
    }
}
