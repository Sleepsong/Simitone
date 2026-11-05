using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Simitone.VR.OpenXR;

namespace Simitone.VR
{
    public struct VRButton
    {
        public bool Down;
        public bool Pressed;
        public bool Released;

        internal void Set(bool down)
        {
            Pressed = down && !Down;
            Released = !down && Down;
            Down = down;
        }
    }

    public struct VRPose
    {
        public bool Valid;
        public Vector3 Position;   // metres, application (tracking) space
        public Quaternion Orientation;

        public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Orientation);
    }

    /// <summary>
    /// OpenXR input for Simitone. Default bindings (right-handed; left-handed mirrors them):
    ///
    ///   Left stick ........ walk (your Sim, or yourself)       Left stick click ... run (hold)
    ///   Right stick X ..... snap / smooth turn                 Right stick Y ...... floor up / down (observer), scroll
    ///   Right stick click . cycle game speed
    ///   A (right) ......... first person / flat screen         B (right) .......... play as Sim / observer; recentre screen
    ///   X (left) .......... pause / resume the game            Y (left) ........... next Sim in the family
    ///   Right trigger ..... point and interact / click         Right grip ......... right click / close a pie menu
    ///   Left grip ......... cycle live / buy / build mode      Left trigger ....... movement arc (hold, aim, let go)
    ///   Left menu ......... VR options menu (Touch; squeeze the walking hand's trackpad on Index; on any controller
    ///                       point at the needs display on your wrist and pull the trigger)
    ///
    /// In buy and build mode the trigger picks up and places, the right stick rotates (X) and changes the arc's reach
    /// (Y) while holding something, the right grip cancels and B sells what you're holding.
    ///
    /// <b>Left-handed</b> (<see cref="LeftHanded"/>): every role swaps hands (the left controller points, the right one
    /// walks; X/Y and A/B swap too), except the menu button, which only the left Touch controller has. The two layouts
    /// are two action sets ("Simitone" and "Simitone (left-handed)"), both bound up front; switching only changes which
    /// one is read, so it applies at once, and each can be rebound in SteamVR's binding UI. A third set has what isn't
    /// a role: the hands' poses and haptics, and eye gaze (XR_EXT_eye_gaze_interaction, for foveated rendering).
    ///
    /// Bindings are suggested for the Steam Frame controller, Oculus Touch, Valve Index, HTC Vive and the KHR
    /// simple controller.
    /// </summary>
    public unsafe sealed class VRInput : IDisposable
    {
        public const string FrameControllerExtension = "XR_VALVE_frame_controller_interaction";
        public const string EyeGazeExtension = "XR_EXT_eye_gaze_interaction";

        private class Roles
        {
            public ulong Set;
            public ulong Move, Turn, Sprint, ToggleView, SwitchMode, Pause, Speed, NextSim, BuildMenu, Select, Secondary, MoveArc, Menu;
        }

        private XrApi Api;
        private ulong LeftHand, RightHand;
        private Roles RightHanded, LeftHandedRoles;
        private ulong HandsSet, AimPoseAction, GripPoseAction, HapticAction, GazeAction;
        private ulong LeftAimSpace, RightAimSpace, LeftGripSpace, RightGripSpace, GazeSpace;
        private bool GazeBound;

        public Vector2 Move;
        public Vector2 Turn;
        public VRButton Sprint, ToggleView, SwitchMode, Pause, Speed, NextSim, BuildMenu, Select, Secondary, MoveArc, Menu;
        public float SelectValue, SecondaryValue, MoveArcValue;
        /// <summary>The controllers by physical hand.</summary>
        public VRPose LeftAim, RightAim, LeftGrip, RightGrip;
        /// <summary>
        /// Where your eyes look (tracking space; forward is the gaze), while the headset tracks them and the player
        /// allows it. Invalid otherwise.
        /// </summary>
        public VRPose Gaze;
        public bool Active;
        public List<string> BoundProfiles = new List<string>();
        /// <summary>The left controller points and the right one walks (set before each sync; applies at once).</summary>
        public bool LeftHanded;
        /// <summary>Eye gaze was offered by the runtime and bound (it may still be untracked, see <see cref="Gaze"/>).</summary>
        public bool GazeAvailable => GazeBound;

        /// <summary>The pointing hand's controller (right, or left when left-handed).</summary>
        public VRPose PointerAim => LeftHanded ? LeftAim : RightAim;
        public VRPose PointerGrip => LeftHanded ? LeftGrip : RightGrip;
        /// <summary>The walking hand's controller (left, or right when left-handed).</summary>
        public VRPose WalkAim => LeftHanded ? RightAim : LeftAim;
        public VRPose WalkGrip => LeftHanded ? RightGrip : LeftGrip;
        /// <summary>What to point with: the pointing hand, or the other one if it isn't tracked.</summary>
        public VRPose AnyPointerAim => PointerAim.Valid ? PointerAim : WalkAim;

        private Action<string> Log;

        public VRInput(Action<string> log)
        {
            Log = log;
        }

        public void Create(XrApi api, bool frameControllerExtension, bool eyeGaze)
        {
            Api = api;
            LeftHand = api.Path("/user/hand/left");
            RightHand = api.Path("/user/hand/right");

            RightHanded = CreateRoles("simitone", "Simitone");
            LeftHandedRoles = CreateRoles("simitone_left", "Simitone (left-handed)");
            HandsSet = CreateSet("simitone_hands", "Simitone hands and eyes", 0);
            AimPoseAction = CreateAction(HandsSet, "aim", "Pointer", XrActionType.PoseInput, true);
            GripPoseAction = CreateAction(HandsSet, "grip", "Hand (wrist displays)", XrActionType.PoseInput, true);
            HapticAction = CreateAction(HandsSet, "haptic", "Haptics", XrActionType.VibrationOutput, true);
            if (eyeGaze) GazeAction = CreateAction(HandsSet, "eye_gaze", "Eye gaze (foveated rendering)", XrActionType.PoseInput, false);

            SuggestBindings(frameControllerExtension);
            if (eyeGaze)
            {
                GazeBound = Suggest("/interaction_profiles/ext/eye_gaze_interaction",
                    new List<(ulong, string)> { (GazeAction, "/user/eyes_ext/input/gaze_ext/pose") });
                Log?.Invoke(GazeBound ? "OpenXR: eye gaze bound (foveated rendering can follow your eyes)" : "OpenXR: eye gaze binding rejected");
            }
        }

        private ulong CreateSet(string name, string localized, uint priority)
        {
            var info = new XrActionSetCreateInfo { Type = XrStructureType.ActionSetCreateInfo, Priority = priority };
            XrConst.WriteString(info.ActionSetName, XrConst.MaxActionSetNameSize, name);
            XrConst.WriteString(info.LocalizedActionSetName, XrConst.MaxLocalizedActionSetNameSize, localized);
            ulong set;
            Api.Check(Api.CreateActionSet(Api.Instance, &info, &set), "xrCreateActionSet(" + name + ")");
            return set;
        }

        private Roles CreateRoles(string name, string localized)
        {
            var r = new Roles { Set = CreateSet(name, localized, 0) };
            r.Move = CreateAction(r.Set, "move", "Move", XrActionType.Vector2fInput, false);
            r.Turn = CreateAction(r.Set, "turn", "Turn / change floor", XrActionType.Vector2fInput, false);
            r.Sprint = CreateAction(r.Set, "sprint", "Sprint", XrActionType.BooleanInput, false);
            r.ToggleView = CreateAction(r.Set, "toggle_view", "First person / flat screen", XrActionType.BooleanInput, false);
            r.SwitchMode = CreateAction(r.Set, "switch_mode", "Play as Sim / observer, recentre screen", XrActionType.BooleanInput, false);
            r.Pause = CreateAction(r.Set, "pause", "Pause / resume", XrActionType.BooleanInput, false);
            r.Speed = CreateAction(r.Set, "speed", "Cycle game speed", XrActionType.BooleanInput, false);
            r.NextSim = CreateAction(r.Set, "next_sim", "Next Sim in the family", XrActionType.BooleanInput, false);
            r.BuildMenu = CreateAction(r.Set, "build_menu", "Cycle live / buy / build mode", XrActionType.BooleanInput, false);
            r.Select = CreateAction(r.Set, "select", "Interact / click", XrActionType.FloatInput, false);
            r.Secondary = CreateAction(r.Set, "secondary", "Right click / close menu", XrActionType.FloatInput, false);
            r.MoveArc = CreateAction(r.Set, "move_arc", "Movement arc (walk / teleport there)", XrActionType.FloatInput, false);
            r.Menu = CreateAction(r.Set, "vr_menu", "VR options menu", XrActionType.BooleanInput, false);
            return r;
        }

        private ulong CreateAction(ulong set, string name, string localized, XrActionType type, bool bothHands)
        {
            var info = new XrActionCreateInfo { Type = XrStructureType.ActionCreateInfo, ActionType = type };
            XrConst.WriteString(info.ActionName, XrConst.MaxActionNameSize, name);
            XrConst.WriteString(info.LocalizedActionName, XrConst.MaxLocalizedActionNameSize, localized);
            ulong* hands = stackalloc ulong[2] { LeftHand, RightHand };
            if (bothHands)
            {
                info.CountSubactionPaths = 2;
                info.SubactionPaths = hands;
            }
            ulong action;
            Api.Check(Api.CreateAction(set, &info, &action), "xrCreateAction(" + name + ")");
            return action;
        }

        private bool Suggest(string profile, List<(ulong action, string path)> bindings)
        {
            ulong profilePath;
            try { profilePath = Api.Path(profile); }
            catch (XrException e) { Log?.Invoke("OpenXR: " + profile + ": " + e.Message); return false; }

            var list = new XrActionSuggestedBinding[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
            {
                try
                {
                    list[i] = new XrActionSuggestedBinding { Action = bindings[i].action, Binding = Api.Path(bindings[i].path) };
                }
                catch (XrException e)
                {
                    Log?.Invoke("OpenXR: bad binding path " + bindings[i].path + ": " + e.Message);
                    return false;
                }
            }
            fixed (XrActionSuggestedBinding* p = list)
            {
                var suggested = new XrInteractionProfileSuggestedBinding
                {
                    Type = XrStructureType.InteractionProfileSuggestedBinding,
                    InteractionProfile = profilePath,
                    CountSuggestedBindings = (uint)list.Length,
                    SuggestedBindings = p
                };
                var result = Api.SuggestInteractionProfileBindings(Api.Instance, &suggested);
                if ((int)result < 0)
                {
                    Log?.Invoke($"OpenXR: bindings for {profile} rejected: {Api.ResultName(result)}");
                    return false;
                }
            }
            BoundProfiles.Add(profile);
            return true;
        }

        private void SuggestBindings(bool frameControllerExtension)
        {
            const string L = "/user/hand/left/";
            const string R = "/user/hand/right/";

            // A profile's bindings are suggested in one call (a second call replaces the first): both layouts, and the
            // hands. "dom" is the pointing hand, "off" the walking hand.
            List<(ulong, string)> Both(Func<Roles, string, string, List<(ulong, string)>> layout)
            {
                var all = layout(RightHanded, R, L);
                all.AddRange(layout(LeftHandedRoles, L, R));
                all.AddRange(new List<(ulong, string)>
                {
                    (AimPoseAction, L + "input/aim/pose"),
                    (AimPoseAction, R + "input/aim/pose"),
                    (GripPoseAction, L + "input/grip/pose"),
                    (GripPoseAction, R + "input/grip/pose"),
                    (HapticAction, L + "output/haptic"),
                    (HapticAction, R + "output/haptic"),
                });
                return all;
            }
            // Touch-style face buttons: A/B on the right, X/Y on the left
            string First(string hand) => hand == R ? "a" : "x";
            string Second(string hand) => hand == R ? "b" : "y";

            Func<Roles, string, string, List<(ulong, string)>> Common(bool faceButtons, bool thumbstickClick, bool menu) => (r, dom, off) =>
            {
                var b = new List<(ulong, string)>
                {
                    (r.Move, off + "input/thumbstick"),
                    (r.Turn, dom + "input/thumbstick"),
                    (r.Select, dom + "input/trigger/value"),
                    (r.MoveArc, off + "input/trigger/value"),
                    (r.Secondary, dom + "input/squeeze/value"),
                    (r.BuildMenu, off + "input/squeeze/value"),
                };
                if (thumbstickClick)
                {
                    b.Add((r.Sprint, off + "input/thumbstick/click"));
                    b.Add((faceButtons ? r.Speed : r.ToggleView, dom + "input/thumbstick/click"));
                }
                if (faceButtons)
                {
                    b.Add((r.ToggleView, dom + "input/" + First(dom) + "/click"));
                    b.Add((r.SwitchMode, dom + "input/" + Second(dom) + "/click"));
                    b.Add((r.Pause, off + "input/" + First(off) + "/click"));
                    b.Add((r.NextSim, off + "input/" + Second(off) + "/click"));
                }
                if (menu) b.Add((r.Menu, L + "input/menu/click")); // (only the left controller has one)
                return b;
            };

            // Steam Frame controllers (SteamVR 2.15.1+ path, then the older one).
            if (frameControllerExtension)
            {
                bool ok = false;
                foreach (var profile in new[] { "/interaction_profiles/valve/frame_controller_valve", "/interaction_profiles/valve/frame_controller" })
                {
                    ok = Suggest(profile, Both(Common(true, true, true))) || Suggest(profile, Both(Common(true, true, false)))
                        || Suggest(profile, Both(Common(false, true, false))) || Suggest(profile, Both(Common(false, false, false)));
                    if (ok) break;
                }
                if (!ok) Log?.Invoke("OpenXR: Steam Frame controller bindings were not accepted; SteamVR will fall back to the Touch bindings.");
            }

            // Oculus Touch - also SteamVR's fallback for controllers without their own bindings.
            Suggest("/interaction_profiles/oculus/touch_controller", Both(Common(true, true, true)));

            // Valve Index: A/B on both hands, no X/Y; the walking hand's trackpad squeeze opens the menu.
            Suggest("/interaction_profiles/valve/index_controller", Both((r, dom, off) => new List<(ulong, string)>
            {
                (r.Move, off + "input/thumbstick"),
                (r.Turn, dom + "input/thumbstick"),
                (r.Sprint, off + "input/thumbstick/click"),
                (r.Speed, dom + "input/thumbstick/click"),
                (r.ToggleView, dom + "input/a/click"),
                (r.SwitchMode, dom + "input/b/click"),
                (r.Pause, off + "input/a/click"),
                (r.NextSim, off + "input/b/click"),
                (r.Menu, off + "input/trackpad/force"),
                (r.Select, dom + "input/trigger/value"),
                (r.Secondary, dom + "input/squeeze/value"),
                (r.BuildMenu, off + "input/squeeze/value"),
                (r.MoveArc, off + "input/trigger/value"),
            }));

            // HTC Vive wands: trackpads for movement.
            Suggest("/interaction_profiles/htc/vive_controller", Both((r, dom, off) => new List<(ulong, string)>
            {
                (r.Move, off + "input/trackpad"),
                (r.Turn, dom + "input/trackpad"),
                (r.Sprint, off + "input/trackpad/click"),
                (r.ToggleView, dom + "input/menu/click"),
                (r.Pause, off + "input/menu/click"),
                (r.SwitchMode, off + "input/squeeze/click"),
                (r.Select, dom + "input/trigger/value"),
                (r.Secondary, dom + "input/squeeze/click"),
                (r.MoveArc, off + "input/trigger/value"),
            }));

            // Lowest common denominator: pointer + click only (no locomotion).
            Suggest("/interaction_profiles/khr/simple_controller", Both((r, dom, off) => new List<(ulong, string)>
            {
                (r.Select, dom + "input/select/click"),
                (r.ToggleView, off + "input/select/click"),
                (r.Pause, off + "input/menu/click"),
                (r.SwitchMode, dom + "input/menu/click"),
            }));

            Log?.Invoke("OpenXR: controller bindings suggested for " + string.Join(", ", BoundProfiles));
        }

        public void Attach(ulong session)
        {
            ulong* sets = stackalloc ulong[3] { RightHanded.Set, LeftHandedRoles.Set, HandsSet };
            var attach = new XrSessionActionSetsAttachInfo
            {
                Type = XrStructureType.SessionActionSetsAttachInfo,
                CountActionSets = 3,
                ActionSets = sets
            };
            Api.Check(Api.AttachSessionActionSets(session, &attach), "xrAttachSessionActionSets");

            LeftAimSpace = CreatePoseSpace(session, AimPoseAction, LeftHand);
            RightAimSpace = CreatePoseSpace(session, AimPoseAction, RightHand);
            LeftGripSpace = CreatePoseSpace(session, GripPoseAction, LeftHand);
            RightGripSpace = CreatePoseSpace(session, GripPoseAction, RightHand);
            if (GazeBound) GazeSpace = CreatePoseSpace(session, GazeAction, 0);
        }

        private ulong CreatePoseSpace(ulong session, ulong action, ulong hand)
        {
            var info = new XrActionSpaceCreateInfo
            {
                Type = XrStructureType.ActionSpaceCreateInfo,
                Action = action,
                SubactionPath = hand,
                PoseInActionSpace = XrPosef.Identity
            };
            ulong space;
            Api.Check(Api.CreateActionSpace(session, &info, &space), "xrCreateActionSpace");
            return space;
        }

        /// <summary>Syncs and reads all actions. Only call while the session is focused.</summary>
        public void Sync(ulong session, ulong baseSpace, long displayTime, float deadZone)
        {
            var roles = LeftHanded ? LeftHandedRoles : RightHanded;
            var active = stackalloc XrActiveActionSet[2];
            active[0] = new XrActiveActionSet { ActionSet = roles.Set, SubactionPath = 0 };
            active[1] = new XrActiveActionSet { ActionSet = HandsSet, SubactionPath = 0 };
            var sync = new XrActionsSyncInfo
            {
                Type = XrStructureType.ActionsSyncInfo,
                CountActiveActionSets = 2,
                ActiveActionSets = active
            };
            var result = Api.SyncActions(session, &sync);
            if (result != XrResult.Success)
            {
                // XR_SESSION_NOT_FOCUSED etc: treat as no input this frame.
                Clear();
                return;
            }
            Active = true;

            Move = ApplyDeadZone(GetVector2(session, roles.Move), deadZone);
            Turn = ApplyDeadZone(GetVector2(session, roles.Turn), deadZone);
            Sprint.Set(GetBool(session, roles.Sprint));
            ToggleView.Set(GetBool(session, roles.ToggleView));
            SwitchMode.Set(GetBool(session, roles.SwitchMode));
            Pause.Set(GetBool(session, roles.Pause));
            Speed.Set(GetBool(session, roles.Speed));
            NextSim.Set(GetBool(session, roles.NextSim));
            BuildMenu.Set(GetBool(session, roles.BuildMenu));
            Menu.Set(GetBool(session, roles.Menu));
            SelectValue = GetFloat(session, roles.Select);
            SecondaryValue = GetFloat(session, roles.Secondary);
            MoveArcValue = GetFloat(session, roles.MoveArc);
            // Hysteresis so a resting finger does not chatter the click.
            Select.Set(Select.Down ? SelectValue > 0.35f : SelectValue > 0.75f);
            Secondary.Set(Secondary.Down ? SecondaryValue > 0.35f : SecondaryValue > 0.75f);
            MoveArc.Set(MoveArc.Down ? MoveArcValue > 0.35f : MoveArcValue > 0.75f);

            LeftAim = Locate(LeftAimSpace, baseSpace, displayTime, false);
            RightAim = Locate(RightAimSpace, baseSpace, displayTime, false);
            LeftGrip = Locate(LeftGripSpace, baseSpace, displayTime, false);
            RightGrip = Locate(RightGripSpace, baseSpace, displayTime, false);
            // (eye gaze only counts while it's really tracked, not a guess)
            Gaze = GazeBound ? Locate(GazeSpace, baseSpace, displayTime, true) : default;
        }

        public void Clear()
        {
            Active = false;
            Move = Turn = Vector2.Zero;
            Sprint.Set(false); ToggleView.Set(false); SwitchMode.Set(false); Pause.Set(false); Speed.Set(false);
            NextSim.Set(false); BuildMenu.Set(false); Select.Set(false); Secondary.Set(false);
            MoveArc.Set(false); Menu.Set(false);
            SelectValue = SecondaryValue = MoveArcValue = 0;
            LeftAim.Valid = RightAim.Valid = LeftGrip.Valid = RightGrip.Valid = Gaze.Valid = false;
        }

        private static Vector2 ApplyDeadZone(Vector2 v, float deadZone)
        {
            var len = v.Length();
            if (len < deadZone) return Vector2.Zero;
            var scaled = (Math.Min(1f, len) - deadZone) / (1f - deadZone);
            return v / len * scaled;
        }

        private Vector2 GetVector2(ulong session, ulong action)
        {
            var info = new XrActionStateGetInfo { Type = XrStructureType.ActionStateGetInfo, Action = action };
            var state = new XrActionStateVector2f { Type = XrStructureType.ActionStateVector2f };
            if (Api.GetActionStateVector2f(session, &info, &state) != XrResult.Success || state.IsActive == 0) return Vector2.Zero;
            return new Vector2(state.CurrentState.X, state.CurrentState.Y);
        }

        private bool GetBool(ulong session, ulong action)
        {
            var info = new XrActionStateGetInfo { Type = XrStructureType.ActionStateGetInfo, Action = action };
            var state = new XrActionStateBoolean { Type = XrStructureType.ActionStateBoolean };
            if (Api.GetActionStateBoolean(session, &info, &state) != XrResult.Success || state.IsActive == 0) return false;
            return state.CurrentState != 0;
        }

        private float GetFloat(ulong session, ulong action)
        {
            var info = new XrActionStateGetInfo { Type = XrStructureType.ActionStateGetInfo, Action = action };
            var state = new XrActionStateFloat { Type = XrStructureType.ActionStateFloat };
            if (Api.GetActionStateFloat(session, &info, &state) != XrResult.Success || state.IsActive == 0) return 0;
            return state.CurrentState;
        }

        private VRPose Locate(ulong space, ulong baseSpace, long time, bool mustBeTracked)
        {
            if (space == 0 || baseSpace == 0) return default;
            var loc = new XrSpaceLocation { Type = XrStructureType.SpaceLocation };
            if (Api.LocateSpace(space, baseSpace, time, &loc) != XrResult.Success) return default;
            const ulong both = XrConst.SpaceLocationOrientationValid | XrConst.SpaceLocationPositionValid;
            if ((loc.LocationFlags & both) != both) return default;
            if (mustBeTracked && (loc.LocationFlags & XrConst.SpaceLocationOrientationTracked) == 0) return default;
            return new VRPose
            {
                Valid = true,
                Position = VRMath.ToXna(loc.Pose.Position),
                Orientation = VRMath.ToXna(loc.Pose.Orientation)
            };
        }

        /// <summary>A buzz in the pointing hand (true) or the walking hand (false).</summary>
        public void Haptic(ulong session, bool pointingHand, float amplitude, float seconds)
        {
            if (session == 0 || HapticAction == 0) return;
            var right = pointingHand != LeftHanded;
            var info = new XrHapticActionInfo
            {
                Type = XrStructureType.HapticActionInfo,
                Action = HapticAction,
                SubactionPath = right ? RightHand : LeftHand
            };
            var vibration = new XrHapticVibration
            {
                Type = XrStructureType.HapticVibration,
                Duration = (long)(seconds * 1e9),
                Frequency = XrConst.FrequencyUnspecified,
                Amplitude = MathHelper.Clamp(amplitude, 0, 1)
            };
            Api.ApplyHapticFeedback(session, &info, &vibration);
        }

        public void Dispose()
        {
            if (Api == null) return;
            foreach (var space in new[] { LeftAimSpace, RightAimSpace, LeftGripSpace, RightGripSpace, GazeSpace })
                if (space != 0) Api.DestroySpace(space);
            LeftAimSpace = RightAimSpace = LeftGripSpace = RightGripSpace = GazeSpace = 0;
            foreach (var set in new[] { RightHanded?.Set ?? 0, LeftHandedRoles?.Set ?? 0, HandsSet })
                if (set != 0) Api.DestroyActionSet(set);
            RightHanded = LeftHandedRoles = null;
            HandsSet = 0;
        }
    }
}
