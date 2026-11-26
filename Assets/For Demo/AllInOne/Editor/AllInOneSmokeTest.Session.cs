using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemoKit;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>Tests of the full session and of the way back to the menu.</summary>
    public static partial class AllInOneSmokeTest
    {
        /// <summary>Writes down what happens in a session.</summary>
        private sealed class SessionRecord : IDisposable
        {
            public readonly List<string> steps = new List<string>();
            public readonly Dictionary<string, AgentSessionScript.Board> boards = new Dictionary<string, AgentSessionScript.Board>();
            public readonly List<(string action, float time, string step)> actions = new List<(string, float, string)>();
            public readonly List<(int index, float remaining)> reminders = new List<(int, float)>();
            public float seconds;
            private int shownReminder = -1;

            public SessionRecord()
            {
                app.Director.StepChanged += AddStep;
                app.Presence.Requested += AddAction;
            }

            public void Dispose()
            {
                app.Director.StepChanged -= AddStep;
                app.Presence.Requested -= AddAction;
            }

            public void Sample(float longestTotal)
            {
                if (app.Boards.ShownReminder != shownReminder && app.Boards.ShownReminder >= 0)
                    reminders.Add((app.Boards.ShownReminder, app.Director.StepRemaining));
                shownReminder = app.Boards.ShownReminder;
                if (app.Director.IsRunning) seconds = longestTotal - app.Director.TotalRemaining;
            }

            private void AddStep()
            {
                steps.Add(app.Director.Step.id);
                boards[app.Director.Step.id] = app.Boards.Current;
            }

            private void AddAction(string action)
            {
                if (app.Director.Step != null) actions.Add((action, app.Director.StepElapsed, app.Director.Step.id));
            }
        }

        private static readonly string[] StepOrder = { "ready", "welcome", "warmup-board", "warmup-start", "warmup", "pause", "preparation", "debate", "end" };

        private static IEnumerator RunSession(SessionRecord record, float speed, Action<string> onStep = null)
        {
            float total = app.Director.TotalSeconds;
            app.Director.TestSpeed = speed;
            string step = "";
            float started = Time.realtimeSinceStartup;
            while (app.Director.IsRunning)
            {
                record.Sample(total);
                if (app.Director.Step.id != step)
                {
                    step = app.Director.Step.id;
                    onStep?.Invoke(step);
                }
                if (Time.realtimeSinceStartup - started > 240f)
                {
                    Check(false, "the session does not end");
                    break;
                }
                yield return null;
            }
            app.Director.TestSpeed = 1f;
            yield return null;
        }

        private static void CheckSteps(SessionRecord record)
        {
            Check(record.steps.SequenceEqual(StepOrder), "steps: " + string.Join(", ", record.steps));
            foreach (KeyValuePair<string, AgentSessionScript.Board> board in record.boards)
            {
                AgentSessionScript.Board expected = board.Key == "warmup-board" ? AgentSessionScript.Board.WarmUp
                    : board.Key == "preparation" ? AgentSessionScript.Board.Instruction : AgentSessionScript.Board.None;
                Check(board.Value == expected, board.Key + " shows the board " + board.Value);
            }
        }

        private static IEnumerator TestQuickSession()
        {
            yield return Boot();
            using (var server = new RealtimeMockServer())
            using (var record = new SessionRecord())
            {
                UseServer(server);
                yield return StartSession("work", SessionPace.Quick, MovementMode.Mimicry);
                Check(app.Switcher.Mode == MovementMode.Mimicry, "movement " + app.Switcher.Mode);
                CheckLayout(app.Session.Panel, "session");
                float total = app.Director.TotalSeconds;
                string stateAtEnd = "";
                yield return RunSession(record, 10f, step =>
                {
                    CheckLayout(app.Session.Panel, "session, " + step);
                    if (step == "end") stateAtEnd = app.Conversation.State.ToString();
                });

                CheckSteps(record);
                AgentTopic topic = app.Topics.First(t => t.id == "work");
                (string action, float time, string step)[] warmUp = record.actions.Where(a => a.step == "warmup").ToArray();
                Check(warmUp.Select(a => a.action).SequenceEqual(topic.warmUpActions), "warm-up: " + string.Join(", ", warmUp.Select(a => a.action)));
                float expected = 0f;
                foreach ((string action, float time, string step) in warmUp)
                {
                    Check(time >= expected && time - expected < 0.5f, action + " at " + time.ToString("0.0") + " s instead of " + expected.ToString("0.0") + " s");
                    expected += app.Presence.LengthOf(action) - WarmUpOverlap;
                }
                Check(server.Connections == 1 && server.Count("session.update") == 1, server.Connections + " connections");
                Check(server.Last("session.update") != null && (string)server.Last("session.update")["session"]["instructions"] == topic.instructions.text, "prompt of the topic");
                Check(server.Count("response.create") == 0, "the agent greets in a session");
                Check(record.reminders.Count == 1 && record.reminders[0].index == 1 && Mathf.Abs(record.reminders[0].remaining - 60f) < 1f, "reminders: " + string.Join(", ", record.reminders));
                Check(stateAtEnd == "Offline", "at the last narration the conversation is " + stateAtEnd);
                Check(Mathf.Abs(total - 203f) < 3f && Mathf.Abs(record.seconds - total) < 3f, "duration " + record.seconds + " of " + total + " s");
                Check(app.Phase == AppPhase.Finished && app.Dialogs.Current == app.Dialogs.Finished, "the session does not finish");
                yield return Wait(0.5f);
                yield return Click("finished/again");
                yield return WaitForTransition();
                Check(app.Phase == AppPhase.Session && app.Director.StepIndex == 0, "the session is not repeated");
                yield return BackToMenu();
            }
            UseNoKey();
        }

        private static IEnumerator TestStudySession()
        {
            yield return Boot();
            UseNoKey();
            yield return WaitFor(() => app.Switcher.Recorded.IsLoaded, 60f, "recordings are not loaded");
            using (var record = new SessionRecord())
            {
                yield return StartSession("social", SessionPace.Study, MovementMode.Noise);
                Check(app.Switcher.Mode == MovementMode.Noise && app.Switcher.Recorded.Elapsed < 3f, "the recording is at " + app.Switcher.Recorded.Elapsed);
                float total = app.Director.TotalSeconds;
                string stateInDebate = "";
                bool gestures = false;
                yield return RunSession(record, 30f, step =>
                {
                    if (step != "debate") return;
                    stateInDebate = StatusText("session/state");
                    gestures = app.Presence.OfflineGestures;
                });
                CheckSteps(record);
                Check(stateInDebate == "Cannot talk without a key" && gestures, "debate without a key: " + stateInDebate);
                Check(record.reminders.Select(r => r.index).SequenceEqual(new[] { 0, 1 }) &&
                      Mathf.Abs(record.reminders[0].remaining - 180f) < 1.5f && Mathf.Abs(record.reminders[1].remaining - 60f) < 1.5f, "reminders: " + string.Join(", ", record.reminders));
                Check(Mathf.Abs(total - 466f) < 3f && Mathf.Abs(record.seconds - total) < 3f, "duration " + record.seconds + " of " + total + " s");
                Check(app.Phase == AppPhase.Finished, "the session does not finish");
                yield return BackToMenu();
            }
        }

        private static IEnumerator TestSessionControl()
        {
            yield return Boot();
            using (var server = new RealtimeMockServer { CloseDelayMilliseconds = 1500 })
            {
                UseServer(server);
                SessionDirector director = app.Director;
                yield return StartSession("growth", SessionPace.Quick, MovementMode.Leading);

                // Pause and continue during a narration
                yield return Click("session/next-step");
                yield return Wait(1f);
                yield return Click("session/pause");
                float elapsed = director.StepElapsed;
                var narration = director.GetComponentInChildren<AudioSource>();
                yield return Wait(5f);
                Check(director.IsPaused && Mathf.Approximately(director.StepElapsed, elapsed) && !narration.isPlaying, "the session goes on during a pause");
                yield return Click("session/pause");
                yield return Wait(1f);
                Check(!director.IsPaused && director.StepElapsed > elapsed, "the session does not continue");

                // Leaving asks, pauses, and hides the board
                yield return Click("session/next-step");
                yield return Wait(0.5f);
                Check(director.Step.id == "warmup-board" && app.Boards.Shown != null, "no warm-up board");
                CheckNoOverlap("warm-up board");
                yield return Click("session/menu");
                Check(app.Dialogs.Current == app.Dialogs.Confirm && director.IsPaused && app.Boards.Shown == null, "the question does not pause or hide the board");
                yield return Wait(0.3f);
                yield return Click("confirm/stay");
                yield return Wait(0.3f);
                Check(!director.IsPaused && app.Boards.Shown != null && app.Dialogs.Current == null, "the session does not go on after Stay");
                yield return Click("session/pause");
                yield return Click("session/menu");
                yield return Wait(0.3f);
                yield return Click("confirm/stay");
                Check(director.IsPaused, "Stay ends a pause of the user");

                // Next step from the warm-up: both bodies go back to idle
                yield return Click("session/next-step");
                yield return Click("session/next-step");
                Check(director.Step.id == "warmup" && !director.IsPaused, "step " + director.Step.id);
                yield return Wait(1.5f);
                director.Pause();
                yield return Wait(0.2f);
                Check(app.Presence.Capture.speed == 0f && app.Presence.Body.speed == 0f, "the agent moves during a pause");
                director.Resume();
                Check(app.Presence.Capture.speed == 1f, "the agent stays frozen");
                yield return Click("session/next-step");
                yield return Wait(1f);
                Check(IsIn(app.Presence.Capture, "Idle") && IsIn(app.Presence.Body, "Idle"), "the agent does not return to idle");

                yield return TestRestartDuringConversation(server);
                yield return BackToMenu();
                yield return Wait(0.5f);
                Check(!director.IsRunning && app.Boards.Shown == null && !app.Conversation.IsWanted, "the session does not stop");
            }
            UseNoKey();
        }

        /// <summary>The late end of the first connection must not touch the second one.</summary>
        private static IEnumerator TestRestartDuringConversation(RealtimeMockServer server)
        {
            Time.captureFramerate = 0;
            SessionDirector director = app.Director;
            while (director.Step.id != "debate") yield return Click("session/next-step");
            yield return WaitFor(() => app.Conversation.IsConnected, 8f, "the debate is not connected");
            Check(server.Connections == 1, server.Connections + " connections");
            app.RestartSession();
            yield return WaitForTransition();
            Check(director.StepIndex == 0, "restart");
            while (director.Step.id != "debate") yield return Click("session/next-step");
            yield return WaitFor(() => app.Conversation.IsConnected && server.Connections == 2, 8f, "no second connection");
            yield return Wait(3f);
            Check(app.Conversation.IsConnected && server.Connections == 2 && app.Dialogs.Current == null, "the second connection was disturbed");

            // A pause keeps the connection
            yield return Click("session/pause");
            yield return Wait(0.5f);
            Check(app.Conversation.State == ConversationState.Suspended && app.Conversation.IsConnected, "pause: " + app.Conversation.State);
            yield return Click("session/pause");
            yield return Wait(0.5f);
            Check(app.Conversation.IsConnected && server.Connections == 2, "after the pause");

            yield return Click("session/interrupt");
            Check(!app.Conversation.InterruptAgent, "interrupt switch of the session");
            yield return Click("session/interrupt");

            // The agent speaks when the time is up: her caption goes, the one of the last narration stays.
            server.Send(Created("s1"));
            for (int i = 0; i < 8; i++) server.Send(Audio("s1", "j1", 0.5f));
            server.Send(Transcript("s1", "I do not agree with that."));
            yield return WaitForState(ConversationState.AgentSpeaking);
            yield return Wait(0.5f);
            Check(app.Captions.Bar.Text.Contains("agree"), "caption of the agent: " + app.Captions.Bar.Text);
            app.Dialogs.OpenProblem(RealtimeProblem.Server, "A text of the service.");
            yield return Click("session/next-step");
            yield return Wait(2f);
            Check(app.Dialogs.Current == null, "a problem of the conversation stays after its end");
            Check(app.Captions.Bar.Text.StartsWith("Time's up"), "caption of the last narration: " + app.Captions.Bar.Text);
            Check(!Api.audioPlayer.IsAudioPlaying(), "the agent speaks after the end of the debate");
            Time.captureFramerate = MenuRate;
        }

        private static IEnumerator TestBackToMenu()
        {
            yield return Boot();
            UseNoKey();
            int roots = 0;
            long memory = 0;
            for (int round = 0; round < 5; round++)
            {
                yield return StartFree(MovementMode.Mimicry);
                yield return Wait(0.5f);
                yield return Click("talk/menu");
                yield return WaitForTransition();
                yield return StartSession("environment", SessionPace.Quick, MovementMode.Noise);
                yield return Click("session/next-step");
                yield return Click("session/next-step");
                yield return Click("session/menu");
                yield return Wait(0.3f);
                yield return Click("confirm/leave");
                yield return WaitForTransition();
                yield return StartSession("work", SessionPace.Quick, MovementMode.Leading);
                while (app.Director.IsRunning) app.Director.Next();
                yield return Wait(0.3f);
                Check(app.Phase == AppPhase.Finished, "phase " + app.Phase);
                yield return Click("finished/menu");
                yield return WaitForTransition();
                Check(app.Phase == AppPhase.Menu && app.Menu.Panel.IsShown && app.Dialogs.Current == null && !app.Presence.Visible, "menu in round " + round);

                int newRoots = SceneManager.GetActiveScene().rootCount;
                long newMemory = GC.GetTotalMemory(true);
                if (round == 1)
                {
                    roots = newRoots;
                    memory = newMemory;
                }
                else if (round > 1) Check(newRoots == roots, newRoots + " root objects instead of " + roots);
                if (round == 4)
                {
                    Check(newMemory - memory < 30 << 20, "memory grows by " + (newMemory - memory) / 1048576 + " MB");
                    Note("memory " + (newMemory - memory) / 1024 + " KB in three rounds");
                }
            }
        }

        private static IEnumerator TestIdle()
        {
            yield return Boot();
            yield return BackToMenu();
            yield return GoToPage(StartPanel.AvatarPage);
            Transform hand = Bone(app.Presence.Capture.gameObject, "RightHand");
            for (int part = 0; part < 4; part++)
            {
                Vector3 low = hand.position, high = hand.position;
                for (float t = 0f; t < 10f; t += Time.deltaTime)
                {
                    low = Vector3.Min(low, hand.position);
                    high = Vector3.Max(high, hand.position);
                    yield return null;
                }
                Check((high - low).magnitude > 0.01f, "the agent moves " + Centimetres((high - low).magnitude) + " in 10 s");
            }
        }
    }
}
