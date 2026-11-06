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
            (VRTurnMode.Snap, 30, "Snap 30"), (VRTurnMode.Snap, 45, "Snap 45"), (VRTurnMode.Snap, 90, "Snap 90"),
            (VRTurnMode.Smooth, 90, "Smooth, slow"), (VRTurnMode.Smooth, 120, "Smooth"), (VRTurnMode.Smooth, 180, "Smooth, fast"),
        };
        private static readonly float[] Heights = { 0, 0.15f, 0.3f, 0.45f, 0.6f };
        private static readonly float[] RenderScales = { 0.5f, 0.6f, 0.7f, 0.75f, 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };

        /// <param name="includeSetup">Add the "start the comfort setup" button (needs the headset running).</param>
        public static List<VROption> Create(bool includeSetup)
        {
            var list = new List<VROption>();
            void Add(string label, Func<string> value, Action<int> change)
                => list.Add(new VROption { Label = label, Value = value, Change = change });

            Add("Hands", () => Settings.LeftHanded ? "Left-handed" : "Right-handed", d => Settings.LeftHanded = !Settings.LeftHanded);
            Add("Sim actions", () => Name(Settings.SimActionView), d => Settings.SimActionView = Cycle(Settings.SimActionView, d));
            Add("Vignette", () => Name(Settings.Vignette), d => Settings.Vignette = Cycle(Settings.Vignette, d));
            Add("Vignette when moving", () => Name(Settings.VignetteWhenMoving), d => Settings.VignetteWhenMoving = Cycle(Settings.VignetteWhenMoving, d));
            Add("Movement", () => MovementName(Settings.Movement), d => Settings.Movement = Cycle(Settings.Movement, d));
            Add("Turning", () => Turns[TurnIndex()].name, d => SetTurn(Turns[Wrap(TurnIndex() + d, Turns.Length)]));
            Add("Walk towards", () => Settings.MoveDirection == VRMoveDirection.Head ? "Head" : "Stick hand",
                d => Settings.MoveDirection = Cycle(Settings.MoveDirection, d));
            Add("Run", () => Settings.RunAtFullTilt ? "Stick full tilt" : "Click the stick", d => Settings.RunAtFullTilt = !Settings.RunAtFullTilt);
            Add("Fast game + walking", () => Settings.NormalSpeedWhenSteering ? "Slow down" : "Keep speed",
                d => Settings.NormalSpeedWhenSteering = !Settings.NormalSpeedWhenSteering);
            Add("Play as", () => SimitoneVR.PlayMode switch { VRPlayMode.Sim => "Your Sim", VRPlayMode.Observer => "Observer", _ => "Dollhouse" }, d =>
            {
                SimitoneVR.PlayMode = Cycle(SimitoneVR.PlayMode, d);
                Settings.PlayMode = SimitoneVR.PlayMode;
            });
            Add("Extra height", () => Settings.HeightOffset < 0.01f ? "Off" : "+" + Settings.HeightOffset.ToString("0.00") + " m",
                d => Settings.HeightOffset = Heights[Wrap(Nearest(Heights, Settings.HeightOffset) + d, Heights.Length)]);
            Add("Wrist displays", () => Name(Settings.WristDisplays), d => Settings.WristDisplays = Cycle(Settings.WristDisplays, d));
            Add("Wrist display size", () => Settings.WristDisplaySize < 1.15f ? "Small" : Settings.WristDisplaySize < 1.45f ? "Medium" : "Large",
                d => Settings.WristDisplaySize = Settings.WristDisplaySize < 1.15f ? (d > 0 ? 1.3f : 1.6f)
                    : Settings.WristDisplaySize < 1.45f ? (d > 0 ? 1.6f : 1f) : (d > 0 ? 1f : 1.3f));
            Add("Wrist side", () => Settings.WristOtherSide ? "Turned round" : "As the controller says", d => Settings.WristOtherSide = !Settings.WristOtherSide);
            Add("Messages", () => Settings.WatchMessages ? "On the watch" : "Pop up", d => Settings.WatchMessages = !Settings.WatchMessages);
            Add("Stick when busy", () => Settings.StickCancelsActions ? "Takes over" : "Waits", d => Settings.StickCancelsActions = !Settings.StickCancelsActions);
            Add("Action bubble", () => Settings.ActionBubble ? "On" : "Off", d => Settings.ActionBubble = !Settings.ActionBubble);
            Add("Sim's hands", () => Settings.SimHands ? "Follow yours" : "Animated", d => Settings.SimHands = !Settings.SimHands);
            Add("Controllers", () => Settings.ControllerModels ? "Shown" : "Hidden", d => Settings.ControllerModels = !Settings.ControllerModels);
            Add("Buy and build", () => Settings.VRBuildMode ? "In VR" : "Flat screen", d => Settings.VRBuildMode = !Settings.VRBuildMode);
            Add("3D sound", () => !Settings.Sound3D ? "Off" : VRAudio.HrtfStatus == "On" ? "On (HRTF)" : "On, " + VRAudio.HrtfStatus,
                d => { Settings.Sound3D = !Settings.Sound3D; SimitoneVR.ApplySound(); });
            Add("Stairs (observer)", () => Name(Settings.ObserverStairs), d => Settings.ObserverStairs = Cycle(Settings.ObserverStairs, d));
            Add("Dollhouse body", () => Settings.DollhouseBody ? "Your Sim, giant" : "Controllers", d => Settings.DollhouseBody = !Settings.DollhouseBody);
            list.Add(new VROption { Label = "Dollhouse", Value = () => "Fit it to me", Change = d => SimitoneVR.RequestDollhouseRefit(), IsAction = true });
            Add("Walls in buy/build", () => Settings.BuildWalls == VRWallView.Cutaway ? "Cut away" : Name(Settings.BuildWalls),
                d => Settings.BuildWalls = Cycle(Settings.BuildWalls, d));
            Add("Catalogue", () => Settings.Catalogue == VRCatalogStyle.Hand ? "In your hand" : "On a desk",
                d => Settings.Catalogue = Cycle(Settings.Catalogue, d));
            Add("Pie menus", () => Settings.VRPieMenus ? "VR" : "Game's", d => Settings.VRPieMenus = !Settings.VRPieMenus);
            Add("Single-pass stereo", () => !Settings.SinglePassStereo ? "Off" : SimitoneVR.StereoStatus,
                d => Settings.SinglePassStereo = !Settings.SinglePassStereo);
            Add("Foveation", () => Settings.Foveation == VRFoveation.Off ? "Off"
                : Name(Settings.Foveation) + (SimitoneVR.Runtime?.EyeGazeSupported == true ? ", eyes" : ", centre"),
                d => Settings.Foveation = Cycle(Settings.Foveation, d));
            Add("Render scale", () => (int)Math.Round(Settings.RenderScale * 100) + "%",
                d => SimitoneVR.SetRenderScale(RenderScales[Wrap(Nearest(RenderScales, Settings.RenderScale) + d, RenderScales.Length)]));
            if (includeSetup)
                list.Add(new VROption { Label = "Comfort setup", Value = () => "Start", Change = d => SimitoneVR.StartSetup(), IsAction = true });
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
            VRMovement.Stick => "Stick",
            VRMovement.Arc => "Arc",
            _ => "Stick + arc"
        };

        public static void SetTurn((VRTurnMode mode, float degrees, string name) turn)
        {
            Settings.TurnMode = turn.mode;
            if (turn.mode == VRTurnMode.Snap) Settings.SnapTurnDegrees = turn.degrees;
            else Settings.SmoothTurnDegreesPerSecond = turn.degrees;
        }

        public static string Name<T>(T value) where T : struct, Enum => value.ToString();

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
