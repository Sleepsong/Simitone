using FSO.Client.UI.Panels.LotControls;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content;
using FSO.Content.Model;
using FSO.HIT;
using FSO.LotView;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.Client.UI.Model;
using FSO.UI.Panels.LotControls;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.LotControls
{
    /// <summary>
    /// Build mode's basement tool: drag out a rectangle of tiles to dig out a basement there, a storey below the ground
    /// floor, as in The Sims 2 (Ctrl, or the touch erase button, to fill it back in). Dug tiles get a floor, and so do
    /// the ground floor tiles above them; the earth's sides round the basement get a wall covering. Then go down a floor
    /// (full 3D) to build walls and put stairs in.
    /// </summary>
    public class UIBasementPlacer : UIFoundationToolBase
    {
        private ushort Covering;
        private ushort FloorPattern;
        private bool Drawing;
        private Point Start;
        private Point End;

        public UIBasementPlacer(VM vm, World world, ILotControl parent, List<int> parameters) : base(vm, world, parent)
        {
            Covering = DefaultCovering();
            FloorPattern = DefaultFloor();
        }

        /// <summary>
        /// The wall covering on the earth's sides round a new basement: a brick or stone wall pattern if the game has one,
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

        /// <summary>The floor a new basement gets (and the ground floor above it, where it had none): concrete if the game has it.</summary>
        public static ushort DefaultFloor()
        {
            try
            {
                var floors = Content.Get().WorldFloors.List().OfType<FloorReference>().Where(f => f.ID > 0 && f.ID < 65503).ToList();
                foreach (var word in new[] { "concrete", "cement", "stone", "tile" })
                {
                    var match = floors.FirstOrDefault(f => (f.Name ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) return (ushort)match.ID;
                }
                return (ushort)(floors.FirstOrDefault()?.ID ?? 1);
            }
            catch (Exception)
            {
                return 1;
            }
        }

        private VMArchitectureCommand Command(Point a, Point b)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            return new VMArchitectureCommand
            {
                Type = VMArchitectureCommandType.BASEMENT_RECT,
                x = x,
                y = y,
                x2 = Math.Abs(a.X - b.X),
                y2 = Math.Abs(a.Y - b.Y),
                level = 1,
                style = (ushort)((Covering << 1) | (Erasing ? 0 : 1)),
                pattern = FloorPattern
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
}
