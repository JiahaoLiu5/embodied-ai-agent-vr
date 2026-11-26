using System;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    public enum ConversationState
    {
        NoKey,
        Offline,
        Connecting,
        Listening,
        UserSpeaking,
        Thinking,
        AgentSpeaking,
        Suspended,
        Problem
    }

    /// <summary>
    /// Runs the live conversation: opens and closes the connection and the microphone, tells what is going on, reconnects
    /// once, and reports problems. The state is computed from the parts, so the order of their events does not matter.
    /// </summary>
    public sealed class ConversationController : MonoBehaviour
    {
        private const float ReconnectDelay = 1.5f;
        private const float MicrophoneInterval = 1f;
        private const float PermissionSeconds = 10f;
        private const float ThinkingLimit = 20f;
        private const float GreetingDelay = 2f;
        private const float SuspendedLimit = 120f;
        private const float IdleLimit = 300f;

        [SerializeField] private RealtimeAPIWrapper api;
        [SerializeField] private AudioRecorder recorder;
        [SerializeField] private AudioPlayer player;
        [Tooltip("While it plays, the microphone is muted.")]
        [SerializeField] private AudioSource narration;

        private bool isWanted, isSuspended, greets, guardsIdle;
        private bool wasConnected, hasReconnected, isGreetingPending, forgotConversation, sampleRateReported;
        private bool pushToTalk, isPressed;
        private float reconnectIn = -1f, microphoneCheckIn, withoutMicrophone, thinkingFor, greetingIn, suspendedFor, idleFor;

        public ConversationState State { get; private set; } = ConversationState.Offline;

        /// <summary>The problem of the Problem state.</summary>
        public RealtimeProblem Problem { get; private set; }

        /// <summary>True from Begin to End, also while connecting or suspended.</summary>
        public bool IsWanted => isWanted;

        public bool IsConnected => api.IsConnected;

        public bool HasKey => api.HasApiKey;

        public string Model => api.Model;

        public MicrophoneState Microphone => recorder.MicState;

        /// <summary>Loudness of the microphone, 0 to 1. Zero while it is closed.</summary>
        public float Level => recorder.IsMicrophoneOpen ? Loudness(recorder.CurrentInputDbFs) : 0f;

        /// <summary>Whether the user can interrupt the agent by speaking.</summary>
        public bool InterruptAgent { get { return recorder.InterruptAgent; } set { recorder.InterruptAgent = value; } }

        /// <summary>Hold a button to speak instead of just speaking.</summary>
        public bool PushToTalk
        {
            get { return pushToTalk; }
            set
            {
                if (pushToTalk == value) return;
                StopMicrophone();
                pushToTalk = value;
                recorder.listeningMode = value ? ListeningMode.PushToTalk : ListeningMode.VAD;
                microphoneCheckIn = 0f;
            }
        }

        public event Action<ConversationState> StateChanged;

        /// <summary>A problem that needs an answer of the user.</summary>
        public event Action<RealtimeProblem, string> ProblemRaised;

        /// <summary>A short message for the user.</summary>
        public event Action<string> Notice;

        private void OnEnable()
        {
            RealtimeAPIWrapper.OnProblem += HandleProblem;
            RealtimeAPIWrapper.OnSessionUpdated += Greet;
            RealtimeAPIWrapper.OnResponseCreated += MarkTurn;
            AudioRecorder.OnVADRecordingStarted += MarkTurn;
        }

        private void OnDisable()
        {
            RealtimeAPIWrapper.OnProblem -= HandleProblem;
            RealtimeAPIWrapper.OnSessionUpdated -= Greet;
            RealtimeAPIWrapper.OnResponseCreated -= MarkTurn;
            AudioRecorder.OnVADRecordingStarted -= MarkTurn;
        }

        /// <summary>Starts a conversation. In a free conversation the agent greets first and stops after a long silence.</summary>
        public void Begin(string instructions, bool isFree)
        {
            End();
            api.SystemInstructions = instructions;
            greets = guardsIdle = isFree;
            if (!api.HasApiKey)
            {
                SetState(ConversationState.NoKey);
                return;
            }

            isWanted = true;
            hasReconnected = false;
            recorder.listeningMode = pushToTalk ? ListeningMode.PushToTalk : ListeningMode.VAD;
            Connect();
        }

        /// <summary>Ends the conversation. Speech that was not played yet is dropped.</summary>
        public void End()
        {
            isWanted = isSuspended = forgotConversation = false;
            Problem = RealtimeProblem.None;
            Disconnect();
            SetState(ConversationState.Offline);
        }

        /// <summary>Interrupts the conversation. The connection stays open for a while unless it is given up at once.</summary>
        public void Suspend(bool keepConnection = true)
        {
            if (!isWanted || Problem != RealtimeProblem.None) return;

            if (!isSuspended)
            {
                isSuspended = true;
                suspendedFor = 0f;
                StopMicrophone();
                api.InterruptResponse();
                StopVoice();
            }
            if (!keepConnection) Disconnect();
        }

        /// <summary>Continues after Suspend, with a new connection when the old one is gone.</summary>
        public void Unsuspend()
        {
            if (!isSuspended) return;

            isSuspended = false;
            microphoneCheckIn = 0f;
            if (Problem != RealtimeProblem.None || api.State != RealtimeState.Disconnected) return;
            forgotConversation = true;
            Connect();
        }

        /// <summary>Starts again after a problem.</summary>
        public void Retry() => Begin(api.SystemInstructions, guardsIdle);

        /// <summary>The push-to-talk button was pressed or released.</summary>
        public void PressToTalk(bool pressed)
        {
            if (pressed == isPressed || (pressed && !CanPressToTalk)) return;

            isPressed = pressed;
            if (pressed) recorder.StartRecording();
            else recorder.StopRecording();
            MarkTurn();
        }

        private bool IsUserSpeaking => isPressed || recorder.IsVADRecording;

        private bool CanPressToTalk => pushToTalk && isWanted && !isSuspended && api.IsConnected && !recorder.Muted;

        private void Update()
        {
            recorder.Muted = narration != null && narration.isPlaying;
            if (isWanted && Problem == RealtimeProblem.None)
            {
                if (isSuspended) UpdateSuspended();
                else
                {
                    UpdateConnection();
                    UpdateMicrophone();
                    UpdateWaiting();
                }
            }
            SetState(CurrentState());
        }

        private ConversationState CurrentState()
        {
            if (Problem != RealtimeProblem.None) return ConversationState.Problem;
            if (!isWanted) return State == ConversationState.NoKey ? ConversationState.NoKey : ConversationState.Offline;
            if (isSuspended) return ConversationState.Suspended;
            if (!api.IsConnected || recorder.Muted) return ConversationState.Connecting;
            if (IsUserSpeaking) return ConversationState.UserSpeaking;
            if (player.IsAudioPlaying()) return ConversationState.AgentSpeaking;
            return api.IsResponsePending ? ConversationState.Thinking : ConversationState.Listening;
        }

        private void SetState(ConversationState state)
        {
            if (state == State) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        // Connection ------------------------------------------------------------------------------------------

        private void Connect()
        {
            wasConnected = false;
            reconnectIn = -1f;
            api.Connect();
        }

        private void Disconnect()
        {
            wasConnected = isGreetingPending = false;
            reconnectIn = -1f;
            StopMicrophone();
            api.Disconnect();
            StopVoice();
        }

        private void StopVoice()
        {
            player.CancelAudioPlayback();
            player.ResetCancelPending();
        }

        private void UpdateConnection()
        {
            if (reconnectIn > 0f)
            {
                reconnectIn -= Time.deltaTime;
                if (reconnectIn <= 0f) Connect();
                return;
            }

            // A connection can also end before it was seen here.
            if (api.IsConnected && !wasConnected) OnConnected();
            else if (api.State == RealtimeState.Disconnected) OnLost();

            if (isGreetingPending)
            {
                greetingIn -= Time.deltaTime;
                if (greetingIn <= 0f) Greet();
            }
        }

        private void OnConnected()
        {
            wasConnected = true;
            microphoneCheckIn = withoutMicrophone = idleFor = 0f;
            isGreetingPending = greets && !forgotConversation && !hasReconnected;
            greetingIn = GreetingDelay;
            if (forgotConversation) Notice?.Invoke(AgentDemoTexts.ForgotConversation);
            forgotConversation = false;
        }

        private void OnLost()
        {
            wasConnected = false;
            StopMicrophone();
            StopVoice();
            if (hasReconnected)
            {
                SetProblem(RealtimeProblem.ConnectionLost, "");
                return;
            }
            hasReconnected = true;
            reconnectIn = ReconnectDelay;
            Notice?.Invoke(AgentDemoTexts.Reconnecting);
        }

        private void UpdateSuspended()
        {
            suspendedFor += Time.deltaTime;
            if (suspendedFor > SuspendedLimit && api.State != RealtimeState.Disconnected) Disconnect();
        }

        /// <summary>The agent speaks first, unless the user was faster.</summary>
        private void Greet()
        {
            if (!isGreetingPending || !api.IsConnected) return;
            isGreetingPending = false;
            if (!IsUserSpeaking && !api.IsResponsePending) api.SendSystemNote(AgentDemoTexts.GreetingNote);
        }

        // Microphone ------------------------------------------------------------------------------------------

        /// <summary>Closes the microphone. What was recorded but not sent is dropped.</summary>
        private void StopMicrophone()
        {
            recorder.StopMicrophone();
            isPressed = false;
        }

        /// <summary>Keeps the microphone open in a hands-free conversation. Opening it fails while the permission is missing.</summary>
        private void UpdateMicrophone()
        {
            microphoneCheckIn -= Time.deltaTime;
            if (!api.IsConnected || microphoneCheckIn > 0f) return;

            microphoneCheckIn = MicrophoneInterval;
            MicrophoneState state = recorder.MicState;
            if (state != MicrophoneState.Ready)
            {
                withoutMicrophone += MicrophoneInterval;
                bool isDenied = state == MicrophoneState.WaitingForPermission;
                if (withoutMicrophone > (isDenied ? PermissionSeconds : MicrophoneInterval))
                    SetProblem(isDenied ? RealtimeProblem.MicDenied : RealtimeProblem.MicMissing, "");
                return;
            }

            withoutMicrophone = 0f;
            if (!pushToTalk && !recorder.IsMicrophoneOpen) recorder.StartMicrophone();
            if (recorder.HasWrongSampleRate && !sampleRateReported)
            {
                sampleRateReported = true;
                Notice?.Invoke(AgentDemoTexts.ProblemLine(RealtimeProblem.MicRate, Model, ""));
            }
        }

        private static float Loudness(float decibels) => Mathf.InverseLerp(-70f, -15f, decibels);

        // Turns -----------------------------------------------------------------------------------------------

        private void MarkTurn() => idleFor = 0f;

        private void UpdateWaiting()
        {
            thinkingFor = State == ConversationState.Thinking ? thinkingFor + Time.deltaTime : 0f;
            if (thinkingFor > ThinkingLimit)
            {
                api.ForgetPendingResponse();
                Notice?.Invoke(AgentDemoTexts.NoAnswer);
            }

            idleFor = State == ConversationState.Listening ? idleFor + Time.deltaTime : 0f;
            if (guardsIdle && idleFor > IdleLimit)
            {
                End();
                Notice?.Invoke(AgentDemoTexts.StoppedListening);
            }
        }

        // Problems --------------------------------------------------------------------------------------------

        private void HandleProblem(RealtimeProblem problem, string detail)
        {
            if (!isWanted || Problem != RealtimeProblem.None) return;

            // The conversation goes on after a single failed request.
            bool isPassing = problem == RealtimeProblem.Server || problem == RealtimeProblem.RateLimit;
            if (isPassing && api.IsConnected) Notice?.Invoke(AgentDemoTexts.ProblemLine(problem, Model, detail));
            else SetProblem(problem, detail);
        }

        private void SetProblem(RealtimeProblem problem, string detail)
        {
            Problem = problem;
            Disconnect();
            SetState(ConversationState.Problem);
            ProblemRaised?.Invoke(problem, detail);
        }
    }
}