using System;
using System.Linq;
using DemoKit;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>The side panel of the free conversation.</summary>
    public sealed class TalkPanel
    {
        // Out of reach of the agent's hands
        private static readonly Vector3 Place = new Vector3(0.82f, 1.50f, 0.02f);
        private const float PanelScale = 0.62f;
        private const float Width = 1140f;
        private const float LevelSteps = 24f;

        private readonly AllInOneController app;
        private readonly DemoPanel panel;
        private DemoStatus state;
        private DemoProgressBar level;
        private DemoButton talk, keyHelp;
        private DemoToggleRow talkMode;
        private DemoLabel hint;
        private DemoScrollGrid mode;
        private DemoStepper strength;
        private DemoSwitch interrupt, captions, selfView;

        public TalkPanel(AllInOneController app)
        {
            this.app = app;
            panel = Build();
            panel.Scale(PanelScale);
            panel.Hide(true);
        }

        public DemoPanel Panel => panel;

        public void Show()
        {
            mode.Select(AgentDemoTexts.ModeId(app.Choices.Mode));
            strength.SetSilently(app.Choices.Strength * 100f);
            strength.Interactable = app.Choices.Mode != MovementMode.Independent;
            talkMode.SetSilently(app.Conversation.PushToTalk ? 1 : 0);
            interrupt.SetSilently(app.Conversation.InterruptAgent);
            captions.SetSilently(app.Captions.On);
            selfView.SetSilently(app.SelfView);
            ShowState(app.Conversation.State);
            panel.ShowFacing(Place, app.HeadHome);
        }

        public void Hide(bool instant = false) => panel.Hide(instant);

        /// <summary>Follows the loudness of the microphone, in steps because every change redraws the panel.</summary>
        public void Tick()
        {
            float amount = Mathf.Round(app.Conversation.Level * LevelSteps) / LevelSteps;
            if (!Mathf.Approximately(amount, level.Amount)) level.Amount = amount;
        }

        public void ShowState(ConversationState newState)
        {
            bool needsKey = newState == ConversationState.NoKey;
            state.Set(AgentDemoTexts.StateText(newState), Tone(newState));
            talk.Label = app.Conversation.IsWanted ? "Stop talking" : "Start talking";
            talk.Interactable = talkMode.Interactable = !needsKey;
            ShowHint();
            if (keyHelp.gameObject.activeSelf == needsKey) return;
            keyHelp.gameObject.SetActive(needsKey);
            panel.Layout();
        }

        internal static DemoTone Tone(ConversationState state)
        {
            switch (state)
            {
                case ConversationState.NoKey: return DemoTone.Warning;
                case ConversationState.Problem: return DemoTone.Danger;
                case ConversationState.Listening: return DemoTone.Good;
                case ConversationState.Connecting:
                case ConversationState.UserSpeaking:
                case ConversationState.AgentSpeaking: return DemoTone.Accent;
                default: return DemoTone.Neutral;
            }
        }

        private DemoPanel Build()
        {
            var modes = ((MovementMode[])Enum.GetValues(typeof(MovementMode)))
                .Select(m => new DemoCardData(AgentDemoTexts.ModeId(m), AgentDemoTexts.ModeTitle(m))).ToList();
            return DemoUi.Panel("talk", Width)
                .Row(r => r
                    .Column(a => a
                        .Heading("Conversation")
                        .Status("state", AgentDemoTexts.AgentName, "Not connected").Out(out state)
                        .Progress("level", "Microphone", 0f).Out(out level)
                        .Primary("talk", "Start talking", app.ToggleConversation, DemoIcon.Mic).Out(out talk)
                        .Button("key-help", AgentDemoTexts.KeyHelpButton, app.OpenKeyHelp, DemoStyle.Quiet, DemoIcon.Info).Out(out keyHelp)
                        .Toggle("talkmode", "", new[] { "Hands-free", "Push to talk" }, 0, ChooseTalkMode).Out(out talkMode)
                        .Label("talk-hint", AgentDemoTexts.HandsFreeHint, DemoTextStyle.Caption).Out(out hint)
                        .Switch("interrupt", AgentDemoTexts.InterruptLabel, true, ChooseInterrupt).Out(out interrupt)
                        .Switch("captions", "Captions", true, on => app.SetCaptions(on)).Out(out captions), 0.44f)
                    .Column(b => b
                        .Heading("Movement")
                        .Cards("mode", modes, ChooseMode, 2, 0, 0f, 0).Out(out mode)
                        .Stepper("strength", "Strength", 0f, 100f, ConditionSwitcher.DefaultStrength * 100f, v => app.ChooseStrength(v / 100f), 10f, "0' %'").Out(out strength)
                        .Switch("selfview", "Self view", false, on => app.SelfView = on).Out(out selfView)
                        .Space(8f)
                        .Button("menu", "Back to menu", app.AskToLeave, DemoStyle.Secondary, DemoIcon.Home), 0.56f), 36f, TextAnchor.UpperLeft)
                .Build();
        }

        private void ChooseTalkMode(int index)
        {
            app.Conversation.PushToTalk = index == 1;
            ShowHint();
        }

        private void ChooseInterrupt(bool on)
        {
            app.Conversation.InterruptAgent = on;
            ShowHint();
        }

        private void ChooseMode(string id)
        {
            app.ChooseMode((MovementMode)Enum.Parse(typeof(MovementMode), id, true));
            strength.Interactable = app.Choices.Mode != MovementMode.Independent;
        }

        private void ShowHint()
        {
            if (app.Conversation.State == ConversationState.NoKey) hint.Text = AgentDemoTexts.NoKeyHint;
            else if (app.Conversation.PushToTalk) hint.Text = AgentDemoTexts.PushToTalkHint(app.Rig.IsHeadset);
            else hint.Text = app.Conversation.InterruptAgent ? AgentDemoTexts.HandsFreeHint : AgentDemoTexts.HandsFreeNoInterruptHint;
        }
    }
}