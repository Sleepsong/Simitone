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
        public static readonly string[] GlyphSettings = { "auto", "xbox", "playstation" };
        public static readonly string[] GlyphNames = { "Automatic", "Xbox", "PlayStation" };

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
                default: return 0;
            }
        }
    }
}
