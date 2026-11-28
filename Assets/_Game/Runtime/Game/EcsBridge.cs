using DiceMania.Data;
using DiceMania.ECS;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DiceMania.Game
{
    /// <summary>
    /// Managed helper around the default ECS world: creates singletons and prefabs, spawns enemies / player,
    /// reads + clears event buffers. Entities are created from code (no SubScenes) so Web builds stay simple.
    /// </summary>
    public class EcsBridge
    {
        public EntityManager Em => World.DefaultGameObjectInjectionWorld.EntityManager;
        public Entity Singleton { get; private set; }
        public Entity Player { get; private set; }
        public Prefabs Prefabs { get; private set; }

        EntityQuery enemyQ, gameplayQ, enemyShotQ, decorQ, orbiterQ, auraQ, pickupQ;

        public bool Ready => World.DefaultGameObjectInjectionWorld != null && Singleton != Entity.Null;

        public void Init()
        {
            var em = Em;
            Singleton = em.CreateEntity(typeof(GameClock), typeof(PlayerStats), typeof(Buffs), typeof(GameEvents), typeof(Prefabs), typeof(Feel));
            em.AddBuffer<DamageNumber>(Singleton);
            em.AddBuffer<SpawnRequest>(Singleton);
            em.AddBuffer<SfxEvent>(Singleton);
            em.SetName(Singleton, "DiceManiaSingleton");

            var pf = new Prefabs
            {
                PlayerShot = MakePrefab(typeof(Pos), typeof(Vel), typeof(Radius), typeof(Visual), typeof(Projectile)),
                EnemyShot = MakePrefab(typeof(Pos), typeof(Vel), typeof(Visual), typeof(EnemyShot)),
                Pickup = MakePrefab(typeof(Pos), typeof(Vel), typeof(Visual), typeof(Pickup)),
                Particle = MakePrefab(typeof(Pos), typeof(Vel), typeof(Visual), typeof(Particle)),
                Zone = MakePrefab(typeof(Pos), typeof(Visual), typeof(DamageZone)),
                Enemy = MakePrefab(typeof(Pos), typeof(Radius), typeof(Visual), typeof(Health), typeof(Enemy), typeof(Shooter)),
                Orbiter = MakePrefab(typeof(Pos), typeof(Radius), typeof(Visual), typeof(Projectile), typeof(Orbiter)),
                Clone = MakePrefab(typeof(Pos), typeof(Visual), typeof(CloneUnit)),
            };
            Prefabs = pf;
            em.SetComponentData(Singleton, pf);

            enemyQ = em.CreateEntityQuery(ComponentType.ReadOnly<Enemy>(), ComponentType.ReadOnly<Pos>(), ComponentType.ReadOnly<Health>());
            enemyShotQ = em.CreateEntityQuery(ComponentType.ReadOnly<EnemyShot>());
            decorQ = em.CreateEntityQuery(ComponentType.ReadOnly<Decor>());
            orbiterQ = em.CreateEntityQuery(ComponentType.ReadOnly<Orbiter>());
            pickupQ = em.CreateEntityQuery(ComponentType.ReadOnly<Pickup>());
            auraQ = em.CreateEntityQuery(ComponentType.ReadOnly<DamageZone>());
            gameplayQ = em.CreateEntityQuery(new EntityQueryDesc
            {
                Any = new ComponentType[]
                {
                    typeof(Enemy), typeof(Projectile), typeof(EnemyShot), typeof(Pickup), typeof(Particle),
                    typeof(DamageZone), typeof(CloneUnit), typeof(PlayerTag), typeof(PlayerGun),
                },
            });
        }

        Entity MakePrefab(params ComponentType[] types)
        {
            var e = Em.CreateEntity(types);
            Em.AddComponent<Prefab>(e);
            return e;
        }

        public T Get<T>() where T : unmanaged, IComponentData => Em.GetComponentData<T>(Singleton);
        public void Set<T>(T v) where T : unmanaged, IComponentData => Em.SetComponentData(Singleton, v);

        public void ApplyConfig(bool running)
        {
            var c = Get<GameClock>();
            c.Running = (byte)(running ? 1 : 0);
            c.ArenaHalf = GameDatabase.Cfg("ArenaHalfSize", 55);
            c.EnemyBulletLife = GameDatabase.Cfg("EnemyBulletLife", 6);
            c.PlayerRadius = GameDatabase.Cfg("PlayerRadius", 0.5f);
            c.CritMult = GameDatabase.Cfg("CritMultiplier", 2);
            c.HealPickupChance = GameDatabase.Cfg("HealPickupChance", 0.004f);
            c.HealPickupAmount = GameDatabase.Cfg("HealPickupAmount", 20);
            c.ChestGold = GameDatabase.Cfg("ChestGold", 20);
            c.MagnetSpeed = GameDatabase.Cfg("MagnetSpeed", 24);
            c.InvulnTime = GameDatabase.Cfg("PlayerInvulnTime", 0.6f);
            Set(c);

            Set(new Feel
            {
                HitStopHurt = GameDatabase.Cfg("HitStopHurt", 0.08f),
                HitStopKill = GameDatabase.Cfg("HitStopKill", 0.025f),
                HitStopElite = GameDatabase.Cfg("HitStopElite", 0.06f),
                HitStopBoss = GameDatabase.Cfg("HitStopBoss", 0.35f),
                CorpseFreeze = GameDatabase.Cfg("CorpseFreeze", 0.07f),
                CorpseThrow = GameDatabase.Cfg("CorpseThrow", 13f),
                SplatLife = GameDatabase.Cfg("SplatLife", 14f),
                ShellLife = GameDatabase.Cfg("ShellLife", 8f),
                Recoil = GameDatabase.Cfg("Recoil", 1f),
                Kick = GameDatabase.Cfg("CameraKick", 1f),
                Shake = GameDatabase.Cfg("ScreenShake", 1f),
            });
        }

        public void SetRunning(bool running)
        {
            var c = Get<GameClock>();
            c.Running = (byte)(running ? 1 : 0);
            Set(c);
        }

        // ------------------------------------------------------------------ player

        public void SpawnPlayer(CharacterDef ch, float maxHp)
        {
            var em = Em;
            Player = em.CreateEntity(typeof(PlayerTag), typeof(Pos), typeof(Radius), typeof(Visual), typeof(Health), typeof(PlayerData), typeof(CopyForm));
            em.AddBuffer<WeaponSlot>(Player);
            em.SetName(Player, "Player");
            float r = GameDatabase.Cfg("PlayerRadius", 0.5f);
            var col = new float4(ch.Color.r, ch.Color.g, ch.Color.b, 0.5f);
            em.SetComponentData(Player, new Pos { Value = float2.zero });
            em.SetComponentData(Player, new Radius { Value = r });
            em.SetComponentData(Player, new Visual { Mesh = (byte)ShapeId.Sphere, Scale = new float3(r * 2), Y = r, Color = col });
            em.SetComponentData(Player, new Health { Value = maxHp, Max = maxHp });
            em.SetComponentData(Player, new PlayerData { Speed = ch.MoveSpeed, AimDir = new float2(1, 0), BaseColor = col });

            // The little gun that kicks back and flashes on every shot.
            var gun = em.CreateEntity(typeof(Pos), typeof(Visual), typeof(PlayerGun));
            em.SetName(gun, "PlayerGun");
            em.SetComponentData(gun, new Pos { Value = new float2(0.68f, 0f) });
            em.SetComponentData(gun, new PlayerGun { Dir = new float2(1, 0) });
            em.SetComponentData(gun, new Visual
            {
                Mesh = (byte)ShapeId.Cube, Scale = new float3(0.5f, 0.2f, 0.2f), Y = 0.55f, Color = new float4(0.85f, 0.92f, 1f, 0.35f),
            });
        }

        /// <summary>Hides the hero's gun right away (death); the WeaponSystem keeps it hidden afterwards.</summary>
        public void HideGun()
        {
            ForGun((ref PlayerGun g, ref Visual v) => v.Scale = float3.zero);
        }

        delegate void GunAction(ref PlayerGun gun, ref Visual vis);

        void ForGun(GunAction action)
        {
            var q = Em.CreateEntityQuery(typeof(PlayerGun), typeof(Visual));
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    var g = Em.GetComponentData<PlayerGun>(e);
                    var v = Em.GetComponentData<Visual>(e);
                    action(ref g, ref v);
                    Em.SetComponentData(e, g);
                    Em.SetComponentData(e, v);
                }
        }

        /// <summary>The cube beside the hero shows the current ability: its colour, dim while recharging, flash when ready.</summary>
        public void SetGunStyle(float4 color, float flash)
        {
            ForGun((ref PlayerGun g, ref Visual v) => { v.Color = color; v.Flash = math.max(v.Flash, flash); });
        }

        /// <summary>Kick the hero's gun back along <paramref name="dir"/> (managed abilities like Bullet / Melee).</summary>
        public void KickGun(float2 dir)
        {
            ForGun((ref PlayerGun g, ref Visual v) => { g.Dir = dir; g.Kick = 1f; });
        }

        public void AddRecoil(float2 impulse)
        {
            if (Player == Entity.Null) return;
            var pd = Em.GetComponentData<PlayerData>(Player);
            pd.Recoil += impulse;
            Em.SetComponentData(Player, pd);
        }

        public float2 AimDir => Player != Entity.Null ? Em.GetComponentData<PlayerData>(Player).AimDir : new float2(1f, 0f);

        public void SetShotScale(Entity shot, float3 scale)
        {
            var v = Em.GetComponentData<Visual>(shot);
            v.Scale = scale;
            Em.SetComponentData(shot, v);
        }

        public float2 PlayerPos => Player != Entity.Null && Em.Exists(Player) ? Em.GetComponentData<Pos>(Player).Value : float2.zero;
        public Health PlayerHealth => Em.GetComponentData<Health>(Player);
        public void SetPlayerHealth(Health h) => Em.SetComponentData(Player, h);
        public CopyForm PlayerCopy => Em.GetComponentData<CopyForm>(Player);

        public void SetPlayerInput(float2 move, float2 aimPoint)
        {
            var pd = Em.GetComponentData<PlayerData>(Player);
            pd.MoveInput = move;
            float2 p = PlayerPos;
            float2 d = aimPoint - p;
            if (math.lengthsq(d) > 0.01f) pd.AimDir = math.normalize(d);
            pd.AimPoint = aimPoint;
            Em.SetComponentData(Player, pd);
        }

        // ------------------------------------------------------------------ enemies

        public int EnemyCount => enemyQ.CalculateEntityCount();

        public Entity SpawnEnemy(EnemyDef def, float2 pos, bool elite, float hpMult, float dmgMult, float speedMult)
        {
            var em = Em;
            var e = em.Instantiate(Prefabs.Enemy);
            float size = def.Size * (elite ? GameDatabase.Cfg("EliteSizeMult", 1.45f) : 1f);
            float hp = def.HP * hpMult * (elite ? GameDatabase.Cfg("EliteHpMult", 4) : 1f);
            float dmg = dmgMult * (elite ? GameDatabase.Cfg("EliteDamageMult", 1.5f) : 1f);
            var col = new float4(def.Color.r, def.Color.g, def.Color.b, elite ? 0.75f : def.IsBoss ? 0.45f : 0.3f);
            if (elite) col.xyz = math.lerp(col.xyz, new float3(1f, 0.9f, 0.3f), 0.35f);

            em.SetComponentData(e, new Pos { Value = pos });
            em.SetComponentData(e, new Radius { Value = size * 0.5f });
            em.SetComponentData(e, new Health { Value = hp, Max = hp });
            em.SetComponentData(e, new Visual { Mesh = (byte)def.Shape, Scale = new float3(size), Y = size * 0.5f, Color = col });
            em.SetComponentData(e, new Enemy
            {
                Def = def.Index, Behaviour = (byte)def.Behaviour, Speed = def.Speed * speedMult,
                ContactDamage = def.ContactDamage * dmg, PreferredRange = def.PreferredRange,
                Elite = (byte)(elite ? 1 : 0), Boss = (byte)(def.IsBoss ? 1 : 0),
                Xp = def.XP * (elite ? 5 : 1), GoldChance = elite ? 1f : def.GoldChance, Gold = def.Gold * (elite ? 3 : 1),
                Score = def.Score * (elite ? 5 : 1),
                SplitDef = GameDatabase.Enemy(def.SplitInto)?.Index ?? -1, SplitCount = def.SplitCount,
                BaseColor = col, Seed = UnityEngine.Random.value * 100f,
            });
            var gun = MakeShooter(def, def.Pattern, dmg);
            gun.Timer = UnityEngine.Random.Range(0.5f, math.max(0.6f, gun.Cooldown));
            em.SetComponentData(e, gun);

            if (def.IsBoss)
            {
                em.AddComponentData(e, new BossState { Index = -1, Timer = 0f });
                var buf = em.AddBuffer<BossPhase>(e);
                foreach (var ph in def.BossPhases)
                    buf.Add(new BossPhase { Gun = MakeShooter(def, ph.Pattern, dmg), Duration = ph.Duration });
            }
            return e;
        }

        public static Shooter MakeShooter(EnemyDef def, PatternDef p, float dmgMult)
        {
            if (p == null) return new Shooter { Pattern = (byte)PatternType.None };
            var s = new Shooter
            {
                Pattern = (byte)p.Type, Count = p.Count, Spread = p.Spread, Spin = p.Spin,
                BulletSpeed = def.BulletSpeed * p.SpeedMult, Damage = def.BulletDamage * dmgMult,
                Cooldown = p.Cooldown > 0f ? p.Cooldown : def.FireCooldown,
                Bursts = math.max(1, p.Bursts), BurstInterval = p.BurstInterval, Range = def.Range,
                SummonDef = GameDatabase.Enemy(p.SummonEnemy)?.Index ?? -1,
            };
            if (p.Type == PatternType.Summon) s.Count = math.max(1, p.Count);
            return s;
        }

        /// <summary>Finds the first living boss. Returns false if none.</summary>
        public bool FindBoss(out Health hp, out int def)
        {
            hp = default; def = -1;
            using (var en = enemyQ.ToComponentDataArray<Enemy>(Allocator.Temp))
            using (var h = enemyQ.ToComponentDataArray<Health>(Allocator.Temp))
            {
                for (int i = 0; i < en.Length; i++)
                {
                    if (en[i].Boss != 0 && h[i].Value > 0f) { hp = h[i]; def = en[i].Def; return true; }
                }
            }
            return false;
        }

        public NativeArray<Entity> EnemyEntities(Allocator a) => enemyQ.ToEntityArray(a);
        public NativeArray<Pos> EnemyPositions(Allocator a) => enemyQ.ToComponentDataArray<Pos>(a);

        public void KillAllEnemies(bool particles)
        {
            var em = Em;
            using (var ents = enemyQ.ToEntityArray(Allocator.Temp))
            using (var pos = enemyQ.ToComponentDataArray<Pos>(Allocator.Temp))
            {
                if (particles)
                {
                    var ecbRng = Unity.Mathematics.Random.CreateFromIndex((uint)Time.frameCount + 1);
                    var ecb = new EntityCommandBuffer(Allocator.Temp);
                    for (int i = 0; i < ents.Length; i++)
                    {
                        var vis = em.GetComponentData<Visual>(ents[i]);
                        SpawnUtil.Burst(ecb, Prefabs.Particle, pos[i].Value, 4, vis.Color, 6f, 0.25f, 0.5f, ref ecbRng);
                    }
                    ecb.Playback(em);
                    ecb.Dispose();
                }
                em.DestroyEntity(ents);
            }
            em.DestroyEntity(enemyShotQ);
        }

        public void MagnetAllPickups()
        {
            var b = Get<Buffs>();
            b.MagnetPulse = 1;
            Set(b);
        }

        public int PickupCount => pickupQ.CalculateEntityCount();

        public void ClearRun()
        {
            Em.DestroyEntity(gameplayQ);
            Player = Entity.Null;
            Em.GetBuffer<DamageNumber>(Singleton).Clear();
            Em.GetBuffer<SpawnRequest>(Singleton).Clear();
            Em.GetBuffer<SfxEvent>(Singleton).Clear();
            Set(new GameEvents());
            Set(new Buffs());
        }

        // ------------------------------------------------------------------ weapons

        public void SetWeapons(RunState run)
        {
            var em = Em;
            // Keep timers of existing slots where possible.
            var old = em.GetBuffer<WeaponSlot>(Player).ToNativeArray(Allocator.Temp);
            var slots = new NativeList<WeaponSlot>(Allocator.Temp);
            em.DestroyEntity(orbiterQ);
            // Remove aura zones (they are re-created below).
            using (var zones = auraQ.ToEntityArray(Allocator.Temp))
                foreach (var z in zones)
                    if ((em.GetComponentData<DamageZone>(z).Flags & ZoneFlags.Aura) != 0 && em.GetComponentData<DamageZone>(z).Life < 0f)
                        em.DestroyEntity(z);

            for (int i = 0; i < run.Weapons.Count; i++)
            {
                var ow = run.Weapons[i];
                var d = ow.Def;
                int l = ow.Level;
                var slot = new WeaponSlot
                {
                    Kind = (byte)d.Kind,
                    Damage = Lvl.At(d.Damage, d.DamagePerLevel, l) * run.Might,
                    Cooldown = math.max(0.05f, d.Cooldown * math.max(0.2f, 1f - d.CooldownReductionPerLevel * (l - 1)) * run.CooldownMult),
                    Count = math.max(d.Kind == WeaponKind.Aura ? 0 : 1, Lvl.AtInt(d.Count, d.CountPerLevel, l) + (d.Kind == WeaponKind.Aura ? 0 : run.ProjectileCount)),
                    Speed = d.Speed * (d.Kind == WeaponKind.Orbit ? 1f : run.ProjectileSpeed),
                    Pierce = Lvl.AtInt(d.Pierce, d.PiercePerLevel, l),
                    Area = Lvl.At(d.Area, d.AreaPerLevel, l) * run.Area,
                    Life = d.Life * run.Duration,
                    Knockback = d.Knockback,
                    Color = new float4(d.Color.r, d.Color.g, d.Color.b, 0.7f),
                    Evolved = (byte)(d.IsEvolution ? 1 : 0),
                    Timer = i < old.Length ? old[i].Timer : 0.3f,
                    Recoil = d.Recoil, Kick = d.Kick, Shake = d.Shake, Shell = (byte)math.clamp(d.Shell, 0, 2),
                };
                slots.Add(slot);

                if (d.Kind == WeaponKind.Orbit)
                {
                    for (int k = 0; k < slot.Count; k++)
                    {
                        var e = em.Instantiate(Prefabs.Orbiter);
                        float size = 0.55f * math.sqrt(run.Area);
                        em.SetComponentData(e, new Pos { Value = PlayerPos });
                        em.SetComponentData(e, new Radius { Value = size * 0.6f });
                        em.SetComponentData(e, new Visual { Mesh = (byte)ShapeId.Cube, Scale = new float3(size), Y = 0.7f, Spin = 5f, Color = slot.Color });
                        em.SetComponentData(e, new Projectile
                        {
                            Kind = ProjKind.Orbit, Damage = slot.Damage, Life = 1e9f, Pierce = 9999, Knockback = slot.Knockback,
                        });
                        em.SetComponentData(e, new Orbiter
                        {
                            Angle = k * math.PI * 2f / slot.Count, Distance = slot.Area, AngularSpeed = slot.Speed,
                        });
                    }
                }
                else if (d.Kind == WeaponKind.Aura)
                {
                    var z = em.Instantiate(Prefabs.Zone);
                    var zone = new DamageZone
                    {
                        Damage = slot.Damage, Interval = slot.Cooldown, Timer = slot.Cooldown, Radius = slot.Area, Life = -1f,
                        Follow = 1, Flags = ZoneFlags.Aura, Knockback = slot.Knockback, Color = new float4(slot.Color.xyz, 0.35f),
                        Lifesteal = d.IsEvolution ? 0.02f : 0f,
                    };
                    em.SetComponentData(z, new Pos { Value = PlayerPos });
                    em.SetComponentData(z, zone);
                    em.SetComponentData(z, new Visual { Mesh = (byte)ShapeId.Ring, Scale = new float3(slot.Area * 2f, 1, slot.Area * 2f), Y = 0.05f, Color = zone.Color });
                }
            }
            var buf = em.GetBuffer<WeaponSlot>(Player);
            buf.Clear();
            for (int i = 0; i < slots.Length; i++) buf.Add(slots[i]);
            old.Dispose();
            slots.Dispose();
        }

        public void PushStats(RunState run)
        {
            Set(new PlayerStats
            {
                Might = run.Might, Area = run.Area, ProjSpeed = run.ProjectileSpeed, Duration = run.Duration, Luck = run.Luck,
                Armor = run.Armor, Regen = run.Regen, PickupRadius = run.PickupRadius, Thorns = run.Thorns, Greed = run.Greed,
            });
            if (Player == Entity.Null) return;
            var pd = Em.GetComponentData<PlayerData>(Player);
            pd.Speed = run.MoveSpeed;
            Em.SetComponentData(Player, pd);
            var h = Em.GetComponentData<Health>(Player);
            float delta = run.MaxHP - h.Max;
            h.Max = run.MaxHP;
            if (delta > 0) h.Value += delta;
            h.Value = math.min(h.Value, h.Max);
            Em.SetComponentData(Player, h);
        }

        // ------------------------------------------------------------------ arena

        public void BuildArena()
        {
            var em = Em;
            em.DestroyEntity(decorQ);
            float half = GameDatabase.Cfg("ArenaHalfSize", 55);
            var rnd = new Unity.Mathematics.Random(12345);

            Entity Prop(float2 p, ShapeId mesh, float3 scale, float y, float rot, float4 col, float spin = 0f)
            {
                var e = em.CreateEntity(typeof(Pos), typeof(Visual), typeof(Decor));
                em.SetComponentData(e, new Pos { Value = p });
                em.SetComponentData(e, new Visual { Mesh = (byte)mesh, Scale = scale, Y = y, RotY = rot, Color = col, Spin = spin });
                return e;
            }

            // Cobblestones in winding paths + scattered.
            for (int path = 0; path < 4; path++)
            {
                float2 p = rnd.NextFloat2(-half * 0.8f, half * 0.8f);
                float ang = rnd.NextFloat(0, 6.28f);
                for (int i = 0; i < 90; i++)
                {
                    ang += rnd.NextFloat(-0.35f, 0.35f);
                    p += new float2(math.cos(ang), math.sin(ang)) * 1.3f;
                    if (math.any(math.abs(p) > half - 2)) { ang += math.PI; continue; }
                    for (int k = 0; k < 2; k++)
                    {
                        float2 q = p + rnd.NextFloat2(-1.2f, 1.2f);
                        float s = rnd.NextFloat(0.6f, 1.3f);
                        float g = rnd.NextFloat(0.10f, 0.16f);
                        Prop(q, ShapeId.Cube, new float3(s, 0.08f, s * rnd.NextFloat(0.6f, 1f)), 0.0f, rnd.NextFloat(0, 6.28f), new float4(g * 0.8f, g * 0.85f, g * 1.4f, 0));
                    }
                }
            }
            // Grass tufts.
            for (int i = 0; i < 380; i++)
            {
                float2 p = rnd.NextFloat2(-half + 1, half - 1);
                float s = rnd.NextFloat(0.15f, 0.3f);
                Prop(p, ShapeId.Diamond, new float3(s, s * 2.2f, s), s * 0.6f, rnd.NextFloat(0, 6.28f), new float4(0.08f, rnd.NextFloat(0.18f, 0.3f), 0.18f, 0));
            }
            // Neon campfires (the original's signature landmarks).
            float4[] fireCols =
            {
                new float4(0.75f, 1f, 0.2f, 1), new float4(0.2f, 0.7f, 1f, 1), new float4(0.6f, 0.3f, 1f, 1), new float4(1f, 0.3f, 0.75f, 1),
            };
            for (int i = 0; i < 10; i++)
            {
                float a = i * math.PI * 2f / 10f + rnd.NextFloat(-0.2f, 0.2f);
                float r = (i % 2 == 0 ? 0.45f : 0.8f) * half;
                float2 c = new float2(math.cos(a), math.sin(a)) * r;
                var col = fireCols[i % fireCols.Length];
                Prop(c, ShapeId.Disc, new float3(10f, 1f, 10f), 0.02f, 0, new float4(col.xyz, 0.12f));
                Prop(c + new float2(-0.3f, 0), ShapeId.Cube, new float3(1.4f, 0.2f, 0.25f), 0.1f, 0.6f, new float4(0.25f, 0.12f, 0.1f, 0));
                Prop(c + new float2(0.3f, 0), ShapeId.Cube, new float3(1.4f, 0.2f, 0.25f), 0.1f, -0.6f, new float4(0.25f, 0.12f, 0.1f, 0));
                for (int k = 0; k < 6; k++)
                {
                    float s = rnd.NextFloat(0.25f, 0.6f);
                    Prop(c + rnd.NextFloat2(-0.35f, 0.35f), ShapeId.Cube, new float3(s), rnd.NextFloat(0.3f, 1.4f), rnd.NextFloat(0, 6.28f), col, rnd.NextFloat(-3, 3));
                }
            }
            // Arena wall.
            for (float x = -half; x <= half; x += 2f)
            {
                float4 wc = new float4(0.16f, 0.1f, 0.3f, 0.1f);
                Prop(new float2(x, half), ShapeId.Cube, new float3(2f, 1.2f, 1f), 0.6f, 0, wc);
                Prop(new float2(x, -half), ShapeId.Cube, new float3(2f, 1.2f, 1f), 0.6f, 0, wc);
                Prop(new float2(half, x), ShapeId.Cube, new float3(1f, 1.2f, 2f), 0.6f, 0, wc);
                Prop(new float2(-half, x), ShapeId.Cube, new float3(1f, 1.2f, 2f), 0.6f, 0, wc);
            }
        }

        // ------------------------------------------------------------------ zones / effects helpers

        public Entity SpawnZone(float2 pos, DamageZone z)
        {
            var e = Em.Instantiate(Prefabs.Zone);
            Em.SetComponentData(e, new Pos { Value = pos });
            Em.SetComponentData(e, z);
            Em.SetComponentData(e, new Visual { Mesh = (byte)((z.Flags & ZoneFlags.Aura) != 0 ? ShapeId.Ring : ShapeId.Disc), Scale = new float3(z.Radius * 2, 1, z.Radius * 2), Y = 0.05f, Color = z.Color });
            return e;
        }

        public Entity SpawnPlayerShot(float2 pos, float2 dir, Projectile p, ShapeId mesh, float size, float4 color, bool halo = false)
        {
            var e = Em.Instantiate(Prefabs.PlayerShot);
            float heading = -math.atan2(dir.y, dir.x);
            Em.SetComponentData(e, new Pos { Value = pos });
            Em.SetComponentData(e, new Vel { Value = dir * p.Speed });
            Em.SetComponentData(e, p);
            Em.SetComponentData(e, new Radius { Value = size * 0.5f });
            Em.SetComponentData(e, new Visual
            {
                Mesh = (byte)mesh, Fx = (byte)(VisualFx.Trail | (halo ? VisualFx.Halo : 0)), Scale = new float3(size), Y = 0.7f,
                RotY = heading, Heading = heading, TrailLen = math.min(1.8f, p.Speed * 0.05f), Color = color,
            });
            return e;
        }

        public void SpawnClone(float2 pos, float life, float damage, float cooldown, float4 color, float angle)
        {
            var e = Em.Instantiate(Prefabs.Clone);
            Em.SetComponentData(e, new Pos { Value = pos });
            Em.SetComponentData(e, new CloneUnit { Life = life, FireCooldown = cooldown, Damage = damage, OrbitAngle = angle, FireTimer = 0.2f });
            Em.SetComponentData(e, new Visual { Mesh = (byte)ShapeId.Sphere, Scale = new float3(0.75f), Y = 0.4f, Color = color, SquashAmp = 1f });
        }

        // ------------------------------------------------------------------ managed access to the Fx library

        Unity.Mathematics.Random fxRng = Unity.Mathematics.Random.CreateFromIndex(4711);

        /// <summary>Spawn effects from managed code: <c>Fx(( ecb, prefab, ref rng) => DiceMania.ECS.Fx.Glow(ecb, prefab, ...))</c>.</summary>
        public delegate void FxAction(EntityCommandBuffer ecb, Entity particlePrefab, ref Unity.Mathematics.Random rng);

        public void Fx(FxAction action)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            action(ecb, Prefabs.Particle, ref fxRng);
            ecb.Playback(Em);
            ecb.Dispose();
        }

        public void Burst(float2 pos, int count, float4 color, float speed, float size, float life)
        {
            Fx((EntityCommandBuffer ecb, Entity prefab, ref Unity.Mathematics.Random r) =>
                SpawnUtil.Burst(ecb, prefab, pos, count, color, speed, size, life, ref r));
        }

        /// <summary>A temporary marker entity used for UI-ish world visuals (copy target ring, aim beam).</summary>
        public Entity MakeMarker(ShapeId mesh, float4 color)
        {
            var e = Em.CreateEntity(typeof(Pos), typeof(Visual));
            Em.SetComponentData(e, new Visual { Mesh = (byte)mesh, Scale = new float3(1), Y = 0.1f, Color = color });
            return e;
        }
    }
}
