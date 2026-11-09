using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Content;
using Simitone.Client.UI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Simitone.Client.UI.Panels
{
    public class UINeighbourhoodSwitcher : UIContainer
    {
        public List<UIElasticButton> LeftBtns = new List<UIElasticButton>();
        public List<UIElasticButton> RightBtns = new List<UIElasticButton>();
        private UINeighborhoodSelectionPanel Panel;
        private ushort Mode;
        public bool MoveInMode;

        public UINeighbourhoodSwitcher(UINeighborhoodSelectionPanel panel, ushort mode, bool moveIn)
        {
            Panel = panel;
            SetMode(mode, moveIn);
        }

        public void SetMode(ushort mode, bool moveIn)
        {
            MoveInMode = moveIn;
            foreach (var btn in LeftBtns) Remove(btn);
            foreach (var btn in RightBtns) Remove(btn);
            LeftBtns.Clear(); RightBtns.Clear();

            AddBtn(LeftBtns, "ngbh_cas.png", "Create a family", (btn) =>
            {
                var transition = new UITransDialog("cas", () =>
                {
                    GameController.EnterCAS();
                });
            });
            if (mode != 4) AddBtn(LeftBtns, "ngbh_back.png", "Back to the neighborhood", (btn) => PopMode(4));
            AddBtn(LeftBtns, "ngbh_opt.png", "Options", (btn) => UIScreen.GlobalShowDialog(new UIOptionsAlert(), true));

            if (mode != 2 && !moveIn) AddBtn(RightBtns, "ngbh_downt.png", "Downtown", (btn) => PopMode(2));
            if (mode != 3 && !moveIn) AddBtn(RightBtns, "ngbh_vacat.png", "Vacation Island", (btn) => PopMode(3));
            if (mode != 5 && !moveIn) AddBtn(RightBtns, "ngbh_studio.png", "Studio Town", (btn) => PopMode(5));
            if (mode != 7) AddBtn(RightBtns, "ngbh_magic.png", "Magic Town", (btn) => PopMode(7));
            Mode = mode;

            LayBtns();
        }

        public void PopMode(ushort mode)
        {
            Panel.PopulateScreen(mode);
            SetMode(mode, MoveInMode);
        }

        public override void GameResized()
        {
            base.GameResized();
            LayBtns(); //the right-hand buttons sit against the right edge of the screen
        }

        private void LayBtns()
        {
            int i = 0;
            foreach (var btn in LeftBtns)
            {
                btn.Position = new Microsoft.Xna.Framework.Vector2(17+48, 14+110*i + 48);
                i++;
            }

            i = 0;
            foreach (var btn in RightBtns)
            {
                btn.Position = new Microsoft.Xna.Framework.Vector2(UIScreen.Current.ScreenWidth - (17+96) + 48, 14 + 110 * i + 48);
                i++;
            }
        }

        private UIElasticButton AddBtn(List<UIElasticButton> targ, string imgname, string tooltip, ButtonClickDelegate onClick)
        {
            var ui = Content.Get().CustomUI;
            var btn = new UIElasticButton(ui.Get(imgname).Get(GameFacade.GraphicsDevice));
            btn.Tooltip = tooltip;
            btn.OnButtonClick += onClick;
            targ.Add(btn);
            Add(btn);
            return btn;
        }
    }
}
