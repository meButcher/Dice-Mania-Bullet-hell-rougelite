using DiceMania.Data;
using DiceMania.ECS;
using DiceMania.Presentation;
using UnityEngine;

namespace DiceMania.Game
{
    /// <summary>
    /// Prototype UI drawn with IMGUI (zero assets, works on Web). Everything is laid out on a 1280x720 virtual canvas.
    /// To be replaced by UI Toolkit in the visual pass - all logic lives in GameDirector, this only draws and forwards clicks.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        enum MenuPage { Home, Characters, Upgrades, Unlocks, HowTo }

        GameDirector D => GameDirector.Instance;
        MenuPage page = MenuPage.Home;
        int charIndex;
        float scale = 1f;
        float W => Screen.width / scale;
        const float H = 720f;

        GUIStyle label, labelC, labelR, big, huge, small, button, card, tiny;
        Texture2D white, vignette, burst;
        bool stylesReady;
        float hpGhost = 1f, lastHpFrac = 1f, lastHpDrop;

        static readonly Color Panel = new Color(0.06f, 0.07f, 0.14f, 0.92f);
        static readonly Color PanelLight = new Color(0.12f, 0.14f, 0.26f, 0.95f);
        static readonly Color Accent = new Color(0.35f, 0.42f, 0.75f, 1f);

        void InitStyles()
        {
            white = Texture2D.whiteTexture;
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
            label.normal.textColor = Color.white;
            labelC = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            labelR = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
            small = new GUIStyle(label) { fontSize = 13 };
            tiny = new GUIStyle(label) { fontSize = 11 };
            big = new GUIStyle(labelC) { fontSize = 28, fontStyle = FontStyle.Bold };
            huge = new GUIStyle(labelC) { fontSize = 64, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            button.normal.textColor = button.hover.textColor = button.active.textColor = Color.white;
            card = new GUIStyle(label) { fontSize = 15, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
            vignette = MakeVignette(128);
            burst = MakeBurstLines(256);
            stylesReady = true;
        }

        /// <summary>White radial edge gradient (tinted red for hurt / low HP).</summary>
        static Texture2D MakeVignette(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "DM_Vignette" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.2f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, r));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>Anime burst lines: random wedges radiating from a clear centre.</summary>
        static Texture2D MakeBurstLines(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "DM_BurstLines" };
            var px = new Color32[size * size];
            const int rays = 56;
            var rnd = new System.Random(3);
            var width = new float[rays];
            var start = new float[rays];
            for (int i = 0; i < rays; i++) { width[i] = 0.15f + (float)rnd.NextDouble() * 0.45f; start[i] = 0.42f + (float)rnd.NextDouble() * 0.3f; }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a01 = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f) % 1f;
                float f = a01 * rays;
                int i = (int)f % rays;
                float within = f - Mathf.Floor(f);
                float w = width[i] * Mathf.InverseLerp(start[i], 1.3f, r);   // rays get wider toward the edge
                float a = within < w ? 1f : 0f;
                a *= Mathf.InverseLerp(start[i], start[i] + 0.12f, r);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void Bar(Rect r, float frac, Color fg, Color bg)
        {
            Fill(r, bg);
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), fg);
        }

        bool Button(Rect r, string text, bool enabled = true, Color? tint = null)
        {
            var old = GUI.enabled;
            GUI.enabled = enabled;
            Fill(r, enabled ? (tint ?? Accent) : new Color(0.25f, 0.25f, 0.3f));
            if (r.Contains(Event.current.mousePosition) && enabled) Fill(r, new Color(1, 1, 1, 0.12f));
            bool clicked = GUI.Button(r, text, button);
            GUI.enabled = old;
            if (clicked && D != null) D.Audio.Play(SfxPlayer.Id.Click, 0.02f, 0.6f);
            return clicked;
        }

        void Text(Rect r, string s, GUIStyle st, Color? c = null)
        {
            var old = st.normal.textColor;
            if (c.HasValue) st.normal.textColor = c.Value;
            GUI.Label(r, s, st);
            st.normal.textColor = old;
        }

        void Shadowed(Rect r, string s, GUIStyle st, Color c)
        {
            Text(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st, new Color(0, 0, 0, 0.7f));
            Text(r, s, st, c);
        }

        void OnGUI()
        {
            if (D == null) return;
            if (!stylesReady) InitStyles();
            scale = Screen.height / H;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            DrawScreenFx();
            DrawFloaters();
            switch (D.State)
            {
                case GameState.Boot: Text(new Rect(0, 0, W, H), "Loading...", big); break;
                case GameState.MainMenu: DrawMenu(); break;
                case GameState.Playing:
                case GameState.WaveClear:
                case GameState.Dying:
                    DrawHud(); break;
                case GameState.LevelUp: DrawHud(); DrawLevelUp(); break;
                case GameState.Forge: DrawForge(); break;
                case GameState.Paused: DrawHud(); DrawPause(); break;
                case GameState.GameOver: DrawEnd(false); break;
                case GameState.Victory: DrawEnd(true); break;
            }
            if (D.BannerTime > 0f && D.State != GameState.MainMenu)
            {
                float a = Mathf.Clamp01(D.BannerTime * 2f);
                var c = D.BannerColor; c.a = a;
                Shadowed(new Rect(0, 150, W, 50), D.Banner, big, c);
            }
        }

        // ================================================================== world floaters

        void DrawFloaters()
        {
            if (D.Rig == null || D.Rig.Cam == null) return;
            var cam = D.Rig.Cam;
            var baseM = GUI.matrix;
            int old = labelC.fontSize;
            foreach (var f in D.Floaters)
            {
                var sp = cam.WorldToScreenPoint(f.World);
                if (sp.z < 0) continue;
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale;
                float life = f.Life > 0f ? f.Life : 0.8f;
                var c = f.Color; c.a = 1f - Mathf.Clamp01((f.Age - life * 0.6f) / (life * 0.4f));
                // Pop in big, settle fast (scaled with the GUI matrix so the font atlas isn't rebuilt per size).
                float pop = 1f + f.Pop * Mathf.Max(0f, 1f - f.Age / 0.12f);
                labelC.fontSize = Mathf.RoundToInt(15 * f.Size);
                GUI.matrix = baseM * Matrix4x4.TRS(new Vector3(x, y, 0f), Quaternion.identity, new Vector3(pop, pop, 1f)) * Matrix4x4.Translate(new Vector3(-x, -y, 0f));
                float h = labelC.fontSize * 1.6f;
                Shadowed(new Rect(x - 80, y - h * 0.5f, 160, h), f.Text, labelC, c);
            }
            GUI.matrix = baseM;
            labelC.fontSize = old;
        }

        // ================================================================== screen feel

        void DrawScreenFx()
        {
            // Hurt vignette, plus a slow red pulse when HP is low.
            float hurt = D.HurtFlash;
            bool inRun = D.Run != null && D.Ecs != null && D.Ecs.Player != Unity.Entities.Entity.Null &&
                         (D.State == GameState.Playing || D.State == GameState.WaveClear || D.State == GameState.LevelUp || D.State == GameState.Paused);
            if (inRun)
            {
                var hp = D.Ecs.PlayerHealth;
                float frac = hp.Value / Mathf.Max(1f, hp.Max);
                if (frac < 0.3f) hurt = Mathf.Max(hurt, (0.3f - frac) / 0.3f * (0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f)));
            }
            var oldC = GUI.color;
            if (hurt > 0.01f)
            {
                GUI.color = new Color(1f, 0.08f, 0.12f, Mathf.Clamp01(hurt) * 0.55f);
                GUI.DrawTexture(new Rect(0, 0, W, H), vignette);
            }
            if (D.BurstLines > 0.01f)
            {
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot((Time.frameCount * 37) % 360, new Vector2(W * 0.5f, H * 0.5f));
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(D.BurstLines) * 0.75f);
                float s = Mathf.Max(W, H) * 1.5f;
                GUI.DrawTexture(new Rect(W * 0.5f - s * 0.5f, H * 0.5f - s * 0.5f, s, s), burst);
                GUI.matrix = m;
            }
            if (D.ScreenFlash > 0.01f)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(D.ScreenFlash) * 0.8f);
                GUI.DrawTexture(new Rect(0, 0, W, H), white);
            }
            GUI.color = oldC;
        }

        // ================================================================== HUD

        void DrawHud()
        {
            var run = D.Run;
            if (run == null || D.Ecs == null || D.Ecs.Player == Unity.Entities.Entity.Null) return;
            var hp = D.Ecs.PlayerHealth;

            // XP bar across the top.
            Bar(new Rect(0, 0, W, 10), run.Xp / Mathf.Max(1f, run.XpToNext), new Color(0.3f, 0.6f, 1f), new Color(0, 0, 0, 0.6f));
            Shadowed(new Rect(8, 10, 120, 22), "LV " + run.Level, small, new Color(0.6f, 0.8f, 1f));

            // HP: the bar shakes when hit and a white "ghost" drains behind it, so you can read how much you lost.
            float hpFrac = Mathf.Clamp01(hp.Value / Mathf.Max(1f, hp.Max));
            if (Event.current.type == EventType.Repaint)
            {
                if (hpFrac < lastHpFrac - 0.0005f) lastHpDrop = Time.unscaledTime;
                if (hpFrac >= hpGhost) hpGhost = hpFrac;
                else if (Time.unscaledTime - lastHpDrop > 0.35f) hpGhost = Mathf.MoveTowards(hpGhost, hpFrac, Time.unscaledDeltaTime * 0.7f);
                lastHpFrac = hpFrac;
            }
            float jolt = D.HurtFlash * 5f;
            Vector2 off = jolt > 0.05f ? new Vector2(Random.Range(-jolt, jolt), Random.Range(-jolt, jolt)) : Vector2.zero;
            Fill(new Rect(14 + off.x, 34 + off.y, 16, 16), Color.white);
            var hpRect = new Rect(38 + off.x, 36 + off.y, 260, 14);
            Fill(hpRect, new Color(0.2f, 0.05f, 0.08f, 0.8f));
            Fill(new Rect(hpRect.x, hpRect.y, hpRect.width * hpGhost, hpRect.height), new Color(1f, 0.92f, 0.85f, 0.9f));
            Fill(new Rect(hpRect.x, hpRect.y, hpRect.width * hpFrac, hpRect.height), Color.Lerp(new Color(1f, 0.3f, 0.35f), Color.white, D.HurtFlash * 0.6f));
            Shadowed(new Rect(304 + off.x, 30 + off.y, 160, 24), $"{Mathf.CeilToInt(hp.Value)}/{Mathf.CeilToInt(hp.Max)}", small, Color.white);

            // Weapons + passives.
            float x = 14;
            foreach (var w in run.Weapons)
            {
                Fill(new Rect(x, 58, 30, 30), w.Def.Color * 0.8f);
                Text(new Rect(x, 58, 30, 30), w.Def.IsEvolution ? "★" : w.Level.ToString(), labelC, Color.black);
                x += 34;
            }
            x = 14;
            foreach (var p in run.Passives)
            {
                Fill(new Rect(x, 92, 22, 22), new Color(0.5f, 0.7f, 1f, 0.7f));
                Text(new Rect(x, 92, 22, 22), p.Level.ToString(), labelC, Color.black);
                x += 25;
            }
            if (run.Relics.Count > 0) Shadowed(new Rect(14, 116, 300, 20), "Relics: " + run.Relics.Count, tiny, new Color(1f, 0.8f, 0.4f));

            // Wave / timer / boss.
            string waveText = "WAVE " + run.Wave.ToString("00") + (run.Wave > D.FinalWave ? " (ENDLESS)" : "");
            Shadowed(new Rect(0, 14, W, 30), waveText, big, Color.white);
            if (D.BossAlive)
            {
                Shadowed(new Rect(0, 46, W, 24), D.BossName, labelC, new Color(1f, 0.5f, 0.75f));
                Bar(new Rect(W / 2 - 250, 72, 500, 14), D.BossHpFrac, new Color(1f, 0.25f, 0.6f), new Color(0.15f, 0.02f, 0.1f, 0.85f));
            }
            else if (D.State == GameState.Playing)
                Shadowed(new Rect(0, 46, W, 26), Mathf.CeilToInt(Mathf.Max(0, D.WaveTimeLeft)) + "s", labelC, new Color(1, 1, 1, 0.85f));

            // Score / gold / kills.
            Shadowed(new Rect(W - 260, 14, 245, 24), "Score: " + run.Score, labelR, Color.white);
            Shadowed(new Rect(W - 260, 38, 245, 24), "Gold: " + run.Gold, labelR, new Color(1f, 0.82f, 0.25f));
            Shadowed(new Rect(W - 260, 62, 245, 24), "Kills: " + run.Kills, labelR, new Color(1, 1, 1, 0.7f));

            // Rolls (bottom left): the counter punches when you roll.
            Fill(new Rect(14, H - 98, 84, 84), PanelLight);
            {
                var baseM = GUI.matrix;
                float pop = 1f + 0.35f * D.RollPunch;
                var pivot = new Vector3(56, H - 66, 0);
                GUI.matrix = baseM * Matrix4x4.TRS(pivot, Quaternion.identity, new Vector3(pop, pop, 1f)) * Matrix4x4.Translate(-pivot);
                Shadowed(new Rect(14, H - 98, 84, 64), run.RollsLeft.ToString(), huge, run.RollsLeft > 0 ? Color.white : new Color(1, 0.4f, 0.4f));
                GUI.matrix = baseM;
            }
            float recharge = GameDatabase.Cfg("RollRechargeSeconds", 25);
            if (recharge > 0) Bar(new Rect(18, H - 28, 76, 6), run.RollRechargeTimer / recharge, new Color(0.7f, 0.5f, 1f), new Color(0, 0, 0, 0.5f));
            Shadowed(new Rect(104, H - 46, 220, 22), "Dice rolls  [SPACE]", small, new Color(1, 1, 1, 0.8f));
            if (run.CopyCharges > 0)
                Shadowed(new Rect(104, H - 70, 320, 22), $"COPY x{run.CopyCharges}  [hold LMB on enemy]", small, new Color(1f, 0.45f, 0.55f));

            DrawDiceTray();

            // Copy form timer.
            var copy = D.Ecs.PlayerCopy;
            if (copy.Time > 0f && copy.EnemyDef >= 0 && copy.EnemyDef < GameDatabase.Enemies.Count)
            {
                var def = GameDatabase.Enemies[copy.EnemyDef];
                Shadowed(new Rect(W - 300, H - 70, 285, 22), "Copied: " + def.Name, labelR, def.Color);
                Bar(new Rect(W - 300, H - 44, 285, 10), copy.Time / Mathf.Max(0.01f, copy.TotalTime), def.Color, new Color(0, 0, 0, 0.6f));
            }
            var buffs = D.Ecs.Get<Buffs>();
            float by = H - 100;
            if (buffs.Invuln > 0f) { Shadowed(new Rect(W - 300, by, 285, 22), $"Aegis {buffs.Invuln:0.0}s", labelR, new Color(1f, 0.85f, 0.4f)); by -= 22; }
            if (buffs.GoldRush > 0f) { Shadowed(new Rect(W - 300, by, 285, 22), $"Gold Rush {buffs.GoldRush:0.0}s", labelR, new Color(1f, 0.8f, 0f)); }

            // Copy aim hint at the mouse.
            if (D.Copy == CopyPhase.Aiming)
            {
                Vector2 m = Event.current.mousePosition;
                string t = D.CopyTarget != Unity.Entities.Entity.Null ? "Release to COPY: " + D.CopyTargetName : "Move over an enemy...";
                Shadowed(new Rect(m.x + 16, m.y - 10, 300, 22), t, small, new Color(1f, 0.5f, 0.6f));
            }
            if (D.State == GameState.Playing && run.Wave == 1 && D.WaveElapsed < 12f && !MetaProgress.Data.SeenTutorial)
                Shadowed(new Rect(0, H - 150, W, 24), "WASD move - weapons fire automatically - SPACE rolls your dice - ESC pause", labelC, new Color(1, 1, 1, 0.8f));
        }

        void DrawDiceTray()
        {
            var run = D.Run;
            int n = run.Dice.Count;
            float size = 70, gap = 12;
            float total = n * size + (n - 1) * gap;
            float x0 = W / 2 - total / 2, y = H - size - 16;
            bool anim = D.RollAnimTime > 0f;
            var roll = D.LastRoll;
            var trayM = GUI.matrix;
            for (int d = 0; d < n; d++)
            {
                var die = run.Dice[d];
                var r = new Rect(x0 + d * (size + gap), y, size, size);
                // Tumble: rattle while rolling; land: pop.
                if (anim) r.position += new Vector2(Mathf.Sin(Time.unscaledTime * 55f + d) * 3f, Mathf.Cos(Time.unscaledTime * 47f + d * 2f) * 3f);
                float pop = 1f + (anim ? 0f : 0.28f * D.RollPunch);
                if (pop > 1.001f)
                {
                    var pivot = new Vector3(r.center.x, r.center.y, 0f);
                    GUI.matrix = trayM * Matrix4x4.TRS(pivot, Quaternion.identity, new Vector3(pop, pop, 1f)) * Matrix4x4.Translate(-pivot);
                }
                else GUI.matrix = trayM;
                FaceSlot shown;
                if (anim) shown = die.Slots[(int)(Time.unscaledTime * 20f + d * 3) % 6];
                else if (roll != null && d < roll.Rolls.Count) shown = die.Slots[roll.Rolls[d].SlotIndex];
                else shown = null;
                Fill(r, PanelLight);
                if (shown != null)
                {
                    float pad = anim ? 6 + Mathf.Sin(Time.unscaledTime * 40f) * 3 : 4;
                    Fill(new Rect(r.x + pad, r.y + pad, r.width - pad * 2, r.height - pad * 2), shown.Face.Color);
                    Text(new Rect(r.x, r.y + 6, r.width, 30), shown.Face.Name, labelC, Color.black);
                    if (!anim) Text(new Rect(r.x, r.y + 34, r.width, 30), "Lv " + shown.Level, small, Color.black);
                }
                else Text(r, "?", big, new Color(1, 1, 1, 0.5f));
            }
            GUI.matrix = trayM;
            if (roll != null && D.RollShowTime > 0f && !string.IsNullOrEmpty(roll.ComboText))
                Shadowed(new Rect(0, y - 30, W, 26), roll.ComboText, labelC, new Color(1f, 0.85f, 0.25f));
        }

        // ================================================================== level up

        void DrawLevelUp()
        {
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f));
            Shadowed(new Rect(0, 100, W, 60), D.LevelUpIsChest ? "TREASURE CHEST!" : "LEVEL UP!", huge, new Color(1f, 0.9f, 0.4f));
            int n = D.LevelOffers.Count;
            float cw = 260, ch = 230, gap = 20;
            float x0 = W / 2 - (n * cw + (n - 1) * gap) / 2;
            for (int i = 0; i < n; i++)
            {
                var o = D.LevelOffers[i];
                var r = new Rect(x0 + i * (cw + gap), 220, cw, ch);
                Fill(r, Panel);
                Fill(new Rect(r.x, r.y, r.width, 6), o.Color);
                bool hover = r.Contains(Event.current.mousePosition);
                if (hover) Fill(r, new Color(1, 1, 1, 0.08f));
                Text(new Rect(r.x + 12, r.y + 14, r.width - 24, 20), o.Tag, tiny, o.Color);
                Text(new Rect(r.x + 12, r.y + 34, r.width - 24, 50), o.Title, new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold });
                Text(new Rect(r.x + 12, r.y + 90, r.width - 24, 100), o.Body, label, new Color(1, 1, 1, 0.85f));
                Text(new Rect(r.x, r.yMax - 30, r.width, 24), $"[{i + 1}]", labelC, new Color(1, 1, 1, 0.5f));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) D.PickOffer(i);
            }
            int cost = Mathf.RoundToInt(GameDatabase.Cfg("LevelUpRerollRollCost", 1));
            if (Button(new Rect(W / 2 - 150, 480, 300, 44), $"Reroll ({cost} dice roll, {D.Run.RollsLeft} left)", D.CanRerollLevelUp, new Color(0.45f, 0.3f, 0.75f)))
                D.RerollLevelUp();
        }

        // ================================================================== forge

        Vector2 forgeScroll;

        void DrawForge()
        {
            var run = D.Run;
            Fill(new Rect(0, 0, W, H), new Color(0.02f, 0.025f, 0.06f, 0.97f));
            Shadowed(new Rect(0, 12, W, 44), "THE DICE FORGE", big, new Color(1f, 0.85f, 0.4f));
            Text(new Rect(0, 50, W, 22), $"Wave {run.Wave} cleared - +{D.LastWaveGold} gold, +{D.LastInterest} interest", labelC, new Color(1, 1, 1, 0.7f));
            Shadowed(new Rect(W - 300, 14, 280, 30), "Gold: " + run.Gold, new GUIStyle(labelR) { fontSize = 22 }, new Color(1f, 0.82f, 0.25f));
            var hp = D.Ecs.PlayerHealth;
            Text(new Rect(20, 14, 300, 24), $"HP {Mathf.CeilToInt(hp.Value)}/{Mathf.CeilToInt(hp.Max)}", label, new Color(1f, 0.5f, 0.5f));

            // ---- Shop (left) ----
            float sx = 20, sy = 90, sw = 520;
            Text(new Rect(sx, sy, sw, 24), "SHOP", label, new Color(1, 1, 1, 0.6f));
            sy += 26;
            foreach (var o in D.ShopOffers)
            {
                var r = new Rect(sx, sy, sw, 78);
                Fill(r, o.Sold ? new Color(0.08f, 0.08f, 0.1f, 0.9f) : Panel);
                Fill(new Rect(r.x, r.y, 6, r.height), o.Color);
                Text(new Rect(r.x + 14, r.y + 6, 330, 24), o.Title, new GUIStyle(label) { fontStyle = FontStyle.Bold }, o.Sold ? Color.gray : Color.white);
                Text(new Rect(r.x + 14, r.y + 30, 340, 46), o.Body, small, new Color(1, 1, 1, o.Sold ? 0.4f : 0.75f));
                if (o.Sold) Text(new Rect(r.xMax - 150, r.y + 24, 140, 30), "SOLD", labelC, Color.gray);
                else
                {
                    bool afford = run.Gold >= o.Cost;
                    if (Button(new Rect(r.xMax - 150, r.y + 8, 140, 34), $"Buy {o.Cost}g", afford && D.Placing == PlaceMode.None, new Color(0.25f, 0.55f, 0.3f)))
                        D.BuyShopOffer(o);
                    if (Button(new Rect(r.xMax - 150, r.y + 46, 140, 24), o.Locked ? "Locked" : "Lock", true, o.Locked ? new Color(0.7f, 0.5f, 0.15f) : new Color(0.25f, 0.27f, 0.4f)))
                        D.ToggleLock(o);
                }
                sy += 84;
            }
            if (Button(new Rect(sx, sy + 4, 250, 40), $"Reroll shop ({D.ShopRerollCost}g)", run.Gold >= D.ShopRerollCost && D.Placing == PlaceMode.None, new Color(0.45f, 0.3f, 0.75f)))
                D.RerollShop();

            // ---- Dice editor (right) ----
            float dx = 570, dy = 90;
            string hint = D.Placing == PlaceMode.NewFace ? $"Click a slot to place {D.PlacingOffer.Face.Name} ({D.PlacingOffer.Cost}g)"
                : D.Placing == PlaceMode.LoadFace ? $"Click a slot to add +1 weight ({D.PlacingOffer.Cost}g)"
                : D.SwapDie >= 0 ? "Click another slot to swap faces"
                : "YOUR DICE - click two slots to swap faces";
            Text(new Rect(dx, dy, W - dx - 20, 24), hint, label, D.Placing != PlaceMode.None || D.SwapDie >= 0 ? new Color(1f, 0.85f, 0.3f) : new Color(1, 1, 1, 0.6f));
            if (D.Placing != PlaceMode.None || D.SwapDie >= 0)
                if (Button(new Rect(W - 140, dy - 2, 120, 26), "Cancel", true, new Color(0.5f, 0.2f, 0.2f))) D.CancelPlacing();
            dy += 30;
            float slotW = (W - dx - 20 - 5 * 8) / 6f;
            for (int d = 0; d < run.Dice.Count; d++)
            {
                var die = run.Dice[d];
                Text(new Rect(dx, dy, 200, 20), "Die " + (d + 1), small, new Color(1, 1, 1, 0.6f));
                dy += 20;
                for (int s = 0; s < 6; s++)
                {
                    var slot = die.Slots[s];
                    var r = new Rect(dx + s * (slotW + 8), dy, slotW, 96);
                    Fill(r, PanelLight);
                    Fill(new Rect(r.x + 3, r.y + 3, r.width - 6, 40), slot.Face.Color);
                    Text(new Rect(r.x, r.y + 3, r.width, 22), slot.Face.Name, new GUIStyle(labelC) { fontStyle = FontStyle.Bold }, Color.black);
                    Text(new Rect(r.x, r.y + 22, r.width, 20), "pip " + (s + 1), new GUIStyle(labelC) { fontSize = 11 }, new Color(0, 0, 0, 0.6f));
                    Text(new Rect(r.x, r.y + 46, r.width, 20), $"Lv {slot.Level}/{slot.Face.MaxLevel}", new GUIStyle(labelC) { fontSize = 13 });
                    Text(new Rect(r.x, r.y + 66, r.width, 20), $"w{slot.Weight}  {die.Chance(s) * 100f:0}%", new GUIStyle(labelC) { fontSize = 12 }, new Color(1, 1, 1, 0.7f));
                    if (D.SwapDie == d && D.SwapSlot == s) Fill(r, new Color(1f, 0.85f, 0.3f, 0.25f));
                    if (r.Contains(Event.current.mousePosition)) Fill(r, new Color(1, 1, 1, 0.07f));
                    if (GUI.Button(r, new GUIContent("", slot.Face.Describe(slot.Level)), GUIStyle.none)) D.ClickDieSlot(d, s);
                }
                dy += 104;
            }
            if (!string.IsNullOrEmpty(GUI.tooltip))
                Text(new Rect(dx, dy, W - dx - 20, 44), GUI.tooltip, small, new Color(0.8f, 0.9f, 1f));
            dy += 50;

            // ---- Build summary ----
            Text(new Rect(dx, dy, W - dx - 20, 22), "BUILD", small, new Color(1, 1, 1, 0.6f));
            dy += 22;
            string weapons = string.Join(", ", run.Weapons.ConvertAll(w => $"{w.Def.Name} {(w.Def.IsEvolution ? "★" : "Lv" + w.Level)}"));
            string passives = string.Join(", ", run.Passives.ConvertAll(p => $"{p.Def.Name} {p.Level}"));
            string relics = string.Join(", ", run.Relics.ConvertAll(r => r.Name));
            Text(new Rect(dx, dy, W - dx - 20, 120),
                $"Weapons: {weapons}\nPassives: {(passives.Length > 0 ? passives : "-")}\nRelics: {(relics.Length > 0 ? relics : "-")}\n" +
                $"Rolls/wave {run.RollsPerWave}  Might x{run.Might:0.00}  Area x{run.Area:0.00}  Armor {run.Armor:0}  Luck {run.Luck * 100:0}%",
                small, new Color(1, 1, 1, 0.8f));

            string next = run.Wave + 1 > D.FinalWave ? $"Wave {run.Wave + 1} (Endless)" : $"Start Wave {run.Wave + 1}";
            var nw = GameDatabase.WaveFor(run.Wave + 1);
            if (nw != null && nw.IsBossWave) next += "  - BOSS!";
            if (nw != null)
            {
                // Next wave preview: who's coming.
                var names = new System.Collections.Generic.List<string>();
                if (nw.IsBossWave) { var b = GameDatabase.Enemy(nw.Boss); if (b != null) names.Add("<b>" + b.Name + "</b>"); }
                foreach (var m in nw.Mix) { var e = GameDatabase.Enemy(m.id); if (e != null && !names.Contains(e.Name)) names.Add(e.Name); }
                Text(new Rect(W - 640, H - 104, 620, 26), "Next: " + string.Join(", ", names), new GUIStyle(labelR) { fontSize = 14, richText = true },
                    nw.IsBossWave ? new Color(1f, 0.55f, 0.75f) : new Color(1, 1, 1, 0.6f));
            }
            if (Button(new Rect(W - 340, H - 70, 320, 52), next + "  >", D.Placing == PlaceMode.None, new Color(0.7f, 0.25f, 0.35f)))
                D.LeaveForge();
        }

        // ================================================================== pause / end

        void DrawPause()
        {
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.65f));
            Shadowed(new Rect(0, 130, W, 70), "PAUSED", huge, Color.white);
            if (Button(new Rect(W / 2 - 130, 225, 260, 46), "Resume")) D.Resume();
            if (Button(new Rect(W / 2 - 130, 279, 260, 46), MetaProgress.Data.SfxOn ? "Sound: ON" : "Sound: OFF")) ToggleSound();
            if (Button(new Rect(W / 2 - 130, 333, 260, 46), MetaProgress.Data.MusicOn ? "Music: ON" : "Music: OFF")) ToggleMusic();
            if (Button(new Rect(W / 2 - 130, 387, 260, 46), MetaProgress.Data.ShakeOn ? "Screen shake: ON" : "Screen shake: OFF")) ToggleShake();
            if (Button(new Rect(W / 2 - 130, 441, 260, 46), MetaProgress.Data.RumbleOn ? "Rumble: ON" : "Rumble: OFF")) ToggleRumble();
            if (Button(new Rect(W / 2 - 130, 505, 260, 46), "Quit to Menu", true, new Color(0.55f, 0.2f, 0.25f))) D.QuitToMenu();
        }

        void ToggleMusic()
        {
            MetaProgress.Data.MusicOn = !MetaProgress.Data.MusicOn;
            D.Music.SetEnabled(MetaProgress.Data.MusicOn);
            MetaProgress.Save();
        }

        void ToggleSound()
        {
            MetaProgress.Data.SfxOn = !MetaProgress.Data.SfxOn;
            D.Audio.Enabled = MetaProgress.Data.SfxOn;
            MetaProgress.Save();
        }

        void ToggleShake()
        {
            MetaProgress.Data.ShakeOn = !MetaProgress.Data.ShakeOn;
            D.ApplyFeelSettings();
            MetaProgress.Save();
        }

        void ToggleRumble()
        {
            MetaProgress.Data.RumbleOn = !MetaProgress.Data.RumbleOn;
            D.ApplyFeelSettings();
            MetaProgress.Save();
        }

        void DrawEnd(bool victory)
        {
            var run = D.Run;
            Fill(new Rect(0, 0, W, H), victory ? new Color(0.05f, 0.08f, 0.04f, 0.92f) : new Color(0.15f, 0.02f, 0.05f, 0.9f));
            Shadowed(new Rect(0, 110, W, 80), victory ? "YOU BEAT THE HOUSE!" : "You Died!!", huge, victory ? new Color(1f, 0.9f, 0.4f) : Color.white);
            if (run != null)
            {
                int mins = (int)(run.RunTime / 60f), secs = (int)(run.RunTime % 60f);
                Text(new Rect(0, 220, W, 160),
                    $"{run.Character.Name}\nWave {run.Wave}   Score {run.Score}   Kills {run.Kills}   Level {run.Level}\n" +
                    $"Bosses {run.BossesKilled}   Time {mins}:{secs:00}   Damage taken {Mathf.RoundToInt(run.DamageTaken)}\n\n" +
                    $"+{D.ChipsEarnedLastRun} chips  (total {MetaProgress.Data.Chips})",
                    labelC, Color.white);
            }
            float y = 420;
            if (victory)
            {
                if (Button(new Rect(W / 2 - 150, y, 300, 50), "Continue (Endless)", true, new Color(0.6f, 0.45f, 0.1f))) D.ContinueEndless();
                y += 60;
            }
            if (Button(new Rect(W / 2 - 150, y, 300, 50), "Play Again")) D.StartRun(run != null ? run.Character : null);
            if (Button(new Rect(W / 2 - 150, y + 60, 300, 50), "Main Menu", true, new Color(0.3f, 0.3f, 0.45f))) D.QuitToMenu();
        }

        // ================================================================== main menu

        void DrawMenu()
        {
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0.02f, 0.35f));
            Shadowed(new Rect(0, 50, W, 80), "DICE MANIA", huge, Color.white);
            Text(new Rect(0, 120, W, 30), "SURVIVORS - prototype", labelC, new Color(1, 1, 1, 0.6f));
            Shadowed(new Rect(W - 300, 16, 280, 26), "Chips: " + MetaProgress.Data.Chips, labelR, new Color(1f, 0.82f, 0.25f));
            Text(new Rect(20, 16, 400, 26), $"Best wave {MetaProgress.Data.BestWave}   Best score {MetaProgress.Data.BestScore}", small, new Color(1, 1, 1, 0.6f));

            switch (page)
            {
                case MenuPage.Home: DrawHome(); break;
                case MenuPage.Characters: DrawCharacters(); break;
                case MenuPage.Upgrades: DrawUpgrades(); break;
                case MenuPage.Unlocks: DrawUnlocks(); break;
                case MenuPage.HowTo: DrawHowTo(); break;
            }

            if (CsvTable.Warnings.Count > 0)
                Text(new Rect(20, H - 50, W - 40, 40), $"Data warnings: {CsvTable.Warnings.Count} (see Console). First: {CsvTable.Warnings[0]}", tiny, new Color(1f, 0.6f, 0.3f));
        }

        CharacterDef SelectedCharacter() => D.SelectedCharacter();

        void DrawHome()
        {
            var ch = SelectedCharacter();
            float y = 190, bw = 320, x = W / 2 - bw / 2;
            if (Button(new Rect(x, y, bw, 56), "PLAY", ch != null, new Color(0.7f, 0.25f, 0.35f))) { D.StartRun(ch); }
            Text(new Rect(x, y + 58, bw, 22), ch != null ? "as " + ch.Name : "", labelC, new Color(1, 1, 1, 0.7f));
            y += 90;
            if (Button(new Rect(x, y, bw, 46), "Characters")) page = MenuPage.Characters;
            if (Button(new Rect(x, y + 54, bw, 46), "Upgrades")) page = MenuPage.Upgrades;
            if (Button(new Rect(x, y + 108, bw, 46), "Unlock Faces")) page = MenuPage.Unlocks;
            if (Button(new Rect(x, y + 162, bw, 46), "How to Play")) page = MenuPage.HowTo;
            if (Button(new Rect(x, y + 216, bw / 2 - 4, 46), MetaProgress.Data.SfxOn ? "Sound: ON" : "Sound: OFF", true, new Color(0.25f, 0.27f, 0.4f))) ToggleSound();
            if (Button(new Rect(x + bw / 2 + 4, y + 216, bw / 2 - 4, 46), MetaProgress.Data.ShakeOn ? "Shake: ON" : "Shake: OFF", true, new Color(0.25f, 0.27f, 0.4f))) ToggleShake();
            Text(new Rect(x - 100, y + 268, bw + 200, 22), "ENTER / gamepad A to play", labelC, new Color(1, 1, 1, 0.35f));
        }

        bool Back()
        {
            if (Button(new Rect(20, H - 110, 160, 44), "< Back", true, new Color(0.25f, 0.27f, 0.4f))) { page = MenuPage.Home; return true; }
            return false;
        }

        void DrawCharacters()
        {
            Text(new Rect(0, 160, W, 30), "CHOOSE YOUR ROLLER", big);
            var list = GameDatabase.Characters;
            float cw = 270, ch = 300, gap = 16;
            float x0 = W / 2 - (list.Count * cw + (list.Count - 1) * gap) / 2;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                var r = new Rect(x0 + i * (cw + gap), 210, cw, ch);
                bool unlocked = MetaProgress.IsCharacterUnlocked(c);
                bool selected = MetaProgress.Data.SelectedCharacter == c.Id;
                Fill(r, selected ? PanelLight : Panel);
                Fill(new Rect(r.x, r.y, r.width, 6), c.Color);
                Text(new Rect(r.x + 12, r.y + 14, r.width - 24, 30), c.Name, new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold });
                Text(new Rect(r.x + 12, r.y + 46, r.width - 24, 60), c.Description, small, new Color(1, 1, 1, 0.8f));
                var sw = GameDatabase.Weapon(c.StartWeapon);
                Text(new Rect(r.x + 12, r.y + 110, r.width - 24, 120),
                    $"HP {c.MaxHP}   Speed {c.MoveSpeed}\nStart: {(sw != null ? sw.Name : c.StartWeapon)}\nRolls/wave {c.StartRolls}   Dice {c.Dice.Count}\n" +
                    string.Join("\n", c.Dice.ConvertAll(d => string.Join(" ", d))), tiny, new Color(0.8f, 0.9f, 1f));
                if (unlocked)
                {
                    if (Button(new Rect(r.x + 12, r.yMax - 52, r.width - 24, 40), selected ? "Selected" : "Select", !selected))
                    { MetaProgress.Data.SelectedCharacter = c.Id; MetaProgress.Save(); }
                }
                else if (Button(new Rect(r.x + 12, r.yMax - 52, r.width - 24, 40), $"Unlock {c.UnlockCost} chips", MetaProgress.Data.Chips >= c.UnlockCost, new Color(0.6f, 0.45f, 0.1f)))
                {
                    if (MetaProgress.TryUnlockCharacter(c)) D.Audio.Play(SfxPlayer.Id.Buy);
                }
            }
            Back();
        }

        void DrawUpgrades()
        {
            Text(new Rect(0, 160, W, 30), "PERMANENT UPGRADES", big);
            var list = GameDatabase.Meta;
            float cw = 360, chh = 66;
            int cols = 3;
            float x0 = W / 2 - (cols * cw + (cols - 1) * 14) / 2;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                int lv = MetaProgress.MetaLevel(m.Id);
                var r = new Rect(x0 + (i % cols) * (cw + 14), 210 + (i / cols) * (chh + 10), cw, chh);
                Fill(r, Panel);
                Text(new Rect(r.x + 10, r.y + 6, 220, 24), $"{m.Name}  {lv}/{m.MaxLevel}", new GUIStyle(label) { fontStyle = FontStyle.Bold });
                Text(new Rect(r.x + 10, r.y + 32, 220, 30), m.Description, tiny, new Color(1, 1, 1, 0.7f));
                if (lv < m.MaxLevel)
                {
                    int cost = m.CostAt(lv);
                    if (Button(new Rect(r.xMax - 120, r.y + 14, 110, 38), cost + " chips", MetaProgress.Data.Chips >= cost, new Color(0.25f, 0.55f, 0.3f)))
                        if (MetaProgress.TryBuyMeta(m)) D.Audio.Play(SfxPlayer.Id.Buy);
                }
                else Text(new Rect(r.xMax - 120, r.y + 14, 110, 38), "MAX", labelC, new Color(1f, 0.85f, 0.3f));
            }
            Back();
            if (Button(new Rect(W - 230, H - 110, 210, 44), "Reset save", true, new Color(0.45f, 0.15f, 0.15f)) && Event.current.shift)
                MetaProgress.ResetAll();
            Text(new Rect(W - 430, H - 64, 410, 20), "(hold SHIFT and click to reset all progress)", tiny, new Color(1, 1, 1, 0.4f));
        }

        void DrawUnlocks()
        {
            Text(new Rect(0, 160, W, 30), "UNLOCK FACES (adds them to the Forge pool)", big);
            var list = GameDatabase.Faces;
            float cw = 380, chh = 70;
            int cols = 3;
            float x0 = W / 2 - (cols * cw + (cols - 1) * 12) / 2;
            int idx = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                var r = new Rect(x0 + (idx % cols) * (cw + 12), 210 + (idx / cols) * (chh + 10), cw, chh);
                idx++;
                Fill(r, Panel);
                Fill(new Rect(r.x, r.y, 8, r.height), f.Color);
                Text(new Rect(r.x + 16, r.y + 4, 230, 24), f.Name, new GUIStyle(label) { fontStyle = FontStyle.Bold }, f.Color);
                Text(new Rect(r.x + 16, r.y + 26, 240, 44), f.Describe(1), tiny, new Color(1, 1, 1, 0.75f));
                if (MetaProgress.IsFaceUnlocked(f)) Text(new Rect(r.xMax - 110, r.y + 16, 100, 38), "Unlocked", labelC, new Color(0.5f, 1f, 0.6f));
                else if (Button(new Rect(r.xMax - 120, r.y + 16, 110, 38), f.UnlockCost + " chips", MetaProgress.Data.Chips >= f.UnlockCost, new Color(0.6f, 0.45f, 0.1f)))
                    if (MetaProgress.TryUnlockFace(f)) D.Audio.Play(SfxPlayer.Id.Buy);
            }
            Back();
        }

        void DrawHowTo()
        {
            Text(new Rect(0, 160, W, 30), "HOW TO PLAY", big);
            string t =
                "<b>Move</b>  WASD / arrows.  Your weapons fire automatically at nearby enemies.\n" +
                "<b>Roll</b>  SPACE rolls all your dice. Each wave gives you a stack of rolls (they slowly recharge).\n" +
                "  The rolled face fires instantly:  Bolt (homing missiles), Copy, Mend (heal ring), Twin (clone),\n" +
                "  Devour (dome that eats enemies AND bullets, costs HP), Reroll (+rolls, slow time) ... and more to unlock.\n" +
                "<b>Copy</b>  Rolling Copy gives a charge. HOLD left mouse to slow time and target an enemy, RELEASE to fire\n" +
                "  a slow-motion shot. It consumes the enemy and you BECOME it for 15s - its attacks, speed and body.\n" +
                "  Enemies of that type stop shooting at you while you're disguised. Upgrade Copy to copy elites and bosses.\n" +
                "<b>Combos</b>  Two dice on the same face = DOUBLES (x1.75). Three = JACKPOT. Two pip-1s = SNAKE EYES (+2 levels, costs HP).\n" +
                "  Rolling 3 rising pips in a row (e.g. 2-3-4) = STRAIGHT (+1 roll).\n" +
                "<b>Level up</b>  Collect gems. Pick 1 of 3 cards (spend a roll to reroll them). Max a weapon + own its passive = EVOLUTION.\n" +
                "<b>Dice Forge</b>  Between waves: upgrade faces, buy new faces and place them, load slots, buy relics or a new die.\n" +
                "<b>Bosses</b>  Waves 5, 10 and 15. Beat The House on wave 15 to win, then keep going in Endless.\n" +
                "<b>Chips</b>  Earned every run - spend them on permanent upgrades, characters and new faces.\n\n" +
                "<i>Designers: every number lives in Assets/_Game/Resources/Data/*.csv (press F5 in the Editor to hot-reload).</i>";
            Text(new Rect(80, 200, W - 160, 420), t, label);
            if (Back()) { MetaProgress.Data.SeenTutorial = true; MetaProgress.Save(); }
        }
    }
}
