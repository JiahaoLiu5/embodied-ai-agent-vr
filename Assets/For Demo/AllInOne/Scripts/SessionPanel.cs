using DemoKit;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>The side panel of the full session: step, times, and manual control.</summary>
    public sealed class SessionPanel
    {
        // Beside the boards, which reach 43 degrees to each side, and out of the desktop view of the boards
        private static readonly Vector3 HeadsetPlace = new Vector3(0.96f, 1.45f, 0.01f);
        private static readonly Vector3 DesktopPlace = new Vector3(1.10f, 1.50f, 0.14f);
        private const float PanelScale = 0.66f;
        private const float Width = 1000f;
        private const float SmallestChange = 0.003f;

        private readonly AllInOneController app;
        private readonly SessionDirector director;
        private readonly DemoPanel panel;
        private DemoLabel title, conditions, hint;
        private DemoStatus step, state;
        private DemoProgressBar stepTime, totalTime;
        private DemoButton pause;
        private DemoSwitch interrupt, captions, selfView;
        private int shownStepSeconds = -1, shownTotalSeconds = -1;

        public SessionPanel(AllInOneController app)
        {
            this.app = app;
            director = app.Director;
            panel = Build();
            panel.Scale(PanelScale);
            panel.Hide(true);
        }

        public DemoPanel Panel => panel;

        public void Show()
        {
            title.Text = app.Choices.Topic.title;
            conditions.Text = AgentDemoTexts.ModeSummary(app.Choices.Mode, app.Choices.Strength) + ", " + AgentDemoTexts.PaceTitle(app.Choices.Pace);
            interrupt.SetSilently(app.Conversation.InterruptAgent);
            captions.SetSilently(app.Captions.On);
            selfView.SetSilently(app.SelfView);
            ShowStep();
            ShowPaused(director.IsPaused);
            panel.ShowFacing(app.Rig.IsHeadset ? HeadsetPlace : DesktopPlace, app.HeadHome);
        }

        public void Hide(bool instant = false) => panel.Hide(instant);

        /// <summary>Follows the session clock.</summary>
        public void Tick()
        {
            if (!director.IsRunning) return;
            ShowShare(stepTime, director.StepRemaining, director.StepSeconds);
            ShowShare(totalTime, director.TotalRemaining, director.TotalSeconds);
            ShowSeconds(stepTime, director.StepRemaining, ref shownStepSeconds);
            ShowSeconds(totalTime, director.TotalRemaining, ref shownTotalSeconds);
        }

        public void ShowStep()
        {
            AgentSessionScript.Step current = director.Step;
            if (current == null) return;
            step.Set(director.StepIndex + 1 + " of " + director.StepCount + ", " + current.title, DemoTone.Accent);
            bool needsButton = current.kind == AgentSessionScript.Kind.Conversation && app.Conversation.PushToTalk;
            hint.Text = needsButton ? current.hint + " " + AgentDemoTexts.PushToTalkHint(app.Rig.IsHeadset) : current.hint;
            state.gameObject.SetActive(director.IsConversationReached);
            ShowState(app.Conversation.State);
            panel.Layout();
        }

        public void ShowState(ConversationState newState) => state.Set(AgentDemoTexts.StateText(newState), TalkPanel.Tone(newState));

        public void ShowPaused(bool isPaused)
        {
            pause.Label = isPaused ? "Continue" : "Pause";
            pause.Icon = isPaused ? DemoIcon.Play : DemoIcon.Pause;
        }

        private DemoPanel Build()
        {
            return DemoUi.Panel("session", Width)
                .Row(r => r
                    .Column(a => a
                        .Label("title", "Topic", DemoTextStyle.Title).Out(out title)
                        .Label("conditions", "Movement", DemoTextStyle.Caption).Out(out conditions)
                        .Status("step", "Step", "").Out(out step)
                        .Label("hint", "", DemoTextStyle.Caption).Out(out hint)
                        .Progress("step-time", "This step", 1f).Out(out stepTime)
                        .Progress("total-time", "Session", 1f).Out(out totalTime)
                        .Status("state", AgentDemoTexts.AgentName, "").Out(out state)
                        .Switch("interrupt", AgentDemoTexts.InterruptLabel, true, on => app.Conversation.InterruptAgent = on).Out(out interrupt), 0.6f)
                    .Column(b => b
                        .Button("pause", "Continue", TogglePause, DemoStyle.Primary, DemoIcon.Pause).Out(out pause)
                        .Button("next-step", "Next step", director.Next, DemoStyle.Secondary, DemoIcon.Right)
                        .Button("restart", "Restart", app.RestartSession)
                        .Button("menu", "Back to menu", app.AskToLeave, DemoStyle.Secondary, DemoIcon.Home)
                        .Switch("captions", "Captions", true, on => app.SetCaptions(on)).Out(out captions)
                        .Switch("selfview", "Self view", false, on => app.SelfView = on).Out(out selfView), 0.4f, 16f), 44f, TextAnchor.UpperLeft)
                .Build();
        }

        private void TogglePause()
        {
            if (director.IsPaused) director.Resume();
            else director.Pause();
        }

        /// <summary>Very small changes are left out, because every change redraws the panel.</summary>
        private static void ShowShare(DemoProgressBar bar, float part, float whole)
        {
            float amount = whole > 0f ? part / whole : 0f;
            if (Mathf.Abs(amount - bar.Amount) > SmallestChange) bar.Amount = amount;
        }

        private static void ShowSeconds(DemoProgressBar bar, float remaining, ref int shown)
        {
            int seconds = Mathf.CeilToInt(remaining);
            if (seconds == shown) return;
            shown = seconds;
            bar.ValueText = seconds / 60 + ":" + (seconds % 60).ToString("00") + " left";
        }
    }
}