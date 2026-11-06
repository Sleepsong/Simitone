using FSO.Client.UI.Controls.Catalog;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content;
using FSO.Content.Model;
using FSO.HIT;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.SimAntics.Utils;
using FSO.UI.Panels.LotControls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.LotControls
{
    /// <summary>
    /// Shared bits of the foundation tools: previewing a command while the tool moves, showing its price, and
    /// sending it.
    /// </summary>
    public abstract class UIFoundationToolBase : UICustomLotControl
    {
        protected VM vm;
        protected World World;
        protected ILotControl Parent;
        private VMArchitectureCommand LastCmd;
        private bool Previewing;

        protected UIFoundationToolBase(VM vm, World world, ILotControl parent)
        {
            this.vm = vm;
            World = parent.World;
            Parent = parent;
        }

        protected bool Erasing => Modifiers.IsSet(UILotControlModifiers.CTRL);

        /// <summary>The tile under the mouse (on the ground floor: foundations are only built there).</summary>
        protected Point CursorTile()
        {
            var tilePos = World.EstTileAtPosWithScroll(new Vector2(MousePosition.X, MousePosition.Y));
            return new Point((int)Math.Floor(tilePos.X), (int)Math.Floor(tilePos.Y));
        }

        /// <summary>Shows what <paramref name="cmd"/> would do (or nothing, for null), with its price by the mouse.</summary>
        protected void Preview(UpdateState state, VMArchitectureCommand? cmd)
        {
            var cmds = vm.Context.Architecture.Commands;
            cmds.Clear();
            if (cmd == null)
            {
                if (Previewing)
                {
                    vm.Context.Architecture.SignalRedraw();
                    Previewing = false;
                }
                state.UIState.TooltipProperties.Show = false;
                state.UIState.TooltipProperties.Opacity = 0;
                return;
            }
            cmds.Add(cmd.Value);
            if (!Previewing || !cmd.Value.Equals(LastCmd))
            {
                vm.Context.Architecture.SignalRedraw();
                Previewing = true;
            }

            var cost = vm.Context.Architecture.LastTestCost;
            var failed = vm.Context.Architecture.LastFailReason;
            if (cost != 0 || failed >= VMFoundationTools.FailBlocked)
            {
                var disallowed = (Parent.ActiveEntity != null && cost > Parent.Budget) || (cost == 0 && failed != 0);
                state.UIState.TooltipProperties.Show = true;
                state.UIState.TooltipProperties.Color = disallowed ? Color.DarkRed : Color.Black;
                state.UIState.TooltipProperties.Opacity = 1;
                state.UIState.TooltipProperties.Position = new Vector2(MousePosition.X, MousePosition.Y);
                state.UIState.Tooltip = (cost == 0) ? FailText(failed) : (cost < 0) ? ("-$" + (-cost)) : ("$" + cost);
                state.UIState.TooltipProperties.UpdateDead = false;
                if (!cmd.Value.Equals(LastCmd) && disallowed) HITVM.Get().PlaySoundEvent(UISounds.Error);
            }
            else
            {
                state.UIState.TooltipProperties.Show = false;
                state.UIState.TooltipProperties.Opacity = 0;
            }
            LastCmd = cmd.Value;
        }

        private static string FailText(int reason)
        {
            switch (reason)
            {
                case VMFoundationTools.FailBlocked: return "Clear furniture and walls first";
                case VMFoundationTools.FailNoFoundation: return "Steps go beside a foundation";
                case VMBasementTools.FailBlocked: return "Clear bare ground of things first (and no pools or foundations)";
                case VMBasementTools.FailOccupied: return "Move everything out of that part of the basement first";
                default: return "";
            }
        }

        /// <summary>Does <paramref name="cmd"/> for real, if it changes anything and can be paid for.</summary>
        protected void Send(VMArchitectureCommand cmd)
        {
            var arch = vm.Context.Architecture;
            var cost = arch.LastTestCost;
            if (cost != 0 && (Parent.ActiveEntity == null || cost <= Parent.Budget))
            {
                vm.SendCommand(new VMNetArchitectureCmd { Commands = new List<VMArchitectureCommand> { cmd } });
                HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolPlace);
            }
            else HITVM.Get().PlaySoundEvent((cost == 0) ? UISounds.Error : UISounds.BuildDragToolUp);
        }

        public override void Release()
        {
            vm.Context.Architecture.Commands.Clear();
            vm.Context.Architecture.SignalRedraw();
        }

        /// <summary>
        /// The wall covering new foundations get on their sides: a brick or stone wall pattern if the game has one,
        /// else the first pattern.
        /// </summary>
        public static ushort DefaultCovering()
        {
            try
            {
                var walls = Content.Get().WorldWalls.List().OfType<WallReference>().Where(w => w.ID != 0).ToList();
                foreach (var word in new[] { "brick", "stone", "block", "concrete" })
                {
                    var match = walls.FirstOrDefault(w => (w.Name ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) return (ushort)match.ID;
                }
                return (ushort)(walls.FirstOrDefault()?.ID ?? 0);
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Build mode's foundation tool: drag out a rectangle of tiles to raise them onto a foundation, as in The Sims 2
    /// (Ctrl, or the touch erase button, to remove one). The ground must be clear of furniture and walls.
    /// </summary>
    public class UIFoundationPlacer : UIFoundationToolBase
    {
        private ushort Covering;
        private bool Drawing;
        private Point Start;
        private Point End;

        public UIFoundationPlacer(VM vm, World world, ILotControl parent, List<int> parameters) : base(vm, world, parent)
        {
            Covering = (parameters != null && parameters.Count > 0 && parameters[0] > 0) ? (ushort)parameters[0] : DefaultCovering();
        }

        private VMArchitectureCommand Command(Point a, Point b)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            return new VMArchitectureCommand
            {
                Type = VMArchitectureCommandType.FOUNDATION_RECT,
                x = x,
                y = y,
                x2 = Math.Abs(a.X - b.X),
                y2 = Math.Abs(a.Y - b.Y),
                level = 1,
                style = (ushort)(Erasing ? 0 : FoundationMap.DefaultHeight),
                pattern = Covering
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
            Send(Command(Start, End));
        }

        public override void Update(UpdateState state, bool scrolled)
        {
            var cursor = CursorTile();
            if (Drawing) End = cursor;
            // (previews the one tile under the mouse until a drag starts)
            Preview(state, Drawing ? Command(Start, End) : Command(cursor, cursor));
        }
    }

    /// <summary>
    /// Build mode's foundation steps: click a tile of ground beside a foundation to put steps up onto it (Ctrl, or the
    /// touch erase button, to take steps away).
    /// </summary>
    public class UIFoundationStepsPlacer : UIFoundationToolBase
    {
        public UIFoundationStepsPlacer(VM vm, World world, ILotControl parent, List<int> parameters) : base(vm, world, parent)
        {
        }

        private VMArchitectureCommand Command(Point tile)
        {
            return new VMArchitectureCommand
            {
                Type = VMArchitectureCommandType.FOUNDATION_STEPS,
                x = tile.X,
                y = tile.Y,
                level = 1,
                pattern = 255,
                style = (ushort)(Erasing ? 0 : 1)
            };
        }

        public override void MouseDown(UpdateState state)
        {
            HITVM.Get().PlaySoundEvent(UISounds.BuildDragToolDown);
        }

        public override void MouseUp(UpdateState state)
        {
            Send(Command(CursorTile()));
        }

        public override void Update(UpdateState state, bool scrolled)
        {
            Preview(state, Command(CursorTile()));
        }
    }

    /// <summary>Names, icons and prices of the foundation tools in the build catalogue (Terrain).</summary>
    public class UICatalogFoundationResProvider : UICatalogResProvider
    {
        public const ulong FoundationID = 100;
        public const ulong StepsID = 101;
        public const ulong BasementID = 102;

        public override Texture2D GetIcon(ulong id)
        {
            if (id == BasementID) return UIElement.GetTexture(0x1E000000001); //down
            return (id == StepsID) ? UIElement.GetTexture(0x1E200000001) : UIElement.GetTexture(0x1E100000001); //up, level
        }

        public override Texture2D GetThumb(ulong id)
        {
            if (id == BasementID) return UIElement.GetTexture(0x1D700000001); //terrain down
            return (id == StepsID) ? UIElement.GetTexture(0x1D900000001) : UIElement.GetTexture(0x1D800000001);
        }

        public override string GetName(ulong id)
        {
            if (id == BasementID) return "Basement";
            return (id == StepsID) ? "Foundation Steps" : "Foundation";
        }

        public override string GetDescription(ulong id)
        {
            if (id == BasementID)
                return "Digs out a basement a storey below the ground floor, as in The Sims 2. Drag out an area; it gets a floor, and so does the ground floor above it. Then go down a floor to build walls and put stairs in. Hold Ctrl (or use erase) to fill it back in.";
            return (id == StepsID)
                ? "Steps up onto a foundation. Click the ground beside one; Sims can only get on and off a foundation by its steps. Hold Ctrl (or use erase) to remove steps."
                : "Raises the ground floor a few steps, as in The Sims 2. Drag out an area of clear ground, then build on it; add steps so Sims can get up. Hold Ctrl (or use erase) to remove a foundation.";
        }

        public override int GetPrice(ulong id)
        {
            if (id == BasementID) return VMArchitecture.BasementTilePrice;
            return (id == StepsID) ? VMArchitecture.FoundationStepsPrice : VMArchitecture.FoundationTilePrice;
        }

        public override bool DoDispose()
        {
            return false;
        }
    }
}
