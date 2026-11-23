using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Network.Coop;
using Simitone.Client.UI.Panels;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// Draws the other co-op players' mouse pointers over the neighbourhood, each in its own colour with the
    /// player's name beside it. Takes no input.
    /// </summary>
    public class UICoopPointers : UIContainer
    {
        /// <summary>The neighbourhood view the pointers are on. Their positions are in its picture's coordinates.</summary>
        public UINeighborhoodSelectionPanel Panel;

        //an arrow like the system's: X is the outline, . is filled with the player's colour
        private static readonly string[] Arrow =
        {
            "X           ",
            "XX          ",
            "X.X         ",
            "X..X        ",
            "X...X       ",
            "X....X      ",
            "X.....X     ",
            "X......X    ",
            "X.......X   ",
            "X........X  ",
            "X.........X ",
            "X......XXXXX",
            "X...X..X    ",
            "X..X X..X   ",
            "X.X  X..X   ",
            "XX    X..X  ",
            "X     X..X  ",
            "       XX   "
        };
        private const float ArrowScale = 1.5f;

        private static readonly Color[] Colours =
        {
            new Color(90, 170, 255),  //the host
            new Color(255, 185, 60),
            new Color(120, 225, 130),
            new Color(245, 130, 205),
            new Color(190, 160, 255),
            new Color(255, 120, 110)
        };

        private static Texture2D ArrowFill;
        private static Texture2D ArrowOutline;

        private class Shown
        {
            public UILabel Label;
            public UILabel Shadow;
            public Vector2 Position;
            public Color Colour;
        }
        private Dictionary<uint, Shown> Pointers = new Dictionary<uint, Shown>();

        public UICoopPointers()
        {
            if (ArrowFill == null)
            {
                //two layers, so the fill can be tinted while the outline stays dark
                ArrowFill = MakeLayer('.');
                ArrowOutline = MakeLayer('X');
            }
        }

        private static Texture2D MakeLayer(char which)
        {
            var width = Arrow[0].Length;
            var height = Arrow.Length;
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = (Arrow[y][x] == which) ? Color.White : Color.Transparent;
                }
            }
            var texture = new Texture2D(GameFacade.GraphicsDevice, width, height);
            texture.SetData(pixels);
            return texture;
        }

        public static Color ColourFor(uint player)
        {
            if (player == CoopProtocol.HostPlayerID) return Colours[0];
            return Colours[1 + (int)((player - CoopProtocol.FirstGuestID) % (uint)(Colours.Length - 1))];
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);

            //labels come and go with the players whose pointers are on the neighbourhood
            foreach (var gone in Pointers.Keys.Where(x => !CoopManager.Pointers.ContainsKey(x)).ToList())
            {
                Remove(Pointers[gone].Shadow);
                Remove(Pointers[gone].Label);
                Pointers.Remove(gone);
            }
            if (Panel == null) return;

            foreach (var pointer in CoopManager.Pointers.Values)
            {
                if (!Pointers.TryGetValue(pointer.Player, out var shown))
                {
                    shown = new Shown() { Colour = ColourFor(pointer.Player) };
                    shown.Shadow = MakeLabel(Color.Black * 0.7f);
                    shown.Label = MakeLabel(Color.White);
                    Pointers[pointer.Player] = shown;
                }
                if (shown.Label.Caption != pointer.Name)
                {
                    shown.Label.Caption = pointer.Name;
                    shown.Shadow.Caption = pointer.Name;
                }
                //from the picture's coordinates to the screen's, then to ours
                shown.Position = GlobalPoint(Panel.LocalPoint(new Vector2(pointer.X, pointer.Y)));
                shown.Label.Position = shown.Position + new Vector2(20, 22);
                shown.Shadow.Position = shown.Label.Position + new Vector2(1, 1);
            }
        }

        private UILabel MakeLabel(Color colour)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = 12;
            label.CaptionStyle.Color = colour;
            label.Alignment = TextAlignment.Left | TextAlignment.Top;
            label.Size = new Vector2(240, 20);
            Add(label);
            return label;
        }

        public override void Draw(UISpriteBatch batch)
        {
            foreach (var shown in Pointers.Values)
            {
                DrawLocalTexture(batch, ArrowOutline, null, shown.Position, new Vector2(ArrowScale), new Color(20, 20, 20));
                DrawLocalTexture(batch, ArrowFill, null, shown.Position, new Vector2(ArrowScale), shown.Colour);
            }
            base.Draw(batch); //the names
        }
    }
}
