using DiceMania.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>Integrates projectiles, bullets, particles and pickups; ticks buffs and visual effects.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EnemySystem))]
    [BurstCompile]
    public partial struct MotionSystem : ISystem
    {
        EntityQuery shotQ, enemyQ;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameClock>();
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<Prefabs>();
            shotQ = SystemAPI.QueryBuilder().WithAllRW<Projectile, Pos>().WithAllRW<Vel, Visual>().Build();
            enemyQ = SystemAPI.QueryBuilder().WithAll<Enemy, Pos, Health>().Build();
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
                if (math.lengthsq(v) > 1e-4f && vi.Spin == 0f) vi.RotY = -math.atan2(v.y, v.x);

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
            foreach (var (pt, pp, pv, pvis, e) in SystemAPI.Query<RefRW<Particle>, RefRW<Pos>, RefRW<Vel>, RefRW<Visual>>().WithEntityAccess())
            {
                pv.ValueRW.Value *= math.exp(-pt.ValueRO.Drag * dt);
                pp.ValueRW.Value += pv.ValueRO.Value * dt;
                pt.ValueRW.Life -= dt;
                float k = math.saturate(pt.ValueRO.Life / math.max(0.01f, pt.ValueRO.MaxLife));
                pvis.ValueRW.Scale = new float3(pt.ValueRO.StartScale * k);
                if (pt.ValueRO.Life <= 0f) ecb.DestroyEntity(e);
            }

            // ---------------- Pickups (magnet) ----------------
            byte pulse = buffsRW.ValueRO.MagnetPulse;
            float r2 = stats.PickupRadius * stats.PickupRadius;
            foreach (var (pk, pp, pv) in SystemAPI.Query<RefRW<Pickup>, RefRW<Pos>, RefRW<Vel>>())
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
            }

            // ---------------- Visual effects & buffs ----------------
            foreach (var v in SystemAPI.Query<RefRW<Visual>>())
            {
                if (v.ValueRO.Flash > 0f) v.ValueRW.Flash = math.max(0f, v.ValueRO.Flash - dt * 6f);
                if (v.ValueRO.Spin != 0f) v.ValueRW.RotY += v.ValueRO.Spin * dt;
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
                Knockback = knock * 3f, Color = new float4(1f, 0.55f, 0.15f, 0.55f),
            });
        }
    }
}
