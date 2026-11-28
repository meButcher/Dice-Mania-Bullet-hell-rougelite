using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DiceMania.Game
{
    /// <summary>Input facade: uses the Input System package when enabled, otherwise the legacy Input Manager.</summary>
    public static class GameInput
    {
        public static Vector2 Move;
        public static Vector2 MousePos;
        public static bool LmbDown, LmbHeld, LmbUp, RmbDown, Roll, Pause, Reload, Confirm;
        public static int NumberDown; // 1..9, 0 = none

        /// <summary>True while a gamepad drives aiming (right trigger held): <see cref="PadAim"/> replaces the mouse.</summary>
        public static bool PadAiming;
        public static Vector2 PadAim;

        /// <summary>
        /// Automated input (play-mode smoke tests, attract mode). When set it replaces the devices: edge flags are
        /// cleared every frame and the callback writes Move / MousePos / Roll / Lmb* itself.
        /// </summary>
        public static System.Action Bot;

        public static void Update()
        {
            NumberDown = 0;
            if (Bot != null)
            {
                Roll = Pause = Reload = Confirm = LmbDown = LmbUp = RmbDown = false;
                Bot();
                if (Move.sqrMagnitude > 1f) Move.Normalize();
                return;
            }
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            var m = Mouse.current;
            Move = Vector2.zero;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed) Move.y += 1;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) Move.y -= 1;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) Move.x += 1;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) Move.x -= 1;
                Roll = k.spaceKey.wasPressedThisFrame;
                Pause = k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame;
                Reload = k.f5Key.wasPressedThisFrame;
                Confirm = k.enterKey.wasPressedThisFrame;
                if (k.digit1Key.wasPressedThisFrame) NumberDown = 1;
                else if (k.digit2Key.wasPressedThisFrame) NumberDown = 2;
                else if (k.digit3Key.wasPressedThisFrame) NumberDown = 3;
                else if (k.digit4Key.wasPressedThisFrame) NumberDown = 4;
                else if (k.digit5Key.wasPressedThisFrame) NumberDown = 5;
            }
            else { Roll = Pause = Reload = Confirm = false; }
            if (m != null)
            {
                MousePos = m.position.ReadValue();
                LmbDown = m.leftButton.wasPressedThisFrame;
                LmbHeld = m.leftButton.isPressed;
                LmbUp = m.leftButton.wasReleasedThisFrame;
                RmbDown = m.rightButton.wasPressedThisFrame;
            }
            else { LmbDown = LmbHeld = LmbUp = RmbDown = false; }

            // Gamepad: left stick move, A / RB roll, Start pause, D-pad picks cards, hold RT + right stick to aim a Copy shot.
            var g = Gamepad.current;
            PadAiming = false;
            if (g != null)
            {
                Vector2 ls = g.leftStick.ReadValue();
                if (ls.sqrMagnitude > 0.04f) Move += ls;
                Roll |= g.buttonSouth.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame;
                Pause |= g.startButton.wasPressedThisFrame;
                Confirm |= g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame;
                if (g.dpad.left.wasPressedThisFrame) NumberDown = 1;
                else if (g.dpad.up.wasPressedThisFrame) NumberDown = 2;
                else if (g.dpad.right.wasPressedThisFrame) NumberDown = 3;
                else if (g.dpad.down.wasPressedThisFrame) NumberDown = 4;
                PadAim = g.rightStick.ReadValue();
                if (g.rightTrigger.isPressed || g.rightTrigger.wasReleasedThisFrame)
                {
                    PadAiming = true;
                    LmbDown |= g.rightTrigger.wasPressedThisFrame;
                    LmbHeld |= g.rightTrigger.isPressed;
                    LmbUp |= g.rightTrigger.wasReleasedThisFrame;
                }
            }
#else
            Move = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) Move.y += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) Move.y -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) Move.x += 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) Move.x -= 1;
            Roll = Input.GetKeyDown(KeyCode.Space);
            Pause = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P);
            Reload = Input.GetKeyDown(KeyCode.F5);
            Confirm = Input.GetKeyDown(KeyCode.Return);
            for (int i = 1; i <= 5; i++) if (Input.GetKeyDown(KeyCode.Alpha0 + i)) NumberDown = i;
            MousePos = Input.mousePosition;
            LmbDown = Input.GetMouseButtonDown(0);
            LmbHeld = Input.GetMouseButton(0);
            LmbUp = Input.GetMouseButtonUp(0);
            RmbDown = Input.GetMouseButtonDown(1);
#endif
            if (Move.sqrMagnitude > 1f) Move.Normalize();
        }
    }
}
