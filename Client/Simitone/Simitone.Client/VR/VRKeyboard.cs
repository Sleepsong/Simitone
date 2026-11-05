using System.Collections.Generic;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// A keyboard you point at, in Simitone's look (outlined keys, the game's font): it appears below the view whenever a
    /// text box is waiting for typing (naming a baby or pet, Create-A-Sim names, any dialog with a text box) and types
    /// into it with FreeSO's on-screen keyboard helpers (<see cref="UITextEdit.PadType"/>, <see cref="UITextEdit.PadBackspace"/>).
    /// Each word starts with a capital; Shift changes the next letter. Hold Del to keep deleting. Hide puts it away until
    /// another text box wants it.
    /// </summary>
    public class VRKeyboard : VRUIPanel
    {
        private const int KeyW = 88, KeyH = 66, Gap = 8, Margin = 28, Stagger = 22, PreviewHeight = 74;
        private const int PanelWidth = Margin * 2 + 10 * KeyW + 9 * Gap + 2 * Stagger + 32;
        private const int PanelHeight = Margin + PreviewHeight + 16 + 5 * (KeyH + Gap) - Gap + Margin;

        private readonly List<(UIVRKey key, char c)> Letters = new List<(UIVRKey, char)>();
        private readonly UIVRKey ShiftKey, DeleteKey;
        private readonly UILabel Preview;
        private bool Shift;
        private float RepeatTimer;

        /// <summary>The text box being typed into (set every update by SimitoneVR).</summary>
        public UITextEdit TextTarget;
        /// <summary>Hide was pressed: SimitoneVR puts the keyboard away until another text box wants it.</summary>
        public bool HideRequested;

        public VRKeyboard() : base(PanelWidth, PanelHeight, 0.6f)
        {
            Add(new UIDiagonalStripe(new Point(PanelWidth - 32, PanelHeight), UIDiagonalStripeSide.RIGHT, UIStyle.Current.DialogBg));

            var previewBox = VRUI.Outline(PanelWidth - 32 - Margin * 2, PreviewHeight);
            previewBox.Position = new Vector2(Margin, Margin);
            Add(previewBox);
            Preview = VRUI.Label("", 25, UIStyle.Current.Text, TextAlignment.Left | TextAlignment.Middle);
            Preview.Position = new Vector2(Margin + 24, Margin);
            Preview.Size = new Vector2(PanelWidth - 32 - Margin * 2 - 48, PreviewHeight);
            Add(Preview);

            var rows = new[] { "1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm-" };
            int y = Margin + PreviewHeight + 16;
            for (int r = 0; r < rows.Length; r++)
            {
                int x = Margin + System.Math.Min(r, 2) * Stagger; // staggered like a real keyboard
                if (r == 3)
                {
                    ShiftKey = AddKey("Shift", Margin, y, KeyW * 3 / 2, 19, _ => { Shift = !Shift; Click(); });
                    x = Margin + KeyW * 3 / 2 + Gap;
                }
                foreach (var c in rows[r])
                {
                    var ch = c;
                    var key = AddKey(c.ToString(), x, y, KeyW, 25, _ => Type(ch));
                    if (char.IsLetter(c)) Letters.Add((key, c));
                    x += KeyW + Gap;
                }
                if (r == 3) DeleteKey = AddKey("Del", x, y, PanelWidth - 32 - Margin - x, 19, _ => { Delete(); RepeatTimer = 0.5f; });
                y += KeyH + Gap;
            }
            AddKey("Space", Margin + 2 * (KeyW + Gap), y, 6 * KeyW + 5 * Gap, 19, _ => Type(' '));
            AddKey("Hide", PanelWidth - 32 - Margin - 2 * KeyW - Gap, y, 2 * KeyW + Gap, 19, _ => { HideRequested = true; Click(); });
        }

        private UIVRKey AddKey(string label, int x, int y, int width, int textSize, System.Action<UIVRKey> press)
        {
            var key = new UIVRKey(label, width, KeyH, textSize) { Position = new Vector2(x, y) };
            key.OnPress += press;
            Add(key);
            return key;
        }

        /// <summary>Whether the next letter typed will be a capital: at the start of a word, unless Shift flips it.</summary>
        private bool NextIsCapital
        {
            get
            {
                var text = TextTarget?.CurrentText ?? "";
                var startOfWord = text.Length == 0 || text.EndsWith(" ");
                return startOfWord != Shift;
            }
        }

        private void Type(char c)
        {
            if (char.IsLetter(c))
            {
                c = NextIsCapital ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
                Shift = false;
            }
            TextTarget?.PadType(c.ToString());
            Click();
        }

        private void Delete()
        {
            TextTarget?.PadBackspace();
            Click();
        }

        private void Click()
        {
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
            SimitoneVR.Runtime?.Haptic(true, 0.25f, 0.012f);
        }

        protected override void Refresh(UpdateState state)
        {
            // letters show the case they'll type
            var capital = NextIsCapital;
            foreach (var (key, c) in Letters) key.Caption = capital ? char.ToUpperInvariant(c).ToString() : c.ToString();
            ShiftKey.Lit = Shift;
            Preview.Caption = (TextTarget?.CurrentText ?? "") + "|";

            // hold Del to keep deleting
            if (DeleteKey.Held)
            {
                RepeatTimer -= (float)(state.Time?.ElapsedGameTime.TotalSeconds ?? 1 / 60.0);
                if (RepeatTimer <= 0)
                {
                    Delete();
                    RepeatTimer = 0.08f;
                }
            }
        }

        /// <summary>Shows the keyboard below and in front of you, tilted up like a desk (tracking space).</summary>
        public void ShowAt(VRPose head)
        {
            if (!head.Valid) return;
            PlaceInFront(head, 0.5f, 0.4f, 40);
            Shift = false;
            Show();
        }
    }
}
