using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
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
    /// TS1's Picture in Picture: a small window at the bottom right of the screen showing, live, what an event is about (the
    /// Sim or object an event popup names, or a fire as it starts), so it can be seen without moving the camera. Clicking it
    /// takes the camera there; the X closes it. It closes by itself a little after what opened it is over (its popup is
    /// closed, or its fire has gone out). The lot is drawn again for it each frame (World.DrawPicture), facing the way the
    /// main view does, with the walls around the subject cut as cutaway mode cuts them. Full 3D only, and an option
    /// (Options, Game: GlobalSettings.LivePIP).
    /// </summary>
    public class UIPictureInPicture : UIContainer
    {
        /// <summary>The picture's size in pixels when drawn (shown smaller, so it's sharp).</summary>
        public const int RenderSize = 384;
        /// <summary>The picture's size on screen, its frame round it, and how far it keeps from the screen's edges (clear of the HUD's corner).</summary>
        public const int ViewSize = 256, Border = 4, RightMargin = 16, BottomMargin = 100;
        private const int CloseSize = 28;
        /// <summary>How long it stays once what opened it is over (seconds).</summary>
        private const float LingerSeconds = 10f;

        public WorldPicture Picture;
        private World World;
        private VM VM;
        private VMEntity Subject;
        private Func<bool> HeldOpen;
        /// <summary>Whether it's showing a fire (a fire starting elsewhere doesn't take it from one).</summary>
        public bool WatchingFire { get; private set; }
        private float Linger = LingerSeconds;
        private bool Drawn;
        private bool Closed;
        private Texture2D CloseIcon;
        private UIMouseEventRef MouseEvt;
        private bool Hovered;

        private LotTilePos LastTile = LotTilePos.OUT_OF_WORLD;
        private WorldRotation LastRotation;

        public event Action<UIPictureInPicture> OnClosed;

        private int Total => ViewSize + Border * 2;

        public UIPictureInPicture(World world, VM vm)
        {
            World = world;
            VM = vm;
            Picture = new WorldPicture(GameFacade.GraphicsDevice, RenderSize);
            CloseIcon = FSO.Content.Content.Get().CustomUI.Get("int_cancel.png").Get(GameFacade.GraphicsDevice);
            MouseEvt = ListenForMouse(new Rectangle(0, 0, Total, Total), OnMouse);
            Place();
        }

        /// <summary>Whether there's picture in picture: the option is on, in full 3D.</summary>
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
            return Showable(subject) ? subject : null;
        }

        /// <summary>Whether something can be shown: on the lot, and something to see.</summary>
        public static bool Showable(VMEntity subject)
        {
            if (subject == null || subject.Dead || subject.Position == LotTilePos.OUT_OF_WORLD || subject.WorldUI == null) return false;
            if (!subject.WorldUI.Visible) return false;
            if (subject is VMGameObject)
            {
                var parts = subject.MultitileGroup?.Objects ?? new List<VMEntity>() { subject };
                if (!parts.Any(x => (x.WorldUI as ObjectComponent)?.DGRP != null)) return false;
            }
            return true;
        }

        /// <summary>
        /// Shows something (the window's moved to it if it was showing something else). It stays while <paramref name="heldOpen"/>
        /// says so, then a little longer.
        /// </summary>
        public void Show(VMEntity subject, Func<bool> heldOpen, bool fire)
        {
            Subject = subject;
            HeldOpen = heldOpen;
            WatchingFire = fire;
            Linger = LingerSeconds;
            LastTile = LotTilePos.OUT_OF_WORLD; //(aimed and cut again for it)
            Log("showing " + Describe(subject) + (fire ? " (a fire)" : ""));
            Aim();
        }

        /// <summary>Closes it (it's taken off the screen, and its picture let go).</summary>
        public void Close()
        {
            if (Closed) return;
            Closed = true;
            Log("closed");
            Parent?.Remove(this);
            OnClosed?.Invoke(this);
        }

        private void Place()
        {
            X = GlobalSettings.Default.GraphicsWidth - Total - RightMargin;
            Y = GlobalSettings.Default.GraphicsHeight - Total - BottomMargin;
        }

        private Rectangle CloseRect => new Rectangle(Total - CloseSize - 6, 6, CloseSize, CloseSize);

        private void OnMouse(UIMouseEventType type, UpdateState state)
        {
            switch (type)
            {
                case UIMouseEventType.MouseOver:
                    Hovered = true;
                    break;
                case UIMouseEventType.MouseOut:
                    Hovered = false;
                    break;
                case UIMouseEventType.MouseDown:
                    var local = GlobalPoint(new Vector2(state.MouseState.X, state.MouseState.Y));
                    if (!CloseRect.Contains((int)local.X, (int)local.Y)) GoTo(); //the picture: the camera goes there
                    Close();
                    break;
            }
        }

        /// <summary>Takes the main view to what it's showing: its floor, centred on it.</summary>
        private void GoTo()
        {
            if (Subject == null || Subject.Dead || Subject.Position == LotTilePos.OUT_OF_WORLD) return;
            var pos = Subject.Position;
            World.State.Level = pos.Level;
            World.State.CenterTile = new Vector2(pos.x / 16f, pos.y / 16f);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (Closed) return;
            Place();
            if (!Enabled(World))
            {
                Close();
                return;
            }
            //it stays while what opened it holds it open (and its subject's still there), then a little longer
            var held = Subject != null && !Subject.Dead && Subject.Position != LotTilePos.OUT_OF_WORLD && (HeldOpen?.Invoke() ?? false);
            if (held) Linger = LingerSeconds;
            else
            {
                Linger -= 1f / Math.Max(1, FSOEnvironment.RefreshRate);
                if (Linger <= 0) Close();
            }
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            base.PreDraw(batch);
            if (Closed || Picture?.Target == null) return;
            try
            {
                Aim();
                if (World.DrawPicture(GameFacade.GraphicsDevice, Picture))
                {
                    Drawn = true;
                    LogOutcome("drawn");
                }
                else LogOutcome("not drawn: " + (World.PictureRefusal ?? "(no reason given)"));
            }
            catch (Exception e)
            {
                //a picture is never worth breaking the game over: it keeps its last frame
                LogOutcome("failed: " + e);
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || Closed || Picture?.Target == null) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            //a frame: a dark edge, then a light one (brighter while the mouse is on it)
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(Total, Total), new Color(0, 0, 0, 160));
            DrawLocalTexture(batch, px, null, new Vector2(1, 1), new Vector2(Total - 2, Total - 2), Hovered ? new Color(255, 255, 255) : new Color(200, 205, 210));
            DrawLocalTexture(batch, px, null, new Vector2(Border - 1, Border - 1), new Vector2(ViewSize + 2, ViewSize + 2), new Color(0, 0, 0, 200));
            if (Drawn) DrawLocalTexture(batch, Picture.Target, null, new Vector2(Border, Border), new Vector2(ViewSize / (float)RenderSize), Color.White);
            //the X, top right
            var close = CloseRect;
            DrawLocalTexture(batch, CloseIcon, null, new Vector2(close.X, close.Y), new Vector2(CloseSize / (float)CloseIcon.Width, CloseSize / (float)CloseIcon.Height), Color.White);
            base.Draw(batch);
        }

        public override void Removed()
        {
            base.Removed();
            Picture?.Dispose();
            Picture = null;
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

        // ---- -logs: pip.log, what picture in picture showed and how its picture drew

        private string LastLogged; //(each frame's outcome is only logged when it changes)

        private void LogOutcome(string outcome)
        {
            if (outcome == LastLogged) return;
            LastLogged = outcome;
            Log("  " + outcome);
        }

        public static void Log(string line)
        {
            if (!LogsWanted()) return;
            try
            {
                var path = System.IO.Path.Combine(FSOEnvironment.UserDir ?? "", "pip.log");
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
                foreach (var part in (FSOEnvironment.Args ?? "").Split(' '))
                    if ((part.StartsWith("-") || part.StartsWith("/")) && part.TrimStart('-', '/').Equals("logs", StringComparison.OrdinalIgnoreCase)) Logs = true;
            }
            return Logs.Value;
        }

        public static string Describe(VMEntity entity)
        {
            if (entity == null) return "nothing";
            return ((entity is VMAvatar) ? "Sim " : "object ") + entity.ToString() + " (id " + entity.ObjectID + ", at " + entity.Position + ")";
        }
    }
}
