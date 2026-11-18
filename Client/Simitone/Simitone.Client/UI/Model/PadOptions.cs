using FSO.Common.Rendering.Framework.IO;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Model
{
    /// <summary>
    /// The controller settings' steps in the Options dialog (its Controller tab), and how a saved value maps onto them.
    /// The values themselves live in config.ini (GlobalSettings.GamePad*).
    /// </summary>
    public static class PadOptions
    {
        /// <summary>Stick dead zones offered: none to half the stick's travel, in twentieths.</summary>
        public static readonly float[] DeadZones = Enumerable.Range(0, 11).Select(i => i * 0.05f).ToArray();

        /// <summary>Speeds offered for the stick pointer (the stand-in mouse), as multiples of its normal speed.</summary>
        public static readonly float[] PointerSpeeds = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f };

        /// <summary>The button symbol sets, as GamePadGlyphs takes them, and their names.</summary>
        public static readonly string[] GlyphSettings = { "auto", "xbox", "playstation", "nintendo" };
        public static readonly string[] GlyphNames = { "Automatic", "Xbox", "PlayStation", "Nintendo" };

        /// <summary>
        /// The actions as Button Mapping lists them, in order, with what they do: in live mode, then (after the slash) in
        /// buy and build mode where that's different. Menus take Confirm, Cancel and the d-pad (the left stick moves
        /// through them too).
        /// </summary>
        public static readonly (PadAction Action, string Name)[] ActionNames =
        {
            (PadAction.Confirm, "Confirm"),
            (PadAction.Cancel, "Cancel"),
            (PadAction.FaceLeft, "Direct Control / Eyedropper"),
            (PadAction.FaceTop, "Action Queue / Sell"),
            (PadAction.ShoulderLeft, "Previous Sim / Rotate"),
            (PadAction.ShoulderRight, "Next Sim / Rotate"),
            (PadAction.TriggerLeft, "Needs Panel / Catalog"),
            (PadAction.TriggerRight, "Panel Tab / Category"),
            (PadAction.Up, "Floor Up"),
            (PadAction.Down, "Floor Down"),
            (PadAction.Left, "Slower"),
            (PadAction.Right, "Faster"),
            (PadAction.Start, "Pause"),
            (PadAction.Select, "Next Mode"),
            (PadAction.LeftStickClick, "Walls"),
            (PadAction.RightStickClick, "Find Sim"),
        };

        public static string ActionName(PadAction action)
        {
            foreach (var entry in ActionNames) if (entry.Action == action) return entry.Name;
            return action.ToString();
        }

        /// <summary>An action's buttons, by name in a symbol set ("B, Back"), or "None".</summary>
        public static string ButtonsText(Buttons[] buttons, PadGlyphSet set)
        {
            return (buttons == null || buttons.Length == 0) ? "None" : string.Join(", ", buttons.Select(x => PadGlyphs.ButtonName(x, set)));
        }

        /// <summary>The stick pointer row: Off, then its speeds (index 0 is off).</summary>
        public static List<string> PointerSteps()
        {
            return new[] { "Off" }.Concat(Labels(PointerSpeeds)).ToList();
        }

        public static int PointerStep(bool on, float speed) => on ? 1 + Nearest(PointerSpeeds, speed) : 0;

        /// <summary>A step's label: a fraction as a percentage ("20%").</summary>
        public static string Percent(float value)
        {
            return (int)Math.Round(value * 100) + "%";
        }

        /// <summary>The labels for a list of steps.</summary>
        public static List<string> Labels(float[] steps)
        {
            return steps.Select(Percent).ToList();
        }

        /// <summary>The step nearest a saved value (one set by hand in config.ini needn't be a step).</summary>
        public static int Nearest(float[] steps, float value)
        {
            var best = 0;
            for (int i = 1; i < steps.Length; i++)
                if (Math.Abs(steps[i] - value) < Math.Abs(steps[best] - value)) best = i;
            return best;
        }

        /// <summary>Which symbol set a GamePadGlyphs value is ("ps" is PlayStation too; anything else is automatic).</summary>
        public static int GlyphIndex(string setting)
        {
            switch ((setting ?? "").Trim().ToLowerInvariant())
            {
                case "xbox": return 1;
                case "playstation":
                case "ps": return 2;
                case "nintendo":
                case "switch": return 3;
                default: return 0;
            }
        }
    }
}
