using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Content.Framework;
using FSO.Content.Model;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using FSO.Client;
using FSO.Content;
using FSO.Common.Utils;
using Simitone.Client.UI.Controls;
using Simitone.Client.Utils;
using FSO.Common;
using System.IO;
using FSO.Files.RC;

namespace Simitone.Client.UI.Panels
{
    public class UINeighborhoodSelectionPanel : UIContainer
    {

        public static NeighborhoodViewConfig[] Neighborhoods = new NeighborhoodViewConfig[]
        {
            new NeighborhoodViewConfig()
            {
                Graphic = "Nbhd\\NScreen.BMP",
                Scale = 1f,
                FullImageAnimations = new NeighborhoodImageAnim[] {new NeighborhoodImageAnim("Nbhd\\DiffN1-N2_8.bmp", "Nbhd\\DiffN1-N3_8.bmp", "Nbhd\\DiffN1-N4_8.bmp") }
            },
            new NeighborhoodViewConfig()
            {
                Graphic = "Downtown\\DScreen.bmp",
                Music = "station_dtnhood",
                Scale = 1f,
                FullImageAnimations = new NeighborhoodImageAnim[] {new NeighborhoodImageAnim("Downtown\\dscreen00.bmp", "Downtown\\dscreen01.bmp", "Downtown\\dscreen02.bmp", "Downtown\\dscreen03.bmp", "Downtown\\dscreen04.bmp") }
            },
            new NeighborhoodViewConfig()
            {
                Graphic = "VIsland\\visland.bmp",
                Music = "station_vacation",
                Scale = 1f,
                FullImageAnimations = new NeighborhoodImageAnim[] {
                    new NeighborhoodImageAnim("VIsland\\visland_waves001.bmp", "VIsland\\visland_waves002.bmp", "VIsland\\visland_waves003.bmp", "VIsland\\visland_waves004.bmp", "VIsland\\visland_waves005.bmp"),
                    new NeighborhoodImageAnim("VIsland\\visland_port.bmp"),
                    new NeighborhoodImageAnim("VIsland\\visland_trees.bmp"),
                } //TODO: nessie
            },
            new NeighborhoodViewConfig()
            {
                Graphic = "Community\\NScreen_unleashed.bmp",
                Scale = 2f,
                Pulsate = false,
                FullImageAnimations = new NeighborhoodImageAnim[] {new NeighborhoodImageAnim("Community\\NScreen_unleashed_waves001.bmp", "Community\\NScreen_unleashed_waves002.bmp", "Community\\NScreen_unleashed_waves003.bmp", "Community\\NScreen_unleashed_waves004.bmp", "Community\\NScreen_unleashed_waves005.bmp", "Community\\NScreen_unleashed_waves006.bmp") },
                BGSound = "river_loop"
            },
            new NeighborhoodViewConfig()
            {
                Graphic = "Studiotown\\DScreen.bmp",
                Music = "station_superstar",
                Scale = 1f,
                FullImageAnimations = new NeighborhoodImageAnim[] {new NeighborhoodImageAnim("Studiotown\\DScreen_top_layer.bmp") } //TODO: cars
            },
            new NeighborhoodViewConfig(),
            new NeighborhoodViewConfig()
            {
                Graphic = "Magicland\\DScreen.bmp",
                Pulsate = false,
                Music = "music_magictown",
                BGSound = "mt_river_loop",
                Scale = 1f,
                FullImageAnimations = new NeighborhoodImageAnim[] {new NeighborhoodImageAnim(new Vector2(0, 62), "Magicland\\DScreen_waves1.bmp", "Magicland\\DScreen_waves2.bmp", "Magicland\\DScreen_waves3.bmp", "Magicland\\DScreen_waves4.bmp", "Magicland\\DScreen_waves5.bmp", "Magicland\\DScreen_waves6.bmp") } //todo: blimp
            },
        };

        public TS1Provider Provider;
        public event Action<int> OnHouseSelect;
        public HITSound BgSound;
        public Dictionary<int, Vector2> HousePositions;
        public Dictionary<int, UINeighborhoodHouseButton> HouseButtons = new Dictionary<int, UINeighborhoodHouseButton>();
        public int LastHouse = -1; //the house last selected, where gamepad focus returns

        private Vector2 _cp = new Vector2(800, 600) / 2;
        public float CenterPositionX
        {
            get
            {
                return _cp.X;
            }
            set
            {
                _cp.X = value;
            }
        }

        public float CenterPositionY
        {
            get
            {
                return _cp.Y;
            }
            set
            {
                _cp.Y = value;
                UpdatePosition();
            }
        }

        private float _z = 1f;
        public float Zoom
        {
            get
            {
                return _z;
            }
            set
            {
                _z = value;
            }
        }

        public UINeighborhoodSelectionPanel(ushort mode)
        {
            Provider = Content.Get().TS1Global;
            PopulateScreen(mode);
            GameResized();
        }

        public void UpdatePosition()
        {
            base.GameResized();
            //the map is 800x600: fit it to the screen whichever way is tighter (narrower than 4:3 is width)
            var scale = Math.Min(GlobalSettings.Default.GraphicsHeight / 600.0f, GlobalSettings.Default.GraphicsWidth / 800.0f);
            ScaleX = ScaleY = scale * Zoom;

            X = (GlobalSettings.Default.GraphicsWidth) / 2 - _cp.X * ScaleX;
            Y = (GlobalSettings.Default.GraphicsHeight) / 2 - _cp.Y * ScaleY;
        }

        public override void GameResized()
        {
            UpdatePosition();
            //the house panel is laid out for the screen size it was opened at: open it again at the new size
            if (LastHS != null) GameThread.NextUpdate((state) => ReopenHousePanel());
        }

        private void ReopenHousePanel()
        {
            if (LastHS == null) return;
            var house = LastHS.HouseID;
            LastHS.Parent?.Remove(LastHS);
            LastHS = null;
            OpenHousePanel(house);
        }

        private Texture2D texture;
        private int Mode;

        public void PopulateScreen(ushort mode)
        {
            var childClone = new List<UIElement>(Children);
            var config = Neighborhoods[mode - 1];
            foreach (var child in childClone) Remove(child);

            var shad = new UIImage(Content.Get().CustomUI.Get("ngbh_outline.png").Get(GameFacade.GraphicsDevice))
                .With9Slice(24, 24, 24, 24);
            shad.SetSize(800 + 48, 600 + 48);
            shad.Position = new Vector2(-24);
            Add(shad);

            var bgTex = ((ITextureRef)Provider.Get(config.Graphic)).Get(GameFacade.GraphicsDevice);
            var bg = new UIImage(bgTex);
            Add(bg);
            //the original game ran at the map's size, so lots past its edges were simply off screen: clip them to it
            var mapBounds = new Rectangle(0, 0, bgTex.Width, bgTex.Height);
            bg.ListenForMouse((evt, state) =>
            {
                if (evt == UIMouseEventType.MouseDown) ResetZoom();
            });

            HousePositions = new Dictionary<int, Vector2>();
            HouseButtons = new Dictionary<int, UINeighborhoodHouseButton>();
            LastHouse = -1;
            var locationIff = Content.Get().Neighborhood.LotLocations;
            var locations = locationIff.Get<STR>(mode);
            if (locations == null) return;

            var buttons = new List<UINeighborhoodHouseButton>();

            for (int i = 0; i < locations.Length; i++)
            {
                var loc = locations.GetString(i).Split(',');
                var num = int.Parse(loc[0].TrimStart());
                if (num == 99) continue;
                var button = new UINeighborhoodHouseButton(num, SelectHouse, config.Scale);
                button.Position = new Vector2(int.Parse(loc[1].TrimStart()), int.Parse(loc[2].TrimStart()));
                button.MapBounds = mapBounds;
                HousePositions[num] = button.Position;
                HouseButtons[num] = button;
                buttons.Add(button);
            }

            var ordered = buttons.OrderBy(x => x.Y);
            foreach (var btn in ordered) Add(btn);

            foreach (var layer in config.FullImageAnimations)
            {
                var lelem = new UINeighborhoodAnimationLayer(layer, config.Pulsate, config.FrameDuration);
                lelem.Position = layer.Position;
                Add(lelem);
            }

            BgSound?.RemoveOwner(-25);
            if (config.Music != null) FSO.HIT.HITVM.Get().PlaySoundEvent(config.Music);
            if (config.BGSound != null)
            {
                BgSound = HITVM.Get().PlaySoundEvent(config.BGSound);
                BgSound?.AddOwner(-25); // null when sound is disabled (-nosound)
            }
            Mode = mode;
            Zoom = Zoom;
            CenterPositionX = CenterPositionX;
            CenterPositionY = CenterPositionY;
            //SimitoneNeighOBJExporter.SaveOBJ(Path.Combine(FSOEnvironment.UserDir, "NeighModel" + mode + "/"), locations);
        }

        public void ResetZoom()
        {
            if (LastHS == null) return;
            LastHS.Kill();
            LastHS = null;
            GameFacade.Screens.Tween.To(this, 0.5f, new Dictionary<string, float>() { { "Zoom", 1f }, { "CenterPositionX", 400 }, { "CenterPositionY", 300 } }, TweenQuad.EaseOut);
        }

        public UIHouseSelectPanel LastHS;

        public void SelectHouse(int house)
        {
            LastHouse = house;
            if (LastHS != null && LastHS.HouseID == house)
            {
                ResetZoom();
            }
            else
            {
                LastHS?.Kill();
                OpenHousePanel(house);
            }
        }

        private void OpenHousePanel(int house)
        {
            LastHS = new UIHouseSelectPanel(house);
            GameFacade.Screens.CurrentUIScreen.Add(LastHS);
            GameFacade.Screens.Tween.To(this, 0.5f, new Dictionary<string, float>() {
                { "Zoom", (Mode==4)?3f:1.5f },
                { "CenterPositionX", HousePositions[house].X - ((Mode==4)?90f:180f) },
                { "CenterPositionY", HousePositions[house].Y } }, TweenQuad.EaseOut);

            LastHS.OnClose += ResetZoom;
            LastHS.OnSelected += (h) =>
            {
                OnHouseSelect?.Invoke(h);
                //HITVM.Get().PlaySoundEvent("bkground_fade");
            };
        }

        public override void Removed()
        {
            base.Removed();
            BgSound?.RemoveOwner(-25);
        }

        public override void Draw(UISpriteBatch batch)
        {
            base.Draw(batch);
            //DrawLocalTexture(batch, texture, new Vector2());
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
        }
    }

    public class UINeighborhoodAnimationLayer : UIElement
    {

        public Texture2D[] Frames;
        public int FrameNum;
        public int SubFrame;
        public int FrameTime;
        private int TotalFrames;
        public NeighborhoodImageAnim Anim;

        public UINeighborhoodAnimationLayer(NeighborhoodImageAnim anim, bool pulsate, int frameTime)
        {
            var provider = Content.Get().TS1Global;
            Frames = anim.Frames.Select(x => ((ITextureRef)provider.Get(x)).Get(GameFacade.GraphicsDevice)).ToArray();
            SubFrame = frameTime;
            FrameTime = frameTime;
            FrameTime *= GlobalSettings.Default.TargetRefreshRate;
            FrameTime /= 60;
            TotalFrames = pulsate ? (Frames.Length * 2 - 2) : Frames.Length;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (--SubFrame <= 0)
            {
                SubFrame = FrameTime;
                FrameNum++;
                if (TotalFrames != 0) FrameNum %= TotalFrames;
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            var realFrame = (FrameNum >= Frames.Length) ? ((Frames.Length - 2) - (FrameNum - Frames.Length)) : FrameNum;
            var fm2 = (TotalFrames != 0) ? ((FrameNum + 1) % TotalFrames) : 0;
            var realFrame2 = (fm2 >= Frames.Length) ? ((Frames.Length - 2) - (fm2 - Frames.Length)) : fm2;
            DrawLocalTexture(batch, Frames[Math.Max(0, realFrame)], Vector2.Zero);
            DrawLocalTexture(batch, Frames[Math.Max(0, realFrame2)], null, Vector2.Zero, Vector2.One, Color.White * (1 - ((float)SubFrame / FrameTime)));
        }
    }

    public class NeighborhoodViewConfig
    {
        public string Graphic;
        public float Scale;
        public NeighborhoodImageAnim[] FullImageAnimations = new NeighborhoodImageAnim[0];
        public int FrameDuration = 15;
        public bool Pulsate = true;
        public string Music = "bkground_nhood1";
        public string BGSound;
    }

    public class NeighborhoodImageAnim
    {
        public string[] Frames;
        public Vector2 Position;

        public NeighborhoodImageAnim(params string[] frames)
        {
            Frames = frames;
        }

        public NeighborhoodImageAnim(Vector2 position, params string[] frames) : this(frames)
        {
            Position = position;
        }
    }

    public class UINeighborhoodHouseButton : UIElement, IPadNavigable, Simitone.Client.UI.Model.IPadLabelCoverable
    {
        private Texture2D HouseTex;
        private Texture2D HouseOpenTex;
        private float HouseScale;
        private bool Hovered;
        private THMB Offsets;
        public float AlphaTime { get; set; }

        private readonly int HouseNumber;
        private readonly Action<int> SelectionCallback;
        private Rectangle HitRect; //what the mouse reacts to: a band across the middle of the lot picture
        private Rectangle DrawRect; //the lot picture as drawn

        /// <summary>The map picture's rectangle in the map's coordinates (this button's parent); the lot is clipped to it.</summary>
        public Rectangle MapBounds;

        public bool PadFocusable => Visible;

        public int House => HouseNumber;
        /// <summary>The mouse (or the pad's virtual cursor) is over the lot.</summary>
        public bool MouseHovered => Hovered;
        /// <summary>A family lives here.</summary>
        public readonly bool Occupied;
        /// <summary>A community lot (downtown, vacation, studio town...), as the house panel decides it.</summary>
        public readonly bool Community;

        /// <summary>What Confirm does here, for the prompt bar: what the house panel it opens is for.</summary>
        public string PadLabel(bool movingIn)
        {
            if (Occupied) return movingIn ? "View house" : "Visit house";
            if (Community) return movingIn ? "View lot" : "Visit lot";
            return movingIn ? "Move in" : "View lot";
        }

        /// <summary>The focus frame goes round the lot picture, as far as it's on the map.</summary>
        public Rectangle PadBounds
        {
            get
            {
                if (MapBounds.IsEmpty) return DrawRect;
                var onMap = new Rectangle(DrawRect.X + (int)X, DrawRect.Y + (int)Y, DrawRect.Width, DrawRect.Height);
                onMap = Rectangle.Intersect(onMap, MapBounds);
                if (onMap.Width <= 0 || onMap.Height <= 0) return DrawRect;
                return new Rectangle(onMap.X - (int)X, onMap.Y - (int)Y, onMap.Width, onMap.Height);
            }
        }
        public bool PadMove(PadDirection direction) => false;

        public void PadActivate()
        {
            HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.NeighborhoodClick);
            SelectionCallback(HouseNumber);
        }

        /// <summary>Gamepad focus: shows the house opened up, as hovering with the mouse does.</summary>
        public void SetPadHover(bool hover)
        {
            GameFacade.Screens.Tween.To(this, 0.5f, new Dictionary<string, float>() { { "AlphaTime", hover ? 1f : 0f } });
            if (hover) HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.NeighborhoodRollover);
        }

        /// <summary>"Lot name - Family name" for the gamepad focus tooltip (the street address if the lot has no name).</summary>
        private static string HouseTitle(int houseNumber)
        {
            try
            {
                var neigh = Content.Get().Neighborhood;
                var name = neigh.GetHouseNameDesc(houseNumber).Item1;
                if (string.IsNullOrEmpty(name))
                {
                    var assignment = neigh.StreetNames.Get<STR>(2001)?.GetString(houseNumber - 1);
                    if (int.TryParse(assignment, out var street))
                        name = neigh.StreetNames.Get<STR>(2000).GetString(street - 1).Replace("%s", houseNumber.ToString());
                }
                name = name?.Trim().TrimEnd(':').TrimEnd(); //some lot names end in a colon ("Reclaimed Public Beach:")
                var family = neigh.GetFamilyForHouse((short)houseNumber);
                var familyName = family?.ChunkParent.Get<FAMs>(family.ChunkID)?.GetString(0);
                if (!string.IsNullOrEmpty(familyName)) name = string.IsNullOrEmpty(name) ? familyName : name + " - " + familyName;
                return name;
            }
            catch
            {
                return null; //a missing string isn't worth failing the neighbourhood over
            }
        }

        public UINeighborhoodHouseButton(int houseNumber, Action<int> selectionCallback, float scale)
        {
            HouseNumber = houseNumber;
            SelectionCallback = selectionCallback;
            Tooltip = HouseTitle(houseNumber);
            try
            {
                var neigh = Content.Get().Neighborhood;
                Occupied = neigh.GetFamilyForHouse((short)houseNumber) != null;
                //as UIHouseSelectPanel works it out
                if (!neigh.ZoningDictionary.TryGetValue((short)houseNumber, out var zone))
                    zone = (short)((houseNumber >= 81 && houseNumber <= 89) ? 2 : 1);
                Community = !Occupied && zone > 0;
            }
            catch { } //only for prompt labels
            if (houseNumber == 71) { }
            AlphaTime = 0;
            var house = Content.Get().Neighborhood.GetHouse(houseNumber);
            HouseTex = house.Get<BMP>(513)?.GetTexture(GameFacade.GraphicsDevice);
            if (HouseTex != null) {
                HouseOpenTex = house.Get<BMP>(512).GetTexture(GameFacade.GraphicsDevice);
                Offsets = house.Get<THMB>(512); //get offsets before scaling
            } else
            {
                HouseTex = house.Get<PNG>(513).GetTexture(GameFacade.GraphicsDevice);
                HouseOpenTex = house.Get<PNG>(512).GetTexture(GameFacade.GraphicsDevice);
                Offsets = new THMB() { Width = HouseTex.Width / 2, Height = HouseTex.Height / 2 };
            }

            HouseScale = scale;

            var w = (int)(HouseTex.Width / HouseScale);
            var h = (int)(HouseTex.Height / HouseScale);
            HitRect = new Rectangle(w / -2, w / -4, w, h / 2);
            //as Draw places the closed picture
            DrawRect = new Rectangle((int)((Offsets.XOff - Offsets.Width) / HouseScale), (int)((-Offsets.BaseYOff - Offsets.Height) / HouseScale), w, h);
            var clickHandler =
                ListenForMouse(HitRect, (evt, state) =>
                {
                    switch (evt)
                    {
                        case UIMouseEventType.MouseUp:
                            HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.NeighborhoodClick);
                            selectionCallback(houseNumber); break;
                        case UIMouseEventType.MouseOver:
                            GameFacade.Screens.Tween.To(this, 0.5f, new Dictionary<string, float>() { { "AlphaTime", 1f } });
                            HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.NeighborhoodRollover);
                            Hovered = true; break;
                        case UIMouseEventType.MouseOut:
                            GameFacade.Screens.Tween.To(this, 0.5f, new Dictionary<string, float>() { { "AlphaTime", 0f } });
                            Hovered = false; break;
                    }
                });
        }

        public override void Draw(UISpriteBatch batch)
        {
            var yOff = new Vector2(Offsets.XOff, -Offsets.BaseYOff) / HouseScale;
            var yOff2 = yOff;
            yOff2.Y -= Offsets.AddYOff / (HouseScale);
            if (AlphaTime > 0)
            {
                DrawOnMap(batch, HouseOpenTex, new Vector2(-Offsets.Width, -Offsets.Height) / HouseScale + yOff2, 1f / HouseScale, Color.White);
            }
            DrawOnMap(batch, HouseTex, new Vector2(-Offsets.Width, -Offsets.Height) / HouseScale + yOff, 1f / HouseScale, Color.White * (1 - AlphaTime));
        }

        /// <summary>Draws a texture at a local position and scale, cropped to <see cref="MapBounds"/>.</summary>
        private void DrawOnMap(UISpriteBatch batch, Texture2D tex, Vector2 local, float scale, Color color)
        {
            if (tex == null) return;
            if (MapBounds.IsEmpty)
            {
                DrawLocalTexture(batch, tex, null, local, new Vector2(scale), color);
                return;
            }
            //in the map's coordinates
            var left = X + local.X;
            var top = Y + local.Y;
            var right = left + tex.Width * scale;
            var bottom = top + tex.Height * scale;
            var cl = Math.Max(left, MapBounds.Left);
            var ct = Math.Max(top, MapBounds.Top);
            var cr = Math.Min(right, MapBounds.Right);
            var cb = Math.Min(bottom, MapBounds.Bottom);
            if (cr <= cl || cb <= ct) return;
            if (cl == left && ct == top && cr == right && cb == bottom)
            {
                DrawLocalTexture(batch, tex, null, local, new Vector2(scale), color);
                return;
            }
            var sx = (int)Math.Floor((cl - left) / scale);
            var sy = (int)Math.Floor((ct - top) / scale);
            var sw = Math.Min(tex.Width - sx, (int)Math.Ceiling((cr - cl) / scale));
            var sh = Math.Min(tex.Height - sy, (int)Math.Ceiling((cb - ct) / scale));
            if (sw <= 0 || sh <= 0) return;
            DrawLocalTexture(batch, tex, new Rectangle(sx, sy, sw, sh), local + new Vector2(sx, sy) * scale, new Vector2(scale), color);
        }

        public override void Removed()
        {
            HouseTex?.Dispose();
            HouseOpenTex?.Dispose();
        }
    }
}
