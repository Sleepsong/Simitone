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
    /// OpenXR action set for Simitone. Default bindings:
    ///
    ///   Left stick ........ move (smooth locomotion)           Left stick click ... sprint (hold)
    ///   Right stick X ..... snap / smooth turn                 Right stick Y ...... floor up / down
    ///   A (right) ......... toggle immersive view / screen     B (right) .......... recentre
    ///   X (left) .......... pause / resume the game            Y (left) ........... cycle game speed
    ///   Right trigger ..... click (virtual screen)             Right grip ......... right click (virtual screen)
    ///
    /// Bindings are suggested for the Steam Frame controller, Oculus Touch, Valve Index, HTC Vive and the KHR
    /// simple controller. SteamVR also lets players rebind everything in its controller binding UI.
    /// </summary>
    public unsafe sealed class VRInput : IDisposable
    {
        public const string FrameControllerExtension = "XR_VALVE_frame_controller_interaction";

        private XrApi Api;
        private ulong ActionSet;
        private ulong LeftHand, RightHand;

        private ulong MoveAction, TurnAction, SprintAction;
        private ulong ToggleViewAction, RecenterAction, PauseAction, SpeedAction;
        private ulong SelectAction, SecondaryAction, AimPoseAction, HapticAction;
        private ulong LeftAimSpace, RightAimSpace;

        public Vector2 Move;
        public Vector2 Turn;
        public VRButton Sprint, ToggleView, Recenter, Pause, Speed, Select, Secondary;
        public float SelectValue, SecondaryValue;
        public VRPose LeftAim, RightAim;
        public bool Active;
        public List<string> BoundProfiles = new List<string>();

        private Action<string> Log;

        public VRInput(Action<string> log)
        {
            Log = log;
        }

        public void Create(XrApi api, bool frameControllerExtension)
        {
            Api = api;
            LeftHand = api.Path("/user/hand/left");
            RightHand = api.Path("/user/hand/right");

            var info = new XrActionSetCreateInfo { Type = XrStructureType.ActionSetCreateInfo, Priority = 0 };
            XrConst.WriteString(info.ActionSetName, XrConst.MaxActionSetNameSize, "simitone");
            XrConst.WriteString(info.LocalizedActionSetName, XrConst.MaxLocalizedActionSetNameSize, "Simitone");
            ulong set;
            api.Check(api.CreateActionSet(api.Instance, &info, &set), "xrCreateActionSet");
            ActionSet = set;

            MoveAction = CreateAction("move", "Move", XrActionType.Vector2fInput, false);
            TurnAction = CreateAction("turn", "Turn / change floor", XrActionType.Vector2fInput, false);
            SprintAction = CreateAction("sprint", "Sprint", XrActionType.BooleanInput, false);
            ToggleViewAction = CreateAction("toggle_view", "Toggle immersive view", XrActionType.BooleanInput, false);
            RecenterAction = CreateAction("recenter", "Recentre", XrActionType.BooleanInput, false);
            PauseAction = CreateAction("pause", "Pause / resume", XrActionType.BooleanInput, false);
            SpeedAction = CreateAction("speed", "Cycle game speed", XrActionType.BooleanInput, false);
            SelectAction = CreateAction("select", "Click", XrActionType.FloatInput, false);
            SecondaryAction = CreateAction("secondary", "Right click", XrActionType.FloatInput, false);
            AimPoseAction = CreateAction("aim", "Pointer", XrActionType.PoseInput, true);
            HapticAction = CreateAction("haptic", "Haptics", XrActionType.VibrationOutput, true);

            SuggestBindings(frameControllerExtension);
        }

        private ulong CreateAction(string name, string localized, XrActionType type, bool bothHands)
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
            Api.Check(Api.CreateAction(ActionSet, &info, &action), "xrCreateAction(" + name + ")");
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

            List<(ulong, string)> Common(bool faceButtons, bool thumbstickClick)
            {
                var b = new List<(ulong, string)>
                {
                    (MoveAction, L + "input/thumbstick"),
                    (TurnAction, R + "input/thumbstick"),
                    (SelectAction, R + "input/trigger/value"),
                    (SecondaryAction, R + "input/squeeze/value"),
                    (AimPoseAction, L + "input/aim/pose"),
                    (AimPoseAction, R + "input/aim/pose"),
                    (HapticAction, L + "output/haptic"),
                    (HapticAction, R + "output/haptic"),
                };
                if (thumbstickClick) b.Add((SprintAction, L + "input/thumbstick/click"));
                if (faceButtons)
                {
                    b.Add((ToggleViewAction, R + "input/a/click"));
                    b.Add((RecenterAction, R + "input/b/click"));
                    b.Add((PauseAction, L + "input/x/click"));
                    b.Add((SpeedAction, L + "input/y/click"));
                }
                else if (thumbstickClick)
                {
                    b.Add((ToggleViewAction, R + "input/thumbstick/click"));
                }
                return b;
            }

            // Steam Frame controllers (SteamVR 2.15.1+ path, then the older one).
            if (frameControllerExtension)
            {
                bool ok = false;
                foreach (var profile in new[] { "/interaction_profiles/valve/frame_controller_valve", "/interaction_profiles/valve/frame_controller" })
                {
                    ok = Suggest(profile, Common(true, true)) || Suggest(profile, Common(false, true)) || Suggest(profile, Common(false, false));
                    if (ok) break;
                }
                if (!ok) Log?.Invoke("OpenXR: Steam Frame controller bindings were not accepted; SteamVR will fall back to the Touch bindings.");
            }

            // Oculus Touch - also SteamVR's fallback for controllers without their own bindings.
            var touch = Common(true, true);
            Suggest("/interaction_profiles/oculus/touch_controller", touch);

            // Valve Index: A/B on both hands, no X/Y.
            Suggest("/interaction_profiles/valve/index_controller", new List<(ulong, string)>
            {
                (MoveAction, L + "input/thumbstick"),
                (TurnAction, R + "input/thumbstick"),
                (SprintAction, L + "input/thumbstick/click"),
                (ToggleViewAction, R + "input/a/click"),
                (RecenterAction, R + "input/b/click"),
                (PauseAction, L + "input/a/click"),
                (SpeedAction, L + "input/b/click"),
                (SelectAction, R + "input/trigger/value"),
                (SecondaryAction, R + "input/squeeze/value"),
                (AimPoseAction, L + "input/aim/pose"),
                (AimPoseAction, R + "input/aim/pose"),
                (HapticAction, L + "output/haptic"),
                (HapticAction, R + "output/haptic"),
            });

            // HTC Vive wands: trackpads for movement.
            Suggest("/interaction_profiles/htc/vive_controller", new List<(ulong, string)>
            {
                (MoveAction, L + "input/trackpad"),
                (TurnAction, R + "input/trackpad"),
                (SprintAction, L + "input/trackpad/click"),
                (ToggleViewAction, R + "input/menu/click"),
                (PauseAction, L + "input/menu/click"),
                (SelectAction, R + "input/trigger/value"),
                (SecondaryAction, R + "input/squeeze/click"),
                (AimPoseAction, L + "input/aim/pose"),
                (AimPoseAction, R + "input/aim/pose"),
                (HapticAction, L + "output/haptic"),
                (HapticAction, R + "output/haptic"),
            });

            // Lowest common denominator: pointer + click only (no locomotion).
            Suggest("/interaction_profiles/khr/simple_controller", new List<(ulong, string)>
            {
                (SelectAction, R + "input/select/click"),
                (ToggleViewAction, L + "input/select/click"),
                (PauseAction, L + "input/menu/click"),
                (AimPoseAction, L + "input/aim/pose"),
                (AimPoseAction, R + "input/aim/pose"),
                (HapticAction, L + "output/haptic"),
                (HapticAction, R + "output/haptic"),
            });

            Log?.Invoke("OpenXR: controller bindings suggested for " + string.Join(", ", BoundProfiles));
        }

        public void Attach(ulong session)
        {
            ulong set = ActionSet;
            var attach = new XrSessionActionSetsAttachInfo
            {
                Type = XrStructureType.SessionActionSetsAttachInfo,
                CountActionSets = 1,
                ActionSets = &set
            };
            Api.Check(Api.AttachSessionActionSets(session, &attach), "xrAttachSessionActionSets");

            LeftAimSpace = CreatePoseSpace(session, LeftHand);
            RightAimSpace = CreatePoseSpace(session, RightHand);
        }

        private ulong CreatePoseSpace(ulong session, ulong hand)
        {
            var info = new XrActionSpaceCreateInfo
            {
                Type = XrStructureType.ActionSpaceCreateInfo,
                Action = AimPoseAction,
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
            var active = new XrActiveActionSet { ActionSet = ActionSet, SubactionPath = 0 };
            var sync = new XrActionsSyncInfo
            {
                Type = XrStructureType.ActionsSyncInfo,
                CountActiveActionSets = 1,
                ActiveActionSets = &active
            };
            var result = Api.SyncActions(session, &sync);
            if (result != XrResult.Success)
            {
                // XR_SESSION_NOT_FOCUSED etc: treat as no input this frame.
                Clear();
                return;
            }
            Active = true;

            Move = ApplyDeadZone(GetVector2(session, MoveAction), deadZone);
            Turn = ApplyDeadZone(GetVector2(session, TurnAction), deadZone);
            Sprint.Set(GetBool(session, SprintAction));
            ToggleView.Set(GetBool(session, ToggleViewAction));
            Recenter.Set(GetBool(session, RecenterAction));
            Pause.Set(GetBool(session, PauseAction));
            Speed.Set(GetBool(session, SpeedAction));
            SelectValue = GetFloat(session, SelectAction);
            SecondaryValue = GetFloat(session, SecondaryAction);
            // Hysteresis so a resting finger does not chatter the click.
            Select.Set(Select.Down ? SelectValue > 0.35f : SelectValue > 0.75f);
            Secondary.Set(Secondary.Down ? SecondaryValue > 0.35f : SecondaryValue > 0.75f);

            LeftAim = Locate(LeftAimSpace, baseSpace, displayTime);
            RightAim = Locate(RightAimSpace, baseSpace, displayTime);
        }

        public void Clear()
        {
            Active = false;
            Move = Turn = Vector2.Zero;
            Sprint.Set(false); ToggleView.Set(false); Recenter.Set(false); Pause.Set(false); Speed.Set(false);
            Select.Set(false); Secondary.Set(false);
            SelectValue = SecondaryValue = 0;
            LeftAim.Valid = RightAim.Valid = false;
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

        private VRPose Locate(ulong space, ulong baseSpace, long time)
        {
            if (space == 0 || baseSpace == 0) return default;
            var loc = new XrSpaceLocation { Type = XrStructureType.SpaceLocation };
            if (Api.LocateSpace(space, baseSpace, time, &loc) != XrResult.Success) return default;
            const ulong both = XrConst.SpaceLocationOrientationValid | XrConst.SpaceLocationPositionValid;
            if ((loc.LocationFlags & both) != both) return default;
            return new VRPose
            {
                Valid = true,
                Position = VRMath.ToXna(loc.Pose.Position),
                Orientation = VRMath.ToXna(loc.Pose.Orientation)
            };
        }

        public void Haptic(ulong session, bool rightHand, float amplitude, float seconds)
        {
            if (session == 0 || HapticAction == 0) return;
            var info = new XrHapticActionInfo
            {
                Type = XrStructureType.HapticActionInfo,
                Action = HapticAction,
                SubactionPath = rightHand ? RightHand : LeftHand
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
            if (LeftAimSpace != 0) Api.DestroySpace(LeftAimSpace);
            if (RightAimSpace != 0) Api.DestroySpace(RightAimSpace);
            if (ActionSet != 0) Api.DestroyActionSet(ActionSet);
            LeftAimSpace = RightAimSpace = ActionSet = 0;
        }
    }
}
