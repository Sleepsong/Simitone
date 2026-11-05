using System.Runtime.InteropServices;

// XR_EXT_render_model and XR_EXT_interaction_render_model (OpenXR 1.1 registry, usable with a 1.0 instance): the
// runtime hands the app glTF models of the devices in use, such as the controllers, and animates their parts.
// Layouts are checked against the Khronos headers by tools/vr/layout-check (with headers that have these extensions).

namespace Simitone.VR.OpenXR
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrUuidEXT
    {
        public fixed byte Data[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelCreateInfoEXT
    {
        public XrStructureType Type;
        public void* Next;
        public ulong RenderModelId;
        public uint GltfExtensionCount;
        public byte** GltfExtensions;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelPropertiesGetInfoEXT
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelPropertiesEXT
    {
        public XrStructureType Type;
        public void* Next;
        public XrUuidEXT CacheId;
        public uint AnimatableNodeCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelSpaceCreateInfoEXT
    {
        public XrStructureType Type;
        public void* Next;
        public ulong RenderModel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelStateGetInfoEXT
    {
        public XrStructureType Type;
        public void* Next;
        public long DisplayTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrRenderModelNodeStateEXT
    {
        public XrPosef NodePose;
        public uint IsVisible;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelStateEXT
    {
        public XrStructureType Type;
        public void* Next;
        public uint NodeStateCount;
        public XrRenderModelNodeStateEXT* NodeStates;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelAssetCreateInfoEXT
    {
        public XrStructureType Type;
        public void* Next;
        public XrUuidEXT CacheId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelAssetDataGetInfoEXT
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelAssetDataEXT
    {
        public XrStructureType Type;
        public void* Next;
        public uint BufferCapacityInput;
        public uint BufferCountOutput;
        public byte* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelAssetPropertiesGetInfoEXT
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelAssetNodePropertiesEXT
    {
        public const int MaxNameSize = 64; // XR_MAX_RENDER_MODEL_ASSET_NODE_NAME_SIZE_EXT
        public fixed byte UniqueName[MaxNameSize];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrRenderModelAssetPropertiesEXT
    {
        public XrStructureType Type;
        public void* Next;
        public uint NodePropertyCount;
        public XrRenderModelAssetNodePropertiesEXT* NodeProperties;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrInteractionRenderModelIdsEnumerateInfoEXT
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrInteractionRenderModelTopLevelUserPathGetInfoEXT
    {
        public XrStructureType Type;
        public void* Next;
        public uint TopLevelUserPathCount;
        public ulong* TopLevelUserPaths;
    }

    /// <summary>The render model functions, resolved when both extensions are enabled.</summary>
    public unsafe sealed class XrRenderModelApi
    {
        public const string RenderModelExtension = "XR_EXT_render_model";
        public const string InteractionRenderModelExtension = "XR_EXT_interaction_render_model";

        public delegate* unmanaged<ulong, XrRenderModelCreateInfoEXT*, ulong*, XrResult> CreateRenderModel;
        public delegate* unmanaged<ulong, XrResult> DestroyRenderModel;
        public delegate* unmanaged<ulong, XrRenderModelPropertiesGetInfoEXT*, XrRenderModelPropertiesEXT*, XrResult> GetRenderModelProperties;
        public delegate* unmanaged<ulong, XrRenderModelSpaceCreateInfoEXT*, ulong*, XrResult> CreateRenderModelSpace;
        public delegate* unmanaged<ulong, XrRenderModelAssetCreateInfoEXT*, ulong*, XrResult> CreateRenderModelAsset;
        public delegate* unmanaged<ulong, XrResult> DestroyRenderModelAsset;
        public delegate* unmanaged<ulong, XrRenderModelAssetDataGetInfoEXT*, XrRenderModelAssetDataEXT*, XrResult> GetRenderModelAssetData;
        public delegate* unmanaged<ulong, XrRenderModelAssetPropertiesGetInfoEXT*, XrRenderModelAssetPropertiesEXT*, XrResult> GetRenderModelAssetProperties;
        public delegate* unmanaged<ulong, XrRenderModelStateGetInfoEXT*, XrRenderModelStateEXT*, XrResult> GetRenderModelState;
        public delegate* unmanaged<ulong, XrInteractionRenderModelIdsEnumerateInfoEXT*, uint, uint*, ulong*, XrResult> EnumerateInteractionRenderModelIds;
        public delegate* unmanaged<ulong, XrInteractionRenderModelTopLevelUserPathGetInfoEXT*, ulong*, XrResult> GetRenderModelPoseTopLevelUserPath;

        /// <summary>Resolves the functions; null if any is missing (then the runtime's models aren't used).</summary>
        public static XrRenderModelApi Load(XrApi api, ulong instance)
        {
            var r = new XrRenderModelApi();
            System.IntPtr F(string name) => api.Get(instance, name);
            var fns = new[]
            {
                F("xrCreateRenderModelEXT"), F("xrDestroyRenderModelEXT"), F("xrGetRenderModelPropertiesEXT"),
                F("xrCreateRenderModelSpaceEXT"), F("xrCreateRenderModelAssetEXT"), F("xrDestroyRenderModelAssetEXT"),
                F("xrGetRenderModelAssetDataEXT"), F("xrGetRenderModelAssetPropertiesEXT"), F("xrGetRenderModelStateEXT"),
                F("xrEnumerateInteractionRenderModelIdsEXT"), F("xrGetRenderModelPoseTopLevelUserPathEXT"),
            };
            foreach (var fn in fns) if (fn == System.IntPtr.Zero) return null;
            r.CreateRenderModel = (delegate* unmanaged<ulong, XrRenderModelCreateInfoEXT*, ulong*, XrResult>)fns[0];
            r.DestroyRenderModel = (delegate* unmanaged<ulong, XrResult>)fns[1];
            r.GetRenderModelProperties = (delegate* unmanaged<ulong, XrRenderModelPropertiesGetInfoEXT*, XrRenderModelPropertiesEXT*, XrResult>)fns[2];
            r.CreateRenderModelSpace = (delegate* unmanaged<ulong, XrRenderModelSpaceCreateInfoEXT*, ulong*, XrResult>)fns[3];
            r.CreateRenderModelAsset = (delegate* unmanaged<ulong, XrRenderModelAssetCreateInfoEXT*, ulong*, XrResult>)fns[4];
            r.DestroyRenderModelAsset = (delegate* unmanaged<ulong, XrResult>)fns[5];
            r.GetRenderModelAssetData = (delegate* unmanaged<ulong, XrRenderModelAssetDataGetInfoEXT*, XrRenderModelAssetDataEXT*, XrResult>)fns[6];
            r.GetRenderModelAssetProperties = (delegate* unmanaged<ulong, XrRenderModelAssetPropertiesGetInfoEXT*, XrRenderModelAssetPropertiesEXT*, XrResult>)fns[7];
            r.GetRenderModelState = (delegate* unmanaged<ulong, XrRenderModelStateGetInfoEXT*, XrRenderModelStateEXT*, XrResult>)fns[8];
            r.EnumerateInteractionRenderModelIds = (delegate* unmanaged<ulong, XrInteractionRenderModelIdsEnumerateInfoEXT*, uint, uint*, ulong*, XrResult>)fns[9];
            r.GetRenderModelPoseTopLevelUserPath = (delegate* unmanaged<ulong, XrInteractionRenderModelTopLevelUserPathGetInfoEXT*, ulong*, XrResult>)fns[10];
            return r;
        }
    }
}
