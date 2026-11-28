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

        GUIStyle label, labelC, labelR, big, huge, small, button, buttonDark, card, tiny;
        GUIStyle tinyC, tinyCBold, smallR, smallBoldC, timerStyle, scoreStyle, abilityStyle, cooldownStyle, endTitle;
        Texture2D white, vignette, burst, glass, ringTex, disc;
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
            button.normal.background = button.hover.background = button.active.background = null; // flat, like the original
            button.normal.textColor = button.hover.textColor = button.active.textColor = Color.white;
            card = new GUIStyle(label) { fontSize = 15, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
            // The original's flat slate-blue buttons with dark text.
            buttonDark = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            buttonDark.normal.background = buttonDark.hover.background = buttonDark.active.background = null;
            buttonDark.normal.textColor = buttonDark.hover.textColor = buttonDark.active.textColor = new Color(0.06f, 0.07f, 0.13f);
            tinyC = new GUIStyle(tiny) { alignment = TextAnchor.MiddleCenter };
            tinyCBold = new GUIStyle(tinyC) { fontSize = 14, fontStyle = FontStyle.Bold };
            smallR = new GUIStyle(small) { alignment = TextAnchor.MiddleRight, fontSize = 15 };
            smallBoldC = new GUIStyle(labelC) { fontSize = 17, fontStyle = FontStyle.Bold };
            timerStyle = new GUIStyle(labelC) { fontSize = 24, fontStyle = FontStyle.Bold };
            scoreStyle = new GUIStyle(labelR) { fontSize = 25, fontStyle = FontStyle.Bold };
            abilityStyle = new GUIStyle(labelR) { fontSize = 24, fontStyle = FontStyle.Bold };
            cooldownStyle = new GUIStyle(labelC) { fontSize = 28, fontStyle = FontStyle.Bold };
            endTitle = new GUIStyle(huge) { fontSize = 72 };
            vignette = MakeVignette(128);
            burst = MakeBurstLines(256);
            glass = MakeGlass(256);
            ringTex = MakeRing(64, 0.76f, 0.97f, 0.4f);
            disc = MakeRing(64, 0f, 0.97f, 1f);
            stylesReady = true;
        }

        /// <summary>The original's glassy circle: faint tinted body, bright rim, soft highlight at the top left.</summary>
        static Texture2D MakeGlass(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "DM_Glass" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f; // dy > 0 = top
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float aa = Mathf.Clamp01((1f - r) * size * 0.5f);
                if (aa <= 0f) { px[y * size + x] = new Color32(0, 0, 0, 0); continue; }
                float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.88f, 0.985f, r)) * (1f - Mathf.InverseLerp(0.985f, 1f, r) * 0.3f);
                float hx = dx + 0.32f, hy = dy - 0.42f;
                float highlight = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) * 1.7f) * 0.2f;
                float body = 0.1f + 0.08f * Mathf.Clamp01(0.5f + dy * 0.5f);
                float a = Mathf.Clamp01(body + rim * 0.55f + highlight) * aa;
                Color c = Color.Lerp(new Color(0.62f, 0.68f, 0.85f), Color.white, Mathf.Clamp01(rim + highlight * 2f));
                px[y * size + x] = new Color(c.r, c.g, c.b, a);
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>White ring (or filled disc when inner = 0) with a darker translucent middle, anti-aliased.</summary>
        static Texture2D MakeRing(int size, float inner, float outer, float middleAlpha)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "DM_Ring" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float px1 = 2f / size;
                float band = Mathf.Clamp01((r - inner) / px1 + 0.5f) * Mathf.Clamp01((outer - r) / px1 + 0.5f);
                float mid = r < inner ? middleAlpha * 0.35f : 0f;
                Color c = band > 0f ? Color.white : new Color(0f, 0f, 0f, 1f);
                px[y * size + x] = new Color(c.r, c.g, c.b, Mathf.Max(band, mid));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
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

        static readonly Color Slate = new Color(0.37f, 0.44f, 0.7f, 1f);

        /// <summary>Untinted buttons use the original game's look (flat slate blue, dark text); tinted ones keep white text.</summary>
        bool Button(Rect r, string text, bool enabled = true, Color? tint = null)
        {
            var old = GUI.enabled;
            GUI.enabled = enabled;
            Fill(r, enabled ? (tint ?? Slate) : new Color(0.25f, 0.25f, 0.3f));
            if (r.Contains(Event.current.mousePosition) && enabled) Fill(r, new Color(1, 1, 1, 0.14f));
            bool clicked = GUI.Button(r, text, tint.HasValue || !enabled ? button : buttonDark);
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
                // Like the original, the HUD (empty HP bar, wave, score, rolls, die) stays visible behind the end screen.
                case GameState.GameOver: DrawHud(); DrawEnd(false); break;
                case GameState.Victory: DrawHud(); DrawEnd(true); break;
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

        // ================================================================== HUD (laid out like the original Dice Mania)

        void DrawHud()
        {
            var run = D.Run;
            if (run == null || D.Ecs == null || D.Ecs.Player == Unity.Entities.Entity.Null) return;
            var hp = D.Ecs.PlayerHealth;

            // ---- Top left: the white "+" and white HP bar of the original; a red ghost drains behind it when hit ----
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
            DrawCross(new Rect(16 + off.x, 13 + off.y, 30, 30), Color.Lerp(Color.white, new Color(1f, 0.35f, 0.35f), D.HurtFlash));
            var hpRect = new Rect(58 + off.x, 21 + off.y, 370, 15);
            Fill(hpRect, new Color(1f, 1f, 1f, 0.13f));
            Fill(new Rect(hpRect.x, hpRect.y, hpRect.width * hpGhost, hpRect.height), new Color(1f, 0.32f, 0.32f, 0.9f));
            Color hpCol = hpFrac < 0.3f ? Color.Lerp(Color.white, new Color(1f, 0.45f, 0.45f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f)) : Color.white;
            Fill(new Rect(hpRect.x, hpRect.y, hpRect.width * hpFrac, hpRect.height), hpCol);
            Shadowed(new Rect(hpRect.xMax + 8, 16 + off.y, 120, 24), $"{Mathf.CeilToInt(hp.Value)}/{Mathf.CeilToInt(hp.Max)}", small, new Color(1, 1, 1, 0.75f));

            // XP + level, weapons, passives (roguelite extras, kept small under the HP bar).
            Bar(new Rect(58, 40, 370, 4), run.Xp / Mathf.Max(1f, run.XpToNext), new Color(0.35f, 0.65f, 1f), new Color(1, 1, 1, 0.08f));
            Shadowed(new Rect(58, 46, 60, 20), "LV " + run.Level, tiny, new Color(0.6f, 0.8f, 1f));
            float x = 100;
            foreach (var w in run.Weapons)
            {
                Fill(new Rect(x, 47, 22, 22), w.Def.Color * 0.85f);
                Text(new Rect(x, 47, 22, 22), w.Def.IsEvolution ? "*" : w.Level.ToString(), tinyC, Color.black);
                x += 25;
            }
            x += 8;
            foreach (var p in run.Passives)
            {
                Fill(new Rect(x, 50, 16, 16), new Color(0.5f, 0.7f, 1f, 0.7f));
                Text(new Rect(x, 50, 16, 16), p.Level.ToString(), tinyC, Color.black);
                x += 19;
            }
            if (run.Relics.Count > 0) Shadowed(new Rect(100, 70, 300, 20), "Relics: " + run.Relics.Count, tiny, new Color(1f, 0.8f, 0.4f));

            // ---- Top centre: "Wave: 01" and the countdown ----
            string waveText = "Wave: " + run.Wave.ToString("00") + (run.Wave > D.FinalWave ? "  (endless)" : "");
            Shadowed(new Rect(0, 12, W, 36), waveText, big, Color.white);
            if (D.BossAlive)
            {
                Shadowed(new Rect(0, 48, W, 24), D.BossName, labelC, new Color(1f, 0.5f, 0.75f));
                Bar(new Rect(W / 2 - 250, 74, 500, 12), D.BossHpFrac, new Color(1f, 0.25f, 0.6f), new Color(0.15f, 0.02f, 0.1f, 0.85f));
            }
            else if (D.State == GameState.WaveClear)
                Shadowed(new Rect(0, 50, W, 30), "Break", timerStyle, Color.white);
            else
                Shadowed(new Rect(0, 50, W, 30), Mathf.CeilToInt(Mathf.Max(0, D.WaveTimeLeft)) + "s", timerStyle, Color.white);

            // ---- Top right: score (gold / kills under it) ----
            Shadowed(new Rect(W - 320, 16, 300, 34), "Score: " + run.Score, scoreStyle, Color.white);
            Shadowed(new Rect(W - 320, 50, 300, 22), "Gold: " + run.Gold, smallR, new Color(1f, 0.82f, 0.25f));
            Shadowed(new Rect(W - 320, 70, 300, 22), "Kills: " + run.Kills, smallR, new Color(1, 1, 1, 0.6f));

            DrawRollsCorner(run);
            DrawDieCorner(run);

            // Status lines above the die: Morph form, buffs.
            float sy = H - 304;
            var copy = D.Ecs.PlayerCopy;
            if (copy.Time > 0f && copy.EnemyDef >= 0 && copy.EnemyDef < GameDatabase.Enemies.Count)
            {
                var def = GameDatabase.Enemies[copy.EnemyDef];
                Shadowed(new Rect(W - 420, sy, 400, 22), "Morphed into: " + def.Name, smallR, def.Color);
                Bar(new Rect(W - 260, sy + 22, 240, 6), copy.Time / Mathf.Max(0.01f, copy.TotalTime), def.Color, new Color(0, 0, 0, 0.6f));
                sy -= 34;
            }
            var buffs = D.Ecs.Get<Buffs>();
            if (buffs.Invuln > 0f) { Shadowed(new Rect(W - 420, sy, 400, 22), $"Aegis {buffs.Invuln:0.0}s", smallR, new Color(1f, 0.85f, 0.4f)); sy -= 22; }
            if (buffs.GoldRush > 0f) Shadowed(new Rect(W - 420, sy, 400, 22), $"Gold Rush {buffs.GoldRush:0.0}s", smallR, new Color(1f, 0.8f, 0f));

            // Morph aim hint at the cursor.
            if (D.Copy == CopyPhase.Aiming)
            {
                // Next to the marked enemy (works for mouse, touch and gamepad aim alike), else at the pointer.
                Vector2 m = new Vector2(GameInput.MousePos.x / scale, H - GameInput.MousePos.y / scale);
                var em = D.Ecs.Em;
                bool target = D.CopyTarget != Unity.Entities.Entity.Null && em.Exists(D.CopyTarget) && em.HasComponent<DiceMania.ECS.Pos>(D.CopyTarget);
                if (target && D.Rig != null && D.Rig.Cam != null)
                {
                    var tp = em.GetComponentData<DiceMania.ECS.Pos>(D.CopyTarget).Value;
                    var sp = D.Rig.Cam.WorldToScreenPoint(new Vector3(tp.x, 0f, tp.y));
                    if (sp.z > 0f) m = new Vector2(sp.x / scale + 14f, (Screen.height - sp.y) / scale - 26f);
                }
                string t = target ? "Release to MORPH: " + D.CopyTargetName : "Move over an enemy...";
                float tw = small.CalcSize(new GUIContent(t)).x + 6f;
                float lx = m.x + 16f + tw > W - 12f ? m.x - 16f - tw : m.x + 16f; // flip to the left near the right edge
                var lr = new Rect(Mathf.Max(8f, lx), Mathf.Clamp(m.y - 10f, 96f, H - 30f), tw, 22f);
                // Never cover the die corner, the ability name above it or the rolls corner: lift the label above them.
                var hud = new[] { new Rect(W - 220f, H - 210f, 220f, 210f), new Rect(W - 445f, H - 256f, 445f, 64f), new Rect(0f, H - 140f, 310f, 140f) };
                foreach (var r in hud) if (lr.Overlaps(r)) lr.y = r.yMin - lr.height - 4f;
                lr.y = Mathf.Max(96f, lr.y);
                Shadowed(lr, t, small, new Color(1f, 0.5f, 0.75f));
            }
            if (D.State == GameState.Playing && run.Wave == 1 && D.WaveElapsed < 14f && !MetaProgress.Data.SeenTutorial)
                Shadowed(new Rect(0, H - 150, W, 24),
                    "WASD move  -  SPACE (or click the die) rolls  -  click / tap uses the rolled ability  -  ESC pause", labelC, new Color(1, 1, 1, 0.85f));
            DrawJoystick();
        }

        void DrawCross(Rect r, Color c)
        {
            float t = r.width * 0.36f;
            Fill(new Rect(r.x + (r.width - t) / 2, r.y, t, r.height), c);
            Fill(new Rect(r.x, r.y + (r.height - t) / 2, r.width, t), c);
        }

        void DrawGlass(Vector2 c, float radius) => GUI.DrawTexture(new Rect(c.x - radius, c.y - radius, radius * 2, radius * 2), glass);

        /// <summary>Bottom left, like the original: a glass quarter-circle with the rolls left.</summary>
        void DrawRollsCorner(RunState run)
        {
            var c = new Vector2(-8, H + 8);
            DrawGlass(c, 132);
            float recharge = GameDatabase.Cfg("RollRechargeSeconds", 25);
            if (recharge > 0 && run.RollsLeft < run.RollsPerWave)
                UiDraw.Arc(c, 125, 132, 90f, 90f - 90f * Mathf.Clamp01(run.RollRechargeTimer / recharge), new Color(0.72f, 0.5f, 1f, 0.9f), scale);
            var baseM = GUI.matrix;
            float pop = 1f + 0.35f * D.RollPunch;
            var pivot = new Vector3(50, H - 52, 0);
            GUI.matrix = baseM * Matrix4x4.TRS(pivot, Quaternion.identity, new Vector3(pop, pop, 1f)) * Matrix4x4.Translate(-pivot);
            Shadowed(new Rect(6, H - 100, 90, 90), run.RollsLeft.ToString(), huge, run.RollsLeft > 0 ? Color.white : new Color(1, 0.45f, 0.45f));
            GUI.matrix = baseM;
            Shadowed(new Rect(126, H - 32, 300, 22), "Dice Rolls remaining.", small, new Color(1, 1, 1, 0.9f));
        }

        /// <summary>
        /// Bottom right, like the original: the 3D die in a glass circle with the six face rings fanned around it.
        /// New: the ring around the die is the ability cooldown; the name above says what you rolled and how to use it.
        /// </summary>
        void DrawDieCorner(RunState run)
        {
            var corner = new Vector2(W + 8, H + 8);
            DrawGlass(corner, 142);
            var die0 = run.Dice.Count > 0 ? run.Dice[0] : null;
            var a = D.Ability;
            bool rolling = D.RollAnimTime > 0f;
            int shownSlot = D.LastRoll != null && D.LastRoll.Rolls.Count > 0 ? D.LastRoll.Rolls[0].SlotIndex : -1;

            if (die0 != null)
            {
                var old = GUI.color;
                for (int s = 0; s < 6; s++)
                {
                    float ang = (171f - s * 14.8f) * Mathf.Deg2Rad;
                    var p = corner + new Vector2(Mathf.Cos(ang), -Mathf.Sin(ang)) * 187f;
                    bool hot = !rolling && s == shownSlot;
                    float sz = hot ? 36f * (1f + 0.3f * D.RollPunch) : 28f;
                    var col = die0.Slots[s].Face.Color;
                    var r = new Rect(p.x - sz / 2, p.y - sz / 2, sz, sz);
                    if (hot) { GUI.color = new Color(col.r, col.g, col.b, 0.95f); GUI.DrawTexture(r, disc); }
                    GUI.color = new Color(col.r, col.g, col.b, hot ? 1f : 0.85f);
                    GUI.DrawTexture(r, ringTex);
                    GUI.color = old;
                    if (hot) Text(new Rect(p.x - 20, p.y - 11, 40, 22), (s + 1).ToString(), tinyCBold, Color.black);
                }
            }

            // The die sits in the corner like the original's tile; extra dice (Gambler, Forge) wait left of the face fan.
            var dieCenter = new Vector2(W - 66, H - 64);
            const float dieSize = 112f;
            Vector2 shake = a != null && a.DenyPulse > 0f ? new Vector2(Mathf.Sin(Time.unscaledTime * 70f) * 5f * a.DenyPulse, 0f) : Vector2.zero;
            var dv = D.DiceCubes;
            if (dv != null && dv.Count > 0 && dv.Texture != null)
            {
                var oldC = GUI.color;
                if (a != null && !a.Ready) GUI.color = new Color(0.5f, 0.5f, 0.56f, 1f); // dimmed while it recharges
                var c0 = dieCenter + shake;
                GUI.DrawTextureWithTexCoords(new Rect(c0.x - dieSize * 0.5f, c0.y - dieSize * 0.5f, dieSize, dieSize), dv.Texture, dv.DieUv(0));
                GUI.color = oldC;
                for (int i = 1; i < dv.Count; i++)
                {
                    const float extra = 92f;
                    var ci = new Vector2(W - 236 - (i - 1) * 84, H - 50);
                    GUI.DrawTextureWithTexCoords(new Rect(ci.x - extra * 0.5f, ci.y - extra * 0.5f, extra, extra), dv.Texture, dv.DieUv(i));
                }
            }

            // Cooldown ring around the die: fills back clockwise in the face colour, pulses when ready.
            UiDraw.Arc(dieCenter + shake, 50f, 57f, 0f, 360f, new Color(0f, 0f, 0f, 0.45f), scale);
            if (a != null)
            {
                var fc = a.Face.Color;
                float pulse = a.ReadyPulse;
                UiDraw.Arc(dieCenter + shake, 50f - pulse * 3f, 57f + pulse * 5f, 90f, 90f - 360f * (1f - a.CooldownFrac),
                    new Color(fc.r, fc.g, fc.b, a.Ready ? 0.95f : 0.75f), scale);
                if (!a.Ready)
                    Shadowed(new Rect(dieCenter.x - 50 + shake.x, dieCenter.y - 18, 100, 36),
                        a.CooldownLeft < 10f ? a.CooldownLeft.ToString("0.0") : Mathf.CeilToInt(a.CooldownLeft).ToString(), cooldownStyle, Color.white);
                string uses = a.UsesLeft > 0 ? $"   x{a.UsesLeft}" : "";
                Shadowed(new Rect(W - 440, H - 248, 420, 30), a.Face.Name.ToUpper() + (a.Level > 1 ? $"  Lv{a.Level}" : "") + uses, abilityStyle, fc);
                string how = a.Face.Id == "copy" ? "hold on an enemy, release to morph"
                    : !a.Ready ? "recharging..."
                    : a.Face.Auto ? "click / tap  (hold to keep using)" : "click / tap to use";
                Shadowed(new Rect(W - 440, H - 220, 420, 22), how, smallR, new Color(1, 1, 1, 0.72f));
            }
            else
            {
                string msg = rolling ? "rolling..." : run.RollsLeft > 0 ? "ROLL!  [SPACE] or click the die" : "no rolls left";
                Shadowed(new Rect(W - 440, H - 236, 420, 26), msg, labelR, new Color(1, 1, 1, 0.85f));
            }
            if (D.LastRoll != null && D.RollShowTime > 0f && !string.IsNullOrEmpty(D.LastRoll.ComboText))
                Shadowed(new Rect(W - 440, H - 276, 420, 26), D.LastRoll.ComboText, labelR, new Color(1f, 0.85f, 0.25f));
        }

        /// <summary>Touch: virtual joystick under the left thumb.</summary>
        void DrawJoystick()
        {
            if (!GameInput.JoyActive) return;
            var o = new Vector2(GameInput.JoyOrigin.x / scale, H - GameInput.JoyOrigin.y / scale);
            var k = new Vector2(GameInput.JoyKnob.x / scale, H - GameInput.JoyKnob.y / scale);
            float radius = GameInput.JoyRadiusFrac * H;
            DrawGlass(o, radius);
            var d = k - o;
            if (d.magnitude > radius) d = d.normalized * radius;
            UiDraw.Disc(o + d, radius * 0.42f, new Color(1f, 1f, 1f, 0.35f), scale);
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
            // Like the original: the world stays visible behind a dark red tint, big "You Died!!", ReStart / Exit.
            var run = D.Run;
            Fill(new Rect(0, 0, W, H), victory ? new Color(0.04f, 0.08f, 0.03f, 0.7f) : new Color(0.16f, 0f, 0.04f, 0.55f));
            Shadowed(new Rect(0, 150, W, 90), victory ? "You Beat The House!!" : "You Died!!", endTitle, victory ? new Color(1f, 0.9f, 0.4f) : Color.white);
            if (run != null)
            {
                int mins = (int)(run.RunTime / 60f), secs = (int)(run.RunTime % 60f);
                Shadowed(new Rect(0, 238, W, 60),
                    $"Wave {run.Wave}   Score {run.Score}   Kills {run.Kills}   Time {mins}:{secs:00}\n+{D.ChipsEarnedLastRun} chips  (total {MetaProgress.Data.Chips})",
                    labelC, new Color(1, 1, 1, 0.85f));
            }
            float y = 318;
            if (victory)
            {
                if (Button(new Rect(W / 2 - 166, y, 332, 66), "Continue (Endless)", true, new Color(0.6f, 0.45f, 0.1f))) D.ContinueEndless();
                y += 84;
            }
            if (Button(new Rect(W / 2 - 166, y, 332, 70), "ReStart")) D.StartRun(run != null ? run.Character : null);
            if (Button(new Rect(W / 2 - 166, y + 92, 332, 70), "Exit")) D.QuitToMenu();
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
                "<b>Move</b>  WASD / arrows (touch: left thumb).  Your weapons fire automatically at nearby enemies.\n" +
                "<b>Roll</b>  SPACE (or click / tap the die) rolls. Each wave gives you a stack of rolls (they slowly recharge).\n" +
                "<b>Use</b>  The rolled face is your ability until you roll again - CLICK / TAP to use it, then wait for its cooldown\n" +
                "  (the ring around the die). 1 Bullet (missile at the cursor, hold to keep firing), 2 Duplicate (clones),\n" +
                "  3 Melee (slash, hold to keep swinging), 4 Heal (ring), 5 Devour (dome that eats enemies AND bullets, costs HP),\n" +
                "  6 Refresh (refill rolls, one use) - or Morph on 6: HOLD on an enemy, RELEASE to fire a slow-motion shot and\n" +
                "  BECOME it for 15s. Enemies of that type stop shooting at you. More faces unlock in the Forge.\n" +
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
