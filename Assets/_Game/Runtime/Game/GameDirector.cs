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
    public enum GameState { Boot, MainMenu, Playing, LevelUp, Forge, Paused, WaveClear, Dying, GameOver, Victory }

    public struct FloatingText
    {
        public Vector3 World;
        public Vector3 Vel;     // numbers jump out and arc down
        public string Text;
        public Color Color;
        public float Age;
        public float Life;
        public float Size;
        public float Pop;       // extra scale at spawn that settles in ~0.12 s
        public bool AbilityName; // the rolled ability's name over the hero (a new roll replaces it)
    }

    /// <summary>
    /// The game's brain on the managed side: owns the run, the state machine, waves, dice, upgrades and the
    /// bridge to the ECS simulation. The UI (GameUI) only reads from here and calls its public methods.
    /// Also turns the ECS "feel" requests into camera kicks, hit-stop, screen flashes and rumble.
    /// </summary>
    public partial class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        /// <summary>Multiplies game speed (used by automated play tests to cover several waves quickly).</summary>
        public static float DebugTimeScale = 1f;

        public GameState State { get; private set; } = GameState.Boot;
        public RunState Run { get; private set; }
        public EcsBridge Ecs { get; private set; }
        public CameraRig Rig { get; private set; }
        public SfxPlayer Audio { get; private set; }
        public MusicPlayer Music { get; private set; }
        /// <summary>The 3D dice drawn in the HUD (bottom-right glass circle).</summary>
        public DiceView DiceCubes { get; private set; }

        public readonly List<FloatingText> Floaters = new List<FloatingText>();
        public string Banner = "";
        public float BannerTime;
        public Color BannerColor = Color.white;

        // ---- Screen feel (read by GameUI) ----
        public float ScreenFlash { get; private set; }   // white flash alpha
        public float HurtFlash { get; private set; }     // red vignette
        public float BurstLines { get; private set; }    // anime burst lines
        public float RollPunch { get; private set; }     // HUD dice / roll counter pop

        System.Random rng = new System.Random();
        GameObject ground;
        float realTimeInState;
        GameState stateBeforePause;
        float slowTimer;          // real seconds of Reroll slow-mo remaining
        float dyingTimer;
        float hitStopTimer, hitStopCooldown, recentKills;
        float slowMoTimer, slowMoScale = 1f;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;

            GameDatabase.Load();
            MetaProgress.Load();
            RenderAssets.Init();

            Rig = CameraRig.Create();
            Audio = gameObject.AddComponent<SfxPlayer>();
            Audio.Enabled = MetaProgress.Data.SfxOn;
            Music = gameObject.AddComponent<MusicPlayer>();
            Music.SetEnabled(MetaProgress.Data.MusicOn);
            DiceCubes = DiceView.Create(transform);
            ApplyFeelSettings();
            gameObject.AddComponent<GameUI>();
            HudShots.StartIfRequested(this);
        }

        void Start()
        {
            if (World.DefaultGameObjectInjectionWorld == null)
            {
                Debug.LogError("[DiceMania] No default ECS world. Is Entities installed and automatic bootstrap enabled?");
                return;
            }
            Ecs = new EcsBridge();
            Ecs.Init();
            Ecs.ApplyConfig(false);
            Ecs.BuildArena();
            BuildGround();
            Rig.Snap(Vector3.zero);
            SetState(GameState.MainMenu);
        }

        void OnDisable() => Rumble.Stop();

        /// <summary>Applies the player's options + Config.csv multipliers to camera shake and rumble.</summary>
        public void ApplyFeelSettings()
        {
            Rig.Intensity = MetaProgress.Data.ShakeOn ? GameDatabase.Cfg("ScreenShake", 1f) : 0f;
            Rumble.Strength = MetaProgress.Data.RumbleOn ? GameDatabase.Cfg("Rumble", 1f) : 0f;
            if (Rumble.Strength <= 0f) Rumble.Stop();
        }

        void BuildGround()
        {
            if (ground != null) Destroy(ground);
            float half = GameDatabase.Cfg("ArenaHalfSize", 55);
            ground = new GameObject("Ground");
            ground.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Quad(half * 2f + 40f);
            var mr = ground.AddComponent<MeshRenderer>();
            var mat = new Material(RenderAssets.OpaqueMaterial) { name = "DM_Ground" };
            mat.SetColor("_Color", new Color(0.045f, 0.05f, 0.1f, 0f));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            ground.transform.position = new Vector3(0, -0.02f, 0);
        }

        void SetState(GameState s)
        {
            if (s != State) Debug.Log($"[DiceMania] State {State} -> {s}");
            State = s;
            realTimeInState = 0f;
            if (Music != null)
            {
                if (s == GameState.MainMenu || s == GameState.Forge || s == GameState.GameOver || s == GameState.Victory) Music.PlayMenu();
                else Music.PlayBattle();
            }
            UpdateTimeScale();
        }

        void UpdateTimeScale()
        {
            float scale;
            switch (State)
            {
                case GameState.Playing:
                case GameState.WaveClear:
                    scale = 1f;
                    if (slowTimer > 0f) scale = Mathf.Min(scale, 0.5f);
                    if (slowMoTimer > 0f) scale = Mathf.Min(scale, slowMoScale);
                    scale = Mathf.Min(scale, CopyTimeScale());
                    if (hitStopTimer > 0f) scale = 0f; // freeze frames
                    break;
                case GameState.Dying:
                    scale = hitStopTimer > 0f ? 0f : 0.25f;
                    break;
                default:
                    scale = 0f;
                    break;
            }
            Time.timeScale = scale * DebugTimeScale;
            bool live = State == GameState.Playing || State == GameState.WaveClear || State == GameState.Dying;
            Music?.SetMood(live && scale > 0f && scale < 0.8f, State == GameState.Paused || State == GameState.LevelUp);
            if (Ecs != null && Ecs.Ready)
                Ecs.SetRunning(scale > 0f && (State == GameState.Playing || State == GameState.WaveClear || State == GameState.Dying));
        }

        void Update()
        {
            GameInput.Update();
            float rdt = Time.unscaledDeltaTime;
            realTimeInState += rdt;
            if (BannerTime > 0f) BannerTime -= rdt;
            TickFeel(rdt);
            if (Ecs == null || !Ecs.Ready) return;

#if UNITY_EDITOR
            if (GameInput.Reload || GameDatabase.ReloadRequested)
            {
                GameDatabase.ReloadRequested = false;
                HotReloadData();
            }
#endif
            switch (State)
            {
                case GameState.MainMenu:
                    if (GameInput.Confirm && realTimeInState > 0.3f) StartRun(SelectedCharacter());
                    break;
                case GameState.Playing:
                    if (GameInput.Pause) { stateBeforePause = State; SetState(GameState.Paused); break; }
                    UpdatePlaying(rdt);
                    break;
                case GameState.WaveClear:
                    UpdateWaveClear(rdt);
                    break;
                case GameState.Paused:
                    if (GameInput.Pause || (GameInput.Confirm && realTimeInState > 0.2f)) Resume();
                    break;
                case GameState.LevelUp:
                    UpdateLevelUpInput();
                    break;
                case GameState.Forge:
                    if (GameInput.Confirm && realTimeInState > 0.4f && Placing == PlaceMode.None) LeaveForge();
                    break;
                case GameState.Dying:
                    ProcessEcsEvents();
                    dyingTimer -= rdt;
                    if (dyingTimer <= 0f) EndRun(false);
                    break;
                case GameState.GameOver:
                    if (GameInput.Confirm && realTimeInState > 0.8f && Run != null) StartRun(Run.Character);
                    break;
            }

            UpdateFloaters(rdt);
            bool hudDice = Run != null && State != GameState.MainMenu && State != GameState.Forge;
            DiceCubes.Sync(hudDice ? Run.Dice : null);
            if (Ecs.Player != Entity.Null)
            {
                var p = Ecs.PlayerPos;
                Rig.Target = new Vector3(p.x, 0f, p.y);
            }
            UpdateTimeScale();
        }

        void TickFeel(float rdt)
        {
            if (hitStopTimer > 0f) hitStopTimer -= rdt;
            if (hitStopCooldown > 0f) hitStopCooldown -= rdt;
            if (slowMoTimer > 0f) slowMoTimer -= rdt;
            recentKills = Mathf.Max(0f, recentKills - recentKills * rdt * 1.2f);
            ScreenFlash = Mathf.Max(0f, ScreenFlash - rdt * 3f);
            HurtFlash = Mathf.Max(0f, HurtFlash - rdt * 3f);
            BurstLines = Mathf.Max(0f, BurstLines - rdt * 4f);
            RollPunch = Mathf.Max(0f, RollPunch - rdt * 5f);
            Rumble.Tick();
        }

        /// <summary>
        /// Freeze the game for a few frames. Freezes respect a cooldown and the small kill beats are skipped during hordes,
        /// so they never turn into stutter; forced ones (getting hurt, Copy hit, boss kill, death) always land.
        /// </summary>
        public void RequestHitStop(float seconds, bool force = false)
        {
            if (seconds <= 0f) return;
            if (!force)
            {
                if (hitStopCooldown > 0f) return;
                if (seconds < 0.05f && recentKills > 6f) return;
            }
            hitStopTimer = Mathf.Max(hitStopTimer, Mathf.Min(seconds, 0.5f));
            hitStopCooldown = Mathf.Max(hitStopCooldown, GameDatabase.Cfg("HitStopCooldown", 0.3f));
        }

        /// <summary>Real-time slow motion (boss kills, dramatic moments).</summary>
        public void SlowMo(float scale, float seconds)
        {
            slowMoScale = Mathf.Clamp(scale, 0.05f, 1f);
            slowMoTimer = Mathf.Max(slowMoTimer, seconds);
        }

        public void Flash(float alpha) => ScreenFlash = Mathf.Max(ScreenFlash, alpha);
        public void Lines() => BurstLines = 1f;

        public CharacterDef SelectedCharacter()
        {
            var list = GameDatabase.Characters;
            if (list.Count == 0) return null;
            var sel = GameDatabase.Character(MetaProgress.Data.SelectedCharacter);
            if (sel == null || !MetaProgress.IsCharacterUnlocked(sel)) sel = list[0];
            return sel;
        }

        public void Resume()
        {
            if (State == GameState.Paused) SetState(stateBeforePause);
        }

        // ------------------------------------------------------------------ per-frame play

        void UpdatePlaying(float rdt)
        {
            float dt = Time.deltaTime;
            Run.RunTime += dt;
            if (slowTimer > 0f) slowTimer -= rdt;

            // Input -> ECS.
            Vector3 mw = AimWorld();
            Ecs.SetPlayerInput(new float2(GameInput.Move.x, GameInput.Move.y), new float2(mw.x, mw.z));

            UpdateRolling(rdt);
            UpdateAbility(rdt, mw);
            UpdateCopy(rdt, mw);
            ProcessEcsEvents();
            if (State != GameState.Playing) return;
            UpdateWave(dt);

            if (pendingLevelUps > 0 || pendingChests > 0) OpenLevelUp();
        }

        /// <summary>Mouse point on the ground, or the gamepad aim (right stick, else the nearest enemy).</summary>
        Vector3 AimWorld()
        {
            if (!GameInput.PadAiming || Ecs.Player == Entity.Null) return Rig.MouseWorld(GameInput.MousePos);
            float2 pp = Ecs.PlayerPos;
            Vector2 a = GameInput.PadAim;
            if (a.sqrMagnitude > 0.04f) return new Vector3(pp.x + a.x * 8f, 0f, pp.y + a.y * 8f);
            float2 best = pp + new float2(6f, 0f);
            float bd = float.MaxValue;
            using (var ep = Ecs.EnemyPositions(Allocator.Temp))
                for (int i = 0; i < ep.Length; i++)
                {
                    float d = math.distancesq(ep[i].Value, pp);
                    if (d < bd) { bd = d; best = ep[i].Value; }
                }
            return new Vector3(best.x, 0f, best.y);
        }

        void ProcessEcsEvents()
        {
            var em = Ecs.Em;
            var ev = Ecs.Get<GameEvents>();
            Ecs.Set(new GameEvents());

            if (ev.Kills > 0) { Run.Kills += ev.Kills; recentKills += ev.Kills; }
            if (ev.Score > 0) Run.Score += ev.Score;
            if (ev.Gold > 0) Run.Gold += ev.Gold;
            if (ev.Xp > 0) AddXp(ev.Xp * Run.Growth);
            if (ev.Chests > 0) pendingChests += ev.Chests;
            if (ev.DamageTaken > 0) Run.DamageTaken += ev.DamageTaken;

            // Feel.
            if (ev.ShakeRequest > 0) Rig.Shake(ev.ShakeRequest);
            if (ev.Vibe > 0) Rig.Vibrate(ev.Vibe);
            float kickLen = math.length(ev.Kick);
            if (kickLen > 1e-4f) Rig.Kick(new Vector2(ev.Kick.x, ev.Kick.y) / kickLen, kickLen);
            if (ev.Punch > 0) Rig.Punch(ev.Punch);
            if (ev.HitStop > 0) RequestHitStop(ev.HitStop, ev.HitStopForce != 0);
            if (ev.ScreenFlash > 0) Flash(ev.ScreenFlash);
            if (ev.BurstLines != 0) Lines();
            if (ev.Hurt > 0) HurtFlash = 1f;
            if (ev.Rumble > 0) Rumble.Pulse(ev.Rumble, 0.06f + 0.18f * ev.Rumble);

            if (ev.BossKills > 0) OnBossKilled(ev.BossKills);
            if (ev.CopyHit != 0) OnCopyHit(ev.CopyHitDef);
            else if (ev.CopyMiss != 0) OnCopyMiss();
            if (ev.PlayerDied != 0 && State == GameState.Playing) OnPlayerDied();

            // Damage numbers -> floating text that pops in, jumps up and falls.
            var nums = em.GetBuffer<DamageNumber>(Ecs.Singleton);
            bool showNumbers = GameDatabase.Cfg("DamageNumbers", 1f) > 0f;
            for (int i = 0; i < nums.Length && showNumbers; i++)
            {
                var n = nums[i];
                Color c; float size = 1f, pop = 0.6f, life = 0.8f, up = 4.5f;
                string text = Mathf.RoundToInt(n.Amount).ToString();
                switch (n.Kind)
                {
                    case NumKind.Crit: c = new Color(1f, 0.82f, 0.2f); size = 1.4f; pop = 1.1f; text += "!"; break;
                    case NumKind.Kill: c = new Color(1f, 0.55f, 0.22f); size = 1.5f; pop = 1.3f; life = 0.95f; up = 6f; break;
                    case NumKind.KillCrit: c = new Color(1f, 0.9f, 0.15f); size = 1.85f; pop = 1.6f; life = 1.05f; up = 6.5f; text += "!"; break;
                    case NumKind.PlayerHurt: c = new Color(1f, 0.25f, 0.25f); size = 1.45f; pop = 1f; break;
                    case NumKind.Heal: c = new Color(0.4f, 1f, 0.45f); text = "+" + text; up = 3f; break;
                    default: c = Color.white; break;
                }
                if (Floaters.Count >= 90) Floaters.RemoveAt(0);
                Floaters.Add(new FloatingText
                {
                    World = new Vector3(n.Pos.x + UnityEngine.Random.Range(-0.3f, 0.3f), 1.2f, n.Pos.y),
                    Vel = new Vector3(UnityEngine.Random.Range(-1.6f, 1.6f), up * UnityEngine.Random.Range(0.85f, 1.15f), UnityEngine.Random.Range(-0.6f, 0.6f)),
                    Text = text, Color = c, Size = size, Pop = pop, Life = life,
                });
            }
            nums.Clear();

            // Sounds: few samples, nudged pitch (SfxPlayer randomizes every play).
            var sfx = em.GetBuffer<SfxEvent>(Ecs.Singleton);
            for (int i = 0; i < sfx.Length; i++)
            {
                switch (sfx[i].Id)
                {
                    case ECS.Sfx.Shoot: Audio.Play(SfxPlayer.Id.Shoot, 0.06f, 0.55f); break;
                    case ECS.Sfx.Hit: Audio.Play(SfxPlayer.Id.Hit, 0.045f, 0.65f); break;
                    case ECS.Sfx.Kill: Audio.Play(SfxPlayer.Id.Kill, 0.05f, 0.65f); break;
                    case ECS.Sfx.Hurt: Audio.Play(SfxPlayer.Id.Hurt, 0.1f); break;
                    case ECS.Sfx.Pickup: Audio.Play(SfxPlayer.Id.Pickup, 0.05f, 0.45f); break;
                    case ECS.Sfx.Explode: Audio.Play(SfxPlayer.Id.Explode, 0.07f); break;
                    case ECS.Sfx.EnemyShoot: Audio.Play(SfxPlayer.Id.EnemyShoot, 0.12f, 0.3f); break;
                    case ECS.Sfx.Copy: Audio.Play(SfxPlayer.Id.Copy, 0.2f); break;
                    case ECS.Sfx.Shell: Audio.Play(SfxPlayer.Id.Shell, 0.07f, 0.35f); break;
                    case ECS.Sfx.Crit: Audio.Play(SfxPlayer.Id.Crit, 0.06f, 0.55f); break;
                    case ECS.Sfx.Heavy: Audio.Play(SfxPlayer.Id.Heavy, 0.1f, 0.8f); break;
                    case ECS.Sfx.Shatter: Audio.Play(SfxPlayer.Id.Shatter, 0.15f, 0.8f); break;
                    case ECS.Sfx.Chest: Audio.Play(SfxPlayer.Id.Chest, 0.2f); break;
                    case ECS.Sfx.BigKill: Audio.Play(SfxPlayer.Id.BigKill, 0.12f, 0.9f); break;
                }
            }
            sfx.Clear();

            // Spawn requests (splitters, boss summons).
            var req = em.GetBuffer<SpawnRequest>(Ecs.Singleton);
            if (req.Length > 0)
            {
                var copy = req.ToNativeArray(Allocator.Temp);
                req.Clear();
                for (int i = 0; i < copy.Length; i++)
                {
                    var r = copy[i];
                    if (r.Def < 0 || r.Def >= GameDatabase.Enemies.Count) continue;
                    var def = GameDatabase.Enemies[r.Def];
                    for (int k = 0; k < r.Count; k++)
                    {
                        if (Ecs.EnemyCount >= maxEnemies) break;
                        float a = k * Mathf.PI * 2f / Mathf.Max(1, r.Count);
                        float2 p = r.Pos + new float2(Mathf.Cos(a), Mathf.Sin(a)) * 1.2f;
                        Ecs.SpawnEnemy(def, p, false, waveHpMult, waveDmgMult, waveSpeedMult);
                    }
                }
                copy.Dispose();
            }
        }

        void UpdateFloaters(float rdt)
        {
            for (int i = Floaters.Count - 1; i >= 0; i--)
            {
                var f = Floaters[i];
                f.Age += rdt;
                f.World += f.Vel * rdt;
                f.Vel.y -= 11f * rdt;
                f.Vel.x *= Mathf.Exp(-3f * rdt);
                f.Vel.z *= Mathf.Exp(-3f * rdt);
                if (f.Age > (f.Life > 0f ? f.Life : 0.8f)) Floaters.RemoveAt(i);
                else Floaters[i] = f;
            }
        }

        public void ShowBanner(string text, Color color, float seconds = 2f)
        {
            Banner = text;
            BannerColor = color;
            BannerTime = seconds;
        }

        void AddXp(float amount)
        {
            Run.Xp += amount;
            while (Run.Xp >= Run.XpToNext)
            {
                Run.Xp -= Run.XpToNext;
                Run.Level++;
                Run.XpToNext = RunState.XpForLevel(Run.Level);
                pendingLevelUps++;
            }
        }

#if UNITY_EDITOR
        void HotReloadData()
        {
            GameDatabase.Load();
            if (Run != null)
            {
                // Re-resolve defs by id so edited numbers apply immediately.
                Run.Character = GameDatabase.Character(Run.Character.Id) ?? Run.Character;
                foreach (var w in Run.Weapons) w.Def = GameDatabase.Weapon(w.Def.Id) ?? w.Def;
                foreach (var p in Run.Passives) p.Def = GameDatabase.Passive(p.Def.Id) ?? p.Def;
                for (int i = 0; i < Run.Relics.Count; i++) Run.Relics[i] = GameDatabase.Relic(Run.Relics[i].Id) ?? Run.Relics[i];
                foreach (var d in Run.Dice) foreach (var s in d.Slots) s.Face = GameDatabase.Face(s.Face.Id) ?? s.Face;
                if (Ability != null)
                {
                    Ability.Face = GameDatabase.Face(Ability.Face.Id) ?? Ability.Face;
                    Ability.Cooldown = Ability.Face.CooldownAt(Ability.Level);
                    Ability.CooldownLeft = Mathf.Min(Ability.CooldownLeft, Ability.Cooldown);
                }
                RefreshBuild();
            }
            Ecs.ApplyConfig(Ecs.Get<GameClock>().Running != 0);
            ApplyFeelSettings();
            ShowBanner("Data reloaded (" + CsvTable.Warnings.Count + " warnings)", Color.cyan);
        }
#endif

        /// <summary>Recompute stats and push weapons + stats into ECS. Call after any build change.</summary>
        public void RefreshBuild()
        {
            Run.RecalculateStats();
            Ecs.PushStats(Run);
            Ecs.SetWeapons(Run);
        }
    }
}
