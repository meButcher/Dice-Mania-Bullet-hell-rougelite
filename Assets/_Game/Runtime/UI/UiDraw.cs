using UnityEngine;

namespace DiceMania.Game
{
    /// <summary>
    /// GL shapes for the IMGUI HUD (cooldown ring, recharge arc, joystick): drawn during Repaint, in GUI coordinates
    /// (1280x720 virtual canvas, y down). Angles in degrees: 0 = right, 90 = up.
    /// </summary>
    public static class UiDraw
    {
        static Material mat;

        static bool Ready()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return false;
            if (mat == null)
            {
                var shader = Resources.Load<Shader>("Shaders/DMUI") ?? Shader.Find("DiceMania/UI");
                if (shader == null) return false;
                mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "DM_UI" };
            }
            return true;
        }

        /// <summary>Filled ring segment from <paramref name="a0"/> to <paramref name="a1"/> degrees.</summary>
        public static void Arc(Vector2 center, float r0, float r1, float a0, float a1, Color color, float guiScale)
        {
            if (Mathf.Abs(a1 - a0) < 0.01f || !Ready()) return;
            mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Screen.width, 0, Screen.height);
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            int seg = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 5f));
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, i / (float)seg) * Mathf.Deg2Rad;
                float t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg) * Mathf.Deg2Rad;
                Vector2 p00 = P(center, r0, t0, guiScale), p10 = P(center, r1, t0, guiScale);
                Vector2 p11 = P(center, r1, t1, guiScale), p01 = P(center, r0, t1, guiScale);
                V(p00); V(p10); V(p11);
                V(p00); V(p11); V(p01);
            }
            GL.End();
            GL.PopMatrix();
        }

        public static void Disc(Vector2 center, float radius, Color color, float guiScale) => Arc(center, 0f, radius, 0f, 360f, color, guiScale);

        static Vector2 P(Vector2 c, float r, float a, float s) =>
            new Vector2((c.x + Mathf.Cos(a) * r) * s, Screen.height - (c.y - Mathf.Sin(a) * r) * s);

        static void V(Vector2 p) => GL.Vertex3(p.x, p.y, 0f);
    }
}
