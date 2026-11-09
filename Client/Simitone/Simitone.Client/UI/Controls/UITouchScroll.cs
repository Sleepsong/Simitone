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
        /// Horizontal lists: items in this many rows (a column at a time, top to bottom), sharing the height. One by
        /// default, as the catalog strip has; the second screen's catalog has room for two.
        /// </summary>
        public int Rows = 1;
        private int RowsUsed => VerticalMode ? 1 : Math.Max(1, Rows);
        private float RowHeight => Size.Y / RowsUsed;
        private int Columns(int length) => (length + RowsUsed - 1) / RowsUsed;

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
            var column = (int)(GetPAxis(at) + Scroll) / ItemWidth;
            var row = (RowsUsed > 1) ? Math.Max(0, Math.Min(RowsUsed - 1, (int)(at.Y / RowHeight))) : 0;
            var item = column * RowsUsed + row;
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
            Scroll = Math.Max(-Margin, Math.Min(Columns(length) * ItemWidth - GetPAxis(Size) + Margin, Scroll));

            //update children positions.
            //delete ones that are not 

            var untouched = new HashSet<UIElement>(Children);

            var rows = RowsUsed;
            var b = (int)(Scroll / ItemWidth);
            var e = b + (GetPAxis(Size) + (ItemWidth - 1)) / ItemWidth;
            for (int c=b; c<e; c++)
            {
                if (c < 0) continue;
                if (c * rows >= length) break;
                for (int r = 0; r < rows; r++)
                {
                    var i = c * rows + r;
                    if (i >= length) break;
                    var item = GetOrPrepare(i);
                    untouched.Remove(item);
                    if (VerticalMode) item.Y = i * ItemWidth - Scroll;
                    else
                    {
                        item.X = c * ItemWidth - Scroll;
                        if (rows > 1) item.Y = r * RowHeight;
                    }
                    item.Visible = true;
                }
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
            // Center the item (its column) in the visible area
            var targetScroll = (itemIndex / RowsUsed) * ItemWidth - (GetPAxis(Size) / 2) + (ItemWidth / 2);
            // Clamp to valid scroll range
            var length = LengthProvider();
            Scroll = Math.Max(-Margin, Math.Min(Columns(length) * ItemWidth - GetPAxis(Size) + Margin, targetScroll));
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
        public Rectangle PadBounds => (RowsUsed > 1)
            ? Rectangle.Intersect(new Rectangle((int)((HighlightedIndex() / RowsUsed) * ItemWidth - Scroll),
                (int)((HighlightedIndex() % RowsUsed) * RowHeight), ItemWidth, (int)RowHeight), new Rectangle(0, 0, (int)Size.X, (int)Size.Y))
            : PadMath.ListItemBounds(HighlightedIndex(), ItemWidth, Scroll, (int)Size.X, (int)Size.Y, VerticalMode);

        /// <summary>The item the pad has highlighted, or -1 if the pad hasn't moved over this list.</summary>
        public int PadHighlightIndex => (PadIndex >= 0 && LengthProvider() > 0) ? HighlightedIndex() : -1;

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
            if (RowsUsed > 1) return PadMoveGrid(direction);
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
        /// <see cref="PadMove"/> with several rows (filled column by column): left and right go to the next column,
        /// on the same row (or the last item, from a full column into a shorter last one); up and down move within
        /// the column. Off the grid, focus moves on.
        /// </summary>
        private bool PadMoveGrid(PadDirection direction)
        {
            var length = LengthProvider();
            if (length == 0) return false;
            var rows = RowsUsed;
            var from = PadHighlightOnly ? HighlightedIndex() : (LastSelected?.ItemID ?? -1);
            int to;
            if (from < 0) to = 0;
            else
            {
                var row = from % rows;
                switch (direction)
                {
                    case PadDirection.Left: to = from - rows; break;
                    case PadDirection.Right:
                        to = from + rows;
                        if (to >= length && from / rows < Columns(length) - 1) to = length - 1;
                        break;
                    case PadDirection.Up: to = (row > 0) ? from - 1 : -1; break;
                    default: to = (row < rows - 1) ? from + 1 : -1; break;
                }
                if (to < 0 || to >= length) return false;
            }
            if (PadHighlightOnly) PadIndex = to;
            else SelectItem(to);
            ScrollToItem(to);
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
