/* Prints sizeof/offsetof for every OpenXR struct Simitone.VR binds by hand.
 * Compared against the C# side by run.sh. Needs the Khronos OpenXR headers (libopenxr-dev) and GL/EGL/X11 headers. */
#include <stdio.h>
#include <stddef.h>
#include <X11/Xlib.h>
#include <GL/glx.h>
#include <EGL/egl.h>
#define XR_USE_PLATFORM_XLIB
#define XR_USE_PLATFORM_EGL
#define XR_USE_GRAPHICS_API_OPENGL
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>

#define S(t) printf("%s size %zu\n", #t, sizeof(t));
#define O(t, f) printf("%s.%s %zu\n", #t, #f, offsetof(t, f));

int main(void) {
  S(XrPosef) S(XrFovf) S(XrSwapchainSubImage) O(XrSwapchainSubImage, imageRect) O(XrSwapchainSubImage, imageArrayIndex)
  S(XrExtensionProperties) O(XrExtensionProperties, extensionName) O(XrExtensionProperties, extensionVersion)
  S(XrApplicationInfo) O(XrApplicationInfo, applicationVersion) O(XrApplicationInfo, engineName) O(XrApplicationInfo, engineVersion) O(XrApplicationInfo, apiVersion)
  S(XrInstanceCreateInfo) O(XrInstanceCreateInfo, createFlags) O(XrInstanceCreateInfo, applicationInfo) O(XrInstanceCreateInfo, enabledApiLayerCount) O(XrInstanceCreateInfo, enabledApiLayerNames) O(XrInstanceCreateInfo, enabledExtensionCount) O(XrInstanceCreateInfo, enabledExtensionNames)
  S(XrInstanceProperties) O(XrInstanceProperties, runtimeVersion) O(XrInstanceProperties, runtimeName)
  S(XrSystemGetInfo) O(XrSystemGetInfo, formFactor)
  S(XrSystemProperties) O(XrSystemProperties, systemId) O(XrSystemProperties, vendorId) O(XrSystemProperties, systemName) O(XrSystemProperties, graphicsProperties) O(XrSystemProperties, trackingProperties)
  S(XrViewConfigurationView) O(XrViewConfigurationView, recommendedImageRectWidth) O(XrViewConfigurationView, maxSwapchainSampleCount)
  S(XrGraphicsRequirementsOpenGLKHR) O(XrGraphicsRequirementsOpenGLKHR, minApiVersionSupported) O(XrGraphicsRequirementsOpenGLKHR, maxApiVersionSupported)
  S(XrGraphicsBindingOpenGLXlibKHR) O(XrGraphicsBindingOpenGLXlibKHR, xDisplay) O(XrGraphicsBindingOpenGLXlibKHR, visualid) O(XrGraphicsBindingOpenGLXlibKHR, glxFBConfig) O(XrGraphicsBindingOpenGLXlibKHR, glxDrawable) O(XrGraphicsBindingOpenGLXlibKHR, glxContext)
  S(XrGraphicsBindingEGLMNDX) O(XrGraphicsBindingEGLMNDX, getProcAddress) O(XrGraphicsBindingEGLMNDX, display) O(XrGraphicsBindingEGLMNDX, config) O(XrGraphicsBindingEGLMNDX, context)
  S(XrSwapchainImageOpenGLKHR) O(XrSwapchainImageOpenGLKHR, image)
  S(XrSessionCreateInfo) O(XrSessionCreateInfo, createFlags) O(XrSessionCreateInfo, systemId)
  S(XrSessionBeginInfo) O(XrSessionBeginInfo, primaryViewConfigurationType)
  S(XrReferenceSpaceCreateInfo) O(XrReferenceSpaceCreateInfo, referenceSpaceType) O(XrReferenceSpaceCreateInfo, poseInReferenceSpace)
  S(XrActionSpaceCreateInfo) O(XrActionSpaceCreateInfo, action) O(XrActionSpaceCreateInfo, subactionPath) O(XrActionSpaceCreateInfo, poseInActionSpace)
  S(XrSpaceLocation) O(XrSpaceLocation, locationFlags) O(XrSpaceLocation, pose)
  S(XrSwapchainCreateInfo) O(XrSwapchainCreateInfo, createFlags) O(XrSwapchainCreateInfo, usageFlags) O(XrSwapchainCreateInfo, format) O(XrSwapchainCreateInfo, sampleCount) O(XrSwapchainCreateInfo, mipCount)
  S(XrSwapchainImageAcquireInfo) S(XrSwapchainImageWaitInfo) O(XrSwapchainImageWaitInfo, timeout) S(XrSwapchainImageReleaseInfo)
  S(XrFrameWaitInfo) S(XrFrameState) O(XrFrameState, predictedDisplayTime) O(XrFrameState, predictedDisplayPeriod) O(XrFrameState, shouldRender)
  S(XrFrameBeginInfo) S(XrFrameEndInfo) O(XrFrameEndInfo, displayTime) O(XrFrameEndInfo, environmentBlendMode) O(XrFrameEndInfo, layerCount) O(XrFrameEndInfo, layers)
  S(XrViewLocateInfo) O(XrViewLocateInfo, viewConfigurationType) O(XrViewLocateInfo, displayTime) O(XrViewLocateInfo, space)
  S(XrViewState) O(XrViewState, viewStateFlags)
  S(XrView) O(XrView, pose) O(XrView, fov)
  S(XrCompositionLayerProjectionView) O(XrCompositionLayerProjectionView, pose) O(XrCompositionLayerProjectionView, fov) O(XrCompositionLayerProjectionView, subImage)
  S(XrCompositionLayerProjection) O(XrCompositionLayerProjection, layerFlags) O(XrCompositionLayerProjection, space) O(XrCompositionLayerProjection, viewCount) O(XrCompositionLayerProjection, views)
  S(XrCompositionLayerQuad) O(XrCompositionLayerQuad, layerFlags) O(XrCompositionLayerQuad, space) O(XrCompositionLayerQuad, eyeVisibility) O(XrCompositionLayerQuad, subImage) O(XrCompositionLayerQuad, pose) O(XrCompositionLayerQuad, size)
  S(XrEventDataBuffer) O(XrEventDataBuffer, varying)
  S(XrEventDataSessionStateChanged) O(XrEventDataSessionStateChanged, session) O(XrEventDataSessionStateChanged, state) O(XrEventDataSessionStateChanged, time)
  S(XrActionSetCreateInfo) O(XrActionSetCreateInfo, actionSetName) O(XrActionSetCreateInfo, localizedActionSetName) O(XrActionSetCreateInfo, priority)
  S(XrActionCreateInfo) O(XrActionCreateInfo, actionName) O(XrActionCreateInfo, actionType) O(XrActionCreateInfo, countSubactionPaths) O(XrActionCreateInfo, subactionPaths) O(XrActionCreateInfo, localizedActionName)
  S(XrActionSuggestedBinding)
  S(XrInteractionProfileSuggestedBinding) O(XrInteractionProfileSuggestedBinding, interactionProfile) O(XrInteractionProfileSuggestedBinding, countSuggestedBindings) O(XrInteractionProfileSuggestedBinding, suggestedBindings)
  S(XrSessionActionSetsAttachInfo) O(XrSessionActionSetsAttachInfo, countActionSets) O(XrSessionActionSetsAttachInfo, actionSets)
  S(XrActiveActionSet) S(XrActionsSyncInfo) O(XrActionsSyncInfo, countActiveActionSets) O(XrActionsSyncInfo, activeActionSets)
  S(XrActionStateGetInfo) O(XrActionStateGetInfo, action) O(XrActionStateGetInfo, subactionPath)
  S(XrActionStateBoolean) O(XrActionStateBoolean, currentState) O(XrActionStateBoolean, changedSinceLastSync) O(XrActionStateBoolean, lastChangeTime) O(XrActionStateBoolean, isActive)
  S(XrActionStateFloat) O(XrActionStateFloat, currentState) O(XrActionStateFloat, lastChangeTime) O(XrActionStateFloat, isActive)
  S(XrActionStateVector2f) O(XrActionStateVector2f, currentState) O(XrActionStateVector2f, changedSinceLastSync) O(XrActionStateVector2f, lastChangeTime) O(XrActionStateVector2f, isActive)
  S(XrActionStatePose) O(XrActionStatePose, isActive)
  S(XrHapticActionInfo) O(XrHapticActionInfo, action) O(XrHapticActionInfo, subactionPath)
  S(XrHapticVibration) O(XrHapticVibration, duration) O(XrHapticVibration, frequency) O(XrHapticVibration, amplitude)
  return 0;
}
