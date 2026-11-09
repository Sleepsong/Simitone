using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Client;
using FSO.Client.UI.Model;
using FSO.HIT;
using FSO.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Rendering.Framework.IO;
using Simitone.Client.UI.Model;

namespace Simitone.Client.UI.Controls
{
    public class UITouchScroll : UICachedContainer, IPadNavigable, IPadOwnFocus
    {
        public int ItemWidth;
        public Texture2D ScrollEdgeL;
        public Texture2D ScrollEdgeR;
        public bool DrawBounds = true;
        public int Margin;
        public bool VerticalMode;

        /// <summary>
        /// Gamepad: directions move a highlight over the items without selecting them, and Confirm selects the
        /// highlighted one. For lists where selecting does something costly or irreversible, like the catalog (it
        /// creates the object). Otherwise directions select as they move.
        /// </summary>
        public bool PadHighlightOnly;
        private int PadIndex = -1;

        /// <summary>
        /// The selected item shows it's selected (the family list's highlight), so with the pad on the list there's no
        /// need for a focus frame round it.
        /// </summary>
        public bool PadSelectionVisible;

        public bool PadShowsFocus => PadSelectionVisible && !PadHighlightOnly && LastSelected != null;

        private UIMouseEventRef HitTest;

        private float Scroll;
        private float ScrollVelocity;

        private Func<int> LengthProvider;
        private Func<int, UITSContainer> ElemProvider;

        public UITouchScroll(Func<int> lengthProvider, Func<int, UITSContainer> elemProvider) : base()
        {
            var ui = Content.Get().CustomUI;
            ScrollEdgeL = ui.Get("scroll_edge_l.png").Get(GameFacade.GraphicsDevice);
            ScrollEdgeR = ui.Get("scroll_edge_r.png").Get(GameFacade.GraphicsDevice);
            HitTest = ListenForMouse(new Rectangle(0, 0, (int)Size.X, (int)Size.Y), new UIMouseEvent(MouseEvents));

            LengthProvider = lengthProvider;
            ElemProvider = elemProvider;
        }

        private int MouseDownTime;
        private int MouseDownID = -1;
        private Point MouseDownAt;
        private bool InScroll;
        private UITSContainer LastSelected;
        private List<float> ScrollVelocityHistory = new List<float>();

        public void MouseEvents(UIMouseEventType type, UpdateState state)
        {
            switch (type)
            {
                case UIMouseEventType.MouseDown:
                    MouseDownID = state.CurrentMouseID;
                    MouseDownTime = 0;
                    MouseDownAt = state.MouseState.Position;
                    InScroll = false;
                    ScrollVelocity = 0;
                    break;
                case UIMouseEventType.MouseUp:
                    if (!InScroll)
                    {
                        Select(GlobalPoint(MouseDownAt.ToVector2()).ToPoint());
                    }
                    else
                    {
                        //calculate scroll velocity
                        if (ScrollVelocityHistory.Count > 1)
                        {
                            int total = 0;
                            ScrollVelocity = 0f;
                            for (int i = 1; i < ScrollVelocityHistory.Count; i++)
                            {
                                total++;
                                ScrollVelocity += ScrollVelocityHistory[i];
                            }
                            ScrollVelocity /= total;
                        }
                        ScrollVelocityHistory.Clear();
                    }

                    InScroll = false;
                    MouseDownID = -1;
                    break;
            }
        }

        private int GetPAxis(Point p)
        {
            return (VerticalMode) ? p.Y : p.X;
        }

        private float GetPAxis(Vector2 p)
        {
            return (VerticalMode) ? p.Y : p.X;
        }

        public void Select(Point at)
        {
            var item = (int)(GetPAxis(at) + Scroll) / ItemWidth;
            if (item >= LengthProvider()) return;
            var rItem = GetOrPrepare(item);
            if (rItem != null)
            {
                LastSelected?.Deselected();
                rItem.Selected();
                LastSelected = rItem;
            }
        }

        public UITSContainer GetOrPrepare(int id)
        {
            var item = Children.Where(x => (x as UITSContainer)?.ItemID == id).FirstOrDefault() as UITSContainer;
            if (item == null)
            {
                item = ElemProvider(id);
                item.Visible = false;
                item.ItemID = id;
                if (id == LastSelected?.ItemID)
                {
                    item.Selected();
                    LastSelected = item;
                }
                Add(item);
            }
            return item;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            var sizeRect = new Rectangle(0, 0, (int)Size.X, (int)Size.Y);
            if (!sizeRect.Equals(HitTest.Region))
            {
                HitTest.Region = sizeRect;
            }

            var length = LengthProvider();

            //perform scroll and input management.

            if (MouseDownID != -1)
            {
                var lastMouse = state.MouseStates.FirstOrDefault(x => x.ID == MouseDownID);
                if (lastMouse != null)
                {
                    var pos = lastMouse.MouseState.Position;
                    if (!InScroll)
                    {
                        if (Math.Abs(GetPAxis(pos - MouseDownAt)) > 25) InScroll = true;
                    }
                    if (InScroll)
                    {
                        ScrollVelocity = -GetPAxis(pos - MouseDownAt);
                        MouseDownAt = pos;
                    }
                }
            }

            ScrollVelocityHistory.Insert(0, ScrollVelocity);
            if (ScrollVelocityHistory.Count > 5) ScrollVelocityHistory.RemoveAt(ScrollVelocityHistory.Count - 1);

            Scroll += ScrollVelocity;
            ScrollVelocity *= 0.9f;
            Scroll = Math.Max(-Margin, Math.Min(length * ItemWidth - GetPAxis(Size) + Margin, Scroll));

            //update children positions.
            //delete ones that are not 

            var untouched = new HashSet<UIElement>(Children);

            var b = (int)(Scroll / ItemWidth);
            var e = b + (GetPAxis(Size) + (ItemWidth - 1)) / ItemWidth;
            for (int i=b; i<e; i++)
            {
                if (i < 0) continue;
                if (i >= length) break;
                var item = GetOrPrepare(i);
                untouched.Remove(item);
                if (VerticalMode) item.Y = i * ItemWidth - Scroll;
                else item.X = i * ItemWidth - Scroll;
                item.Visible = true;
            }

            foreach (var child in untouched)
            {
                Children.Remove(child);
            }

            Invalidate();
        }

        public void SetScroll(float value)
        {
            Scroll = value;
        }

        public void ScrollToItem(int itemIndex)
        {
            // Center the item in the visible area
            var targetScroll = itemIndex * ItemWidth - (GetPAxis(Size) / 2) + (ItemWidth / 2);
            // Clamp to valid scroll range
            var length = LengthProvider();
            Scroll = Math.Max(-Margin, Math.Min(length * ItemWidth - GetPAxis(Size) + Margin, targetScroll));
            ScrollVelocity = 0; // Stop any ongoing momentum
        }

        /// <summary>
        /// Selects an item by its index programmatically, triggering the visual highlight.
        /// </summary>
        public void SelectItem(int itemIndex)
        {
            if (itemIndex < 0 || itemIndex >= LengthProvider()) return;
            var rItem = GetOrPrepare(itemIndex);
            if (rItem != null)
            {
                LastSelected?.Deselected();
                rItem.Selected();
                LastSelected = rItem;
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            base.Draw(batch);
            if (DrawBounds)
            {
                if (VerticalMode)
                {
                    DrawLocalTexture(batch, ScrollEdgeL, null, new Vector2(Size.X / 2 - 64, 0), Vector2.One, Color.White, (float)Math.PI/2f, new Vector2(0, ScrollEdgeL.Height));
                    DrawLocalTexture(batch, ScrollEdgeR, null, new Vector2(Size.X / 2 - 64, Size.Y - 15), Vector2.One, Color.White, (float)Math.PI / 2f, new Vector2(0, ScrollEdgeL.Height));
                }
                else
                {
                    DrawLocalTexture(batch, ScrollEdgeL, new Vector2(0, Size.Y / 2 - 64));
                    DrawLocalTexture(batch, ScrollEdgeR, new Vector2(Size.X - 15, Size.Y / 2 - 64));
                }
            }
        }

        public bool PadFocusable => Visible && LengthProvider() > 0;

        /// <summary>
        /// The highlighted item (in <see cref="PadHighlightOnly"/> mode) or the selected one (else the first, which
        /// Confirm would select), clipped to the list; the whole list if it's out of view.
        /// </summary>
        public Rectangle PadBounds =>
            PadMath.ListItemBounds(HighlightedIndex(), ItemWidth, Scroll, (int)Size.X, (int)Size.Y, VerticalMode);

        /// <summary>The item the highlight is on: where the pad left it, else the selected item, else the first.</summary>
        private int HighlightedIndex()
        {
            return PadMath.ClampIndex((PadIndex >= 0) ? PadIndex : (LastSelected?.ItemID ?? 0), LengthProvider());
        }

        /// <summary>
        /// Gamepad: directions along the list move the selection (and scroll to it); at either end, or across the
        /// list, focus moves on to the neighbouring element.
        /// </summary>
        public bool PadMove(PadDirection direction)
        {
            int step;
            if (VerticalMode) step = (direction == PadDirection.Up) ? -1 : ((direction == PadDirection.Down) ? 1 : 0);
            else step = (direction == PadDirection.Left) ? -1 : ((direction == PadDirection.Right) ? 1 : 0);
            if (step == 0) return false;

            if (PadHighlightOnly)
            {
                var to = PadMath.HighlightStep(HighlightedIndex(), step, LengthProvider());
                if (to < 0) return false;
                PadIndex = to;
                ScrollToItem(to);
                HITVM.Get()?.PlaySoundEvent(UISounds.Click);
                return true;
            }

            var current = LastSelected?.ItemID ?? -1;
            var next = (current < 0) ? 0 : current + step;
            if (next < 0 || next >= LengthProvider()) return false;
            SelectItem(next);
            ScrollToItem(next);
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
            return true;
        }

        /// <summary>
        /// Gamepad Confirm: selects the highlighted item in <see cref="PadHighlightOnly"/> mode; otherwise selects the
        /// first item if nothing is selected yet.
        /// </summary>
        public void PadActivate()
        {
            if (LengthProvider() == 0) return;
            if (PadHighlightOnly)
            {
                var index = HighlightedIndex();
                PadIndex = index;
                ScrollToItem(index);
                SelectItem(index);
                return;
            }
            if (LastSelected != null) return;
            SelectItem(0);
            ScrollToItem(0);
        }

        public void Reset()
        {
            var children = new List<UIElement>(Children);
            foreach (var child in children)
            {
                Remove(child);
            }
            LastSelected = null;
            PadIndex = -1;
        }
    }

    public class UITSContainer : UIContainer
    {
        public int ItemID;
        public virtual void Selected()
        {

        }

        public virtual void Deselected()
        {

        }
    }
}
