using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Simitone.Client.VR;

// 3D sound (VRAudio): HRTF switched on through OpenAL Soft on MonoGame's own device, then a noise burst placed around
// the listener as the game's sounds are (MonoGame's Apply3D, emitters inside OpenAL's reference distance). OpenAL Soft
// writes what it plays to a WAV file (its "wave" output: run.sh sets ALSOFT_DRIVERS and ALSOFT_CONF, and UICHECK_WAV
// to the file), which is then measured: which ear is louder, front and behind sounding different (HRTF's spectral
// cues), and the game's volume kept.
static class SoundCheck
{
    static int Failures;
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + "sound: " + name + (detail != null ? " (" + detail + ")" : ""));
        if (!ok) Failures++;
    }

    public static int Run()
    {
        var wav = Environment.GetEnvironmentVariable("UICHECK_WAV");
        if (string.IsNullOrEmpty(wav))
        {
            Console.WriteLine("     sound: skipped (run through run.sh, which sends OpenAL's output to a file)");
            return 0;
        }
        string log = null;
        var on = VRAudio.SetHrtf(true, s => log = s);
        Check("HRTF switches on, on MonoGame's OpenAL device", on && VRAudio.HrtfStatus == "On", log);
        if (!on) return Failures;

        // a mono noise burst (broadband, so the ears' filtering shows), placed as VRAudio places sounds
        var rng = new Random(1);
        int n = 44100 * 4 / 10;
        var pcm = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            var v = (short)((rng.NextDouble() * 2 - 1) * 12000 * Math.Min(1, Math.Min(i, n - i) / 441.0));
            pcm[2 * i] = (byte)v;
            pcm[2 * i + 1] = (byte)(v >> 8);
        }
        using var sfx = new SoundEffect(pcm, 44100, AudioChannels.Mono);
        SoundEffect.DistanceScale = 100; // (as FreeSO's HITSound.Set3D sets it)
        // the listener looks along -Z; one burst left, right, in front, behind, then in front nearer and much further
        var listener = new AudioListener { Position = new Vector3(10, 5, 20), Forward = -Vector3.UnitZ, Up = Vector3.UnitY };
        var places = new[] { new Vector3(-50, 0, 0), new Vector3(50, 0, 0), new Vector3(0, 0, -50), new Vector3(0, 0, 50),
            new Vector3(0, 0, -25), new Vector3(0, 0, -400) };
        foreach (var place in places)
        {
            var instance = sfx.CreateInstance();
            var emitter = new AudioEmitter { Position = listener.Position + place };
            instance.Volume = 1;
            instance.Play();
            instance.Apply3D(listener, emitter); // (after Play: MonoGame gives it an OpenAL source then; the game re-applies every tick)
            Thread.Sleep(450);
            instance.Stop();
            instance.Dispose();
            Thread.Sleep(300);
        }
        // close the device so the file is complete
        typeof(SoundEffect).Assembly.GetType("Microsoft.Xna.Framework.Audio.OpenALSoundController")?.GetMethod("DestroyInstance")?.Invoke(null, null);

        var (left, right) = ReadWav(wav);
        var bursts = Bursts(left, right, 441);
        Check("six bursts recorded", bursts.Count == 6, $"{bursts.Count} found in {left.Length / 44100.0:0.0} s");
        if (bursts.Count != 6) return Failures;
        var m = new List<(double l, double r, double hfL, double hfR)>();
        foreach (var (a, b) in bursts) m.Add((Rms(left, a, b), Rms(right, a, b), Bright(left, a, b), Bright(right, a, b)));
        Check("on your left: louder and brighter in the left ear", m[0].l > 3 * m[0].r && m[0].hfL > 2 * m[0].hfR,
            $"left {m[0].l:0.000} / right {m[0].r:0.000}");
        Check("on your right: louder and brighter in the right ear", m[1].r > 3 * m[1].l && m[1].hfR > 2 * m[1].hfL,
            $"left {m[1].l:0.000} / right {m[1].r:0.000}");
        Check("behind you sounds different from in front (duller): HRTF", m[3].hfL < 0.8 * m[2].hfL && Math.Abs(m[2].l - m[2].r) < 0.01 * m[2].l,
            $"brightness {m[2].hfL:0.00} in front, {m[3].hfL:0.00} behind");
        Check("inside the reference distance the game's volume is kept", Math.Abs(m[4].l - m[2].l) < 0.02 * m[2].l,
            $"{m[4].l:0.0000} at 25, {m[2].l:0.0000} at 50 (VRAudio places sounds at 50)");
        Check("...and beyond it OpenAL would make it quieter", m[5].l > 0.15 * m[2].l && m[5].l < 0.35 * m[2].l,
            $"{m[5].l / m[2].l:0.00} of it at 400");
        return Failures;
    }

    /// <summary>The two channels of a WAV file (16-bit PCM or 32-bit float, as OpenAL Soft writes them).</summary>
    static (float[] left, float[] right) ReadWav(string path)
    {
        var d = File.ReadAllBytes(path);
        int i = 12, format = 1, channels = 2, bits = 16;
        byte[] data = null;
        while (i + 8 <= d.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(d, i, 4);
            var size = BitConverter.ToInt32(d, i + 4);
            if (id == "fmt ")
            {
                format = BitConverter.ToUInt16(d, i + 8);
                channels = BitConverter.ToUInt16(d, i + 10);
                bits = BitConverter.ToUInt16(d, i + 22);
                if (format == 0xFFFE) format = BitConverter.ToUInt16(d, i + 32); // (extensible: the subformat's first two bytes)
            }
            if (id == "data")
            {
                // (the size may not be filled in if the file wasn't closed: take the rest)
                data = new byte[d.Length - (i + 8)];
                Array.Copy(d, i + 8, data, 0, data.Length);
                break;
            }
            i += 8 + size;
        }
        if (data == null) return (new float[0], new float[0]);
        var bytes = bits / 8;
        var frames = data.Length / (bytes * channels);
        var l = new float[frames];
        var r = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float Sample(int c)
            {
                var o = (f * channels + c) * bytes;
                return format == 3 ? BitConverter.ToSingle(data, o) : BitConverter.ToInt16(data, o) / 32768f;
            }
            l[f] = Sample(0);
            r[f] = channels > 1 ? Sample(1) : l[f];
        }
        return (l, r);
    }

    /// <summary>Stretches of sound (over 150 ms) between silences, found in 10 ms windows.</summary>
    static List<(int, int)> Bursts(float[] l, float[] r, int window)
    {
        var energy = new List<double>();
        for (int j = 0; j + window <= l.Length; j += window)
        {
            double e = 0;
            for (int k = j; k < j + window; k++) e += l[k] * l[k] + r[k] * r[k];
            energy.Add(e);
        }
        var max = 0.0;
        foreach (var e in energy) max = Math.Max(max, e);
        var runs = new List<(int, int)>();
        int start = -1;
        for (int j = 0; j <= energy.Count; j++)
        {
            var loud = j < energy.Count && energy[j] > max * 0.005;
            if (loud && start < 0) start = j;
            if (!loud && start >= 0)
            {
                if (j - start > 15) runs.Add((start * window, j * window));
                start = -1;
            }
        }
        return runs;
    }

    static double Rms(float[] x, int a, int b)
    {
        double s = 0;
        for (int k = a; k < b; k++) s += x[k] * x[k];
        return Math.Sqrt(s / Math.Max(1, b - a));
    }

    /// <summary>How much of a signal is high frequencies: the energy of its differences over its own energy.</summary>
    static double Bright(float[] x, int a, int b)
    {
        double s = 0, dsum = 0;
        for (int k = a + 1; k < b; k++) { s += x[k] * x[k]; var dd = x[k] - x[k - 1]; dsum += dd * dd; }
        return dsum / Math.Max(1e-12, s);
    }
}
