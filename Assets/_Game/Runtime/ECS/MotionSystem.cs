using DiceMania.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>
    /// Integrates projectiles, bullets, particles and pickups; ticks buffs and visual effects
    /// (hit flash, squash wobble, spin, trails, particle physics: arcs, bounces, freeze-then-throw, pop).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EnemySystem))]
    [BurstCompile]
    public partial struct MotionSystem : ISystem
    {
        EntityQuery shotQ, enemyQ;
        Random rng;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameClock>();
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<Prefabs>();
            shotQ = SystemAPI.QueryBuilder().WithAllRW<Projectile, Pos>().WithAllRW<Vel, Visual>().Build();
            enemyQ = SystemAPI.QueryBuilder().WithAll<Enemy, Pos, Health>().Build();
            rng = Random.CreateFromIndex(31337);
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float t = x - 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clockRW = SystemAPI.GetSingletonRW<GameClock>();
            var clock = clockRW.ValueRO;
            if (clock.Running == 0) return;
            float dt = SystemAPI.Time.DeltaTime;
            clockRW.ValueRW.Time += dt;

            var pf = SystemAPI.GetSingleton<Prefabs>();
            var stats = SystemAPI.GetSingleton<PlayerStats>();
            var buffsRW = SystemAPI.GetSingletonRW<Buffs>();
            var events = SystemAPI.GetSingletonRW<GameEvents>();
            var sfx = SystemAPI.GetSingletonBuffer<SfxEvent>();
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            float2 ppos = SystemAPI.GetComponent<Pos>(player).Value;
            float lim = clock.ArenaHalf + 4f;

            // ---------------- Player projectiles ----------------
            var ents = shotQ.ToEntityArray(Allocator.Temp);
            var proj = shotQ.ToComponentDataArray<Projectile>(Allocator.Temp);
            var pos = shotQ.ToComponentDataArray<Pos>(Allocator.Temp);
            var vel = shotQ.ToComponentDataArray<Vel>(Allocator.Temp);
            var vis = shotQ.ToComponentDataArray<Visual>(Allocator.Temp);
            NativeArray<Pos> ep = default;
            NativeArray<Health> eh = default;
            bool haveEnemies = false;

            for (int i = 0; i < ents.Length; i++)
            {
                var p = proj[i];
                if (p.Kind == ProjKind.Orbit) continue; // positioned by WeaponSystem
                float2 v = vel[i].Value;

                if (p.Kind == ProjKind.Copy)
                {
                    if (p.Target != Entity.Null && SystemAPI.Exists(p.Target) && SystemAPI.HasComponent<Pos>(p.Target)
                        && SystemAPI.GetComponent<Health>(p.Target).Value > 0f)
                    {
                        float2 to = SystemAPI.GetComponent<Pos>(p.Target).Value - pos[i].Value;
                        v = SpawnUtil.SafeNormalize(to, v) * p.Speed;
                    }
                    else p.Target = Entity.Null;
                }
                else if (p.Homing != 0)
                {
                    if (!haveEnemies)
                    {
                        ep = enemyQ.ToComponentDataArray<Pos>(Allocator.Temp);
                        eh = enemyQ.ToComponentDataArray<Health>(Allocator.Temp);
                        haveEnemies = true;
                    }
                    int best = -1; float bd = 144f;
                    for (int k = 0; k < ep.Length; k++)
                    {
                        if (eh[k].Value <= 0f) continue;
                        float d = math.distancesq(ep[k].Value, pos[i].Value);
                        if (d < bd) { bd = d; best = k; }
                    }
                    if (best >= 0)
                    {
                        float2 want = SpawnUtil.SafeNormalize(ep[best].Value - pos[i].Value, v);
                        float2 cur = SpawnUtil.SafeNormalize(v, want);
                        float2 nd = SpawnUtil.SafeNormalize(math.lerp(cur, want, math.saturate(p.TurnRate * dt)), cur);
                        v = nd * p.Speed;
                    }
                }

                float2 np = pos[i].Value + v * dt;
                p.Life -= dt;
                p.HitCooldown -= dt;
                var vi = vis[i];
                float speed = math.length(v);
                if (speed > 1e-3f)
                {
                    vi.Heading = -math.atan2(v.y, v.x);
                    if (vi.Spin == 0f) vi.RotY = vi.Heading;
                }
                vi.TrailLen = math.min(1.8f, speed * 0.05f);

                if (p.Life <= 0f || math.any(math.abs(np) > lim))
                {
                    if (p.Kind == ProjKind.Explode)
                        SpawnExplosion(ecb, pf.Zone, np, p.Damage, p.Area * stats.Area, p.Knockback);
                    if (p.Kind == ProjKind.Copy) events.ValueRW.CopyMiss = 1;
                    p.Life = 0f;
                    ecb.DestroyEntity(ents[i]);
                }
                proj[i] = p;
                pos[i] = new Pos { Value = np };
                vel[i] = new Vel { Value = v };
                vis[i] = vi;
            }
            shotQ.CopyFromComponentDataArray(proj);
            shotQ.CopyFromComponentDataArray(pos);
            shotQ.CopyFromComponentDataArray(vel);
            shotQ.CopyFromComponentDataArray(vis);
            if (haveEnemies) { ep.Dispose(); eh.Dispose(); }

            // ---------------- Enemy bullets ----------------
            foreach (var (shot, sp, sv, e) in SystemAPI.Query<RefRW<EnemyShot>, RefRW<Pos>, RefRO<Vel>>().WithEntityAccess())
            {
                sp.ValueRW.Value += sv.ValueRO.Value * dt;
                shot.ValueRW.Life -= dt;
                if (shot.ValueRO.Life <= 0f || math.any(math.abs(sp.ValueRO.Value) > lim))
                {
                    shot.ValueRW.Life = 0f;
                    ecb.DestroyEntity(e);
                }
            }

            // ---------------- Particles ----------------
            bool clinked = false, shattered = false;
            int popBudget = 24;
            foreach (var (pt, pp, pv, pvis, e) in SystemAPI.Query<RefRW<Particle>, RefRW<Pos>, RefRW<Vel>, RefRW<Visual>>().WithEntityAccess())
            {
                ref var p = ref pt.ValueRW;
                ref var v = ref pvis.ValueRW;
                ushort f = p.Flags;

                // The "beat": hold still as a white silhouette, then go.
                if (p.Freeze > 0f)
                {
                    p.Freeze -= dt;
                    v.Flash = 1f;
                    if ((f & PFlags.Hidden) != 0) v.Scale = float3.zero;
                    else if (p.Freeze <= 0f) { v.SquashAmp = 0.9f; v.SquashT = 0f; }
                    continue;
                }

                p.Age += dt;
                p.Life -= dt;
                float2 velo = pv.ValueRO.Value;
                if (p.Drag > 0f) velo *= math.exp(-p.Drag * dt);
                float2 at = pp.ValueRO.Value + velo * dt;

                if (p.Gravity != 0f || p.VelY != 0f)
                {
                    p.VelY -= p.Gravity * dt;
                    v.Y += p.VelY * dt;
                    float ground = v.Mesh <= (byte)ShapeId.Diamond
                        ? math.max(0.03f, v.Scale.y * (v.Mesh == (byte)ShapeId.Diamond ? 0.6f : 0.5f))
                        : 0.05f;
                    if (v.Y <= ground && p.VelY < 0f)
                    {
                        v.Y = ground;
                        float impact = -p.VelY;
                        if ((f & PFlags.Clink) != 0 && impact > 3f) clinked = true;
                        if ((f & PFlags.Land) != 0 || impact * p.Bounce < 1.2f)
                        {
                            p.VelY = 0f;
                            p.Gravity = 0f;
                            velo = (f & PFlags.Land) != 0 ? float2.zero : velo * 0.4f;
                            p.Drag = math.max(p.Drag, 5f);
                            v.Spin = 0f;
                        }
                        else
                        {
                            p.VelY = impact * p.Bounce;
                            velo *= 0.6f;
                            v.Spin *= 0.5f;
                            v.SquashAmp = math.max(v.SquashAmp, 0.5f);
                            v.SquashT = 0f;
                        }
                    }
                }

                float k = math.saturate(p.Life / math.max(0.01f, p.MaxLife));
                float s = 1f;
                if ((f & PFlags.Shrink) != 0) s *= k;
                if ((f & PFlags.ShrinkLate) != 0) s *= math.saturate(p.Life / 0.4f);
                if ((f & PFlags.PopIn) != 0) s *= EaseOutBack(math.saturate(p.Age / 0.09f));
                float3 sc = p.BaseScale * s;
                if ((f & PFlags.Grow) != 0)
                {
                    float u = 1f - k;
                    float g = math.lerp(1f, p.GrowTo, 1f - (1f - u) * (1f - u) * (1f - u));
                    sc.x *= g; sc.z *= g;
                    if (v.Mesh <= (byte)ShapeId.Diamond) sc.y *= g;
                }
                if ((f & PFlags.Streak) != 0)
                {
                    float spd = math.length(velo);
                    if (spd > 0.05f) v.RotY = -math.atan2(velo.y, velo.x);
                    sc.x *= 1f + spd * 0.06f;
                }
                if ((f & PFlags.Fade) != 0) v.Color.w = p.Alpha0 * k;
                v.Scale = sc;

                if (p.Life <= 0f)
                {
                    if ((f & PFlags.PopOut) != 0 && popBudget-- > 0)
                    {
                        float size = math.clamp(math.cmax(p.BaseScale) * 0.25f, 0.12f, 0.6f);
                        float y = math.max(0.3f, v.Y);
                        Fx.Debris(ecb, pf.Particle, at, y, 5, v.Color, 6f, size, 0.45f, ref rng);
                        Fx.Glow(ecb, pf.Particle, at, y, math.cmax(p.BaseScale) * 1.6f,
                            new float4(math.lerp(v.Color.xyz, new float3(1f), 0.5f), 0.7f), 0.1f);
                        if ((f & PFlags.Ice) != 0) shattered = true;
                    }
                    ecb.DestroyEntity(e);
                }
                pv.ValueRW.Value = velo;
                pp.ValueRW.Value = at;
            }
            if (clinked) sfx.Add(new SfxEvent { Id = Sfx.Shell });
            if (shattered) sfx.Add(new SfxEvent { Id = Sfx.Shatter });

            // ---------------- Pickups (magnet, chest wobble) ----------------
            byte pulse = buffsRW.ValueRO.MagnetPulse;
            float r2 = stats.PickupRadius * stats.PickupRadius;
            foreach (var (pk, pp, pv, pvis) in SystemAPI.Query<RefRW<Pickup>, RefRW<Pos>, RefRW<Vel>, RefRW<Visual>>())
            {
                if (pulse != 0 && pk.ValueRO.Kind != PickupKind.Chest) pk.ValueRW.Magnet = 1;
                float2 to = ppos - pp.ValueRO.Value;
                if (pk.ValueRO.Magnet == 0 && math.lengthsq(to) < r2) pk.ValueRW.Magnet = 1;
                if (pk.ValueRO.Magnet != 0)
                {
                    float d = math.length(to);
                    float2 want = (d > 1e-4f ? to / d : float2.zero) * clock.MagnetSpeed;
                    pv.ValueRW.Value = math.lerp(pv.ValueRO.Value, want, math.saturate(dt * 8f));
                }
                else pv.ValueRW.Value *= math.exp(-5f * dt);
                pp.ValueRW.Value += pv.ValueRO.Value * dt;

                if (pk.ValueRO.Kind == PickupKind.Chest)
                {
                    // Wobble faster and faster as the player gets close.
                    ref var v = ref pvis.ValueRW;
                    float interval = math.lerp(0.28f, 1.3f, math.saturate(math.length(to) / 14f));
                    if (v.SquashAmp <= 0f || v.SquashT > interval) { v.SquashAmp = 0.6f; v.SquashT = 0f; }
                }
            }

            // ---------------- Visual effects & buffs ----------------
            foreach (var v in SystemAPI.Query<RefRW<Visual>>())
            {
                ref var vi = ref v.ValueRW;
                if (vi.Flash > 0f) vi.Flash = math.max(0f, vi.Flash - dt * 7f);
                if (vi.SquashAmp > 0f)
                {
                    vi.SquashT += dt;
                    if (vi.SquashT > 2f) { vi.SquashAmp = 0f; vi.SquashT = 0f; }
                }
                if (vi.Spin != 0f) vi.RotY += vi.Spin * dt;
            }

            ref var b = ref buffsRW.ValueRW;
            b.Invuln = math.max(0f, b.Invuln - dt);
            b.GoldRush = math.max(0f, b.GoldRush - dt);
            b.MagnetPulse = 0;
        }

        public static void SpawnExplosion(EntityCommandBuffer ecb, Entity zonePrefab, float2 pos, float damage, float radius, float knock)
        {
            SpawnUtil.Zone(ecb, zonePrefab, pos, new DamageZone
            {
                Damage = damage, Interval = 0f, Timer = 0f, Radius = radius, Life = 0.25f, Flags = ZoneFlags.SingleTick,
                Knockback = knock * 3f, Color = new float4(1f, 0.55f, 0.15f, 0.3f), Vfx = ZoneVfx.Explosion,
            });
        }
    }
}
