using System.Collections;
using System.IO;
using DiceMania.Data;
using DiceMania.ECS;
using DiceMania.Game;
using DiceMania.Presentation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace DiceMania.Tests
{
    /// <summary>
    /// End-to-end smoke tests: a bot plays the real game (menu, waves, dice, Copy, level-ups, Forge, boss, death)
    /// at 3x speed. Any error or exception logged during a run fails the test (Unity Test Framework default).
    /// Camera captures go to $DICEMANIA_CAPTURE_DIR (default: Builds/Captures) so the visuals can be reviewed.
    /// </summary>
    public class SmokePlayTests
    {
        const string TestSaveKey = "dicemania_save_playmode_test";
        const string RealSaveKey = "dicemania_save_v1";

        class Bot
        {
            public float T;
            public bool RollNext, LmbDownNext, LmbUpNext, LmbHeld, PauseNext, Still;
            public Vector2 Mouse;

            public void Apply()
            {
                GameInput.Move = Still ? Vector2.zero : new Vector2(Mathf.Cos(T * 0.9f), Mathf.Sin(T * 0.63f));
                GameInput.MousePos = Mouse;
                GameInput.Roll = RollNext; RollNext = false;
                GameInput.Pause = PauseNext; PauseNext = false;
                GameInput.LmbDown = LmbDownNext; LmbDownNext = false;
                GameInput.LmbUp = LmbUpNext; LmbUpNext = false;
                GameInput.LmbHeld = LmbHeld;
            }
        }

        string captureDir;
        static int captureIndex;
        bool captureRequested;
        string captureLabel;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            float t0 = Time.realtimeSinceStartup;
            while ((GameDirector.Instance == null || GameDirector.Instance.State != GameState.MainMenu)
                   && Time.realtimeSinceStartup - t0 < 30f)
            {
                if (GameDirector.Instance != null && GameDirector.Instance.State != GameState.Boot
                    && GameDirector.Instance.State != GameState.MainMenu)
                    GameDirector.Instance.QuitToMenu();
                yield return null;
            }
            Assert.IsNotNull(GameDirector.Instance, "GameDirector did not boot");
            Assert.AreEqual(GameState.MainMenu, GameDirector.Instance.State, "game should reach the main menu");

            // Never touch the player's real save.
            MetaProgress.Key = TestSaveKey;
            PlayerPrefs.DeleteKey(TestSaveKey);
            MetaProgress.Load();

            captureDir = System.Environment.GetEnvironmentVariable("DICEMANIA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(captureDir)) captureDir = Path.Combine(Application.dataPath, "../Builds/Captures");
            Directory.CreateDirectory(captureDir);
            InstancedRenderSystem.AfterDraw = OnAfterDraw;
        }

        [TearDown]
        public void TearDown()
        {
            GameInput.Bot = null;
            GameDirector.DebugTimeScale = 1f;
            InstancedRenderSystem.AfterDraw = null;
            PlayerPrefs.DeleteKey(TestSaveKey);
            MetaProgress.Key = RealSaveKey;
            MetaProgress.Load();
        }

        // ------------------------------------------------------------------ tests

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator BotPlaysSeveralWavesThenDies()
        {
            var d = GameDirector.Instance;
            var bot = new Bot();
            GameInput.Bot = bot.Apply;
            GameDirector.DebugTimeScale = 3f;

            d.StartRun(GameDatabase.Characters[0]);
            Assert.AreEqual(GameState.Playing, d.State);
            Capture("run_start");

            int levelUps = 0, forgeVisits = 0, rolls = 0, copies = 0, maxEnemies = 0, frames = 0, copyState = 0, abilityUses = 0;
            float rollTimer = 1.5f, captureTimer = 2.5f, copyHold = 0f, maxFrame = 0f, frameSum = 0f;
            bool pauseTested = false;
            float start = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - start < 260f)
            {
                yield return null;
                frames++;
                float rdt = Time.unscaledDeltaTime;
                frameSum += rdt;
                if (frames > 10) maxFrame = Mathf.Max(maxFrame, rdt);
                var run = d.Run;
                switch (d.State)
                {
                    case GameState.Playing:
                    {
                        bot.T += rdt;
                        maxEnemies = Mathf.Max(maxEnemies, d.Ecs.EnemyCount);
                        // The bot can't dodge: keep it alive (it still takes hits, so hurt feedback runs) to cover the flow.
                        var hp = d.Ecs.PlayerHealth;
                        if (hp.Value > 0f && hp.Value < hp.Max * 0.4f) { hp.Value = hp.Max; d.Ecs.SetPlayerHealth(hp); }
                        rollTimer -= rdt;
                        if (rollTimer <= 0f && run.RollsLeft > 0 && d.RollAnimTime <= 0f && d.Copy == CopyPhase.None)
                        {
                            bot.RollNext = true;
                            rolls++;
                            rollTimer = 2.2f;
                        }
                        var ab = d.Ability;
                        if (copyState == 0 && ab != null && ab.Face.Id == "copy" && ab.Ready && d.Copy == CopyPhase.None && d.RollAnimTime <= 0f)
                        {
                            // Morph: hold on an enemy, release.
                            if (TryNearestEnemyScreen(d, out var sp))
                            {
                                bot.Mouse = sp; bot.LmbDownNext = true; bot.LmbHeld = true;
                                copyState = 1; copyHold = 0.35f;
                            }
                        }
                        else if (copyState == 1)
                        {
                            if (TryNearestEnemyScreen(d, out var sp)) bot.Mouse = sp;
                            copyHold -= rdt;
                            if (copyHold <= 0f) { bot.LmbHeld = false; bot.LmbUpNext = true; copyState = 0; copies++; }
                        }
                        else if (ab != null && ab.Face.Id != "copy" && ab.Ready && d.RollAnimTime <= 0f)
                        {
                            // Every other face: click on the nearest enemy.
                            if (TryNearestEnemyScreen(d, out var sp)) bot.Mouse = sp;
                            bot.LmbDownNext = true;
                            abilityUses++;
                        }
                        if (!pauseTested && run.Wave == 1 && d.WaveElapsed > 6f)
                        {
                            bot.PauseNext = true;
                            yield return null;
                            Assert.AreEqual(GameState.Paused, d.State, "ESC should pause");
                            bot.PauseNext = true;
                            yield return null;
                            Assert.AreEqual(GameState.Playing, d.State, "ESC should resume");
                            pauseTested = true;
                        }
                        captureTimer -= rdt;
                        if (captureTimer <= 0f) { captureTimer = 6f; Capture($"w{run.Wave}_play"); }
                        break;
                    }
                    case GameState.LevelUp:
                        levelUps++;
                        d.PickOffer(levelUps % Mathf.Max(1, d.LevelOffers.Count));
                        break;
                    case GameState.Forge:
                        forgeVisits++;
                        Capture($"w{run.Wave}_after_forge");
                        ShopOnce(d);
                        d.LeaveForge();
                        break;
                    case GameState.GameOver:
                    case GameState.Victory:
                        Assert.Fail($"run ended unexpectedly in state {d.State} at wave {run.Wave}");
                        break;
                }
                if (forgeVisits >= 3 && run.Wave >= 4) break;
            }

            var r = d.Run;
            Debug.Log($"[Smoke] wave {r.Wave} kills {r.Kills} level {r.Level} levelUps {levelUps} forge {forgeVisits} rolls {rolls} " +
                      $"copies {copies} abilityUses {abilityUses} maxEnemies {maxEnemies} frames {frames} avgFrame {frameSum / Mathf.Max(1, frames) * 1000f:0.0}ms " +
                      $"maxFrame {maxFrame * 1000f:0}ms weapons {r.Weapons.Count} passives {r.Passives.Count} relics {r.Relics.Count}");
            Assert.Greater(r.Kills, 20, "the bot should kill enemies");
            Assert.GreaterOrEqual(r.Wave, 3, "the bot should clear waves");
            Assert.Greater(levelUps, 0, "level-ups should happen");
            Assert.Greater(forgeVisits, 0, "the Forge should open between waves");
            Assert.Greater(rolls, 0, "dice should be rolled");
            Assert.Greater(abilityUses, 0, "rolled faces should be used by clicking");
            Assert.IsTrue(pauseTested, "pause should be tested");

            // Death -> Game Over -> menu.
            float t = Time.realtimeSinceStartup;
            while (d.State != GameState.Playing && Time.realtimeSinceStartup - t < 15f)
            {
                if (d.State == GameState.LevelUp) d.PickOffer(0);
                if (d.State == GameState.Forge) d.LeaveForge();
                yield return null;
            }
            d.Run.RevivalsLeft = 0;
            d.DebugKillPlayer();
            Assert.AreEqual(GameState.Dying, d.State);
            Capture("dying");
            t = Time.realtimeSinceStartup;
            while (d.State == GameState.Dying && Time.realtimeSinceStartup - t < 15f) yield return null;
            Assert.AreEqual(GameState.GameOver, d.State);
            Assert.Greater(MetaProgress.Data.Chips, 0, "a run should award chips");
            d.QuitToMenu();
            Assert.AreEqual(GameState.MainMenu, d.State);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator BossWaveSpawnsBossAndKillingItClearsTheWave()
        {
            var d = GameDirector.Instance;
            var bot = new Bot();
            GameInput.Bot = bot.Apply;
            GameDirector.DebugTimeScale = 2f;

            d.StartRun(GameDatabase.Characters[0]);
            d.DebugStartWave(5);
            Assert.AreEqual(GameState.Playing, d.State);
            Assert.IsTrue(d.BossAlive, "wave 5 should spawn a boss");
            var b = d.Ecs.Get<Buffs>(); b.Invuln = 999f; d.Ecs.Set(b);

            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 8f)
            {
                yield return null;
                bot.T += Time.unscaledDeltaTime;
                if (d.State == GameState.LevelUp) d.PickOffer(0);
            }
            Capture("boss_fight");
            Assert.IsTrue(d.BossAlive, "boss should still be alive after a few seconds");
            Assert.Greater(d.BossHpFrac, 0f);

            // Bring the boss to 1 HP and shoot it directly (the auto gun prefers the swarmers crowding the bot).
            start = Time.realtimeSinceStartup;
            bool sawKill = false;
            while (Time.realtimeSinceStartup - start < 20f)
            {
                if (d.State == GameState.LevelUp) d.PickOffer(0);
                if (!d.BossAlive) { sawKill = true; break; }
                using (var ents = d.Ecs.EnemyEntities(Allocator.Temp))
                {
                    foreach (var e in ents)
                    {
                        var en = d.Ecs.Em.GetComponentData<Enemy>(e);
                        if (en.Boss == 0) continue;
                        var h = d.Ecs.Em.GetComponentData<Health>(e);
                        if (h.Value > 1f) { h.Value = 1f; d.Ecs.Em.SetComponentData(e, h); }
                        var bp = d.Ecs.Em.GetComponentData<Pos>(e).Value;
                        d.Ecs.SpawnPlayerShot(bp - new float2(3f, 0f), new float2(1f, 0f),
                            new Projectile { Kind = ProjKind.Normal, Damage = 5f, Life = 1f, Speed = 20f, Knockback = 1f }, Data.ShapeId.Cube, 0.4f,
                            new float4(1f, 1f, 1f, 1f));
                    }
                }
                yield return null;
            }
            Assert.IsTrue(sawKill, "killing the boss should end the boss fight");
            Capture("boss_dead");
            Assert.AreEqual(1, d.Run.BossesKilled);
            start = Time.realtimeSinceStartup;
            while (d.State != GameState.Forge && Time.realtimeSinceStartup - start < 20f)
            {
                if (d.State == GameState.LevelUp) d.PickOffer(0);
                yield return null;
            }
            Assert.AreEqual(GameState.Forge, d.State, "boss kill should lead to the Forge");
            d.QuitToMenu();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator FaceEffectsGallery()
        {
            var d = GameDirector.Instance;
            var bot = new Bot { Still = true };
            GameInput.Bot = bot.Apply;
            GameDirector.DebugTimeScale = 1f;
            d.StartRun(GameDatabase.Characters[0]);
            var grunt = GameDatabase.Enemy("grunt");
            Assert.IsNotNull(grunt);

            // Kill feel: a line of grunts right in front of the gun -> flash, squash, freeze, throw, splat, shells.
            yield return WaitReal(0.5f);
            var pp0 = d.Ecs.PlayerPos;
            for (int i = 0; i < 6; i++) d.Ecs.SpawnEnemy(grunt, pp0 + new float2(3f + i * 0.9f, (i % 2) * 0.8f - 0.4f), false, 0.3f, 0f, 0f);
            for (int f = 0; f < 40; f++)
            {
                if (f % 4 == 0) Capture($"kill_seq_{f:00}");
                yield return null;
            }

            string[] faces = { "bolt", "melee", "mend", "twin", "devour", "reroll", "meteor", "frost", "aegis", "goldrush", "magnet" };
            foreach (var id in faces)
            {
                if (d.State == GameState.LevelUp) d.PickOffer(0);
                var b = d.Ecs.Get<Buffs>(); b.Invuln = 999f; d.Ecs.Set(b);
                var pp = d.Ecs.PlayerPos;
                for (int i = 0; i < 10; i++)
                {
                    float a = i * Mathf.PI * 2f / 10f;
                    d.Ecs.SpawnEnemy(grunt, pp + new float2(Mathf.Cos(a), Mathf.Sin(a)) * 6f, false, 2f, 0f, 0.2f);
                }
                yield return WaitReal(0.3f);
                d.DebugActivateFace(id, 3);
                yield return WaitReal(id == "meteor" ? 0.5f : 0.1f);
                Capture($"face_{id}_a");
                yield return WaitReal(id == "meteor" ? 0.45f : 0.35f);
                Capture($"face_{id}_b");
                yield return WaitReal(1.2f);
            }
            Assert.AreEqual(GameState.Playing, d.State == GameState.LevelUp ? GameState.Playing : d.State);
            d.QuitToMenu();
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RolledFaceIsUsedByClickingWithCooldown()
        {
            var d = GameDirector.Instance;
            var bot = new Bot { Still = true, Mouse = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f) };
            GameInput.Bot = bot.Apply;
            d.StartRun(GameDatabase.Characters[0]);
            yield return null;
            Assert.IsNull(d.Ability, "a new run starts without an ability");

            // Melee: click -> cooldown starts -> it comes back.
            d.DebugSetAbility("melee", 1);
            Assert.IsNotNull(d.Ability);
            Assert.IsTrue(d.Ability.Ready);
            bot.LmbDownNext = true;
            yield return null;
            yield return null;
            Assert.IsFalse(d.Ability.Ready, "using the ability should start its cooldown");
            Assert.Greater(d.Ability.CooldownLeft, 0f);
            float t0 = Time.realtimeSinceStartup;
            while (!d.Ability.Ready && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Assert.IsTrue(d.Ability.Ready, "the cooldown should finish");

            // Clicking during the cooldown does nothing but shake the ring.
            d.DebugSetAbility("devour", 1);
            bot.LmbDownNext = true;
            yield return null;
            yield return null;
            float cd = d.Ability.CooldownLeft;
            Assert.Greater(cd, 1f, "Devour has a long cooldown");
            bot.LmbDownNext = true;
            yield return null;
            Assert.LessOrEqual(d.Ability.CooldownLeft, cd, "a click during the cooldown must not restart it");

            // Refresh is a one-use face: it refills the roll count to full and is spent.
            d.DebugSetAbility("reroll", 1);
            d.Run.RollsLeft = 0;
            bot.LmbDownNext = true;
            yield return null;
            yield return null;
            Assert.IsNull(d.Ability, "Refresh is used up after one click");
            Assert.GreaterOrEqual(d.Run.RollsLeft, d.Run.RollsPerWave, "Refresh refills the dice rolls");
            Assert.Greater(d.Run.RollsLeft, 0);

            // Rolling sets a new ability once the dice land.
            bot.RollNext = true;
            yield return WaitReal(1.2f);
            Assert.IsNotNull(d.Ability, "the rolled face becomes the current ability");
            Capture("ability_after_roll");
            d.QuitToMenu();
        }

        // ------------------------------------------------------------------ helpers

        static IEnumerator WaitReal(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                var d = GameDirector.Instance;
                if (d != null && d.State == GameState.LevelUp) d.PickOffer(0);
                yield return null;
            }
        }

        static void ShopOnce(GameDirector d)
        {
            // Free swap and swap back.
            d.ClickDieSlot(0, 0); d.ClickDieSlot(0, 1);
            d.ClickDieSlot(0, 1); d.ClickDieSlot(0, 0);
            foreach (var o in d.ShopOffers.ToArray())
            {
                if (o.Sold || d.Run.Gold < o.Cost) continue;
                d.BuyShopOffer(o);
                if (d.Placing != PlaceMode.None) d.ClickDieSlot(0, 5);
            }
            if (d.Run.Gold >= d.ShopRerollCost) d.RerollShop();
        }

        static bool TryNearestEnemyScreen(GameDirector d, out Vector2 screen)
        {
            screen = default;
            var pp = d.Ecs.PlayerPos;
            using (var pos = d.Ecs.EnemyPositions(Allocator.Temp))
            {
                int best = -1;
                float bd = 400f;
                for (int i = 0; i < pos.Length; i++)
                {
                    float dd = math.distancesq(pos[i].Value, pp);
                    if (dd < bd) { bd = dd; best = i; }
                }
                if (best < 0) return false;
                var s = d.Rig.Cam.WorldToScreenPoint(new Vector3(pos[best].Value.x, 0f, pos[best].Value.y));
                screen = new Vector2(s.x, s.y);
                return true;
            }
        }

        void Capture(string label)
        {
            captureRequested = true;
            captureLabel = label;
        }

        void OnAfterDraw()
        {
            if (!captureRequested) return;
            captureRequested = false;
            var d = GameDirector.Instance;
            if (d == null || d.Rig == null || d.Rig.Cam == null) return;
            var cam = d.Rig.Cam;
            const int w = 960, h = 540;
            var rt = RenderTexture.GetTemporary(w, h, 24);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevTarget;
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(captureDir, $"{captureIndex++:000}_{captureLabel}.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
