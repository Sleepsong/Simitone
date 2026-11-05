using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Simitone.VR.OpenXR
{
    /// <summary>
    /// Finds an implementation of xrGetInstanceProcAddr.
    ///
    /// 1. The Khronos OpenXR loader (bundled next to the executable, or installed system-wide).
    /// 2. Failing that, the active runtime itself: we read active_runtime.json exactly like the loader does
    ///    and perform the loader/runtime negotiation (xrNegotiateLoaderRuntimeInterface) ourselves.
    ///
    /// Path 2 means a self-contained linux-arm64 build (Steam Frame) works with only SteamVR's runtime on
    /// the device, without shipping an arm64 libopenxr_loader. API layers are not supported on that path.
    /// </summary>
    public static unsafe class XrLoader
    {
        private const int LoaderInterfaceStructLoaderInfo = 1;
        private const int LoaderInterfaceStructRuntimeRequest = 3;
        private const uint LoaderInfoStructVersion = 1;
        private const uint RuntimeInfoStructVersion = 1;
        private const uint CurrentLoaderRuntimeVersion = 1;

        public static IntPtr GetInstanceProcAddr { get; private set; }
        public static string Description { get; private set; } = "not loaded";

        private static IntPtr LibraryHandle;

        public static bool Load(Action<string> log, out string error)
        {
            error = null;
            if (GetInstanceProcAddr != IntPtr.Zero) return true;

            var mode = (Environment.GetEnvironmentVariable("SIMITONE_XR_LOADER") ?? "auto").ToLowerInvariant();
            var errors = new List<string>();

            if (mode != "direct")
            {
                if (TryKhronosLoader(log, errors)) return true;
                if (mode == "khronos")
                {
                    error = string.Join("; ", errors);
                    return false;
                }
            }

            if (TryDirectRuntime(log, errors)) return true;

            error = string.Join("; ", errors);
            return false;
        }

        // ------------------------------------------------------------------ Khronos loader

        private static IEnumerable<string> LoaderNames()
        {
            if (OperatingSystem.IsWindows())
            {
                yield return "openxr_loader.dll";
            }
            else if (OperatingSystem.IsMacOS())
            {
                yield return "libopenxr_loader.dylib";
            }
            else
            {
                yield return "libopenxr_loader.so.1";
                yield return "libopenxr_loader.so";
            }
        }

        private static bool TryKhronosLoader(Action<string> log, List<string> errors)
        {
            var baseDir = AppContext.BaseDirectory;
            foreach (var name in LoaderNames())
            {
                var candidates = new[]
                {
                    Path.Combine(baseDir, name),
                    Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", name),
                    name // default OS search path
                };
                foreach (var candidate in candidates)
                {
                    if (candidate.Contains(Path.DirectorySeparatorChar) && !File.Exists(candidate)) continue;
                    if (!NativeLibrary.TryLoad(candidate, out var handle)) continue;
                    if (NativeLibrary.TryGetExport(handle, "xrGetInstanceProcAddr", out var gipa))
                    {
                        LibraryHandle = handle;
                        GetInstanceProcAddr = gipa;
                        Description = "Khronos loader (" + candidate + ")";
                        log?.Invoke("OpenXR: using " + Description);
                        return true;
                    }
                    NativeLibrary.Free(handle);
                }
            }
            errors.Add("no Khronos OpenXR loader found");
            return false;
        }

        // ------------------------------------------------------------------ direct runtime

        private static string ArchName()
        {
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X64: return "x86_64";
                case Architecture.X86: return "i686";
                case Architecture.Arm64: return "aarch64";
                case Architecture.Arm: return "armv7a-vfp";
                default: return RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            }
        }

        /// <summary>Candidate active runtime manifests, in the order the Khronos loader checks them.</summary>
        public static IEnumerable<string> ActiveRuntimeManifestCandidates()
        {
            var env = Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
            if (!string.IsNullOrEmpty(env)) yield return env;

            if (OperatingSystem.IsWindows())
            {
                string path = null;
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1"))
                        path = key?.GetValue("ActiveRuntime") as string;
                }
                catch { }
                if (!string.IsNullOrEmpty(path)) yield return path;
                yield break;
            }

            var arch = ArchName();
            var fileNames = new[] { $"active_runtime.{arch}.json", "active_runtime.json" };
            var dirs = new List<string>();

            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome))
            {
                var home = Environment.GetEnvironmentVariable("HOME");
                if (!string.IsNullOrEmpty(home)) configHome = Path.Combine(home, ".config");
            }
            if (!string.IsNullOrEmpty(configHome)) dirs.Add(configHome);

            var configDirs = Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS");
            if (string.IsNullOrEmpty(configDirs)) configDirs = "/etc/xdg";
            dirs.AddRange(configDirs.Split(':', StringSplitOptions.RemoveEmptyEntries));
            dirs.Add("/etc");
            dirs.Add("/usr/local/etc");

            foreach (var dir in dirs)
                foreach (var file in fileNames)
                    yield return Path.Combine(dir, "openxr", "1", file);
        }

        private static bool TryDirectRuntime(Action<string> log, List<string> errors)
        {
            string manifest = null;
            foreach (var candidate in ActiveRuntimeManifestCandidates())
            {
                if (File.Exists(candidate)) { manifest = candidate; break; }
            }
            if (manifest == null)
            {
                errors.Add("no active OpenXR runtime manifest (active_runtime.json) found - is SteamVR/Monado installed and set as the OpenXR runtime?");
                return false;
            }

            string libraryPath;
            string negotiateName = "xrNegotiateLoaderRuntimeInterface";
            try
            {
                using (var doc = JsonDocument.Parse(File.ReadAllText(manifest)))
                {
                    var runtime = doc.RootElement.GetProperty("runtime");
                    libraryPath = runtime.GetProperty("library_path").GetString();
                    if (runtime.TryGetProperty("functions", out var funcs) &&
                        funcs.TryGetProperty("xrNegotiateLoaderRuntimeInterface", out var custom))
                    {
                        negotiateName = custom.GetString();
                    }
                }
            }
            catch (Exception e)
            {
                errors.Add($"could not parse {manifest}: {e.Message}");
                return false;
            }

            // Relative paths are relative to the manifest. A bare file name uses the OS search path.
            string resolved = libraryPath;
            if (libraryPath.Contains('/') || libraryPath.Contains('\\'))
            {
                if (!Path.IsPathRooted(libraryPath))
                    resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest)), libraryPath));
            }

            if (!NativeLibrary.TryLoad(resolved, out var handle))
            {
                errors.Add($"could not load runtime library '{resolved}' from {manifest}" +
                    (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? " (an arm64 runtime is required for the arm64 build)" : ""));
                return false;
            }

            if (NativeLibrary.TryGetExport(handle, negotiateName, out var negotiate))
            {
                var info = new XrNegotiateLoaderInfo
                {
                    StructType = LoaderInterfaceStructLoaderInfo,
                    StructVersion = LoaderInfoStructVersion,
                    StructSize = (UIntPtr)sizeof(XrNegotiateLoaderInfo),
                    MinInterfaceVersion = 1,
                    MaxInterfaceVersion = CurrentLoaderRuntimeVersion,
                    MinApiVersion = XrConst.MakeVersion(1, 0, 0),
                    MaxApiVersion = XrConst.MakeVersion(1, 0x3ff, 0xfff),
                };
                var request = new XrNegotiateRuntimeRequest
                {
                    StructType = LoaderInterfaceStructRuntimeRequest,
                    StructVersion = RuntimeInfoStructVersion,
                    StructSize = (UIntPtr)sizeof(XrNegotiateRuntimeRequest),
                };
                var fn = (delegate* unmanaged<XrNegotiateLoaderInfo*, XrNegotiateRuntimeRequest*, XrResult>)negotiate;
                var result = fn(&info, &request);
                if (result != XrResult.Success || request.GetInstanceProcAddr == IntPtr.Zero)
                {
                    errors.Add($"runtime negotiation with {resolved} failed ({result})");
                    NativeLibrary.Free(handle);
                    return false;
                }
                LibraryHandle = handle;
                GetInstanceProcAddr = request.GetInstanceProcAddr;
                Description = $"direct runtime {resolved} (interface v{request.RuntimeInterfaceVersion}, api {XrConst.VersionString(request.RuntimeApiVersion)})";
                log?.Invoke("OpenXR: using " + Description);
                return true;
            }

            // Very old runtimes export xrGetInstanceProcAddr without the negotiation entry point.
            if (NativeLibrary.TryGetExport(handle, "xrGetInstanceProcAddr", out var gipa))
            {
                LibraryHandle = handle;
                GetInstanceProcAddr = gipa;
                Description = $"direct runtime {resolved} (legacy export)";
                log?.Invoke("OpenXR: using " + Description);
                return true;
            }

            errors.Add($"{resolved} does not export {negotiateName}");
            NativeLibrary.Free(handle);
            return false;
        }
    }
}
