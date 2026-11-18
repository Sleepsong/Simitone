using System;

namespace Simitone.VR
{
    /// <summary>
    /// Stereo sounds mixed down to mono, so they can be placed in 3D: OpenAL only places mono sounds, and plays stereo
    /// ones as they are, wherever their source is.
    /// </summary>
    public static class VRSoundMix
    {
        /// <summary>
        /// A stereo PCM WAV file (8 or 16 bits) as mono 16-bit samples (each the average of its left and right), with its
        /// sample rate. Null for anything else (a mono file, which can be placed as it is, or one this can't read).
        /// </summary>
        public static byte[] MonoFromStereoWav(byte[] wav, out int sampleRate)
        {
            sampleRate = 0;
            if (wav == null || wav.Length < 44 || Text(wav, 0) != "RIFF" || Text(wav, 8) != "WAVE") return null;
            int format = 0, channels = 0, bits = 0, rate = 0;
            int dataStart = -1, dataLength = 0;
            var pos = 12;
            while (pos + 8 <= wav.Length)
            {
                var id = Text(wav, pos);
                var length = BitConverter.ToInt32(wav, pos + 4);
                var body = pos + 8;
                if (length < 0 || body > wav.Length) return null;
                if (id == "fmt " && length >= 16 && body + 16 <= wav.Length)
                {
                    format = BitConverter.ToUInt16(wav, body);
                    channels = BitConverter.ToUInt16(wav, body + 2);
                    rate = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToUInt16(wav, body + 14);
                }
                else if (id == "data")
                {
                    dataStart = body;
                    dataLength = Math.Min(length, wav.Length - body); //(some files say more than they have)
                    break;
                }
                pos = body + length + (length & 1); //(chunks are padded to an even length)
            }
            if (format != 1 || channels != 2 || (bits != 8 && bits != 16) || rate <= 0 || dataStart < 0) return null;

            var frameBytes = bits / 8 * 2;
            var frames = dataLength / frameBytes;
            var mono = new byte[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                var at = dataStart + i * frameBytes;
                int left, right;
                if (bits == 16)
                {
                    left = BitConverter.ToInt16(wav, at);
                    right = BitConverter.ToInt16(wav, at + 2);
                }
                else
                {
                    // (8-bit samples are unsigned, 128 being silence)
                    left = (wav[at] - 128) << 8;
                    right = (wav[at + 1] - 128) << 8;
                }
                var sample = (short)((left + right) / 2);
                mono[i * 2] = (byte)(sample & 0xFF);
                mono[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            sampleRate = rate;
            return mono;
        }

        private static string Text(byte[] data, int at) =>
            at + 4 <= data.Length ? new string(new[] { (char)data[at], (char)data[at + 1], (char)data[at + 2], (char)data[at + 3] }) : "";
    }
}
