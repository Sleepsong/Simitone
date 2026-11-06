using Android.App;
using Android.Runtime;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Simitone.Android
{
    /// <summary>
    /// Starts before anything else in the app: opens the crash log in Android/media/com.sleepsong.simitone (readable
    /// with the Files app, no permissions needed) and catches every kind of unhandled error into it.
    /// </summary>
    [Application]
    public class SimitoneApp : Application
    {
        public SimitoneApp(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

        public override void OnCreate()
        {
            base.OnCreate();
            try
            {
#pragma warning disable CA1422 // (Android/media: the one app folder the Files app can open on Android 11+)
                var media = GetExternalMediaDirs();
#pragma warning restore CA1422
                var dir = (media != null && media.Length > 0 && media[0] != null) ? media[0].AbsolutePath : GetExternalFilesDir(null)?.AbsolutePath;
                if (dir != null)
                {
                    Directory.CreateDirectory(dir);
                    SimitoneActivity.MediaLogPath = Path.Combine(dir, "simitone.log");
                    File.WriteAllText(SimitoneActivity.MediaLogPath, "");
                }
            }
            catch (Exception) { }
            AppDomain.CurrentDomain.UnhandledException += (s, e) => SimitoneActivity.Log("FATAL: " + e.ExceptionObject);
            AndroidEnvironment.UnhandledExceptionRaiser += (s, e) => SimitoneActivity.Log("FATAL (Android): " + e.Exception);
            TaskScheduler.UnobservedTaskException += (s, e) => SimitoneActivity.Log("unobserved task error: " + e.Exception);
            Java.Lang.Thread.DefaultUncaughtExceptionHandler = new JavaCrashLogger(Java.Lang.Thread.DefaultUncaughtExceptionHandler);
            SimitoneActivity.Log("app starting");
        }

        private class JavaCrashLogger : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
        {
            private readonly Java.Lang.Thread.IUncaughtExceptionHandler Previous;
            public JavaCrashLogger(Java.Lang.Thread.IUncaughtExceptionHandler previous) { Previous = previous; }
            public void UncaughtException(Java.Lang.Thread t, Java.Lang.Throwable e)
            {
                SimitoneActivity.Log("FATAL (Java, thread " + t?.Name + "): " + e);
                Previous?.UncaughtException(t, e);
            }
        }
    }
}
