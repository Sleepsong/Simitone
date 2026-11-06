using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using FSO.HIT;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace Simitone.Client.VR
{
    /// <summary>
    /// 3D sound in first person. The game decides how loud each sound is (distance, floor, zoom: its own model, with
    /// you as the listener) and normally pans it left or right. Here each sound that belongs to a Sim or an object
    /// is instead placed in 3D, in the direction it really is from your head (above, below, behind...), through
    /// FreeSO's own 3D sound path (an emitter per sound, applied with MonoGame's <c>Apply3D</c>). The emitter sits
    /// inside OpenAL's reference distance, so OpenAL adds direction only and the game's volume stays as it was.
    ///
    /// With headphones (a headset), OpenAL Soft's HRTF turns that direction into binaural sound
    /// (<see cref="SetHrtf"/>): you can tell front from back and up from down, not just left from right. Mono sounds
    /// are placed (most of the game's); stereo ones play as they are.
    ///
    /// MonoGame doesn't expose its OpenAL device, so it's found by reflection (<c>OpenALSoundController._device</c>,
    /// <c>MonoGame.OpenAL.AL.NativeLibrary</c>); if anything is missing, sound stays as the game plays it.
    /// </summary>
    public sealed class VRAudio
    {
        /// <summary>How HRTF stands, for the VR menu ("On", "Off", or why not).</summary>
        public static string HrtfStatus { get; private set; } = "Off";

        private static readonly FieldInfo SoundsField = typeof(HITVM).GetField("Sounds", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo EmitterField = typeof(HITSound).GetField("Emitter3D", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly HashSet<HITSound> Placed = new HashSet<HITSound>();
        private readonly List<HITSound> Gone = new List<HITSound>();

        /// <summary>How far in front of you a sound is placed (world units): inside OpenAL's reference distance.</summary>
        private const float EmitterDistance = 50f;
        /// <summary>Where on a Sim or object its sound comes from, above its floor (world units: about 0.7 m).</summary>
        private const float SourceHeight = 2.1f;

        /// <summary>
        /// Places this frame's sounds around your head (<paramref name="active"/>), or gives them back to the game's
        /// panning. Call once per update, on the game thread.
        /// </summary>
        public void Update(VM vm, VRCameraController camera, bool active)
        {
            var hit = HITVM.Get();
            if (hit == null || SoundsField == null || EmitterField == null) return;
            if (!(SoundsField.GetValue(hit) is List<HITSound> sounds)) return;
            var head = SimitoneVR.HeadPoseTracking();
            if (!active || vm == null || camera == null || !head.Valid)
            {
                ReleaseAll();
                return;
            }

            // the listener: your head, in the lot's 3D units
            var listener = hit.Listener;
            var position = camera.HeadWorld;
            var forward = Vector3.Normalize(camera.TrackingDirectionToWorld(Vector3.Transform(-Vector3.UnitZ, head.Orientation)));
            var up = Vector3.Normalize(camera.TrackingDirectionToWorld(Vector3.Transform(Vector3.UnitY, head.Orientation)));
            listener.Position = position;
            listener.Forward = forward;
            listener.Up = up;

            foreach (var sound in sounds)
            {
                var owner = sound.LastMainOwner;
                var entity = owner > 0 && owner <= short.MaxValue ? vm.GetObjectById((short)owner) : null;
                if (entity == null || entity.Dead || !(sound is HITThread))
                {
                    if (Placed.Remove(sound)) EmitterField.SetValue(sound, null);
                    continue;
                }
                // (as the game measures it: tiles and floors to the lot's 3D units)
                var vp = entity.VisualPosition * 3f;
                var source = new Vector3(vp.X, vp.Z + SourceHeight, vp.Y);
                var direction = source - position;
                direction = direction.LengthSquared() > 1e-4f ? Vector3.Normalize(direction) : forward;
                sound.Set3D(position + direction * EmitterDistance);
                Placed.Add(sound);
            }

            // sounds that finished
            Gone.Clear();
            foreach (var sound in Placed) if (sound.Dead || !sounds.Contains(sound)) Gone.Add(sound);
            foreach (var sound in Gone) Placed.Remove(sound);
        }

        /// <summary>Gives every placed sound back to the game's left/right panning.</summary>
        public void ReleaseAll()
        {
            if (Placed.Count == 0 || EmitterField == null) return;
            foreach (var sound in Placed) EmitterField.SetValue(sound, null);
            Placed.Clear();
        }

        // ------------------------------------------------------------------ HRTF (OpenAL Soft's ALC_SOFT_HRTF)

        private const int ALC_HRTF_SOFT = 0x1992, ALC_HRTF_STATUS_SOFT = 0x1993, ALC_HRTF_SPECIFIER_SOFT = 0x1995;
        private const int ALC_TRUE = 1, ALC_FALSE = 0, ALC_DONT_CARE_SOFT = 0x0002;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AlcGetProcAddress(IntPtr device, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte AlcIsExtensionPresent(IntPtr device, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte AlcResetDeviceSoft(IntPtr device, int[] attributes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AlcGetIntegerv(IntPtr device, int param, int size, int[] values);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AlcGetString(IntPtr device, int param);

        /// <summary>
        /// Turns HRTF on or off on the game's OpenAL device (off lets OpenAL Soft decide, as before). Returns whether
        /// it ended up as asked; <see cref="HrtfStatus"/> says how it stands.
        /// </summary>
        public static bool SetHrtf(bool on, Action<string> log)
        {
            try
            {
                var asm = typeof(SoundEffect).Assembly;
                var controllerType = asm.GetType("Microsoft.Xna.Framework.Audio.OpenALSoundController");
                // (made when the first sound is loaded; make sure it exists)
                controllerType?.GetMethod("EnsureInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
                var controller = controllerType?.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
                var device = controller != null ? (IntPtr)(controllerType.GetField("_device", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(controller) ?? IntPtr.Zero) : IntPtr.Zero;
                var library = (IntPtr)(asm.GetType("MonoGame.OpenAL.AL")?.GetField("NativeLibrary", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) ?? IntPtr.Zero);
                if (device == IntPtr.Zero || library == IntPtr.Zero)
                    return Status(false, "No OpenAL device", log);

                T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
                var isExtension = Export<AlcIsExtensionPresent>("alcIsExtensionPresent");
                if (isExtension(device, "ALC_SOFT_HRTF") == 0) return Status(false, "Not supported by OpenAL", log);
                var getProc = Export<AlcGetProcAddress>("alcGetProcAddress");
                var resetPtr = getProc(device, "alcResetDeviceSOFT");
                if (resetPtr == IntPtr.Zero) return Status(false, "Not supported by OpenAL", log);
                var reset = Marshal.GetDelegateForFunctionPointer<AlcResetDeviceSoft>(resetPtr);
                if (reset(device, new[] { ALC_HRTF_SOFT, on ? ALC_TRUE : ALC_DONT_CARE_SOFT, 0 }) == 0)
                    return Status(false, "OpenAL refused", log);

                var status = new int[1];
                Export<AlcGetIntegerv>("alcGetIntegerv")(device, ALC_HRTF_STATUS_SOFT, 1, status);
                // enabled, required (by alsoft.conf) or headphones detected: on
                var enabled = status[0] == 1 || status[0] == 3 || status[0] == 4;
                var name = enabled ? Marshal.PtrToStringAnsi(Export<AlcGetString>("alcGetString")(device, ALC_HRTF_SPECIFIER_SOFT)) : null;
                var text = enabled ? "On" : on ? StatusName(status[0]) : "Off";
                log?.Invoke($"HRTF {(on ? "requested" : "released")}: status {status[0]} ({text}){(name != null ? ", " + name : "")}");
                return Status(enabled == on, text, null);
            }
            catch (Exception e)
            {
                return Status(false, "Unavailable", log, e.Message);
            }
        }

        private static string StatusName(int status) => status switch
        {
            2 => "Denied (alsoft.conf)", 5 => "Unsupported output (not stereo)",
            _ => "Off"
        };

        private static bool Status(bool ok, string text, Action<string> log, string detail = null)
        {
            HrtfStatus = text;
            if (log != null) log("HRTF: " + text + (detail != null ? " (" + detail + ")" : ""));
            return ok;
        }
    }
}
