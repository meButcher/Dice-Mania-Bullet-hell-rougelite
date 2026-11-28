using DiceMania.Data;
using DiceMania.ECS;
using DiceMania.Presentation;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DiceMania.Game
{
    public partial class GameDirector
    {
        public WaveDef CurrentWave { get; private set; }
        public float WaveTimeLeft { get; private set; }
        public float WaveElapsed { get; private set; }
        public bool BossAlive { get; private set; }
        public float BossHpFrac { get; private set; }
        public string BossName { get; private set; } = "";
        public int ChipsEarnedLastRun { get; private set; }
        public bool LastRunWon { get; private set; }

        int pendingLevelUps, pendingChests;
        float spawnAcc, eventTimer, waveClearTimer;
        int maxEnemies = 900;
        float waveHpMult = 1, waveDmgMult = 1, waveSpeedMult = 1;
        int bossesAliveCount;

        public int FinalWave => Mathf.RoundToInt(GameDatabase.Cfg("FinalWave", 15));

        // ------------------------------------------------------------------ run start / end

        public void StartRun(CharacterDef ch)
        {
            if (ch == null) ch = GameDatabase.Characters[0];
            Ecs.ClearRun();
            EndCopy();
            Ecs.ApplyConfig(false);

            Run = new RunState { Character = ch, MetaLevels = MetaProgress.MetaLevelsDict() };
            foreach (var faces in ch.Dice) Run.Dice.Add(Die.FromIds(faces));
            var w = GameDatabase.Weapon(ch.StartWeapon) ?? GameDatabase.Weapons[0];
            Run.Weapons.Add(new OwnedWeapon { Def = w, Level = 1 });
            Run.RecalculateStats();
            Run.XpToNext = RunState.XpForLevel(1);
            Run.RevivalsLeft = Run.Revivals;
            Run.CopyLevel = Mathf.Max(1, Run.MaxFaceLevel("copy"));

            Ecs.SpawnPlayer(ch, Run.MaxHP);
            Ability = null;
            LastRoll = null;
            DiceCubes.Show(null);
            RefreshBuild();
            var h = Ecs.PlayerHealth; h.Value = h.Max; Ecs.SetPlayerHealth(h);

            maxEnemies = Mathf.RoundToInt(GameDatabase.Cfg("MaxEnemies", 900));
            pendingLevelUps = pendingChests = 0;
            slowTimer = 0f;
            Floaters.Clear();
            Rig.Snap(Vector3.zero);
            MetaProgress.Data.SelectedCharacter = ch.Id;
            MetaProgress.Data.TotalRuns++;
            MetaProgress.Save();

            StartWave(1);
        }

        void StartWave(int wave)
        {
            Run.Wave = wave;
            CurrentWave = GameDatabase.WaveFor(wave);
            var wd = CurrentWave;
            float endless = wave > FinalWave ? 1f + GameDatabase.Cfg("EndlessHpGrowth", 0.25f) * (wave - FinalWave) : 1f;
            waveHpMult = wd.HpMult * endless;
            waveDmgMult = wd.DamageMult * (wave > FinalWave ? 1f + 0.05f * (wave - FinalWave) : 1f);
            waveSpeedMult = wd.SpeedMult;
            WaveTimeLeft = wd.Duration;
            WaveElapsed = 0f;
            spawnAcc = 0f;
            eventTimer = wd.EventEvery;
            Run.RollsLeft = Run.RollsPerWave;
            Run.RollRechargeTimer = 0f;
            BossAlive = false;
            bossesAliveCount = 0;

            if (wd.IsBossWave && (wave <= FinalWave || wave % 5 == 0))
            {
                var bd = GameDatabase.Enemy(wd.Boss);
                if (bd != null)
                {
                    float2 pp = Ecs.PlayerPos;
                    float2 bp = math.clamp(pp + new float2(0, 14f), -GameDatabase.Cfg("ArenaHalfSize", 55) + 6, GameDatabase.Cfg("ArenaHalfSize", 55) - 6);
                    Ecs.SpawnEnemy(bd, bp, false, waveHpMult, waveDmgMult, 1f);
                    BossAlive = true;
                    bossesAliveCount = 1;
                    ShowBanner("BOSS: " + bd.Name.ToUpper(), new Color(1f, 0.3f, 0.6f), 3f);
                    Audio.Play(SfxPlayer.Id.Boss);
                    Rig.Shake(0.6f);
                    Rig.Punch(0.15f);
                    var bc = new float4(bd.Color.r, bd.Color.g, bd.Color.b, 1f);
                    float bsize = bd.Size;
                    Ecs.Fx((Unity.Entities.EntityCommandBuffer ecb, Unity.Entities.Entity pf, ref Unity.Mathematics.Random r) =>
                    {
                        Fx.Shockwave(ecb, pf, bp, 0.2f, bsize * 0.5f, bsize * 4f, bc, 0.7f);
                        Fx.Glow(ecb, pf, bp, 1f, bsize * 3f, bc, 0.4f);
                        Fx.Splat(ecb, pf, bp, bsize * 1.6f, new float4(0.05f, 0.03f, 0.05f, 0.1f), 20f, ref r);
                    });
                }
            }
            else ShowBanner("WAVE " + wave, Color.white, 2f);

            SetState(GameState.Playing);
        }

        void UpdateWave(float dt)
        {
            var wd = CurrentWave;
            WaveElapsed += dt;
            if (BossAlive && Ecs.FindBoss(out var bossHp, out int bossDef))
            {
                BossHpFrac = bossHp.Value / Mathf.Max(1f, bossHp.Max);
                BossName = bossDef >= 0 && bossDef < GameDatabase.Enemies.Count ? GameDatabase.Enemies[bossDef].Name : "Boss";
            }
            bool bossWave = BossAlive || (wd.IsBossWave && wd.Duration <= 0f);
            if (!bossWave)
            {
                WaveTimeLeft -= dt;
                if (WaveTimeLeft <= 0f) { WaveCleared(); return; }
            }

            // Steady spawning.
            float progress = wd.Duration > 0f ? Mathf.Clamp01(WaveElapsed / wd.Duration) : Mathf.Clamp01(WaveElapsed / 90f);
            float rate = Mathf.Lerp(wd.SpawnRateStart, wd.SpawnRateEnd, progress);
            spawnAcc += rate * dt;
            int alive = Ecs.EnemyCount;
            int cap = Mathf.Min(wd.MaxAlive, maxEnemies);
            while (spawnAcc >= 1f)
            {
                spawnAcc -= 1f;
                if (alive >= cap || wd.Mix.Count == 0) { spawnAcc = 0f; break; }
                var def = PickFromMix(wd);
                if (def == null) break;
                bool elite = UnityEngine.Random.value < wd.EliteChance;
                Ecs.SpawnEnemy(def, SpawnPoint(), elite, waveHpMult, waveDmgMult, waveSpeedMult);
                alive++;
            }

            // Spawn events (rings / packs).
            if (wd.EventEvery > 0f && wd.EventCount > 0)
            {
                eventTimer -= dt;
                if (eventTimer <= 0f)
                {
                    eventTimer = wd.EventEvery;
                    var ed = GameDatabase.Enemy(wd.EventEnemy);
                    if (ed != null) SpawnEvent(ed, wd.EventCount, wd.EventShape);
                }
            }

            // Roll recharge.
            float recharge = GameDatabase.Cfg("RollRechargeSeconds", 25);
            if (recharge > 0f)
            {
                Run.RollRechargeTimer += dt * Run.RollRechargeMult;
                if (Run.RollRechargeTimer >= recharge)
                {
                    Run.RollRechargeTimer = 0f;
                    if (Run.RollsLeft < Run.RollsPerWave) Run.RollsLeft++;
                }
            }
        }

        EnemyDef PickFromMix(WaveDef wd)
        {
            float total = 0f;
            foreach (var m in wd.Mix) total += m.weight;
            float r = UnityEngine.Random.value * total;
            foreach (var m in wd.Mix)
            {
                r -= m.weight;
                if (r <= 0f) return GameDatabase.Enemy(m.id);
            }
            return GameDatabase.Enemy(wd.Mix[wd.Mix.Count - 1].id);
        }

        float2 SpawnPoint()
        {
            float half = GameDatabase.Cfg("ArenaHalfSize", 55) - 2f;
            float rmin = GameDatabase.Cfg("SpawnRadiusMin", 19), rmax = GameDatabase.Cfg("SpawnRadiusMax", 25);
            float2 pp = Ecs.PlayerPos;
            for (int tries = 0; tries < 8; tries++)
            {
                float a = UnityEngine.Random.value * Mathf.PI * 2f;
                float r = UnityEngine.Random.Range(rmin, rmax);
                float2 p = pp + new float2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (math.all(math.abs(p) < half)) return p;
            }
            return math.clamp(pp + new float2(rmin, 0), -half, half);
        }

        void SpawnEvent(EnemyDef def, int count, string shape)
        {
            float half = GameDatabase.Cfg("ArenaHalfSize", 55) - 2f;
            float2 pp = Ecs.PlayerPos;
            int room = maxEnemies - Ecs.EnemyCount;
            count = Mathf.Min(count, room);
            if (count <= 0) return;
            if (shape == "pack")
            {
                float2 c = SpawnPoint();
                for (int i = 0; i < count; i++)
                    Ecs.SpawnEnemy(def, math.clamp(c + (float2)(UnityEngine.Random.insideUnitCircle * 2.5f), -half, half), false, waveHpMult, waveDmgMult, waveSpeedMult);
            }
            else
            {
                float r = 15f;
                for (int i = 0; i < count; i++)
                {
                    float a = i * Mathf.PI * 2f / count;
                    Ecs.SpawnEnemy(def, math.clamp(pp + new float2(Mathf.Cos(a), Mathf.Sin(a)) * r, -half, half), false, waveHpMult, waveDmgMult, waveSpeedMult);
                }
            }
        }

        void OnBossKilled(int count)
        {
            Run.BossesKilled += count;
            bossesAliveCount -= count;
            if (bossesAliveCount <= 0 && BossAlive)
            {
                BossAlive = false;
                ShowBanner("BOSS DEFEATED!", new Color(1f, 0.85f, 0.3f), 3f);
                SlowMo(0.3f, 1.4f);   // after the freeze, let the finish breathe
                WaveCleared();
            }
        }

        void WaveCleared()
        {
            Ecs.KillAllEnemies(true);
            Ecs.MagnetAllPickups();
            waveClearTimer = 2.2f;
            Audio.Play(SfxPlayer.Id.WaveClear);
            if (!BossAlive) ShowBanner("WAVE " + Run.Wave + " CLEARED", new Color(0.5f, 1f, 0.6f), 2.2f);
            if (Run.Wave == 1 && !MetaProgress.Data.SeenTutorial) { MetaProgress.Data.SeenTutorial = true; MetaProgress.Save(); }
            CancelCopyAim();
            SetState(GameState.WaveClear);
        }

        void UpdateWaveClear(float rdt)
        {
            Vector3 mw = Rig.MouseWorld(GameInput.MousePos);
            Ecs.SetPlayerInput(new float2(GameInput.Move.x, GameInput.Move.y), new float2(mw.x, mw.z));
            ProcessEcsEvents();
            waveClearTimer -= rdt;
            if (waveClearTimer > 0f) return;
            if (pendingLevelUps > 0 || pendingChests > 0) { OpenLevelUp(); return; }

            if (Run.Wave >= FinalWave && !Run.Endless)
            {
                LastRunWon = true;
                SetState(GameState.Victory);
                ChipsEarnedLastRun = AwardChips(true);
                return;
            }
            OpenForge();
        }

        public void ContinueEndless()
        {
            Run.Endless = true;
            OpenForge();
        }

        void OnPlayerDied()
        {
            if (Run.RevivalsLeft > 0)
            {
                Run.RevivalsLeft--;
                var h = Ecs.PlayerHealth;
                h.Value = h.Max * 0.5f;
                Ecs.SetPlayerHealth(h);
                var b = Ecs.Get<Buffs>(); b.Invuln = 3f; Ecs.Set(b);
                Ecs.Em.DestroyEntity(Ecs.Em.CreateEntityQuery(typeof(EnemyShot)));
                ShowBanner("SECOND CHANCE!", new Color(1f, 0.9f, 0.4f), 2f);
                Rig.Shake(0.7f);
                Flash(0.5f);
                var rp = Ecs.PlayerPos;
                Ecs.Fx((Unity.Entities.EntityCommandBuffer ecb, Unity.Entities.Entity pf, ref Unity.Mathematics.Random r) =>
                {
                    Fx.Shockwave(ecb, pf, rp, 0.3f, 0.5f, 12f, new float4(1f, 0.85f, 0.4f, 1f), 0.6f);
                    Fx.Sparkles(ecb, pf, rp, 20, 1.5f, new float4(1f, 0.85f, 0.4f, 1f), ref r);
                });
                return;
            }
            var p = Ecs.PlayerPos;
            var vis = Ecs.Em.GetComponentData<Visual>(Ecs.Player);
            var blue = new float4(0.25f, 0.6f, 1f, 0.8f);
            // Shatter into blue chunks (the original's death), with a freeze, flash and punch-in.
            Ecs.Fx((Unity.Entities.EntityCommandBuffer ecb, Unity.Entities.Entity pf, ref Unity.Mathematics.Random r) =>
            {
                SpawnUtil.Burst(ecb, pf, p, 40, blue, 14f, 0.55f, 1.6f, ref r);
                Fx.Debris(ecb, pf, p, 0.6f, 24, blue, 10f, 0.35f, 2f, ref r, (byte)ShapeId.Diamond);
                Fx.Shockwave(ecb, pf, p, 0.3f, 0.5f, 10f, new float4(0.4f, 0.7f, 1f, 1f), 0.8f);
                Fx.Glow(ecb, pf, p, 0.8f, 8f, new float4(0.6f, 0.8f, 1f, 1f), 0.4f);
            });
            vis.Scale = 0f;
            Ecs.Em.SetComponentData(Ecs.Player, vis);
            Ecs.HideGun();
            CancelCopyAim();
            Rig.Shake(1f);
            Rig.Punch(0.3f);
            Flash(0.45f);
            RequestHitStop(0.2f, true);
            Rumble.Pulse(1f, 0.5f);
            Audio.Play(SfxPlayer.Id.Hurt);
            dyingTimer = 1.8f;
            SetState(GameState.Dying);
        }

        /// <summary>Test hook: clear the field and jump straight to a wave (e.g. a boss wave).</summary>
        public void DebugStartWave(int wave)
        {
            if (Run == null) return;
            Ecs.KillAllEnemies(false);
            CancelCopyAim();
            StartWave(wave);
        }

        /// <summary>Test hook: kill the player right now (revivals still apply).</summary>
        public void DebugKillPlayer()
        {
            if (State != GameState.Playing || Ecs.Player == Unity.Entities.Entity.Null) return;
            var h = Ecs.PlayerHealth; h.Value = 0f; Ecs.SetPlayerHealth(h);
            OnPlayerDied();
        }

        void EndRun(bool won)
        {
            LastRunWon = won;
            ChipsEarnedLastRun = AwardChips(won);
            SetState(GameState.GameOver);
        }

        int AwardChips(bool won)
        {
            int cleared = Mathf.Max(0, Run.Wave - (won ? 0 : 1));
            float chips = cleared * GameDatabase.Cfg("ChipsPerWave", 4) + Run.BossesKilled * GameDatabase.Cfg("ChipsPerBoss", 20)
                          + Run.Kills * GameDatabase.Cfg("ChipsPerKill", 0.05f);
            int c = Mathf.RoundToInt(chips);
            var d = MetaProgress.Data;
            d.Chips += c;
            d.BestWave = Mathf.Max(d.BestWave, Run.Wave);
            d.BestScore = Mathf.Max(d.BestScore, Run.Score);
            MetaProgress.Save();
            return c;
        }

        public void QuitToMenu()
        {
            if (Run != null && (State == GameState.Paused || State == GameState.Playing)) AwardChips(false);
            Ecs.ClearRun();
            EndCopy();
            Run = null;
            Ability = null;
            Floaters.Clear();
            Rig.Snap(Vector3.zero);
            SetState(GameState.MainMenu);
        }
    }
}
