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
    /// their sticks, triggers and buttons moving as you use them; otherwise, or for a hand it has no model for,
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

        public VRControllerRenderer(GraphicsDevice device)
        {
            BuiltIn = new VRControllerModel(device);
        }

        /// <summary>Works out this frame's models (once per frame, before the eyes are drawn).</summary>
        public void Prepare(GraphicsDevice device, VRCameraController camera, VRRenderModels runtimeModels)
        {
            Frame.Clear();
            LeftCovered = RightCovered = false;
            if (!camera.ShowControllers || runtimeModels == null) return;
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

        /// <summary>Draws this frame's controllers for one eye.</summary>
        public void Draw(GraphicsDevice device, VRCameraController camera, Matrix view, Matrix projection)
        {
            if (!camera.ShowControllers) return;
            foreach (var (model, world, d) in Frame)
            {
                model.SetAnimatedNodes(d.AnimatedNodes);
                model.Draw(device, world, view, projection, d.NodePoses, d.NodeVisible);
            }
            // hands the runtime has no model for get the built-in one, at the grip
            if (camera.LeftHandValid && !LeftCovered) BuiltIn.Draw(device, camera.LeftGripWorld, view, projection);
            if (camera.RightHandValid && !RightCovered) BuiltIn.Draw(device, camera.RightGripWorld, view, projection);
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
