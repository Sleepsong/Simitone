using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Simitone.VR.OpenXR
{
    /// <summary>
    /// OpenXR function table. Everything is resolved through xrGetInstanceProcAddr, so the same code works
    /// with the Khronos loader and with a runtime that was loaded directly.
    /// </summary>
    public unsafe sealed class XrApi
    {
        public ulong Instance { get; private set; }

        private readonly delegate* unmanaged<ulong, byte*, IntPtr*, XrResult> getInstanceProcAddr;

        // global
        public delegate* unmanaged<byte*, uint, uint*, XrExtensionProperties*, XrResult> EnumerateInstanceExtensionProperties;
        public delegate* unmanaged<XrInstanceCreateInfo*, ulong*, XrResult> CreateInstance;

        // instance
        public delegate* unmanaged<ulong, XrResult> DestroyInstance;
        public delegate* unmanaged<ulong, XrInstanceProperties*, XrResult> GetInstanceProperties;
        public delegate* unmanaged<ulong, XrEventDataBuffer*, XrResult> PollEvent;
        public delegate* unmanaged<ulong, XrResult, byte*, XrResult> ResultToString;
        public delegate* unmanaged<ulong, byte*, ulong*, XrResult> StringToPath;
        public delegate* unmanaged<ulong, XrSystemGetInfo*, ulong*, XrResult> GetSystem;
        public delegate* unmanaged<ulong, ulong, XrSystemProperties*, XrResult> GetSystemProperties;
        public delegate* unmanaged<ulong, ulong, int, uint, uint*, XrViewConfigurationView*, XrResult> EnumerateViewConfigurationViews;
        public delegate* unmanaged<ulong, ulong, int, uint, uint*, int*, XrResult> EnumerateEnvironmentBlendModes;
        public delegate* unmanaged<ulong, XrSessionCreateInfo*, ulong*, XrResult> CreateSession;
        public delegate* unmanaged<ulong, XrActionSetCreateInfo*, ulong*, XrResult> CreateActionSet;
        public delegate* unmanaged<ulong, XrInteractionProfileSuggestedBinding*, XrResult> SuggestInteractionProfileBindings;

        // session
        public delegate* unmanaged<ulong, XrResult> DestroySession;
        public delegate* unmanaged<ulong, uint, uint*, XrReferenceSpaceType*, XrResult> EnumerateReferenceSpaces;
        public delegate* unmanaged<ulong, XrReferenceSpaceCreateInfo*, ulong*, XrResult> CreateReferenceSpace;
        public delegate* unmanaged<ulong, XrActionSpaceCreateInfo*, ulong*, XrResult> CreateActionSpace;
        public delegate* unmanaged<ulong, ulong, long, XrSpaceLocation*, XrResult> LocateSpace;
        public delegate* unmanaged<ulong, XrResult> DestroySpace;
        public delegate* unmanaged<ulong, uint, uint*, long*, XrResult> EnumerateSwapchainFormats;
        public delegate* unmanaged<ulong, XrSwapchainCreateInfo*, ulong*, XrResult> CreateSwapchain;
        public delegate* unmanaged<ulong, XrResult> DestroySwapchain;
        public delegate* unmanaged<ulong, uint, uint*, void*, XrResult> EnumerateSwapchainImages;
        public delegate* unmanaged<ulong, XrSwapchainImageAcquireInfo*, uint*, XrResult> AcquireSwapchainImage;
        public delegate* unmanaged<ulong, XrSwapchainImageWaitInfo*, XrResult> WaitSwapchainImage;
        public delegate* unmanaged<ulong, XrSwapchainImageReleaseInfo*, XrResult> ReleaseSwapchainImage;
        public delegate* unmanaged<ulong, XrSessionBeginInfo*, XrResult> BeginSession;
        public delegate* unmanaged<ulong, XrResult> EndSession;
        public delegate* unmanaged<ulong, XrResult> RequestExitSession;
        public delegate* unmanaged<ulong, XrFrameWaitInfo*, XrFrameState*, XrResult> WaitFrame;
        public delegate* unmanaged<ulong, XrFrameBeginInfo*, XrResult> BeginFrame;
        public delegate* unmanaged<ulong, XrFrameEndInfo*, XrResult> EndFrame;
        public delegate* unmanaged<ulong, XrViewLocateInfo*, XrViewState*, uint, uint*, XrView*, XrResult> LocateViews;

        // actions
        public delegate* unmanaged<ulong, XrResult> DestroyActionSet;
        public delegate* unmanaged<ulong, XrActionCreateInfo*, ulong*, XrResult> CreateAction;
        public delegate* unmanaged<ulong, XrSessionActionSetsAttachInfo*, XrResult> AttachSessionActionSets;
        public delegate* unmanaged<ulong, XrActionsSyncInfo*, XrResult> SyncActions;
        public delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateBoolean*, XrResult> GetActionStateBoolean;
        public delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateFloat*, XrResult> GetActionStateFloat;
        public delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateVector2f*, XrResult> GetActionStateVector2f;
        public delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStatePose*, XrResult> GetActionStatePose;
        public delegate* unmanaged<ulong, XrHapticActionInfo*, void*, XrResult> ApplyHapticFeedback;

        // XR_KHR_opengl_enable
        public delegate* unmanaged<ulong, ulong, XrGraphicsRequirementsOpenGLKHR*, XrResult> GetOpenGLGraphicsRequirementsKHR;

        public XrApi(IntPtr getInstanceProcAddrPtr)
        {
            getInstanceProcAddr = (delegate* unmanaged<ulong, byte*, IntPtr*, XrResult>)getInstanceProcAddrPtr;
            EnumerateInstanceExtensionProperties =
                (delegate* unmanaged<byte*, uint, uint*, XrExtensionProperties*, XrResult>)Require(0, "xrEnumerateInstanceExtensionProperties");
            CreateInstance = (delegate* unmanaged<XrInstanceCreateInfo*, ulong*, XrResult>)Require(0, "xrCreateInstance");
        }

        public IntPtr Get(ulong instance, string name)
        {
            var bytes = Encoding.ASCII.GetBytes(name + "\0");
            IntPtr fn = IntPtr.Zero;
            fixed (byte* p = bytes)
            {
                var r = getInstanceProcAddr(instance, p, &fn);
                if (r != XrResult.Success) return IntPtr.Zero;
            }
            return fn;
        }

        private IntPtr Require(ulong instance, string name)
        {
            var fn = Get(instance, name);
            if (fn == IntPtr.Zero) throw new EntryPointNotFoundException("OpenXR function not available: " + name);
            return fn;
        }

        /// <summary>Resolves all instance level functions. Call once, right after xrCreateInstance.</summary>
        public void LoadInstanceFunctions(ulong instance, bool openGL)
        {
            Instance = instance;
            ulong i = instance;
            DestroyInstance = (delegate* unmanaged<ulong, XrResult>)Require(i, "xrDestroyInstance");
            GetInstanceProperties = (delegate* unmanaged<ulong, XrInstanceProperties*, XrResult>)Require(i, "xrGetInstanceProperties");
            PollEvent = (delegate* unmanaged<ulong, XrEventDataBuffer*, XrResult>)Require(i, "xrPollEvent");
            ResultToString = (delegate* unmanaged<ulong, XrResult, byte*, XrResult>)Require(i, "xrResultToString");
            StringToPath = (delegate* unmanaged<ulong, byte*, ulong*, XrResult>)Require(i, "xrStringToPath");
            GetSystem = (delegate* unmanaged<ulong, XrSystemGetInfo*, ulong*, XrResult>)Require(i, "xrGetSystem");
            GetSystemProperties = (delegate* unmanaged<ulong, ulong, XrSystemProperties*, XrResult>)Require(i, "xrGetSystemProperties");
            EnumerateViewConfigurationViews = (delegate* unmanaged<ulong, ulong, int, uint, uint*, XrViewConfigurationView*, XrResult>)Require(i, "xrEnumerateViewConfigurationViews");
            EnumerateEnvironmentBlendModes = (delegate* unmanaged<ulong, ulong, int, uint, uint*, int*, XrResult>)Require(i, "xrEnumerateEnvironmentBlendModes");
            CreateSession = (delegate* unmanaged<ulong, XrSessionCreateInfo*, ulong*, XrResult>)Require(i, "xrCreateSession");
            CreateActionSet = (delegate* unmanaged<ulong, XrActionSetCreateInfo*, ulong*, XrResult>)Require(i, "xrCreateActionSet");
            SuggestInteractionProfileBindings = (delegate* unmanaged<ulong, XrInteractionProfileSuggestedBinding*, XrResult>)Require(i, "xrSuggestInteractionProfileBindings");

            DestroySession = (delegate* unmanaged<ulong, XrResult>)Require(i, "xrDestroySession");
            EnumerateReferenceSpaces = (delegate* unmanaged<ulong, uint, uint*, XrReferenceSpaceType*, XrResult>)Require(i, "xrEnumerateReferenceSpaces");
            CreateReferenceSpace = (delegate* unmanaged<ulong, XrReferenceSpaceCreateInfo*, ulong*, XrResult>)Require(i, "xrCreateReferenceSpace");
            CreateActionSpace = (delegate* unmanaged<ulong, XrActionSpaceCreateInfo*, ulong*, XrResult>)Require(i, "xrCreateActionSpace");
            LocateSpace = (delegate* unmanaged<ulong, ulong, long, XrSpaceLocation*, XrResult>)Require(i, "xrLocateSpace");
            DestroySpace = (delegate* unmanaged<ulong, XrResult>)Require(i, "xrDestroySpace");
            EnumerateSwapchainFormats = (delegate* unmanaged<ulong, uint, uint*, long*, XrResult>)Require(i, "xrEnumerateSwapchainFormats");
            CreateSwapchain = (delegate* unmanaged<ulong, XrSwapchainCreateInfo*, ulong*, XrResult>)Require(i, "xrCreateSwapchain");
            DestroySwapchain = (delegate* unmanaged<ulong, XrResult>)Require(i, "xrDestroySwapchain");
            EnumerateSwapchainImages = (delegate* unmanaged<ulong, uint, uint*, void*, XrResult>)Require(i, "xrEnumerateSwapchainImages");
            AcquireSwapchainImage = (delegate* unmanaged<ulong, XrSwapchainImageAcquireInfo*, uint*, XrResult>)Require(i, "xrAcquireSwapchainImage");
            WaitSwapchainImage = (delegate* unmanaged<ulong, XrSwapchainImageWaitInfo*, XrResult>)Require(i, "xrWaitSwapchainImage");
            ReleaseSwapchainImage = (delegate* unmanaged<ulong, XrSwapchainImageReleaseInfo*, XrResult>)Require(i, "xrReleaseSwapchainImage");
            BeginSession = (delegate* unmanaged<ulong, XrSessionBeginInfo*, XrResult>)Require(i, "xrBeginSession");
            EndSession = (delegate* unmanaged<ulong, XrResult>)Require(i, "xrEndSession");
            RequestExitSession = (delegate* unmanaged<ulong, XrResult>)Require(i, "xrRequestExitSession");
            WaitFrame = (delegate* unmanaged<ulong, XrFrameWaitInfo*, XrFrameState*, XrResult>)Require(i, "xrWaitFrame");
            BeginFrame = (delegate* unmanaged<ulong, XrFrameBeginInfo*, XrResult>)Require(i, "xrBeginFrame");
            EndFrame = (delegate* unmanaged<ulong, XrFrameEndInfo*, XrResult>)Require(i, "xrEndFrame");
            LocateViews = (delegate* unmanaged<ulong, XrViewLocateInfo*, XrViewState*, uint, uint*, XrView*, XrResult>)Require(i, "xrLocateViews");

            DestroyActionSet = (delegate* unmanaged<ulong, XrResult>)Require(i, "xrDestroyActionSet");
            CreateAction = (delegate* unmanaged<ulong, XrActionCreateInfo*, ulong*, XrResult>)Require(i, "xrCreateAction");
            AttachSessionActionSets = (delegate* unmanaged<ulong, XrSessionActionSetsAttachInfo*, XrResult>)Require(i, "xrAttachSessionActionSets");
            SyncActions = (delegate* unmanaged<ulong, XrActionsSyncInfo*, XrResult>)Require(i, "xrSyncActions");
            GetActionStateBoolean = (delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateBoolean*, XrResult>)Require(i, "xrGetActionStateBoolean");
            GetActionStateFloat = (delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateFloat*, XrResult>)Require(i, "xrGetActionStateFloat");
            GetActionStateVector2f = (delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateVector2f*, XrResult>)Require(i, "xrGetActionStateVector2f");
            GetActionStatePose = (delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStatePose*, XrResult>)Require(i, "xrGetActionStatePose");
            ApplyHapticFeedback = (delegate* unmanaged<ulong, XrHapticActionInfo*, void*, XrResult>)Require(i, "xrApplyHapticFeedback");

            if (openGL)
            {
                GetOpenGLGraphicsRequirementsKHR = (delegate* unmanaged<ulong, ulong, XrGraphicsRequirementsOpenGLKHR*, XrResult>)Require(i, "xrGetOpenGLGraphicsRequirementsKHR");
            }
        }

        public string ResultName(XrResult result)
        {
            if (Instance == 0 || ResultToString == null) return result.ToString();
            var buffer = stackalloc byte[XrConst.MaxResultStringSize];
            if (ResultToString(Instance, result, buffer) != XrResult.Success) return result.ToString();
            return XrConst.ReadString(buffer, XrConst.MaxResultStringSize);
        }

        /// <summary>Throws for any failure code (negative XrResult). Qualified success codes pass through.</summary>
        public XrResult Check(XrResult result, string call)
        {
            if ((int)result < 0) throw new XrException(call, result, ResultName(result));
            return result;
        }

        public ulong Path(string path)
        {
            var bytes = Encoding.UTF8.GetBytes(path + "\0");
            ulong result;
            fixed (byte* p = bytes)
            {
                Check(StringToPath(Instance, p, &result), "xrStringToPath(" + path + ")");
            }
            return result;
        }
    }
}
