using System;
using UnityEngine;

namespace DiceMania.Presentation
{
    /// <summary>
    /// Procedural looping soundtrack (no audio files): an A-minor progression (Am F C G) with bass, arpeggio and drums.
    /// "Menu" drops the drums and adds a soft pad. The music pitches down during slow motion (Copy shot, Reroll, boss kill).
    /// Swap for real tracks in the audio pass.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        const int Rate = 22050;
        AudioSource src;
        AudioClip menu, battle;
        float targetPitch = 1f, targetVolume = 1f;
        public bool Enabled = true;
        public float Volume = 0.2f;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = Volume;
            try
            {
                menu = Compose("music_menu", false, 96f);
                battle = Compose("music_battle", true, 116f);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DiceMania] Procedural music unavailable: " + e.Message);
                Enabled = false;
            }
        }

        public void PlayMenu() => Switch(menu);
        public void PlayBattle() => Switch(battle);

        void Switch(AudioClip clip)
        {
            if (clip == null) return;
            if (src.clip == clip && (src.isPlaying || !Enabled)) return;
            src.clip = clip;
            if (Enabled) src.Play();
        }

        public void SetEnabled(bool on)
        {
            Enabled = on;
            if (!on) src.Stop();
            else if (src.clip != null && !src.isPlaying) src.Play();
        }

        /// <summary>Slow motion: drop the pitch a little (and duck while paused).</summary>
        public void SetMood(bool slow, bool ducked)
        {
            targetPitch = slow ? 0.84f : 1f;
            targetVolume = ducked ? 0.45f : 1f;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            src.pitch = Mathf.MoveTowards(src.pitch, targetPitch, dt * 1.2f);
            src.volume = Mathf.MoveTowards(src.volume, Volume * targetVolume, dt * 0.8f);
        }

        // ------------------------------------------------------------------ composition

        static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        static AudioClip Compose(string name, bool drums, float bpm)
        {
            float beat = 60f / bpm, step = beat / 4f;
            const int bars = 8;
            int steps = bars * 16;
            int n = (int)(steps * step * Rate);
            var data = new float[n];
            int[][] chords = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 }, new[] { 55, 59, 62 } }; // Am F C G
            int[] arp = { 0, 1, 2, 1, 0, 2, 1, 2 };
            var rnd = new System.Random(5);

            for (int s = 0; s < steps; s++)
            {
                float t0 = s * step;
                var ch = chords[(s / 32) % 4];
                // Bass: driving 8ths in battle, half notes in the menu.
                if (drums ? s % 2 == 0 : s % 8 == 0)
                    Note(data, t0, drums ? step * 1.7f : beat * 1.9f, Midi(ch[0] - 12), 0.26f, 1, rnd);
                // Arpeggio (16ths in battle, 8ths in the menu).
                if (drums || s % 2 == 0)
                {
                    int k = arp[(s / (drums ? 1 : 2)) % arp.Length];
                    int note = ch[k] + 12 + ((s / 16) % 2 == 1 && k == 2 ? 12 : 0);
                    Note(data, t0, step * (drums ? 0.9f : 1.8f), Midi(note), drums ? 0.075f : 0.06f, 0, rnd);
                }
                if (!drums && s % 32 == 0)
                    foreach (var c in ch) Pad(data, t0, beat * 8f, Midi(c), 0.05f);
                if (drums)
                {
                    int b = s % 16;
                    if (b == 0 || b == 8 || b == 10) Kick(data, t0, b == 10 ? 0.25f : 0.45f);
                    if (b == 4 || b == 12) Snare(data, t0, 0.22f, rnd);
                    if (s % 2 == 0) Hat(data, t0, b % 4 == 2 ? 0.07f : 0.04f, rnd);
                }
            }

            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            if (peak > 0.9f) for (int i = 0; i < n; i++) data[i] *= 0.9f / peak;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>wave 0 = soft square-ish lead, 1 = warm saw bass (one-pole low-passed).</summary>
        static void Note(float[] d, float t0, float dur, float freq, float vol, int wave, System.Random rnd)
        {
            int start = (int)(t0 * Rate), len = (int)(dur * Rate);
            float lp = 0f, phase = (float)rnd.NextDouble();
            for (int i = 0; i < len; i++)
            {
                int idx = (start + i) % d.Length;
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t * (wave == 1 ? 5f : 9f));
                phase += freq / Rate;
                float p = phase - Mathf.Floor(phase);
                float s;
                if (wave == 1) { float saw = p * 2f - 1f; lp += (saw - lp) * 0.08f; s = lp * 1.6f; }
                else s = (p < 0.5f ? 0.6f : -0.6f) * 0.5f + Mathf.Sin(p * Mathf.PI * 2f) * 0.5f;
                d[idx] += s * env * vol;
            }
        }

        static void Pad(float[] d, float t0, float dur, float freq, float vol)
        {
            int start = (int)(t0 * Rate), len = (int)(dur * Rate);
            for (int i = 0; i < len; i++)
            {
                int idx = (start + i) % d.Length;
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t / 0.8f) * Mathf.Min(1f, (dur - t) / 0.8f);
                float s = Mathf.Sin(t * freq * Mathf.PI * 2f) + Mathf.Sin(t * freq * 1.003f * Mathf.PI * 2f) * 0.6f;
                d[idx] += s * env * vol;
            }
        }

        static void Kick(float[] d, float t0, float vol)
        {
            int start = (int)(t0 * Rate), len = (int)(0.18f * Rate);
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                phase += Mathf.Lerp(130f, 42f, Mathf.Min(1f, t / 0.12f)) / Rate;
                d[(start + i) % d.Length] += Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t * 18f) * vol;
            }
        }

        static void Snare(float[] d, float t0, float vol, System.Random rnd)
        {
            int start = (int)(t0 * Rate), len = (int)(0.16f * Rate);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float s = (float)(rnd.NextDouble() * 2 - 1) * 0.8f + Mathf.Sin(t * 190f * Mathf.PI * 2f) * 0.4f;
                d[(start + i) % d.Length] += s * Mathf.Exp(-t * 22f) * vol;
            }
        }

        static void Hat(float[] d, float t0, float vol, System.Random rnd)
        {
            int start = (int)(t0 * Rate), len = (int)(0.04f * Rate);
            float prev = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rnd.NextDouble() * 2 - 1);
                float hp = x - prev; prev = x; // crude high-pass
                d[(start + i) % d.Length] += hp * Mathf.Exp(-t * 70f) * vol;
            }
        }
    }
}
