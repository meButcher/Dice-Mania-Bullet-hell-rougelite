using System.Collections.Generic;
using DiceMania.Data;
using DiceMania.ECS;
using DiceMania.Presentation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DiceMania.Game
{
    public enum GameState { Boot, MainMenu, Playing, LevelUp, Forge, Paused, WaveClear, Dying, GameOver, Victory }

    public struct FloatingText
    {
        public Vector3 World;
        public string Text;
        public Color Color;
        public float Age;
        public float Size;
    }

    /// <summary>
    /// The game's brain on the managed side: owns the run, the state machine, waves, dice, upgrades and the
    /// bridge to the ECS simulation. The UI (GameUI) only reads from here and calls its public methods.
    /// </summary>
    public partial class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Boot;
        public RunState Run { get; private set; }
        public EcsBridge Ecs { get; private set; }
        public CameraRig Rig { get; private set; }
        public SfxPlayer Audio { get; private set; }

        public readonly List<FloatingText> Floaters = new List<FloatingText>();
        public string Banner = "";
        public float BannerTime;
        public Color BannerColor = Color.white;

        System.Random rng = new System.Random();
        GameObject ground;
        float realTimeInState;
        GameState stateBeforePause;
        float slowTimer;          // real seconds of Reroll slow-mo remaining
        float dyingTimer;

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
            gameObject.AddComponent<GameUI>();
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

        void BuildGround()
        {
            if (ground != null) Destroy(ground);
            float half = GameDatabase.Cfg("ArenaHalfSize", 55);
            ground = new GameObject("Ground");
            ground.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Quad(half * 2f + 40f);
            var mr = ground.AddComponent<MeshRenderer>();
            var mat = new Material(RenderAssets.OpaqueMaterial) { name = "DM_Ground" };
            mat.SetColor("_Color", new Color(0.035f, 0.04f, 0.085f, 0f));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            ground.transform.position = new Vector3(0, -0.02f, 0);
        }

        void SetState(GameState s)
        {
            State = s;
            realTimeInState = 0f;
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
                    scale = Mathf.Min(scale, CopyTimeScale());
                    break;
                case GameState.Dying:
                    scale = 0.25f;
                    break;
                default:
                    scale = 0f;
                    break;
            }
            Time.timeScale = scale;
            if (Ecs != null && Ecs.Ready)
                Ecs.SetRunning(scale > 0f && (State == GameState.Playing || State == GameState.WaveClear || State == GameState.Dying));
        }

        void Update()
        {
            GameInput.Update();
            float rdt = Time.unscaledDeltaTime;
            realTimeInState += rdt;
            if (BannerTime > 0f) BannerTime -= rdt;
            if (Ecs == null || !Ecs.Ready) return;

#if UNITY_EDITOR
            if (GameInput.Reload) HotReloadData();
#endif
            switch (State)
            {
                case GameState.Playing:
                    if (GameInput.Pause) { stateBeforePause = State; SetState(GameState.Paused); break; }
                    UpdatePlaying(rdt);
                    break;
                case GameState.WaveClear:
                    UpdateWaveClear(rdt);
                    break;
                case GameState.Paused:
                    if (GameInput.Pause) Resume();
                    break;
                case GameState.LevelUp:
                    UpdateLevelUpInput();
                    break;
                case GameState.Dying:
                    ProcessEcsEvents();
                    dyingTimer -= rdt;
                    if (dyingTimer <= 0f) EndRun(false);
                    break;
            }

            UpdateFloaters(rdt);
            if (Ecs.Player != Entity.Null)
            {
                var p = Ecs.PlayerPos;
                Rig.Target = new Vector3(p.x, 0f, p.y);
            }
            UpdateTimeScale();
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
            Vector3 mw = Rig.MouseWorld(GameInput.MousePos);
            Ecs.SetPlayerInput(new float2(GameInput.Move.x, GameInput.Move.y), new float2(mw.x, mw.z));

            UpdateRolling(rdt);
            UpdateCopy(rdt, mw);
            ProcessEcsEvents();
            if (State != GameState.Playing) return;
            UpdateWave(dt);

            if (pendingLevelUps > 0 || pendingChests > 0) OpenLevelUp();
        }

        void ProcessEcsEvents()
        {
            var em = Ecs.Em;
            var ev = Ecs.Get<GameEvents>();
            Ecs.Set(new GameEvents());

            if (ev.Kills > 0) { Run.Kills += ev.Kills; }
            if (ev.Score > 0) Run.Score += ev.Score;
            if (ev.Gold > 0) Run.Gold += ev.Gold;
            if (ev.Xp > 0) AddXp(ev.Xp * Run.Growth);
            if (ev.Chests > 0) pendingChests += ev.Chests;
            if (ev.DamageTaken > 0) Run.DamageTaken += ev.DamageTaken;
            if (ev.ShakeRequest > 0) Rig.Shake(ev.ShakeRequest);
            if (ev.BossKills > 0) OnBossKilled(ev.BossKills);
            if (ev.CopyHit != 0) OnCopyHit(ev.CopyHitDef);
            else if (ev.CopyMiss != 0) OnCopyMiss();
            if (ev.PlayerDied != 0 && State == GameState.Playing) OnPlayerDied();

            // Damage numbers -> floating text.
            var nums = em.GetBuffer<DamageNumber>(Ecs.Singleton);
            for (int i = 0; i < nums.Length; i++)
            {
                var n = nums[i];
                Color c; float size = 1f;
                switch (n.Kind)
                {
                    case NumKind.Crit: c = new Color(1f, 0.85f, 0.2f); size = 1.35f; break;
                    case NumKind.PlayerHurt: c = new Color(1f, 0.25f, 0.25f); size = 1.3f; break;
                    case NumKind.Heal: c = new Color(0.4f, 1f, 0.45f); break;
                    default: c = Color.white; break;
                }
                if (Floaters.Count < 80)
                    Floaters.Add(new FloatingText
                    {
                        World = new Vector3(n.Pos.x + UnityEngine.Random.Range(-0.3f, 0.3f), 1.2f, n.Pos.y),
                        Text = n.Kind == NumKind.Heal ? "+" + Mathf.RoundToInt(n.Amount) : Mathf.RoundToInt(n.Amount).ToString(),
                        Color = c, Size = size,
                    });
            }
            nums.Clear();

            // Sounds.
            var sfx = em.GetBuffer<SfxEvent>(Ecs.Singleton);
            for (int i = 0; i < sfx.Length; i++)
            {
                switch (sfx[i].Id)
                {
                    case ECS.Sfx.Shoot: Audio.Play(SfxPlayer.Id.Shoot, 0.07f, 0.5f); break;
                    case ECS.Sfx.Hit: Audio.Play(SfxPlayer.Id.Hit, 0.05f, 0.6f); break;
                    case ECS.Sfx.Kill: Audio.Play(SfxPlayer.Id.Kill, 0.06f, 0.6f); break;
                    case ECS.Sfx.Hurt: Audio.Play(SfxPlayer.Id.Hurt, 0.1f); break;
                    case ECS.Sfx.Pickup: Audio.Play(SfxPlayer.Id.Pickup, 0.05f, 0.5f); break;
                    case ECS.Sfx.Explode: Audio.Play(SfxPlayer.Id.Explode, 0.08f); break;
                    case ECS.Sfx.EnemyShoot: Audio.Play(SfxPlayer.Id.EnemyShoot, 0.12f, 0.35f); break;
                    case ECS.Sfx.Copy: Audio.Play(SfxPlayer.Id.Copy, 0.2f); break;
                }
            }
            sfx.Clear();

            // Spawn requests (splitters, boss summons).
            var req = em.GetBuffer<SpawnRequest>(Ecs.Singleton);
            if (req.Length > 0)
            {
                var copy = req.ToNativeArray(Unity.Collections.Allocator.Temp);
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
                f.World.y += rdt * 1.5f;
                if (f.Age > 0.8f) Floaters.RemoveAt(i);
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
                RefreshBuild();
            }
            Ecs.ApplyConfig(Ecs.Get<GameClock>().Running != 0);
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
