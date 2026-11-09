using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Screens;
using System;

namespace Simitone.Client.UI.DualScreen
{
    /// <summary>
    /// Everything on the second screen. It's one of FreeSO's <see cref="UIExternalContainer"/>s: the UI layer
    /// updates it and draws it into its own texture (not onto the main screen), with its own input, and
    /// <see cref="SecondScreen.Present"/> hands each new image to the platform to show. Its touches come from the
    /// platform too. On a lot it shows the lot's controls (<see cref="UIDualDeck"/>); elsewhere, a title.
    /// </summary>
    public class UISecondScreen : UIExternalContainer
    {
        public readonly int PixelWidth, PixelHeight;
        /// <summary>UI scale, as the main screen's on phones: the short side is about 560 UI pixels.</summary>
        public readonly float UIScale;
        public float LogicalWidth => Size.X;
        public float LogicalHeight => Size.Y;
        public bool Shown { get; private set; }

        private bool Registered;
        private int RenderedFrames, TakenFrames;
        private int UpdatesSinceDraw;

        private bool TouchDown, Touched;
        private Point TouchPos;
        private int ReleasedUpdates = 10;
        private static readonly MouseState Away = new MouseState(-10000, -10000, 0, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);

        private UIElement Content;
        private object ContentFor;

        public UISecondScreen(int pixelWidth, int pixelHeight) : base(1, 1)
        {
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            UIScale = Math.Max(1f, Math.Min(4f, Math.Min(pixelWidth, pixelHeight) / 560f));
            ScaleX = ScaleY = UIScale;
            // the texture is Size x scale (rounded down): a hair over, so it comes out at the screen's exact size
            Size = new Vector2(pixelWidth / UIScale + 0.01f, pixelHeight / UIScale + 0.01f);
            NeedFrames = 0; //(no copy of each frame to the CPU: the image goes straight to the screen)
            ClearColor = new Color(22, 28, 38);
            Visible = false;
            mouse = Away;
        }

        public void Show()
        {
            if (!Registered && GameFacade.Screens != null)
            {
                GameFacade.Screens.AddExternal(this);
                Registered = true;
            }
            Shown = Visible = true;
            Invalidated = true;
        }

        /// <summary>Stops drawing and updating it (it stays with the UI layer, to come back quickly).</summary>
        public void Hide()
        {
            Shown = Visible = false;
            mouse = Away;
            TouchDown = false;
        }

        public void Destroy()
        {
            Hide();
            if (Registered) GameFacade.Screens?.RemoveExternal(this);
            Registered = false;
        }

        /// <summary>The second screen's current image (drawn by the UI layer), or null before its first frame.</summary>
        public RenderTarget2D Image => Target;

        /// <summary>True once for each newly drawn image.</summary>
        public bool TakeNewFrame()
        {
            if (TakenFrames == RenderedFrames) return false;
            TakenFrames = RenderedFrames;
            return true;
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            var willDraw = Invalidated && Visible;
            base.PreDraw(batch);
            if (willDraw && !Invalidated && Target != null) RenderedFrames++;
        }

        public override void Update(UpdateState state)
        {
            if (!Shown) return;
            PollTouches();
            RefreshContent();
            base.Update(state);
            // redrawn at up to 30 frames a second (the clock, needs and animations move), and at once on a touch
            if (Touched || ++UpdatesSinceDraw >= 2)
            {
                Invalidated = true;
                UpdatesSinceDraw = 0;
                Touched = false;
            }
        }

        /// <summary>
        /// The platform's touches become this screen's mouse. One press or release per update, so a quick tap
        /// (down and up between two frames) still clicks. After a release the mouse leaves the screen, so nothing
        /// stays highlighted.
        /// </summary>
        private void PollTouches()
        {
            var host = SecondScreen.Host;
            if (host == null) return;
            var count = 0;
            while (host.NextTouch(out var x, out var y, out var down))
            {
                TouchPos = new Point(x, y);
                Touched = true;
                if (down != TouchDown)
                {
                    TouchDown = down;
                    if (!down) ReleasedUpdates = 0;
                    break;
                }
                if (++count > 64) break;
            }
            if (TouchDown) ReleasedUpdates = 0;
            else ReleasedUpdates++;

            if (TouchDown || ReleasedUpdates <= 1)
            {
                mouse = new MouseState(TouchPos.X, TouchPos.Y, 0, TouchDown ? ButtonState.Pressed : ButtonState.Released,
                    ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            }
            else mouse = Away;
        }

        /// <summary>The lot's controls while a lot is up (made again for each lot), otherwise the title.</summary>
        private void RefreshContent()
        {
            var game = GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;
            object want = (game?.Frontend != null && game.vm != null) ? game.Frontend : null;
            if (Content != null && want == ContentFor) return;
            if (Content != null) Remove(Content);
            ContentFor = want;
            Content = (want != null)
                ? (UIElement)new UIDualDeck(game, LogicalWidth, LogicalHeight)
                : new UIDualIdle(LogicalWidth, LogicalHeight);
            Add(Content);
            Invalidated = true;
        }
    }

    /// <summary>The second screen away from a lot: the title, and where the controls will be.</summary>
    public class UIDualIdle : UIContainer
    {
        public UIDualIdle(float width, float height)
        {
            var title = new UILabel();
            title.CaptionStyle = title.CaptionStyle.Clone();
            title.CaptionStyle.Size = 37;
            title.CaptionStyle.Color = UIStyle.Current.Text;
            title.Alignment = TextAlignment.Center | TextAlignment.Middle;
            title.Size = new Vector2(width, 60);
            title.Position = new Vector2(0, height / 2 - 60);
            title.Caption = "Simitone";
            Add(title);

            var hint = new UILabel();
            hint.CaptionStyle = hint.CaptionStyle.Clone();
            hint.CaptionStyle.Size = 15;
            hint.CaptionStyle.Color = UIStyle.Current.Text * 0.7f;
            hint.Alignment = TextAlignment.Center | TextAlignment.Middle;
            hint.Size = new Vector2(width, 30);
            hint.Position = new Vector2(0, height / 2 + 5);
            hint.Caption = "On a lot, your Sims' controls show up here.";
            Add(hint);
        }
    }
}
