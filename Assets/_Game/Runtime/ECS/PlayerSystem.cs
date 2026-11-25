using DiceMania.Data;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>Moves the player, handles regen/invulnerability and the Copy transformation (look + borrowed attack).</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct PlayerSystem : ISystem
    {
        Random rng;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameClock>();
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<Prefabs>();
            rng = Random.CreateFromIndex(1234);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<GameClock>();
            if (clock.Running == 0) return;
            float dt = SystemAPI.Time.DeltaTime;
            var stats = SystemAPI.GetSingleton<PlayerStats>();
            var buffs = SystemAPI.GetSingleton<Buffs>();
            var pf = SystemAPI.GetSingleton<Prefabs>();
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (pd, pos, vis, hp, copy) in SystemAPI.Query<RefRW<PlayerData>, RefRW<Pos>, RefRW<Visual>, RefRW<Health>, RefRW<CopyForm>>()
                         .WithAll<PlayerTag>())
            {
                ref var c = ref copy.ValueRW;
                bool copied = c.Time > 0f;

                float speed = pd.ValueRO.Speed * (copied ? c.SpeedMult : 1f);
                float2 move = pd.ValueRO.MoveInput;
                float ml = math.length(move);
                if (ml > 1f) move /= ml;
                float2 p = pos.ValueRO.Value + move * speed * dt;
                float lim = clock.ArenaHalf - 1f;
                pos.ValueRW.Value = math.clamp(p, -lim, lim);

                pd.ValueRW.InvulnTimer = math.max(0f, pd.ValueRO.InvulnTimer - dt);

                if (stats.Regen > 0f && hp.ValueRO.Value > 0f)
                    hp.ValueRW.Value = math.min(hp.ValueRO.Max, hp.ValueRO.Value + stats.Regen * dt);

                ref var v = ref vis.ValueRW;
                if (copied)
                {
                    c.Time = math.max(0f, c.Time - dt);
                    v.Mesh = c.Mesh;
                    v.Scale = new float3(c.Size);
                    v.Y = c.Size * 0.5f;
                    // Blink during the last 2 seconds as a warning.
                    bool blink = c.Time < 2f && math.frac(c.Time * 4f) < 0.5f;
                    v.Color = blink ? pd.ValueRO.BaseColor : c.Color;
                    v.Spin = c.Mesh == (byte)ShapeId.Cube ? 1.5f : 0f;

                    if (c.Gun.Pattern != (byte)PatternType.None)
                    {
                        if (SpawnUtil.TickShooter(ref c.Gun, dt, true))
                        {
                            var tpl = new Projectile
                            {
                                Kind = ProjKind.Normal, Life = 2.2f, Pierce = 1, Knockback = 0.8f,
                            };
                            c.Gun.Damage = c.BulletDamage * stats.Might;
                            c.Gun.BulletSpeed = c.BulletSpeed * stats.ProjSpeed;
                            SpawnUtil.FireVolley(ecb, pf, ref c.Gun, pos.ValueRO.Value, pd.ValueRO.AimDir, true, 0f,
                                new float4(c.Color.xyz, 0.9f), 0.35f, ref rng, tpl);
                        }
                    }
                }
                else
                {
                    v.Mesh = (byte)ShapeId.Sphere;
                    v.Scale = new float3(clock.PlayerRadius * 2f);
                    v.Y = clock.PlayerRadius;
                    v.Spin = 0f;
                    bool invuln = pd.ValueRO.InvulnTimer > 0f || buffs.Invuln > 0f;
                    bool blink = pd.ValueRO.InvulnTimer > 0f && math.frac(pd.ValueRO.InvulnTimer * 10f) < 0.5f;
                    float4 col = pd.ValueRO.BaseColor;
                    if (buffs.Invuln > 0f) col = new float4(1f, 0.85f, 0.4f, 1f);
                    v.Color = blink ? new float4(1, 1, 1, 1) : col;
                    if (!invuln) v.Color.w = 0.5f;
                }
            }
        }
    }
}
