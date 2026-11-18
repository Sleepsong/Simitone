using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Screens;

namespace Simitone.Client.UI.DualScreen
{
    /// <summary>
    /// Create a Family's controls on the second screen, so the main screen shows just the lot and the Sims. Each
    /// mode's panel is moved here from the main screen, laid out for this one (and slides in and out as it does there):
    /// - picking a family: the families (<see cref="Panels.CAS.UIFamiliesCASPanel"/>), with New family and Delete;
    /// - a new family: its last name and its Sims (<see cref="Panels.CAS.UIFamilyCASPanel"/>), the family standing on
    ///   the main screen;
    /// - a new Sim: the Sim's panel (name, gender, age, skin, personality, bio: <see cref="Panels.CAS.UISimCASPanel"/>;
    ///   the head and body carousels stay on the main screen, in the 3D view);
    /// - Back and Accept (Move in, Done), which press the main screen's own (hidden there while this shows);
    /// - a keyboard (<see cref="UIDualKeyboard"/>) whenever a name or the bio is being typed, on either screen: tap
    ///   the text box, then the keys (the main screen's pad keyboard serves the pad there instead).
    /// The pad reaches these from the main screen: down from its lowest element (see <see cref="SecondScreen"/>).
    /// </summary>
    public class UIDualCAS : UIContainer
    {
        /// <summary>The deck on the second screen, while it shows Create a Family.</summary>
        public static UIDualCAS Current { get; private set; }

        private readonly TS1CASScreen Cas;
        private readonly float W, H;
        private readonly UILabel Title, Hint;
        /// <summary>Back and Accept: the pad's root for them (with two screens, the main screen navigates them here).</summary>
        public readonly UIContainer PadButtons = new UIContainer();
        private readonly UITwoStateButton BackMirror, AcceptMirror;
        private readonly UIDualKeyboard Keyboard;
        private const float ButtonScale = 0.7f;
        private const float PanelTop = 58;
        /// <summary>The height of the row of Back and Accept, at the bottom (the panels stay above it).</summary>
        private const float ButtonRow = 95;
        /// <summary>The widths the families and family panels need (their lists are 810 wide, and the buttons beside).</summary>
        private const float FamiliesWidth = 960, FamilyWidth = 860;

        /// <summary>The keyboard is showing, typing into <see cref="Target"/>.</summary>
        public bool KeyboardOpen { get; private set; }
        private UITextEdit Target;
        private InputManager Input; //this screen's (a text box tapped here has its focus)

        public UIDualCAS(TS1CASScreen cas, float width, float height)
        {
            Cas = cas;
            W = width;
            H = height;

            Title = new UILabel();
            Title.CaptionStyle = Title.CaptionStyle.Clone();
            Title.CaptionStyle.Size = 26;
            Title.CaptionStyle.Color = UIStyle.Current.Text;
            Title.Alignment = TextAlignment.Center | TextAlignment.Middle;
            Title.Size = new Vector2(W, 44);
            Title.Position = new Vector2(0, 8);
            Add(Title);

            Hint = new UILabel();
            Hint.CaptionStyle = Hint.CaptionStyle.Clone();
            Hint.CaptionStyle.Size = 15;
            Hint.CaptionStyle.Color = UIStyle.Current.Text * 0.7f;
            Hint.Alignment = TextAlignment.Center | TextAlignment.Top;
            Hint.Wrapped = true;
            Hint.Size = new Vector2(W - 60, 90);
            Hint.Position = new Vector2(30, 70);
            Add(Hint);

            var size = 115 * ButtonScale;
            BackMirror = Mirror(cas.BackButton, new Vector2(15, H - 15 - size));
            AcceptMirror = Mirror(cas.AcceptButton, new Vector2(W - 15 - size, H - 15 - size));
            Add(PadButtons);

            Keyboard = new UIDualKeyboard(W, H - 344 - 4);
            Keyboard.Position = new Vector2(0, 344);
            Keyboard.OnKey = KeyPressed;
            Keyboard.HasText = () => (Target?.CurrentText.Length ?? 0) > 0;
            Keyboard.Visible = false;
            Add(Keyboard);

            Current = this;
        }

        private UITwoStateButton Mirror(UITwoStateButton original, Vector2 position)
        {
            var mirror = new UITwoStateButton(original.Texture);
            mirror.Tooltip = original.Tooltip;
            mirror.OnButtonClick += (b) => original.PerformClick();
            mirror.ScaleX = mirror.ScaleY = ButtonScale;
            mirror.Position = position;
            PadButtons.Add(mirror);
            return mirror;
        }

        public override void Update(UpdateState state)
        {
            Input = state.InputManager;
            var mode = Cas.CurrentMode;
            TakePanels(mode);

            //(the families and family panels have titles of their own)
            switch (mode)
            {
                case UICASMode.FamilySelect:
                    Title.Caption = "";
                    Hint.Caption = "";
                    break;
                case UICASMode.FamilyEdit:
                    Title.Caption = "";
                    Hint.Caption = "Add Sims with +, and tap one to edit or delete them. Name the family above, then Done. The family stands on the top screen.";
                    Hint.Y = 125;
                    break;
                default:
                    Title.Caption = "Create a Sim";
                    Hint.Caption = "";
                    break;
            }

            //Back and Accept, as the main screen's (which change with the mode)
            Sync(BackMirror, Cas.BackButton);
            Sync(AcceptMirror, Cas.AcceptButton);

            UpdateKeyboard(state);
            base.Update(state);
        }

        private static void Sync(UITwoStateButton mirror, UITwoStateButton original)
        {
            if (mirror.Texture != original.Texture) mirror.Texture = original.Texture;
            mirror.Tooltip = original.Tooltip;
            mirror.Disabled = original.Disabled;
        }

        /// <summary>
        /// The families, family and Sim panels here instead of on the main screen (in the order they're there), the
        /// first two scaled down to fit and laid out for the space above Back and Accept; the Sim panel under the title,
        /// in Sim edit.
        /// </summary>
        private void TakePanels(UICASMode mode)
        {
            Take(Cas.CASPanel);
            Take(Cas.FamilyPanel);
            Take(Cas.FamiliesPanel);
            Cas.PanelsElsewhere = true;

            var families = Cas.FamiliesPanel;
            var scale = System.Math.Min(1f, W / FamiliesWidth);
            families.ScaleX = families.ScaleY = scale;
            families.Position = Vector2.Zero;
            families.SetLayout(new Point((int)(W / scale), (int)((H - ButtonRow) / scale)));

            var family = Cas.FamilyPanel;
            scale = System.Math.Min(1f, W / FamilyWidth);
            family.ScaleX = family.ScaleY = scale;
            family.Position = Vector2.Zero;
            family.SetLayout(new Point((int)(W / scale), (int)((H - ButtonRow) / scale)));

            var sim = Cas.CASPanel;
            sim.Position = new Vector2((W - 500) / 2, PanelTop);
            sim.Visible = mode == UICASMode.SimEdit;
        }

        private void Take(UIElement panel)
        {
            if (panel.Parent == this) return;
            var from = panel.Parent?.GetChildren();
            if (from != null) lock (from) from.Remove(panel); //(not Remove: the panel isn't gone)
            AddBefore(panel, PadButtons);
        }

        // ---- keyboard

        /// <summary>
        /// The text box being typed into: one tapped here, or the main screen's (a family's name). Not while the main
        /// screen's pad keyboard is up, which has the pad and types there.
        /// </summary>
        private void UpdateKeyboard(UpdateState state)
        {
            var here = Input?.GetFocus() as UITextEdit;
            var there = Cas.MainTextFocus;
            var padKeys = UIPadOverlay.Instance?.Keyboard.Active ?? false;
            UITextEdit target = null;
            if (here != null && !here.IsReadOnly && here.Visible) target = here;
            else if (there != null && !there.IsReadOnly && there.Visible && (!padKeys || SecondScreen.IsOnSecondScreen(there))) target = there;
            if (target != Target)
            {
                Target = target;
                Keyboard.Layout.Reset();
                Keyboard.Shift = (target?.CurrentText.Length ?? 0) == 0; //capitalise a name's first letter
            }
            KeyboardOpen = target != null;
            Keyboard.Visible = KeyboardOpen;
            Keyboard.ShowFocus = KeyboardOpen && Cas.PadInUse;
            PadButtons.Visible = !KeyboardOpen; //(under the keyboard)
        }

        /// <summary>The pad's controls on the keyboard, while it shows (from the main screen's update).</summary>
        public void PadKeyboardUpdate(UpdateState state)
        {
            Keyboard.PadUpdate(state);
        }

        private void KeyPressed(PadKey key)
        {
            var target = Target;
            if (target == null) return;
            switch (key.Action)
            {
                case PadKeyAction.Backspace:
                    target.PadBackspace();
                    break;
                case PadKeyAction.Done:
                    CloseKeyboard();
                    break;
                default:
                    var typed = key.Type(Keyboard.Shift);
                    if (typed != null) target.PadType(typed);
                    break;
            }
        }

        /// <summary>Done: the text box lets go of the focus (here, or on the main screen), and the keyboard goes.</summary>
        private void CloseKeyboard()
        {
            if (Target != null && Input?.GetFocus() == Target) Input.SetFocus(null);
            else Cas.ClearMainTextFocus = true;
            Target = null;
            KeyboardOpen = false;
            Keyboard.Visible = false;
        }

        public override void Removed()
        {
            if (Current == this) Current = null;
            //the panels go back to the main screen (not removed with this)
            if (Cas.PanelsElsewhere) Cas.ReclaimPanels();
            base.Removed();
        }
    }
}
