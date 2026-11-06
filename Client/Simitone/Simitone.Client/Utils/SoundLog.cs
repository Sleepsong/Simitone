using System;
using System.IO;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// -soundlog: sound.log in the user folder, a line for every sound the game asks for and what came of it (who asked,
    /// where they are, whether it was skipped or handed an earlier copy, and the volume it starts at). For sounds that go
    /// missing without leaving a note in simantics-errors.log.
    /// </summary>
    public static class SoundLog
    {
        private static StreamWriter Writer;
        private static readonly object Lock = new object();

        public static void Start(string path, string header)
        {
            try
            {
                Writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                Writer.WriteLine();
                Writer.WriteLine("===== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + header + " =====");
                FSO.Content.TS1.TS1Audio.Trace = Write;
            }
            catch
            {
                Writer = null; //no log rather than no game
            }
        }

        private static void Write(string line)
        {
            lock (Lock)
            {
                try
                {
                    Writer?.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.f") + "] " + line);
                }
                catch
                {
                }
            }
        }
    }
}
