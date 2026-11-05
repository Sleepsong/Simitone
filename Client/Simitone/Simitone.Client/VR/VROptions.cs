using System;
using System.Collections.Generic;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>One VR option: a name, its current value as text, and how to step it (+1 next, -1 previous).</summary>
    public class VROption
    {
        public string Label;
        public Func<string> Value;
        public Action<int> Change;
        /// <summary>A button that does something (e.g. start the comfort setup) rather than a value.</summary>
        public bool IsAction;
    }

    /// <summary>
    /// The VR options that can be changed while playing: one list, shown both by the VR menu in the headset
    /// (<see cref="VRMenu"/>) and by the VR button in the game's Options panel. Changes apply at once and are saved
    /// to vr.ini.
    /// </summary>
    public static class VROptions
    {
        private static VRSettings Settings => SimitoneVR.Settings;

        private static readonly (VRTurnMode mode, float degrees, string name)[] Turns =
        {
            (VRTurnMode.Snap, 30, "SNAP 30"), (VRTurnMode.Snap, 45, "SNAP 45"), (VRTurnMode.Snap, 90, "SNAP 90"),
            (VRTurnMode.Smooth, 90, "SMOOTH SLOW"), (VRTurnMode.Smooth, 120, "SMOOTH"), (VRTurnMode.Smooth, 180, "SMOOTH FAST"),
        };
        private static readonly float[] Heights = { 0, 0.15f, 0.3f, 0.45f, 0.6f };
        private static readonly float[] RenderScales = { 0.5f, 0.6f, 0.7f, 0.75f, 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };

        /// <param name="includeSetup">Add the "start the comfort setup" button (needs the headset running).</param>
        public static List<VROption> Create(bool includeSetup)
        {
            var list = new List<VROption>();
            void Add(string label, Func<string> value, Action<int> change)
                => list.Add(new VROption { Label = label, Value = value, Change = change });

            Add("SIM ACTIONS", () => Name(Settings.SimActionView), d => Settings.SimActionView = Cycle(Settings.SimActionView, d));
            Add("VIGNETTE", () => Name(Settings.Vignette), d => Settings.Vignette = Cycle(Settings.Vignette, d));
            Add("VIGNETTE MOVING", () => Name(Settings.VignetteWhenMoving), d => Settings.VignetteWhenMoving = Cycle(Settings.VignetteWhenMoving, d));
            Add("MOVEMENT", () => MovementName(Settings.Movement), d => Settings.Movement = Cycle(Settings.Movement, d));
            Add("TURNING", () => Turns[TurnIndex()].name, d => SetTurn(Turns[Wrap(TurnIndex() + d, Turns.Length)]));
            Add("WALK TOWARDS", () => Settings.MoveDirection == VRMoveDirection.Head ? "HEAD" : "LEFT HAND",
                d => Settings.MoveDirection = Cycle(Settings.MoveDirection, d));
            Add("RUN", () => Settings.RunAtFullTilt ? "STICK FULL TILT" : "CLICK STICK", d => Settings.RunAtFullTilt = !Settings.RunAtFullTilt);
            Add("FAST GAME + WALKING", () => Settings.NormalSpeedWhenSteering ? "SLOW TO 1" : "KEEP SPEED",
                d => Settings.NormalSpeedWhenSteering = !Settings.NormalSpeedWhenSteering);
            Add("PLAY AS", () => SimitoneVR.PlayMode == VRPlayMode.Sim ? "YOUR SIM" : "OBSERVER", d =>
            {
                SimitoneVR.PlayMode = SimitoneVR.PlayMode == VRPlayMode.Sim ? VRPlayMode.Observer : VRPlayMode.Sim;
                Settings.PlayMode = SimitoneVR.PlayMode;
            });
            Add("EXTRA HEIGHT", () => Settings.HeightOffset < 0.01f ? "OFF" : "+" + Settings.HeightOffset.ToString("0.00") + " M",
                d => Settings.HeightOffset = Heights[Wrap(Nearest(Heights, Settings.HeightOffset) + d, Heights.Length)]);
            Add("WRIST DISPLAYS", () => Name(Settings.WristDisplays), d => Settings.WristDisplays = Cycle(Settings.WristDisplays, d));
            Add("MESSAGES", () => Settings.WatchMessages ? "ON WATCH" : "POP UP", d => Settings.WatchMessages = !Settings.WatchMessages);
            Add("STICK WHEN BUSY", () => Settings.StickCancelsActions ? "TAKES OVER" : "WAITS", d => Settings.StickCancelsActions = !Settings.StickCancelsActions);
            Add("ACTION BUBBLE", () => Settings.ActionBubble ? "ON" : "OFF", d => Settings.ActionBubble = !Settings.ActionBubble);
            Add("BUY AND BUILD", () => Settings.VRBuildMode ? "IN VR" : "FLAT SCREEN", d => Settings.VRBuildMode = !Settings.VRBuildMode);
            Add("RENDER SCALE", () => (int)Math.Round(Settings.RenderScale * 100) + "%",
                d => SimitoneVR.SetRenderScale(RenderScales[Wrap(Nearest(RenderScales, Settings.RenderScale) + d, RenderScales.Length)]));
            if (includeSetup)
                list.Add(new VROption { Label = "COMFORT SETUP", Value = () => "START", Change = d => SimitoneVR.StartSetup(), IsAction = true });
            return list;
        }

        /// <summary>Steps an option and saves the settings.</summary>
        public static void Apply(VROption option, int direction)
        {
            option.Change(direction);
            if (option.IsAction) return;
            Settings.Save();
            SimitoneVR.Log("VR option: " + option.Label + " = " + option.Value());
        }

        public static string MovementName(VRMovement movement) => movement switch
        {
            VRMovement.Stick => "STICK",
            VRMovement.Arc => "ARC",
            _ => "STICK + ARC"
        };

        public static void SetTurn((VRTurnMode mode, float degrees, string name) turn)
        {
            Settings.TurnMode = turn.mode;
            if (turn.mode == VRTurnMode.Snap) Settings.SnapTurnDegrees = turn.degrees;
            else Settings.SmoothTurnDegreesPerSecond = turn.degrees;
        }

        public static string Name<T>(T value) where T : struct, Enum => value.ToString().ToUpperInvariant();

        public static T Cycle<T>(T value, int direction) where T : struct, Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            return values[Wrap(Array.IndexOf(values, value) + direction, values.Length)];
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        private static int Nearest(float[] values, float value)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++) if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value)) best = i;
            return best;
        }

        private static int TurnIndex()
        {
            var s = Settings;
            int best = 0;
            float bestError = float.MaxValue;
            for (int i = 0; i < Turns.Length; i++)
            {
                if (Turns[i].mode != s.TurnMode) continue;
                var error = Math.Abs(Turns[i].degrees - (s.TurnMode == VRTurnMode.Snap ? s.SnapTurnDegrees : s.SmoothTurnDegreesPerSecond));
                if (error < bestError) { bestError = error; best = i; }
            }
            return best;
        }
    }
}
