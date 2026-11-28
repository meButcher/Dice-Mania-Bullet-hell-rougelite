using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DiceMania.Presentation
{
    /// <summary>Gamepad rumble pulses (no-op without a gamepad / Input System). Call <see cref="Tick"/> every frame.</summary>
    public static class Rumble
    {
        public static float Strength = 1f;
        static float until, low, high;

        public static void Pulse(float amount, float seconds)
        {
            if (Strength <= 0f || amount <= 0f) return;
            amount = Mathf.Clamp01(amount * Strength);
            // Big hits lean on the heavy low motor, small ones on the buzzy high motor.
            low = Mathf.Max(low, amount * amount);
            high = Mathf.Max(high, amount);
            until = Mathf.Max(until, Time.unscaledTime + seconds);
#if ENABLE_INPUT_SYSTEM
            Gamepad.current?.SetMotorSpeeds(low, high);
#endif
        }

        public static void Tick()
        {
            if (until <= 0f || Time.unscaledTime < until) return;
            Stop();
        }

        public static void Stop()
        {
            until = low = high = 0f;
#if ENABLE_INPUT_SYSTEM
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
#endif
        }
    }
}
