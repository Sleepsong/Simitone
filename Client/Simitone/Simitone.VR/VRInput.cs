using System;
using System.Collections.Generic;
using System.Linq;
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

        private bool Suggest(string profile, List<(ulong action, string path)> bindings, bool quiet = false)
        {
            ulong profilePath;
            try { profilePath = Api.Path(profile); }
            catch (XrException e) { if (!quiet) Log?.Invoke("OpenXR: " + profile + ": " + e.Message); return false; }

            var list = new XrActionSuggestedBinding[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
            {
                try
                {
                    list[i] = new XrActionSuggestedBinding { Action = bindings[i].action, Binding = Api.Path(bindings[i].path) };
                }
                catch (XrException e)
                {
                    if (!quiet) Log?.Invoke("OpenXR: bad binding path " + bindings[i].path + ": " + e.Message);
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
                    if (!quiet) Log?.Invoke($"OpenXR: bindings for {profile} rejected: {Api.ResultName(result)}");
                    return false;
                }
            }
            if (!quiet && !BoundProfiles.Contains(profile)) BoundProfiles.Add(profile);
            return true;
        }

        private ulong ProbeSet, ProbeBool, ProbeFloat, ProbeVector, ProbePose, ProbeHaptic;

        /// <summary>
        /// Which input paths (for each hand, e.g. "/user/hand/left/input/a/click") a profile has: each likely one
        /// suggested on its own, to probe actions of its type in a set that's never used. Empty if the runtime knows
        /// none of them (or not the profile).
        /// </summary>
        private HashSet<string> ProbeComponents(string profile)
        {
            if (ProbeSet == 0)
            {
                ProbeSet = CreateSet("simitone_probe", "Simitone (finding buttons)", 0);
                ProbeBool = CreateAction(ProbeSet, "probe_bool", "Probe (button)", XrActionType.BooleanInput, true);
                ProbeFloat = CreateAction(ProbeSet, "probe_float", "Probe (value)", XrActionType.FloatInput, true);
                ProbeVector = CreateAction(ProbeSet, "probe_vector", "Probe (stick)", XrActionType.Vector2fInput, true);
                ProbePose = CreateAction(ProbeSet, "probe_pose", "Probe (pose)", XrActionType.PoseInput, true);
                ProbeHaptic = CreateAction(ProbeSet, "probe_haptic", "Probe (haptic)", XrActionType.VibrationOutput, true);
            }
            var candidates = new List<(string component, ulong action)>();
            foreach (var c in new[] { "thumbstick", "trackpad" })
            {
                candidates.Add(("input/" + c, ProbeVector));
                candidates.Add(("input/" + c + "/click", ProbeBool));
                candidates.Add(("input/" + c + "/touch", ProbeBool));
            }
            candidates.Add(("input/trackpad/force", ProbeFloat));
            foreach (var c in new[] { "trigger", "squeeze" })
            {
                candidates.Add(("input/" + c + "/value", ProbeFloat));
                candidates.Add(("input/" + c + "/click", ProbeBool));
                candidates.Add(("input/" + c + "/touch", ProbeBool));
                candidates.Add(("input/" + c + "/force", ProbeFloat));
            }
            foreach (var c in new[] { "a", "b", "x", "y", "menu", "view", "start", "select", "back", "system", "thumbrest",
                "dpad_up", "dpad_down", "dpad_left", "dpad_right", "bumper", "shoulder", "grip" })
            {
                candidates.Add(("input/" + c + "/click", ProbeBool));
                candidates.Add(("input/" + c + "/touch", ProbeBool));
            }
            foreach (var c in new[] { "aim", "grip", "grip_surface", "palm_ext" }) candidates.Add(("input/" + c + "/pose", ProbePose));
            candidates.Add(("output/haptic", ProbeHaptic));

            var known = new HashSet<string>();
            foreach (var hand in new[] { "/user/hand/left/", "/user/hand/right/" })
                foreach (var (component, action) in candidates)
                    if (Suggest(profile, new List<(ulong, string)> { (action, hand + component) }, quiet: true)) known.Add(hand + component);
            return known;
        }

        private static string Components(HashSet<string> known, string hand)
        {
            var list = known.Where(k => k.StartsWith(hand)).Select(k => k.Substring(hand.Length)).OrderBy(k => k).ToList();
            return list.Count == 0 ? "nothing" : string.Join(" ", list);
        }

        /// <summary>
        /// The Steam Frame controllers' bindings, from the paths they have (<paramref name="known"/>): for each action the
        /// first of its likely inputs that's there. Face buttons are A/B on the right and X/Y on the left if it has
        /// those, else A/B on both (as the Index); without them the pointing stick's click toggles the view.
        /// </summary>
        private static List<(ulong, string)> FrameLayout(Roles r, string dom, string off, HashSet<string> known)
        {
            const string L = "/user/hand/left/", R = "/user/hand/right/";
            var b = new List<(ulong, string)>();
            string Pick(params string[] paths) => paths.FirstOrDefault(known.Contains);
            void Bind(ulong action, params string[] paths)
            {
                var p = Pick(paths);
                if (p != null) b.Add((action, p));
            }
            Bind(r.Move, off + "input/thumbstick", off + "input/trackpad");
            Bind(r.Turn, dom + "input/thumbstick", dom + "input/trackpad");
            Bind(r.Select, dom + "input/trigger/value", dom + "input/trigger/click");
            Bind(r.MoveArc, off + "input/trigger/value", off + "input/trigger/click");
            Bind(r.Secondary, dom + "input/squeeze/value", dom + "input/squeeze/force", dom + "input/squeeze/click", dom + "input/grip/click");
            Bind(r.BuildMenu, off + "input/squeeze/value", off + "input/squeeze/force", off + "input/squeeze/click", off + "input/grip/click");
            Bind(r.Sprint, off + "input/thumbstick/click", off + "input/trackpad/click");
            string First(string hand) => hand == R ? "a" : "x";
            string Second(string hand) => hand == R ? "b" : "y";
            // (the Steam Frame's right controller has all four, A B X Y, as a gamepad does, and the left a D-pad: so on
            // the right hand X and Y pause and go to the next Sim; a hand without face buttons uses its D-pad)
            var domFirst = Pick(dom + "input/" + First(dom) + "/click", dom + "input/a/click", dom + "input/dpad_down/click");
            var domSecond = Pick(dom + "input/" + Second(dom) + "/click", dom + "input/b/click", dom + "input/dpad_right/click");
            var offFirst = Pick(off + "input/" + First(off) + "/click", off + "input/a/click", dom + "input/x/click", off + "input/dpad_down/click");
            var offSecond = Pick(off + "input/" + Second(off) + "/click", off + "input/b/click", dom + "input/y/click", off + "input/dpad_right/click");
            if (offFirst == domFirst) offFirst = Pick(off + "input/dpad_down/click");
            if (offSecond == domSecond) offSecond = Pick(off + "input/dpad_right/click");
            var stickClick = Pick(dom + "input/thumbstick/click", dom + "input/trackpad/click");
            if (domFirst != null)
            {
                b.Add((r.ToggleView, domFirst));
                if (stickClick != null) b.Add((r.Speed, stickClick));
            }
            else if (stickClick != null) b.Add((r.ToggleView, stickClick));
            if (domSecond != null) b.Add((r.SwitchMode, domSecond));
            if (offFirst != null) b.Add((r.Pause, offFirst));
            if (offSecond != null) b.Add((r.NextSim, offSecond));
            Bind(r.Menu, L + "input/menu/click", R + "input/menu/click", L + "input/view/click", L + "input/start/click",
                L + "input/select/click", L + "input/back/click", R + "input/start/click", R + "input/view/click");
            return b;
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

            // Steam Frame controllers (SteamVR 2.15.1+ path, then the older one). Their extension isn't in the public
            // registry, so the names of their buttons are found by asking: each likely one is suggested on its own, and
            // the bindings are made from those SteamVR takes (a suggestion with any unknown path is refused whole).
            // (SIMITONE_XR_PROBE_PROFILE=<profile> probes another profile the same way, to test this on a runtime without
            // the Frame's; that profile's own bindings are suggested after, as usual)
            var probeOther = Environment.GetEnvironmentVariable("SIMITONE_XR_PROBE_PROFILE");
            if (frameControllerExtension || !string.IsNullOrEmpty(probeOther))
            {
                bool ok = false;
                var profiles = string.IsNullOrEmpty(probeOther)
                    ? new[] { "/interaction_profiles/valve/frame_controller_valve", "/interaction_profiles/valve/frame_controller" }
                    : new[] { probeOther };
                foreach (var profile in profiles)
                {
                    var known = ProbeComponents(profile);
                    if (known.Count == 0) continue;
                    Log?.Invoke($"OpenXR: {profile} has: left {Components(known, L)}; right {Components(known, R)}");
                    var bindings = Both((r, dom, off) => FrameLayout(r, dom, off, known)).Where(b => known.Contains(b.Item2)).ToList();
                    ok = Suggest(profile, bindings);
                    Log?.Invoke($"OpenXR: {profile}: {bindings.Count} bindings from what it has {(ok ? "accepted" : "refused")}");
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
            if (result != LastSyncResult)
            {
                // (what the runtime says when it changes: input only comes with XR_SUCCESS)
                if (result != XrResult.Success || LastSyncResult != (XrResult)int.MinValue)
                    Log?.Invoke("OpenXR: xrSyncActions: " + Api.ResultName(result));
                LastSyncResult = result;
            }
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

            // the first time each control does something, for the log (did the input arrive?)
            First("walking stick", Move != Vector2.Zero, Move.ToString());
            First("turning stick", Turn != Vector2.Zero, Turn.ToString());
            First("pointing trigger", SelectValue > 0.05f, SelectValue.ToString("0.00"));
            First("pointing grip", SecondaryValue > 0.05f, SecondaryValue.ToString("0.00"));
            First("walking trigger", MoveArcValue > 0.05f, MoveArcValue.ToString("0.00"));
            First("walking grip", BuildMenu.Down, "");
            First("walking stick click", Sprint.Down, "");
            First("pointing stick click", Speed.Down, "");
            First("view button", ToggleView.Down, "");
            First("mode button", SwitchMode.Down, "");
            First("pause button", Pause.Down, "");
            First("next Sim button", NextSim.Down, "");
            First("menu button", Menu.Down, "");
            First("left controller pose", LeftAim.Valid, "");
            First("right controller pose", RightAim.Valid, "");
        }

        private XrResult LastSyncResult = (XrResult)int.MinValue;
        private readonly HashSet<string> SeenInput = new HashSet<string>();

        private void First(string name, bool now, string value)
        {
            if (now && SeenInput.Add(name)) Log?.Invoke("OpenXR: first input: " + name + (value.Length > 0 ? " " + value : ""));
        }

        /// <summary>
        /// Logs what each action is bound to now, as the runtime says (its own names for the controls, e.g. "Right Hand
        /// Trigger"), or that it's bound to nothing: call when the controllers change.
        /// </summary>
        public void LogBindings(ulong session)
        {
            if (Api == null || Api.EnumerateBoundSourcesForAction == null || session == 0) return;
            var r = LeftHanded ? LeftHandedRoles : RightHanded;
            var actions = new (string name, ulong action)[]
            {
                ("move", r.Move), ("turn", r.Turn), ("select", r.Select), ("secondary", r.Secondary), ("move_arc", r.MoveArc),
                ("build_menu", r.BuildMenu), ("sprint", r.Sprint), ("speed", r.Speed), ("toggle_view", r.ToggleView),
                ("switch_mode", r.SwitchMode), ("pause", r.Pause), ("next_sim", r.NextSim), ("vr_menu", r.Menu),
                ("aim", AimPoseAction), ("grip", GripPoseAction), ("haptic", HapticAction)
            };
            var parts = new List<string>();
            foreach (var (name, action) in actions)
            {
                var info = new XrBoundSourcesForActionEnumerateInfo { Type = XrStructureType.BoundSourcesForActionEnumerateInfo, Action = action };
                uint count = 0;
                var result = Api.EnumerateBoundSourcesForAction(session, &info, 0, &count, null);
                if ((int)result < 0) { parts.Add(name + ": (" + Api.ResultName(result) + ")"); continue; }
                var sources = new ulong[count];
                if (count > 0)
                    fixed (ulong* p = sources) Api.EnumerateBoundSourcesForAction(session, &info, count, &count, p);
                var names = sources.Take((int)count).Select(source => SourceName(session, source)).ToList();
                parts.Add(name + ": " + (names.Count == 0 ? "nothing" : string.Join(" / ", names)));
            }
            Log?.Invoke("OpenXR: bound (" + (LeftHanded ? "left-handed" : "right-handed") + "): " + string.Join("; ", parts));
        }

        private string SourceName(ulong session, ulong source)
        {
            var path = Api.PathString(source);
            if (Api.GetInputSourceLocalizedName == null) return path;
            var info = new XrInputSourceLocalizedNameGetInfo
            {
                Type = XrStructureType.InputSourceLocalizedNameGetInfo,
                SourcePath = source,
                WhichComponents = 1 | 4 // (which hand, and which control)
            };
            uint count = 0;
            if (Api.GetInputSourceLocalizedName(session, &info, 0, &count, null) != XrResult.Success || count == 0) return path;
            var buffer = new byte[count];
            fixed (byte* p = buffer)
                if (Api.GetInputSourceLocalizedName(session, &info, count, &count, p) != XrResult.Success) return path;
            var localized = System.Text.Encoding.UTF8.GetString(buffer, 0, Math.Max(0, (int)count - 1));
            return localized.Length > 0 ? $"{localized} ({path})" : path;
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
