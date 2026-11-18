using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.DualScreen
{
    /// <summary>
    /// The neighbourhood's controls on the second screen, so the main screen shows just the map:
    /// - the neighbourhood's name, and what to do (pick a house; or, moving a family in, their new lot);
    /// - the map's buttons (Create a family, Options, back to the neighbourhood, the other neighbourhoods), which
    ///   press the main screen's own (hidden there while this shows), so they do just what they do there;
    /// - a picked house's panel (<see cref="UIHouseSelectPanel"/>, laid out for this screen) over all of that.
    /// The pad reaches these from the map: down from its lowest house (the second screen counts as below the main
    /// one; see <see cref="SecondScreen"/>).
    /// </summary>
    public class UIDualNeighbourhood : UIContainer
    {
        /// <summary>The deck on the second screen, while it shows the neighbourhood.</summary>
        public static UIDualNeighbourhood Current { get; private set; }

        private readonly TS1GameScreen Game;
        private readonly float W, H;
        private readonly UILabel Title, Hint;
        private readonly List<UIElement> Mirrors = new List<UIElement>();
        private string MirrorKey;
        private const float ButtonScale = 0.8f;

        public UIDualNeighbourhood(TS1GameScreen game, float width, float height)
        {
            Game = game;
            W = width;
            H = height;

            Title = new UILabel();
            Title.CaptionStyle = Title.CaptionStyle.Clone();
            Title.CaptionStyle.Size = 30;
            Title.CaptionStyle.Color = UIStyle.Current.Text;
            Title.Alignment = TextAlignment.Center | TextAlignment.Middle;
            Title.Size = new Vector2(W, 50);
            Title.Position = new Vector2(0, 22);
            Add(Title);

            Hint = new UILabel();
            Hint.CaptionStyle = Hint.CaptionStyle.Clone();
            Hint.CaptionStyle.Size = 15;
            Hint.CaptionStyle.Color = UIStyle.Current.Text * 0.7f;
            Hint.Alignment = TextAlignment.Center | TextAlignment.Middle;
            Hint.Size = new Vector2(W, 30);
            Hint.Position = new Vector2(0, 74);
            Add(Hint);

            Current = this;
        }

        /// <summary>The size a house panel lays out for to fill this screen (it takes half of what it's given).</summary>
        public Point HousePanelLayout => new Point((int)(W * 2), (int)H);

        /// <summary>Shows a house's panel here, over the rest.</summary>
        public void HostHousePanel(UIHouseSelectPanel panel)
        {
            Add(panel);
        }

        public override void Update(UpdateState state)
        {
            var switcher = Game.NeighSwitcher;
            Title.Caption = NeighbourhoodName(switcher?.NeighMode ?? 4);
            Hint.Caption = (Game.MoveInFamily != null) ? "Choose a lot for the family on the top screen." : "Choose a house on the top screen.";
            UpdateMirrors(switcher);
            base.Update(state);
        }

        private static string NeighbourhoodName(ushort mode)
        {
            switch (mode)
            {
                case 2: return "Downtown";
                case 3: return "Vacation Island";
                case 5: return "Studio Town";
                case 7: return "Magic Town";
                default: return "The Neighborhood";
            }
        }

        /// <summary>
        /// The map's buttons, again whenever its set changes (it changes with the neighbourhood): its own group on
        /// the first row (Create a family, Options...), the other neighbourhoods on the second, each named under it.
        /// </summary>
        private void UpdateMirrors(UINeighbourhoodSwitcher switcher)
        {
            var left = switcher?.LeftBtns ?? new List<UIElasticButton>();
            var right = switcher?.RightBtns ?? new List<UIElasticButton>();
            var key = string.Join("|", left.Select(x => x.Tooltip)) + "#" + string.Join("|", right.Select(x => x.Tooltip));
            if (key == MirrorKey) return;
            MirrorKey = key;
            foreach (var old in Mirrors) Remove(old);
            Mirrors.Clear();
            AddRow(left, 205);
            AddRow(right, 385);
            //a house panel opened before stays on top
            foreach (var panel in GetChildren().OfType<UIHouseSelectPanel>().ToList()) Add(panel);
        }

        private void AddRow(List<UIElasticButton> buttons, float y)
        {
            if (buttons.Count == 0) return;
            var spacing = W / System.Math.Max(4, buttons.Count);
            var start = W / 2 - spacing * (buttons.Count - 1) / 2;
            for (int i = 0; i < buttons.Count; i++)
            {
                var original = buttons[i];
                var x = start + spacing * i;
                var mirror = new UIElasticButton(original.Texture);
                mirror.Tooltip = original.Tooltip;
                mirror.OnButtonClick += (b) => original.PerformClick();
                //(in a holder: the button animates its own scale when pressed)
                var holder = new UIContainer();
                holder.ScaleX = holder.ScaleY = ButtonScale;
                holder.Position = new Vector2(x, y);
                holder.Add(mirror);
                Add(holder);
                Mirrors.Add(holder);

                var label = new UILabel();
                label.CaptionStyle = label.CaptionStyle.Clone();
                label.CaptionStyle.Size = 13;
                label.CaptionStyle.Color = UIStyle.Current.Text;
                label.Alignment = TextAlignment.Center | TextAlignment.Top;
                label.Wrapped = true;
                label.MaxLines = 2;
                label.Size = new Vector2(spacing - 8, 40);
                label.Position = new Vector2(x - (spacing - 8) / 2, y + 48 * ButtonScale + 10);
                label.Caption = original.Tooltip;
                Add(label);
                Mirrors.Add(label);
            }
        }

        public override void Removed()
        {
            if (Current == this) Current = null;
            base.Removed();
        }
    }
}
