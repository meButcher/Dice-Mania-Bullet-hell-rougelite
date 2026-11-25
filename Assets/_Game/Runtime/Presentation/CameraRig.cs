using UnityEngine;

namespace DiceMania.Presentation
{
    /// <summary>Tilted top-down follow camera with trauma-based screen shake and a zoom used by the Copy slow-mo shot.</summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public Vector3 Target;
        public Vector3 Offset = new Vector3(0f, 21f, -12.5f);
        public float Zoom = 1f;
        public float ZoomTarget = 1f;
        float trauma;
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

        public void Shake(float amount) => trauma = Mathf.Clamp01(Mathf.Max(trauma, amount));

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            Zoom = Mathf.Lerp(Zoom, ZoomTarget, 1f - Mathf.Exp(-6f * dt));
            smoothed = Vector3.Lerp(smoothed, Target, 1f - Mathf.Exp(-10f * dt));
            Vector3 pos = smoothed + Offset * Zoom;
            trauma = Mathf.Max(0f, trauma - dt * 1.6f);
            float s = trauma * trauma;
            float t = Time.unscaledTime * 25f;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(t, 0.1f) - 0.5f, 0f, Mathf.PerlinNoise(0.2f, t) - 0.5f) * 2.2f * s;
            transform.position = pos + shake;
            transform.LookAt(smoothed + shake * 0.5f);
        }

        public void Snap(Vector3 target)
        {
            Target = smoothed = target;
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
