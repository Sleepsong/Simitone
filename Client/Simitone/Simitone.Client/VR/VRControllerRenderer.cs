using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;
using Simitone.VR.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Draws the controllers in your hands when you're not in your Sim's body: the runtime's own models of the devices
    /// you're holding when it has them (<see cref="VRRenderModels"/>: SteamVR, including on the Steam Frame), with
    /// their sticks, triggers and buttons moving as you use them; otherwise the models shipped with Simitone for
    /// common controllers (<see cref="VRBundledControllers"/>: Meta Touch, Vive wands), moving the same way; otherwise
    /// Simitone's simple one (<see cref="VRControllerModel"/>).
    ///
    /// Each model's glTF is parsed on a worker thread the first time it's seen (the built-in model stands in
    /// meanwhile), then put on the GPU on the game thread, and kept for as long as the game runs.
    /// </summary>
    public class VRControllerRenderer : IDisposable
    {
        private readonly VRControllerModel BuiltIn;
        private readonly Dictionary<string, GltfModel> Ready = new Dictionary<string, GltfModel>();
        private readonly Dictionary<string, Task<GltfAsset>> Parsing = new Dictionary<string, Task<GltfAsset>>();
        private readonly HashSet<string> Broken = new HashSet<string>();
        private readonly List<(GltfModel model, Matrix world, VRDeviceModel device)> Frame = new List<(GltfModel, Matrix, VRDeviceModel)>();
        private bool LeftCovered, RightCovered;
        private readonly VRBundledControllers Bundled;

        public VRControllerRenderer(GraphicsDevice device)
        {
            BuiltIn = new VRControllerModel(device);
            Bundled = new VRBundledControllers(System.IO.Path.Combine(AppContext.BaseDirectory, "VRControllers"), SimitoneVR.Log);
        }

        /// <summary>Where the hands are, in whatever space the controllers are drawn in.</summary>
        public struct Hands
        {
            public bool Show, LeftValid, RightValid;
            public Matrix LeftGrip, RightGrip;
            /// <summary>A pose in tracking space as a world matrix for something modelled in metres.</summary>
            public Func<VRPose, Matrix> PoseToWorld;

            /// <summary>The lot's: the VR camera's world (its scale and rig).</summary>
            public static Hands Of(VRCameraController camera) => new Hands
            {
                Show = camera.ShowControllers,
                LeftValid = camera.LeftHandValid, RightValid = camera.RightHandValid,
                LeftGrip = camera.LeftGripWorld, RightGrip = camera.RightGripWorld,
                PoseToWorld = camera.PoseToWorld
            };

            /// <summary>Your room's (tracking space, metres): for drawing them with no lot, over the flat screen.</summary>
            public static Hands InRoom(VRInput input)
            {
                Matrix World(VRPose pose) => Matrix.CreateFromQuaternion(pose.Orientation) * Matrix.CreateTranslation(pose.Position);
                var active = input != null && input.Active;
                return new Hands
                {
                    Show = true,
                    LeftValid = active && input.LeftGrip.Valid, RightValid = active && input.RightGrip.Valid,
                    LeftGrip = active ? World(input.LeftGrip) : Matrix.Identity, RightGrip = active ? World(input.RightGrip) : Matrix.Identity,
                    PoseToWorld = World
                };
            }
        }

        /// <summary>Works out this frame's models (once per frame, before the eyes are drawn).</summary>
        public void Prepare(GraphicsDevice device, VRCameraController camera, VRRuntime runtime) => Prepare(device, Hands.Of(camera), runtime);

        public void Prepare(GraphicsDevice device, Hands camera, VRRuntime runtime)
        {
            Frame.Clear();
            LeftCovered = RightCovered = false;
            if (!camera.Show || runtime == null) return;
            var runtimeModels = runtime.RenderModels;
            if (runtimeModels != null)
            {
                foreach (var device_ in runtimeModels.Models)
                {
                    if (!device_.Pose.Valid || device_.Glb == null) continue;
                    var model = Get(device, device_);
                    if (model == null) continue;
                    Frame.Add((model, camera.PoseToWorld(device_.Pose), device_));
                    if (device_.Hand < 0) LeftCovered = true;
                    else if (device_.Hand > 0) RightCovered = true;
                }
            }
            // hands the runtime has no model for: ours, for the controller type it says that hand is using
            var input = runtime.Input;
            if (input == null || !input.Active) return;
            for (int hand = 0; hand < 2; hand++)
            {
                if (hand == 0 ? LeftCovered || !camera.LeftValid : RightCovered || !camera.RightValid) continue;
                var layout = Bundled.For(hand, runtime.HandProfiles[hand], runtime.SystemName);
                if (layout == null) continue;
                var model = GetBundled(device, layout);
                if (model == null) continue;
                VRBundledControllers.Animate(layout, VRBundledControllers.Hand(input, hand));
                Frame.Add((model, hand == 0 ? camera.LeftGrip : camera.RightGrip, layout.Device));
                if (hand == 0) LeftCovered = true;
                else RightCovered = true;
            }
        }

        /// <summary>One of Simitone's own models: parsed on a worker thread, then its moving parts read from its profile.</summary>
        private GltfModel GetBundled(GraphicsDevice device, VRBundledControllers.Layout layout)
        {
            var key = "bundled:" + layout.Key;
            if (Ready.TryGetValue(key, out var model)) return model;
            if (Broken.Contains(key)) return null;
            if (!Parsing.TryGetValue(key, out var task))
            {
                var glb = layout.Glb;
                Parsing[key] = Task.Run(() => GltfAsset.Parse(glb));
                return null;
            }
            if (!task.IsCompleted) return null;
            Parsing.Remove(key);
            try
            {
                if (task.IsFaulted) throw task.Exception.GetBaseException();
                VRBundledControllers.Prepare(layout, task.Result);
                model = new GltfModel(device, task.Result);
                Ready[key] = model;
                SimitoneVR.Log($"controller model {layout.Key} ready ({model.Bounds.Radius * 200:0} cm across, {layout.Device.AnimatedNodes.Length} moving parts)");
                return model;
            }
            catch (Exception e)
            {
                Broken.Add(key);
                SimitoneVR.Log($"controller model {layout.Key} can't be shown, using the built-in one: {e.Message}");
                return null;
            }
        }

        /// <summary>Draws this frame's controllers for one eye.</summary>
        public void Draw(GraphicsDevice device, VRCameraController camera, Matrix view, Matrix projection) => Draw(device, Hands.Of(camera), view, projection);

        public void Draw(GraphicsDevice device, Hands camera, Matrix view, Matrix projection)
        {
            if (!camera.Show) return;
            foreach (var (model, world, d) in Frame)
            {
                model.SetAnimatedNodes(d.AnimatedNodes);
                model.Draw(device, world, view, projection, d.NodePoses, d.NodeVisible);
            }
            // hands the runtime has no model for get the built-in one, at the grip
            if (camera.LeftValid && !LeftCovered) BuiltIn.Draw(device, camera.LeftGrip, view, projection);
            if (camera.RightValid && !RightCovered) BuiltIn.Draw(device, camera.RightGrip, view, projection);
        }

        private GltfModel Get(GraphicsDevice device, VRDeviceModel d)
        {
            var key = d.CacheId ?? "";
            if (Ready.TryGetValue(key, out var model)) return model;
            if (Broken.Contains(key)) return null;
            if (!Parsing.TryGetValue(key, out var task))
            {
                var glb = d.Glb;
                Parsing[key] = Task.Run(() => GltfAsset.Parse(glb));
                return null;
            }
            if (!task.IsCompleted) return null;
            Parsing.Remove(key);
            try
            {
                if (task.IsFaulted) throw task.Exception.GetBaseException();
                model = new GltfModel(device, task.Result);
                Ready[key] = model;
                SimitoneVR.Log($"controller model {key} ready ({model.Bounds.Radius * 200:0} cm across)");
                return model;
            }
            catch (Exception e)
            {
                Broken.Add(key);
                SimitoneVR.Log($"controller model {key} can't be shown, using the built-in one: {e.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            BuiltIn.Dispose();
            foreach (var model in Ready.Values) model.Dispose();
            Ready.Clear();
        }
    }
}
