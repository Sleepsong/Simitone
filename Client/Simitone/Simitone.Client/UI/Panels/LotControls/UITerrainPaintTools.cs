using FSO.Client;
using FSO.Client.UI.Controls.Catalog;
using FSO.Client.UI.Model;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content.Model;
using FSO.HIT;
using FSO.LotView;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.SimAntics.Utils;
using FSO.UI.Panels.LotControls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.LotControls
{
    /// <summary>
    /// Build mode's terrain paint, as in The Sims 2: paint the ground with one of the game's terrain types (grass, dark
    /// or autumn grass, sand, rock, snow, cloud) by dragging a round brush over it.
    /// - + and - change the brush size.
    /// - Shift (the second touch button) gives a soft brush that feathers into what's there.
    /// - Ctrl (the touch erase button), or "Remove Terrain Paint", takes the paint off so the lot's own ground shows.
    /// </summary>
    public class UITerrainPainter : UICustomLotControl
    {
        private VM vm;
        private World World;
        private ILotControl Parent;
        private byte Type;

        private bool Drawing;
        private Point LastPosition = new Point(-100, -100);
        private Dictionary<Point, VMArchitectureCommand> Strokes = new Dictionary<Point, VMArchitectureCommand>();
        private bool Previewing;
        private int LastHash;
        private KeyboardState LastKeys;

        /// <summary>Brush radius in tiles (kept between tools): 0 paints one point.</summary>
        public static int BrushSize = 1;

        public UITerrainPainter(VM vm, World world, ILotControl parent, List<int> parameters)
        {
            this.vm = vm;
            World = parent.World;
            Parent = parent;
            Type = (byte)((parameters != null && parameters.Count > 0) ? parameters[0] : VMTerrainPaintTools.Erase);
        }

        private bool Erasing => Type == VMTerrainPaintTools.Erase || Modifiers.IsSet(UILotControlModifiers.CTRL);
        private bool Soft => Modifiers.IsSet(UILotControlModifiers.SHIFT);

        /// <summary>The ground point nearest the cursor (terrain is only on the ground floor).</summary>
        private Point CursorPoint()
        {
            var tilePos = World.EstTileAtPosWithScroll(new Vector2(MousePosition.X, MousePosition.Y), 1);
            return new Point((int)Math.Round(tilePos.X), (int)Math.Round(tilePos.Y));
        }

        private VMArchitectureCommand Stamp(Point point)
        {
            return new VMArchitectureCommand
            {
                Type = VMArchitectureCommandType.TERRAIN_PAINT,
                x = point.X - 1, //(laid out like the grass state, as the grass tool's dots)
                y = point.Y - 1,
                x2 = BrushSize,
                pattern = Erasing ? VMTerrainPaintTools.Erase : Type,
                style = (ushort)(Soft ? 1 : 0)
            };
        }

        public override void MouseDown(UpdateState state)
        {
            if (Drawing) return;
            HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolDown);
            Strokes.Clear();
            LastPosition = new Point(-100, -100);
            Drawing = true;
        }

        public override void MouseUp(UpdateState state)
        {
            if (!Drawing) return;
            Drawing = false;
            var cost = vm.Context.Architecture.LastTestCost;
            if (Strokes.Count > 0 && cost != 0 && (Parent.ActiveEntity == null || cost <= Parent.Budget))
            {
                vm.SendCommand(new VMNetArchitectureCmd { Commands = Strokes.Values.ToList() });
                HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolPlace);
            }
            else HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolUp);
            Strokes.Clear();
        }

        public override void Update(UpdateState state, bool scrolled)
        {
            //brush size: + and -
            var keys = state.KeyboardState;
            if (Pressed(keys, Keys.OemPlus) || Pressed(keys, Keys.Add)) BrushSize = Math.Min(VMTerrainPaintTools.MaxRadius, BrushSize + 1);
            if (Pressed(keys, Keys.OemMinus) || Pressed(keys, Keys.Subtract)) BrushSize = Math.Max(0, BrushSize - 1);
            LastKeys = keys;

            var cursor = CursorPoint();
            if (Drawing && cursor != LastPosition)
            {
                //a dab of paint wherever the brush goes while the button is down
                Strokes[cursor] = Stamp(cursor);
                LastPosition = cursor;
            }

            //preview: the strokes so far, or the brush where it would paint
            var cmds = vm.Context.Architecture.Commands;
            cmds.Clear();
            if (Drawing) cmds.AddRange(Strokes.Values);
            else cmds.Add(Stamp(cursor));

            var hash = cmds.Count * 31 + cmds.Aggregate(17, (h, c) => h * 23 + c.GetHashCode());
            if (!Previewing || hash != LastHash)
            {
                vm.Context.Architecture.SignalTerrainRedraw();
                vm.Context.Architecture.SignalRedraw();
                Previewing = true;
                LastHash = hash;
            }

            var cost = vm.Context.Architecture.LastTestCost;
            var size = (BrushSize == 0) ? "smallest brush" : $"brush size {BrushSize}";
            var what = Erasing ? "removing paint" : VMTerrainPaintTools.Name((TerrainType)Type);
            state.UIState.TooltipProperties.Show = true;
            state.UIState.TooltipProperties.Color = (Parent.ActiveEntity != null && cost > Parent.Budget) ? Color.DarkRed : Color.Black;
            state.UIState.TooltipProperties.Opacity = 1;
            state.UIState.TooltipProperties.Position = new Vector2(MousePosition.X, MousePosition.Y);
            state.UIState.Tooltip = (Drawing && cost != 0 ? $"${cost}  " : "") + $"{what}, {size}" + (Soft ? ", soft" : "");
            state.UIState.TooltipProperties.UpdateDead = false;
        }

        private bool Pressed(KeyboardState keys, Keys key) => keys.IsKeyDown(key) && !LastKeys.IsKeyDown(key);

        public override void Release()
        {
            vm.Context.Architecture.Commands.Clear();
            vm.Context.Architecture.SignalTerrainRedraw();
            vm.Context.Architecture.SignalRedraw();
        }
    }

    /// <summary>Names, swatches and prices of the terrain paints in the build catalogue (Terrain).</summary>
    public class UICatalogTerrainPaintResProvider : UICatalogResProvider
    {
        private static Dictionary<ulong, Texture2D> Icons = new Dictionary<ulong, Texture2D>();

        public override Texture2D GetIcon(ulong id)
        {
            Texture2D icon;
            if (!Icons.TryGetValue(id, out icon) || icon.IsDisposed)
            {
                icon = DrawSwatch(GameFacade.GraphicsDevice, (int)id);
                Icons[id] = icon;
            }
            return icon;
        }

        public override Texture2D GetThumb(ulong id) => GetIcon(id);

        public override string GetName(ulong id)
        {
            return (id == VMTerrainPaintTools.Erase) ? "Remove Terrain Paint" : "Paint " + VMTerrainPaintTools.Name((TerrainType)id);
        }

        public override string GetDescription(ulong id)
        {
            const string controls = " Drag to paint; + and - change the brush size, Shift (or the second touch button) makes it soft. Hold Ctrl (or use erase) to take paint off.";
            if (id == VMTerrainPaintTools.Erase) return "Drag over painted ground to take the paint off, so the lot's own ground shows again. + and - change the brush size.";
            return $"Paint the ground with {VMTerrainPaintTools.Name((TerrainType)id).ToLowerInvariant()}, as in The Sims 2." + controls;
        }

        public override int GetPrice(ulong id) => 1;

        public override bool DoDispose() => false;

        /// <summary>A round swatch of the terrain type's colours (a red cross through the lot's grass for removing).</summary>
        private static Texture2D DrawSwatch(GraphicsDevice gd, int id)
        {
            const int size = 64;
            var t = (id == VMTerrainPaintTools.Erase) ? 0 : Math.Min(id, LotTypeGrassInfo.LightGreen.Length - 1);
            var light = LotTypeGrassInfo.LightGreen[t];
            var dark = LotTypeGrassInfo.DarkGreen[t];
            var brown = LotTypeGrassInfo.LightBrown[t];
            var data = new Color[size * size];
            var rand = new Random(id);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var dx = x - 31.5f;
                    var dy = y - 31.5f;
                    var d = (float)Math.Sqrt(dx * dx + dy * dy);
                    Color c = Color.Transparent;
                    if (d < 28)
                    {
                        //speckled like the ground: mostly the light colour, flecks of the dark one and the dry one
                        var r = rand.NextDouble();
                        c = (r < 0.18) ? dark : (r < 0.3) ? brown : light;
                        if (d > 26) c = Color.Lerp(c, Color.Black, 0.35f);
                    }
                    if (id == VMTerrainPaintTools.Erase && d < 28 && (Math.Abs(x - y) < 3 || Math.Abs(x + y - 63) < 3)) c = Color.Red;
                    data[x + y * size] = c;
                }
            }
            var tex = new Texture2D(gd, size, size);
            tex.SetData(data);
            return tex;
        }
    }
}
