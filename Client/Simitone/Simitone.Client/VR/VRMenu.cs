using System.Collections.Generic;
using FSO.Client.UI.Model;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The VR options menu: a small floating panel for the comfort and control options (<see cref="VROptions"/>), so
    /// they can be changed in the headset while playing. Point at it with the right controller: the trigger steps an
    /// option forward, the grip steps it back, and pressing either beside the menu closes it. Changes apply at once and
    /// are saved to vr.ini.
    ///
    /// Open it with the left controller's menu button (Touch-style controllers), a left trackpad squeeze (Index), by
    /// pointing at your left wrist (the needs display) and pulling the trigger, or with F9 on the desktop keyboard.
    /// </summary>
    public class VRMenu : VRPanel
    {
        private const int Width = 640, HeaderHeight = 78, RowHeight = 36, FooterHeight = 14;
        private const int Dot = 3;

        private readonly List<VROption> Rows;
        public bool Open { get; private set; }
        private int Hover = -1;
        private bool Armed;
        private bool Dirty = true;

        private int Height => HeaderHeight + (Rows.Count + 1) * RowHeight + FooterHeight; // + the Close row

        public VRMenu()
        {
            WidthMetres = 0.6f;
            Rows = VROptions.Create(includeSetup: true);
        }

        /// <summary>Opens the menu in front of you (tracking space).</summary>
        public void Show(VRPose head, VRInput input)
        {
            if (!head.Valid) return;
            PlaceInFront(head, 0.85f, 0.15f, 12); // a little below eye level, tilted back to face you
            Open = true;
            Armed = input == null || (!input.Select.Down && !input.Secondary.Down);
            Hover = -1;
            Dirty = true;
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        public void Close()
        {
            if (!Open) return;
            Open = false;
            PointerDistance = 0;
            Hover = -1;
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        /// <summary>Points the right controller at the menu and handles its buttons. Call once per frame while open.</summary>
        public void Update(VRInput input, VRRuntime runtime)
        {
            PointerDistance = 0;
            if (!Open || input == null || !input.Active) return;
            if (!input.Select.Down && !input.Secondary.Down) Armed = true;

            var hover = -1;
            var onMenu = PointAt(input, Width, Height, out var pixel);
            if (onMenu)
            {
                var row = (pixel.Y - HeaderHeight) / RowHeight;
                if (pixel.Y >= HeaderHeight && row <= Rows.Count) hover = row; // Rows.Count = Close
            }
            if (hover != Hover)
            {
                Hover = hover;
                Dirty = true;
                if (hover >= 0) runtime?.Haptic(true, 0.1f, 0.01f);
            }
            if (!Armed) return;

            int direction = input.Select.Pressed ? 1 : input.Secondary.Pressed ? -1 : 0;
            if (direction == 0) return;
            if (!onMenu || hover == Rows.Count)
            {
                Close(); // pressed beside the menu, or on Close
                return;
            }
            if (hover < 0) return;
            var option = Rows[hover];
            if (option.IsAction) Close();
            VROptions.Apply(option, direction);
            Dirty = true;
            runtime?.Haptic(true, 0.3f, 0.02f);
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        /// <summary>Redraws the menu when something changed. Call outside the main draw (it switches render targets).</summary>
        public void Render(GraphicsDevice gd)
        {
            if (!Open || (!Dirty && Target != null)) return;
            Dirty = false;

            var bg = new Color(16, 20, 28) * 0.94f;
            var frame = new Color(90, 200, 255);
            var text = Color.White * 0.92f;
            var value = new Color(140, 255, 190);

            BeginRender(gd, Width, Height);
            Fill(new Rectangle(0, 0, Width, Height), bg);
            Border(new Rectangle(0, 0, Width, Height), frame, 3);
            Text("VR OPTIONS", 20, 16, 4, frame);
            Text("TRIGGER: NEXT   GRIP: BACK", 20, 54, 2, text * 0.6f);
            for (int i = 0; i <= Rows.Count; i++)
            {
                var y = HeaderHeight + i * RowHeight;
                var rect = new Rectangle(8, y + 2, Width - 16, RowHeight - 4);
                if (i == Hover) Fill(rect, frame * 0.25f);
                var textY = y + (RowHeight - VRPixelFont.GlyphHeight * Dot) / 2;
                if (i == Rows.Count)
                {
                    TextCentred("CLOSE", rect, Dot, i == Hover ? frame : text);
                    continue;
                }
                var row = Rows[i];
                Text(row.Label, 20, textY, Dot, text);
                var shown = row.IsAction ? row.Value() : i == Hover ? "< " + row.Value() + " >" : row.Value();
                Text(shown, Width - 20 - VRPixelFont.Measure(shown, Dot), textY, Dot, value);
            }
            EndRender(gd);
        }

        /// <summary>Adds the menu to the frame, on top of everything else.</summary>
        public void Submit(VRRuntime runtime, ref VRFrameSubmission submission)
        {
            if (Open) SubmitPanel(runtime, ref submission);
        }
    }
}
