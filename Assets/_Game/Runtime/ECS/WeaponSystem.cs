using DiceMania.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>Fires the player's auto-weapons, moves orbiters and runs Twin clones.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerSystem))]
    [BurstCompile]
    public partial struct WeaponSystem : ISystem
    {
        Random rng;
        EntityQuery enemyQ;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameClock>();
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<Prefabs>();
            rng = Random.CreateFromIndex(777);
            enemyQ = SystemAPI.QueryBuilder().WithAll<Enemy, Pos, Health>().Build();
        }

        static int Nearest(NativeArray<Pos> ep, NativeArray<Health> eh, float2 from, float maxDist)
        {
            int best = -1;
            float bd = maxDist * maxDist;
            for (int i = 0; i < ep.Length; i++)
            {
                if (eh[i].Value <= 0f) continue;
                float d = math.distancesq(ep[i].Value, from);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<GameClock>();
            if (clock.Running == 0) return;
            float dt = SystemAPI.Time.DeltaTime;
            var stats = SystemAPI.GetSingleton<PlayerStats>();
            var pf = SystemAPI.GetSingleton<Prefabs>();
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            var sfx = SystemAPI.GetSingletonBuffer<SfxEvent>();

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            float2 ppos = SystemAPI.GetComponent<Pos>(player).Value;
            var pdata = SystemAPI.GetComponent<PlayerData>(player);
            var ep = enemyQ.ToComponentDataArray<Pos>(Allocator.Temp);
            var eh = enemyQ.ToComponentDataArray<Health>(Allocator.Temp);

            var slots = SystemAPI.GetBuffer<WeaponSlot>(player);
            float2 moveDir = SpawnUtil.SafeNormalize(pdata.MoveInput, pdata.AimDir);
            bool fired = false;

            for (int i = 0; i < slots.Length; i++)
            {
                var w = slots[i];
                var kind = (WeaponKind)w.Kind;
                if (kind == WeaponKind.Orbit || kind == WeaponKind.Aura) continue;
                w.Timer -= dt;
                if (w.Timer > 0f) { slots[i] = w; continue; }

                int target = Nearest(ep, eh, ppos, 24f);
                float2 aim = target >= 0 ? SpawnUtil.SafeNormalize(ep[target].Value - ppos, moveDir) : moveDir;
                if (target < 0 && kind != WeaponKind.ChipRing) { w.Timer = 0.1f; slots[i] = w; continue; }

                var tpl = new Projectile
                {
                    Kind = ProjKind.Normal, Damage = w.Damage, Life = w.Life, Pierce = w.Pierce, Knockback = w.Knockback,
                    Speed = w.Speed, Area = w.Area, Homing = w.Evolved, TurnRate = 4f,
                };
                float baseAngle = SpawnUtil.Angle(aim);
                switch (kind)
                {
                    case WeaponKind.PipShot:
                    {
                        float spread = 8f;
                        float start = baseAngle - spread * (w.Count - 1) * 0.5f;
                        for (int k = 0; k < w.Count; k++)
                            SpawnUtil.PlayerShot(ecb, pf.PlayerShot, ppos, SpawnUtil.Dir(start + spread * k), tpl, (byte)ShapeId.Cube, w.Area, w.Color);
                        break;
                    }
                    case WeaponKind.Cannon:
                    {
                        tpl.Kind = ProjKind.Explode;
                        for (int k = 0; k < w.Count; k++)
                        {
                            var t = tpl;
                            t.Damage = w.Damage * rng.NextInt(1, 7); // rolls a d6
                            float a = baseAngle + (k - (w.Count - 1) * 0.5f) * 12f;
                            SpawnUtil.PlayerShot(ecb, pf.PlayerShot, ppos, SpawnUtil.Dir(a), t, (byte)ShapeId.Sphere, 0.7f, w.Color);
                        }
                        break;
                    }
                    case WeaponKind.CardFan:
                    {
                        float spread = math.min(90f, 14f * w.Count);
                        float step = w.Count > 1 ? spread / (w.Count - 1) : 0f;
                        float start = baseAngle - spread * 0.5f;
                        for (int k = 0; k < w.Count; k++)
                        {
                            var e = SpawnUtil.PlayerShot(ecb, pf.PlayerShot, ppos, SpawnUtil.Dir(start + step * k), tpl, (byte)ShapeId.Cube, w.Area, w.Color);
                            ecb.SetComponent(e, new Visual
                            {
                                Mesh = (byte)ShapeId.Cube, Scale = new float3(w.Area * 1.4f, 0.08f, w.Area), Y = 0.6f,
                                RotY = -math.radians(start + step * k), Spin = 0f, Color = w.Color,
                            });
                        }
                        break;
                    }
                    case WeaponKind.ChipRing:
                    {
                        float step = 360f / math.max(1, w.Count);
                        for (int k = 0; k < w.Count; k++)
                        {
                            var e = SpawnUtil.PlayerShot(ecb, pf.PlayerShot, ppos, SpawnUtil.Dir(w.Angle + step * k), tpl, (byte)ShapeId.Cube, w.Area, w.Color);
                            ecb.SetComponent(e, new Visual
                            {
                                Mesh = (byte)ShapeId.Cube, Scale = new float3(w.Area * 1.3f, 0.12f, w.Area * 1.3f), Y = 0.6f,
                                Spin = 6f, Color = w.Color,
                            });
                        }
                        w.Angle += w.Evolved != 0 ? 17f : step * 0.5f;
                        break;
                    }
                }
                w.Timer = w.Cooldown;
                slots[i] = w;
                fired = true;
            }
            if (fired) sfx.Add(new SfxEvent { Id = Sfx.Shoot });

            // Orbiters circle the player.
            foreach (var (orb, pos, vis) in SystemAPI.Query<RefRW<Orbiter>, RefRW<Pos>, RefRW<Visual>>())
            {
                orb.ValueRW.Angle += orb.ValueRO.AngularSpeed * dt;
                math.sincos(orb.ValueRO.Angle, out float s, out float c);
                pos.ValueRW.Value = ppos + new float2(c, s) * orb.ValueRO.Distance;
            }

            // Twin clones orbit loosely and shoot.
            foreach (var (clone, pos, vis, e) in SystemAPI.Query<RefRW<CloneUnit>, RefRW<Pos>, RefRW<Visual>>().WithEntityAccess())
            {
                ref var c = ref clone.ValueRW;
                c.Life -= dt;
                if (c.Life <= 0f)
                {
                    SpawnUtil.Burst(ecb, pf.Particle, pos.ValueRO.Value, 8, vis.ValueRO.Color, 6f, 0.25f, 0.5f, ref rng);
                    ecb.DestroyEntity(e);
                    continue;
                }
                c.OrbitAngle += dt * 1.2f;
                math.sincos(c.OrbitAngle, out float s, out float co);
                float2 target = ppos + new float2(co, s) * 2.2f;
                pos.ValueRW.Value = math.lerp(pos.ValueRO.Value, target, math.saturate(dt * 6f));
                vis.ValueRW.Color.w = c.Life < 1.5f && math.frac(c.Life * 5f) < 0.5f ? 0f : 0.6f;

                c.FireTimer -= dt;
                if (c.FireTimer <= 0f)
                {
                    int t = Nearest(ep, eh, pos.ValueRO.Value, 20f);
                    if (t >= 0)
                    {
                        float2 d = SpawnUtil.SafeNormalize(ep[t].Value - pos.ValueRO.Value, new float2(1, 0));
                        var p = new Projectile { Kind = ProjKind.Normal, Damage = c.Damage * stats.Might, Life = 1.5f, Pierce = 0, Knockback = 0.4f, Speed = 24f * stats.ProjSpeed };
                        SpawnUtil.PlayerShot(ecb, pf.PlayerShot, pos.ValueRO.Value, d, p, (byte)ShapeId.Diamond, 0.32f, new float4(0.95f, 0.76f, 0.24f, 1f));
                    }
                    c.FireTimer = c.FireCooldown;
                }
            }

            ep.Dispose();
            eh.Dispose();
        }
    }
}
