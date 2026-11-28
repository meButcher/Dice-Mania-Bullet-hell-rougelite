using System.Collections;
using System.IO;
using DiceMania.Data;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DiceMania.Game
{
    /// <summary>
    /// Command line <c>-hudshots &lt;folder&gt;</c>: plays a short scripted run with bot input, saves screenshots of the HUD
    /// (menu, rolling 3D die, the rolled ability, cooldown ring, Melee, Bullet, two dice, game over) and quits.
    /// Lets a build's UI be checked with nobody at the keyboard. Uses its own save slot, never the player's.
    /// </summary>
    public class HudShots : MonoBehaviour
    {
        const string SaveKey = "dicemania_save_hudshots";
        string dir;
        int index;
        Vector2 mouse;
        bool lmbDown, lmbHeld, roll;

        public static void StartIfRequested(GameDirector d)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer || Application.isEditor) return;
            string[] args;
            try { args = System.Environment.GetCommandLineArgs(); }
            catch (System.Exception) { return; }
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-hudshots")
                {
                    d.gameObject.AddComponent<HudShots>().dir = args[i + 1];
                    return;
                }
        }

        void Start()
        {
            Application.runInBackground = true;
            MetaProgress.Key = SaveKey;
            PlayerPrefs.DeleteKey(SaveKey);
            MetaProgress.Load();
            StartCoroutine(Script());
        }

        void Bot()
        {
            GameInput.Move = Vector2.zero;
            GameInput.MousePos = mouse;
            GameInput.LmbDown = lmbDown;
            lmbDown = false;
            GameInput.LmbHeld = lmbHeld || GameInput.LmbDown;
            GameInput.Roll = roll;
            roll = false;
        }

        IEnumerator Shot(string name)
        {
            // End of frame: the back buffer holds the finished frame, HUD included.
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply(false);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{index++:00}_{name}.png"), tex.EncodeToPNG());
            Destroy(tex);
            yield return null;
        }

        static IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds); }

        void AimAtNearestEnemy(GameDirector d)
        {
            if (d.Rig == null || d.Rig.Cam == null || d.Ecs == null) return;
            float2 pp = d.Ecs.PlayerPos, best = pp + new float2(4f, 1f);
            float bd = float.MaxValue;
            using (var ep = d.Ecs.EnemyPositions(Allocator.Temp))
                for (int i = 0; i < ep.Length; i++)
                {
                    float dd = math.distancesq(ep[i].Value, pp);
                    if (dd < bd) { bd = dd; best = ep[i].Value; }
                }
            var sp = d.Rig.Cam.WorldToScreenPoint(new Vector3(best.x, 0f, best.y));
            mouse = new Vector2(sp.x, sp.y);
        }

        IEnumerator Use(GameDirector d, string face, int level, string shot, float shotDelay)
        {
            d.DebugSetAbility(face, level);
            yield return Wait(0.35f);
            AimAtNearestEnemy(d);
            lmbDown = true;
            yield return Wait(shotDelay);
            yield return Shot(shot);
        }

        IEnumerator Script()
        {
            var d = GameDirector.Instance;
            while (d == null || d.State != GameState.MainMenu) { yield return null; d = GameDirector.Instance; }
            yield return Wait(1.2f);
            yield return Shot("menu");

            GameInput.Bot = Bot;
            mouse = new Vector2(Screen.width * 0.62f, Screen.height * 0.55f);
            d.StartRun(GameDatabase.Characters[0]);
            yield return Wait(3f);
            yield return Shot("run_start");

            roll = true;
            yield return Wait(0.16f);
            yield return Shot("die_tumbling");
            float t0 = Time.realtimeSinceStartup;
            while (d.Ability == null && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            yield return Wait(0.12f);
            yield return Shot("die_landed");

            yield return Use(d, "melee", 2, "melee_slash", 0.05f);
            yield return Wait(0.12f);
            yield return Shot("melee_cooldown");

            yield return Use(d, "devour", 1, "devour", 0.5f);
            yield return Wait(2.2f);
            yield return Shot("devour_cooldown_ring");
            lmbDown = true; // clicking during the cooldown shakes the ring
            yield return Wait(0.05f);
            yield return Shot("cooldown_deny");

            d.DebugSetAbility("bolt", 3);
            yield return Wait(0.3f);
            AimAtNearestEnemy(d);
            lmbDown = true;
            lmbHeld = true;
            yield return Wait(0.07f);
            yield return Shot("bullet_fire");
            yield return Wait(0.6f);
            yield return Shot("bullet_hold");
            lmbHeld = false;

            d.DebugSetAbility("reroll", 1);
            yield return Wait(0.4f);
            yield return Shot("refresh_ready");

            // Morph: hold on an enemy (slow motion, marker + beam), release to fire, become the enemy.
            d.DebugSetAbility("copy", 1);
            yield return Wait(0.4f);
            AimAtNearestEnemy(d);
            lmbDown = true;
            lmbHeld = true;
            yield return Wait(0.35f);
            AimAtNearestEnemy(d);
            yield return Wait(0.1f);
            yield return Shot("morph_aim");
            lmbHeld = false;
            yield return Wait(0.25f);
            yield return Shot("morph_shot");
            t0 = Time.realtimeSinceStartup;
            while (d.Copy != CopyPhase.None && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            yield return Wait(0.5f);
            yield return Shot("morphed");

            // A two-dice hero (the Gambler) for the multi-dice layout.
            var gambler = GameDatabase.Character("gambler");
            if (gambler != null)
            {
                d.QuitToMenu();
                yield return Wait(0.5f);
                d.StartRun(gambler);
                yield return Wait(2f);
                roll = true;
                yield return Wait(0.16f);
                yield return Shot("two_dice_tumbling");
                yield return Wait(1f);
                yield return Shot("two_dice_landed");
            }

            // The end screen: one HP left, and the next hit ends the run (the death check runs when damage lands).
            t0 = Time.realtimeSinceStartup;
            while (d.State != GameState.GameOver && Time.realtimeSinceStartup - t0 < 25f)
            {
                if (d.State == GameState.Playing && d.Ecs.Player != Unity.Entities.Entity.Null)
                {
                    var h = d.Ecs.PlayerHealth;
                    if (h.Value > 1f) { h.Value = 1f; d.Ecs.SetPlayerHealth(h); }
                }
                yield return null;
            }
            yield return Wait(0.8f);
            yield return Shot("game_over");

            GameInput.Bot = null;
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            yield return Wait(0.5f);
            Application.Quit();
        }
    }
}
