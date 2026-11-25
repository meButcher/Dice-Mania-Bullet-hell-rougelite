using System.Collections.Generic;
using DiceMania.Data;
using DiceMania.ECS;
using DiceMania.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DiceMania.Game
{
    public enum CopyPhase { None, Aiming, Flying }

    public partial class GameDirector
    {
        // ---- Dice UI state (read by GameUI) ----
        public RollOutcome LastRoll { get; private set; }
        public float RollAnimTime { get; private set; }      // >0 while the dice tumble
        public float RollShowTime { get; private set; }      // >0 while the result is highlighted
        float rollCooldown;
        RollOutcome pendingRoll;

        // ---- Copy state ----
        public CopyPhase Copy { get; private set; }
        public Entity CopyTarget { get; private set; }
        public string CopyTargetName { get; private set; } = "";
        Entity copyShot, markerRing, markerBeam;
        float copyFlightRealTime;
        float copyTimeScale = 1f;

        // ================================================================== rolling

        void UpdateRolling(float rdt)
        {
            if (rollCooldown > 0f) rollCooldown -= rdt;
            if (RollShowTime > 0f) RollShowTime -= rdt;

            if (RollAnimTime > 0f)
            {
                RollAnimTime -= rdt;
                if (RollAnimTime <= 0f && pendingRoll != null)
                {
                    ApplyRoll(pendingRoll);
                    pendingRoll = null;
                }
                return;
            }

            if (GameInput.Roll) TryRoll();
        }

        public void TryRoll()
        {
            if (State != GameState.Playing || RollAnimTime > 0f || rollCooldown > 0f) return;
            if (Run.RollsLeft <= 0)
            {
                Audio.Play(SfxPlayer.Id.Deny, 0.2f);
                ShowBanner("No rolls left", new Color(1f, 0.5f, 0.5f), 0.8f);
                return;
            }
            Run.RollsLeft--;
            rollCooldown = GameDatabase.Cfg("RollCooldown", 0.5f);
            var cfg = new DiceLogic.ComboConfig
            {
                DoublesMult = GameDatabase.Cfg("DoublesPowerMult", 1.75f),
                TriplesMult = GameDatabase.Cfg("TriplesPowerMult", 3f),
                TriplesBonusRolls = Mathf.RoundToInt(GameDatabase.Cfg("TriplesBonusRolls", 1)),
                StraightBonusRolls = Mathf.RoundToInt(GameDatabase.Cfg("StraightBonusRolls", 1)),
                SnakeEyesLevelBonus = Mathf.RoundToInt(GameDatabase.Cfg("SnakeEyesLevelBonus", 2)),
            };
            pendingRoll = DiceLogic.Roll(Run.Dice, rng, Run.PipHistory, cfg);
            RollAnimTime = GameDatabase.Cfg("RollAnimSeconds", 0.45f);
            Audio.Play(SfxPlayer.Id.Roll, 0.1f);
        }

        void ApplyRoll(RollOutcome o)
        {
            LastRoll = o;
            RollShowTime = 2.5f;
            Run.RollsLeft += o.BonusRolls;

            if (o.SnakeEyes)
            {
                var h = Ecs.PlayerHealth;
                h.Value = Mathf.Max(1f, h.Value - h.Max * GameDatabase.Cfg("SnakeEyesHpCost", 0.15f));
                Ecs.SetPlayerHealth(h);
            }
            if (!string.IsNullOrEmpty(o.ComboText))
            {
                ShowBanner(o.ComboText, new Color(1f, 0.85f, 0.25f), 1.6f);
                Rig.Shake(0.25f);
            }

            foreach (var a in o.Activations)
            {
                float power = a.Power * Run.FacePower;
                ActivateFace(a.Face, a.Level, power, a.Jackpot);
                if (UnityEngine.Random.value < Run.EchoChance)
                {
                    ActivateFace(a.Face, a.Level, power, false);
                    ShowBanner("ECHO! " + a.Face.Name, a.Face.Color, 1f);
                }
            }
        }

        // ================================================================== faces

        static float4 F4(Color c, float a) => new float4(c.r, c.g, c.b, a);

        void ActivateFace(FaceDef f, int level, float power, bool jackpot)
        {
            float2 pp = Ecs.PlayerPos;
            float dur = f.DurationAt(level) * Run.Duration;
            float radius = f.RadiusAt(level) * Run.Area;
            float value = f.ValueAt(level) * power;
            int count = Mathf.Max(0, f.CountAt(level));
            if (jackpot) { count = Mathf.CeilToInt(count * 1.5f); radius *= 1.3f; }

            Floaters.Add(new FloatingText { World = new Vector3(pp.x, 2.2f, pp.y), Text = f.Name.ToUpper(), Color = f.Color, Size = 1.4f });

            switch (f.Id)
            {
                case "bolt":
                {
                    float dmg = value * Run.Might;
                    int n = Mathf.Max(1, count);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * 360f / n + UnityEngine.Random.Range(-10f, 10f);
                        float2 d = SpawnUtil.Dir(a);
                        Ecs.SpawnPlayerShot(pp, d, new Projectile
                        {
                            Kind = ProjKind.Normal, Damage = dmg, Life = 3f, Pierce = jackpot ? 2 : 0, Knockback = 1f,
                            Speed = 15f * Run.ProjectileSpeed, Homing = 1, TurnRate = 7f,
                        }, ShapeId.Diamond, 0.5f, F4(f.Color, 0.9f));
                    }
                    Audio.Play(SfxPlayer.Id.Shoot, 0.01f);
                    break;
                }
                case "copy":
                {
                    Run.CopyCharges += Mathf.Max(1, count);
                    Run.CopyLevel = Mathf.Max(Run.CopyLevel, level);
                    ShowBanner("COPY READY - hold LMB on an enemy", f.Color, 2f);
                    break;
                }
                case "mend":
                {
                    Ecs.SpawnZone(pp, new DamageZone
                    {
                        Damage = value * 0.5f, Interval = 0.5f, Timer = 0.05f, Radius = radius, Life = dur, Follow = 1,
                        Flags = ZoneFlags.Heal, Color = F4(f.Color, 0.35f),
                    });
                    Audio.Play(SfxPlayer.Id.LevelUp, 0.1f, 0.5f);
                    break;
                }
                case "twin":
                {
                    int n = Mathf.Max(1, count);
                    for (int i = 0; i < n; i++)
                        Ecs.SpawnClone(pp, dur, value, 0.45f, F4(Run.Character.Color, 0.6f), i * Mathf.PI * 2f / n);
                    break;
                }
                case "devour":
                {
                    var h = Ecs.PlayerHealth;
                    h.Value = Mathf.Max(1f, h.Value - f.HpCost);
                    Ecs.SetPlayerHealth(h);
                    Ecs.SpawnZone(pp, new DamageZone
                    {
                        Damage = value * Run.Might, Interval = 0.25f, Timer = 0.05f, Radius = 0.8f, MaxRadius = radius, GrowRate = radius * 2.2f,
                        Life = dur, Follow = 1, Flags = (byte)(ZoneFlags.Grow | ZoneFlags.ConsumeBullets | ZoneFlags.ConsumeEnemies),
                        Lifesteal = 0.05f, Knockback = 0f, Color = F4(f.Color, 0.6f),
                    });
                    Rig.Shake(0.3f);
                    Audio.Play(SfxPlayer.Id.Explode, 0.05f);
                    break;
                }
                case "reroll":
                {
                    Run.RollsLeft += Mathf.Max(1, count);
                    slowTimer = Mathf.Max(slowTimer, dur);
                    break;
                }
                case "meteor":
                {
                    using (var ep = Ecs.EnemyPositions(Allocator.Temp))
                    {
                        int n = Mathf.Max(1, count);
                        for (int i = 0; i < n; i++)
                        {
                            float2 target = ep.Length > 0 ? ep[UnityEngine.Random.Range(0, ep.Length)].Value
                                : pp + (float2)(UnityEngine.Random.insideUnitCircle * 8f);
                            Ecs.SpawnZone(target, new DamageZone
                            {
                                Damage = value * Run.Might, Radius = radius, Life = 1.1f + i * 0.12f, Delay = 0.7f + i * 0.12f,
                                Flags = ZoneFlags.SingleTick, Knockback = 6f, Color = F4(f.Color, 0.7f),
                            });
                        }
                    }
                    break;
                }
                case "frost":
                {
                    Ecs.SpawnZone(pp, new DamageZone
                    {
                        Damage = value * Run.Might, Radius = radius, Life = 0.5f, Flags = (byte)(ZoneFlags.SingleTick | ZoneFlags.Freeze),
                        FreezeTime = dur, Color = F4(f.Color, 0.6f),
                    });
                    break;
                }
                case "magnet":
                    Ecs.MagnetAllPickups();
                    break;
                case "aegis":
                {
                    var b = Ecs.Get<Buffs>(); b.Invuln = Mathf.Max(b.Invuln, dur); Ecs.Set(b);
                    Ecs.SpawnZone(pp, new DamageZone
                    {
                        Damage = value * Run.Might, Interval = 0.3f, Timer = 0.05f, Radius = radius, Life = dur, Follow = 1,
                        Flags = ZoneFlags.Aura, Knockback = 2f, Color = F4(f.Color, 0.5f),
                    });
                    break;
                }
                case "goldrush":
                {
                    var b = Ecs.Get<Buffs>(); b.GoldRush = Mathf.Max(b.GoldRush, dur); Ecs.Set(b);
                    break;
                }
                default:
                    Debug.LogWarning($"[DiceMania] Face '{f.Id}' has no effect implementation yet.");
                    break;
            }
        }

        // ================================================================== copy

        float CopyTimeScale() => Copy == CopyPhase.None ? 1f : copyTimeScale;

        bool CanCopy(EnemyDef def, bool elite)
        {
            if (def.IsBoss) return Run.CopyLevel >= GameDatabase.Cfg("CopyBossLevel", 5);
            if (elite) return Run.CopyLevel >= GameDatabase.Cfg("CopyEliteLevel", 3);
            return true;
        }

        void UpdateCopy(float rdt, Vector3 mouseWorld)
        {
            switch (Copy)
            {
                case CopyPhase.None:
                    if (Run.CopyCharges > 0 && GameInput.LmbDown && RollAnimTime <= 0f)
                    {
                        Copy = CopyPhase.Aiming;
                        copyTimeScale = GameDatabase.Cfg("CopyAimTimeScale", 0.3f);
                        markerRing = Ecs.MakeMarker(ShapeId.Ring, new float4(1f, 0.3f, 0.45f, 0.9f));
                        markerBeam = Ecs.MakeMarker(ShapeId.Cube, new float4(1f, 0.3f, 0.45f, 0.9f));
                    }
                    break;

                case CopyPhase.Aiming:
                {
                    var face = GameDatabase.Face("copy");
                    float snap = face != null ? face.RadiusAt(Run.CopyLevel) : 5f;
                    CopyTarget = FindCopyTarget(new float2(mouseWorld.x, mouseWorld.z), snap);
                    UpdateCopyMarkers();
                    if (!GameInput.LmbHeld || GameInput.LmbUp)
                    {
                        if (CopyTarget != Entity.Null) FireCopyShot();
                        else CancelCopyAim();
                    }
                    break;
                }

                case CopyPhase.Flying:
                {
                    copyFlightRealTime += rdt;
                    UpdateCopyMarkers();
                    if (copyFlightRealTime > 6f) OnCopyMiss();
                    break;
                }
            }
        }

        Entity FindCopyTarget(float2 mouse, float snap)
        {
            var em = Ecs.Em;
            Entity best = Entity.Null;
            float bd = snap * snap;
            using (var ents = Ecs.EnemyEntities(Allocator.Temp))
            using (var pos = Ecs.EnemyPositions(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    float d = math.distancesq(pos[i].Value, mouse);
                    if (d >= bd) continue;
                    var en = em.GetComponentData<Enemy>(ents[i]);
                    if (em.GetComponentData<Health>(ents[i]).Value <= 0f) continue;
                    if (en.Def < 0 || en.Def >= GameDatabase.Enemies.Count) continue;
                    if (!CanCopy(GameDatabase.Enemies[en.Def], en.Elite != 0)) continue;
                    bd = d;
                    best = ents[i];
                }
            }
            if (best != Entity.Null)
            {
                var en = em.GetComponentData<Enemy>(best);
                CopyTargetName = GameDatabase.Enemies[en.Def].Name + (en.Elite != 0 ? " (Elite)" : "");
            }
            else CopyTargetName = "";
            return best;
        }

        void UpdateCopyMarkers()
        {
            var em = Ecs.Em;
            if (markerRing == Entity.Null || !em.Exists(markerRing)) return;
            bool hasTarget = CopyTarget != Entity.Null && em.Exists(CopyTarget) && em.HasComponent<Pos>(CopyTarget);
            float2 pp = Ecs.PlayerPos;
            float2 tp = hasTarget ? em.GetComponentData<Pos>(CopyTarget).Value : pp;
            float tr = hasTarget ? em.GetComponentData<Radius>(CopyTarget).Value : 0.5f;
            float pulse = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 12f);

            var rv = em.GetComponentData<Visual>(markerRing);
            rv.Scale = hasTarget ? new float3(tr * 2.8f * pulse, 1f, tr * 2.8f * pulse) : float3.zero;
            rv.Y = 0.1f;
            em.SetComponentData(markerRing, rv);
            em.SetComponentData(markerRing, new Pos { Value = tp });

            var bv = em.GetComponentData<Visual>(markerBeam);
            float2 d = tp - pp;
            float len = math.length(d);
            bool showBeam = hasTarget && Copy == CopyPhase.Aiming && len > 0.1f;
            bv.Scale = showBeam ? new float3(len, 0.05f, 0.08f) : float3.zero;
            bv.RotY = showBeam ? -math.atan2(d.y, d.x) : 0f;
            bv.Y = 0.6f;
            em.SetComponentData(markerBeam, bv);
            em.SetComponentData(markerBeam, new Pos { Value = (pp + tp) * 0.5f });
        }

        void FireCopyShot()
        {
            Run.CopyCharges--;
            float2 pp = Ecs.PlayerPos;
            var tp = Ecs.Em.GetComponentData<Pos>(CopyTarget).Value;
            float2 d = SpawnUtil.SafeNormalize(tp - pp, new float2(1, 0));
            copyShot = Ecs.SpawnPlayerShot(pp, d, new Projectile
            {
                Kind = ProjKind.Copy, Damage = 0f, Life = 4f, Pierce = 0, Target = CopyTarget,
                Speed = GameDatabase.Cfg("CopyShotSpeed", 28),
            }, ShapeId.Diamond, 0.8f, new float4(1f, 0.3f, 0.45f, 1f));
            Copy = CopyPhase.Flying;
            copyFlightRealTime = 0f;
            copyTimeScale = GameDatabase.Cfg("CopyShotTimeScale", 0.12f);
            Rig.ZoomTarget = 0.7f;
            Audio.Play(SfxPlayer.Id.Copy, 0.1f);
        }

        void OnCopyHit(int enemyDef)
        {
            if (enemyDef < 0 || enemyDef >= GameDatabase.Enemies.Count) { OnCopyMiss(); return; }
            var def = GameDatabase.Enemies[enemyDef];
            var face = GameDatabase.Face("copy");
            int level = Run.CopyLevel;
            float duration = (face != null ? face.DurationAt(level) : 15f) * Run.Duration;
            float mult = (face != null ? face.ValueAt(level) : 1f) * Run.FacePower;

            var gun = EcsBridge.MakeShooter(def, def.CopyPattern ?? def.Pattern, 1f);
            gun.Cooldown *= GameDatabase.Cfg("CopyFireCooldownMult", 0.6f);
            gun.Timer = 0.1f;
            gun.Range = 999f;
            if (gun.Pattern == (byte)PatternType.Summon) gun.Pattern = (byte)PatternType.Ring;

            var form = new CopyForm
            {
                Time = duration, TotalTime = duration, EnemyDef = enemyDef,
                Mesh = (byte)def.Shape, Size = Mathf.Clamp(def.Size, 0.8f, 2.2f),
                Color = new float4(def.Color.r, def.Color.g, def.Color.b, 0.7f),
                SpeedMult = def.CopySpeedMult, Armor = def.CopyArmor,
                ContactDps = def.ContactDamage * def.CopyContactMult * 3f * mult,
                Gun = gun,
                BulletDamage = math.max(1f, def.BulletDamage) * def.CopyDamageMult * mult,
                BulletSpeed = math.max(12f, def.BulletSpeed * GameDatabase.Cfg("CopyBulletSpeedMult", 1.4f)),
            };
            Ecs.Em.SetComponentData(Ecs.Player, form);
            ShowBanner("YOU ARE NOW: " + def.Name.ToUpper(), new Color(1f, 0.4f, 0.55f), 2.2f);
            Audio.Play(SfxPlayer.Id.Transform);
            Rig.Shake(0.5f);
            EndCopy();
        }

        void OnCopyMiss()
        {
            if (Copy == CopyPhase.None) return;
            Run.CopyCharges++;
            ShowBanner("Copy missed - charge refunded", new Color(1f, 0.6f, 0.6f), 1.5f);
            if (copyShot != Entity.Null && Ecs.Em.Exists(copyShot)) Ecs.Em.DestroyEntity(copyShot);
            EndCopy();
        }

        void CancelCopyAim()
        {
            if (Copy == CopyPhase.Flying && copyShot != Entity.Null && Ecs.Em.Exists(copyShot))
            {
                Ecs.Em.DestroyEntity(copyShot);
                Run.CopyCharges++;
            }
            EndCopy();
        }

        void EndCopy()
        {
            Copy = CopyPhase.None;
            CopyTarget = Entity.Null;
            copyShot = Entity.Null;
            copyTimeScale = 1f;
            Rig.ZoomTarget = 1f;
            ClearCopyVisuals();
        }

        void ClearCopyVisuals()
        {
            if (Ecs == null || !Ecs.Ready) return;
            var em = Ecs.Em;
            if (markerRing != Entity.Null && em.Exists(markerRing)) em.DestroyEntity(markerRing);
            if (markerBeam != Entity.Null && em.Exists(markerBeam)) em.DestroyEntity(markerBeam);
            markerRing = markerBeam = Entity.Null;
        }
    }
}
