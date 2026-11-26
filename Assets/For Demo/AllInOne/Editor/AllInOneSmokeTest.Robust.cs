using System.Collections;
using System.Linq;
using DemoKit;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>Tests with hurried users, bad connections, a low frame rate, and a scene that is loaded again.</summary>
    public static partial class AllInOneSmokeTest
    {
        private const int LowRate = 20;

        // Hurried users ---------------------------------------------------------------------------------------

        private static IEnumerator TestHurriedUser()
        {
            yield return Boot();
            UseNoKey();
            yield return BackToMenu();
            yield return HurryAvatars();
            yield return HurryFree();
            yield return HurrySession();
            yield return BackToMenu();
        }

        /// <summary>Many choices in one frame: the last one counts.</summary>
        private static IEnumerator HurryAvatars()
        {
            yield return GoToPage(StartPanel.AvatarPage);
            foreach (int number in new[] { 2, 6, 1, 4, 3, 5 }) DemoUi.Click("start/avatar.avatar-" + number);
            yield return Frames(12);
            AgentAvatarCatalog.Entry avatar = app.Catalog.Find("avatar-5");
            CheckBody(app.Body, avatar, "body after quick choices");
            CheckBody(app.Figure.Body, avatar, "figure after quick choices");
        }

        private static IEnumerator HurryFree()
        {
            app.Choices.IsSession = false;
            app.ChooseMode(MovementMode.Independent);
            yield return GoToPage(StartPanel.ReadyPage);
            DemoUi.Click("start/go");
            DemoUi.Click("start/go");
            DemoUi.Click("start/back");
            yield return WaitForTransition();
            Check(app.Phase == AppPhase.Free && app.Talk.Panel.IsShown && !app.Menu.Panel.IsShown && !app.Figure.IsShown, "start, pressed twice together with Back");
            Check(app.Presence.Visible && app.Switcher.Mode == MovementMode.Independent, "the agent after a hurried start");

            DemoUi.Click("talk/menu");
            DemoUi.Click("talk/menu");
            DemoUi.Click("talk/selfview");
            DemoUi.Click("talk/mode.noise");
            yield return WaitForTransition();
            yield return Wait(1f);
            Check(app.Phase == AppPhase.Menu && app.Menu.Panel.IsShown && !app.Talk.Panel.IsShown && !app.Figure.IsShown, "menu, pressed twice together with Self view");
            Check(app.Switcher.Mode == MovementMode.Independent && !app.Switcher.IsSwitching, "movement in the menu: " + app.Switcher.Mode);
            app.ChooseMode(MovementMode.Independent);

            // The view from the side does not stop a transition.
            app.Rig.ToggleObserver();
            app.Begin();
            yield return WaitForTransition();
            Check(app.Phase == AppPhase.Free, "start with the view from the side");
            app.LeaveAtOnce();
            yield return WaitForTransition();
            app.Rig.ToggleObserver();
            Check(app.Phase == AppPhase.Menu && app.Rig.ViewCamera.enabled, "the view from the side stays");
        }

        private static IEnumerator HurrySession()
        {
            yield return StartSession("social", SessionPace.Quick, MovementMode.Noise);
            DemoUi.Click("session/restart");
            DemoUi.Click("session/restart");
            DemoUi.Click("session/menu");
            yield return WaitForTransition();
            Check(app.Phase == AppPhase.Session && app.Director.IsRunning && !app.Director.IsPaused && app.Dialogs.Current == null, "restart together with Back to menu");

            // The session ends while it is asked whether to leave it.
            yield return Click("session/menu");
            while (app.Director.IsRunning) app.Director.Next();
            yield return Wait(0.3f);
            Check(app.Phase == AppPhase.Finished && app.Dialogs.Current == app.Dialogs.Finished, "the end of a session with an open question");
            DemoUi.Click("finished/again");
            DemoUi.Click("finished/menu");
            yield return WaitForTransition();
            Check(app.Phase == AppPhase.Session && app.Director.IsRunning && !app.Director.IsPaused, "repeat together with Back to menu");

            // A pause of the user survives a dialog, also after the question of before.
            yield return Click("session/pause");
            app.Dialogs.OpenProblem(RealtimeProblem.Server, "A text of the service.");
            yield return Wait(0.3f);
            app.Dialogs.CloseAll();
            Check(app.Director.IsPaused, "a dialog ends a pause of the user");

            // Without a key the agent gestures in the debate, and stands still in a pause.
            yield return Next("debate");
            yield return Wait(3f);
            yield return Click("session/pause");
            yield return null;
            Transform hand = Bone(app.Presence.Capture.gameObject, "RightHand");
            Vector3 before = hand.position;
            yield return Wait(6f);
            Check(Vector3.Distance(before, hand.position) < 0.001f, "the agent gestures in a pause");
            yield return Click("session/pause");
            Check(app.Presence.Capture.speed == 1f, "the agent stays frozen after a pause");
        }

        // Bad connections -------------------------------------------------------------------------------------

        private static IEnumerator TestBadConnections()
        {
            yield return Boot();
            Time.captureFramerate = 0;
            using (var server = new RealtimeMockServer { ClosesAtOnce = true })
            {
                UseServer(server);
                yield return ConnectionThatEndsAtOnce(server);
                yield return MicrophoneMeter();
                yield return BackToMenu();
                yield return ProblemInPause(server);
                yield return HeadsetTakenOff(server);
                yield return BackToMenu();
            }
            UseNoKey();
            Time.captureFramerate = MenuRate;
        }

        private static IEnumerator ConnectionThatEndsAtOnce(RealtimeMockServer server)
        {
            yield return StartFree();
            yield return WaitFor(() => app.Conversation.State == ConversationState.Problem, 12f, "a connection that ends at once is not noticed: " + app.Conversation.State);
            Check(app.Conversation.Problem == RealtimeProblem.ConnectionLost && server.Connections == 2, app.Conversation.Problem + " after " + server.Connections + " connections");
            Check(app.Dialogs.Current == app.Dialogs.Problem, "no dialog for a connection that ends at once");
            yield return Click("talk/talk");
            Check(app.Dialogs.Current == null && app.Conversation.State == ConversationState.Offline, "Stop talking leaves the problem open");
            server.ClosesAtOnce = false;
            yield return Click("talk/talk");
            yield return WaitForState(ConversationState.Thinking, 8f);
            server.Send(Created("b1"));
            server.Send(Audio("b1", "c1", 0.3f));
            server.Send(Done("b1"));
            yield return WaitForState(ConversationState.AgentSpeaking);
            yield return WaitForState(ConversationState.Listening);
        }

        /// <summary>The meter moves with the voice, and is empty when the microphone is closed.</summary>
        private static IEnumerator MicrophoneMeter()
        {
            DemoProgressBar meter = DemoUi.Get<DemoProgressBar>("talk/level");
            yield return Speak(0.5f, 0f);
            Check(app.Conversation.Level > 0.5f && meter.Amount > 0.5f, "meter while speaking: " + meter.Amount);
            yield return Click("talk/talk");
            yield return Frames(2);
            Check(app.Conversation.Level == 0f && meter.Amount < 0.01f && app.Conversation.State == ConversationState.Offline, "meter after Stop talking: " + meter.Amount);
        }

        /// <summary>A problem during a pause waits for the user and does not connect again by itself.</summary>
        private static IEnumerator ProblemInPause(RealtimeMockServer server)
        {
            yield return StartSession("growth", SessionPace.Quick, MovementMode.Independent);
            yield return Next("debate");
            yield return WaitFor(() => app.Conversation.IsConnected, 8f, "the debate is not connected");
            yield return WaitFor(() => server.Count("session.update") > 0, 5f, "no session.update");
            yield return Click("session/pause");
            int connections = server.Connections;
            server.Send(new JObject { ["type"] = "error", ["error"] = new JObject { ["code"] = "session_expired", ["message"] = "The session has expired." } });
            yield return WaitFor(() => app.Conversation.Problem == RealtimeProblem.SessionEnded, 4f, "a problem in a pause is not noticed");
            Check(app.Dialogs.Current == app.Dialogs.Problem, "no dialog for a problem in a pause");
            yield return Click("session/pause");
            yield return Wait(1f);
            Check(!app.Director.IsPaused && server.Connections == connections && !app.Conversation.IsConnected, "a problem in a pause connects again by itself");
            yield return Click("problem/continue");
            Check(app.Dialogs.Current == null && app.Presence.OfflineGestures && app.Conversation.State == ConversationState.Offline, "continue without voice");
        }

        /// <summary>A headset that is taken off in the debate pauses the session and closes the connection.</summary>
        private static IEnumerator HeadsetTakenOff(RealtimeMockServer server)
        {
            app.RestartSession();
            yield return WaitForTransition();
            yield return Next("debate");
            yield return WaitFor(() => app.Conversation.IsConnected, 8f, "the debate is not connected");
            int connections = server.Connections;
            app.SendMessage("OnApplicationPause", true);
            yield return Wait(0.3f);
            app.SendMessage("OnApplicationPause", false);
            yield return Wait(1f);
            Check(app.Director.IsPaused && !app.Conversation.IsConnected && server.Connections == connections, "the session goes on by itself after a break");
            yield return Click("session/pause");
            yield return WaitFor(() => app.Conversation.IsConnected && server.Connections == connections + 1, 8f, "no connection after a break");
            yield return Wait(0.3f);
            Check(DemoToast.Current == AgentDemoTexts.ForgotConversation, "no message about the break: " + DemoToast.Current);
            yield return Click("session/next-step");
            yield return Wait(0.5f);
            Check(!app.Conversation.IsConnected && app.Director.Step.id == "end", "the end of the debate after a break");
        }

        // Low frame rate --------------------------------------------------------------------------------------

        /// <summary>With few frames per second movement is only as much larger per frame as a frame is longer.</summary>
        private static IEnumerator TestLowFrameRate()
        {
            yield return Boot();
            UseNoKey();
            yield return BackToMenu();
            Time.captureFramerate = LowRate;
            float factor = MovementRate / (float)LowRate;
            yield return GoToPage(StartPanel.MovementPage);
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Talk;
            yield return WaitFor(() => app.Switcher.Recorded.IsLoaded, 60f, "recordings are not loaded");
            yield return Wait(1f);

            var user = new StepMeter(IkTargets(app.Body.gameObject));
            var agent = new StepMeter(IkTargets(app.Presence.Body.gameObject));
            foreach (string mode in new[] { "leading", "mimicry", "noise", "leading", "independent" })
            {
                user.Reset();
                agent.Reset();
                yield return Click("start/movement." + mode);
                for (float t = 0f; t < 2.5f || app.Switcher.IsSwitching; t += Time.deltaTime)
                {
                    user.Sample();
                    agent.Sample();
                    yield return null;
                }
                Check(user.LargestStep < 0.015f * factor, mode + ": the user's avatar moves " + Centimetres(user.LargestStep) + " in a frame");
                Check(agent.LargestStep < 0.05f * factor, mode + ": the agent moves " + Centimetres(agent.LargestStep) + " in a frame");
                CheckEndState(mode);
                Note(mode + ": user " + Centimetres(user.LargestStep) + ", agent " + Centimetres(agent.LargestStep) + " per frame");
            }
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Idle;
            yield return LowRateSession();
        }

        /// <summary>The clock of the session does not drift with long frames.</summary>
        private static IEnumerator LowRateSession()
        {
            using (var record = new SessionRecord())
            {
                yield return StartSession("environment", SessionPace.Quick, MovementMode.Leading);
                float total = app.Director.TotalSeconds, started = Time.time, before = total - app.Director.TotalRemaining;
                yield return RunSession(record, 8f);
                CheckSteps(record);
                float expected = (total - before) / 8f;
                Check(Mathf.Abs(Time.time - started - expected) < 0.5f, "duration " + (Time.time - started).ToString("0.0") + " s instead of " + expected.ToString("0.0") + " s");
                yield return BackToMenu();
            }
        }

        // The scene is loaded again ---------------------------------------------------------------------------

        private static IEnumerator TestReload()
        {
            yield return Boot();
            for (int round = 0; round < 2; round++)
            {
                yield return round == 0 ? StartFree(MovementMode.Leading) : StartSession("work", SessionPace.Quick, MovementMode.Noise);
                app = null;
                SceneManager.LoadScene(AllInOnePaths.Scene);
                yield return Frames(3);
                yield return Boot();
                Check(DemoInput.Pointers.Count == 1, DemoInput.Pointers.Count + " pointers");
                Check(DemoUi.ShownPanels.Count(p => !p.Id.StartsWith("toast")) == 1 && app.Menu.Panel.IsShown, "panels: " + string.Join(", ", DemoUi.ShownPanels.Select(p => p.Id)));
            }

            // Everything answers once.
            Time.captureFramerate = 0;
            using (var server = new RealtimeMockServer())
            {
                UseServer(server);
                yield return StartFree();
                yield return WaitFor(() => server.Count("response.create") > 0, 8f, "no greeting");
                server.Send(Created("n1"));
                server.Send(Done("n1"));
                yield return WaitForState(ConversationState.Listening);
                Check(server.Count("response.create") == 1 && server.Connections == 1, server.Count("response.create") + " greetings");
                server.Forget();
                yield return Speak(0.8f);
                yield return WaitFor(() => server.Count("response.create") > 0, 5f, "no request after an utterance");
                yield return Wait(0.3f);
                Check(server.Count("response.create") == 1 && server.Count("conversation.item.create") == 1, "the utterance was sent " + server.Count("conversation.item.create") + " times");
                yield return BackToMenu();
            }
            UseNoKey();
            Time.captureFramerate = MenuRate;
        }
    }
}
