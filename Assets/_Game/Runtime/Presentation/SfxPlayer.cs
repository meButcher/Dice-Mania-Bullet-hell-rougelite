using System;
using UnityEngine;

namespace DiceMania.Presentation
{
    /// <summary>
    /// Tiny procedural sound bank (no audio files needed). Follows "crack, whiz, thud, ping — few samples, nudge the pitch":
    /// every sound is a short layered synth, and every play gets a random pitch so repeats never sound identical.
    /// Replace with real clips during the audio pass.
    /// </summary>
    public class SfxPlayer : MonoBehaviour
    {
        public enum Id
        {
            Shoot, Hit, Kill, Hurt, Pickup, Explode, EnemyShoot, Copy, Roll, LevelUp, Buy, Click, WaveClear, Boss, Transform, Deny,
            Shell, Crit, Heavy, Shatter, Chest, BigKill, Swing, Ready,
        }

        AudioClip[] clips;
        float[] lastPlayed;
        float[] pitchJitter;
        AudioSource[] sources;
        int next;
        public bool Enabled = true;
        public float Volume = 0.35f;

        const int Rate = 22050;

        void Awake()
        {
            int n = Enum.GetValues(typeof(Id)).Length;
            clips = new AudioClip[n];
            lastPlayed = new float[n];
            pitchJitter = new float[n];
            for (int i = 0; i < n; i++) pitchJitter[i] = 0.08f;
            sources = new AudioSource[14];
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = gameObject.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].spatialBlend = 0f;
            }
            try
            {
                // "crack": noise snap over a falling square blip.
                clips[(int)Id.Shoot] = Make("shoot", 0.08f, (t, r) => Noise(r) * Env(t, 0.004f, 30f) * 0.5f + Square(Sweep(t, 0.08f, 1100f, 420f)) * Env(t, 0.002f, 40f) * 0.35f);
                // "thud": low sine knock + click.
                clips[(int)Id.Hit] = Make("hit", 0.09f, (t, r) => Sine(Sweep(t, 0.09f, 190f, 70f)) * Env(t, 0.002f, 35f) * 0.8f + Noise(r) * Env(t, 0.001f, 120f) * 0.4f);
                // "pop": balloon-ish sweep + noise burst.
                clips[(int)Id.Kill] = Make("kill", 0.13f, (t, r) => Sine(Sweep(t, 0.13f, 760f, 140f)) * Env(t, 0.002f, 22f) * 0.6f + Noise(r) * Env(t, 0.001f, 45f) * 0.45f);
                clips[(int)Id.Hurt] = Make("hurt", 0.22f, (t, r) => Saw(Sweep(t, 0.22f, 240f, 80f)) * Env(t, 0.005f, 9f) * 0.6f + Noise(r) * Env(t, 0.001f, 30f) * 0.3f);
                clips[(int)Id.Pickup] = Make("pickup", 0.08f, (t, r) => Sine(Sweep(t, 0.08f, 1200f, 1800f)) * Env(t, 0.003f, 25f) * 0.4f);
                // "boom": low rumble + noise body.
                clips[(int)Id.Explode] = Make("explode", 0.5f, (t, r) => Lowpass(r, Noise(r)) * Env(t, 0.004f, 6f) * 0.9f + Sine(Sweep(t, 0.5f, 110f, 35f)) * Env(t, 0.003f, 7f) * 0.6f);
                clips[(int)Id.EnemyShoot] = Make("eshoot", 0.06f, (t, r) => Sine(Sweep(t, 0.06f, 560f, 400f)) * Env(t, 0.003f, 40f) * 0.3f);
                clips[(int)Id.Copy] = Make("copy", 0.5f, (t, r) => Saw(Sweep(t, 0.5f, 200f, 1400f)) * Env(t, 0.01f, 4f) * 0.4f);
                clips[(int)Id.Roll] = Make("roll", 0.28f, (t, r) => ((int)(t * 46f) % 2 == 0 ? Noise(r) : 0f) * Env(t, 0.002f, 6f) * 0.5f);
                clips[(int)Id.LevelUp] = Arp("levelup", new[] { 523f, 659f, 784f, 1046f }, 0.07f, 0.4f);
                clips[(int)Id.Buy] = Arp("buy", new[] { 784f, 1046f }, 0.06f, 0.35f);
                clips[(int)Id.Click] = Make("click", 0.03f, (t, r) => Square(1000f * t) * Env(t, 0.001f, 80f) * 0.25f);
                clips[(int)Id.WaveClear] = Arp("wave", new[] { 392f, 523f, 659f, 784f, 1046f }, 0.09f, 0.4f);
                clips[(int)Id.Boss] = Make("boss", 0.9f, (t, r) => Saw(Sweep(t, 0.9f, 90f, 55f)) * Env(t, 0.02f, 2.5f) * 0.6f + Lowpass(r, Noise(r)) * Env(t, 0.01f, 3f) * 0.3f);
                clips[(int)Id.Transform] = Arp("transform", new[] { 300f, 450f, 600f, 900f, 1200f }, 0.05f, 0.45f);
                clips[(int)Id.Deny] = Make("deny", 0.12f, (t, r) => Square(Sweep(t, 0.12f, 170f, 140f)) * Env(t, 0.003f, 12f) * 0.35f);
                // "ping": tiny metallic partials for shells hitting the floor.
                clips[(int)Id.Shell] = Make("shell", 0.14f, (t, r) => (Sine(3150f * t) * 0.6f + Sine(4720f * t) * 0.4f + Sine(6900f * t) * 0.2f) * Env(t, 0.0005f, 30f) * 0.35f);
                // Crit: thud + bright ping on top.
                clips[(int)Id.Crit] = Make("crit", 0.12f, (t, r) => Sine(Sweep(t, 0.12f, 220f, 80f)) * Env(t, 0.002f, 30f) * 0.6f + Sine(2100f * t) * Env(t, 0.001f, 25f) * 0.35f);
                // Heavy cannon: deep boom with a crack on top.
                clips[(int)Id.Heavy] = Make("heavy", 0.35f, (t, r) => Sine(Sweep(t, 0.35f, 120f, 40f)) * Env(t, 0.002f, 9f) * 0.8f + Noise(r) * Env(t, 0.002f, 25f) * 0.5f);
                // Ice shatter: crackly noise grains + glassy bells.
                clips[(int)Id.Shatter] = Make("shatter", 0.4f, (t, r) => (r.NextDouble() < 0.18 ? Noise(r) : 0f) * Env(t, 0.002f, 7f) * 0.7f + (Sine(2600f * t) + Sine(3900f * t) * 0.6f) * Env(t, 0.002f, 9f) * 0.2f);
                clips[(int)Id.Chest] = Arp("chest", new[] { 659f, 880f, 1175f, 1568f }, 0.05f, 0.4f);
                // Big kill: boom + rising sparkle.
                clips[(int)Id.BigKill] = Make("bigkill", 0.45f, (t, r) => Sine(Sweep(t, 0.45f, 90f, 40f)) * Env(t, 0.002f, 6f) * 0.7f + Noise(r) * Env(t, 0.001f, 14f) * 0.4f + Sine(Sweep(t, 0.45f, 900f, 1800f)) * Env(t, 0.05f, 6f) * 0.15f);

                // "whoosh": a noise swell that rises in pitch (melee swing).
                clips[(int)Id.Swing] = Make("swing", 0.18f, (t, r) => Lowpass(r, Noise(r)) * Mathf.Sin(Mathf.Clamp01(t / 0.18f) * Mathf.PI) * 0.9f
                                                                      + Sine(Sweep(t, 0.18f, 300f, 900f)) * Mathf.Sin(Mathf.Clamp01(t / 0.18f) * Mathf.PI) * 0.12f);
                // Ability ready again: a short bright double tick.
                clips[(int)Id.Ready] = Make("ready", 0.12f, (t, r) => (Sine(1568f * t) * Env(t, 0.001f, 40f) + Sine(2093f * t) * Env(Mathf.Max(0f, t - 0.05f), 0.001f, 40f) * (t > 0.05f ? 1f : 0f)) * 0.3f);
                pitchJitter[(int)Id.Ready] = 0f;
                pitchJitter[(int)Id.Swing] = 0.12f;
                pitchJitter[(int)Id.Shell] = 0.22f;
                pitchJitter[(int)Id.Hit] = 0.12f;
                pitchJitter[(int)Id.Kill] = 0.14f;
                pitchJitter[(int)Id.Shoot] = 0.1f;
                pitchJitter[(int)Id.Shatter] = 0.15f;
                pitchJitter[(int)Id.LevelUp] = pitchJitter[(int)Id.WaveClear] = pitchJitter[(int)Id.Chest] = 0f;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DiceMania] Procedural audio unavailable: " + e.Message);
                Enabled = false;
            }
        }

        public void Play(Id id, float minInterval = 0.04f, float vol = 1f, float pitch = 1f)
        {
            if (!Enabled) return;
            int i = (int)id;
            var clip = clips[i];
            if (clip == null) return;
            if (Time.unscaledTime - lastPlayed[i] < minInterval) return;
            lastPlayed[i] = Time.unscaledTime;
            var src = sources[next];
            next = (next + 1) % sources.Length;
            float j = pitchJitter[i];
            src.pitch = pitch * UnityEngine.Random.Range(1f - j, 1f + j);
            src.PlayOneShot(clip, Volume * vol);
        }

        // ------------------------------------------------------------------ synthesis helpers

        delegate float Sample(float t, System.Random rnd);

        static float lpState;

        static AudioClip Make(string name, float dur, Sample fn)
        {
            int n = Mathf.Max(1, (int)(dur * Rate));
            var data = new float[n];
            var rnd = new System.Random(name.GetHashCode());
            lpState = 0f;
            float phaseT = 0f;
            for (int i = 0; i < n; i++)
            {
                phaseT = i / (float)Rate;
                data[i] = Mathf.Clamp(fn(phaseT, rnd), -1f, 1f);
            }
            // Short fade-out to avoid clicks.
            int fade = Mathf.Min(n, Rate / 200);
            for (int i = 0; i < fade; i++) data[n - 1 - i] *= i / (float)fade;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Attack-then-exponential-decay envelope.</summary>
        static float Env(float t, float attack, float decay) => Mathf.Min(1f, t / Mathf.Max(1e-4f, attack)) * Mathf.Exp(-t * decay);

        /// <summary>Phase (in cycles) of a linear frequency sweep f0 -> f1 over dur seconds, evaluated at time t.</summary>
        static float Sweep(float t, float dur, float f0, float f1)
        {
            float k = (f1 - f0) / Mathf.Max(1e-4f, dur);
            return f0 * t + 0.5f * k * t * t;
        }

        static float Sine(float cycles) => Mathf.Sin(cycles * Mathf.PI * 2f);
        static float Square(float cycles) => Mathf.Sin(cycles * Mathf.PI * 2f) >= 0f ? 0.6f : -0.6f;
        static float Saw(float cycles) => (cycles - Mathf.Floor(cycles)) * 2f - 1f;
        static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);
        static float Lowpass(System.Random r, float x) { lpState += (x - lpState) * 0.12f; return lpState * 2.2f; }

        static AudioClip Arp(string name, float[] notes, float noteDur, float vol)
        {
            int per = (int)(noteDur * Rate);
            int n = per * notes.Length + per * 2;
            var data = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                for (int i = 0; i < per * 3 && k * per + i < n; i++)
                {
                    float t = i / (float)(per * 3);
                    float env = Mathf.Min(1f, t * 30f) * (1f - t);
                    data[k * per + i] += Mathf.Sin(i * notes[k] * Mathf.PI * 2f / Rate) * env * vol * 0.6f;
                }
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
