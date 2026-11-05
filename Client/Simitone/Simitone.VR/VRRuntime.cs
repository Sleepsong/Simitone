using System;
using System.Collections.Generic;
using System.Linq;
using Simitone.VR.Graphics;
using Simitone.VR.OpenXR;

namespace Simitone.VR
{
    /// <summary>An OpenXR swapchain of OpenGL textures.</summary>
    public unsafe sealed class VRSwapchain : IDisposable
    {
        private readonly XrApi Api;
        public ulong Handle { get; private set; }
        public int Width { get; }
        public int Height { get; }
        public long Format { get; }
        public uint[] Images { get; }
        private bool Acquired;

        internal VRSwapchain(XrApi api, ulong session, long format, int width, int height)
        {
            Api = api;
            Width = width;
            Height = height;
            Format = format;
            var info = new XrSwapchainCreateInfo
            {
                Type = XrStructureType.SwapchainCreateInfo,
                UsageFlags = XrConst.SwapchainUsageColorAttachment | XrConst.SwapchainUsageTransferDst | XrConst.SwapchainUsageSampled,
                Format = format,
                SampleCount = 1,
                Width = (uint)width,
                Height = (uint)height,
                FaceCount = 1,
                ArraySize = 1,
                MipCount = 1
            };
            ulong handle;
            api.Check(api.CreateSwapchain(session, &info, &handle), "xrCreateSwapchain");
            Handle = handle;

            uint count;
            api.Check(api.EnumerateSwapchainImages(handle, 0, &count, null), "xrEnumerateSwapchainImages");
            var images = new XrSwapchainImageOpenGLKHR[count];
            for (int i = 0; i < count; i++) images[i].Type = XrStructureType.SwapchainImageOpenGLKHR;
            fixed (XrSwapchainImageOpenGLKHR* p = images)
                api.Check(api.EnumerateSwapchainImages(handle, count, &count, p), "xrEnumerateSwapchainImages");
            Images = images.Select(x => x.Image).ToArray();
        }

        /// <summary>Acquires the next image and waits until the compositor is done with it. Returns its GL texture.</summary>
        public uint Acquire()
        {
            var acquire = new XrSwapchainImageAcquireInfo { Type = XrStructureType.SwapchainImageAcquireInfo };
            uint index;
            Api.Check(Api.AcquireSwapchainImage(Handle, &acquire, &index), "xrAcquireSwapchainImage");
            var wait = new XrSwapchainImageWaitInfo { Type = XrStructureType.SwapchainImageWaitInfo, Timeout = XrConst.InfiniteDuration };
            Api.Check(Api.WaitSwapchainImage(Handle, &wait), "xrWaitSwapchainImage");
            Acquired = true;
            return Images[index];
        }

        public void Release()
        {
            if (!Acquired) return;
            var release = new XrSwapchainImageReleaseInfo { Type = XrStructureType.SwapchainImageReleaseInfo };
            Api.Check(Api.ReleaseSwapchainImage(Handle, &release), "xrReleaseSwapchainImage");
            Acquired = false;
        }

        public XrSwapchainSubImage SubImage => new XrSwapchainSubImage
        {
            Swapchain = Handle,
            ImageRect = new XrRect2Di { Extent = new XrExtent2Di { Width = Width, Height = Height } },
            ImageArrayIndex = 0
        };

        public void Dispose()
        {
            if (Handle != 0) Api.DestroySwapchain(Handle);
            Handle = 0;
        }
    }

    public struct VREyeView
    {
        public XrPosef Pose;     // in application (tracking) space, metres
        public XrFovf Fov;
    }

    /// <summary>A flat panel in tracking space: the virtual screen, the UI panel, a wrist display.</summary>
    public struct VRQuad
    {
        public VRSwapchain Swapchain;
        public XrPosef Pose;
        public float Width, Height;
        /// <summary>Blend with the texture's (premultiplied) alpha instead of drawing an opaque rectangle.</summary>
        public bool Transparent;
    }

    /// <summary>What to show in the headset this frame.</summary>
    public struct VRFrameSubmission
    {
        public const int MaxQuads = 8;

        /// <summary>Stereo world view (one swapchain per eye, rendered with <see cref="VRRuntime.Views"/>).</summary>
        public VRSwapchain[] ProjectionSwapchains;
        /// <summary>Quads drawn over the world, back to front.</summary>
        public VRQuad[] Quads;
        public int QuadCount;

        public void AddQuad(VRQuad quad)
        {
            Quads ??= new VRQuad[MaxQuads];
            if (QuadCount < MaxQuads) Quads[QuadCount++] = quad;
        }
    }

    /// <summary>
    /// Owns the OpenXR instance, session, reference spaces and frame loop.
    /// The frame loop is driven by the game: <see cref="BeginFrame"/> before drawing, <see cref="EndFrame"/> after.
    /// </summary>
    public unsafe sealed class VRRuntime : IDisposable
    {
        public XrApi Api { get; private set; }
        public ulong Instance { get; private set; }
        public ulong SystemId { get; private set; }
        public ulong Session { get; private set; }
        public ulong AppSpace { get; private set; }
        public XrReferenceSpaceType AppSpaceType { get; private set; }
        public XrSessionState SessionState { get; private set; } = XrSessionState.Unknown;
        public bool SessionRunning { get; private set; }
        public bool Lost { get; private set; }
        public bool ExitRequested { get; private set; }

        public string RuntimeName { get; private set; } = "";
        public string SystemName { get; private set; } = "";
        public GLContextInfo GLContext { get; private set; }
        public long SwapchainFormat { get; private set; }
        public int RecommendedWidth { get; private set; }
        public int RecommendedHeight { get; private set; }
        public int MaxWidth { get; private set; }
        public int MaxHeight { get; private set; }
        public HashSet<string> EnabledExtensions { get; } = new HashSet<string>();

        public VRInput Input { get; private set; }
        /// <summary>The headset tracks your eyes and lets apps read the gaze (XR_EXT_eye_gaze_interaction).</summary>
        public bool EyeGazeSupported { get; private set; }
        /// <summary>The controllers as the runtime models them, or null if it can't (see <see cref="VRRenderModels"/>).</summary>
        public VRRenderModels RenderModels { get; private set; }

        // per frame
        public bool FrameInProgress { get; private set; }
        public bool ShouldRender { get; private set; }
        public long PredictedDisplayTime { get; private set; }
        public double PredictedDisplayPeriod { get; private set; } = 1 / 90.0;
        public bool ViewsValid { get; private set; }
        public readonly VREyeView[] Views = new VREyeView[2];

        /// <summary>Raised when the runtime moves the tracking origin (e.g. the user recentred from the system menu).</summary>
        public event Action ReferenceSpaceChanged;

        private readonly Action<string> Log;

        public VRRuntime(Action<string> log)
        {
            Log = log ?? (s => { });
        }

        // ------------------------------------------------------------------ initialisation

        public bool Initialize(string applicationName, out string error)
        {
            error = null;
            try
            {
                InitializeInternal(applicationName);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Log("OpenXR: initialisation failed: " + e);
                Dispose();
                return false;
            }
        }

        private void InitializeInternal(string applicationName)
        {
            if (!XrLoader.Load(Log, out var loadError)) throw new InvalidOperationException("OpenXR unavailable: " + loadError);
            Api = new XrApi(XrLoader.GetInstanceProcAddr);

            GLContext = GLInterop.CaptureCurrentContext();
            Log("OpenXR: GL context " + GLContext + ", GL " + GLInterop.GLVersion + " on " + GLInterop.GLRenderer);
            if (GLContext.Kind == GLContextKind.None) throw new InvalidOperationException("No current OpenGL context to share with the OpenXR runtime.");

            // ---- extensions
            var available = EnumerateExtensions();
            Log("OpenXR: runtime extensions: " + string.Join(" ", available.OrderBy(x => x)));
            var wanted = new List<string> { "XR_KHR_opengl_enable" };
            if (!available.Contains("XR_KHR_opengl_enable"))
                throw new InvalidOperationException("The OpenXR runtime does not support OpenGL (XR_KHR_opengl_enable).");
            if (GLContext.Kind == GLContextKind.EGL)
            {
                if (!available.Contains("XR_MNDX_egl_enable"))
                    throw new InvalidOperationException("SDL created an EGL context but the runtime lacks XR_MNDX_egl_enable. Try SDL_VIDEODRIVER=x11.");
                wanted.Add("XR_MNDX_egl_enable");
            }
            foreach (var optional in new[] { "XR_EXT_local_floor", VRInput.FrameControllerExtension, VRInput.EyeGazeExtension })
                if (available.Contains(optional)) wanted.Add(optional);
            // the controllers' own models (SteamVR, including on the Steam Frame), when the runtime offers them
            if (available.Contains(XrRenderModelApi.RenderModelExtension) && available.Contains(XrRenderModelApi.InteractionRenderModelExtension))
            {
                wanted.Add(XrRenderModelApi.RenderModelExtension);
                wanted.Add(XrRenderModelApi.InteractionRenderModelExtension);
            }

            // ---- instance
            var namePtrs = wanted.Select(s => System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(s)).ToArray();
            try
            {
                fixed (IntPtr* names = namePtrs)
                {
                    var create = new XrInstanceCreateInfo
                    {
                        Type = XrStructureType.InstanceCreateInfo,
                        EnabledExtensionCount = (uint)wanted.Count,
                        EnabledExtensionNames = (byte**)names,
                    };
                    XrConst.WriteString(create.ApplicationInfo.ApplicationName, XrConst.MaxApplicationNameSize, applicationName);
                    XrConst.WriteString(create.ApplicationInfo.EngineName, XrConst.MaxEngineNameSize, "FreeSO");
                    create.ApplicationInfo.ApplicationVersion = 1;
                    create.ApplicationInfo.EngineVersion = 1;
                    // Ask for 1.0 - SteamVR on the Steam Frame and older runtimes may not accept 1.1.
                    create.ApplicationInfo.ApiVersion = XrConst.MakeVersion(1, 0, 0);
                    ulong instance;
                    var r = Api.CreateInstance(&create, &instance);
                    if ((int)r < 0) throw new XrException("xrCreateInstance", r);
                    Instance = instance;
                }
            }
            finally
            {
                foreach (var p in namePtrs) System.Runtime.InteropServices.Marshal.FreeHGlobal(p);
            }
            foreach (var ext in wanted) EnabledExtensions.Add(ext);
            Api.LoadInstanceFunctions(Instance, true);

            var props = new XrInstanceProperties { Type = XrStructureType.InstanceProperties };
            Api.Check(Api.GetInstanceProperties(Instance, &props), "xrGetInstanceProperties");
            RuntimeName = XrConst.ReadString(props.RuntimeName, XrConst.MaxRuntimeNameSize) + " " + XrConst.VersionString(props.RuntimeVersion);
            Log("OpenXR: runtime " + RuntimeName);

            // ---- system
            var sysInfo = new XrSystemGetInfo { Type = XrStructureType.SystemGetInfo, FormFactor = XrConst.FormFactorHeadMountedDisplay };
            ulong systemId;
            Api.Check(Api.GetSystem(Instance, &sysInfo, &systemId), "xrGetSystem (is the headset connected and SteamVR running?)");
            SystemId = systemId;
            var sysProps = new XrSystemProperties { Type = XrStructureType.SystemProperties };
            var eyeGazeProps = new XrSystemEyeGazeInteractionPropertiesEXT { Type = XrStructureType.SystemEyeGazeInteractionPropertiesEXT };
            if (EnabledExtensions.Contains(VRInput.EyeGazeExtension)) sysProps.Next = &eyeGazeProps;
            Api.Check(Api.GetSystemProperties(Instance, SystemId, &sysProps), "xrGetSystemProperties");
            EyeGazeSupported = EnabledExtensions.Contains(VRInput.EyeGazeExtension) && eyeGazeProps.SupportsEyeGazeInteraction != 0;
            if (EnabledExtensions.Contains(VRInput.EyeGazeExtension))
                Log("OpenXR: eye tracking " + (EyeGazeSupported ? "available" : "not available on this headset"));
            SystemName = XrConst.ReadString(sysProps.SystemName, XrConst.MaxSystemNameSize);
            Log($"OpenXR: system '{SystemName}' (vendor 0x{sysProps.VendorId:x}), position tracking: {sysProps.TrackingProperties.PositionTracking != 0}");

            uint viewCount;
            Api.Check(Api.EnumerateViewConfigurationViews(Instance, SystemId, XrConst.ViewConfigurationPrimaryStereo, 0, &viewCount, null), "xrEnumerateViewConfigurationViews");
            var configViews = new XrViewConfigurationView[viewCount];
            for (int i = 0; i < viewCount; i++) configViews[i].Type = XrStructureType.ViewConfigurationView;
            fixed (XrViewConfigurationView* p = configViews)
                Api.Check(Api.EnumerateViewConfigurationViews(Instance, SystemId, XrConst.ViewConfigurationPrimaryStereo, viewCount, &viewCount, p), "xrEnumerateViewConfigurationViews");
            if (viewCount != 2) throw new InvalidOperationException($"Expected 2 stereo views, runtime reported {viewCount}.");
            RecommendedWidth = (int)configViews[0].RecommendedImageRectWidth;
            RecommendedHeight = (int)configViews[0].RecommendedImageRectHeight;
            MaxWidth = (int)configViews[0].MaxImageRectWidth;
            MaxHeight = (int)configViews[0].MaxImageRectHeight;
            Log($"OpenXR: recommended eye resolution {RecommendedWidth}x{RecommendedHeight} (max {MaxWidth}x{MaxHeight})");

            uint blendCount;
            Api.Check(Api.EnumerateEnvironmentBlendModes(Instance, SystemId, XrConst.ViewConfigurationPrimaryStereo, 0, &blendCount, null), "xrEnumerateEnvironmentBlendModes");
            var blends = new int[blendCount];
            fixed (int* p = blends)
                Api.Check(Api.EnumerateEnvironmentBlendModes(Instance, SystemId, XrConst.ViewConfigurationPrimaryStereo, blendCount, &blendCount, p), "xrEnumerateEnvironmentBlendModes");
            if (!blends.Contains(XrConst.EnvironmentBlendModeOpaque))
                throw new InvalidOperationException("The headset does not support opaque rendering.");

            // ---- graphics requirements (mandatory before xrCreateSession)
            var req = new XrGraphicsRequirementsOpenGLKHR { Type = XrStructureType.GraphicsRequirementsOpenGLKHR };
            Api.Check(Api.GetOpenGLGraphicsRequirementsKHR(Instance, SystemId, &req), "xrGetOpenGLGraphicsRequirementsKHR");
            Log($"OpenXR: runtime wants OpenGL {XrConst.VersionString(req.MinApiVersionSupported)} - {XrConst.VersionString(req.MaxApiVersionSupported)}");

            // ---- input (bindings must be suggested before the action set is attached)
            Input = new VRInput(Log);
            Input.Create(Api, EnabledExtensions.Contains(VRInput.FrameControllerExtension), EyeGazeSupported);

            // ---- session
            CreateSession();
            Input.Attach(Session);
            CreateAppSpace();
            if (EnabledExtensions.Contains(XrRenderModelApi.InteractionRenderModelExtension))
            {
                var rm = XrRenderModelApi.Load(Api, Instance);
                if (rm != null) RenderModels = new VRRenderModels(Api, rm, Log);
                Log(rm != null ? "OpenXR: controller models will come from the runtime" : "OpenXR: render model functions missing; using built-in controller models");
            }
            else Log("OpenXR: the runtime doesn't offer controller models (XR_EXT_interaction_render_model); using built-in ones");

            // ---- swapchain format: sRGB storage for MonoGame's gamma-space output (copied without conversion).
            uint formatCount;
            Api.Check(Api.EnumerateSwapchainFormats(Session, 0, &formatCount, null), "xrEnumerateSwapchainFormats");
            var formats = new long[formatCount];
            fixed (long* p = formats)
                Api.Check(Api.EnumerateSwapchainFormats(Session, formatCount, &formatCount, p), "xrEnumerateSwapchainFormats");
            var preferred = new[] { GLInterop.GL_SRGB8_ALPHA8, GLInterop.GL_RGBA8 };
            SwapchainFormat = preferred.FirstOrDefault(f => formats.Contains(f));
            if (SwapchainFormat == 0) SwapchainFormat = formats.Length > 0 ? formats[0] : GLInterop.GL_RGBA8;
            Log($"OpenXR: swapchain format 0x{SwapchainFormat:x} (offered: {string.Join(", ", formats.Select(f => "0x" + f.ToString("x")))})");
        }

        private HashSet<string> EnumerateExtensions()
        {
            uint count;
            Api.Check(Api.EnumerateInstanceExtensionProperties(null, 0, &count, null), "xrEnumerateInstanceExtensionProperties");
            var props = new XrExtensionProperties[count];
            for (int i = 0; i < count; i++) props[i].Type = XrStructureType.ExtensionProperties;
            fixed (XrExtensionProperties* p = props)
                Api.Check(Api.EnumerateInstanceExtensionProperties(null, count, &count, p), "xrEnumerateInstanceExtensionProperties");
            var result = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                fixed (byte* name = props[i].ExtensionName)
                    result.Add(XrConst.ReadString(name, XrConst.MaxExtensionNameSize));
            }
            return result;
        }

        private void CreateSession()
        {
            var ctx = GLContext;
            ulong session;
            XrResult r;
            switch (ctx.Kind)
            {
                case GLContextKind.GLX:
                    {
                        var binding = new XrGraphicsBindingOpenGLXlibKHR
                        {
                            Type = XrStructureType.GraphicsBindingOpenGLXlibKHR,
                            XDisplay = ctx.Display,
                            VisualId = ctx.VisualId,
                            GlxFBConfig = ctx.FBConfig,
                            GlxDrawable = ctx.Drawable,
                            GlxContext = ctx.Context
                        };
                        var info = new XrSessionCreateInfo { Type = XrStructureType.SessionCreateInfo, Next = &binding, SystemId = SystemId };
                        r = Api.CreateSession(Instance, &info, &session);
                        break;
                    }
                case GLContextKind.EGL:
                    {
                        var binding = new XrGraphicsBindingEGLMNDX
                        {
                            Type = XrStructureType.GraphicsBindingEglMNDX,
                            GetProcAddress = ctx.EglGetProcAddress,
                            Display = ctx.Display,
                            Config = ctx.FBConfig,
                            Context = ctx.Context
                        };
                        var info = new XrSessionCreateInfo { Type = XrStructureType.SessionCreateInfo, Next = &binding, SystemId = SystemId };
                        r = Api.CreateSession(Instance, &info, &session);
                        break;
                    }
                case GLContextKind.WGL:
                    {
                        var binding = new XrGraphicsBindingOpenGLWin32KHR
                        {
                            Type = XrStructureType.GraphicsBindingOpenGLWin32KHR,
                            HDC = ctx.HDC,
                            HGLRC = ctx.Context
                        };
                        var info = new XrSessionCreateInfo { Type = XrStructureType.SessionCreateInfo, Next = &binding, SystemId = SystemId };
                        r = Api.CreateSession(Instance, &info, &session);
                        break;
                    }
                default:
                    throw new InvalidOperationException("Unsupported GL context type " + ctx.Kind);
            }
            GLInterop.EnsureContext(GLContext);
            Api.Check(r, "xrCreateSession");
            Session = session;
            Log("OpenXR: session created");
        }

        private void CreateAppSpace()
        {
            uint count;
            Api.Check(Api.EnumerateReferenceSpaces(Session, 0, &count, null), "xrEnumerateReferenceSpaces");
            var types = new XrReferenceSpaceType[count];
            fixed (XrReferenceSpaceType* p = types)
                Api.Check(Api.EnumerateReferenceSpaces(Session, count, &count, p), "xrEnumerateReferenceSpaces");

            // Floor-relative spaces give real standing height. LOCAL_FLOOR keeps the origin where the player is.
            var order = new List<XrReferenceSpaceType>();
            if (EnabledExtensions.Contains("XR_EXT_local_floor")) order.Add(XrReferenceSpaceType.LocalFloorEXT);
            order.Add(XrReferenceSpaceType.Stage);
            order.Add(XrReferenceSpaceType.Local);

            foreach (var type in order)
            {
                if (type != XrReferenceSpaceType.LocalFloorEXT && !types.Contains(type)) continue;
                var info = new XrReferenceSpaceCreateInfo
                {
                    Type = XrStructureType.ReferenceSpaceCreateInfo,
                    ReferenceSpaceType = type,
                    PoseInReferenceSpace = XrPosef.Identity
                };
                ulong space;
                if (Api.CreateReferenceSpace(Session, &info, &space) == XrResult.Success)
                {
                    AppSpace = space;
                    AppSpaceType = type;
                    Log("OpenXR: tracking space " + type);
                    return;
                }
            }
            throw new InvalidOperationException("Could not create any OpenXR reference space.");
        }

        /// <summary>True if eye heights are relative to the floor (STAGE / LOCAL_FLOOR) rather than the initial head position.</summary>
        public bool FloorRelative => AppSpaceType == XrReferenceSpaceType.Stage || AppSpaceType == XrReferenceSpaceType.LocalFloorEXT;

        public VRSwapchain CreateSwapchain(int width, int height)
        {
            var sc = new VRSwapchain(Api, Session, SwapchainFormat, width, height);
            GLInterop.EnsureContext(GLContext);
            return sc;
        }

        // ------------------------------------------------------------------ events

        public void PollEvents()
        {
            if (Api == null || Instance == 0) return;
            var buffer = new XrEventDataBuffer();
            while (true)
            {
                buffer.Type = XrStructureType.EventDataBuffer;
                buffer.Next = null;
                var r = Api.PollEvent(Instance, &buffer);
                if (r != XrResult.Success) break;

                switch (buffer.Type)
                {
                    case XrStructureType.EventDataSessionStateChanged:
                        var changed = *(XrEventDataSessionStateChanged*)&buffer;
                        HandleSessionState(changed.State);
                        break;
                    case XrStructureType.EventDataInstanceLossPending:
                        Log("OpenXR: instance loss pending - VR disabled until restart");
                        Lost = true;
                        SessionRunning = false;
                        break;
                    case XrStructureType.EventDataReferenceSpaceChangePending:
                        ReferenceSpaceChanged?.Invoke();
                        break;
                    case XrStructureType.EventDataInteractionProfileChanged:
                        Log("OpenXR: interaction profile changed");
                        break;
                    case XrStructureType.EventDataInteractionRenderModelsChangedEXT:
                        if (RenderModels != null) RenderModels.NeedsEnumerate = true;
                        break;
                }
            }
        }

        private void HandleSessionState(XrSessionState state)
        {
            Log($"OpenXR: session state {SessionState} -> {state}");
            SessionState = state;
            switch (state)
            {
                case XrSessionState.Ready:
                    var begin = new XrSessionBeginInfo
                    {
                        Type = XrStructureType.SessionBeginInfo,
                        PrimaryViewConfigurationType = XrConst.ViewConfigurationPrimaryStereo
                    };
                    Api.Check(Api.BeginSession(Session, &begin), "xrBeginSession");
                    GLInterop.EnsureContext(GLContext);
                    SessionRunning = true;
                    break;
                case XrSessionState.Stopping:
                    SessionRunning = false;
                    Api.Check(Api.EndSession(Session), "xrEndSession");
                    break;
                case XrSessionState.Exiting:
                    ExitRequested = true;
                    SessionRunning = false;
                    break;
                case XrSessionState.LossPending:
                    Lost = true;
                    SessionRunning = false;
                    break;
            }
        }

        public bool Focused => SessionState == XrSessionState.Focused;

        // ------------------------------------------------------------------ frame loop

        /// <summary>
        /// Waits for the runtime's frame timing, begins the frame and locates the eyes.
        /// Returns false when no frame was started (session not running); then do not call <see cref="EndFrame"/>.
        /// </summary>
        public bool BeginFrame(float deadZone)
        {
            PollEvents();
            if (!SessionRunning || Lost) return false;

            var waitInfo = new XrFrameWaitInfo { Type = XrStructureType.FrameWaitInfo };
            var frameState = new XrFrameState { Type = XrStructureType.FrameState };
            Api.Check(Api.WaitFrame(Session, &waitInfo, &frameState), "xrWaitFrame");
            var beginInfo = new XrFrameBeginInfo { Type = XrStructureType.FrameBeginInfo };
            Api.Check(Api.BeginFrame(Session, &beginInfo), "xrBeginFrame");
            GLInterop.EnsureContext(GLContext);

            FrameInProgress = true;
            ShouldRender = frameState.ShouldRender != 0;
            PredictedDisplayTime = frameState.PredictedDisplayTime;
            if (frameState.PredictedDisplayPeriod > 0) PredictedDisplayPeriod = frameState.PredictedDisplayPeriod / 1e9;

            ViewsValid = false;
            if (ShouldRender) LocateViews();

            if (Focused)
            {
                Input.Sync(Session, AppSpace, PredictedDisplayTime, deadZone);
                RenderModels?.Update(Session, AppSpace, PredictedDisplayTime);
            }
            else
            {
                Input.Clear();
                RenderModels?.Untrack();
            }
            return true;
        }

        private void LocateViews()
        {
            var locate = new XrViewLocateInfo
            {
                Type = XrStructureType.ViewLocateInfo,
                ViewConfigurationType = XrConst.ViewConfigurationPrimaryStereo,
                DisplayTime = PredictedDisplayTime,
                Space = AppSpace
            };
            var viewState = new XrViewState { Type = XrStructureType.ViewState };
            var views = stackalloc XrView[2];
            views[0].Type = views[1].Type = XrStructureType.View;
            uint count;
            var r = Api.LocateViews(Session, &locate, &viewState, 2, &count, views);
            if (r != XrResult.Success || count != 2) return;
            const ulong both = XrConst.ViewStateOrientationValid | XrConst.ViewStatePositionValid;
            if ((viewState.ViewStateFlags & both) != both) return;
            for (int i = 0; i < 2; i++)
            {
                Views[i].Pose = views[i].Pose;
                Views[i].Fov = views[i].Fov;
            }
            ViewsValid = true;
        }

        /// <summary>Submits the frame. Pass default to submit nothing (the runtime shows its own void).</summary>
        public void EndFrame(in VRFrameSubmission submission)
        {
            if (!FrameInProgress) return;
            FrameInProgress = false;

            var layers = stackalloc void*[1 + VRFrameSubmission.MaxQuads];
            uint layerCount = 0;

            var projectionViews = stackalloc XrCompositionLayerProjectionView[2];
            var projection = new XrCompositionLayerProjection();
            var quads = stackalloc XrCompositionLayerQuad[VRFrameSubmission.MaxQuads];

            if (ShouldRender && ViewsValid && submission.ProjectionSwapchains != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    projectionViews[i] = new XrCompositionLayerProjectionView
                    {
                        Type = XrStructureType.CompositionLayerProjectionView,
                        Pose = Views[i].Pose,
                        Fov = Views[i].Fov,
                        SubImage = submission.ProjectionSwapchains[i].SubImage
                    };
                }
                projection.Type = XrStructureType.CompositionLayerProjection;
                projection.Space = AppSpace;
                projection.ViewCount = 2;
                projection.Views = projectionViews;
                layers[layerCount++] = &projection;
            }

            if (ShouldRender)
            {
                for (int i = 0; i < submission.QuadCount; i++)
                {
                    var q = submission.Quads[i];
                    if (q.Swapchain == null) continue;
                    quads[i] = new XrCompositionLayerQuad
                    {
                        Type = XrStructureType.CompositionLayerQuad,
                        LayerFlags = q.Transparent ? XrConst.CompositionLayerBlendTextureSourceAlpha : 0,
                        Space = AppSpace,
                        EyeVisibility = XrConst.EyeVisibilityBoth,
                        SubImage = q.Swapchain.SubImage,
                        Pose = q.Pose,
                        Size = new XrExtent2Df { Width = q.Width, Height = q.Height }
                    };
                    layers[layerCount++] = &quads[i];
                }
            }

            var end = new XrFrameEndInfo
            {
                Type = XrStructureType.FrameEndInfo,
                DisplayTime = PredictedDisplayTime,
                EnvironmentBlendMode = XrConst.EnvironmentBlendModeOpaque,
                LayerCount = layerCount,
                Layers = layers
            };
            var r = Api.EndFrame(Session, &end);
            GLInterop.EnsureContext(GLContext);
            Api.Check(r, "xrEndFrame");
        }

        /// <summary>A buzz in the pointing hand (true; right unless left-handed) or the walking hand (false).</summary>
        public void Haptic(bool pointingHand, float amplitude, float seconds)
        {
            if (SessionRunning && Focused) Input?.Haptic(Session, pointingHand, amplitude, seconds);
        }

        public void RequestExit()
        {
            if (Session != 0 && SessionRunning) Api.RequestExitSession(Session);
        }

        public void Dispose()
        {
            if (Api == null) return;
            try
            {
                RenderModels?.Dispose();
                RenderModels = null;
                Input?.Dispose();
                Input = null;
                if (AppSpace != 0) Api.DestroySpace(AppSpace);
                AppSpace = 0;
                if (Session != 0) Api.DestroySession(Session);
                Session = 0;
                if (Instance != 0) Api.DestroyInstance(Instance);
                Instance = 0;
            }
            catch (Exception e)
            {
                Log("OpenXR: error during shutdown: " + e.Message);
            }
            SessionRunning = false;
        }
    }
}
