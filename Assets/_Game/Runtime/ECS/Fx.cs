using DiceMania.Data;
using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>
    /// Burst-compatible VFX spawners. Everything goes through an ECB and uses the Particle prefab, so systems and
    /// the managed GameDirector can both call these. Colour convention: opaque meshes = rgb + emission in a;
    /// additive meshes (Glow / Streak / Ring / Disc) = rgb * a.
    /// See docs/04_GAME_FEEL.md for the "why" behind each effect.
    /// </summary>
    public static class Fx
    {
        public static Entity Emit(EntityCommandBuffer ecb, Entity prefab, float2 pos, float2 vel, in Particle p, in Visual v)
        {
            var e = ecb.Instantiate(prefab);
            ecb.SetComponent(e, new Pos { Value = pos });
            ecb.SetComponent(e, new Vel { Value = vel });
            ecb.SetComponent(e, p);
            ecb.SetComponent(e, v);
            return e;
        }

        static float2 Rotate(float2 d, float degrees)
        {
            math.sincos(math.radians(degrees), out float s, out float c);
            return new float2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>Soft additive flash (muzzle flash, impact light, explosion core).</summary>
        public static void Glow(EntityCommandBuffer ecb, Entity prefab, float2 pos, float y, float size, float4 color, float life,
            float growTo = 1.35f)
        {
            var p = new Particle
            {
                Life = life, MaxLife = life, BaseScale = new float3(size, 1f, size), GrowTo = growTo, Alpha0 = color.w,
                Flags = PFlags.Grow | PFlags.Fade,
            };
            var v = new Visual { Mesh = (byte)ShapeId.Glow, Scale = p.BaseScale, Y = y, Color = color };
            Emit(ecb, prefab, pos, float2.zero, p, v);
        }

        /// <summary>Expanding ring that fades out.</summary>
        public static void Shockwave(EntityCommandBuffer ecb, Entity prefab, float2 pos, float y, float fromRadius, float toRadius,
            float4 color, float life)
        {
            float from = math.max(0.05f, fromRadius);
            var p = new Particle
            {
                Life = life, MaxLife = life, BaseScale = new float3(from * 2f, 1f, from * 2f),
                GrowTo = math.max(1f, toRadius / from), Alpha0 = color.w, Flags = PFlags.Grow | PFlags.Fade,
            };
            var v = new Visual { Mesh = (byte)ShapeId.Ring, Scale = p.BaseScale, Y = y, Color = color };
            Emit(ecb, prefab, pos, float2.zero, p, v);
        }

        /// <summary>White-hot streaks that arc, fall and fade to <paramref name="color"/>.</summary>
        public static void Sparks(EntityCommandBuffer ecb, Entity prefab, float2 pos, float y, float2 dir, float spreadDeg, int count,
            float speedMin, float speedMax, float length, float4 color, float life, ref Random rng)
        {
            float baseA = SpawnUtil.Angle(dir);
            for (int i = 0; i < count; i++)
            {
                float a = baseA + rng.NextFloat(-0.5f, 0.5f) * spreadDeg;
                float sp = rng.NextFloat(speedMin, speedMax);
                float l = life * rng.NextFloat(0.6f, 1.2f);
                var p = new Particle
                {
                    Life = l, MaxLife = l, Drag = 3.5f, BaseScale = new float3(length, 1f, length * 0.22f),
                    VelY = rng.NextFloat(0f, 5f), Gravity = 22f, Bounce = 0.3f, Alpha0 = color.w,
                    Flags = PFlags.Streak | PFlags.Shrink,
                };
                var v = new Visual { Mesh = (byte)ShapeId.Streak, Scale = p.BaseScale, Y = y, Color = color, Flash = 1f };
                Emit(ecb, prefab, pos, SpawnUtil.Dir(a) * sp, p, v);
            }
        }

        /// <summary>Chunky cubes (or other solids) thrown in arcs that bounce and shrink away.</summary>
        public static void Debris(EntityCommandBuffer ecb, Entity prefab, float2 pos, float y, int count, float4 color, float speed,
            float size, float life, ref Random rng, byte mesh = (byte)ShapeId.Cube, float2 bias = default)
        {
            for (int i = 0; i < count; i++)
            {
                float2 d = SpawnUtil.Dir(rng.NextFloat(0f, 360f)) * rng.NextFloat(0.35f, 1f) * speed + bias;
                float s = size * rng.NextFloat(0.6f, 1.2f);
                float l = life * rng.NextFloat(0.7f, 1.2f);
                var p = new Particle
                {
                    Life = l, MaxLife = l, Drag = 1.5f, BaseScale = new float3(s), VelY = rng.NextFloat(3f, 9f),
                    Gravity = 26f, Bounce = 0.35f, Flags = PFlags.ShrinkLate,
                };
                var v = new Visual
                {
                    Mesh = mesh, Scale = p.BaseScale, Y = y, RotY = rng.NextFloat(0f, 6.28f), Spin = rng.NextFloat(-12f, 12f),
                    Color = color, Flash = 0.4f,
                };
                Emit(ecb, prefab, pos, d, p, v);
            }
        }

        // ------------------------------------------------------------------ guns

        /// <summary>A brass casing ejected to the right of the shot: tumbles, bounces, clinks and stays on the floor.</summary>
        public static void Shell(EntityCommandBuffer ecb, Entity prefab, float2 pos, float2 aim, bool big, float life, ref Random rng)
        {
            float2 right = new float2(aim.y, -aim.x);
            float2 d = math.normalizesafe(right - aim * 0.35f + SpawnUtil.Dir(rng.NextFloat(0f, 360f)) * 0.25f, right);
            float s = big ? 1.7f : 1f;
            var p = new Particle
            {
                Life = life, MaxLife = life, Drag = 0.8f, BaseScale = new float3(0.27f, 0.11f, 0.11f) * s,
                VelY = rng.NextFloat(4.5f, 7f), Gravity = 30f, Bounce = 0.45f, Flags = PFlags.ShrinkLate | PFlags.Clink,
            };
            var v = new Visual
            {
                Mesh = (byte)ShapeId.Cube, Scale = p.BaseScale, Y = 0.65f, RotY = rng.NextFloat(0f, 6.28f),
                Spin = rng.NextFloat(14f, 24f) * (rng.NextBool() ? 1f : -1f), Color = new float4(1f, 0.74f, 0.28f, 0.35f),
            };
            Emit(ecb, prefab, pos, d * rng.NextFloat(3f, 5.5f), p, v);
        }

        /// <summary>Muzzle flash: a hot glow plus a few sparks out of the barrel.</summary>
        public static void Muzzle(EntityCommandBuffer ecb, Entity prefab, float2 muzzle, float2 aim, float size, float4 color, ref Random rng)
        {
            Glow(ecb, prefab, muzzle, 0.6f, 0.9f * size, new float4(math.lerp(color.xyz, new float3(1f), 0.6f), 1f), 0.07f, 1.6f);
            Sparks(ecb, prefab, muzzle, 0.6f, aim, 40f, 2, 8f, 15f, 0.35f, new float4(color.xyz, 1f), 0.12f, ref rng);
        }

        // ------------------------------------------------------------------ hits & kills

        /// <summary>Impact: a small light where the shot landed and sparks spraying through the target.</summary>
        public static void Impact(EntityCommandBuffer ecb, Entity prefab, float2 pos, float2 dir, float4 color, int sparks, ref Random rng)
        {
            Glow(ecb, prefab, pos, 0.6f, 0.9f, new float4(1f, 0.95f, 0.85f, 0.8f), 0.06f);
            if (sparks > 0) Sparks(ecb, prefab, pos, 0.6f, dir, 75f, sparks, 6f, 13f, 0.32f, new float4(color.xyz, 1f), 0.16f, ref rng);
        }

        /// <summary>Kill: the body hangs as a white silhouette for <paramref name="freeze"/> seconds, then gets thrown.</summary>
        public static void Corpse(EntityCommandBuffer ecb, Entity prefab, float2 pos, in Visual body, float2 dir, float speed,
            float freeze, ref Random rng)
        {
            float2 d = Rotate(dir, rng.NextFloat(-18f, 18f));
            const float life = 0.7f;
            var p = new Particle
            {
                Life = life, MaxLife = life, Drag = 2.2f, BaseScale = body.Scale, VelY = rng.NextFloat(8f, 11f),
                Gravity = 36f, Bounce = 0.32f, Freeze = freeze, Flags = PFlags.ShrinkLate | PFlags.PopOut,
            };
            var v = body;
            // Keep the body pale and glowing in flight so it reads as "dead" against the living horde.
            v.Color = new float4(math.lerp(body.Color.xyz, new float3(1f), 0.4f), math.max(body.Color.w, 0.65f));
            v.Flash = 1f;
            v.Fx = 0;
            v.SquashAmp = 0f;
            v.Spin = rng.NextFloat(8f, 16f) * (rng.NextBool() ? 1f : -1f);
            Emit(ecb, prefab, pos, d * speed, p, v);
        }

        /// <summary>
        /// Melee swing: an arc of glowing streaks that sweeps across the swing (alternating direction), a white-hot inner
        /// edge, a flash in front and sparks flying off the tip.
        /// </summary>
        public static void Slash(EntityCommandBuffer ecb, Entity prefab, float2 c, float2 dir, float radius, float4 color, bool flip,
            ref Random rng)
        {
            float baseA = SpawnUtil.Angle(dir);
            float sign = flip ? -1f : 1f;
            const int n = 16;
            const float half = 72f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float bulge = math.sin(t * math.PI);
                float a = baseA + sign * math.lerp(-half, half, t);
                float2 radial = SpawnUtil.Dir(a);
                float r = radius * (0.78f + 0.14f * bulge); // the blade traces the reach of the hit
                float tangent = a + sign * 90f;
                float len = radius * 0.46f * (0.55f + 0.6f * bulge);
                float delay = t * 0.06f;
                // Three layers per segment: a soft wide wake (the crescent's body), the coloured blade, a white-hot edge.
                var p = new Particle
                {
                    Life = 0.2f, MaxLife = 0.2f, BaseScale = new float3(len, 1f, 0.32f + 0.7f * bulge), Freeze = delay, Alpha0 = 1f,
                    Flags = PFlags.Fade | PFlags.Hidden,
                };
                var v = new Visual { Mesh = (byte)ShapeId.Streak, Scale = float3.zero, Y = 0.6f, RotY = -math.radians(tangent), Color = color };
                var wake = p;
                wake.BaseScale = new float3(len * 1.1f, 1f, 0.5f + 1.1f * bulge);
                wake.Alpha0 = 0.45f;
                var wv = v;
                wv.Color = new float4(color.xyz, 0.45f);
                Emit(ecb, prefab, c + radial * (r - radius * 0.12f), float2.zero, wake, wv);
                Emit(ecb, prefab, c + radial * r, float2.zero, p, v);
                var core = p;
                core.BaseScale = new float3(len * 0.85f, 1f, 0.12f + 0.24f * bulge);
                var cv = v;
                cv.Color = new float4(1f, 1f, 1f, 1f);
                Emit(ecb, prefab, c + radial * (r + 0.05f), float2.zero, core, cv);
            }
            Glow(ecb, prefab, c + dir * radius * 0.55f, 0.6f, radius * 1.5f, new float4(color.xyz, 0.7f), 0.12f, 1.2f);
            float2 tip = SpawnUtil.Dir(baseA + sign * half);
            Sparks(ecb, prefab, c + tip * radius * 0.8f, 0.6f, SpawnUtil.Dir(baseA + sign * (half + 90f)), 70f, 4, 6f, 12f, 0.35f, color, 0.18f, ref rng);
        }

        /// <summary>A splat on the floor that stays for a while ("the fight leaves a mess").</summary>
        public static void Splat(EntityCommandBuffer ecb, Entity prefab, float2 pos, float size, float4 color, float life, ref Random rng)
        {
            var p = new Particle
            {
                Life = life, MaxLife = life,
                BaseScale = new float3(size * rng.NextFloat(0.8f, 1.25f), 1f, size * rng.NextFloat(0.6f, 1f)),
                Flags = PFlags.PopIn | PFlags.ShrinkLate,
            };
            var v = new Visual
            {
                Mesh = (byte)ShapeId.Splat, Scale = float3.zero, Y = 0.045f + rng.NextFloat(0f, 0.012f), RotY = rng.NextFloat(0f, 6.28f),
                Color = color,
            };
            Emit(ecb, prefab, pos, float2.zero, p, v);
        }

        /// <summary>Droplets sprayed along the killing hit; they land and stay as dots.</summary>
        public static void Droplets(EntityCommandBuffer ecb, Entity prefab, float2 pos, float2 dir, int count, float4 color, float life,
            ref Random rng)
        {
            float baseA = SpawnUtil.Angle(dir);
            for (int i = 0; i < count; i++)
            {
                float s = rng.NextFloat(0.12f, 0.24f);
                float l = life * rng.NextFloat(0.7f, 1f);
                var p = new Particle
                {
                    Life = l, MaxLife = l, Drag = 1.2f, BaseScale = new float3(s, s * 0.45f, s),
                    VelY = rng.NextFloat(3f, 7f), Gravity = 30f, Flags = PFlags.Land | PFlags.ShrinkLate,
                };
                var v = new Visual { Mesh = (byte)ShapeId.Sphere, Scale = p.BaseScale, Y = 0.6f, Color = color };
                Emit(ecb, prefab, pos, SpawnUtil.Dir(baseA + rng.NextFloat(-50f, 50f)) * rng.NextFloat(3f, 9f), p, v);
            }
        }

        // ------------------------------------------------------------------ abilities

        /// <summary>Explosion: white core, warm flash, shockwave, embers, debris, smoke balls and a scorch mark.</summary>
        public static void Explosion(EntityCommandBuffer ecb, Entity prefab, float2 c, float radius, float4 color, float scorchLife,
            bool big, ref Random rng)
        {
            Glow(ecb, prefab, c, 0.6f, radius * 2.6f, new float4(1f, 0.8f, 0.5f, 1f), 0.18f);
            Glow(ecb, prefab, c, 0.5f, radius * 1.3f, new float4(1f, 1f, 1f, 1f), 0.08f);
            Shockwave(ecb, prefab, c, 0.15f, radius * 0.3f, radius * 1.35f, new float4(color.xyz, 0.9f), 0.3f);
            Sparks(ecb, prefab, c, 0.5f, new float2(1f, 0f), 360f, big ? 16 : 10, 6f, 16f, 0.5f, new float4(1f, 0.6f, 0.2f, 1f), 0.5f, ref rng);
            Debris(ecb, prefab, c, 0.4f, big ? 10 : 6, new float4(0.22f, 0.11f, 0.07f, 0.15f), 7f, 0.2f, 0.9f, ref rng);
            Splat(ecb, prefab, c, radius * 1.5f, new float4(0.07f, 0.045f, 0.035f, 0.08f), scorchLife, ref rng);
            int puffs = big ? 5 : 3;
            for (int i = 0; i < puffs; i++)
            {
                float s = radius * rng.NextFloat(0.35f, 0.6f);
                var p = new Particle
                {
                    Life = 0.9f, MaxLife = 0.9f, Drag = 2f, BaseScale = new float3(s), VelY = rng.NextFloat(1.2f, 2.4f),
                    Flags = PFlags.Shrink,
                };
                var v = new Visual { Mesh = (byte)ShapeId.Sphere, Scale = p.BaseScale, Y = 0.6f, Color = new float4(0.13f, 0.12f, 0.15f, 0f) };
                Emit(ecb, prefab, c + SpawnUtil.Dir(rng.NextFloat(0f, 360f)) * radius * 0.3f, SpawnUtil.Dir(rng.NextFloat(0f, 360f)) * 2f, p, v);
            }
        }

        /// <summary>A meteor falling diagonally onto <paramref name="target"/>, landing after <paramref name="delay"/> seconds.</summary>
        public static void FallingRock(EntityCommandBuffer ecb, Entity prefab, float2 target, float delay, float size, float4 color)
        {
            const float h = 16f;
            float2 offset = new float2(-4.5f, 3f);
            float2 vel = -offset / math.max(0.05f, delay);
            var p = new Particle { Life = delay, MaxLife = delay, BaseScale = new float3(size), VelY = -h / math.max(0.05f, delay) };
            var v = new Visual
            {
                Mesh = (byte)ShapeId.Sphere, Fx = VisualFx.Trail | VisualFx.Halo, Scale = p.BaseScale, Y = h, Spin = 6f, Color = color,
                Heading = -math.atan2(vel.y, vel.x), TrailLen = 3.5f,
            };
            Emit(ecb, prefab, target + offset, vel, p, v);
        }

        /// <summary>One ice spike that pops out of the floor after <paramref name="delay"/>, then shatters.</summary>
        public static void IceSpike(EntityCommandBuffer ecb, Entity prefab, float2 pos, float height, float width, float delay, float life,
            float4 color, ref Random rng)
        {
            var p = new Particle
            {
                Life = life, MaxLife = life, BaseScale = new float3(width, height, width), Freeze = delay,
                Flags = PFlags.PopIn | PFlags.Hidden | PFlags.PopOut | PFlags.ShrinkLate | PFlags.Ice,
            };
            var v = new Visual
            {
                Mesh = (byte)ShapeId.Diamond, Scale = float3.zero, Y = height * 0.25f, RotY = rng.NextFloat(0f, 6.28f), Color = color,
            };
            Emit(ecb, prefab, pos, float2.zero, p, v);
        }

        /// <summary>A glowing jagged crack in the floor (lightning rules: exaggerated corners, tapering).</summary>
        public static void Crack(EntityCommandBuffer ecb, Entity prefab, float2 from, float angle, float length, float4 color, float life,
            ref Random rng)
        {
            const int segs = 4;
            float2 p = from;
            for (int s = 0; s < segs; s++)
            {
                angle += rng.NextFloat(-38f, 38f);
                float len = length / segs * rng.NextFloat(0.7f, 1.3f);
                float2 d = SpawnUtil.Dir(angle);
                var pp = new Particle
                {
                    Life = life, MaxLife = life, BaseScale = new float3(len, 1f, 0.16f * (1f - s * 0.2f)), Alpha0 = color.w,
                    Flags = PFlags.Fade,
                };
                var v = new Visual { Mesh = (byte)ShapeId.Streak, Scale = pp.BaseScale, Y = 0.06f, RotY = -math.radians(angle), Color = color };
                Emit(ecb, prefab, p + d * len * 0.5f, float2.zero, pp, v);
                p += d * len;
            }
        }

        /// <summary>Soft drifting cloud (frost mist, dust).</summary>
        public static void Mist(EntityCommandBuffer ecb, Entity prefab, float2 pos, float size, float4 color, float life, float2 drift)
        {
            var p = new Particle
            {
                Life = life, MaxLife = life, Drag = 0.6f, BaseScale = new float3(size, 1f, size), GrowTo = 1.8f, Alpha0 = color.w,
                Flags = PFlags.Grow | PFlags.Fade,
            };
            var v = new Visual { Mesh = (byte)ShapeId.Glow, Scale = p.BaseScale, Y = 0.35f, Color = color };
            Emit(ecb, prefab, pos, drift, p, v);
        }

        /// <summary>Frost Nova: rings of spikes that grow outward, smaller spikes fanning out, cracked floor, mist, shatter.</summary>
        public static void FrostNova(EntityCommandBuffer ecb, Entity prefab, float2 c, float radius, ref Random rng)
        {
            var ice = new float4(0.62f, 0.9f, 1f, 0.55f);
            Glow(ecb, prefab, c, 0.4f, radius * 1.2f, new float4(0.5f, 0.85f, 1f, 0.9f), 0.25f);
            Shockwave(ecb, prefab, c, 0.12f, 0.6f, radius, new float4(0.6f, 0.9f, 1f, 0.9f), 0.35f);
            int rings = math.clamp((int)(radius / 1.8f), 2, 5);
            for (int r = 1; r <= rings; r++)
            {
                float t = r / (float)rings;
                float dist = radius * (0.22f + 0.78f * t);
                int count = 5 + r * 4;
                float h = 0.7f + 1.2f * t;
                for (int i = 0; i < count; i++)
                {
                    float a = (i + rng.NextFloat(-0.3f, 0.3f)) * 360f / count + r * 17f;
                    float2 p = c + SpawnUtil.Dir(a) * dist * rng.NextFloat(0.88f, 1.08f);
                    float delay = 0.045f * r + rng.NextFloat(0f, 0.03f);
                    IceSpike(ecb, prefab, p, h * rng.NextFloat(0.8f, 1.15f), 0.3f + 0.12f * t, delay, 0.5f + 0.08f * r, ice, ref rng);
                    if (rng.NextFloat() < 0.55f)
                        IceSpike(ecb, prefab, p + SpawnUtil.Dir(a + 90f) * 0.38f, h * 0.5f, 0.2f, delay + 0.02f, 0.45f + 0.08f * r, ice, ref rng);
                }
            }
            for (int k = 0; k < 7; k++)
                Crack(ecb, prefab, c, rng.NextFloat(0f, 360f), radius * rng.NextFloat(0.6f, 1f), new float4(0.45f, 0.8f, 1f, 0.7f), 1.6f, ref rng);
            for (int k = 0; k < 6; k++)
            {
                float2 p = c + SpawnUtil.Dir(rng.NextFloat(0f, 360f)) * radius * rng.NextFloat(0.2f, 0.9f);
                Mist(ecb, prefab, p, radius * rng.NextFloat(0.35f, 0.6f), new float4(0.75f, 0.9f, 1f, 0.22f), 1.3f,
                    SpawnUtil.Dir(rng.NextFloat(0f, 360f)) * 0.8f);
            }
        }

        /// <summary>Particles sucked in toward <paramref name="center"/>, arriving after <paramref name="time"/> seconds (charge-up).</summary>
        public static void Converge(EntityCommandBuffer ecb, Entity prefab, float2 center, float radius, int count, float time, float4 color,
            ref Random rng)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i * 360f / count + rng.NextFloat(-12f, 12f);
                float2 start = center + SpawnUtil.Dir(a) * radius * rng.NextFloat(0.75f, 1.2f);
                float t = time * rng.NextFloat(0.8f, 1f);
                var p = new Particle
                {
                    Life = t, MaxLife = t, BaseScale = new float3(0.45f, 1f, 0.09f), Alpha0 = color.w, Flags = PFlags.Streak,
                };
                var v = new Visual { Mesh = (byte)ShapeId.Streak, Scale = p.BaseScale, Y = 0.8f, Color = color };
                Emit(ecb, prefab, start, (center - start) / t, p, v);
            }
        }

        /// <summary>Little diamonds that float up and fade (heals, buffs).</summary>
        public static void Sparkles(EntityCommandBuffer ecb, Entity prefab, float2 pos, int count, float radius, float4 color, ref Random rng)
        {
            for (int i = 0; i < count; i++)
            {
                float l = rng.NextFloat(0.6f, 0.9f);
                var p = new Particle
                {
                    Life = l, MaxLife = l, BaseScale = new float3(rng.NextFloat(0.14f, 0.22f)), VelY = rng.NextFloat(2f, 4f),
                    Flags = PFlags.Shrink,
                };
                var v = new Visual
                {
                    Mesh = (byte)ShapeId.Diamond, Scale = p.BaseScale, Y = rng.NextFloat(0.2f, 0.8f), Spin = 4f, Color = color, Flash = 0.5f,
                };
                Emit(ecb, prefab, pos + SpawnUtil.Dir(rng.NextFloat(0f, 360f)) * rng.NextFloat(0f, radius), float2.zero, p, v);
            }
        }

        /// <summary>Big gold stars that shoot up out of an opened chest and pop.</summary>
        public static void Stars(EntityCommandBuffer ecb, Entity prefab, float2 pos, int count, ref Random rng)
        {
            for (int i = 0; i < count; i++)
            {
                float l = rng.NextFloat(0.55f, 0.85f);
                var p = new Particle
                {
                    Life = l, MaxLife = l, Drag = 1f, BaseScale = new float3(0.42f), VelY = rng.NextFloat(9f, 14f), Gravity = 22f,
                    Flags = PFlags.PopOut | PFlags.ShrinkLate,
                };
                var v = new Visual
                {
                    Mesh = (byte)ShapeId.Diamond, Fx = VisualFx.Halo, Scale = p.BaseScale, Y = 0.8f, Spin = 10f,
                    Color = new float4(1f, 0.85f, 0.3f, 1f), Flash = 1f,
                };
                Emit(ecb, prefab, pos, SpawnUtil.Dir(rng.NextFloat(0f, 360f)) * rng.NextFloat(1.5f, 4f), p, v);
            }
        }
    }
}
