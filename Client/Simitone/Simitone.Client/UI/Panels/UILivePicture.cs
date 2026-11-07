using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// TS1's Live Picture in Picture: an event popup's picture is a live, close view of what it's about (a Sim, or an
    /// object on the lot) instead of a still. The lot is drawn again for it each frame while the popup is open
    /// (World.DrawPicture), facing the way the main view does, with the walls around the subject cut as cutaway mode cuts
    /// them. Full 3D only, and an option (Options, Game: GlobalSettings.LivePIP).
    /// </summary>
    public class UILivePicture : UIElement
    {
        /// <summary>The picture's size in pixels (shown at 256, so it's sharp).</summary>
        public const int Size = 384;

        public WorldPicture Picture;
        private World World;
        private VM VM;
        private VMEntity Subject;
        private bool Drawn;

        private LotTilePos LastTile = LotTilePos.OUT_OF_WORLD;
        private WorldRotation LastRotation;

        public UILivePicture(World world, VM vm, VMEntity subject)
        {
            World = world;
            VM = vm;
            Subject = subject;
            Picture = new WorldPicture(GameFacade.GraphicsDevice, Size);
            Aim();
            Log("popup about " + Describe(subject) + ": live picture of level " + Picture.Level + ", looking at " + Picture.Focus);
        }

        private string LastLogged; //(each frame's outcome is only logged when it changes)

        /// <summary>-logs: pip.log, what popups' live pictures did (and why there wasn't one), to find out why one doesn't show.</summary>
        public static void Log(string line)
        {
            if (!LogsWanted()) return;
            try
            {
                var path = System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir ?? "", "pip.log");
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch (Exception) { }
        }

        private static bool? Logs;
        private static bool LogsWanted()
        {
            if (Logs == null)
            {
                Logs = false;
                foreach (var part in (FSO.Common.FSOEnvironment.Args ?? "").Split(' '))
                    if ((part.StartsWith("-") || part.StartsWith("/")) && part.TrimStart('-', '/').Equals("logs", StringComparison.OrdinalIgnoreCase)) Logs = true;
            }
            return Logs.Value;
        }

        public static string Describe(VMEntity entity)
        {
            if (entity == null) return "nothing";
            return ((entity is VMAvatar) ? "Sim " : "object ") + entity.ToString() + " (id " + entity.ObjectID + ", at " + entity.Position + ")";
        }

        private static bool Sampled; //(the first picture drawn this run is saved, with -logs)

        /// <summary>-logs: what the first picture drawn looks like, in pip.log (how much is see-through, its colours) and pip.png.</summary>
        private void LogSample()
        {
            try
            {
                var target = Picture.Target;
                var data = new Color[target.Width * target.Height];
                target.GetData(data);
                int clear = 0, partial = 0;
                long r = 0, g = 0, b = 0;
                var colours = new HashSet<uint>();
                foreach (var c in data)
                {
                    if (c.A == 0) clear++;
                    else if (c.A < 255) partial++;
                    r += c.R; g += c.G; b += c.B;
                    if (colours.Count < 1000) colours.Add(c.PackedValue);
                }
                var n = Math.Max(1, data.Length);
                Log("  picture " + target.Width + "x" + target.Height + ": " + (clear * 100 / n) + "% see-through, " + (partial * 100 / n) + "% partly; average colour "
                    + (r / n) + ", " + (g / n) + ", " + (b / n) + "; " + (colours.Count >= 1000 ? "1000+" : colours.Count.ToString()) + " colours");
                var path = System.IO.Path.Combine(FSO.Common.FSOEnvironment.UserDir ?? "", "pip.png");
                using (var file = System.IO.File.Create(path)) target.SaveAsPng(file, target.Width, target.Height);
                Log("  saved it to " + path);
            }
            catch (Exception e)
            {
                Log("  couldn't look at the picture: " + e.Message);
            }
        }

        private void LogOutcome(string outcome)
        {
            if (outcome == LastLogged) return;
            LastLogged = outcome;
            Log("  " + outcome);
        }

        /// <summary>Whether popups show live pictures: the option is on, in full 3D.</summary>
        public static bool Enabled(World world)
        {
            return GlobalSettings.Default.LivePIP && world?.State != null && world.State.CameraMode == CameraRenderMode._3D;
        }

        /// <summary>
        /// The Sim or object a popup is about, if there's one to look at on the lot: its icon object, or the neighbour it
        /// names if they're here. Not objects with nothing to see (controllers), or anything off the lot.
        /// </summary>
        public static VMEntity SubjectOf(VM vm, VMDialogInfo info)
        {
            var subject = info.Icon;
            if (subject == null && info.IconNeighborGUID != 0)
                subject = vm.Context.ObjectQueries.Avatars.FirstOrDefault(x => x.Object?.OBJ?.GUID == info.IconNeighborGUID);
            if (subject == null || subject.Dead || subject.Position == LotTilePos.OUT_OF_WORLD || subject.WorldUI == null) return null;
            if (!subject.WorldUI.Visible) return null;
            if (subject is VMGameObject)
            {
                var parts = subject.MultitileGroup?.Objects ?? new List<VMEntity>() { subject };
                if (!parts.Any(x => (x.WorldUI as ObjectComponent)?.DGRP != null)) return null;
            }
            return subject;
        }

        /// <summary>Points the picture at the subject, and cuts the walls around it when it's moved to another tile.</summary>
        private void Aim()
        {
            if (Subject == null || Subject.Dead || Subject.Position == LotTilePos.OUT_OF_WORLD) return; //keeps its last view
            var pos = Subject.Position;
            Picture.Level = pos.Level;

            if (Subject is VMAvatar)
            {
                //a little below their head
                Picture.Focus = Subject.WorldUI.GetLookTarget() - new Vector3(0, 0.45f, 0);
            }
            else
            {
                //the middle of all its tiles, a little above its base
                var parts = Subject.MultitileGroup?.Objects ?? new List<VMEntity>() { Subject };
                var sum = Vector3.Zero;
                foreach (var part in parts)
                {
                    var p = part.WorldUI.Position;
                    sum += new Vector3(p.X + 0.5f, p.Z + 0.4f, p.Y + 0.5f);
                }
                Picture.Focus = sum / Math.Max(1, parts.Count);
            }

            var rotation = World.State.CutRotation;
            var tile = LotTilePos.FromBigTile(pos.TileX, pos.TileY, pos.Level);
            if (tile == LastTile && rotation == LastRotation && Picture.Cut != null) return;
            LastTile = tile;
            LastRotation = rotation;

            //as cutaway mode cuts: the subject's room, and a few tiles around it
            var arch = VM.Context.Architecture;
            var rooms = new HashSet<uint>();
            var room = VM.Context.GetRoomAt(tile);
            if (room < VM.Context.RoomInfo.Length && !VM.Context.RoomInfo[room].Room.IsOutside) rooms.Add(room);
            var cut = VMArchitectureTools.GenerateRoomCut(arch, pos.Level, rotation, rooms);
            var rect = new Rectangle(pos.TileX - 2, pos.TileY - 2, 5, 5);
            rect.X -= VMArchitectureTools.CutCheckDir[(int)rotation][0] * 2;
            rect.Y -= VMArchitectureTools.CutCheckDir[(int)rotation][1] * 2;
            VMArchitectureTools.ApplyCutRectangle(arch, pos.Level, cut, rect);
            Picture.Cut = cut;
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            base.PreDraw(batch);
            if (Picture?.Target == null) return;
            var gd = GameFacade.GraphicsDevice;
            try
            {
                Aim();
                if (World.DrawPicture(gd, Picture))
                {
                    Drawn = true;
                    LogOutcome("drawn");
                    if (!Sampled && LogsWanted())
                    {
                        Sampled = true;
                        LogSample();
                    }
                }
                else LogOutcome("not drawn: " + (World.PictureRefusal ?? "(no reason given)"));
            }
            catch (Exception e)
            {
                //a picture is never worth breaking the game over: it keeps its last frame
                LogOutcome("failed: " + e);
            }
            if (!Drawn)
            {
                //nothing drawn yet (the camera's moving, or not in 3D): show nothing rather than whatever the memory held
                var old = gd.GetRenderTargets();
                gd.SetRenderTarget(Picture.Target);
                gd.Clear(Color.Transparent);
                if (old.Length > 0) gd.SetRenderTargets(old);
                else gd.SetRenderTarget(null);
                Drawn = true;
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
        }

        public override void Removed()
        {
            base.Removed();
            Picture?.Dispose();
            Picture = null;
        }
    }
}
