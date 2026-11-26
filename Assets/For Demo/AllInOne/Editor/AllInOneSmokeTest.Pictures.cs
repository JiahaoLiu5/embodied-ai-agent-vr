using System.Collections;
using DemoKit;
using UnityEngine;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>Takes pictures of all panels and phases for a look by a person.</summary>
    public static partial class AllInOneSmokeTest
    {
        private static readonly Vector3 SidePlace = new Vector3(2.05f, 1.5f, 0.35f);
        private static readonly Vector3 SideTarget = new Vector3(0f, 1.22f, 0f);
        private const float SideView = 46f;

        // Behind the user's shoulder
        private static readonly Vector3 ShoulderPlace = new Vector3(-0.95f, 2.45f, 1.7f);
        private static readonly Vector3 ShoulderTarget = new Vector3(0.28f, 1.3f, -0.1f);
        private const float ShoulderView = 50f;

        private static IEnumerator TakePictures()
        {
            yield return Boot();
            UseNoKey();
            DemoToast.Clear();
            yield return BackToMenu();
            yield return PictureMenu();
            yield return PictureModes();
            yield return PictureFree();
            yield return PictureSession();
            yield return PictureDialogs();
        }

        private static IEnumerator PictureMenu()
        {
            yield return GoToPage(StartPanel.AvatarPage);
            yield return Click("start/avatar.avatar-4");
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.RaiseLeft;
            yield return Wait(1.5f);
            Shot("start_1_avatar");
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Idle;

            yield return GoToPage(StartPanel.MovementPage);
            yield return Click("start/movement.leading");
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Talk;
            yield return Wait(4f);
            Shot("start_2_movement");
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Idle;
            yield return GoToPage(StartPanel.ExperiencePage);
            yield return Click("start/experience.free");
            yield return Wait(1.5f);
            Shot("start_3_experience_free");
            yield return Click("start/experience.session");
            yield return Click("start/topic.work");
            yield return Click("start/pace.study-timing");
            yield return Wait(1f);
            Shot("start_3_experience_session");
            yield return GoToPage(StartPanel.ReadyPage);
            yield return Wait(0.6f);
            Shot("start_4_ready_no_key");
            RealtimeAPIWrapper.KeyOverride = () => "mock";
            app.Menu.RefreshSummary();
            yield return Wait(0.5f);
            Shot("start_4_ready_key");
            UseNoKey();
        }

        /// <summary>The same poses in all four modes: the user raises a hand, the agent stands still.</summary>
        private static IEnumerator PictureModes()
        {
            yield return StartFree();
            yield return Wait(2.5f);
            Shot("free_no_key");
            app.Presence.OfflineGestures = false;
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.RaiseRight;
            foreach (string mode in new[] { "independent", "leading", "mimicry", "noise" })
            {
                yield return Click("talk/mode." + mode);
                yield return Wait(4f);
                ShotFrom("mode_" + mode, SidePlace, SideTarget, SideView);
            }
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Idle;
            yield return Click("talk/mode.independent");
        }

        private static IEnumerator PictureFree()
        {
            Time.captureFramerate = 0;
            using (var server = new RealtimeMockServer())
            {
                UseServer(server);
                yield return StartFree(MovementMode.Mimicry);
                app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Talk;
                yield return WaitFor(() => app.Conversation.IsConnected, 8f, "not connected");
                yield return Click("talk/selfview");
                yield return Wait(1.5f);
                server.Send(Created("p1"));
                for (int i = 0; i < 12; i++) server.Send(Audio("p1", "q1", 0.5f));
                server.Send(Transcript("p1", "Hello! I am Amy. What would you like to talk about today?"));
                server.Send(Call("p1", "c1", "Waving", "Talking (3)"));
                server.Send(Done("p1"));
                yield return WaitForState(ConversationState.AgentSpeaking);
                yield return Wait(1.6f);
                Shot("free_speaking");
                app.Rig.TurnTo(186f);
                yield return Wait(1.2f);
                Shot("free_self_view");
                ShotFrom("free_side", SidePlace, SideTarget, SideView);
                ShotFrom("free_overview", ShoulderPlace, ShoulderTarget, ShoulderView, true);
                yield return BackToMenu();
            }
            UseNoKey();
            Time.captureFramerate = MenuRate;
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Idle;
        }

        private static IEnumerator PictureSession()
        {
            yield return StartSession("work", SessionPace.Study, MovementMode.Leading);
            yield return Next("welcome");
            yield return Wait(3f);
            Shot("session_1_welcome");
            yield return Next("warmup-board");
            yield return Wait(4f);
            Shot("session_2_warmup_board");
            app.Rig.TurnTo(180f);
            yield return Wait(1.5f);
            Shot("session_2_warmup_board_front");
            ShotFrom("session_2_warmup_board_overview", ShoulderPlace, ShoulderTarget, ShoulderView, true);
            CheckNoOverlap("warm-up board from the front");
            yield return Next("warmup");
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.RaiseRight;
            yield return Wait(2.2f);
            Shot("session_4_warmup");
            ShotFrom("session_4_warmup_side", SidePlace, SideTarget, SideView);
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Idle;
            yield return Next("preparation");
            yield return Wait(5f);
            Shot("session_6_preparation");
            ShotFrom("session_6_preparation_overview", ShoulderPlace, ShoulderTarget, ShoulderView, true);
            yield return Next("debate");
            yield return Wait(5f);
            app.Director.TestSpeed = 200f;
            yield return WaitFor(() => app.Boards.ShownReminder == 0, 20f, "no reminder");
            app.Director.TestSpeed = 1f;
            yield return Wait(0.5f);
            Shot("session_7_debate");
            app.Rig.TurnTo(180f);
            yield return Wait(1f);
            Shot("session_7_reminder");
            yield return Click("session/menu");
            yield return Wait(1f);
            Shot("session_confirm");
            yield return Click("confirm/stay");
            yield return Next("end");
            app.Director.Next();
            yield return Wait(1.5f);
            Shot("session_finished");
            yield return Click("finished/menu");
            yield return WaitForTransition();
        }

        private static IEnumerator Next(string step)
        {
            while (app.Director.Step.id != step) yield return Click("session/next-step");
        }

        private static IEnumerator PictureDialogs()
        {
            yield return Click("start/key-help");
            yield return Wait(0.6f);
            Shot("key_help");
            yield return Click("keyhelp/close");
            yield return StartFree();
            app.Dialogs.OpenProblem(RealtimeProblem.NoInternet, "");
            yield return Wait(0.6f);
            Shot("problem");
            app.Dialogs.CloseAll();
            yield return BackToMenu();
        }
    }
}
