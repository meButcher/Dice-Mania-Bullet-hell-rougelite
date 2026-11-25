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
    /// <summary>Meshes + materials for the instanced renderer. Created once by the GameDirector.</summary>
    public static class RenderAssets
    {
        public const int MeshCount = 5;
        public static Mesh[] Meshes;
        public static Material[] Materials;
        public static Material OpaqueMaterial, AdditiveMaterial;
        public static bool Ready;

        public static void Init()
        {
            if (Ready) return;
            var opaqueShader = Resources.Load<Shader>("Shaders/DMInstanced") ?? Shader.Find("DiceMania/Instanced");
            var addShader = Resources.Load<Shader>("Shaders/DMAdditive") ?? Shader.Find("DiceMania/Additive");
            if (opaqueShader == null || addShader == null)
            {
                Debug.LogError("[DiceMania] Instanced shaders not found in Resources/Shaders.");
                return;
            }
            OpaqueMaterial = new Material(opaqueShader) { enableGPUInstancing = true, name = "DM_Opaque" };
            AdditiveMaterial = new Material(addShader) { enableGPUInstancing = true, name = "DM_Additive" };
            Meshes = new[] { MeshFactory.Sphere(), MeshFactory.Cube(), MeshFactory.Diamond(), MeshFactory.Disc(), MeshFactory.Ring() };
            Materials = new[] { OpaqueMaterial, OpaqueMaterial, OpaqueMaterial, AdditiveMaterial, AdditiveMaterial };
            Ready = true;
        }
    }

    /// <summary>
    /// Draws every entity with Pos + Visual using Graphics.RenderMeshInstanced (works on WebGL2, unlike Entities Graphics).
    /// A Burst job counting-sorts instances by mesh and builds matrices + colours; we then submit batches.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class InstancedRenderSystem : SystemBase
    {
        const int Batch = 511;
        EntityQuery query;
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
                var counts = new NativeArray<int>(meshCount, Allocator.Temp);
                for (int i = 0; i < Vis.Length; i++)
                {
                    int m = Vis[i].Mesh;
                    if (m < meshCount) counts[m]++;
                }
                int acc = 0;
                for (int m = 0; m < meshCount; m++) { Offsets[m] = acc; acc += counts[m]; counts[m] = 0; }
                Offsets[meshCount] = acc;
                for (int i = 0; i < Vis.Length; i++)
                {
                    var v = Vis[i];
                    int m = v.Mesh;
                    if (m >= meshCount) continue;
                    int idx = Offsets[m] + counts[m]++;
                    float2 p = Pos[i].Value;
                    var trs = float4x4.TRS(new float3(p.x, v.Y, p.y), quaternion.RotateY(v.RotY), v.Scale);
                    Matrices[idx] = trs;
                    float4 c = v.Color;
                    if (v.Flash > 0f) { c.xyz = math.lerp(c.xyz, new float3(1f), v.Flash); c.w = math.max(c.w, v.Flash); }
                    Colors[idx] = c;
                }
            }
        }

        protected override void OnUpdate()
        {
            if (!RenderAssets.Ready) return;
            var pos = query.ToComponentDataArray<Pos>(Allocator.TempJob);
            var vis = query.ToComponentDataArray<Visual>(Allocator.TempJob);
            int n = pos.Length;
            var offsets = new NativeArray<int>(RenderAssets.MeshCount + 1, Allocator.TempJob);
            var mats = new NativeArray<Matrix4x4>(math.max(1, n), Allocator.TempJob);
            var cols = new NativeArray<Vector4>(math.max(1, n), Allocator.TempJob);
            new BuildJob { Pos = pos, Vis = vis, Offsets = offsets, Matrices = mats, Colors = cols }.Run();

            var bounds = new Bounds(Vector3.zero, new Vector3(400f, 50f, 400f));
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
        }
    }
}
