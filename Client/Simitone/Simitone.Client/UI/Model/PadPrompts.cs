using System;
using FSO.Common.Rendering.Framework.IO;

namespace Simitone.Client.UI.Model
{
    public enum PadStick
    {
        None,
        Left,
        Right,
        DPad
    }

    /// <summary>One entry in the button prompt bar: a button (or two, or a stick) and what it does.</summary>
    public struct PadPrompt
    {
        public PadAction? Action;
        public PadAction? Action2; //shown as "first/second", e.g. LB/RB
        public PadStick Stick;
        public string Label;

        public static PadPrompt Of(PadAction action, string label) => new PadPrompt { Action = action, Label = label };
        public static PadPrompt Pair(PadAction first, PadAction second, string label) => new PadPrompt { Action = first, Action2 = second, Label = label };
        public static PadPrompt For(PadStick stick, string label) => new PadPrompt { Stick = stick, Label = label };
    }

    /// <summary>
    /// The button prompts for this frame. Whatever owns the pad sets them while it updates; the pad overlay, which
    /// draws after everything else, shows them. Prompts not set this frame aren't shown.
    /// </summary>
    public static class PadPrompts
    {
        private static readonly PadPrompt[] None = new PadPrompt[0];
        private static PadPrompt[] Items = None;
        private static int SetFrame = -1;

        /// <summary>Counts updates. The overlay advances it first thing every update.</summary>
        public static int Frame { get; private set; }

        public static void BeginFrame() => Frame++;

        public static void Set(params PadPrompt[] items)
        {
            Items = items ?? None;
            SetFrame = Frame;
        }

        public static PadPrompt[] Current => (SetFrame == Frame) ? Items : None;
    }
}
