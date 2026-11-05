using System;
using System.Runtime.InteropServices;
using System.Text;

// Minimal, hand written OpenXR 1.0 bindings covering what Simitone needs.
// Layouts mirror openxr.h / openxr_platform.h for 64-bit targets (x64 and arm64, Linux and Windows).
// Every struct here is verified against the C headers by tools/vr/layout-check/run.sh (see VR.md).

namespace Simitone.VR.OpenXR
{
    public enum XrResult : int
    {
        Success = 0,
        TimeoutExpired = 1,
        SessionLossPending = 3,
        EventUnavailable = 4,
        SpaceBoundsUnavailable = 7,
        SessionNotFocused = 8,
        FrameDiscarded = 9,
        ErrorValidationFailure = -1,
        ErrorRuntimeFailure = -2,
        ErrorOutOfMemory = -3,
        ErrorApiVersionUnsupported = -4,
        ErrorInitializationFailed = -6,
        ErrorFunctionUnsupported = -7,
        ErrorFeatureUnsupported = -8,
        ErrorExtensionNotPresent = -9,
        ErrorLimitReached = -10,
        ErrorSizeInsufficient = -11,
        ErrorHandleInvalid = -12,
        ErrorInstanceLost = -13,
        ErrorSessionRunning = -14,
        ErrorSessionNotRunning = -16,
        ErrorSessionLost = -17,
        ErrorSystemInvalid = -18,
        ErrorPathInvalid = -19,
        ErrorPathCountExceeded = -20,
        ErrorPathFormatInvalid = -21,
        ErrorPathUnsupported = -22,
        ErrorLayerInvalid = -23,
        ErrorLayerLimitExceeded = -24,
        ErrorSwapchainRectInvalid = -25,
        ErrorSwapchainFormatUnsupported = -26,
        ErrorActionTypeMismatch = -27,
        ErrorSessionNotReady = -28,
        ErrorSessionNotStopping = -29,
        ErrorTimeInvalid = -30,
        ErrorReferenceSpaceUnsupported = -31,
        ErrorFileAccessError = -32,
        ErrorFileContentsInvalid = -33,
        ErrorFormFactorUnsupported = -34,
        ErrorFormFactorUnavailable = -35,
        ErrorApiLayerNotPresent = -36,
        ErrorCallOrderInvalid = -37,
        ErrorGraphicsDeviceInvalid = -38,
        ErrorPoseInvalid = -39,
        ErrorIndexOutOfRange = -40,
        ErrorViewConfigurationTypeUnsupported = -41,
        ErrorEnvironmentBlendModeUnsupported = -42,
        ErrorNameDuplicated = -44,
        ErrorNameInvalid = -45,
        ErrorActionsetNotAttached = -46,
        ErrorActionsetsAlreadyAttached = -47,
        ErrorLocalizedNameDuplicated = -48,
        ErrorLocalizedNameInvalid = -49,
        ErrorGraphicsRequirementsCallMissing = -50,
        ErrorRuntimeUnavailable = -51,
        // XR_EXT_render_model / XR_EXT_interaction_render_model
        ErrorRenderModelIdInvalidEXT = -1000300000,
        ErrorRenderModelAssetUnavailableEXT = -1000300001,
        ErrorRenderModelGltfExtensionRequiredEXT = -1000300002,
        ErrorNotInteractionRenderModelEXT = -1000301000,
    }

    public enum XrStructureType : int
    {
        Unknown = 0,
        ExtensionProperties = 2,
        InstanceCreateInfo = 3,
        SystemGetInfo = 4,
        SystemProperties = 5,
        ViewLocateInfo = 6,
        View = 7,
        SessionCreateInfo = 8,
        SwapchainCreateInfo = 9,
        SessionBeginInfo = 10,
        ViewState = 11,
        FrameEndInfo = 12,
        HapticVibration = 13,
        EventDataBuffer = 16,
        EventDataInstanceLossPending = 17,
        EventDataSessionStateChanged = 18,
        ActionStateBoolean = 23,
        ActionStateFloat = 24,
        ActionStateVector2f = 25,
        ActionStatePose = 27,
        ActionSetCreateInfo = 28,
        ActionCreateInfo = 29,
        InstanceProperties = 32,
        FrameWaitInfo = 33,
        CompositionLayerProjection = 35,
        CompositionLayerQuad = 36,
        ReferenceSpaceCreateInfo = 37,
        ActionSpaceCreateInfo = 38,
        EventDataReferenceSpaceChangePending = 40,
        ViewConfigurationView = 41,
        SpaceLocation = 42,
        FrameState = 44,
        FrameBeginInfo = 46,
        CompositionLayerProjectionView = 48,
        EventDataEventsLost = 49,
        InteractionProfileSuggestedBinding = 51,
        EventDataInteractionProfileChanged = 52,
        SwapchainImageAcquireInfo = 55,
        SwapchainImageWaitInfo = 56,
        SwapchainImageReleaseInfo = 57,
        ActionStateGetInfo = 58,
        HapticActionInfo = 59,
        SessionActionSetsAttachInfo = 60,
        ActionsSyncInfo = 61,
        GraphicsBindingOpenGLWin32KHR = 1000023000,
        GraphicsBindingOpenGLXlibKHR = 1000023001,
        SwapchainImageOpenGLKHR = 1000023004,
        GraphicsRequirementsOpenGLKHR = 1000023005,
        GraphicsBindingEglMNDX = 1000048004,
        // XR_EXT_render_model
        RenderModelCreateInfoEXT = 1000300000,
        RenderModelPropertiesGetInfoEXT = 1000300001,
        RenderModelPropertiesEXT = 1000300002,
        RenderModelSpaceCreateInfoEXT = 1000300003,
        RenderModelStateGetInfoEXT = 1000300004,
        RenderModelStateEXT = 1000300005,
        RenderModelAssetCreateInfoEXT = 1000300006,
        RenderModelAssetDataGetInfoEXT = 1000300007,
        RenderModelAssetDataEXT = 1000300008,
        RenderModelAssetPropertiesGetInfoEXT = 1000300009,
        RenderModelAssetPropertiesEXT = 1000300010,
        // XR_EXT_interaction_render_model
        InteractionRenderModelIdsEnumerateInfoEXT = 1000301000,
        InteractionRenderModelSubactionPathInfoEXT = 1000301001,
        EventDataInteractionRenderModelsChangedEXT = 1000301002,
        InteractionRenderModelTopLevelUserPathGetInfoEXT = 1000301003,
    }

    public enum XrSessionState : int
    {
        Unknown = 0,
        Idle = 1,
        Ready = 2,
        Synchronized = 3,
        Visible = 4,
        Focused = 5,
        Stopping = 6,
        LossPending = 7,
        Exiting = 8,
    }

    public enum XrReferenceSpaceType : int
    {
        View = 1,
        Local = 2,
        Stage = 3,
        LocalFloorEXT = 1000426000,
    }

    public enum XrActionType : int
    {
        BooleanInput = 1,
        FloatInput = 2,
        Vector2fInput = 3,
        PoseInput = 4,
        VibrationOutput = 100,
    }

    public static class XrConst
    {
        public const int FormFactorHeadMountedDisplay = 1;
        public const int ViewConfigurationPrimaryStereo = 2;
        public const int EnvironmentBlendModeOpaque = 1;
        public const int EyeVisibilityBoth = 0;

        public const ulong SwapchainUsageColorAttachment = 0x1;
        public const ulong SwapchainUsageTransferDst = 0x10;
        public const ulong SwapchainUsageSampled = 0x20;

        public const ulong ViewStateOrientationValid = 0x1;
        public const ulong ViewStatePositionValid = 0x2;
        public const ulong SpaceLocationOrientationValid = 0x1;
        public const ulong SpaceLocationPositionValid = 0x2;
        public const ulong SpaceLocationOrientationTracked = 0x4;
        public const ulong SpaceLocationPositionTracked = 0x8;

        public const ulong CompositionLayerBlendTextureSourceAlpha = 0x2;

        public const long InfiniteDuration = 0x7fffffffffffffffL;
        public const long MinHapticDuration = -1;
        public const float FrequencyUnspecified = 0;

        public const int MaxExtensionNameSize = 128;
        public const int MaxApplicationNameSize = 128;
        public const int MaxEngineNameSize = 128;
        public const int MaxRuntimeNameSize = 128;
        public const int MaxSystemNameSize = 256;
        public const int MaxActionSetNameSize = 64;
        public const int MaxActionNameSize = 64;
        public const int MaxLocalizedActionSetNameSize = 128;
        public const int MaxLocalizedActionNameSize = 128;
        public const int MaxResultStringSize = 64;

        public static ulong MakeVersion(ulong major, ulong minor, ulong patch)
            => ((major & 0xffff) << 48) | ((minor & 0xffff) << 32) | (patch & 0xffffffff);

        public static string VersionString(ulong v)
            => $"{(v >> 48) & 0xffff}.{(v >> 32) & 0xffff}.{v & 0xffffffff}";

        /// <summary>Copies a managed string into a fixed-size, null terminated UTF-8 buffer.</summary>
        public static unsafe void WriteString(byte* dest, int capacity, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            int n = Math.Min(bytes.Length, capacity - 1);
            for (int i = 0; i < n; i++) dest[i] = bytes[i];
            dest[n] = 0;
        }

        public static unsafe string ReadString(byte* src, int capacity)
        {
            int n = 0;
            while (n < capacity && src[n] != 0) n++;
            return Encoding.UTF8.GetString(src, n);
        }
    }

    // ---------------------------------------------------------------- basic math types

    [StructLayout(LayoutKind.Sequential)]
    public struct XrVector2f { public float X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrVector3f { public float X, Y, Z; }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrQuaternionf
    {
        public float X, Y, Z, W;
        public static XrQuaternionf Identity => new XrQuaternionf { W = 1 };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrPosef
    {
        public XrQuaternionf Orientation;
        public XrVector3f Position;
        public static XrPosef Identity => new XrPosef { Orientation = XrQuaternionf.Identity };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrFovf { public float AngleLeft, AngleRight, AngleUp, AngleDown; }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrOffset2Di { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrExtent2Di { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrExtent2Df { public float Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrRect2Di { public XrOffset2Di Offset; public XrExtent2Di Extent; }

    // ---------------------------------------------------------------- instance / system

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrExtensionProperties
    {
        public XrStructureType Type;
        public void* Next;
        public fixed byte ExtensionName[XrConst.MaxExtensionNameSize];
        public uint ExtensionVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrApplicationInfo
    {
        public fixed byte ApplicationName[XrConst.MaxApplicationNameSize];
        public uint ApplicationVersion;
        public fixed byte EngineName[XrConst.MaxEngineNameSize];
        public uint EngineVersion;
        public ulong ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrInstanceCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public ulong CreateFlags;
        public XrApplicationInfo ApplicationInfo;
        public uint EnabledApiLayerCount;
        public byte** EnabledApiLayerNames;
        public uint EnabledExtensionCount;
        public byte** EnabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrInstanceProperties
    {
        public XrStructureType Type;
        public void* Next;
        public ulong RuntimeVersion;
        public fixed byte RuntimeName[XrConst.MaxRuntimeNameSize];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSystemGetInfo
    {
        public XrStructureType Type;
        public void* Next;
        public int FormFactor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrSystemGraphicsProperties
    {
        public uint MaxSwapchainImageHeight;
        public uint MaxSwapchainImageWidth;
        public uint MaxLayerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrSystemTrackingProperties
    {
        public uint OrientationTracking;
        public uint PositionTracking;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSystemProperties
    {
        public XrStructureType Type;
        public void* Next;
        public ulong SystemId;
        public uint VendorId;
        public fixed byte SystemName[XrConst.MaxSystemNameSize];
        public XrSystemGraphicsProperties GraphicsProperties;
        public XrSystemTrackingProperties TrackingProperties;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrViewConfigurationView
    {
        public XrStructureType Type;
        public void* Next;
        public uint RecommendedImageRectWidth;
        public uint MaxImageRectWidth;
        public uint RecommendedImageRectHeight;
        public uint MaxImageRectHeight;
        public uint RecommendedSwapchainSampleCount;
        public uint MaxSwapchainSampleCount;
    }

    // ---------------------------------------------------------------- OpenGL graphics bindings

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrGraphicsRequirementsOpenGLKHR
    {
        public XrStructureType Type;
        public void* Next;
        public ulong MinApiVersionSupported;
        public ulong MaxApiVersionSupported;
    }

    /// <summary>XR_KHR_opengl_enable on X11/GLX. The SDL window's own context is handed to the runtime.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrGraphicsBindingOpenGLXlibKHR
    {
        public XrStructureType Type;
        public void* Next;
        public IntPtr XDisplay;
        public uint VisualId;
        public IntPtr GlxFBConfig;
        public ulong GlxDrawable; // GLXDrawable is an XID (unsigned long)
        public IntPtr GlxContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrGraphicsBindingOpenGLWin32KHR
    {
        public XrStructureType Type;
        public void* Next;
        public IntPtr HDC;
        public IntPtr HGLRC;
    }

    /// <summary>XR_MNDX_egl_enable: used when SDL created an EGL context (Wayland, or X11 with EGL forced).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrGraphicsBindingEGLMNDX
    {
        public XrStructureType Type;
        public void* Next;
        public IntPtr GetProcAddress;
        public IntPtr Display;
        public IntPtr Config;
        public IntPtr Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainImageOpenGLKHR
    {
        public XrStructureType Type;
        public void* Next;
        public uint Image;
    }

    // ---------------------------------------------------------------- session / spaces

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSessionCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public ulong CreateFlags;
        public ulong SystemId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSessionBeginInfo
    {
        public XrStructureType Type;
        public void* Next;
        public int PrimaryViewConfigurationType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrReferenceSpaceCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public XrReferenceSpaceType ReferenceSpaceType;
        public XrPosef PoseInReferenceSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrActionSpaceCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public ulong Action;
        public ulong SubactionPath;
        public XrPosef PoseInActionSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSpaceLocation
    {
        public XrStructureType Type;
        public void* Next;
        public ulong LocationFlags;
        public XrPosef Pose;
    }

    // ---------------------------------------------------------------- swapchains

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public ulong CreateFlags;
        public ulong UsageFlags;
        public long Format;
        public uint SampleCount;
        public uint Width;
        public uint Height;
        public uint FaceCount;
        public uint ArraySize;
        public uint MipCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainImageAcquireInfo
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainImageWaitInfo
    {
        public XrStructureType Type;
        public void* Next;
        public long Timeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSwapchainImageReleaseInfo
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrSwapchainSubImage
    {
        public ulong Swapchain;
        public XrRect2Di ImageRect;
        public uint ImageArrayIndex;
    }

    // ---------------------------------------------------------------- frame loop

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrFrameWaitInfo
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrFrameState
    {
        public XrStructureType Type;
        public void* Next;
        public long PredictedDisplayTime;
        public long PredictedDisplayPeriod;
        public uint ShouldRender;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrFrameBeginInfo
    {
        public XrStructureType Type;
        public void* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrFrameEndInfo
    {
        public XrStructureType Type;
        public void* Next;
        public long DisplayTime;
        public int EnvironmentBlendMode;
        public uint LayerCount;
        public void** Layers;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrViewLocateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public int ViewConfigurationType;
        public long DisplayTime;
        public ulong Space;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrViewState
    {
        public XrStructureType Type;
        public void* Next;
        public ulong ViewStateFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrView
    {
        public XrStructureType Type;
        public void* Next;
        public XrPosef Pose;
        public XrFovf Fov;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrCompositionLayerProjectionView
    {
        public XrStructureType Type;
        public void* Next;
        public XrPosef Pose;
        public XrFovf Fov;
        public XrSwapchainSubImage SubImage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrCompositionLayerProjection
    {
        public XrStructureType Type;
        public void* Next;
        public ulong LayerFlags;
        public ulong Space;
        public uint ViewCount;
        public XrCompositionLayerProjectionView* Views;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrCompositionLayerQuad
    {
        public XrStructureType Type;
        public void* Next;
        public ulong LayerFlags;
        public ulong Space;
        public int EyeVisibility;
        public XrSwapchainSubImage SubImage;
        public XrPosef Pose;
        public XrExtent2Df Size;
    }

    // ---------------------------------------------------------------- events

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrEventDataBuffer
    {
        public XrStructureType Type;
        public void* Next;
        public fixed byte Varying[4000];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrEventDataSessionStateChanged
    {
        public XrStructureType Type;
        public void* Next;
        public ulong Session;
        public XrSessionState State;
        public long Time;
    }

    // ---------------------------------------------------------------- actions

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrActionSetCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public fixed byte ActionSetName[XrConst.MaxActionSetNameSize];
        public fixed byte LocalizedActionSetName[XrConst.MaxLocalizedActionSetNameSize];
        public uint Priority;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrActionCreateInfo
    {
        public XrStructureType Type;
        public void* Next;
        public fixed byte ActionName[XrConst.MaxActionNameSize];
        public XrActionType ActionType;
        public uint CountSubactionPaths;
        public ulong* SubactionPaths;
        public fixed byte LocalizedActionName[XrConst.MaxLocalizedActionNameSize];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrActionSuggestedBinding
    {
        public ulong Action;
        public ulong Binding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrInteractionProfileSuggestedBinding
    {
        public XrStructureType Type;
        public void* Next;
        public ulong InteractionProfile;
        public uint CountSuggestedBindings;
        public XrActionSuggestedBinding* SuggestedBindings;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrSessionActionSetsAttachInfo
    {
        public XrStructureType Type;
        public void* Next;
        public uint CountActionSets;
        public ulong* ActionSets;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrActiveActionSet
    {
        public ulong ActionSet;
        public ulong SubactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrActionsSyncInfo
    {
        public XrStructureType Type;
        public void* Next;
        public uint CountActiveActionSets;
        public XrActiveActionSet* ActiveActionSets;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrActionStateGetInfo
    {
        public XrStructureType Type;
        public void* Next;
        public ulong Action;
        public ulong SubactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrActionStateBoolean
    {
        public XrStructureType Type;
        public void* Next;
        public uint CurrentState;
        public uint ChangedSinceLastSync;
        public long LastChangeTime;
        public uint IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrActionStateFloat
    {
        public XrStructureType Type;
        public void* Next;
        public float CurrentState;
        public uint ChangedSinceLastSync;
        public long LastChangeTime;
        public uint IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrActionStateVector2f
    {
        public XrStructureType Type;
        public void* Next;
        public XrVector2f CurrentState;
        public uint ChangedSinceLastSync;
        public long LastChangeTime;
        public uint IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrActionStatePose
    {
        public XrStructureType Type;
        public void* Next;
        public uint IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrHapticActionInfo
    {
        public XrStructureType Type;
        public void* Next;
        public ulong Action;
        public ulong SubactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XrHapticVibration
    {
        public XrStructureType Type;
        public void* Next;
        public long Duration;
        public float Frequency;
        public float Amplitude;
    }

    // ---------------------------------------------------------------- loader <-> runtime negotiation
    // (openxr_loader_negotiation.h) - used when talking to a runtime directly without the Khronos loader.

    [StructLayout(LayoutKind.Sequential)]
    public struct XrNegotiateLoaderInfo
    {
        public int StructType;      // XR_LOADER_INTERFACE_STRUCT_LOADER_INFO = 1
        public uint StructVersion;  // XR_LOADER_INFO_STRUCT_VERSION = 1
        public UIntPtr StructSize;
        public uint MinInterfaceVersion;
        public uint MaxInterfaceVersion;
        public ulong MinApiVersion;
        public ulong MaxApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XrNegotiateRuntimeRequest
    {
        public int StructType;      // XR_LOADER_INTERFACE_STRUCT_RUNTIME_REQUEST = 3
        public uint StructVersion;  // XR_RUNTIME_INFO_STRUCT_VERSION = 1
        public UIntPtr StructSize;
        public uint RuntimeInterfaceVersion;
        public ulong RuntimeApiVersion;
        public IntPtr GetInstanceProcAddr;
    }

    public class XrException : Exception
    {
        public XrResult Result { get; }
        public XrException(string call, XrResult result, string resultName = null)
            : base($"{call} failed: {resultName ?? result.ToString()} ({(int)result})")
        {
            Result = result;
        }
    }
}
