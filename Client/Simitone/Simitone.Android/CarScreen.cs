using Android.App;
using Android.OS;
using Android.Util;
using Android.Views;
using FSO.Client;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Simitone.Client;
using Simitone.Client.UI.Screens;
using System;
using System.Diagnostics;
using System.Threading;

namespace Simitone.Android
{
    /// <summary>
    /// Android Auto, while parked. On phones with Android 15 or later, Android Auto lists games in the car's own
    /// launcher (SimitoneActivity has the CAR_LAUNCHER category, and AndroidManifest.xml says appCategory="game") and
    /// runs them on the car's screen, which is a display of its own, only while the car is parked: when the car starts
    /// moving, Android Auto closes the game. The same activity runs there, and it can move between the phone's screen
    /// and the car's while the game is running.
    ///
    /// This works out which screen Simitone is on and the UI scale for it, and readies the game to be closed: when
    /// Simitone is paused on the car's screen, time stops and the household is saved, so it carries on from there.
    /// Another external screen (a monitor, a desktop mode) is treated the same way.
    /// </summary>
    public static class CarScreen
    {
        /// <summary>The intent category that puts an activity in Android Auto's launcher (Intent.CATEGORY_CAR_LAUNCHER).</summary>
        public const string LauncherCategory = "android.intent.category.CAR_LAUNCHER";

        /// <summary>The phone's own screen (Display.DEFAULT_DISPLAY).</summary>
        public const int PhoneDisplay = Display.DefaultDisplay;

        /// <summary>How long OnPause waits for the game to save (a lot save takes about a second on a phone).</summary>
        private const int SaveTimeoutMs = 4000;

        private static Display DisplayOf(Activity activity)
        {
#pragma warning disable CA1422 // (an activity's WindowManager.DefaultDisplay is the display it's on: the way below Android 11)
            return (Build.VERSION.SdkInt >= BuildVersionCodes.R) ? activity.Display : activity.WindowManager?.DefaultDisplay;
#pragma warning restore CA1422
        }

        /// <summary>
        /// The id of the display the activity is on: <see cref="PhoneDisplay"/>, or another one, which with Android
        /// Auto is the car's screen. (Android's guide also checks that Android Auto is connected, with the car app
        /// library; Simitone treats any other screen the same way, so the display is enough.)
        /// </summary>
        public static int DisplayId(Activity activity)
        {
            try { return DisplayOf(activity)?.DisplayId ?? PhoneDisplay; }
            catch (Exception) { return PhoneDisplay; }
        }

        /// <summary>The size in pixels and the density of the screen the activity is on.</summary>
        public static DisplayMetrics Metrics(Activity activity)
        {
            var metrics = new DisplayMetrics();
#pragma warning disable CA1422
            DisplayOf(activity).GetRealMetrics(metrics);
#pragma warning restore CA1422
            return metrics;
        }

        /// <summary>
        /// The UI scale for a screen: the UI is drawn at least ~560 px tall, up to 4x. An 800x480 car screen gets 1x, a
        /// 1080p one about 1.9x, and phones about 2x.
        /// </summary>
        public static float UIScale(DisplayMetrics metrics)
        {
            var shortSide = Math.Min(metrics.WidthPixels, metrics.HeightPixels);
            return Math.Max(1f, Math.Min(4f, shortSide / 560f));
        }

        /// <summary>
        /// The game is on a new screen: it started on the car's, or moved there or back while running (when it's
        /// opened from the car's launcher while already running on the phone, or the other way round). The UI is
        /// scaled for the screen once the game gets to it; the surface's new size arrives on its own, through
        /// MonoGame's window resize.
        /// </summary>
        public static void MovedTo(SimitoneGame game, bool onCar, float scale)
        {
            GameThread.NextUpdate(_ =>
            {
                try
                {
                    // the car's screen doesn't turn with the phone: landscape only there, so turning the phone in its
                    // holder can't ask for the game upside down on the car's screen
                    var graphics = GameFacade.GraphicsDeviceManager;
                    var orientations = onCar ? DisplayOrientation.LandscapeLeft : DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
                    if (graphics != null && graphics.SupportedOrientations != orientations)
                    {
                        graphics.SupportedOrientations = orientations;
                        graphics.ApplyChanges();
                    }
                    game.SetUIScale(scale);
                    SimitoneActivity.Log($"car: now on the {(onCar ? "car's" : "phone's")} screen, UI scale {scale:0.00}");
                }
                catch (Exception e)
                {
                    SimitoneActivity.Log("car: couldn't lay the game out for the new screen: " + e);
                }
            });
        }

        /// <summary>
        /// Simitone is being paused on the car's screen: the car may be about to move, and Android Auto then closes
        /// it. Stops time on the lot and saves the household, as the Save button does, so nothing is lost and it
        /// carries on from there when it's opened again. Call it from OnPause before base.OnPause, while MonoGame's
        /// game loop still runs (it stops it there); it waits up to a few seconds for the game.
        /// </summary>
        public static void PauseAndSave(string why)
        {
            var job = new SaveJob();
            Action work = () =>
            {
                if (job.Cancelled) return; //(OnPause stopped waiting: don't save later, when Simitone is back)
                try { PauseAndSaveNow(why); }
                catch (Exception e) { SimitoneActivity.Log($"car ({why}): couldn't save: {e}"); }
                finally { job.Done.Set(); }
            };
            // MonoGame may run the game's frames on this (the UI) thread; then the game is between two frames now
            if (GameThread.IsInGameThread()) work();
            else
            {
                GameThread.NextUpdate(_ => work());
                if (!job.Done.Wait(SaveTimeoutMs))
                {
                    job.Cancelled = true;
                    SimitoneActivity.Log($"car ({why}): the game didn't get to it within {SaveTimeoutMs / 1000} s: not saved");
                }
            }
        }

        private class SaveJob
        {
            public volatile bool Cancelled;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private static void PauseAndSaveNow(string why)
        {
            var screen = GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;
            var vm = screen?.vm;
            if (vm == null)
            {
                SimitoneActivity.Log($"car ({why}): not on a lot, nothing to save");
                return;
            }
            if (screen.PauseQuietly()) SimitoneActivity.Log($"car ({why}): paused time");
            if (!vm.Ready)
            {
                SimitoneActivity.Log($"car ({why}): the lot is still loading: not saved");
                return;
            }
            if (screen.Downtown)
            {
                SimitoneActivity.Log($"car ({why}): visiting another lot (downtown, on vacation...): not saved, as in The Sims");
                return;
            }
            var clock = Stopwatch.StartNew();
            screen.Save();
            SimitoneActivity.Log($"car ({why}): saved the household ({clock.ElapsedMilliseconds} ms)");
        }

        /// <summary>Simitone left the car's screen while running: time stops (the household was saved when it was paused there).</summary>
        public static void Pause(string why)
        {
            GameThread.NextUpdate(_ =>
            {
                if ((GameFacade.Screens?.CurrentUIScreen as TS1GameScreen)?.PauseQuietly() == true)
                    SimitoneActivity.Log($"car ({why}): paused time");
            });
        }
    }
}
