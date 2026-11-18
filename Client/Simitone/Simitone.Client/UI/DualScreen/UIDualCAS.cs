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
    /// Create a Family's controls on the second screen, so the main screen shows the Sims and the families:
    /// - what's being done (the families, a new family, a new Sim);
    /// - in Sim edit, the Sim's panel (name, gender, age, skin, personality, bio: <see cref="Panels.CAS.UISimCASPanel"/>,
    ///   moved here from the main screen; the head and body carousels stay there, in the 3D view);
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
            TakeSimPanel(mode);

            switch (mode)
            {
                case UICASMode.FamilySelect:
                    Title.Caption = "Families";
                    Hint.Caption = "Pick a family on the top screen and move them in, or make a new one.";
                    break;
                case UICASMode.FamilyEdit:
                    Title.Caption = "Create a Family";
                    Hint.Caption = "Add Sims to the family and give it a name on the top screen, then Done.";
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

        /// <summary>The Sim panel here (in Sim edit) instead of on the main screen, under the title.</summary>
        private void TakeSimPanel(UICASMode mode)
        {
            var panel = Cas.CASPanel;
            if (panel.Parent != this)
            {
                var from = panel.Parent?.GetChildren();
                if (from != null) lock (from) from.Remove(panel); //(not Remove: the panel isn't gone)
                AddBefore(panel, PadButtons);
                Cas.CASPanelElsewhere = true;
            }
            panel.Position = new Vector2((W - 500) / 2, PanelTop);
            panel.Visible = mode == UICASMode.SimEdit;
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
            //the Sim panel goes back to the main screen (not removed with this)
            var panel = Cas.CASPanel;
            if (panel.Parent == this)
            {
                lock (GetChildren()) GetChildren().Remove(panel);
                Cas.ReclaimCASPanel();
            }
            base.Removed();
        }
    }
}
