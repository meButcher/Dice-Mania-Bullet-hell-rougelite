using DiceMania.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>
    /// Fires the player's auto-weapons (with recoil, camera kick, muzzle flash, shells), aims + kicks the hero's gun,
    /// moves orbiters and runs Twin clones.
    /// </summary>
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
            state.RequireForUpdate<Feel>();
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
            var feel = SystemAPI.GetSingleton<Feel>();
            var pf = SystemAPI.GetSingleton<Prefabs>();
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            var sfx = SystemAPI.GetSingletonBuffer<SfxEvent>();
            var evRW = SystemAPI.GetSingletonRW<GameEvents>();

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            float2 ppos = SystemAPI.GetComponent<Pos>(player).Value;
            var pdata = SystemAPI.GetComponent<PlayerData>(player);
            bool copied = SystemAPI.GetComponent<CopyForm>(player).Time > 0f;
            bool dead = SystemAPI.GetComponent<Health>(player).Value <= 0f;
            var ep = enemyQ.ToComponentDataArray<Pos>(Allocator.Temp);
            var eh = enemyQ.ToComponentDataArray<Health>(Allocator.Temp);

            var slots = SystemAPI.GetBuffer<WeaponSlot>(player);
            float2 moveDir = SpawnUtil.SafeNormalize(pdata.MoveInput, pdata.AimDir);
            int nearest = Nearest(ep, eh, ppos, 24f);
            float2 nearestDir = nearest >= 0 ? SpawnUtil.SafeNormalize(ep[nearest].Value - ppos, moveDir) : moveDir;

            bool fired = false, heavy = false, gunFired = false;
            float2 kick = float2.zero, gunDir = float2.zero;
            float vibe = 0f;
            int shellBudget = 3;

            for (int i = 0; i < slots.Length; i++)
            {
                var w = slots[i];
                var kind = (WeaponKind)w.Kind;
                if (kind == WeaponKind.Orbit || kind == WeaponKind.Aura || dead) continue;
                w.Timer -= dt;
                if (w.Timer > 0f) { slots[i] = w; continue; }

                if (nearest < 0 && kind != WeaponKind.ChipRing) { w.Timer = 0.1f; slots[i] = w; continue; }
                float2 aim = nearestDir;

                var tpl = new Projectile
                {
                    Kind = ProjKind.Normal, Damage = w.Damage, Life = w.Life, Pierce = w.Pierce, Knockback = w.Knockback,
                    Speed = w.Speed, Area = w.Area, Homing = w.Evolved, TurnRate = 4f,
                };
                float baseAngle = SpawnUtil.Angle(aim);
                float2 muzzle = ppos + aim * 0.85f;
                switch (kind)
                {
                    case WeaponKind.PipShot:
                    {
                        // Spread + a little jitter: identical bullets read as a laser.
                        float spread = 8f;
                        float start = baseAngle - spread * (w.Count - 1) * 0.5f;
                        for (int k = 0; k < w.Count; k++)
                            SpawnUtil.PlayerShot(ecb, pf.PlayerShot, muzzle, SpawnUtil.Dir(start + spread * k + rng.NextFloat(-3f, 3f)), tpl,
                                (byte)ShapeId.Cube, w.Area, w.Color);
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
                            SpawnUtil.PlayerShot(ecb, pf.PlayerShot, muzzle, SpawnUtil.Dir(a), t, (byte)ShapeId.Sphere, 0.7f, w.Color);
                        }
                        heavy = true;
                        break;
                    }
                    case WeaponKind.CardFan:
                    {
                        float spread = math.min(90f, 14f * w.Count);
                        float step = w.Count > 1 ? spread / (w.Count - 1) : 0f;
                        float start = baseAngle - spread * 0.5f;
                        for (int k = 0; k < w.Count; k++)
                        {
                            float a = start + step * k;
                            var e = SpawnUtil.PlayerShot(ecb, pf.PlayerShot, ppos, SpawnUtil.Dir(a), tpl, (byte)ShapeId.Cube, w.Area, w.Color);
                            ecb.SetComponent(e, new Visual
                            {
                                Mesh = (byte)ShapeId.Cube, Fx = VisualFx.Trail, Scale = new float3(w.Area * 1.4f, 0.08f, w.Area), Y = 0.6f,
                                RotY = -math.radians(a), Heading = -math.radians(a), TrailLen = math.min(1.8f, w.Speed * 0.05f), Color = w.Color,
                            });
                        }
                        break;
                    }
                    case WeaponKind.ChipRing:
                    {
                        float step = 360f / math.max(1, w.Count);
                        for (int k = 0; k < w.Count; k++)
                        {
                            float a = w.Angle + step * k;
                            var e = SpawnUtil.PlayerShot(ecb, pf.PlayerShot, ppos, SpawnUtil.Dir(a), tpl, (byte)ShapeId.Cube, w.Area, w.Color);
                            ecb.SetComponent(e, new Visual
                            {
                                Mesh = (byte)ShapeId.Cube, Fx = VisualFx.Trail, Scale = new float3(w.Area * 1.3f, 0.12f, w.Area * 1.3f), Y = 0.6f,
                                Spin = 6f, Heading = -math.radians(a), TrailLen = math.min(1.8f, w.Speed * 0.05f), Color = w.Color,
                            });
                        }
                        w.Angle += w.Evolved != 0 ? 17f : step * 0.5f;
                        break;
                    }
                }

                // ---- Feel: kick the gun, flash the barrel, push the hero, kick the camera, eject a shell ----
                if (kind == WeaponKind.ChipRing)
                {
                    Fx.Shockwave(ecb, pf.Particle, ppos, 0.3f, 0.4f, 1.8f, new float4(w.Color.xyz, 0.7f), 0.18f);
                }
                else
                {
                    pdata.Recoil -= aim * w.Recoil * feel.Recoil;
                    kick += aim * w.Kick;
                    gunDir = aim;
                    gunFired = true;
                    Fx.Muzzle(ecb, pf.Particle, muzzle, aim, kind == WeaponKind.Cannon ? 1.9f : 1f, w.Color, ref rng);
                }
                vibe = math.max(vibe, w.Shake);
                if (w.Shell > 0 && shellBudget-- > 0)
                    Fx.Shell(ecb, pf.Particle, ppos + aim * 0.35f, aim, w.Shell >= 2, feel.ShellLife, ref rng);

                w.Timer = w.Cooldown;
                slots[i] = w;
                fired = true;
            }
            if (fired) sfx.Add(new SfxEvent { Id = Sfx.Shoot });
            if (heavy) sfx.Add(new SfxEvent { Id = Sfx.Heavy });
            if (fired)
            {
                ref var ev = ref evRW.ValueRW;
                ev.Kick += kick * feel.Kick;
                ev.Vibe = math.max(ev.Vibe, vibe * feel.Shake);
                ev.Rumble = math.max(ev.Rumble, math.min(1f, vibe * 2.5f));
            }
            SystemAPI.SetComponent(player, pdata);

            // The hero's gun: tracks the target, snaps to the shot, kicks back and flashes.
            foreach (var (gun, gpos, gvis) in SystemAPI.Query<RefRW<PlayerGun>, RefRW<Pos>, RefRW<Visual>>())
            {
                ref var g = ref gun.ValueRW;
                if (gunFired) { g.Dir = gunDir; g.Kick = 1f; }
                else g.Dir = SpawnUtil.SafeNormalize(math.lerp(g.Dir, nearestDir, math.saturate(dt * 10f)), nearestDir);
                g.Kick *= math.exp(-12f * dt);
                // A small cube beside the hero (like the original), coloured by the current ability.
                gpos.ValueRW.Value = ppos + g.Dir * (0.78f - 0.28f * g.Kick);
                ref var v = ref gvis.ValueRW;
                v.RotY = -math.atan2(g.Dir.y, g.Dir.x);
                v.Y = 0.62f;
                v.Scale = copied || dead ? float3.zero : new float3(0.36f * (1f - 0.22f * g.Kick), 0.3f * (1f + 0.35f * g.Kick), 0.3f * (1f + 0.35f * g.Kick));
                v.Flash = math.max(v.Flash, g.Kick * 0.9f);
            }

            // Orbiters circle the player.
            foreach (var (orb, pos) in SystemAPI.Query<RefRW<Orbiter>, RefRW<Pos>>())
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
                    Fx.Shockwave(ecb, pf.Particle, pos.ValueRO.Value, 0.2f, 0.3f, 1.4f, new float4(vis.ValueRO.Color.xyz, 0.8f), 0.25f);
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
                        SpawnUtil.PlayerShot(ecb, pf.PlayerShot, pos.ValueRO.Value + d * 0.4f, d, p, (byte)ShapeId.Diamond, 0.32f, new float4(0.95f, 0.76f, 0.24f, 1f));
                        Fx.Glow(ecb, pf.Particle, pos.ValueRO.Value + d * 0.5f, 0.5f, 0.6f, new float4(1f, 0.9f, 0.5f, 0.9f), 0.06f, 1.5f);
                        vis.ValueRW.SquashAmp = 0.4f;
                        vis.ValueRW.SquashT = 0f;
                    }
                    c.FireTimer = c.FireCooldown;
                }
            }

            ep.Dispose();
            eh.Dispose();
        }
    }
}
