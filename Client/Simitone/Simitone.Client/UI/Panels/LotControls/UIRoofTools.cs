using FSO.Client;
using FSO.Client.UI.Controls.Catalog;
using FSO.Client.UI.Model;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
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

namespace Simitone.Client.UI.Panels.LotControls
{
    /// <summary>
    /// Build mode's roof tool, as in The Sims 2: drag out a roof on top of the walls of the floor you're looking at.
    /// - Hip: slopes on all sides.
    /// - Gable: the ridge runs along the long side (Shift: along the short side), with upright ends.
    /// - Shed: one slope, low on the side you drag towards (Shift: high there).
    /// Ctrl (or the touch erase button) removes the roof under the cursor. + and - change the pitch.
    /// The first roof drawn on a lot replaces the automatic roof; "Automatic Roof" brings it back.
    /// </summary>
    public class UIRoofTool : UICustomLotControl
    {
        private VM vm;
        private World World;
        private ILotControl Parent;
        private RoofPieceType Type;
        private bool EraseOnly;
        private bool Drawing;
        private Point Start;
        private Point End;
        private VMArchitectureCommand LastCmd;
        private bool Previewing;
        private bool OldDrawRoofs;
        private KeyboardState LastKeys;

        /// <summary>Pitch of the next roof, in percent (kept between tools).</summary>
        public static int Pitch = RoofPiece.DefaultPitch;

        public UIRoofTool(VM vm, World world, ILotControl parent, List<int> parameters)
        {
            this.vm = vm;
            World = parent.World;
            Parent = parent;
            var mode = (parameters != null && parameters.Count > 0) ? parameters[0] : 0;
            EraseOnly = mode == UICatalogRoofToolResProvider.Remove;
            Type = EraseOnly ? RoofPieceType.Hip : (RoofPieceType)Math.Max(0, Math.Min(2, mode));
            //roofs show while the tool is in use
            OldDrawRoofs = World.State.DrawRoofs;
            World.State.DrawRoofs = true;
        }

        private bool Erasing => EraseOnly || Modifiers.IsSet(UILotControlModifiers.CTRL);
        private bool Alternate => Modifiers.IsSet(UILotControlModifiers.SHIFT);

        /// <summary>The level of a roof drawn now: on top of the walls of the floor being looked at.</summary>
        private sbyte RoofLevel => (sbyte)(World.State.Level + 1);

        /// <summary>The tile under the cursor, at the height of the tops of the walls (where the roof sits).</summary>
        private Point CursorTile()
        {
            var tilePos = World.EstTileAtPosWithScroll(new Vector2(MousePosition.X, MousePosition.Y), RoofLevel);
            return new Point((int)Math.Floor(tilePos.X), (int)Math.Floor(tilePos.Y));
        }

        private VMArchitectureCommand Command(Point a, Point b)
        {
            if (Erasing)
            {
                return new VMArchitectureCommand
                {
                    Type = VMArchitectureCommandType.ROOF_ERASE,
                    x = b.X,
                    y = b.Y,
                    level = RoofLevel
                };
            }
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            var w = Math.Abs(a.X - b.X) + 1;
            var h = Math.Abs(a.Y - b.Y) + 1;
            int dir = 0;
            switch (Type)
            {
                case RoofPieceType.Gable:
                    //ridge along the long side (0: along x), or the short side with Shift
                    var alongX = w >= h;
                    if (Alternate) alongX = !alongX;
                    dir = alongX ? 0 : 1;
                    break;
                case RoofPieceType.Shed:
                    //low on the side the drag went towards (high there with Shift)
                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    if (dx == 0 && dy == 0) dir = 2;
                    else if (Math.Abs(dx) > Math.Abs(dy)) dir = (dx > 0) ? 1 : 3;
                    else dir = (dy > 0) ? 2 : 0;
                    if (Alternate) dir = (dir + 2) % 4;
                    break;
            }
            return new VMArchitectureCommand
            {
                Type = VMArchitectureCommandType.ROOF_RECT,
                x = x,
                y = y,
                x2 = w - 1,
                y2 = h - 1,
                level = RoofLevel,
                pattern = (ushort)Type,
                style = (ushort)(dir | (Pitch << 8))
            };
        }

        public override void MouseDown(UpdateState state)
        {
            if (Drawing) return;
            HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolDown);
            Start = End = CursorTile();
            Drawing = true;
        }

        public override void MouseUp(UpdateState state)
        {
            if (!Drawing) return;
            Drawing = false;
            var cmd = Command(Start, End);
            var cost = vm.Context.Architecture.LastTestCost;
            if (cost != 0 && (Parent.ActiveEntity == null || cost <= Parent.Budget))
            {
                vm.SendCommand(new VMNetArchitectureCmd { Commands = new List<VMArchitectureCommand> { cmd } });
                HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolPlace);
            }
            else HITVM.Get().PlaySoundEvent((cost == 0) ? UISounds.Error : UISounds.BuildDragToolUp);
        }

        public override void Update(UpdateState state, bool scrolled)
        {
            //pitch: + and -
            var keys = state.KeyboardState;
            if (Pressed(keys, Keys.OemPlus) || Pressed(keys, Keys.Add)) Pitch = Math.Min(150, Pitch + 10);
            if (Pressed(keys, Keys.OemMinus) || Pressed(keys, Keys.Subtract)) Pitch = Math.Max(20, Pitch - 10);
            LastKeys = keys;

            World.State.DrawRoofs = true;
            var cursor = CursorTile();
            if (Drawing) End = cursor;
            // (previews a one-tile roof under the cursor until a drag starts)
            Preview(state, Drawing ? Command(Start, End) : Command(cursor, cursor));
        }

        private bool Pressed(KeyboardState keys, Keys key) => keys.IsKeyDown(key) && !LastKeys.IsKeyDown(key);

        private void Preview(UpdateState state, VMArchitectureCommand cmd)
        {
            var arch = vm.Context.Architecture;
            arch.Commands.Clear();
            arch.Commands.Add(cmd);
            if (!Previewing || !cmd.Equals(LastCmd))
            {
                arch.SignalRedraw();
                Previewing = true;
            }

            var cost = arch.LastTestCost;
            var failed = arch.LastFailReason;
            var disallowed = (Parent.ActiveEntity != null && cost > Parent.Budget) || cost == 0;
            string text;
            if (cost != 0) text = ((cost < 0) ? ("-$" + (-cost)) : ("$" + cost)) + (Erasing ? "" : $"  ({Name(Type)}, pitch {Pitch}%)");
            else text = FailText(failed);
            if (text != "")
            {
                state.UIState.TooltipProperties.Show = true;
                state.UIState.TooltipProperties.Color = disallowed ? Color.DarkRed : Color.Black;
                state.UIState.TooltipProperties.Opacity = 1;
                state.UIState.TooltipProperties.Position = new Vector2(MousePosition.X, MousePosition.Y);
                state.UIState.Tooltip = text;
                state.UIState.TooltipProperties.UpdateDead = false;
            }
            else
            {
                state.UIState.TooltipProperties.Show = false;
                state.UIState.TooltipProperties.Opacity = 0;
            }
            LastCmd = cmd;
        }

        public static string Name(RoofPieceType type)
        {
            switch (type)
            {
                case RoofPieceType.Gable: return "gable roof";
                case RoofPieceType.Shed: return "shed roof";
                default: return "hip roof";
            }
        }

        private string FailText(int reason)
        {
            switch (reason)
            {
                case VMRoofTools.FailOutside: return "Roofs must be inside the lot";
                case VMRoofTools.FailNoRoof: return Erasing ? "No roof here on this floor" : "";
                case VMRoofTools.FailTooMany: return "This lot has as many roofs as it can";
                case VMRoofTools.FailLevel: return "No roofs above the top floor";
                default: return "";
            }
        }

        public override void Release()
        {
            vm.Context.Architecture.Commands.Clear();
            vm.Context.Architecture.SignalRedraw();
            World.State.DrawRoofs = OldDrawRoofs;
        }
    }

    /// <summary>
    /// "Automatic Roof" in the catalogue: goes back to the game's automatic roof (drawn roofs are removed and paid
    /// back), straight away, like picking a roof style.
    /// </summary>
    public class UIAutoRoofer : UICustomLotControl
    {
        public UIAutoRoofer(VM vm, World world, ILotControl parent, List<int> parameters)
        {
            if (vm.Context.Architecture.Roofs.AutoRoof && vm.Context.Architecture.Roofs.Pieces.Count == 0)
            {
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                return;
            }
            vm.SendCommand(new VMNetArchitectureCmd
            {
                Commands = new List<VMArchitectureCommand>
                {
                    new VMArchitectureCommand { Type = VMArchitectureCommandType.ROOF_AUTO, style = 1 }
                }
            });
            HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolPlace);
        }

        public override void MouseDown(UpdateState state) { }
        public override void MouseUp(UpdateState state) { }
        public override void Update(UpdateState state, bool scrolled) { }
        public override void Release() { }
    }

    /// <summary>Names, icons and prices of the roof tools in the build catalogue (Roofs).</summary>
    public class UICatalogRoofToolResProvider : UICatalogResProvider
    {
        //resource IDs (also the tools' parameter)
        public const int Hip = 0;
        public const int Gable = 1;
        public const int Shed = 2;
        public const int Remove = 3;
        public const int Auto = 4;

        private static Dictionary<ulong, Texture2D> Icons = new Dictionary<ulong, Texture2D>();

        public override Texture2D GetIcon(ulong id)
        {
            Texture2D icon;
            if (!Icons.TryGetValue(id, out icon) || icon.IsDisposed)
            {
                icon = DrawIcon(GameFacade.GraphicsDevice, (int)id);
                Icons[id] = icon;
            }
            return icon;
        }

        public override Texture2D GetThumb(ulong id) => GetIcon(id);

        public override string GetName(ulong id)
        {
            switch ((int)id)
            {
                case Hip: return "Hip Roof";
                case Gable: return "Gable Roof";
                case Shed: return "Shed Roof";
                case Remove: return "Remove Roof";
                default: return "Automatic Roof";
            }
        }

        public override string GetDescription(ulong id)
        {
            const string common = " It sits on top of the walls of the floor you're looking at; + and - change the pitch. Hold Ctrl (or use erase) to remove a roof. Your first roof replaces the automatic roof.";
            switch ((int)id)
            {
                case Hip: return "Drag out a roof that slopes on all four sides." + common;
                case Gable: return "Drag out a roof with a ridge along its long side and upright gable ends. Hold Shift (or use the second touch button) for a ridge along the short side." + common;
                case Shed: return "Drag out a roof with one slope, low on the side you drag towards. Hold Shift (or use the second touch button) to make that side the high one." + common;
                case Remove: return "Click a roof to remove it (on the floor you're looking at).";
                default: return "Go back to the game's automatic roof. Drawn roofs are removed, and you get their price back.";
            }
        }

        public override int GetPrice(ulong id)
        {
            return ((int)id <= Shed) ? VMRoofTools.TilePrice : 0;
        }

        public override bool DoDispose()
        {
            return false;
        }

        /// <summary>A small drawing of each tool: the roof's outline as seen from the front.</summary>
        private static Texture2D DrawIcon(GraphicsDevice gd, int id)
        {
            const int size = 64;
            var data = new Color[size * size];
            var roof = new Color(170, 70, 50);
            var edge = new Color(90, 35, 25);
            var wall = new Color(236, 226, 205);

            bool InRoof(int x, int y)
            {
                //y grows downwards; roofs sit on a base line at y = 46, between x = 6 and 58
                if (y > 46 || x < 6 || x > 58) return false;
                switch (id)
                {
                    case Gable:
                    case Remove:
                        return y >= 14 + Math.Abs(x - 32) * 32 / 26 - 0 && y <= 46;
                    case Shed:
                        return y >= 14 + (x - 6) * 32 / 52;
                    case Auto:
                    case Hip:
                    default:
                        //a trapezoid: flat top between 22 and 42
                        var d = Math.Max(0, Math.Max(22 - x, x - 42));
                        return y >= 22 + d * 24 / 16;
                }
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color c = Color.Transparent;
                    if (InRoof(x, y))
                    {
                        var border = !InRoof(x - 2, y) || !InRoof(x + 2, y) || !InRoof(x, y - 2) || !InRoof(x, y + 2);
                        c = border ? edge : roof;
                        //upright ends of gable and shed roofs
                        if (!border && id == Gable && y > 34 && x > 26 && x < 38) c = wall;
                        if (!border && id == Shed && x < 14) c = wall;
                    }
                    else if (y > 46 && y < 58 && x > 12 && x < 52) c = wall; //walls under the roof
                    if (id == Remove && (Math.Abs(x - y) < 3 || Math.Abs(x + y - 63) < 3) && x > 8 && x < 56) c = Color.Red;
                    if (id == Auto && x > 24 && x < 40 && y > 50 && y < 56) c = new Color(60, 120, 200);
                    data[x + y * size] = c;
                }
            }
            var tex = new Texture2D(gd, size, size);
            tex.SetData(data);
            return tex;
        }
    }
}
