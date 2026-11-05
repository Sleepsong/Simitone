using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Simitone.VR.OpenXR;

namespace Simitone.VR
{
    /// <summary>
    /// One device the runtime has a model of (a controller, usually): its glTF asset, which hand it's in, where it is
    /// and how its parts (thumbstick, trigger, buttons) are moved this frame.
    /// </summary>
    public class VRDeviceModel
    {
        /// <summary>The asset's cache UUID, as text: the same asset (e.g. both Touch controllers' meshes) shares it.</summary>
        public string CacheId;
        /// <summary>The model as glTF binary (GLB), from the runtime.</summary>
        public byte[] Glb;
        /// <summary>The names of the nodes the runtime animates, in the order of <see cref="NodePoses"/>.</summary>
        public string[] AnimatedNodes;
        public Matrix[] NodePoses;
        public bool[] NodeVisible;
        /// <summary>Where the model's origin is (tracking space). Only draw it while valid.</summary>
        public VRPose Pose;
        /// <summary>-1 left hand, 1 right hand, 0 neither (e.g. put down, or a tracker).</summary>
        public int Hand;

        internal ulong Id, Handle, Space;
        internal XrRenderModelNodeStateEXT[] States;
    }

    /// <summary>
    /// The controllers as the runtime models them (XR_EXT_render_model with XR_EXT_interaction_render_model): the
    /// exact devices in use, such as the Steam Frame's controllers, Index or Touch controllers on SteamVR, with their
    /// sticks and buttons moving. The runtime says when the set of devices changes (during xrSyncActions); the models
    /// are then (re)enumerated, and each one's glTF asset is read once. Every frame each model is located and its
    /// animated parts are read. Runtimes without the extensions (or before the first sync) have no models here.
    /// </summary>
    public unsafe sealed class VRRenderModels : IDisposable
    {
        private readonly XrApi Api;
        private readonly XrRenderModelApi RM;
        private readonly Action<string> Log;
        private readonly ulong LeftHand, RightHand;
        private readonly Dictionary<ulong, VRDeviceModel> ById = new Dictionary<ulong, VRDeviceModel>();
        private readonly Dictionary<string, (byte[] glb, string[] nodes)> AssetCache = new Dictionary<string, (byte[], string[])>();
        private bool Failed, Synced;

        /// <summary>The models, located for this frame (check each one's <see cref="VRDeviceModel.Pose"/>).</summary>
        public readonly List<VRDeviceModel> Models = new List<VRDeviceModel>();
        /// <summary>Set when the runtime reports the set of devices changed: enumerate on the next update.</summary>
        public bool NeedsEnumerate;
        /// <summary>Bumped whenever <see cref="Models"/> changes, so the renderer can rebuild.</summary>
        public int Version { get; private set; }

        public VRRenderModels(XrApi api, XrRenderModelApi rm, Action<string> log)
        {
            Api = api;
            RM = rm;
            Log = log ?? (s => { });
            LeftHand = api.Path("/user/hand/left");
            RightHand = api.Path("/user/hand/right");
        }

        /// <summary>After xrSyncActions: enumerate if needed, then locate and animate every model for this frame.</summary>
        public void Update(ulong session, ulong baseSpace, long displayTime)
        {
            if (Failed) return;
            try
            {
                // The runtime says when the devices change (an event during xrSyncActions); also look once after the
                // first sync, in case that event goes missing.
                if (!Synced) NeedsEnumerate = Synced = true;
                if (NeedsEnumerate)
                {
                    NeedsEnumerate = false;
                    Enumerate(session);
                }
                foreach (var model in Models) Locate(model, baseSpace, displayTime);
            }
            catch (Exception e)
            {
                // the runtime's models are a nicety: on any failure, fall back to the built-in controller
                Log("OpenXR: controller models from the runtime failed, using the built-in ones: " + e.Message);
                Failed = true;
                Clear();
            }
        }

        /// <summary>Hides all models (not focused: the runtime doesn't locate them then anyway).</summary>
        public void Untrack()
        {
            foreach (var model in Models) model.Pose.Valid = false;
        }

        private void Enumerate(ulong session)
        {
            uint count;
            Check(RM.EnumerateInteractionRenderModelIds(session, null, 0, &count, null), "xrEnumerateInteractionRenderModelIdsEXT");
            var ids = new ulong[count];
            if (count > 0)
                fixed (ulong* p = ids)
                    Check(RM.EnumerateInteractionRenderModelIds(session, null, count, &count, p), "xrEnumerateInteractionRenderModelIdsEXT");

            // models no longer enumerated are inactive for good: let them go
            var current = new HashSet<ulong>(ids);
            foreach (var gone in new List<ulong>(ById.Keys))
            {
                if (current.Contains(gone)) continue;
                Destroy(ById[gone]);
                ById.Remove(gone);
            }
            foreach (var id in ids)
            {
                if (ById.ContainsKey(id)) continue;
                try
                {
                    ById[id] = Create(session, id);
                }
                catch (Exception e)
                {
                    Log("OpenXR: could not load a controller model: " + e.Message);
                }
            }
            Models.Clear();
            Models.AddRange(ById.Values);
            Version++;
            Log($"OpenXR: {Models.Count} controller model(s) from the runtime");
        }

        private VRDeviceModel Create(ulong session, ulong id)
        {
            var model = new VRDeviceModel { Id = id };
            // no glTF extensions needed: the runtime must provide interaction models without required extensions
            var create = new XrRenderModelCreateInfoEXT { Type = XrStructureType.RenderModelCreateInfoEXT, RenderModelId = id };
            ulong handle;
            Check(RM.CreateRenderModel(session, &create, &handle), "xrCreateRenderModelEXT");
            model.Handle = handle;
            try
            {
                var spaceInfo = new XrRenderModelSpaceCreateInfoEXT { Type = XrStructureType.RenderModelSpaceCreateInfoEXT, RenderModel = handle };
                ulong space;
                Check(RM.CreateRenderModelSpace(session, &spaceInfo, &space), "xrCreateRenderModelSpaceEXT");
                model.Space = space;

                var propsInfo = new XrRenderModelPropertiesGetInfoEXT { Type = XrStructureType.RenderModelPropertiesGetInfoEXT };
                var props = new XrRenderModelPropertiesEXT { Type = XrStructureType.RenderModelPropertiesEXT };
                Check(RM.GetRenderModelProperties(handle, &propsInfo, &props), "xrGetRenderModelPropertiesEXT");
                model.CacheId = UuidText(props.CacheId);

                if (!AssetCache.TryGetValue(model.CacheId, out var asset))
                {
                    asset = ReadAsset(session, props.CacheId, props.AnimatableNodeCount);
                    AssetCache[model.CacheId] = asset;
                    Log($"OpenXR: controller model {model.CacheId}: {asset.glb.Length / 1024} KB glTF, {asset.nodes.Length} moving parts");
                }
                model.Glb = asset.glb;
                model.AnimatedNodes = asset.nodes;
                var n = asset.nodes.Length;
                model.States = new XrRenderModelNodeStateEXT[n];
                model.NodePoses = new Matrix[n];
                model.NodeVisible = new bool[n];
                return model;
            }
            catch
            {
                Destroy(model);
                throw;
            }
        }

        private (byte[] glb, string[] nodes) ReadAsset(ulong session, XrUuidEXT cacheId, uint nodeCount)
        {
            var assetInfo = new XrRenderModelAssetCreateInfoEXT { Type = XrStructureType.RenderModelAssetCreateInfoEXT, CacheId = cacheId };
            ulong asset;
            Check(RM.CreateRenderModelAsset(session, &assetInfo, &asset), "xrCreateRenderModelAssetEXT");
            try
            {
                // the GLB, with the two-call idiom
                var dataInfo = new XrRenderModelAssetDataGetInfoEXT { Type = XrStructureType.RenderModelAssetDataGetInfoEXT };
                var data = new XrRenderModelAssetDataEXT { Type = XrStructureType.RenderModelAssetDataEXT };
                Check(RM.GetRenderModelAssetData(asset, &dataInfo, &data), "xrGetRenderModelAssetDataEXT");
                var glb = new byte[data.BufferCountOutput];
                fixed (byte* p = glb)
                {
                    data.BufferCapacityInput = (uint)glb.Length;
                    data.Buffer = p;
                    Check(RM.GetRenderModelAssetData(asset, &dataInfo, &data), "xrGetRenderModelAssetDataEXT");
                }

                // the names of the animated nodes, in the order their states come in
                var names = new string[nodeCount];
                if (nodeCount > 0)
                {
                    var nodes = new XrRenderModelAssetNodePropertiesEXT[nodeCount];
                    fixed (XrRenderModelAssetNodePropertiesEXT* p = nodes)
                    {
                        var propsInfo = new XrRenderModelAssetPropertiesGetInfoEXT { Type = XrStructureType.RenderModelAssetPropertiesGetInfoEXT };
                        var props = new XrRenderModelAssetPropertiesEXT
                        {
                            Type = XrStructureType.RenderModelAssetPropertiesEXT,
                            NodePropertyCount = nodeCount,
                            NodeProperties = p
                        };
                        Check(RM.GetRenderModelAssetProperties(asset, &propsInfo, &props), "xrGetRenderModelAssetPropertiesEXT");
                        for (int i = 0; i < nodeCount; i++)
                            names[i] = XrConst.ReadString(p[i].UniqueName, XrRenderModelAssetNodePropertiesEXT.MaxNameSize);
                    }
                }
                return (glb, names);
            }
            finally
            {
                RM.DestroyRenderModelAsset(asset);
            }
        }

        private void Locate(VRDeviceModel model, ulong baseSpace, long displayTime)
        {
            model.Pose = default;
            var loc = new XrSpaceLocation { Type = XrStructureType.SpaceLocation };
            if (Api.LocateSpace(model.Space, baseSpace, displayTime, &loc) != XrResult.Success) return;
            // only draw a model whose position and orientation are both tracked (as the extension asks)
            const ulong tracked = XrConst.SpaceLocationOrientationTracked | XrConst.SpaceLocationPositionTracked;
            if ((loc.LocationFlags & tracked) != tracked) return;

            var n = model.States.Length;
            if (n > 0)
            {
                fixed (XrRenderModelNodeStateEXT* p = model.States)
                {
                    var info = new XrRenderModelStateGetInfoEXT { Type = XrStructureType.RenderModelStateGetInfoEXT, DisplayTime = displayTime };
                    var state = new XrRenderModelStateEXT { Type = XrStructureType.RenderModelStateEXT, NodeStateCount = (uint)n, NodeStates = p };
                    if (RM.GetRenderModelState(model.Handle, &info, &state) != XrResult.Success) return;
                }
                for (int i = 0; i < n; i++)
                {
                    var s = model.States[i];
                    model.NodeVisible[i] = s.IsVisible != 0;
                    model.NodePoses[i] = Matrix.CreateFromQuaternion(VRMath.ToXna(s.NodePose.Orientation))
                        * Matrix.CreateTranslation(VRMath.ToXna(s.NodePose.Position));
                }
            }

            // which hand it's in (this only changes during xrSyncActions, which has just run)
            var paths = stackalloc ulong[2] { LeftHand, RightHand };
            var pathInfo = new XrInteractionRenderModelTopLevelUserPathGetInfoEXT
            {
                Type = XrStructureType.InteractionRenderModelTopLevelUserPathGetInfoEXT,
                TopLevelUserPathCount = 2,
                TopLevelUserPaths = paths
            };
            ulong hand = 0;
            if (RM.GetRenderModelPoseTopLevelUserPath(model.Handle, &pathInfo, &hand) != XrResult.Success) hand = 0;
            model.Hand = hand == LeftHand ? -1 : hand == RightHand ? 1 : 0;

            model.Pose = new VRPose
            {
                Valid = true,
                Position = VRMath.ToXna(loc.Pose.Position),
                Orientation = VRMath.ToXna(loc.Pose.Orientation)
            };
        }

        private void Destroy(VRDeviceModel model)
        {
            if (model.Space != 0) Api.DestroySpace(model.Space);
            if (model.Handle != 0) RM.DestroyRenderModel(model.Handle);
            model.Space = model.Handle = 0;
            model.Pose.Valid = false;
        }

        private void Clear()
        {
            foreach (var model in ById.Values)
            {
                try { Destroy(model); } catch { }
            }
            ById.Clear();
            Models.Clear();
            Version++;
        }

        private void Check(XrResult result, string call) => Api.Check(result, call);

        private static string UuidText(XrUuidEXT id)
        {
            var bytes = new byte[16];
            for (int i = 0; i < 16; i++) bytes[i] = id.Data[i];
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        public void Dispose() => Clear();
    }
}
