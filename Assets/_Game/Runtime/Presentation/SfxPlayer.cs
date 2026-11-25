using System;
using UnityEngine;

namespace DiceMania.Presentation
{
    /// <summary>Tiny procedural sound bank (no audio files needed). Replace with real clips during the visual/audio pass.</summary>
    public class SfxPlayer : MonoBehaviour
    {
        public enum Id { Shoot, Hit, Kill, Hurt, Pickup, Explode, EnemyShoot, Copy, Roll, LevelUp, Buy, Click, WaveClear, Boss, Transform, Deny }

        AudioClip[] clips;
        float[] lastPlayed;
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
            sources = new AudioSource[10];
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = gameObject.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].spatialBlend = 0f;
            }
            try
            {
                clips[(int)Id.Shoot] = Tone("shoot", 0.06f, 880, 660, 0.25f, Wave.Square);
                clips[(int)Id.Hit] = Tone("hit", 0.05f, 300, 180, 0.3f, Wave.Noise);
                clips[(int)Id.Kill] = Tone("kill", 0.12f, 520, 120, 0.35f, Wave.Square);
                clips[(int)Id.Hurt] = Tone("hurt", 0.2f, 220, 90, 0.6f, Wave.Saw);
                clips[(int)Id.Pickup] = Tone("pickup", 0.07f, 1200, 1700, 0.25f, Wave.Sine);
                clips[(int)Id.Explode] = Tone("explode", 0.3f, 160, 40, 0.6f, Wave.Noise);
                clips[(int)Id.EnemyShoot] = Tone("eshoot", 0.05f, 500, 420, 0.12f, Wave.Sine);
                clips[(int)Id.Copy] = Tone("copy", 0.5f, 200, 1400, 0.5f, Wave.Saw);
                clips[(int)Id.Roll] = Tone("roll", 0.25f, 400, 600, 0.35f, Wave.Rattle);
                clips[(int)Id.LevelUp] = Arp("levelup", new[] { 523f, 659f, 784f, 1046f }, 0.07f, 0.4f);
                clips[(int)Id.Buy] = Arp("buy", new[] { 784f, 1046f }, 0.06f, 0.35f);
                clips[(int)Id.Click] = Tone("click", 0.03f, 1000, 900, 0.2f, Wave.Square);
                clips[(int)Id.WaveClear] = Arp("wave", new[] { 392f, 523f, 659f, 784f, 1046f }, 0.09f, 0.4f);
                clips[(int)Id.Boss] = Tone("boss", 0.8f, 90, 60, 0.6f, Wave.Saw);
                clips[(int)Id.Transform] = Arp("transform", new[] { 300f, 450f, 600f, 900f, 1200f }, 0.05f, 0.45f);
                clips[(int)Id.Deny] = Tone("deny", 0.12f, 160, 140, 0.4f, Wave.Square);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DiceMania] Procedural audio unavailable: " + e.Message);
                Enabled = false;
            }
        }

        public void Play(Id id, float minInterval = 0.04f, float vol = 1f)
        {
            if (!Enabled) return;
            int i = (int)id;
            var clip = clips[i];
            if (clip == null) return;
            if (Time.unscaledTime - lastPlayed[i] < minInterval) return;
            lastPlayed[i] = Time.unscaledTime;
            var src = sources[next];
            next = (next + 1) % sources.Length;
            src.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            src.PlayOneShot(clip, Volume * vol);
        }

        enum Wave { Sine, Square, Saw, Noise, Rattle }

        static AudioClip Tone(string name, float dur, float f0, float f1, float vol, Wave w)
        {
            int n = Mathf.Max(1, (int)(dur * Rate));
            var data = new float[n];
            float phase = 0f;
            var rnd = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t);
                phase += f / Rate;
                float s;
                switch (w)
                {
                    case Wave.Square: s = Mathf.Sign(Mathf.Sin(phase * Mathf.PI * 2f)) * 0.6f; break;
                    case Wave.Saw: s = (phase % 1f) * 2f - 1f; break;
                    case Wave.Noise: s = (float)(rnd.NextDouble() * 2 - 1); break;
                    case Wave.Rattle: s = ((int)(t * 14) % 2 == 0 ? (float)(rnd.NextDouble() * 2 - 1) : 0f) * 0.8f; break;
                    default: s = Mathf.Sin(phase * Mathf.PI * 2f); break;
                }
                float env = Mathf.Min(1f, t * 40f) * (1f - t) * (1f - t);
                data[i] = s * env * vol;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

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
