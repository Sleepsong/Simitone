using System;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Screens;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// Native gamepad control of the lot (controller support, "cursor mode").
    ///
    /// While the lot owns the pad (no modal dialog or text field has focus):
    /// <list type="bullet">
    /// <item>right stick: up/down zooms, left/right rotates (continuous in 3D, a flick per step in 2D)</item>
    /// <item>Select: next mode (Live, Buy, Build, Options); Start: pause/resume</item>
    /// <item>D-pad up/down: floor; D-pad left/right: game speed</item>
    /// <item>L3: cycle walls (down, cutaway, up, roofs); R3: re-centre on and follow the active Sim</item>
    /// </list>
    /// In live mode it also captures the pad, which makes the virtual cursor stand down:
    /// <list type="bullet">
    /// <item>left stick: moves a selector over the lot that snaps to nearby objects and people; pushing it to the
    /// edge of the screen scrolls the camera</item>
    /// <item>A: interaction menu for the selected object, or "go here" on empty floor</item>
    /// <item>LB/RB: previous/next household Sim</item>
    /// <item>LT: open/close the needs panel; RT: next panel tab</item>
    /// <item>Y: focus the action queue (left/right to choose, A cancels the action, B or Y to leave)</item>
    /// </list>
    /// Outside live mode the pad is left to the virtual cursor, except for the shared buttons above,
    /// none of which the virtual cursor uses.
    /// </summary>
    public class UILotControlPadHelper : UIElement
    {
        private const float SelectorSpeed = 520f; //UI px per second at full deflection (quadratic response)
        private const float SnapPixels = 36f; //on-screen distance from a footprint at which the selector snaps to it
        private const float MinSnapTiles = 0.2f;
        private const float MaxSnapTiles = 0.6f;
        private const double CutawayFocusSeconds = 3; //after the selector stops, the cutaway goes back to the active sim
        private const float ZoomRate = 1600f; //wheel units per second at full deflection
        private const float RotateRate3D = 2.2f; //radians per second at full deflection
        private const int MaxFloor = 5; //matches the mobile layout's floor buttons

        private readonly UILotControl Master;

        /// <summary>The lot owns the pad this frame: live mode, and no modal dialog or text field has focus.</summary>
        public bool Capturing { get; private set; }

        /// <summary>Capturing, and the pad is the device in use: show the selector and hide mouse affordances.</summary>
        public bool PadActive { get; private set; }

        /// <summary>Tile the cutaway should centre on while the pad is in use, or null to follow the mouse.</summary>
        public Vector2? CutawayFocus { get; private set; }

        private Vector2 Selector; //tile coordinates on the current level
        private bool SelectorPlaced;
        private double LastSelectorMove = double.NegativeInfinity;

        private VMEntity TargetPart; //entity the selector snapped to; may be one tile of a multitile object
        private VMEntity Target; //its interaction group leader, which owns the interaction menu
        private bool TargetHasInteractions;
        private VMEntity TargetCheckedFor; //the active sim TargetHasInteractions was computed for

        private bool WasPadActive;
        private bool FollowSim;
        private VMEntity LastActive;
        private bool RotateArmed = true;
        private bool QueueFocus;
        private int SpeedBeforePause = 1;

        private Vector2? MarkerBase; //UI coordinates, set in Update for Draw
        private Vector2? MarkerTop;
        private float AnimTime;

        public UILotControlPadHelper(UILotControl master)
        {
            Master = master;
        }

        private FSO.LotView.World World => Master.World;

        public override void Update(UpdateState state)
        {
            base.Update(state);
            Capturing = false;
            PadActive = false;
            CutawayFocus = null;
            MarkerBase = MarkerTop = null;

            var screen = Master.Parent as TS1GameScreen;
            var vm = Master.vm;
            if (screen?.Frontend == null || !vm.Ready || vm.Context.Architecture == null || !Master.Visible || !screen.Visible)
            {
                EndQueueFocus();
                return;
            }

            var frontend = screen.Frontend;
            var lotHasPad = !GameFacade.Screens.HasModalDialog
                && state.InputManager?.GetFocus() == null
                && !frontend.MainPanel.ShowingSelect
                && !frontend.CheatTextbox.Visible;
            if (!lotHasPad)
            {
                EndQueueFocus();
                return;
            }

            var pad = state.Pad;
            var dt = (float)Math.Min(state.Time.ElapsedGameTime.TotalSeconds, 0.1);
            var now = state.Time.TotalGameTime.TotalSeconds;
            //analog input only counts while the pad is the device in use, so a drifting stick can't move
            //the camera under a mouse player. Buttons are safe: pressing one makes the pad the active device.
            var padInUse = state.LastInputDevice == InputDevice.GamePad;
            var wasPadActive = WasPadActive;
            WasPadActive = false;

            if (!Master.PieMenuOpen)
            {
                if (padInUse) UpdateCamera(pad, dt, screen);
                HandleSharedButtons(pad, screen, frontend);
            }

            var active = Master.ActiveEntity;
            if (!Master.LiveMode || active == null)
            {
                EndQueueFocus();
                return;
            }

            Capturing = true;
            state.PadCaptured = true;
            PadActive = padInUse;
            WasPadActive = PadActive;

            TrackActiveSim(active);
            if (!PadActive)
            {
                EndQueueFocus(); //the mouse took over
                return;
            }
            if (!SelectorPlaced) PlaceSelectorOn(active, true);

            var recentlyMoved = now - LastSelectorMove < CutawayFocusSeconds;
            CutawayFocus = recentlyMoved ? SelectorOrTargetTile() : new Vector2(active.VisualPosition.X, active.VisualPosition.Y);

            if (Master.PieMenuOpen) return; //the pie menu reads the pad itself

            var wasQueueFocus = QueueFocus;
            if (QueueFocus) UpdateQueueFocus(pad);
            else
            {
                HandleLiveButtons(pad, frontend);
                MoveSelector(pad.LeftStick, dt, now);
            }
            if (QueueFocus) return;

            UpdateTarget(active);
            UpdateMarker(state, dt);
            //the press that hands control to the pad only reveals the selector; it doesn't act on whatever is under it.
            if (wasPadActive && !wasQueueFocus && pad.Pressed(PadAction.Confirm)) OpenMenu(state);
        }

        #region Buttons

        private void HandleSharedButtons(GamePadInput pad, TS1GameScreen screen, UISimitoneFrontend frontend)
        {
            if (pad.Pressed(PadAction.Select)) frontend.CycleMode();
            if (pad.Pressed(PadAction.Start)) TogglePause(screen);

            if (pad.PressedRepeat(PadAction.Up) && screen.Level < MaxFloor)
            {
                screen.Level++;
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
            if (pad.PressedRepeat(PadAction.Down) && screen.Level > 1)
            {
                screen.Level--;
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }

            if (!QueueFocus) //left/right move the queue focus instead
            {
                var speed = CurrentSpeed(Master.vm);
                if (pad.Pressed(PadAction.Right) && speed >= 0 && speed < 3) screen.ChangeSpeedTo(speed + 1);
                if (pad.Pressed(PadAction.Left) && speed > 0) screen.ChangeSpeedTo(speed - 1);
            }

            if (pad.Pressed(PadAction.LeftStickClick))
            {
                frontend.SetWallsMode((Master.WallsMode + 1) % 4);
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }

            if (pad.Pressed(PadAction.RightStickClick) && Master.ActiveEntity != null)
            {
                PlaceSelectorOn(Master.ActiveEntity, true);
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
        }

        private void HandleLiveButtons(GamePadInput pad, UISimitoneFrontend frontend)
        {
            if (pad.Pressed(PadAction.ShoulderLeft) || pad.Pressed(PadAction.ShoulderRight))
            {
                FollowSim = true; //TrackActiveSim moves the selector and camera once the switch lands
                frontend.CycleSim(pad.Pressed(PadAction.ShoulderLeft) ? -1 : 1);
            }

            var panel = frontend.MainPanel;
            if (pad.Pressed(PadAction.TriggerLeft))
            {
                if (panel.PanelActive) panel.Close();
                else OpenPanel(panel);
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
            if (pad.Pressed(PadAction.TriggerRight))
            {
                if (!panel.PanelActive) OpenPanel(panel);
                else
                {
                    var categories = panel.Switcher.Categories;
                    var index = categories.FindIndex(x => x.ID == panel.Switcher.ActiveCategory);
                    panel.Switcher.Select(categories[(index + 1) % categories.Count].ID);
                }
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }

            if (pad.Pressed(PadAction.FaceTop) && Master.Queue.FocusLastForPad()) QueueFocus = true;
        }

        private static void OpenPanel(UIMainPanel panel)
        {
            panel.Open();
            panel.Switcher_OnCategorySelect(panel.Switcher.ActiveCategory);
        }

        private void TogglePause(TS1GameScreen screen)
        {
            var speed = CurrentSpeed(Master.vm);
            if (speed == 0) screen.ChangeSpeedTo(SpeedBeforePause);
            else if (speed > 0)
            {
                SpeedBeforePause = speed;
                screen.ChangeSpeedTo(0);
            }
        }

        /// <summary>Speed as ChangeSpeedTo numbers it: 0 paused, 1-3, or -1 when the clock is stopped (build/buy).</summary>
        private static int CurrentSpeed(VM vm)
        {
            switch (vm.SpeedMultiplier)
            {
                case 0: return 0;
                case 1: return 1;
                case 3: return 2;
                case 10: return 3;
                default: return -1;
            }
        }

        private void UpdateQueueFocus(GamePadInput pad)
        {
            var queue = Master.Queue;
            if (!queue.HasPadFocus)
            {
                QueueFocus = false; //the queue emptied
                return;
            }
            if (pad.NavPressedRepeat(PadDirection.Left)) queue.MovePadFocus(-1);
            if (pad.NavPressedRepeat(PadDirection.Right)) queue.MovePadFocus(1);
            if (pad.Pressed(PadAction.Confirm)) queue.CancelPadFocused();
            if (pad.Pressed(PadAction.Cancel) || pad.Pressed(PadAction.FaceTop)) EndQueueFocus();
        }

        private void EndQueueFocus()
        {
            if (!QueueFocus) return;
            Master.Queue.ClearPadFocus();
            QueueFocus = false;
        }

        #endregion

        #region Camera

        private void UpdateCamera(GamePadInput pad, float dt, TS1GameScreen screen)
        {
            var stick = pad.RightStick;
            if (Math.Abs(stick.X) < 0.3f) RotateArmed = true;
            if (stick == Vector2.Zero) return;

            if (Math.Abs(stick.Y) >= Math.Abs(stick.X))
            {
                Master.Touch.NudgeZoom(stick.Y * ZoomRate * dt);
            }
            else if (Master.Touch._3D)
            {
                Master.Rotate.RotationX += stick.X * RotateRate3D * dt;
            }
            else if (RotateArmed && Math.Abs(stick.X) > 0.7f)
            {
                screen.Rotation = (screen.Rotation + ((stick.X > 0) ? 1 : 3)) % 4;
                HITVM.Get().PlaySoundEvent(UISounds.ObjectRotate);
                RotateArmed = false;
            }
        }

        /// <summary>Notices when the active sim changes, and when something else (mouse, edge scroll) takes the camera.</summary>
        private void TrackActiveSim(VMEntity active)
        {
            if (active != LastActive)
            {
                LastActive = active;
                if (FollowSim) PlaceSelectorOn(active, true);
            }
            if (FollowSim && World.State.ScrollAnchor != (active.WorldUI as AvatarComponent)) FollowSim = false;
        }

        private void PlaceSelectorOn(VMEntity entity, bool follow)
        {
            Selector = new Vector2(entity.VisualPosition.X, entity.VisualPosition.Y);
            SelectorPlaced = true;
            if (follow && entity.WorldUI is AvatarComponent avatar)
            {
                FollowSim = true;
                World.State.ScrollAnchor = avatar;
            }
        }

        #endregion

        #region Selector

        private void MoveSelector(Vector2 stick, float dt, double now)
        {
            if (stick == Vector2.Zero) return;
            var level = World.State.Level;
            var width = ScreenWidth;
            var height = ScreenHeight;

            //if the selector was left off screen (e.g. the camera followed a sim away), start from the middle.
            var pos = ToUI(World.ProjectTileToScreen(Selector, level));
            if (PadMath.Overflow(pos, 0, 0, width, height) != Vector2.Zero)
            {
                Selector = ClampToLot(World.EstTileAtPosWithScroll(FromUI(new Vector2(width, height) / 2)));
            }

            var screenDelta = new Vector2(stick.X, -stick.Y) * (SelectorSpeed * stick.Length() * dt);
            Selector = ClampToLot(Selector + ScreenToTileDelta(Selector, screenDelta, level));
            LastSelectorMove = now;

            //keep the selector inside a safe area by scrolling the camera instead. The bottom margin clears the HUD.
            pos = ToUI(World.ProjectTileToScreen(Selector, level));
            var overflow = PadMath.Overflow(pos, width * 0.12f, height * 0.15f, width * 0.88f, height * 0.72f);
            if (overflow != Vector2.Zero)
            {
                World.State.CenterTile += ScreenToTileDelta(Selector, overflow, level);
                World.State.ScrollAnchor = null;
                FollowSim = false;
            }
        }

        /// <summary>Average on-screen length, in UI pixels, of one tile step along X and Y at a tile.</summary>
        private float PixelsPerTile(Vector2 tile, sbyte level)
        {
            var origin = ToUI(World.ProjectTileToScreen(tile, level));
            var stepX = ToUI(World.ProjectTileToScreen(tile + Vector2.UnitX, level)) - origin;
            var stepY = ToUI(World.ProjectTileToScreen(tile + Vector2.UnitY, level)) - origin;
            return (stepX.Length() + stepY.Length()) / 2;
        }

        /// <summary>Converts a movement in UI pixels at a tile into the tile-space movement that produces it.</summary>
        private Vector2 ScreenToTileDelta(Vector2 tile, Vector2 screenDelta, sbyte level)
        {
            var origin = ToUI(World.ProjectTileToScreen(tile, level));
            var stepX = ToUI(World.ProjectTileToScreen(tile + Vector2.UnitX, level)) - origin;
            var stepY = ToUI(World.ProjectTileToScreen(tile + Vector2.UnitY, level)) - origin;
            return PadMath.SolveBasis(stepX, stepY, screenDelta);
        }

        private Vector2 ClampToLot(Vector2 tile)
        {
            var arch = Master.vm.Context.Architecture;
            return new Vector2(
                Math.Max(0.5f, Math.Min(arch.Width - 0.5f, tile.X)),
                Math.Max(0.5f, Math.Min(arch.Height - 0.5f, tile.Y)));
        }

        private void UpdateTarget(VMEntity active)
        {
            var vm = Master.vm;
            var level = World.State.Level;
            VMEntity best = null;
            var bestScore = float.MaxValue;

            //snap within a fixed distance on screen. A fixed tile radius felt sticky up close (e.g. the 3D camera),
            //where a tile covers many more pixels. The tie-break biases scale with it.
            var snapRadius = PadMath.SnapRadius(PixelsPerTile(Selector, level), SnapPixels, MinSnapTiles, MaxSnapTiles);
            var bias = snapRadius / MaxSnapTiles;

            foreach (var entity in vm.Entities)
            {
                if (entity == Master.GotoObject || entity.Dead) continue;
                var pos = entity.Position;
                if (pos == LotTilePos.OUT_OF_WORLD || pos.Level != level) continue;

                var footprint = entity.Footprint;
                var distance = (footprint != null)
                    ? PadMath.DistanceToFootprint(Selector, footprint.x1, footprint.y1, footprint.x2, footprint.y2)
                    : PadMath.DistanceToFootprint(Selector, pos.x, pos.y, pos.x, pos.y);
                if (distance > snapRadius) continue;

                var leader = entity.MultitileGroup.GetInteractionGroupLeader(entity);
                if ((leader.TreeTable?.Interactions.Length ?? 0) == 0 && (entity.TreeTable?.Interactions.Length ?? 0) == 0) continue;

                var score = distance;
                if (entity is VMAvatar) score += ((entity == active) ? 0.3f : -0.15f) * bias; //people over furniture, but not yourself
                if (entity.Container != null) score -= 0.1f * bias; //things sitting on surfaces over the surface
                if (entity == TargetPart) score -= 0.15f * bias; //hysteresis, so the target doesn't flicker between neighbours
                if (score < bestScore)
                {
                    bestScore = score;
                    best = entity;
                }
            }

            if (best != TargetPart || active != TargetCheckedFor)
            {
                TargetPart = best;
                Target = best?.MultitileGroup.GetInteractionGroupLeader(best);
                //checked once per target, like mouse hover. Building a menu runs every interaction's check tree,
                //so re-checking on a timer is costly, and any tree that throws would raise a popup each time.
                TargetHasInteractions = Target != null && Target.GetPieMenu(vm, active, false, true).Count > 0;
                TargetCheckedFor = active;
            }
        }

        private Vector2 SelectorOrTargetTile()
        {
            return (TargetPart != null) ? new Vector2(TargetPart.VisualPosition.X, TargetPart.VisualPosition.Y) : Selector;
        }

        private void OpenMenu(UpdateState state)
        {
            var anchor = MarkerTop ?? MarkerBase ?? new Vector2(ScreenWidth, ScreenHeight) / 2;
            //keep the whole pie (about 300px wide and 250px tall with captions) on screen.
            var menuPos = PadMath.ClampAnchor(anchor, ScreenWidth, ScreenHeight, 170, 130);
            Master.OpenPieMenu(Target, Selector, menuPos, state);
        }

        #endregion

        #region Drawing

        private void UpdateMarker(UpdateState state, float dt)
        {
            AnimTime += dt;
            var level = World.State.Level;
            if (TargetPart == null)
            {
                MarkerBase = ToUI(World.ProjectTileToScreen(Selector, level));
                return;
            }

            var visual = TargetPart.VisualPosition;
            var tile = new Vector2(visual.X, visual.Y);
            var entityLevel = TargetPart.Position.Level;
            var heightAboveFloor = Math.Max(0, visual.Z - (entityLevel - 1) * 2.95f); //e.g. items on counters
            var markerHeight = (TargetPart is VMAvatar) ? 2.1f : 1.3f;
            MarkerBase = ToUI(World.ProjectTileToScreen(tile, entityLevel, heightAboveFloor));
            MarkerTop = ToUI(World.ProjectTileToScreen(tile, entityLevel, heightAboveFloor + markerHeight));

            if (Target is VMAvatar avatar)
            {
                var tip = state.UIState.TooltipProperties;
                tip.Show = true;
                tip.Color = Color.Black;
                tip.Opacity = 1;
                tip.Position = MarkerTop.Value * FSOEnvironment.DPIScaleFactor;
                tip.UpdateDead = false;
                state.UIState.Tooltip = avatar.ToString();
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (MarkerBase == null) return;
            var gd = GameFacade.GraphicsDevice;
            var pulse = 0.8f + 0.2f * (float)Math.Sin(AnimTime * 5);
            Color color;
            if (Target == null) color = Color.White;
            else color = TargetHasInteractions ? new Color(140, 255, 120) : new Color(255, 110, 100);

            var ring = PadGraphics.GetRing(gd);
            var size = (Target == null) ? 36f : 56f;
            //squashed into an ellipse so it reads as lying on the floor.
            DrawLocalTexture(batch, ring, null, GlobalPoint(MarkerBase.Value) - new Vector2(size / 2, size / 4),
                new Vector2(size / ring.Width, size / 2 / ring.Height), color * pulse);

            if (MarkerTop != null)
            {
                var marker = PadGraphics.GetMarker(gd);
                var bob = (float)Math.Sin(AnimTime * 3) * 4;
                DrawLocalTexture(batch, marker, null, GlobalPoint(MarkerTop.Value) - new Vector2(marker.Width / 2f, marker.Height - bob),
                    Vector2.One, color);
            }
        }

        #endregion

        #region Coordinates

        private float ScreenWidth => GameFacade.Screens.CurrentUIScreen.ScreenWidth;
        private float ScreenHeight => GameFacade.Screens.CurrentUIScreen.ScreenHeight;

        /// <summary>World screen space (what EstTileAtPosWithScroll takes) to UI space, undoing the 2D smooth-zoom scale.</summary>
        private Vector2 ToUI(Vector2 worldScreen)
        {
            var middle = new Vector2(ScreenWidth, ScreenHeight) / 2;
            return (worldScreen - middle) * World.BackbufferScale + middle;
        }

        private Vector2 FromUI(Vector2 ui)
        {
            var middle = new Vector2(ScreenWidth, ScreenHeight) / 2;
            return (ui - middle) / World.BackbufferScale + middle;
        }

        #endregion
    }
}
