using System;
using System.Collections.Generic;
using FSO.Client.UI.Model;
using FSO.Client.UI.Controls;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;
using Simitone.VR.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// A keyboard you point at, for typing in the headset: it appears below the view whenever a text box is waiting
    /// for input (naming a baby or pet, Create-A-Sim names, any dialog with a text box) and types into it with
    /// FreeSO's on-screen keyboard helpers (<see cref="UITextEdit.PadType"/>, <see cref="UITextEdit.PadBackspace"/>).
    /// Point with the right controller and pull the trigger on a key. Letters start each word in capitals; SHIFT
    /// changes the next letter's case. HIDE puts it away until another text box wants it.
    /// </summary>
    public class VRKeyboard : VRPanel
    {
        private const int KeyW = 64, KeyH = 54, Gap = 6, Margin = 12, PreviewHeight = 58, Stagger = 16;
        private const int Width = Margin * 2 + 10 * KeyW + 9 * Gap + 2 * Stagger;

        private enum KeyKind { Char, Shift, Backspace, Space, Hide }

        private struct Key
        {
            public Rectangle Rect;
            public KeyKind Kind;
            public char Char;
            public string Label;
        }

        private readonly List<Key> Keys = new List<Key>();
        private readonly int Height;
        public bool Visible { get; private set; }
        private int Hover = -1;
        private bool Armed;
        private bool Dirty = true;
        private bool Shift;      // the next letter's case is flipped
        private string LastText;
        private float RepeatTimer;

        // keys pressed since the last game update, in order ('\b' = backspace), applied there to the text box
        private readonly List<char> Pending = new List<char>();

        public VRKeyboard()
        {
            WidthMetres = 0.56f;
            var rows = new[] { "1234567890", "QWERTYUIOP", "ASDFGHJKL'", "ZXCVBNM-" };
            int y = Margin + PreviewHeight + Gap;
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                int x = Margin + Math.Min(r, 2) * Stagger; // staggered like a real keyboard
                if (r == 3)
                {
                    Keys.Add(new Key { Rect = new Rectangle(Margin, y, KeyW * 3 / 2, KeyH), Kind = KeyKind.Shift, Label = "SHIFT" });
                    x = Margin + KeyW * 3 / 2 + Gap;
                }
                foreach (var c in row)
                {
                    Keys.Add(new Key { Rect = new Rectangle(x, y, KeyW, KeyH), Kind = KeyKind.Char, Char = c, Label = c.ToString() });
                    x += KeyW + Gap;
                }
                if (r == 3) Keys.Add(new Key { Rect = new Rectangle(x, y, Width - Margin - x, KeyH), Kind = KeyKind.Backspace, Label = "DEL" });
                y += KeyH + Gap;
            }
            Keys.Add(new Key { Rect = new Rectangle(Margin + KeyW * 2, y, KeyW * 6 + Gap * 5, KeyH), Kind = KeyKind.Space, Label = "SPACE" });
            Keys.Add(new Key { Rect = new Rectangle(Width - Margin - KeyW * 2, y, KeyW * 2, KeyH), Kind = KeyKind.Hide, Label = "HIDE" });
            Height = y + KeyH + Margin;
        }

        /// <summary>Shows the keyboard below and in front of you, tilted up like a desk (tracking space).</summary>
        public void Show(VRPose head, VRInput input)
        {
            if (!head.Valid) return;
            PlaceInFront(head, 0.5f, 0.4f, 40);
            Visible = true;
            Armed = input == null || !input.Select.Down;
            Hover = -1;
            Dirty = true;
        }

        public void Hide()
        {
            Visible = false;
            PointerDistance = 0;
            Hover = -1;
            ClearPending();
        }

        /// <summary>
        /// Points the right controller at the keyboard and presses keys. Returns true while the pointer is on it (the
        /// keyboard then has the pointer, not the screen or panel behind).
        /// </summary>
        public bool Update(VRInput input, VRRuntime runtime, float dt)
        {
            PointerDistance = 0;
            if (!Visible || input == null || !input.Active) return false;
            if (!input.Select.Down) Armed = true;

            var hover = -1;
            var onKeyboard = PointAt(input, Width, Height, out var pixel);
            if (onKeyboard)
            {
                for (int i = 0; i < Keys.Count; i++)
                {
                    var r = Keys[i].Rect;
                    r.Inflate(Gap / 2, Gap / 2);
                    if (r.Contains(pixel)) { hover = i; break; }
                }
            }
            if (hover != Hover)
            {
                Hover = hover;
                Dirty = true;
                RepeatTimer = 0;
                if (hover >= 0) runtime?.Haptic(true, 0.08f, 0.008f);
            }
            if (!Armed || hover < 0) return onKeyboard;

            if (input.Select.Pressed)
            {
                Press(Keys[hover], runtime);
                RepeatTimer = 0.5f;
            }
            else if (input.Select.Down && Keys[hover].Kind == KeyKind.Backspace)
            {
                // hold DEL to keep deleting
                RepeatTimer -= dt;
                if (RepeatTimer <= 0)
                {
                    Press(Keys[hover], runtime);
                    RepeatTimer = 0.08f;
                }
            }
            return onKeyboard;
        }

        private void Press(Key key, VRRuntime runtime)
        {
            runtime?.Haptic(true, 0.25f, 0.012f);
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
            Dirty = true;
            switch (key.Kind)
            {
                case KeyKind.Char:
                    var c = key.Char;
                    if (char.IsLetter(c))
                    {
                        // capital at the start of each word, unless SHIFT flips it
                        var text = ExpectedText();
                        var startOfWord = text.Length == 0 || text.EndsWith(" ");
                        c = startOfWord != Shift ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
                        Shift = false;
                    }
                    Pending.Add(c);
                    break;
                case KeyKind.Space:
                    Pending.Add(' ');
                    break;
                case KeyKind.Backspace:
                    if (Pending.Count > 0 && Pending[Pending.Count - 1] != '\b') Pending.RemoveAt(Pending.Count - 1);
                    else Pending.Add('\b');
                    break;
                case KeyKind.Shift:
                    Shift = !Shift;
                    break;
                case KeyKind.Hide:
                    HideRequested = true;
                    break;
            }
        }

        /// <summary>The text as it will be once the pending keys are applied (typing at the end).</summary>
        private string ExpectedText()
        {
            var text = new System.Text.StringBuilder(LastText ?? "");
            foreach (var c in Pending)
            {
                if (c != '\b') text.Append(c);
                else if (text.Length > 0) text.Length--;
            }
            return text.ToString();
        }

        /// <summary>Set when HIDE was pressed; the owner hides the keyboard until another text box wants it.</summary>
        public bool HideRequested;

        /// <summary>Applies the keys pressed since the last update to the text box. Call from the game update.</summary>
        public void ApplyPending(UITextEdit target)
        {
            if (target == null) { ClearPending(); return; }
            var run = new System.Text.StringBuilder();
            foreach (var c in Pending)
            {
                if (c != '\b') { run.Append(c); continue; }
                if (run.Length > 0) { target.PadType(run.ToString()); run.Clear(); }
                target.PadBackspace();
            }
            if (run.Length > 0) target.PadType(run.ToString());
            ClearPending();
            var text = target.CurrentText ?? "";
            if (text != LastText)
            {
                LastText = text;
                Dirty = true;
            }
        }

        private void ClearPending() => Pending.Clear();

        // ------------------------------------------------------------------ drawing

        /// <summary>Redraws the keyboard when something changed. Call outside the main draw.</summary>
        public void Render(GraphicsDevice gd)
        {
            if (!Visible || (!Dirty && Target != null)) return;
            Dirty = false;

            var bg = new Color(16, 20, 28) * 0.94f;
            var frame = new Color(90, 200, 255);
            var keyBg = new Color(40, 48, 62);
            var text = Color.White * 0.92f;

            BeginRender(gd, Width, Height);
            Fill(new Rectangle(0, 0, Width, Height), bg);
            Border(new Rectangle(0, 0, Width, Height), frame, 3);

            // what's in the text box (the font only has capitals; the box itself shows the real text)
            var preview = new Rectangle(Margin, Margin, Width - Margin * 2, PreviewHeight);
            Fill(preview, Color.Black * 0.5f);
            var shown = VRPixelFont.FitEnd((LastText ?? "") + "_", 3, preview.Width - 24);
            Text(shown, preview.X + 12, preview.Y + (PreviewHeight - 21) / 2, 3, new Color(140, 255, 190));

            for (int i = 0; i < Keys.Count; i++)
            {
                var key = Keys[i];
                var active = key.Kind == KeyKind.Shift && Shift;
                Fill(key.Rect, i == Hover ? frame * 0.55f : active ? frame * 0.35f : keyBg);
                TextCentred(key.Label, key.Rect, key.Label.Length == 1 ? 4 : 2, i == Hover ? Color.White : text);
            }
            EndRender(gd);
        }

        /// <summary>Adds the keyboard to the frame while it's shown.</summary>
        public void Submit(VRRuntime runtime, ref VRFrameSubmission submission)
        {
            if (Visible) SubmitPanel(runtime, ref submission);
        }
    }
}
