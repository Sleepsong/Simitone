using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Content;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Simitone.Client.UI.Controls
{
    public class UIBigButton : UIButton
    {
        public UIBigButton(bool green) : base()
        {
            CaptionStyle = CaptionStyle.Clone();
            CaptionStyle.Size = 37;
            CaptionStyle.Color = (green)?UIStyle.Current.GreenBtnTxt:UIStyle.Current.BtnTxt;
            //on hover the caption shifts towards Simitone's green, as the icon buttons do. (It used to keep the default
            //white, which all but vanished against the paler hover image.)
            CaptionStyle.HighlightedColor = Color.Lerp(CaptionStyle.Color, UIStyle.Current.BtnActive, 0.35f);
            CaptionStyle.DisabledColor = UIStyle.Current.BtnDisable;
            //pressed keeps the normal colour (the default white vanishes on these white buttons; the texture has its own
            //pressed frame). Hover keeps the tint above: it's also how the gamepad focus shows on these buttons.
            CaptionStyle.SelectedColor = CaptionStyle.Color;
            Texture = Content.Get().CustomUI.Get(green ? "greenbutton.png" : "button.png").Get(GameFacade.GraphicsDevice);
        }
    }
}
