using System;
using System.IO;
using FSO.Common;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// Appends fatal errors to crash.log in the user directory (next to simantics-errors.log), so a crash can be
    /// reported even when the game was started without a console, e.g. from Steam.
    /// </summary>
    public static class CrashLog
    {
        public static string Path => System.IO.Path.Combine(FSOEnvironment.UserDir ?? "", "crash.log");

        public static void Write(object exception)
        {
            try
            {
                var header = FSO.SimAntics.Engine.VMExceptionLog.SessionHeader;
                File.AppendAllText(Path,
                    $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {header} =====" + Environment.NewLine +
                    exception + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                //nowhere better to report it
            }
        }
    }
}
