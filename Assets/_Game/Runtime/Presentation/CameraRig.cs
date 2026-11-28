using UnityEngine;

namespace DiceMania.Presentation
{
    /// <summary>
    /// Tilted top-down follow camera with game-feel layers (all on unscaled time, so they play through hit-stop):
    /// - trauma shake (big hits): Perlin position + roll, trauma squared
    /// - vibration (gunshots): short high-frequency jitter
    /// - kick (recoil): spring-damped offset opposite to the shot direction
    /// - punch (big moments): quick zoom in that snaps back
    /// plus the zoom used by the Copy slow-mo shot.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public Vector3 Target;
        public Vector3 Offset = new Vector3(0f, 21f, -12.5f);
        public float Zoom = 1f;
        public float ZoomTarget = 1f;
        /// <summary>Global multiplier for shake / vibration / kick / punch (0 = off, e.g. the "Screen shake" option).</summary>
        public float Intensity = 1f;

        float trauma, vibe, punch;
        Vector3 kickOffset, kickVel;
        Vector3 smoothed;

        public static CameraRig Create()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.025f, 0.06f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            var rig = cam.gameObject.GetComponent<CameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Cam = cam;
            return rig;
        }

        /// <summary>Big slow shake (0..1 trauma, squared).</summary>
        public void Shake(float amount) => trauma = Mathf.Clamp01(Mathf.Max(trauma, amount));

        /// <summary>Short high-frequency vibration (gunshots).</summary>
        public void Vibrate(float amount) => vibe = Mathf.Clamp(Mathf.Max(vibe, amount), 0f, 1f);

        /// <summary>Recoil kick: the camera jolts opposite to <paramref name="shotDirXZ"/> and springs back.</summary>
        public void Kick(Vector2 shotDirXZ, float amount)
        {
            kickVel += new Vector3(-shotDirXZ.x, 0f, -shotDirXZ.y) * amount * 14f;
        }

        /// <summary>Punch-in: zoom in hard, snap back.</summary>
        public void Punch(float amount) => punch = Mathf.Clamp(Mathf.Max(punch, amount), 0f, 0.5f);

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float k = Mathf.Max(0f, Intensity);
            Zoom = Mathf.Lerp(Zoom, ZoomTarget, 1f - Mathf.Exp(-6f * dt));
            smoothed = Vector3.Lerp(smoothed, Target, 1f - Mathf.Exp(-10f * dt));

            // Kick spring (stiff, slightly under-damped so it settles with one small overshoot).
            const float stiffness = 260f, damping = 22f;
            kickVel += (-stiffness * kickOffset - damping * kickVel) * dt;
            kickOffset += kickVel * dt;
            if (kickOffset.sqrMagnitude > 4f) kickOffset = kickOffset.normalized * 2f;

            punch = Mathf.MoveTowards(punch, 0f, dt * 2.2f);
            float zoom = Zoom * (1f - punch * k);
            Vector3 pos = smoothed + Offset * zoom;

            trauma = Mathf.Max(0f, trauma - dt * 1.6f);
            vibe *= Mathf.Exp(-16f * dt);
            if (vibe < 0.002f) vibe = 0f;
            float s = trauma * trauma;
            float t = Time.unscaledTime;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(t * 25f, 0.1f) - 0.5f, 0f, Mathf.PerlinNoise(0.2f, t * 25f) - 0.5f) * 2.4f * s;
            shake += new Vector3(Mathf.Sin(t * 91f), 0f, Mathf.Cos(t * 77f)) * 0.8f * vibe;
            shake *= k;
            float roll = (Mathf.PerlinNoise(t * 18f, 7.7f) - 0.5f) * 5f * s * k;

            transform.position = pos + shake + kickOffset * k;
            transform.LookAt(smoothed + shake * 0.5f + kickOffset * (0.6f * k));
            if (roll != 0f) transform.Rotate(0f, 0f, roll, Space.Self);
        }

        public void Snap(Vector3 target)
        {
            Target = smoothed = target;
            kickOffset = kickVel = Vector3.zero;
            trauma = vibe = punch = 0f;
        }

        /// <summary>Mouse position projected onto the ground plane (y = 0).</summary>
        public Vector3 MouseWorld(Vector2 screen)
        {
            if (Cam == null) return Vector3.zero;
            var ray = Cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return Target;
            float t = -ray.origin.y / ray.direction.y;
            return ray.origin + ray.direction * t;
        }
    }
}
