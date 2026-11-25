using DiceMania.Data;
using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>Burst-compatible spawn helpers shared by systems (all spawns go through an ECB).</summary>
    public static class SpawnUtil
    {
        public static float2 Dir(float degrees)
        {
            math.sincos(math.radians(degrees), out float s, out float c);
            return new float2(c, s);
        }

        public static float Angle(float2 dir) => math.degrees(math.atan2(dir.y, dir.x));

        public static float2 SafeNormalize(float2 v, float2 fallback)
        {
            float l = math.length(v);
            return l > 1e-5f ? v / l : fallback;
        }

        public static Entity PlayerShot(EntityCommandBuffer ecb, Entity prefab, float2 pos, float2 dir, Projectile p,
            byte mesh, float size, float4 color)
        {
            var e = ecb.Instantiate(prefab);
            ecb.SetComponent(e, new Pos { Value = pos });
            ecb.SetComponent(e, new Vel { Value = dir * p.Speed });
            ecb.SetComponent(e, p);
            ecb.SetComponent(e, new Radius { Value = size * 0.5f });
            ecb.SetComponent(e, new Visual
            {
                Mesh = mesh, Scale = new float3(size), Y = 0.6f, RotY = -math.atan2(dir.y, dir.x),
                Spin = mesh == (byte)ShapeId.Cube ? 8f : 0f, Color = color,
            });
            return e;
        }

        public static void EnemyShot(EntityCommandBuffer ecb, Entity prefab, float2 pos, float2 dir, float speed, float damage,
            float life, float size, float4 color)
        {
            var e = ecb.Instantiate(prefab);
            ecb.SetComponent(e, new Pos { Value = pos });
            ecb.SetComponent(e, new Vel { Value = dir * speed });
            ecb.SetComponent(e, new EnemyShot { Damage = damage, Life = life, Radius = size * 0.5f });
            ecb.SetComponent(e, new Visual { Mesh = (byte)ShapeId.Sphere, Scale = new float3(size), Y = 0.6f, Color = color });
        }

        /// <summary>
        /// Fire one volley of a pattern. Player shots use <paramref name="playerTemplate"/>, enemy shots use damage/speed from the shooter.
        /// Returns the number of bullets spawned. Summon patterns spawn nothing (handled by callers).
        /// </summary>
        public static int FireVolley(EntityCommandBuffer ecb, in Prefabs pf, ref Shooter s, float2 origin, float2 aim, bool player,
            float enemyLife, float4 color, float size, ref Random rng, Projectile playerTemplate)
        {
            int n = 0;
            float baseAngle = Angle(aim);
            switch ((PatternType)s.Pattern)
            {
                case PatternType.Aimed:
                    Emit(ecb, pf, origin, Dir(baseAngle), player, ref s, enemyLife, color, size, playerTemplate); n = 1;
                    break;
                case PatternType.Spread:
                {
                    int c = math.max(1, s.Count);
                    float step = c > 1 ? s.Spread / (c - 1) : 0f;
                    float start = baseAngle - s.Spread * 0.5f;
                    for (int i = 0; i < c; i++) Emit(ecb, pf, origin, Dir(start + step * i), player, ref s, enemyLife, color, size, playerTemplate);
                    n = c;
                    break;
                }
                case PatternType.Ring:
                {
                    int c = math.max(1, s.Count);
                    float step = 360f / c;
                    for (int i = 0; i < c; i++) Emit(ecb, pf, origin, Dir(s.Angle + step * i), player, ref s, enemyLife, color, size, playerTemplate);
                    s.Angle += step * 0.5f;
                    n = c;
                    break;
                }
                case PatternType.Spiral:
                {
                    int c = math.max(1, s.Count);
                    float step = 360f / c;
                    for (int i = 0; i < c; i++) Emit(ecb, pf, origin, Dir(s.Angle + step * i), player, ref s, enemyLife, color, size, playerTemplate);
                    s.Angle += s.Spin;
                    n = c;
                    break;
                }
                case PatternType.Random:
                {
                    int c = math.max(1, s.Count);
                    for (int i = 0; i < c; i++)
                        Emit(ecb, pf, origin, Dir(baseAngle + rng.NextFloat(-s.Spread, s.Spread) * 0.5f), player, ref s, enemyLife, color, size, playerTemplate);
                    n = c;
                    break;
                }
            }
            return n;
        }

        static void Emit(EntityCommandBuffer ecb, in Prefabs pf, float2 origin, float2 dir, bool player, ref Shooter s,
            float enemyLife, float4 color, float size, Projectile t)
        {
            if (player)
            {
                t.Speed = s.BulletSpeed;
                t.Damage = s.Damage;
                PlayerShot(ecb, pf.PlayerShot, origin + dir * 0.6f, dir, t, (byte)ShapeId.Sphere, size, color);
            }
            else
            {
                EnemyShot(ecb, pf.EnemyShot, origin + dir * 0.5f, dir, s.BulletSpeed, s.Damage, enemyLife, size, color);
            }
        }

        /// <summary>
        /// Advance a shooter's timers. Returns true when a volley should be fired this frame.
        /// </summary>
        public static bool TickShooter(ref Shooter s, float dt, bool canFire)
        {
            if (s.Pattern == (byte)PatternType.None) return false;
            if (s.BurstLeft > 0)
            {
                s.BurstTimer -= dt;
                if (s.BurstTimer <= 0f)
                {
                    s.BurstLeft--;
                    s.BurstTimer = s.BurstInterval;
                    if (s.BurstLeft == 0) s.Timer = s.Cooldown;
                    return true;
                }
                return false;
            }
            s.Timer -= dt;
            if (s.Timer <= 0f && canFire)
            {
                s.BurstLeft = math.max(1, s.Bursts);
                s.BurstTimer = 0f;
                // Fire the first shot immediately.
                s.BurstLeft--;
                s.BurstTimer = s.BurstInterval;
                if (s.BurstLeft == 0) s.Timer = s.Cooldown;
                return true;
            }
            if (s.Timer < 0f) s.Timer = 0f;
            return false;
        }

        public static void Burst(EntityCommandBuffer ecb, Entity prefab, float2 pos, int count, float4 color, float speed, float size,
            float life, ref Random rng, byte mesh = (byte)ShapeId.Cube)
        {
            for (int i = 0; i < count; i++)
            {
                var e = ecb.Instantiate(prefab);
                float2 d = Dir(rng.NextFloat(0f, 360f)) * rng.NextFloat(0.4f, 1f) * speed;
                ecb.SetComponent(e, new Pos { Value = pos });
                ecb.SetComponent(e, new Vel { Value = d });
                float s = size * rng.NextFloat(0.6f, 1.2f);
                ecb.SetComponent(e, new Particle { Life = life, MaxLife = life, Drag = 3f, StartScale = s });
                ecb.SetComponent(e, new Visual
                {
                    Mesh = mesh, Scale = new float3(s), Y = rng.NextFloat(0.3f, 1.2f), RotY = rng.NextFloat(0, 6.28f),
                    Spin = rng.NextFloat(-10f, 10f), Color = color,
                });
            }
        }

        public static void Pickup(EntityCommandBuffer ecb, Entity prefab, float2 pos, byte kind, float value, ref Random rng)
        {
            var e = ecb.Instantiate(prefab);
            float2 jitter = Dir(rng.NextFloat(0, 360)) * rng.NextFloat(0f, 0.6f);
            ecb.SetComponent(e, new Pos { Value = pos + jitter });
            ecb.SetComponent(e, new Vel { Value = jitter * 3f });
            ecb.SetComponent(e, new Pickup { Kind = kind, Value = value });
            float4 col; byte mesh; float scale;
            switch (kind)
            {
                case PickupKind.Gold: col = new float4(1f, 0.82f, 0.15f, 0.8f); mesh = (byte)ShapeId.Cube; scale = 0.38f; break;
                case PickupKind.Heal: col = new float4(0.3f, 1f, 0.4f, 0.9f); mesh = (byte)ShapeId.Sphere; scale = 0.55f; break;
                case PickupKind.Chest: col = new float4(1f, 0.9f, 0.4f, 0.9f); mesh = (byte)ShapeId.Cube; scale = 0.9f; break;
                default:
                    mesh = (byte)ShapeId.Diamond;
                    scale = value >= 10 ? 0.6f : value >= 3 ? 0.45f : 0.32f;
                    col = value >= 10 ? new float4(1f, 0.3f, 0.9f, 0.9f) : value >= 3 ? new float4(0.3f, 1f, 0.5f, 0.9f) : new float4(0.3f, 0.7f, 1f, 0.9f);
                    break;
            }
            ecb.SetComponent(e, new Visual { Mesh = mesh, Scale = new float3(scale), Y = 0.4f, Spin = 2.5f, Color = col });
        }

        public static Entity Zone(EntityCommandBuffer ecb, Entity prefab, float2 pos, DamageZone z)
        {
            var e = ecb.Instantiate(prefab);
            ecb.SetComponent(e, new Pos { Value = pos });
            ecb.SetComponent(e, z);
            ecb.SetComponent(e, new Visual { Mesh = (byte)ShapeId.Disc, Scale = new float3(z.Radius * 2f, 1f, z.Radius * 2f), Y = 0.05f, Color = z.Color });
            return e;
        }
    }
}
