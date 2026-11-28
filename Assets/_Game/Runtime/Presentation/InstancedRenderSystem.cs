using DiceMania.Data;
using DiceMania.ECS;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace DiceMania.Presentation
{
    /// <summary>Meshes + materials for the instanced renderer. Created once by the GameDirector. Index order = ShapeId.</summary>
    public static class RenderAssets
    {
        public const int MeshCount = 8;
        public static Mesh[] Meshes;
        public static Material[] Materials;
        public static Material OpaqueMaterial, AdditiveMaterial, GlowMaterial, StreakMaterial;
        public static bool Ready;

        public static void Init()
        {
            if (Ready) return;
            var opaqueShader = Resources.Load<Shader>("Shaders/DMInstanced") ?? Shader.Find("DiceMania/Instanced");
            var addShader = Resources.Load<Shader>("Shaders/DMAdditive") ?? Shader.Find("DiceMania/Additive");
            var glowShader = Resources.Load<Shader>("Shaders/DMGlow") ?? Shader.Find("DiceMania/Glow");
            if (opaqueShader == null || addShader == null || glowShader == null)
            {
                Debug.LogError("[DiceMania] Instanced shaders not found in Resources/Shaders.");
                return;
            }
            OpaqueMaterial = new Material(opaqueShader) { enableInstancing = true, name = "DM_Opaque" };
            AdditiveMaterial = new Material(addShader) { enableInstancing = true, name = "DM_Additive" };
            GlowMaterial = new Material(glowShader) { enableInstancing = true, name = "DM_Glow" };
            GlowMaterial.SetFloat("_Streak", 0f);
            StreakMaterial = new Material(glowShader) { enableInstancing = true, name = "DM_Streak" };
            StreakMaterial.SetFloat("_Streak", 1f);
            Meshes = new[]
            {
                MeshFactory.Sphere(), MeshFactory.Cube(), MeshFactory.Diamond(), MeshFactory.Disc(), MeshFactory.Ring(),
                MeshFactory.Disc(24), MeshFactory.Quad(1f, "DM_Streak"), MeshFactory.Splat(),
            };
            Materials = new[]
            {
                OpaqueMaterial, OpaqueMaterial, OpaqueMaterial, AdditiveMaterial, AdditiveMaterial,
                GlowMaterial, StreakMaterial, OpaqueMaterial,
            };
            Ready = true;
        }
    }

    /// <summary>
    /// Draws every entity with Pos + Visual using Graphics.RenderMeshInstanced (works on WebGL2, unlike Entities Graphics).
    /// A Burst job counting-sorts instances by mesh and builds matrices + colours (applying hit flash and squash wobble,
    /// and adding glow instances for trails and halos); we then submit batches.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class InstancedRenderSystem : SystemBase
    {
        const int Batch = 511;
        EntityQuery query;

        /// <summary>Invoked right after this frame's instanced draws are submitted (tests use it to render captures).</summary>
        public static System.Action AfterDraw;

        readonly Vector4[] colorBuf = new Vector4[Batch];
        MaterialPropertyBlock[] blocks;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<Pos>(), ComponentType.ReadOnly<Visual>());
            blocks = new MaterialPropertyBlock[64];
            for (int i = 0; i < blocks.Length; i++) blocks[i] = new MaterialPropertyBlock();
        }

        [BurstCompile]
        struct BuildJob : IJob
        {
            [ReadOnly] public NativeArray<Pos> Pos;
            [ReadOnly] public NativeArray<Visual> Vis;
            public NativeArray<int> Offsets;       // MeshCount + 1
            public NativeArray<Matrix4x4> Matrices;
            public NativeArray<Vector4> Colors;

            public void Execute()
            {
                int meshCount = Offsets.Length - 1;
                const int streak = (int)ShapeId.Streak, glow = (int)ShapeId.Glow;
                var counts = new NativeArray<int>(meshCount, Allocator.Temp);
                for (int i = 0; i < Vis.Length; i++)
                {
                    var v = Vis[i];
                    if (v.Mesh < meshCount) counts[v.Mesh]++;
                    if ((v.Fx & VisualFx.Trail) != 0 && v.TrailLen > 0.05f) counts[streak]++;
                    if ((v.Fx & VisualFx.Halo) != 0) counts[glow]++;
                }
                int acc = 0;
                for (int m = 0; m < meshCount; m++) { Offsets[m] = acc; acc += counts[m]; counts[m] = 0; }
                Offsets[meshCount] = acc;

                for (int i = 0; i < Vis.Length; i++)
                {
                    var v = Vis[i];
                    float2 p = Pos[i].Value;

                    // Squash & stretch: a damped jelly wobble started by hits / bounces.
                    float3 scale = v.Scale;
                    if (v.SquashAmp > 0f)
                    {
                        float w = v.SquashAmp * math.exp(-9f * v.SquashT) * math.cos(26f * v.SquashT);
                        scale.y *= 1f - 0.45f * w;
                        scale.x *= 1f + 0.3f * w;
                        scale.z *= 1f + 0.3f * w;
                    }

                    // Hit flash: a crisp white frame, then a fast fade back to the colour.
                    float4 c = v.Color;
                    if (v.Flash > 0f)
                    {
                        float f = math.saturate(v.Flash * 1.5f);
                        c.xyz = math.lerp(c.xyz, new float3(1f), f);
                        c.w = math.max(c.w, f);
                    }

                    if (v.Mesh < meshCount)
                    {
                        int idx = Offsets[v.Mesh] + counts[v.Mesh]++;
                        Matrices[idx] = float4x4.TRS(new float3(p.x, v.Y, p.y), quaternion.RotateY(v.RotY), scale);
                        Colors[idx] = c;
                    }
                    if ((v.Fx & VisualFx.Trail) != 0 && v.TrailLen > 0.05f)
                    {
                        float2 d = new float2(math.cos(v.Heading), -math.sin(v.Heading));
                        float2 center = p - d * (v.TrailLen * 0.5f);
                        float width = math.max(0.12f, math.max(scale.x, scale.z) * 0.9f);
                        int idx = Offsets[streak] + counts[streak]++;
                        Matrices[idx] = float4x4.TRS(new float3(center.x, v.Y, center.y), quaternion.RotateY(v.Heading),
                            new float3(v.TrailLen, 1f, width));
                        Colors[idx] = new float4(math.lerp(v.Color.xyz, new float3(1f), 0.25f), 0.9f);
                    }
                    if ((v.Fx & VisualFx.Halo) != 0)
                    {
                        float hs = math.max(0.5f, math.cmax(scale) * 2.6f);
                        int idx = Offsets[glow] + counts[glow]++;
                        Matrices[idx] = float4x4.TRS(new float3(p.x, v.Y, p.y), quaternion.identity, new float3(hs, 1f, hs));
                        Colors[idx] = new float4(c.xyz, 0.5f);
                    }
                }
            }
        }

        protected override void OnUpdate()
        {
            if (!RenderAssets.Ready) return;
            var pos = query.ToComponentDataArray<Pos>(Allocator.TempJob);
            var vis = query.ToComponentDataArray<Visual>(Allocator.TempJob);
            int n = pos.Length;
            int cap = math.max(1, n * 3);
            var offsets = new NativeArray<int>(RenderAssets.MeshCount + 1, Allocator.TempJob);
            var mats = new NativeArray<Matrix4x4>(cap, Allocator.TempJob);
            var cols = new NativeArray<Vector4>(cap, Allocator.TempJob);
            new BuildJob { Pos = pos, Vis = vis, Offsets = offsets, Matrices = mats, Colors = cols }.Run();

            var bounds = new Bounds(Vector3.zero, new Vector3(400f, 60f, 400f));
            int blockIdx = 0;
            for (int m = 0; m < RenderAssets.MeshCount; m++)
            {
                int start = offsets[m], end = offsets[m + 1];
                for (int s = start; s < end; s += Batch)
                {
                    int count = math.min(Batch, end - s);
                    NativeArray<Vector4>.Copy(cols, s, colorBuf, 0, count);
                    if (blockIdx >= blocks.Length)
                    {
                        System.Array.Resize(ref blocks, blocks.Length * 2);
                        for (int i = blockIdx; i < blocks.Length; i++) blocks[i] = new MaterialPropertyBlock();
                    }
                    var mpb = blocks[blockIdx++];
                    mpb.SetVectorArray(ColorId, colorBuf);
                    var rp = new RenderParams(RenderAssets.Materials[m])
                    {
                        matProps = mpb,
                        worldBounds = bounds,
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false,
                    };
                    Graphics.RenderMeshInstanced(rp, RenderAssets.Meshes[m], 0, mats, count, s);
                }
            }

            pos.Dispose(); vis.Dispose(); offsets.Dispose(); mats.Dispose(); cols.Dispose();
            AfterDraw?.Invoke();
        }
    }
}
