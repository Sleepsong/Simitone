using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.Content;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Simitone.Client.UI.Panels.CAS
{
    public class UIFamilyCASPanel : UIContainer
    {
        public UIDiagonalStripe NameStripe;
        public UIDiagonalStripe ListStripe;

        public UILabel SecondNameTitle;
        public UITextBox SecondName;
        public UIAvatarListPanel AvatarList;
        public UICategorySwitcher AvatarOptions;

        public Action<bool, int> ModifySim;

        /// <summary>
        /// The size this lays out for, when not the screen's (on a second screen: see UIDualCAS). SetLayout changes it.
        /// </summary>
        private Point? Layout;
        private int SW => Layout?.X ?? UIScreen.Current.ScreenWidth;
        private int SH => Layout?.Y ?? UIScreen.Current.ScreenHeight;

        /// <summary>Lays out for a size other than the screen's (null: the screen's again).</summary>
        public void SetLayout(Point? layout)
        {
            if (layout == Layout) return;
            Layout = layout;
            SecondNameTitle.Size = new Vector2(SW, 25);
            NameStripe.Position = new Vector2(SW, 30);
            GameResized();
            ShowI = ShowI;
        }

        private float _ShowI;
        public float ShowI
        {
            get
            {
                return _ShowI;
            }
            set
            {
                if (value < 1 && AvatarOptions.CategoryExpand == 1) AvatarOptions.Close();
                ListStripe.Visible = value > 0;
                NameStripe.Visible = value > 0;
                SecondNameTitle.Visible = value > 0;
                NameStripe.X = (1 - value) * SW;
                NameStripe.Y = 30;
                NameStripe.BodySize = new Point((int)(value * SW), NameStripe.BodySize.Y);
                ListStripe.BodySize = new Point((int)(value * SW), ListStripe.BodySize.Y);
                AvatarList.X = (1-value) * (-SW);
                SecondNameTitle.X = (1 - value) * (SW);
                SecondName.X = (1 - value) * (SW);
                _ShowI = value;
            }
        }

        private List<UICategory> AvatarCategories = new List<UICategory>()
        {
            new UICategory() { ID = 0, IconName = "live_motives.png" }, //dummy
            new UICategory() { ID = 1, IconName = "cas_cat_edit.png", Name = "Edit Sim" },
            new UICategory() { ID = 2, IconName = "cas_cat_del.png", Name = "Delete Sim" },
        };

        private int ActiveSelection = -1;
        private List<VMAvatar> Avatars;

        public UIFamilyCASPanel(List<VMAvatar> avatar)
        {
            Avatars = avatar;

            AvatarOptions = new UICategorySwitcher();
            AvatarOptions.InitCategories(AvatarCategories);
            AvatarOptions.MainButton.Visible = false;
            AvatarOptions.OnCategorySelect += AvatarOptions_OnCategorySelect;
            Add(AvatarOptions);

            NameStripe = new UIDiagonalStripe(new Point(0, 75), UIDiagonalStripeSide.LEFT, UIStyle.Current.Bg);
            NameStripe.Position = new Vector2(UIScreen.Current.ScreenWidth, 30);
            Add(NameStripe);

            SecondNameTitle = new UILabel();
            SecondNameTitle.Caption = "Last Name";
            SecondNameTitle.CaptionStyle = SecondNameTitle.CaptionStyle.Clone();
            SecondNameTitle.CaptionStyle.Color = UIStyle.Current.SecondaryText;
            SecondNameTitle.CaptionStyle.Size = 15;
            SecondNameTitle.Size = new Vector2(UIScreen.Current.ScreenWidth, 25);
            SecondNameTitle.Alignment = TextAlignment.Center | TextAlignment.Middle;
            SecondNameTitle.Position = new Vector2(0, 15);
            Add(SecondNameTitle);

            SecondName = new UITextBox();
            SecondName.TextMargin = new Rectangle();
            SecondName.SetSize(UIScreen.Current.ScreenWidth, 60);
            SecondName.Alignment = TextAlignment.Center;
            SecondName.TextStyle = SecondName.TextStyle.Clone();
            SecondName.TextStyle.Size = 37;
            SecondName.TextStyle.Color = UIStyle.Current.SecondaryText;
            SecondName.Position = new Vector2(0, 40);
            Add(SecondName);

            ListStripe = new UIDiagonalStripe(new Point(0, 125), UIDiagonalStripeSide.RIGHT, UIStyle.Current.Bg);
            ListStripe.Position = new Vector2(0, UIScreen.Current.ScreenHeight - 145);
            Add(ListStripe);

            AvatarList = new UIAvatarListPanel(avatar, () => SW);
            AvatarList.Y = UIScreen.Current.ScreenHeight - 145;
            AvatarList.OnSelection += AvatarList_OnSelection;
            Add(AvatarList);

            Reset();
            ShowI = ShowI;
        }

        private void AvatarOptions_OnCategorySelect(int obj)
        {
            if (obj == 0) return;
            ModifySim?.Invoke((obj == 2), ActiveSelection);
            AvatarOptions.Select(0);
        }

        public void Reset()
        {
            AvatarList.InitAvatarList();
            ActiveSelection = -1;
        }

        private void AvatarList_OnSelection(int obj)
        {
            if (obj >= Avatars.Count)
            {
                ModifySim?.Invoke(false, -1);
            } else
            {
                AvatarOptions.X = SW / 2 - (Avatars.Count()) * 50 + obj * 100 - 44;
                AvatarOptions.Y = SH - 145;
                if (AvatarOptions.CategoryExpand < 1 || ActiveSelection == -1 || ActiveSelection == obj)
                    AvatarOptions.Open();
            }
            ActiveSelection = obj;
        }

        public override void GameResized()
        {
            base.GameResized();
            AvatarList.Y = SH - 145;
            ListStripe.Position = new Vector2(0, SH - 145);
            SecondName.SetSize(SW, 60);

            if (ActiveSelection > -1)
            {
                AvatarOptions.X = SW / 2 - (Avatars.Count()) * 50 + ActiveSelection*100 - 44;
                AvatarOptions.Y = SH - 145;
            }

            AvatarList.InitAvatarList();
        }
    }

    public class UIAvatarListPanel : UIContainer
    {
        private List<VMAvatar> Avatars;
        private List<UIAvatarSelectButton> Btns = new List<UIAvatarSelectButton>();
        private Texture2D Bg;

        /// <summary>The "add a member" button at the end of the list.</summary>
        public UIElement AddButton => Btns.LastOrDefault();
        public event Action<int> OnSelection;
        /// <summary>The width the list is centred in (the family panel's layout).</summary>
        private Func<int> Width;
        public UIAvatarListPanel(List<VMAvatar> avatar, Func<int> width = null)
        {
            Avatars = avatar;
            Width = width ?? (() => UIScreen.Current.ScreenWidth);

            InitAvatarList();
        }

        public void InitAvatarList()
        {
            int i = 0;
            foreach (var btn in Btns)
            {
                Remove(btn);
            }
            Btns.Clear();

            i = 0;
            var baseX = Width()/2 - (Avatars.Count()) * 50;
            foreach (var fam in Avatars)
            {
                var btn = new UIAvatarSelectButton(UIIconCache.GetObject(fam));
                btn.Opacity = 1f;
                var id = i;
                btn.OnButtonClick += (b) => { Select(id); };
                btn.Name = fam.Name;
                btn.X = baseX + (i++) * 100;
                btn.Y = 50;
                Btns.Add(btn);
                Add(btn);
            }
            var btn2 = new UIAvatarSelectButton(Content.Get().CustomUI.Get("cas_new_plus.png").Get(GameFacade.GraphicsDevice));
            btn2.Opacity = 1f;
            var id2 = i;
            btn2.OnButtonClick += (b) => { Select(id2); };
            btn2.X = baseX + (i++) * 100;
            btn2.Y = 50;
            Btns.Add(btn2);
            Add(btn2);
        }

        private void Select(int selected)
        {
            int i = 0;
            foreach (var btn in Btns)
            {
                btn.Outlined = (i++) == selected;
            }
            OnSelection?.Invoke(selected);
        }

        public override void Draw(UISpriteBatch batch)
        {
            base.Draw(batch);
        }
    }
}
