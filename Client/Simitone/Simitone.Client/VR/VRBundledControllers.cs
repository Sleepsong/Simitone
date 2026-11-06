using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Simitone.VR;
using Simitone.VR.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Controller models shipped with Simitone (VRControllers/, from the MIT-licensed webxr-input-profiles assets) for
    /// runtimes that don't provide their own: Meta's PC runtime (Quest and Rift over Link) and Monado with Vive wands.
    /// The model follows the controller type the runtime says each hand is using (its interaction profile; for Meta's
    /// Touch controllers, the headset's name tells which generation). Its trigger, grip, stick and buttons move as you
    /// use them, as each model's profile.json describes (the WebXR motion-controller format: a node of the model moves
    /// between a "min" and a "max" node by a button's value or a stick's position, or is shown only when touched).
    /// </summary>
    public sealed class VRBundledControllers
    {
        /// <summary>One way a part of the model moves (a profile.json "visualResponse").</summary>
        internal sealed class Response
        {
            public string Component;   // "xr-standard-trigger", "a-button"...
            public string Property;    // "button", "xAxis", "yAxis", "state"
            public string[] States;    // which states it moves in: "default", "touched", "pressed"
            public bool Visibility;    // shows/hides the node instead of moving it
            public int Slot;           // index into the animated nodes
            public Matrix Min, Max;
        }

        /// <summary>A model for one hand, and how its parts move.</summary>
        public sealed class Layout
        {
            public string Key;
            public byte[] Glb;
            public string ProfileJson;
            internal Response[] Responses;
            public VRDeviceModel Device; // (the node poses for this frame, as the runtime's models carry them)
            internal bool Prepared;
        }

        private readonly string Folder;
        private readonly Dictionary<string, Layout> Layouts = new Dictionary<string, Layout>();
        private readonly HashSet<string> Missing = new HashSet<string>();
        private readonly Action<string> Log;

        public VRBundledControllers(string folder, Action<string> log)
        {
            Folder = folder;
            Log = log;
        }

        /// <summary>
        /// The folder of the model for a controller type ("" for none we have). Meta's runtime reports every Touch
        /// generation as the same type, so the headset decides which one it is.
        /// </summary>
        public static string ProfileFolder(string interactionProfile, string systemName)
        {
            var system = (systemName ?? "").Replace(" ", "").ToLowerInvariant();
            switch (interactionProfile ?? "")
            {
                case "/interaction_profiles/oculus/touch_controller":
                    if (system.Contains("quest3")) return "meta-quest-touch-plus";
                    if (system.Contains("questpro")) return "meta-quest-touch-pro";
                    if (system.Contains("quest2")) return "oculus-touch-v3";
                    if (system.Contains("rift") || system.EndsWith("quest")) return "oculus-touch-v2";
                    return "oculus-touch-v3";
                case "/interaction_profiles/meta/touch_controller_plus":
                case "/interaction_profiles/meta/touch_plus_controller":
                    return "meta-quest-touch-plus";
                case "/interaction_profiles/facebook/touch_controller_pro":
                    return "meta-quest-touch-pro";
                case "/interaction_profiles/htc/vive_controller":
                    return "htc-vive";
                default:
                    return "";
            }
        }

        /// <summary>The model for a hand (0 left, 1 right) using this controller type, or null if we have none.</summary>
        public Layout For(int hand, string interactionProfile, string systemName)
        {
            var folder = ProfileFolder(interactionProfile, systemName);
            if (folder == "") return null;
            var key = folder + "/" + (hand == 0 ? "left" : "right");
            if (Layouts.TryGetValue(key, out var layout)) return layout;
            if (Missing.Contains(key)) return null;
            try
            {
                var json = File.ReadAllText(Path.Combine(Folder, folder, "profile.json"));
                using var doc = JsonDocument.Parse(json);
                var layouts = doc.RootElement.GetProperty("layouts");
                if (!layouts.TryGetProperty(hand == 0 ? "left" : "right", out var entry) && !layouts.TryGetProperty("none", out entry))
                    throw new InvalidDataException("no layout for this hand");
                var asset = entry.GetProperty("assetPath").GetString();
                layout = new Layout
                {
                    Key = key,
                    Glb = File.ReadAllBytes(Path.Combine(Folder, folder, asset)),
                    ProfileJson = entry.GetRawText()
                };
                Layouts[key] = layout;
                Log?.Invoke($"controller model {key}: from VRControllers ({asset})");
                return layout;
            }
            catch (Exception e)
            {
                Missing.Add(key);
                Log?.Invoke($"controller model {key} not available: {e.Message}");
                return null;
            }
        }

        /// <summary>Reads which nodes move and how, once the model is parsed (needs its nodes' rest transforms).</summary>
        public static void Prepare(Layout layout, GltfAsset asset)
        {
            if (layout.Prepared) return;
            layout.Prepared = true;
            var byName = new Dictionary<string, int>();
            for (int i = 0; i < asset.Nodes.Length; i++)
                if (asset.Nodes[i].Name != null && !byName.ContainsKey(asset.Nodes[i].Name)) byName[asset.Nodes[i].Name] = i;
            var responses = new List<Response>();
            var names = new List<string>();
            using (var doc = JsonDocument.Parse(layout.ProfileJson))
            {
                foreach (var component in doc.RootElement.GetProperty("components").EnumerateObject())
                {
                    if (!component.Value.TryGetProperty("visualResponses", out var visual)) continue;
                    foreach (var response in visual.EnumerateObject())
                    {
                        var r = response.Value;
                        var valueName = r.GetProperty("valueNodeName").GetString();
                        if (valueName == null || !byName.ContainsKey(valueName)) continue;
                        var visibility = r.TryGetProperty("valueNodeProperty", out var vp) && vp.GetString() == "visibility";
                        Matrix min = Matrix.Identity, max = Matrix.Identity;
                        if (!visibility)
                        {
                            if (!r.TryGetProperty("minNodeName", out var mn) || !r.TryGetProperty("maxNodeName", out var mx)
                                || !byName.TryGetValue(mn.GetString() ?? "", out var minIndex) || !byName.TryGetValue(mx.GetString() ?? "", out var maxIndex))
                                continue;
                            min = asset.Nodes[minIndex].Local;
                            max = asset.Nodes[maxIndex].Local;
                        }
                        var states = new List<string>();
                        if (r.TryGetProperty("states", out var st)) foreach (var s in st.EnumerateArray()) states.Add(s.GetString());
                        var slot = names.IndexOf(valueName);
                        if (slot < 0) { slot = names.Count; names.Add(valueName); }
                        responses.Add(new Response
                        {
                            Component = component.Name,
                            Property = r.TryGetProperty("componentProperty", out var cp) ? cp.GetString() : "button",
                            States = states.ToArray(),
                            Visibility = visibility,
                            Slot = slot,
                            Min = min,
                            Max = max
                        });
                    }
                }
            }
            layout.Responses = responses.ToArray();
            layout.Device = new VRDeviceModel
            {
                CacheId = layout.Key,
                AnimatedNodes = names.ToArray(),
                NodePoses = new Matrix[names.Count],
                NodeVisible = new bool[names.Count]
            };
            for (int i = 0; i < names.Count; i++)
            {
                layout.Device.NodePoses[i] = asset.Nodes[byName[names[i]]].Local;
                layout.Device.NodeVisible[i] = true;
            }
        }

        /// <summary>What one hand's controls are doing, as the model shows it.</summary>
        public struct HandState
        {
            public float Trigger, Squeeze;
            public Vector2 Stick; // (+Y up)
            public bool StickClick, Lower, Upper, Menu; // (lower/upper face button: A/B on the right, X/Y on the left)
        }

        /// <summary>
        /// The controls of each hand (0 left, 1 right) from the game's actions, which are bound per role (pointing or
        /// walking hand), mirrored when left-handed.
        /// </summary>
        public static HandState Hand(VRInput input, int hand)
        {
            var pointing = (hand == 1) != input.LeftHanded;
            var state = pointing
                ? new HandState { Trigger = input.SelectValue, Squeeze = input.SecondaryValue, Stick = input.Turn, StickClick = input.Speed.Down,
                    Lower = input.ToggleView.Down, Upper = input.SwitchMode.Down }
                : new HandState { Trigger = input.MoveArcValue, Squeeze = input.BuildMenu.Down ? 1 : 0, Stick = input.Move, StickClick = input.Sprint.Down,
                    Lower = input.Pause.Down, Upper = input.NextSim.Down };
            state.Menu = hand == 0 && input.Menu.Down;
            return state;
        }

        /// <summary>Moves the model's parts for this frame (WebXR motion-controller rules).</summary>
        public static void Animate(Layout layout, HandState hand)
        {
            if (layout.Responses == null || layout.Device == null) return;
            foreach (var r in layout.Responses)
            {
                // the component's value and state
                float button = 0, x = 0, y = 0;
                bool pressed = false, touched = false;
                switch (r.Component)
                {
                    case "xr-standard-trigger": button = hand.Trigger; break;
                    case "xr-standard-squeeze": button = hand.Squeeze; break;
                    case "xr-standard-thumbstick":
                    case "xr-standard-touchpad":
                        x = hand.Stick.X; y = -hand.Stick.Y; // (WebXR: +y is down)
                        button = hand.StickClick ? 1 : 0;
                        touched = hand.Stick.LengthSquared() > 0.0001f;
                        break;
                    case "a-button": case "x-button": button = hand.Lower ? 1 : 0; break;
                    case "b-button": case "y-button": button = hand.Upper ? 1 : 0; break;
                    case "menu": button = hand.Menu ? 1 : 0; break;
                }
                pressed |= button > 0.5f;
                touched |= button > 0;
                var state = pressed ? "pressed" : touched ? "touched" : "default";
                var active = Array.IndexOf(r.States, state) >= 0;

                if (r.Visibility)
                {
                    layout.Device.NodeVisible[r.Slot] = active;
                    continue;
                }
                float value = r.Property switch
                {
                    "xAxis" => active ? (x + 1) / 2 : 0.5f,
                    "yAxis" => active ? (y + 1) / 2 : 0.5f,
                    "state" => active ? 1 : 0,
                    _ => active ? button : 0
                };
                layout.Device.NodePoses[r.Slot] = Blend(r.Min, r.Max, MathHelper.Clamp(value, 0, 1));
            }
        }

        private static Matrix Blend(Matrix a, Matrix b, float t)
        {
            if (!a.Decompose(out var sa, out var ra, out var ta) || !b.Decompose(out var sb, out var rb, out var tb))
                return t < 0.5f ? a : b;
            return Matrix.CreateScale(Vector3.Lerp(sa, sb, t)) * Matrix.CreateFromQuaternion(Quaternion.Slerp(ra, rb, t))
                * Matrix.CreateTranslation(Vector3.Lerp(ta, tb, t));
        }
    }
}
