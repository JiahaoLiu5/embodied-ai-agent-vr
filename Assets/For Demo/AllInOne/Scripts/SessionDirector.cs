using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// Runs the full session: narration, boards, warm-up, preparation, and the timed conversation. One clock drives
    /// everything, so the session can be paused, skipped forward, and repeated.
    /// </summary>
    public sealed class SessionDirector : MonoBehaviour
    {
        // A movement starts this long before the previous one has ended, as in the study.
        private const float WarmUpOverlap = 0.2f;
        private const float LongestConnectionWait = 10f;
        private const float ReminderMargin = 10f;

        [SerializeField] private AgentSessionScript script;
        [SerializeField] private AudioSource narration;
        [SerializeField] private BoardPresenter boards;
        [SerializeField] private AgentPresence presence;
        [SerializeField] private ConversationController conversation;
        [SerializeField] private ConditionSwitcher switcher;

        private readonly List<float> seconds = new List<float>();
        private readonly List<float> actionStarts = new List<float>();
        private AgentTopic topic;
        private SessionPace pace;
        private int nextAction, nextReminder;
        private float narrationElapsed, connectionWait, reminderLeft;
        private bool isWaitingForConnection;

        public AgentSessionScript Script => script;

        public int StepCount => script.steps.Length;

        /// <summary>The current step, counted from 0, or -1.</summary>
        public int StepIndex { get; private set; } = -1;

        public AgentSessionScript.Step Step => StepIndex >= 0 && StepIndex < StepCount ? script.steps[StepIndex] : null;

        public float StepElapsed { get; private set; }

        public float StepSeconds => Step != null ? seconds[StepIndex] : 0f;

        public float StepRemaining => Mathf.Max(0f, StepSeconds - StepElapsed);

        /// <summary>The time that is left in this step and all later ones.</summary>
        public float TotalRemaining
        {
            get
            {
                float sum = StepRemaining;
                for (int i = StepIndex + 1; i < seconds.Count; i++) sum += seconds[i];
                return sum;
            }
        }

        public float TotalSeconds { get; private set; }

        public bool IsRunning { get; private set; }

        public bool IsPaused { get; private set; }

        /// <summary>True from the start of the conversation to the end of the session.</summary>
        public bool IsConversationReached => Step != null && (Step.kind == AgentSessionScript.Kind.Conversation || StepIndex > ConversationIndex);

        public CaptionService Captions { get; set; }

        /// <summary>For automated tests: the session clock runs this many times faster.</summary>
        internal float TestSpeed { get; set; } = 1f;

        public event Action StepChanged;
        public event Action Finished;
        public event Action<bool> PausedChanged;

        private int ConversationIndex => Array.FindIndex(script.steps, s => s.kind == AgentSessionScript.Kind.Conversation);

        public void Begin(AgentTopic newTopic, SessionPace newPace)
        {
            Stop();
            topic = newTopic;
            pace = newPace;
            MeasureSteps();
            foreach (AgentSessionScript.Step step in script.steps)
                if (step.clip != null) step.clip.LoadAudioData();
            boards.Prepare(topic, script);

            // The recording of the Noise mode starts with the session, as in the study.
            switcher.NoiseStart = RecordedStart.Session;
            if (switcher.Mode == MovementMode.Noise) switcher.Recorded.Play(RecordedStart.Session);

            IsRunning = true;
            Enter(0);
        }

        public void Pause()
        {
            if (!IsRunning || IsPaused) return;

            IsPaused = true;
            narration.Pause();
            switcher.Recorded.Paused = true;
            presence.Frozen = Step.kind == AgentSessionScript.Kind.WarmUp || presence.OfflineGestures;
            conversation.Suspend();
            PausedChanged?.Invoke(true);
        }

        public void Resume()
        {
            if (!IsRunning || !IsPaused) return;

            IsPaused = false;
            narration.UnPause();
            switcher.Recorded.Paused = false;
            presence.Frozen = false;
            conversation.Unsuspend();
            PausedChanged?.Invoke(false);
        }

        /// <summary>Ends the current step now.</summary>
        public void Next()
        {
            if (!IsRunning) return;
            if (Step.kind == AgentSessionScript.Kind.Conversation) conversation.End();
            Resume();
            Enter(StepIndex + 1);
        }

        /// <summary>Starts again with the same choices.</summary>
        public void Restart()
        {
            if (topic != null) Begin(topic, pace);
        }

        /// <summary>Ends the session. Does nothing when none is running.</summary>
        public void Stop()
        {
            conversation.End();
            Resume();
            IsRunning = isWaitingForConnection = false;
            StepIndex = -1;
            StepElapsed = 0f;
            narration.Stop();
            Captions?.Clear();
            boards.Clear();
            presence.OfflineGestures = false;
            switcher.NoiseStart = RecordedStart.Conversation;
        }

        private void Update()
        {
            if (!IsRunning || IsPaused) return;

            float step = Time.deltaTime * TestSpeed;
            narrationElapsed += step;
            UpdateReminder(step);
            if (isWaitingForConnection && !IsConnectionDecided(step)) return;

            StepElapsed += step;
            RunStep();
            while (IsRunning && StepElapsed >= StepSeconds)
            {
                // Time that is left over belongs to the next step, so the session does not drift.
                float rest = StepElapsed - StepSeconds;
                Enter(StepIndex + 1);
                if (!IsRunning || isWaitingForConnection) break;
                StepElapsed = rest;
                RunStep();
            }
        }

        private void Enter(int index)
        {
            LeaveStep();
            if (index >= StepCount)
            {
                Stop();
                Finished?.Invoke();
                return;
            }

            StepIndex = index;
            StepElapsed = 0f;
            boards.Show(Step.board);
            StartNarration(Step);
            nextAction = nextReminder = 0;
            if (Step.kind == AgentSessionScript.Kind.Conversation) StartConversation();
            StepChanged?.Invoke();
        }

        private void LeaveStep()
        {
            if (Step == null) return;

            narration.Stop();
            Captions?.Clear();
            ShowReminder(-1);
            if (Step.kind == AgentSessionScript.Kind.WarmUp) presence.ReturnToIdle();
            if (Step.kind == AgentSessionScript.Kind.Conversation)
            {
                isWaitingForConnection = false;
                conversation.End();
                presence.OfflineGestures = false;
            }
        }

        private void StartNarration(AgentSessionScript.Step step)
        {
            narrationElapsed = 0f;
            if (step.clip == null) return;
            narration.clip = step.clip;
            narration.Play();
            Captions?.Narrate(step.caption, step.clip.length, () => narrationElapsed);
        }

        private void StartConversation()
        {
            conversation.Begin(topic.instructions.text, false);
            connectionWait = 0f;
            isWaitingForConnection = conversation.IsWanted;
            presence.OfflineGestures = !isWaitingForConnection;
        }

        /// <summary>The clock of the conversation starts when it is connected, has failed, or takes too long.</summary>
        private bool IsConnectionDecided(float step)
        {
            connectionWait += step;
            bool isOver = conversation.IsConnected || !conversation.IsWanted || conversation.Problem != RealtimeProblem.None ||
                          connectionWait > LongestConnectionWait;
            if (isOver) isWaitingForConnection = false;
            return isOver;
        }

        private void RunStep()
        {
            switch (Step.kind)
            {
                case AgentSessionScript.Kind.WarmUp:
                    while (nextAction < topic.warmUpActions.Length && StepElapsed >= actionStarts[nextAction])
                        presence.Play(topic.warmUpActions[nextAction++]);
                    break;

                case AgentSessionScript.Kind.Conversation:
                    while (nextReminder < script.reminders.Length && StepRemaining <= script.reminders[nextReminder])
                    {
                        if (script.reminders[nextReminder] <= StepSeconds - ReminderMargin) ShowReminder(nextReminder);
                        nextReminder++;
                    }
                    break;
            }
        }

        private void ShowReminder(int index)
        {
            reminderLeft = index >= 0 ? script.reminderSeconds : 0f;
            boards.ShowReminder(index);
        }

        private void UpdateReminder(float step)
        {
            if (reminderLeft <= 0f) return;
            reminderLeft -= step;
            if (reminderLeft <= 0f) ShowReminder(-1);
        }

        private void MeasureSteps()
        {
            actionStarts.Clear();
            float warmUp = 0f;
            foreach (string action in topic.warmUpActions)
            {
                actionStarts.Add(warmUp);
                warmUp += Mathf.Max(0f, presence.LengthOf(action) - WarmUpOverlap);
            }

            seconds.Clear();
            TotalSeconds = 0f;
            foreach (AgentSessionScript.Step step in script.steps)
            {
                float length = step.kind == AgentSessionScript.Kind.WarmUp ? warmUp
                    : step.kind == AgentSessionScript.Kind.ClipLength && step.clip != null ? step.clip.length
                    : step.Seconds(pace);
                seconds.Add(length);
                TotalSeconds += length;
            }
        }

        private void OnApplicationPause(bool isPausedBySystem)
        {
            // The system may close the connection of a paused app without notice.
            if (!isPausedBySystem || !IsRunning) return;
            conversation.Suspend(false);
            Pause();
        }
    }
}