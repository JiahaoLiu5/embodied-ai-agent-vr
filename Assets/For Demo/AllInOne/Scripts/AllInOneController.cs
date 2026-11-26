using System;
using System.Collections;
using System.Linq;
using DemoKit;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    public enum AppPhase { Boot, Menu, Free, Session, Finished }

    /// <summary>
    /// The integrated demo: start panel, free conversation, full session, and the way back to the menu. Connects the
    /// panels with the parts that do the work.
    /// </summary>
    public sealed class AllInOneController : MonoBehaviour
    {
        private const string AvatarKey = "aio.avatar";
        private const string CaptionsKey = "aio.captions";
        private const int PanelLayer = 1;
        private const float TrackingWait = 3f;
        private const float CoveredSeconds = 0.4f;

        // Where the desktop view turns to
        private const float CentreYaw = 180f;
        private const float AvatarYaw = 174f;
        private const float MovementYaw = 160f;
        private const float TalkYaw = 151f;
        private const float SessionYaw = 144f;

        private const float CaptionScale = 0.55f;
        private const float DesktopCaptionAngle = 4f;
        private const float ToastScale = 0.6f;
        private const float ToastDistance = 0.7f;
        private const float ToastHeight = 0.34f;

        private static readonly Vector3 LeftOfUser = new Vector3(0.78f, 0f, -0.72f);
        private static readonly Vector3 RightOfUser = new Vector3(-0.64f, 0f, -0.90f);

        [Header("Content")]
        [SerializeField] private AgentAvatarCatalog catalog;
        [SerializeField] private AgentTopic[] topics;
        [SerializeField] private TextAsset freePrompt;

        [Header("Parts")]
        [SerializeField] private UserRig rig;
        [SerializeField] private RecentreControl recentre;
        [SerializeField] private ConditionSwitcher switcher;
        [SerializeField] private ConversationController conversation;
        [SerializeField] private SessionDirector director;
        [SerializeField] private AgentPresence presence;
        [SerializeField] private BoardPresenter boards;
        [SerializeField] private SpeechCaptions speechCaptions;
        [SerializeField] private AvatarDresser body;
        [SerializeField] private MirrorFigure figure;

        private StartPanel start;
        private TalkPanel talk;
        private SessionPanel session;
        private bool isBusy, isSelfView, pausedByDialog;

        public AgentChoices Choices { get; } = new AgentChoices();

        public AppPhase Phase { get; private set; } = AppPhase.Boot;

        public AgentAvatarCatalog Catalog => catalog;

        public AgentTopic[] Topics => topics;

        public UserRig Rig => rig;

        public ConversationController Conversation => conversation;

        public SessionDirector Director => director;

        public CaptionService Captions { get; private set; }

        public Dialogs Dialogs { get; private set; }

        /// <summary>Where the user's eyes are when the user stands in the start place.</summary>
        public Vector3 HeadHome => UserRig.Home + Vector3.up * UserRig.EyeHeight;

        internal ConditionSwitcher Switcher => switcher;
        internal AgentPresence Presence => presence;
        internal BoardPresenter Boards => boards;
        internal MirrorFigure Figure => figure;
        internal AvatarDresser Body => body;
        internal RecentreControl Recentring => recentre;
        internal StartPanel Menu => start;
        internal TalkPanel Talk => talk;
        internal SessionPanel Session => session;
        internal bool IsBusy => isBusy;

        /// <summary>Whether the figure stands beside the user during the experience.</summary>
        public bool SelfView
        {
            get { return isSelfView; }
            set
            {
                isSelfView = value;
                if (value && Phase != AppPhase.Menu) figure.Show(RightOfUser);
                else figure.Hide();
            }
        }

        // Start -----------------------------------------------------------------------------------------------

        private IEnumerator Start()
        {
            if (!IsComplete())
            {
                Debug.LogError("[AllInOne] A part of the demo is missing. Build the scene again (menu Embodied Agent, All-in-One).", this);
                enabled = false;
                yield break;
            }

            DemoFade fade = DemoFade.For(rig.ViewCamera);
            fade.To(1f, 0f);
            yield return rig.WaitForHeadset();
            rig.SetUp();
            recentre.Listen();
            yield return WaitForTracking();
            bool startedTracked = rig.IsTracked;

            Choices.Avatar = catalog.Find(PlayerPrefs.GetString(AvatarKey, ""));
            Choices.Topic = topics[0];
            BuildPanels();
            Subscribe();

            switcher.SetMode(MovementMode.Independent, true);
            body.Dress(Choices.Avatar);
            figure.Body.Dress(Choices.Avatar);
            recentre.Recentre();
            ShowMenu(StartPanel.AvatarPage);

            // The bodies need a few frames to take their poses.
            for (int i = 0; i < 12; i++) yield return null;
            fade.In(0.5f);

            // A headset that is put on later starts in the right place as well.
            if (startedTracked) yield break;
            while (!rig.IsTracked) yield return null;
            Recentre();
        }

        private bool IsComplete()
        {
            UnityEngine.Object[] parts =
            {
                catalog, freePrompt, rig, recentre, switcher, conversation, director, presence, boards, speechCaptions, body, figure
            };
            return parts.All(part => part != null) && catalog.avatars.Length > 0 && topics.Length > 0 && topics.All(topic => topic != null);
        }

        private IEnumerator WaitForTracking()
        {
            yield return null;
            for (float waited = 0f; !rig.IsTracked && waited < TrackingWait; waited += Time.deltaTime) yield return null;
        }

        private void BuildPanels()
        {
            // One layer for all panels keeps them out of the mirrors.
            DemoUi.Theme.layer = PanelLayer;
            DemoToast.Distance = ToastDistance;
            DemoToast.Height = ToastHeight;

            // Captions and messages stand between the user and the agent, below and above the boards.
            DemoCaptionBar bar = rig.IsHeadset
                ? DemoCaptionBar.Create(rig.Head, "captions", 900f, 0.65f, -0.51f)
                : DemoCaptionBar.Create(rig.Head, "captions", 900f, 0.47f, -0.312f);
            bar.Bar.Scale(CaptionScale);
            if (!rig.IsHeadset)
            {
                // Upright, because a tilted bar looks distorted at the edge of a wide flat picture.
                bar.Bar.Placement.billboard = DemoBillboard.Yaw;
                bar.Bar.Placement.lazyAngle = DesktopCaptionAngle;
            }
            Captions = new CaptionService(bar, PlayerPrefs.GetInt(CaptionsKey, 1) == 1);
            director.Captions = Captions;
            speechCaptions.Captions = Captions;

            start = new StartPanel(this);
            talk = new TalkPanel(this);
            session = new SessionPanel(this);
            Dialogs = new Dialogs(this);
        }

        private void Subscribe()
        {
            start.PageChanged += HandlePage;
            conversation.StateChanged += HandleConversationState;
            conversation.ProblemRaised += HandleProblem;
            conversation.Notice += text => Notify(text);
            switcher.RecordingsMissing += () => Notify(AgentDemoTexts.NoRecordings, DemoTone.Warning);
            director.StepChanged += HandleStep;
            director.PausedChanged += HandlePaused;
            director.Finished += HandleFinished;
            rig.TalkPressed += conversation.PressToTalk;
            rig.MenuPressed += AskToLeave;
            rig.PanelPressed += LookAtPanel;
        }

        private void Update()
        {
            if (Phase == AppPhase.Menu) start.Tick();
            else if (Phase == AppPhase.Free) talk.Tick();
            else if (Phase == AppPhase.Session) session.Tick();
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (!isPaused) return;

            // The system may close the app or its connection without notice. A session pauses itself.
            PlayerPrefs.Save();
            if (Phase == AppPhase.Free) conversation.End();
        }

        // Choices ---------------------------------------------------------------------------------------------

        public void ChooseAvatar(string id)
        {
            Choices.Avatar = catalog.Find(id);
            body.Dress(Choices.Avatar);
            figure.Body.Dress(Choices.Avatar);
            PlayerPrefs.SetString(AvatarKey, Choices.Avatar.id);
        }

        /// <summary>Applies a movement transformation at once when the user can see it.</summary>
        public void ChooseMode(MovementMode mode)
        {
            Choices.Mode = mode;
            if (Phase == AppPhase.Free || IsMovementPage) switcher.SetMode(mode);
        }

        public void ChooseStrength(float strength)
        {
            Choices.Strength = strength;
            switcher.Strength = strength;
        }

        public void SetCaptions(bool on)
        {
            Captions.On = on;
            PlayerPrefs.SetInt(CaptionsKey, on ? 1 : 0);
        }

        public void Recentre() => recentre.RecentreSoftly();

        public void OpenKeyHelp() => Dialogs.OpenKeyHelp();

        public void CheckKey()
        {
            bool hasKey = conversation.HasKey;
            if (hasKey && conversation.State == ConversationState.NoKey) conversation.End();
            start.RefreshSummary();
            Notify(hasKey ? "Key found" : "No key found", hasKey ? DemoTone.Good : DemoTone.Warning);
        }

        // Menu ------------------------------------------------------------------------------------------------

        private bool IsMovementPage => Phase == AppPhase.Menu && start.Page == StartPanel.MovementPage;

        private void ShowMenu(int page)
        {
            Phase = AppPhase.Menu;
            SelfView = false;
            rig.ShowHelp(true);
            start.Show(page);
        }

        private void HandlePage(int page)
        {
            if (Phase != AppPhase.Menu) return;

            bool isMovement = page == StartPanel.MovementPage;
            presence.Visible = isMovement;
            presence.OfflineGestures = isMovement;
            switcher.Strength = Choices.Strength;
            switcher.SetMode(isMovement ? Choices.Mode : MovementMode.Independent);

            if (isMovement) figure.Show(RightOfUser);
            else if (page == StartPanel.AvatarPage) figure.Show(LeftOfUser);
            else figure.Hide();
            rig.TurnTo(isMovement ? MovementYaw : page == StartPanel.AvatarPage ? AvatarYaw : CentreYaw);
        }

        // Experience ------------------------------------------------------------------------------------------

        /// <summary>Starts what the user has chosen.</summary>
        public void Begin()
        {
            if (Phase == AppPhase.Menu) StartCoroutine(Transition(BeginCovered));
        }

        private void BeginCovered()
        {
            Dialogs.CloseAll();
            start.Hide(true);
            figure.Hide();
            presence.OfflineGestures = false;
            presence.Visible = true;
            presence.ReturnToIdle();
            recentre.Recentre();
            rig.ShowHelp(false);
            switcher.Strength = Choices.Strength;
            switcher.NoiseStart = Choices.IsSession ? RecordedStart.Session : RecordedStart.Conversation;
            switcher.SetMode(Choices.Mode, true);

            if (Choices.IsSession) BeginSession();
            else BeginFree();

            // On a desktop the view turns to the panel by itself.
            if (rig.IsHeadset) Notify(AgentDemoTexts.PanelHint);
        }

        private void BeginFree()
        {
            Phase = AppPhase.Free;
            StartTalking();
            talk.Show();
            rig.TurnTo(TalkYaw);
        }

        private void BeginSession()
        {
            Phase = AppPhase.Session;
            director.Begin(Choices.Topic, Choices.Pace);
            session.Show();
            HandleStep();
        }

        private void StartTalking()
        {
            conversation.Begin(freePrompt.text, true);
            presence.OfflineGestures = !conversation.IsWanted;
        }

        public void ToggleConversation()
        {
            if (conversation.IsWanted) conversation.End();
            else StartTalking();
        }

        /// <summary>Starts the session again, also after it has finished.</summary>
        public void RestartSession()
        {
            if (Phase == AppPhase.Session || Phase == AppPhase.Finished) StartCoroutine(Transition(RestartCovered));
        }

        private void RestartCovered()
        {
            Dialogs.CloseAll();
            switcher.NoiseStart = RecordedStart.Session;
            presence.ReturnToIdle();
            BeginSession();
        }

        // Back to the menu ------------------------------------------------------------------------------------

        /// <summary>Goes back to the menu. A running session asks first.</summary>
        public void AskToLeave()
        {
            if (Phase == AppPhase.Session) Dialogs.OpenConfirm();
            else if (Phase == AppPhase.Free || Phase == AppPhase.Finished) LeaveAtOnce();
        }

        public void StayInSession() => Dialogs.Close(Dialogs.Confirm);

        public void LeaveAtOnce()
        {
            if (Phase != AppPhase.Boot && Phase != AppPhase.Menu) StartCoroutine(Transition(LeaveCovered));
        }

        private void LeaveCovered()
        {
            pausedByDialog = false;
            Dialogs.CloseAll();
            director.Stop();
            conversation.End();
            speechCaptions.Stop();
            Captions.Clear();
            talk.Hide(true);
            session.Hide(true);
            presence.OfflineGestures = false;
            presence.ReturnToIdle();
            recentre.Recentre();
            switcher.SetMode(MovementMode.Independent, true);
            ShowMenu(StartPanel.ReadyPage);
        }

        /// <summary>Covers the view, makes the change, and uncovers it. Other changes are ignored meanwhile.</summary>
        private IEnumerator Transition(Action change)
        {
            if (isBusy) yield break;
            isBusy = true;
            yield return DemoFade.For(rig.ViewCamera).BlinkRoutine(change, 0.22f, CoveredSeconds, 0.38f);
            isBusy = false;
        }

        // Events ----------------------------------------------------------------------------------------------

        private void HandleConversationState(ConversationState state)
        {
            // A problem does not matter any more when the conversation has ended or starts again.
            if (state != ConversationState.Problem) Dialogs.Close(Dialogs.Problem);
            talk.ShowState(state);
            session.ShowState(state);
        }

        private static void Notify(string text, DemoTone tone = DemoTone.Neutral, float seconds = 3.5f)
        {
            DemoUi.Toast(text, tone, seconds);
            DemoToast.CurrentPanel.Scale(ToastScale);
        }

        /// <summary>On a desktop the view turns to the board, or so that the panel and the agent are both seen.</summary>
        private void HandleStep()
        {
            session.ShowStep();
            rig.TurnTo(boards.Current == AgentSessionScript.Board.None ? SessionYaw : CentreYaw);
        }

        private void LookAtPanel()
        {
            float panelYaw = Phase == AppPhase.Session ? SessionYaw : Phase == AppPhase.Free ? TalkYaw : CentreYaw;
            rig.TurnTo(rig.IsTurnedTo(panelYaw) ? CentreYaw : panelYaw);
        }

        private void HandleProblem(RealtimeProblem problem, string detail)
        {
            if (Phase == AppPhase.Free || Phase == AppPhase.Session) Dialogs.OpenProblem(problem, detail);
        }

        public void RetryConversation() => conversation.Retry();

        /// <summary>Goes on with gestures only.</summary>
        public void ContinueWithoutVoice()
        {
            conversation.End();
            presence.OfflineGestures = true;
        }

        private void HandlePaused(bool isPaused)
        {
            session.ShowPaused(isPaused);
            if (isPaused) Notify(AgentDemoTexts.Paused, DemoTone.Neutral, 1.5f);
        }

        private void HandleFinished()
        {
            Dialogs.CloseAll();
            Phase = AppPhase.Finished;
            session.Hide();
            presence.ReturnToIdle();
            Dialogs.OpenFinished();
            rig.TurnTo(CentreYaw);
        }

        /// <summary>A board or the menu would stand in front of a dialog, and leaving the session pauses it.</summary>
        internal void HandleDialogChanged()
        {
            bool isOpen = Dialogs.Current != null;
            boards.Hidden = isOpen;
            if (Phase == AppPhase.Menu) start.MakeRoom(isOpen);
            if (isOpen) rig.TurnTo(CentreYaw);
            else if (Phase == AppPhase.Free) rig.TurnTo(TalkYaw);
            if (Phase != AppPhase.Session) return;

            bool asks = Dialogs.Current == Dialogs.Confirm;
            if (asks && !director.IsPaused)
            {
                pausedByDialog = true;
                director.Pause();
            }
            else if (!asks && pausedByDialog)
            {
                pausedByDialog = false;
                director.Resume();
            }
            if (!isOpen && director.IsRunning) HandleStep();
        }
    }
}