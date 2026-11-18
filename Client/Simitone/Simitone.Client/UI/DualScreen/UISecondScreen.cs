using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;

namespace Simitone.Client.UI.DualScreen
{
    /// <summary>
    /// Everything on the second screen. It's one of FreeSO's <see cref="UIExternalContainer"/>s: the UI layer
    /// updates it and draws it into its own texture (not onto the main screen), with its own input, and
    /// <see cref="SecondScreen.Present"/> hands each new image to the platform to show. Its touches come from the
    /// platform too. On a lot it shows the lot's controls (<see cref="UIDualDeck"/>); in the neighbourhood, its
    /// buttons and the house panel (<see cref="UIDualNeighbourhood"/>); in Create a Family, the Sim's panel and the
    /// screen's buttons (<see cref="UIDualCAS"/>); elsewhere, a title.
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
        /// <summary>Updates left before a new touch presses: the UI only presses what the pointer is already over.</summary>
        private int PressDelay;
        private Point TouchPos;
        private int ReleasedUpdates = 10;
        private static readonly MouseState Away = new MouseState(-10000, -10000, 0, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);

        private UIElement Content;
        private object ContentFor;
        private readonly PadOutlineLayer Outline = new PadOutlineLayer();

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
        /// The platform's touches become this screen's mouse:
        /// - a new touch first only moves the mouse there, for one update, and presses on the next: the UI presses
        ///   what the mouse was over the update before, and a finger, unlike a mouse, arrives without hovering first
        ///   (the main screen gives each new finger the same update's wait);
        /// - one press or release per update, so a quick tap (down and up between two frames) still clicks;
        /// - after a release the mouse leaves the screen, so nothing stays highlighted.
        /// </summary>
        private void PollTouches()
        {
            var host = SecondScreen.Host;
            if (host == null) return;
            if (PressDelay > 0) PressDelay--; //(the press happens now; the next touch event waits for the next update)
            else
            {
                var count = 0;
                while (host.NextTouch(out var x, out var y, out var down))
                {
                    TouchPos = new Point(x, y);
                    Touched = true;
                    if (down != TouchDown)
                    {
                        TouchDown = down;
                        if (down) PressDelay = 1;
                        else ReleasedUpdates = 0;
                        break;
                    }
                    if (++count > 64) break;
                }
            }
            if (TouchDown) ReleasedUpdates = 0;
            else ReleasedUpdates++;

            if (TouchDown || ReleasedUpdates <= 1)
            {
                var pressed = TouchDown && PressDelay == 0;
                mouse = new MouseState(TouchPos.X, TouchPos.Y, 0, pressed ? ButtonState.Pressed : ButtonState.Released,
                    ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            }
            else mouse = Away;
        }

        /// <summary>
        /// What the second screen shows for the main screen: a lot's controls (made again for each lot), the
        /// neighbourhood's (for each visit to it), Create a Family's, or the title.
        /// </summary>
        private void RefreshContent()
        {
            var screen = GameFacade.Screens?.CurrentUIScreen;
            var game = screen as TS1GameScreen;
            object want;
            if (game?.Frontend != null && game.vm != null) want = game.Frontend;
            else if (game != null && game.InNeighbourhood) want = game.TS1NeighPanel;
            else if (screen is TS1CASScreen) want = screen;
            else want = null;
            if (Content != null && want == ContentFor) return;
            if (Content != null) Remove(Content);
            ContentFor = want;
            if (want == null) Content = new UIDualIdle(LogicalWidth, LogicalHeight);
            else if (want is UINeighborhoodSelectionPanel) Content = new UIDualNeighbourhood(game, LogicalWidth, LogicalHeight);
            else if (want is TS1CASScreen cas) Content = new UIDualCAS(cas, LogicalWidth, LogicalHeight);
            else Content = new UIDualDeck(game, LogicalWidth, LogicalHeight);
            Add(Content);
            Add(Outline); //(on top)
            Invalidated = true;
        }

        /// <summary>
        /// Simitone's pad focus frame (as the main screen draws it) round a rectangle in this screen's pixels.
        /// </summary>
        public static void DrawPadOutline(UISpriteBatch batch, Rectangle rect, float time)
        {
            var scale = SecondScreen.Panel?.UIScale ?? 1f;
            var gd = GameFacade.GraphicsDevice;
            var pad = (int)((PadGraphics.GlowInset + 3) * scale);
            var frame = rect;
            frame.Inflate(pad, pad);
            var pulse = 0.82f + 0.18f * (float)Math.Sin(time * 4f);
            var shadow = frame;
            shadow.Inflate((int)(2 * scale), (int)(2 * scale));
            PadDraw.NineSliceScreen(batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, shadow, PadDraw.Ink * 0.45f, scale);
            PadDraw.NineSliceScreen(batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, frame, PadDraw.Accent * pulse, scale);
        }

        /// <summary>
        /// The focus outline of a main-screen navigator whose focus is on this screen (the neighbourhood's house panel,
        /// Create a Family's panel), drawn over everything here. See <see cref="SecondScreen.ElsewhereFocus"/>.
        /// </summary>
        private class PadOutlineLayer : UIElement
        {
            private float Time;

            public override void Update(UpdateState state)
            {
                base.Update(state);
                Time += (float)state.Time.ElapsedGameTime.TotalSeconds;
            }

            public override void Draw(UISpriteBatch batch)
            {
                var nav = SecondScreen.ElsewhereFocus;
                if (nav == null || SecondScreen.Frame - SecondScreen.ElsewhereFocusFrame > 2) return; //(not drawn lately: gone)
                var focused = nav.Focused;
                if (focused == null || !focused.WillDraw() || !SecondScreen.IsOnSecondScreen(focused)) return;
                if ((focused as IPadOwnFocus)?.PadShowsFocus ?? false) return; //(it shows the focus itself)
                DrawPadOutline(batch, UIFocusNavigator.GlobalRect(focused), Time);
            }
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
