using System;
using System.Collections.Generic;
using System.Linq;
using DemoKit;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>The start panel: avatar, movement, experience, and start.</summary>
    public sealed class StartPanel
    {
        public const int AvatarPage = 0;
        public const int MovementPage = 1;
        public const int ExperiencePage = 2;
        public const int ReadyPage = 3;

        private const string FreeId = "free";
        private const string SessionId = "session";

        // Beside the figure, beside the agent, and straight ahead
        private static readonly Vector3 AvatarPlace = new Vector3(-0.22f, 1.52f, -0.67f);
        private static readonly Vector3 MovementPlace = new Vector3(0.96f, 1.50f, -0.24f);
        private static readonly Vector3 CentrePlace = new Vector3(0f, 1.50f, -0.70f);
        private const float Width = 1200f;
        private const float PanelScale = 0.93f;
        private const float MovementScale = 0.72f;
        private const float MicrophoneInterval = 1f;

        private readonly AllInOneController app;
        private readonly DemoPanel panel;
        private DemoPages pages;
        private DemoProgressDots steps;
        private DemoButton back, next, go;
        private DemoScrollGrid avatars, modes, experiences, topics;
        private DemoStepper strength;
        private DemoToggleRow pace;
        private RectTransform topicColumn, freeColumn, paceBox;
        private DemoLabel topicInfo, paceInfo, avatarName, movementName, experienceName, keyNote;
        private DemoPicture portrait;
        private DemoStatus microphone, key;
        private float microphoneCheckIn;

        public StartPanel(AllInOneController app)
        {
            this.app = app;
            panel = Build();
            pages.Changed += ShowPage;
            ShowExperience();
            panel.Hide(true);
        }

        public DemoPanel Panel => panel;

        public int Page => pages.Index;

        /// <summary>Raised with the page when it changes, also when the panel is opened.</summary>
        public event Action<int> PageChanged;

        public void Show(int page)
        {
            ShowChoices();
            if (pages.Index == page) ShowPage(page);
            else pages.Go(page);
        }

        public void Hide(bool instant) => panel.Hide(instant);

        /// <summary>Steps aside while a dialog is open.</summary>
        public void MakeRoom(bool isNeeded)
        {
            if (isNeeded) panel.Hide();
            else panel.Show();
        }

        /// <summary>Follows the state of the microphone while it is shown.</summary>
        public void Tick()
        {
            microphoneCheckIn -= Time.deltaTime;
            if (Page != ReadyPage || microphoneCheckIn > 0f) return;
            microphoneCheckIn = MicrophoneInterval;
            RefreshMicrophone();
        }

        /// <summary>Shows the choices and the state of the key and the microphone on the last page.</summary>
        public void RefreshSummary()
        {
            AgentChoices choices = app.Choices;
            portrait.Texture = choices.Avatar.thumbnail;
            avatarName.Text = choices.Avatar.label;
            movementName.Text = AgentDemoTexts.ModeSummary(choices.Mode, choices.Strength);
            experienceName.Text = choices.IsSession
                ? "Full session, " + choices.Topic.title + ", " + AgentDemoTexts.PaceTitle(choices.Pace)
                : "Free conversation";

            bool hasKey = app.Conversation.HasKey;
            key.Set(hasKey ? "Found" : "Not found", hasKey ? DemoTone.Good : DemoTone.Warning);
            keyNote.Text = hasKey ? AgentDemoTexts.KeyNote : AgentDemoTexts.NoKeyNote;
            go.Label = hasKey ? "Start" : "Start without conversation";
            RefreshMicrophone();
        }

        private void RefreshMicrophone()
        {
            MicrophoneState state = app.Conversation.Microphone;
            microphone.Set(
                state == MicrophoneState.Ready ? "Ready" : state == MicrophoneState.Missing ? "Not found" : "Waiting for permission",
                state == MicrophoneState.Ready ? DemoTone.Good : DemoTone.Warning);
        }

        private DemoPanel Build()
        {
            return DemoUi.Panel("start", Width)
                .Steps("steps", 0, "Avatar", "Movement", "Experience", "Start").Out(out steps)
                .Pages("pages", p => p
                    .Page("avatar", FillAvatar)
                    .Page("movement", FillMovement)
                    .Page("experience", FillExperience)
                    .Page("ready", FillReady)).Out(out pages)
                .Row(r => r
                    .Button("back", "Back", () => pages.Back(), DemoStyle.Quiet, DemoIcon.Left, 190f).Out(out back)
                    .Flex()
                    .Primary("next", "Next", () => pages.Next(), DemoIcon.Right, 240f).Out(out next)
                    .Primary("go", "Start without conversation", app.Begin, DemoIcon.Play, 560f).Out(out go))
                .Build();
        }

        private void FillAvatar(DemoBuilder b)
        {
            AgentAvatarCatalog.Entry[] entries = app.Catalog.avatars;
            List<DemoCardData> cards = entries.Select(a => new DemoCardData(a.id, a.label, null, a.thumbnail)).ToList();
            int selected = Mathf.Max(0, Array.IndexOf(entries, app.Choices.Avatar));
            b.Title(AgentDemoTexts.AvatarTitle, AgentDemoTexts.AvatarSubtitle)
                .Cards("avatar", cards, app.ChooseAvatar, 3, 2, 192f, selected).Out(out avatars)
                .Row(r => r
                    .Button("recentre", "Recentre", app.Recentre, DemoStyle.Secondary, DemoIcon.None, 230f)
                    .Caption(AgentDemoTexts.RecentreHint));
        }

        private void FillMovement(DemoBuilder b)
        {
            b.Title(AgentDemoTexts.MovementTitle, AgentDemoTexts.MovementSubtitle)
                .Cards("movement", new[]
                {
                    new DemoCardData(AgentDemoTexts.ModeId(MovementMode.Independent), "Independent", AgentDemoTexts.IndependentText) { tag = "Default" },
                    new DemoCardData(AgentDemoTexts.ModeId(MovementMode.Leading), "Leading", AgentDemoTexts.LeadingText),
                    new DemoCardData(AgentDemoTexts.ModeId(MovementMode.Mimicry), "Mimicry", AgentDemoTexts.MimicryText),
                    new DemoCardData(AgentDemoTexts.ModeId(MovementMode.Noise), "Noise", AgentDemoTexts.NoiseText)
                }, ChooseMode, 2, 0, 0f, 0).Out(out modes)
                .Box(x => x
                    .Stepper("strength", "Strength", 0f, 100f, ConditionSwitcher.DefaultStrength * 100f, v => app.ChooseStrength(v / 100f), 10f, "0' %'")
                    .Out(out strength)
                    .Caption(AgentDemoTexts.StrengthNote))
                .Caption(AgentDemoTexts.MovementHint);
            strength.Interactable = false;
        }

        private void ChooseMode(string id)
        {
            app.ChooseMode((MovementMode)Enum.Parse(typeof(MovementMode), id, true));
            strength.Interactable = app.Choices.Mode != MovementMode.Independent;
        }

        /// <summary>Takes over what was chosen elsewhere, for example in a conversation.</summary>
        private void ShowChoices()
        {
            AgentChoices choices = app.Choices;
            avatars.Select(choices.Avatar.id);
            modes.Select(AgentDemoTexts.ModeId(choices.Mode));
            strength.SetSilently(choices.Strength * 100f);
            strength.Interactable = choices.Mode != MovementMode.Independent;
            experiences.Select(choices.IsSession ? SessionId : FreeId);
            topics.Select(choices.Topic.id);
            pace.SetSilently(choices.Pace == SessionPace.Study ? 0 : 1);
            ShowExperience();
        }

        private void FillExperience(DemoBuilder b)
        {
            b.Title(AgentDemoTexts.ExperienceTitle)
                .Row(r => r
                    .Column(left => left
                        .Cards("experience", new[]
                        {
                            new DemoCardData(FreeId, "Free conversation", AgentDemoTexts.FreeText) { icon = DemoIcon.Chat, tag = "Default" },
                            new DemoCardData(SessionId, "Full session", AgentDemoTexts.SessionText) { icon = DemoIcon.Clock }
                        }, ChooseExperience, 1, 0, 0f, 0).Out(out experiences)
                        .Column(c => c
                            .Toggle("pace", "Pace", new[] { "Study timing", "Quick preview" }, 0, ChoosePace, true).Out(out pace)
                            .Label("pace-info", AgentDemoTexts.QuickPaceInfo, DemoTextStyle.Caption).Out(out paceInfo)).Out(out paceBox), 0.46f)
                    .Column(right => right
                        .Column(c => c
                            .Heading("Topic")
                            .Cards("topic", app.Topics.Select(t => new DemoCardData(t.id, t.title)).ToList(), ChooseTopic, 2, 0, 0f, 0).Out(out topics)
                            .Label("topic-info", LongestTopicInfo()).Out(out topicInfo)).Out(out topicColumn)
                        .Column(c => c
                            .Heading(AgentDemoTexts.FreeInfoTitle)
                            .Note(AgentDemoTexts.FreeInfoVoice, DemoIcon.Mic, DemoTone.Neutral)
                            .Note(AgentDemoTexts.FreeInfoInterrupt, DemoIcon.Chat, DemoTone.Neutral)
                            .Note(AgentDemoTexts.FreeInfoMovement, DemoIcon.Link, DemoTone.Neutral)).Out(out freeColumn)
                        .Do(() => freeColumn.gameObject.SetActive(false)), 0.54f), 36f, TextAnchor.UpperLeft);
        }

        private void ChooseExperience(string id)
        {
            app.Choices.IsSession = id == SessionId;
            ShowExperience();
        }

        private void ChoosePace(int index)
        {
            app.Choices.Pace = index == 0 ? SessionPace.Study : SessionPace.Quick;
            ShowExperience();
        }

        private void ChooseTopic(string id)
        {
            app.Choices.Topic = app.Topics.First(t => t.id == id);
            ShowExperience();
        }

        private void ShowExperience()
        {
            AgentChoices choices = app.Choices;
            topicColumn.gameObject.SetActive(choices.IsSession);
            freeColumn.gameObject.SetActive(!choices.IsSession);
            paceBox.gameObject.SetActive(choices.IsSession);
            paceInfo.Text = choices.Pace == SessionPace.Study ? AgentDemoTexts.StudyPaceInfo : AgentDemoTexts.QuickPaceInfo;
            topicInfo.Text = TopicInfo(choices.Topic);
        }

        private static string TopicInfo(AgentTopic topic) =>
            topic.question + "\n\n<b>You argue:</b> " + topic.userPosition + "\n<b>Amy argues:</b> " + topic.agentPosition;

        /// <summary>The panel takes its height from the longest text, so that it does not grow later.</summary>
        private string LongestTopicInfo() => app.Topics.Select(TopicInfo).OrderByDescending(t => t.Length).First();

        private void FillReady(DemoBuilder b)
        {
            b.Title(AgentDemoTexts.ReadyTitle)
                .Row(r => r
                    .Picture(null, 236f, 372f).Out(out portrait)
                    .Column(c => c
                        .Column(a => a.Caption("Avatar").Label("avatar-name", "Avatar", DemoTextStyle.Strong).Out(out avatarName), 1f, 2f)
                        .Column(a => a.Caption("Movement").Label("movement-name", "Movement", DemoTextStyle.Strong).Out(out movementName), 1f, 2f)
                        .Column(a => a.Caption("Experience").Label("experience-name", "Experience", DemoTextStyle.Strong).Out(out experienceName), 1f, 2f), 1f, 16f),
                    32f, TextAnchor.MiddleLeft)
                .Box(x => x
                    .Status("mic", "Microphone", "Ready").Out(out microphone)
                    .Status("key", "OpenAI API key", "Not found").Out(out key))
                .Box(x => x.Label("key-note", AgentDemoTexts.KeyNote, DemoTextStyle.Caption).Out(out keyNote))
                .Row(r => r.Button("key-help", AgentDemoTexts.KeyHelpButton, app.OpenKeyHelp, DemoStyle.Quiet, DemoIcon.Info).Flex());
        }

        private void ShowPage(int page)
        {
            steps.Index = page;
            back.gameObject.SetActive(page > 0);
            next.gameObject.SetActive(page < ReadyPage);
            go.gameObject.SetActive(page == ReadyPage);
            if (page == ReadyPage) RefreshSummary();

            panel.Scale(page == MovementPage ? MovementScale : PanelScale);
            panel.ShowFacing(page == AvatarPage ? AvatarPlace : page == MovementPage ? MovementPlace : CentrePlace, app.HeadHome);
            PageChanged?.Invoke(page);
        }
    }
}