using System;
using System.Collections.Generic;
using FSO.Client.UI.Model;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The first-run comfort setup: a few pages, each one question with big choices (the current setting is
    /// highlighted, and choosing applies it at once): what happens when your Sim walks off, turning, moving, standing
    /// or seated, and the vignette. Shown the first time VR starts (until <see cref="VRSettings.ComfortSetupDone"/>),
    /// and from the VR menu's "Comfort setup". Point with the right controller; the trigger chooses, the grip goes back.
    /// </summary>
    public class VRSetup : VRPanel
    {
        private const int Width = 720, Height = 640, Margin = 24;
        private const int ChoiceTop = 150, ChoiceHeight = 84, ChoiceGap = 10;
        private const int NavTop = Height - Margin - 56, NavWidth = 200;

        private class Choice
        {
            public string Name;
            public string Help;
            public Action Apply;
            public Func<bool> IsCurrent;
        }

        private class Page
        {
            public string Title;
            public string Intro;
            public readonly List<Choice> Choices = new List<Choice>();
        }

        private readonly List<Page> Pages = new List<Page>();
        private int PageIndex;
        public bool Open { get; private set; }
        private int Hover = NoHover;   // a choice index, or BackButton / NextButton
        private bool Armed;
        private bool Dirty = true;

        private const int NoHover = -1, BackButton = -2, NextButton = -3;
        private static VRSettings Settings => SimitoneVR.Settings;

        public VRSetup()
        {
            WidthMetres = 0.66f;

            Pages.Add(new Page
            {
                Title = "WELCOME TO SIMITONE VR",
                Intro = "A FEW QUICK CHOICES TO KEEP YOU COMFORTABLE. YOU CAN CHANGE THEM ANY TIME IN THE VR MENU: THE LEFT "
                    + "MENU BUTTON, OR POINT AT YOUR LEFT WRIST AND PULL THE TRIGGER. POINT WITH THE RIGHT CONTROLLER AND "
                    + "PULL THE TRIGGER TO CHOOSE."
            });

            var tiers = Add("WHEN YOUR SIM WALKS OFF ON THEIR OWN", "TO A FRIDGE, A CHAIR, A FRIEND...");
            Option(tiers, "COMFORTABLE (BEST TO START)", "STEP OUT, WATCH, AND FOLLOW THEM IN QUICK BLINKS. THE VIEW NEVER SLIDES.",
                () => Settings.SimActionView = VRSimActionView.Comfortable, () => Settings.SimActionView == VRSimActionView.Comfortable);
            Option(tiers, "MODERATE", "STEP OUT AND GLIDE AFTER THEM AT A STEADY WALKING PACE.",
                () => Settings.SimActionView = VRSimActionView.Moderate, () => Settings.SimActionView == VRSimActionView.Moderate);
            Option(tiers, "INTENSE", "STAY IN THEIR HEAD WHILE THEY WALK YOU ABOUT. FOR PEOPLE USED TO VR.",
                () => Settings.SimActionView = VRSimActionView.Intense, () => Settings.SimActionView == VRSimActionView.Intense);

            var turning = Add("TURNING", "WITH THE RIGHT STICK.");
            Option(turning, "SNAP 30 (BEST TO START)", "TURN IN QUICK STEPS.", () => SetSnap(30), () => IsSnap(30));
            Option(turning, "SNAP 45", "BIGGER STEPS.", () => SetSnap(45), () => IsSnap(45));
            Option(turning, "SMOOTH", "TURN CONTINUOUSLY. THIS CAN MAKE SOME PEOPLE FEEL SICK.",
                () => { Settings.TurnMode = VRTurnMode.Smooth; Settings.SmoothTurnDegreesPerSecond = 120; },
                () => Settings.TurnMode == VRTurnMode.Smooth);

            var moving = Add("MOVING", "PLAYING AS YOUR SIM, THEY WALK; AS AN OBSERVER, YOU DO.");
            Option(moving, "STICK AND ARC", "WALK WITH THE LEFT STICK, OR HOLD THE LEFT TRIGGER, AIM THE ARC AND LET GO TO GO THERE.",
                () => Settings.Movement = VRMovement.StickAndArc, () => Settings.Movement == VRMovement.StickAndArc);
            Option(moving, "ARC ONLY (MOST COMFORTABLE)", "ONLY THE ARC: YOUR SIM WALKS THERE AND YOU FOLLOW. NO SLIDING AT ALL.",
                () => Settings.Movement = VRMovement.Arc, () => Settings.Movement == VRMovement.Arc);
            Option(moving, "STICK ONLY", "ONLY THE LEFT STICK.",
                () => Settings.Movement = VRMovement.Stick, () => Settings.Movement == VRMovement.Stick);

            var posture = Add("HOW ARE YOU PLAYING?", "IN YOUR SIM'S HEAD YOU SEE FROM THEIR HEIGHT EITHER WAY.");
            Option(posture, "STANDING", "YOUR OWN HEIGHT WHEN YOU STEP OUT OR ROAM AS AN OBSERVER.",
                () => Settings.HeightOffset = 0, () => Settings.HeightOffset < 0.01f);
            Option(posture, "SEATED", "RAISES YOUR VIEW BY 45 CM WHEN YOU'RE NOT IN YOUR SIM'S HEAD.",
                () => Settings.HeightOffset = 0.45f, () => Settings.HeightOffset >= 0.01f);

            var vignette = Add("VIGNETTE", "DARKENS THE EDGE OF YOUR VIEW WHILE IT MOVES WITHOUT YOU MOVING YOUR HEAD.");
            Option(vignette, "OFF", "NEVER.", () => SetVignette(VRVignetteStrength.Off, VRVignetteStrength.Off),
                () => Settings.Vignette == VRVignetteStrength.Off);
            Option(vignette, "LOW", "A LITTLE, WHILE YOUR SIM CARRIES YOU.", () => SetVignette(VRVignetteStrength.Low, VRVignetteStrength.Off),
                () => Settings.Vignette == VRVignetteStrength.Low);
            Option(vignette, "MEDIUM (BEST TO START)", "WHILE YOUR SIM CARRIES YOU, AND A LITTLE WHILE YOU WALK THEM.",
                () => SetVignette(VRVignetteStrength.Medium, VRVignetteStrength.Low), () => Settings.Vignette == VRVignetteStrength.Medium);
            Option(vignette, "HIGH", "STRONGLY, ALSO WHILE YOU WALK THEM.", () => SetVignette(VRVignetteStrength.High, VRVignetteStrength.Medium),
                () => Settings.Vignette == VRVignetteStrength.High);

            Pages.Add(new Page
            {
                Title = "ALL SET",
                Intro = "HAVE FUN! IF ANYTHING FEELS UNCOMFORTABLE, TAKE A BREAK AND TRY THE COMFORTABLE OPTIONS. THE VR MENU HAS "
                    + "ALL OF THESE AND MORE, AND THE GAME'S OPTIONS PANEL HAS A VR BUTTON TOO."
            });
        }

        private Page Add(string title, string intro)
        {
            var page = new Page { Title = title, Intro = intro };
            Pages.Add(page);
            return page;
        }

        private static void Option(Page page, string name, string help, Action apply, Func<bool> isCurrent)
            => page.Choices.Add(new Choice { Name = name, Help = help, Apply = apply, IsCurrent = isCurrent });

        private static void SetSnap(float degrees)
        {
            Settings.TurnMode = VRTurnMode.Snap;
            Settings.SnapTurnDegrees = degrees;
        }

        private static bool IsSnap(float degrees) => Settings.TurnMode == VRTurnMode.Snap && Math.Abs(Settings.SnapTurnDegrees - degrees) < 1;

        private static void SetVignette(VRVignetteStrength carried, VRVignetteStrength moving)
        {
            Settings.Vignette = carried;
            Settings.VignetteWhenMoving = moving;
        }

        // ------------------------------------------------------------------ open, close, pointing

        public void Show(VRPose head, VRInput input)
        {
            if (!head.Valid) return;
            PlaceInFront(head, 0.9f, 0.1f, 8);
            Open = true;
            PageIndex = 0;
            Armed = input == null || (!input.Select.Down && !input.Secondary.Down);
            Hover = NoHover;
            Dirty = true;
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        private void Finish()
        {
            Open = false;
            PointerDistance = 0;
            Settings.ComfortSetupDone = true;
            Settings.Save();
            SimitoneVR.Log($"comfort setup done: {Settings.SimActionView}, {Settings.TurnMode} {Settings.SnapTurnDegrees}, "
                + $"{Settings.Movement}, height +{Settings.HeightOffset}, vignette {Settings.Vignette}/{Settings.VignetteWhenMoving}");
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        private const int IntroTop = 82, IntroLine = 20;

        /// <summary>Where a page's choices start: below its intro text.</summary>
        private static int ChoicesTop(Page page)
            => Math.Max(ChoiceTop, IntroTop + VRPixelFont.Wrap(page.Intro ?? "", 2, Width - Margin * 2).Count * IntroLine + 12);

        private static Rectangle ChoiceRect(Page page, int i)
            => new Rectangle(Margin, ChoicesTop(page) + i * (ChoiceHeight + ChoiceGap), Width - Margin * 2, ChoiceHeight);
        private static Rectangle BackRect => new Rectangle(Margin, NavTop, NavWidth, 56);
        private static Rectangle NextRect => new Rectangle(Width - Margin - NavWidth, NavTop, NavWidth, 56);

        /// <summary>Points the right controller at the setup and handles choosing. Call once per frame while open.</summary>
        public void Update(VRInput input, VRRuntime runtime)
        {
            PointerDistance = 0;
            if (!Open || input == null || !input.Active) return;
            if (!input.Select.Down && !input.Secondary.Down) Armed = true;

            var page = Pages[PageIndex];
            var hover = NoHover;
            if (PointAt(input, Width, Height, out var pixel))
            {
                for (int i = 0; i < page.Choices.Count; i++) if (ChoiceRect(page, i).Contains(pixel)) hover = i;
                if (PageIndex > 0 && BackRect.Contains(pixel)) hover = BackButton;
                if (NextRect.Contains(pixel)) hover = NextButton;
            }
            if (hover != Hover)
            {
                Hover = hover;
                Dirty = true;
                if (hover != NoHover) runtime?.Haptic(true, 0.1f, 0.01f);
            }
            if (!Armed) return;

            if (input.Secondary.Pressed && PageIndex > 0) GoTo(PageIndex - 1, runtime);
            if (!input.Select.Pressed || hover == NoHover) return;
            if (hover == BackButton) GoTo(PageIndex - 1, runtime);
            else if (hover == NextButton)
            {
                if (PageIndex == Pages.Count - 1) Finish();
                else GoTo(PageIndex + 1, runtime);
            }
            else
            {
                page.Choices[hover].Apply();
                Settings.Save();
                Dirty = true;
                runtime?.Haptic(true, 0.3f, 0.02f);
                HITVM.Get()?.PlaySoundEvent(UISounds.Click);
            }
        }

        private void GoTo(int index, VRRuntime runtime)
        {
            PageIndex = Math.Max(0, Math.Min(Pages.Count - 1, index));
            Hover = NoHover;
            Dirty = true;
            runtime?.Haptic(true, 0.2f, 0.015f);
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Redraws the setup when something changed. Call outside the main draw.</summary>
        public void Render(GraphicsDevice gd)
        {
            if (!Open || (!Dirty && Target != null)) return;
            Dirty = false;
            var page = Pages[PageIndex];

            var bg = new Color(16, 20, 28) * 0.95f;
            var frame = new Color(90, 200, 255);
            var text = Color.White * 0.92f;
            var green = new Color(140, 255, 190);

            BeginRender(gd, Width, Height);
            Fill(new Rectangle(0, 0, Width, Height), bg);
            Border(new Rectangle(0, 0, Width, Height), frame, 3);
            Text($"COMFORT SETUP  {PageIndex + 1}/{Pages.Count}", Margin, 18, 2, text * 0.5f);
            var titleDot = VRPixelFont.Measure(page.Title, 4) <= Width - Margin * 2 ? 4 : 3; // long titles a size smaller
            Text(VRPixelFont.Fit(page.Title, titleDot, Width - Margin * 2), Margin, titleDot == 4 ? 40 : 44, titleDot, frame);
            var y = IntroTop;
            foreach (var line in VRPixelFont.Wrap(page.Intro ?? "", 2, Width - Margin * 2))
            {
                Text(line, Margin, y, 2, text * 0.8f);
                y += IntroLine;
            }

            for (int i = 0; i < page.Choices.Count; i++)
            {
                var choice = page.Choices[i];
                var r = ChoiceRect(page, i);
                var current = choice.IsCurrent();
                Fill(r, i == Hover ? frame * 0.3f : current ? green * 0.18f : new Color(40, 48, 62));
                if (current) Border(r, green, 3);
                Text((current ? "> " : "") + choice.Name, r.X + 16, r.Y + 12, 3, current ? green : text);
                var helpY = r.Y + 44;
                foreach (var line in VRPixelFont.Wrap(choice.Help, 2, r.Width - 32))
                {
                    Text(line, r.X + 16, helpY, 2, text * 0.7f);
                    helpY += 18;
                }
            }

            if (PageIndex > 0)
            {
                Fill(BackRect, Hover == BackButton ? frame * 0.45f : new Color(40, 48, 62));
                TextCentred("BACK", BackRect, 3, text);
            }
            var last = PageIndex == Pages.Count - 1;
            Fill(NextRect, Hover == NextButton ? frame * 0.6f : frame * 0.3f);
            TextCentred(last ? "START" : "NEXT", NextRect, 3, Color.White);
            EndRender(gd);
        }

        public void Submit(VRRuntime runtime, ref VRFrameSubmission submission)
        {
            if (Open) SubmitPanel(runtime, ref submission);
        }
    }
}
