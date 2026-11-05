using System;
using System.Collections.Generic;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The first-run comfort setup, as a Simitone dialog floating in front of you: a few pages, each one question with
    /// outlined choices (the current setting is filled green, and choosing applies it at once): which hand you point
    /// with, what happens when your Sim walks off, turning, moving, standing or seated, and the vignette. Shown the
    /// first time VR starts (until <see cref="VRSettings.ComfortSetupDone"/>), and from the VR menu's "Comfort setup".
    /// Point with a controller and pull the trigger; the grip goes back a page.
    /// </summary>
    public class VRSetup : VRUIPanel
    {
        private const int PanelWidth = 1000, PanelHeight = 790, Left = 50, ContentWidth = 880;
        private const int ChoicesTop = 230, ChoiceHeight = 96, ChoiceGap = 10;

        private class Choice
        {
            public string Name, Help;
            public Action Apply;
            public Func<bool> IsCurrent;
        }

        private class Page
        {
            public string Title, Intro;
            public readonly List<Choice> Choices = new List<Choice>();
        }

        private readonly List<Page> Pages = new List<Page>();
        private int PageIndex;
        private readonly UILabel StepLabel, PageTitle, Intro;
        private readonly UIBigButton Back, Next;
        private readonly List<UIVRChoice> ChoiceUI = new List<UIVRChoice>();

        private static VRSettings Settings => SimitoneVR.Settings;

        public VRSetup() : base(PanelWidth, PanelHeight, 0.75f)
        {
            DefinePages();
            VRUI.DialogFrame(this, PanelWidth, PanelHeight, "Comfort setup");
            StepLabel = VRUI.Label("", 19, UIStyle.Current.DialogTitle, FSO.Client.UI.Framework.TextAlignment.Right | FSO.Client.UI.Framework.TextAlignment.Middle);
            StepLabel.Position = new Vector2(PanelWidth - 32 - 40 - 200, 10);
            StepLabel.Size = new Vector2(200, 70);
            Add(StepLabel);
            PageTitle = VRUI.Label("", 25, UIStyle.Current.Text);
            PageTitle.Position = new Vector2(Left, 104);
            Add(PageTitle);
            Intro = VRUI.Paragraph("", 15, UIStyle.Current.Text * 0.85f, ContentWidth, 80);
            Intro.Position = new Vector2(Left, 146);
            Add(Intro);

            Back = new UIBigButton(false) { Caption = "Back", Width = 275 };
            Back.Position = new Vector2(Left, PanelHeight - 125);
            Back.OnButtonClick += _ => GoBack();
            Add(Back);
            Next = new UIBigButton(true) { Caption = "Next", Width = 275 };
            Next.Position = new Vector2(PanelWidth - 32 - Left - 275, PanelHeight - 125);
            Next.OnButtonClick += _ => GoNext();
            Add(Next);
            BuildPage();
        }

        private void DefinePages()
        {
            Pages.Add(new Page
            {
                Title = "Welcome to Simitone VR",
                Intro = "A few quick choices to keep you comfortable. You can change them any time in the VR menu: the "
                    + "menu button, or the VR options button on your wrist. Point with a controller and pull the trigger to "
                    + "choose."
            });

            var hands = Add("Which hand do you point with?", "That hand points and picks; the other one walks.");
            Option(hands, "Right", "Point with the right controller, walk with the left stick.",
                () => Settings.LeftHanded = false, () => !Settings.LeftHanded);
            Option(hands, "Left", "Point with the left controller, walk with the right stick.",
                () => Settings.LeftHanded = true, () => Settings.LeftHanded);

            var tiers = Add("When your Sim walks off on their own", "To the fridge, a chair, a friend...");
            Option(tiers, "Comfortable (best to start)", "Step out, watch, and follow them in quick blinks. The view never slides.",
                () => Settings.SimActionView = VRSimActionView.Comfortable, () => Settings.SimActionView == VRSimActionView.Comfortable);
            Option(tiers, "Moderate", "Step out and glide after them at a steady walking pace.",
                () => Settings.SimActionView = VRSimActionView.Moderate, () => Settings.SimActionView == VRSimActionView.Moderate);
            Option(tiers, "Intense", "Stay in their head while they walk you about. For people used to VR.",
                () => Settings.SimActionView = VRSimActionView.Intense, () => Settings.SimActionView == VRSimActionView.Intense);

            var turning = Add("Turning", "With the stick on your pointing hand.");
            Option(turning, "Snap 30 (best to start)", "Turn in quick steps.", () => SetSnap(30), () => IsSnap(30));
            Option(turning, "Snap 45", "Bigger steps.", () => SetSnap(45), () => IsSnap(45));
            Option(turning, "Smooth", "Turn continuously. This can make some people feel sick.",
                () => { Settings.TurnMode = VRTurnMode.Smooth; Settings.SmoothTurnDegreesPerSecond = 120; },
                () => Settings.TurnMode == VRTurnMode.Smooth);

            var moving = Add("Moving", "Playing as your Sim, they walk; as an observer, you do.");
            Option(moving, "Stick and arc", "Walk with the stick, or hold that hand's trigger, aim the arc and let go to go there.",
                () => Settings.Movement = VRMovement.StickAndArc, () => Settings.Movement == VRMovement.StickAndArc);
            Option(moving, "Arc only (most comfortable)", "Only the arc: your Sim walks there and you follow. No sliding at all.",
                () => Settings.Movement = VRMovement.Arc, () => Settings.Movement == VRMovement.Arc);
            Option(moving, "Stick only", "Only the left stick.",
                () => Settings.Movement = VRMovement.Stick, () => Settings.Movement == VRMovement.Stick);

            var posture = Add("How are you playing?", "In your Sim's head you see from their height either way.");
            Option(posture, "Standing", "Your own height when you step out, or roam as an observer.",
                () => Settings.HeightOffset = 0, () => Settings.HeightOffset < 0.01f);
            Option(posture, "Seated", "Raises your view by 45 cm when you're not in your Sim's head.",
                () => Settings.HeightOffset = 0.45f, () => Settings.HeightOffset >= 0.01f);

            var vignette = Add("Vignette", "Darkens the edge of your view while it moves without you moving your head.");
            Option(vignette, "Off", "Never.", () => SetVignette(VRVignetteStrength.Off, VRVignetteStrength.Off),
                () => Settings.Vignette == VRVignetteStrength.Off);
            Option(vignette, "Low", "A little, while your Sim carries you.", () => SetVignette(VRVignetteStrength.Low, VRVignetteStrength.Off),
                () => Settings.Vignette == VRVignetteStrength.Low);
            Option(vignette, "Medium (best to start)", "While your Sim carries you, and a little while you walk them.",
                () => SetVignette(VRVignetteStrength.Medium, VRVignetteStrength.Low), () => Settings.Vignette == VRVignetteStrength.Medium);
            Option(vignette, "High", "Strongly, also while you walk them.", () => SetVignette(VRVignetteStrength.High, VRVignetteStrength.Medium),
                () => Settings.Vignette == VRVignetteStrength.High);

            Pages.Add(new Page
            {
                Title = "All set",
                Intro = "Have fun! If anything feels uncomfortable, take a break and try the more comfortable options. The VR "
                    + "menu has all of these and more, and the game's Options panel has a VR button too."
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

        // ------------------------------------------------------------------ pages

        private void BuildPage()
        {
            foreach (var old in ChoiceUI) Remove(old);
            ChoiceUI.Clear();
            var page = Pages[PageIndex];
            StepLabel.Caption = $"{PageIndex + 1} of {Pages.Count}";
            PageTitle.Caption = page.Title;
            Intro.Caption = page.Intro ?? "";
            for (int i = 0; i < page.Choices.Count; i++)
            {
                var choice = page.Choices[i];
                var ui = new UIVRChoice(choice.Name, choice.Help, ContentWidth, ChoiceHeight);
                ui.Position = new Vector2(Left, ChoicesTop + i * (ChoiceHeight + ChoiceGap));
                ui.OnChosen += () =>
                {
                    choice.Apply();
                    Settings.Save();
                    HITVM.Get()?.PlaySoundEvent(UISounds.Click);
                };
                Add(ui);
                ChoiceUI.Add(ui);
            }
            Back.Visible = PageIndex > 0;
            Next.Caption = PageIndex == Pages.Count - 1 ? "Start" : "Next";
        }

        protected override void Refresh(UpdateState state)
        {
            var page = Pages[PageIndex];
            for (int i = 0; i < ChoiceUI.Count && i < page.Choices.Count; i++) ChoiceUI[i].Selected = page.Choices[i].IsCurrent();
        }

        /// <summary>Starts the setup from its first page, in front of you.</summary>
        public void Begin(VRPose head)
        {
            if (!head.Valid) return;
            PlaceInFront(head, 1.0f, 0.1f, 8);
            PageIndex = 0;
            BuildPage();
            Show();
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        public void GoBack()
        {
            if (PageIndex == 0) return;
            PageIndex--;
            BuildPage();
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        private void GoNext()
        {
            if (PageIndex < Pages.Count - 1)
            {
                PageIndex++;
                BuildPage();
                HITVM.Get()?.PlaySoundEvent(UISounds.Click);
                return;
            }
            Hide();
            Settings.ComfortSetupDone = true;
            Settings.Save();
            SimitoneVR.Log($"comfort setup done: {Settings.SimActionView}, {Settings.TurnMode} {Settings.SnapTurnDegrees}, "
                + $"{Settings.Movement}, height +{Settings.HeightOffset}, vignette {Settings.Vignette}/{Settings.VignetteWhenMoving}");
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        /// <summary>For the UI check tool: shows a page without placing the panel.</summary>
        public void ShowPage(int index)
        {
            PageIndex = Math.Max(0, Math.Min(Pages.Count - 1, index));
            BuildPage();
            Show();
        }
    }
}
