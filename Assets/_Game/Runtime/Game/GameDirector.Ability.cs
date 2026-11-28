using DiceMania.Data;
using DiceMania.ECS;
using DiceMania.Presentation;
using Unity.Mathematics;
using UnityEngine;

namespace DiceMania.Game
{
    /// <summary>The face you rolled: your current ability until the next roll. Used by clicking / tapping, with a cooldown.</summary>
    public class ActiveAbility
    {
        public FaceDef Face;
        public int Level;
        public float Power;         // combo multiplier x face power
        public bool Jackpot;
        public int UsesLeft;        // -1 = unlimited (cooldown only)
        public float Cooldown;      // seconds between uses
        public float CooldownLeft;
        public float ReadyPulse;    // UI pulse when it comes off cooldown
        public float UsedPulse;     // UI pulse on use
        public float DenyPulse;     // UI shake when clicked during the cooldown

        public bool Ready => CooldownLeft <= 0f;
        public float CooldownFrac => Cooldown > 0f ? Mathf.Clamp01(CooldownLeft / Cooldown) : 0f;
    }

    public partial class GameDirector
    {
        public ActiveAbility Ability { get; private set; }
        bool meleeFlip;
        bool pressStartedOnHud;
        static readonly float4 IdleGunColor = new float4(0.85f, 0.92f, 1f, 0.35f);

        /// <summary>After the dice land: the strongest rolled face (most matches, then highest level) becomes the ability.</summary>
        void SetAbility(RollOutcome o)
        {
            FaceActivation best = null;
            foreach (var a in o.Activations)
                if (best == null || a.Matches > best.Matches || (a.Matches == best.Matches && a.Level > best.Level)) best = a;
            if (best == null) { SpendAbility(); return; }
            var f = best.Face;
            Ability = new ActiveAbility
            {
                Face = f, Level = best.Level, Power = best.Power * Run.FacePower, Jackpot = best.Jackpot,
                UsesLeft = f.Uses > 0 ? f.Uses + (best.Jackpot ? 1 : 0) : -1,
                Cooldown = f.CooldownAt(best.Level), CooldownLeft = 0f, ReadyPulse = 1f,
            };
            if (f.Id == "copy") Run.CopyLevel = Mathf.Max(1, best.Level);
            float2 pp = Ecs.PlayerPos;
            Floaters.RemoveAll(x => x.AbilityName);
            Floaters.Add(new FloatingText
            {
                World = new Vector3(pp.x, 2.2f, pp.y), Vel = new Vector3(0f, 3f, 0f), Text = f.Name.ToUpper(), Color = f.Color,
                Size = 1.5f, Pop = 1.2f, Life = 1.1f, AbilityName = true,
            });
            string combo = string.IsNullOrEmpty(o.ComboText) ? "" : o.ComboText + "  ";
            string hint = f.Id == "copy" ? "  -  hold on an enemy, release" : "  -  click / tap to use";
            ShowBanner(combo + f.Name.ToUpper() + (MetaProgress.Data.SeenTutorial ? "" : hint), f.Color, 1.8f);
            Ecs.SetGunStyle(F4(f.Color, 0.75f), 1f);
        }

        void SpendAbility()
        {
            Ability = null;
            if (Ecs != null && Ecs.Player != Unity.Entities.Entity.Null) Ecs.SetGunStyle(IdleGunColor, 0f);
        }

        /// <summary>Clicking / tapping the dice circles (bottom corners) rolls instead of using the ability.</summary>
        public static bool IsOverRollButton(Vector2 screen)
        {
            float s = Screen.height / 720f;
            return screen.magnitude < 125f * s || Vector2.Distance(screen, new Vector2(Screen.width, 0f)) < 135f * s;
        }

        void UpdateAbility(float rdt, Vector3 mouseWorld)
        {
            if (GameInput.LmbDown) pressStartedOnHud = IsOverRollButton(GameInput.MousePos) && !GameInput.PadAiming;
            if (GameInput.LmbDown && pressStartedOnHud) TryRoll();

            var a = Ability;
            if (a == null) return;
            a.ReadyPulse = Mathf.Max(0f, a.ReadyPulse - rdt * 2.5f);
            a.UsedPulse = Mathf.Max(0f, a.UsedPulse - rdt * 4f);
            a.DenyPulse = Mathf.Max(0f, a.DenyPulse - rdt * 4f);
            if (a.CooldownLeft > 0f)
            {
                // Game time: freezes, slow motion and pauses hold the cooldown too.
                a.CooldownLeft -= Time.deltaTime;
                if (a.CooldownLeft <= 0f)
                {
                    a.CooldownLeft = 0f;
                    a.ReadyPulse = 1f;
                    Ecs.SetGunStyle(F4(a.Face.Color, 0.75f), 1f);
                    Audio.Play(SfxPlayer.Id.Ready, 0.05f, 0.45f);
                }
            }
            if (a.Face.Id == "copy") return; // Morph is hold-to-aim, release-to-fire (UpdateCopy)
            if (RollAnimTime > 0f || pressStartedOnHud) return;
            // A press always counts (a quick tap can start and end inside one frame); Auto faces also repeat while held.
            bool press = GameInput.LmbDown || (a.Face.Auto && GameInput.LmbHeld);
            if (!press) return;
            if (!a.Ready)
            {
                if (GameInput.LmbDown) { a.DenyPulse = 1f; Audio.Play(SfxPlayer.Id.Deny, 0.15f, 0.35f); }
                return;
            }
            UseAbility(new float2(mouseWorld.x, mouseWorld.z));
        }

        void UseAbility(float2 aim)
        {
            var a = Ability;
            if (a == null) return;
            ActivateFace(a.Face, a.Level, a.Power, a.Jackpot, aim);
            if (UnityEngine.Random.value < Run.EchoChance)
            {
                ActivateFace(a.Face, a.Level, a.Power, false, aim);
                ShowBanner("ECHO! " + a.Face.Name, a.Face.Color, 1f);
            }
            StartAbilityCooldown();
            if (a.UsesLeft > 0 && --a.UsesLeft == 0) SpendAbility();
        }

        void StartAbilityCooldown()
        {
            var a = Ability;
            if (a == null) return;
            a.CooldownLeft = a.Cooldown;
            a.UsedPulse = 1f;
            if (a.Cooldown > 0.2f) Ecs.SetGunStyle(F4(a.Face.Color, 0.12f), 0f); // dim while it recharges
        }

        // ------------------------------------------------------------------ gun-type / melee faces

        static readonly float4 MissileColor = new float4(1f, 0.5f, 0.2f, 1f);

        /// <summary>Bullet: missiles at the cursor with the full gun treatment (kick, recoil, camera kick, shells).</summary>
        void FireBullet(float2 pp, float2 aim, float dmg, int count, bool jackpot)
        {
            float2 dir = SpawnUtil.SafeNormalize(aim - pp, Ecs.AimDir);
            int n = Mathf.Max(1, count);
            float spread = n > 1 ? 9f : 0f;
            float baseA = SpawnUtil.Angle(dir) - spread * (n - 1) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float2 d = SpawnUtil.Dir(baseA + spread * i + UnityEngine.Random.Range(-2f, 2f));
                var e = Ecs.SpawnPlayerShot(pp + d * 0.8f, d, new Projectile
                {
                    Kind = ProjKind.Normal, Damage = dmg, Life = 1.4f, Pierce = jackpot ? 3 : 1, Knockback = 1.4f,
                    Speed = 32f * Run.ProjectileSpeed, Homing = 1, TurnRate = 2.5f,
                }, ShapeId.Diamond, 0.5f, MissileColor, halo: true);
                Ecs.SetShotScale(e, new float3(0.95f, 0.36f, 0.36f));
            }
            float recoil = GameDatabase.Cfg("Recoil", 1f), kick = GameDatabase.Cfg("CameraKick", 1f);
            Ecs.KickGun(dir);
            Ecs.AddRecoil(-dir * 4.5f * recoil);
            Rig.Kick(new Vector2(dir.x, dir.y), 0.45f * kick);
            Rig.Vibrate(0.22f * GameDatabase.Cfg("ScreenShake", 1f));
            Rumble.Pulse(0.45f, 0.08f);
            float shellLife = GameDatabase.Cfg("ShellLife", 8f);
            Ecs.Fx((Unity.Entities.EntityCommandBuffer ecb, Unity.Entities.Entity pf, ref Unity.Mathematics.Random r) =>
            {
                Fx.Muzzle(ecb, pf, pp + dir * 0.85f, dir, 1.7f, MissileColor, ref r);
                Fx.Shell(ecb, pf, pp + dir * 0.3f, dir, true, shellLife, ref r);
            });
            Audio.Play(SfxPlayer.Id.Shoot, 0.02f, 0.9f, 0.8f);
            Audio.Play(SfxPlayer.Id.Heavy, 0.05f, 0.45f, 1.3f);
        }

        /// <summary>Melee: a slash arc toward the cursor; the hero lunges into the swing.</summary>
        void Slash(float2 pp, float2 aim, float dmg, float radius, Color color)
        {
            float2 dir = SpawnUtil.SafeNormalize(aim - pp, Ecs.AimDir);
            meleeFlip = !meleeFlip;
            Ecs.SpawnZone(pp + dir * 0.2f, new DamageZone
            {
                Damage = dmg, Radius = radius, Life = 0.12f, Flags = (byte)(ZoneFlags.SingleTick | ZoneFlags.Cone),
                Dir = dir, ConeCos = 0.42f, Knockback = 7f, Color = F4(color, 0f), Vfx = ZoneVfx.Slash,
                VfxFlip = (byte)(meleeFlip ? 1 : 0),
            });
            Ecs.KickGun(dir);
            Ecs.AddRecoil(dir * 5f);
            Rig.Kick(new Vector2(-dir.x, -dir.y), 0.15f * GameDatabase.Cfg("CameraKick", 1f));
            Audio.Play(SfxPlayer.Id.Swing, 0.04f, 0.8f);
        }
    }
}
