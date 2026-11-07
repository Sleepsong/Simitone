using FSO.Client.UI.Controls.Catalog;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content;
using FSO.Content.Model;
using FSO.Content.Interfaces;
using FSO.HIT;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Components.Geometry;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.SimAntics.Utils;
using FSO.UI.Panels.LotControls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Panels.LiveSubpanels.Catalog;
using FSO.Files.Formats.IFF.Chunks;
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
    }

    /// <summary>
    /// Build mode's foundation tool: drag out a rectangle of tiles to raise them onto a foundation, as in The Sims 2
    /// (Ctrl, or the touch erase button, to remove one). The ground must be clear of furniture and walls.
    /// </summary>
    public class UIFoundationPlacer : UIFoundationToolBase
    {
        private FoundationKind Kind;
        private bool Drawing;
        private Point Start;
        private Point End;

        /// <param name="parameters">The kind of foundation (see <see cref="FoundationKind"/>): a foundation, a latticed deck, a column deck.</param>
        public UIFoundationPlacer(VM vm, World world, ILotControl parent, List<int> parameters) : base(vm, world, parent)
        {
            Kind = (parameters != null && parameters.Count > 0) ? (FoundationKind)parameters[0] : FoundationKind.Foundation;
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
                style = (ushort)(Erasing ? 0 : (FoundationMap.DefaultHeight | ((int)Kind << 12))),
                pattern = 0 //(its own brick; painted with the wall coverings after)
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
        private uint Style;

        /// <param name="parameters">The staircase (catalog GUID) whose bottom piece the steps are; none for plain steps.</param>
        public UIFoundationStepsPlacer(VM vm, World world, ILotControl parent, List<int> parameters) : base(vm, world, parent)
        {
            Style = (parameters != null && parameters.Count > 0) ? (uint)parameters[0] : 0;
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
                style = (ushort)(Erasing ? 0 : 1),
                x2 = (int)Style
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
        /// <summary>Plain steps: only when the game has no staircase to make steps from.</summary>
        public const ulong StepsID = 101;
        public const ulong BasementID = 102;
        public const ulong LatticedDeckID = 103;
        public const ulong ColumnDeckID = 104;

        /// <summary>The kind of foundation a catalog entry builds.</summary>
        public static FoundationKind KindOf(ulong id)
        {
            if (id == LatticedDeckID) return FoundationKind.LatticedDeck;
            if (id == ColumnDeckID) return FoundationKind.ColumnDeck;
            return FoundationKind.Foundation;
        }

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
            if (id == StepsID) return "Foundation Steps";
            if (id == LatticedDeckID) return "Latticed Deck";
            if (id == ColumnDeckID) return "Column Deck";
            return "Foundation";
        }

        public override string GetDescription(ulong id)
        {
            if (id == BasementID)
                return "Digs out a basement a storey below the ground floor, as in The Sims 2. Drag out an area; it gets a floor, and so does the ground floor above it. Then go down a floor to build walls and put stairs in. Hold Ctrl (or use erase) to fill it back in.";
            if (id == StepsID)
                return "Steps up onto a foundation or deck. Click the ground beside one; Sims can only get on and off by its steps. Hold Ctrl (or use erase) to remove steps.";
            if (id == LatticedDeckID)
                return "The perfect solution for your deck needs, as in The Sims 2: a plank deck with lattice underneath. Drag out an area of clear ground, then build on it; add steps so Sims can get up. Hold Ctrl (or use erase) to remove it.";
            if (id == ColumnDeckID)
                return "Sturdy columns hold up a plank deck, as in The Sims 2. Drag out an area of clear ground, then build on it; add steps so Sims can get up. Hold Ctrl (or use erase) to remove it.";
            return "This is where it all begins, as in The Sims 2: raises the ground floor a few steps, with brick sides. Drag out an area of clear ground, then build on it; add steps so Sims can get up. Paint its sides with the wall coverings. Hold Ctrl (or use erase) to remove a foundation.";
        }

        public override int GetPrice(ulong id)
        {
            if (id == BasementID) return VMArchitecture.BasementTilePrice;
            return (id == StepsID) ? VMArchitecture.FoundationStepsPrice : VMFoundationTools.TilePrice(KindOf(id));
        }

        public override bool DoDispose()
        {
            return false;
        }
    }

    /// <summary>
    /// Foundation steps in the build catalogue, one entry per staircase: the steps are that staircase's bottom piece
    /// (The Sims has no short flights of its own), at a quarter of its price. The entry's id is the staircase's GUID.
    /// </summary>
    public class UICatalogFoundationStepsResProvider : UICatalogResProvider
    {
        private static List<ObjectCatalogItem> Cached;

        /// <summary>The game's staircases that steps can be made from (the ones with a bottom piece).</summary>
        public static List<ObjectCatalogItem> Staircases()
        {
            if (Cached != null) return Cached;
            var result = new List<ObjectCatalogItem>();
            try
            {
                var stairs = Content.Get().WorldCatalog.GetItemsByCategory(10); //build mode's staircases
                if (stairs != null)
                    foreach (var item in stairs)
                        if (item.GUID != 0 && FoundationLook.StairFor(item.GUID) != null) result.Add(item);
                if (LogsWanted()) WriteLog(stairs?.Select(x => x.GUID));
            }
            catch (Exception) { }
            Cached = result;
            return result;
        }

        /// <summary>Whether the game was started with -logs (or --logs, /logs).</summary>
        private static bool LogsWanted()
        {
            foreach (var part in (FSO.Common.FSOEnvironment.Args ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if ((part.StartsWith("-") || part.StartsWith("/")) && part.TrimStart('-', '/').Equals("logs", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>-logs: foundations.log, the game's textures and staircases foundations were made from.</summary>
        private static void WriteLog(IEnumerable<uint> staircases)
        {
            try
            {
                var path = System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir ?? "", "foundations.log");
                System.IO.File.WriteAllText(path, "Simitone " + SimitoneVersion.Current + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + Environment.NewLine + FoundationLook.Describe(staircases));
            }
            catch (Exception) { }
        }

        private static string StairName(ulong id)
        {
            try
            {
                var obj = Content.Get().WorldObjects.Get((uint)id);
                var name = obj?.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0);
                if (string.IsNullOrEmpty(name)) name = Content.Get().WorldCatalog.GetItemByGUID((uint)id)?.CatalogName;
                return name ?? "Staircase";
            }
            catch (Exception)
            {
                return "Staircase";
            }
        }

        public override Texture2D GetIcon(ulong id) => UICatalogItem.GetObjIcon((uint)id);

        public override Texture2D GetThumb(ulong id) => UICatalogItem.GetObjIcon((uint)id);

        public override string GetName(ulong id) => StairName(id) + " (Foundation Steps)";

        public override string GetDescription(ulong id)
        {
            return "Steps up onto a foundation or deck: the bottom of the " + StairName(id) + ", for a quarter of its price. Click the ground beside a foundation; Sims can only get on and off by its steps. Click steps already there to change them to these. Hold Ctrl (or use erase) to remove steps.";
        }

        public override int GetPrice(ulong id) => VMFoundationTools.StepsPrice((uint)id);

        public override bool DoDispose()
        {
            return false;
        }
    }
}
