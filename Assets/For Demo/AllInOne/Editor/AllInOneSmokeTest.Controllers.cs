using System;
using System.Collections;
using DemoKit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>The way through the demo with two simulated controllers: rays, triggers, and the push-to-talk button.</summary>
    public static partial class AllInOneSmokeTest
    {
        private const int ControllerRate = 60;

        private static IEnumerator TestControllers()
        {
            yield return Boot();
            UseNoKey();
            yield return BackToMenu();
            Time.captureFramerate = ControllerRate;
            app.Recentring.Recentre();
            app.Choices.IsSession = false;
            app.Choices.Mode = MovementMode.Independent;
            app.Menu.Show(StartPanel.AvatarPage);
            using (var left = new SimulatedController(true))
            using (var right = new SimulatedController(false))
            {
                yield return Wait(0.5f);
                yield return ControlMenu(left, right);
                yield return ControlFree(left, right);
                yield return ControlSession(left, right);
                yield return ControlPushToTalk(right);
            }
            app.ChooseMode(MovementMode.Independent);
            app.ChooseStrength(ConditionSwitcher.DefaultStrength);
        }

        private static IEnumerator ControlMenu(SimulatedController left, SimulatedController right)
        {
            yield return right.Press("start/avatar.avatar-2");
            yield return left.Press("start/avatar.avatar-6");
            Check(app.Choices.Avatar.id == "avatar-6" && app.Body.Worn == app.Choices.Avatar, "avatar " + app.Choices.Avatar.id);
            yield return left.Press("start/next");
            Check(app.Menu.Page == StartPanel.MovementPage, "page " + app.Menu.Page);
            yield return Wait(0.5f);
            yield return right.Press("start/movement.leading");
            yield return left.Press("start/strength.plus");
            Check(app.Choices.Mode == MovementMode.Leading && Mathf.Approximately(app.Choices.Strength, 0.4f), app.Choices.Mode + " " + app.Choices.Strength);
            yield return right.Press("start/next");
            yield return Wait(0.5f);
            yield return right.Press("start/experience.session");
            yield return left.Press("start/topic.growth");
            yield return right.Press("start/pace.quick-preview");
            Check(app.Choices.IsSession && app.Choices.Topic.id == "growth" && app.Choices.Pace == SessionPace.Quick, "experience");
            yield return left.Press("start/experience.free");
            yield return right.Press("start/next");
            yield return Wait(0.5f);
            yield return left.Press("start/key-help");
            Check(app.Dialogs.Current == app.Dialogs.KeyHelp, "key help with a controller");
            yield return Wait(0.5f);
            yield return right.Press("keyhelp/close");
            yield return Wait(0.5f);
        }

        private static IEnumerator ControlFree(SimulatedController left, SimulatedController right)
        {
            yield return right.Press("start/go");
            yield return WaitForTransition();
            yield return Wait(0.6f);
            Check(app.Phase == AppPhase.Free, "free conversation with a controller");
            yield return left.Press("talk/mode.mimicry");
            yield return right.Press("talk/selfview");
            Check(app.Choices.Mode == MovementMode.Mimicry && app.Figure.IsShown, "switches of the talk panel");
            yield return right.Press("talk/menu");
            yield return WaitForTransition();
            yield return Wait(0.6f);
            Check(app.Phase == AppPhase.Menu, "back to the menu with a controller");
        }

        private static IEnumerator ControlSession(SimulatedController left, SimulatedController right)
        {
            app.Choices.IsSession = true;
            yield return left.Press("start/go");
            yield return WaitForTransition();
            yield return Wait(0.6f);
            yield return left.Press("session/pause");
            Check(app.Director.IsPaused, "pause with a controller");
            yield return right.Press("session/next-step");
            Check(!app.Director.IsPaused && app.Director.StepIndex == 1, "next step with a controller");
            yield return right.Press("session/menu");
            yield return Wait(0.5f);
            yield return left.Press("confirm/leave");
            yield return WaitForTransition();
            yield return Wait(0.6f);
            Check(app.Phase == AppPhase.Menu, "leaving the session with a controller");
            app.Choices.IsSession = false;
        }

        private static IEnumerator ControlPushToTalk(SimulatedController right)
        {
            using (var server = new RealtimeMockServer())
            {
                UseServer(server);
                yield return StartFree();
                yield return WaitFor(() => app.Conversation.IsConnected, 8f, "not connected");
                yield return Wait(0.6f);
                yield return right.Press("talk/talkmode.push-to-talk");
                Check(app.Conversation.PushToTalk && DemoUi.Get<DemoLabel>("talk/talk-hint").Text == AgentDemoTexts.PushToTalkHeadset, "push to talk with a controller");
                right.HoldsTalkButton = true;
                yield return Frames(4);
                Check(app.Conversation.State == ConversationState.UserSpeaking, "button A: " + app.Conversation.State);
                right.HoldsTalkButton = false;
                yield return Frames(4);
                Check(app.Conversation.State != ConversationState.UserSpeaking, "button A released: " + app.Conversation.State);
                yield return right.Press("talk/talkmode.hands-free");
                Check(!app.Conversation.PushToTalk, "hands-free with a controller");
                yield return BackToMenu();
            }
            UseNoKey();
        }

        /// <summary>A tracked controller that is held in front of the body and can point at a control.</summary>
        private sealed class SimulatedController : IDisposable
        {
            private const int Tracked = 3;

            private readonly XRSimulatedController device;
            private readonly Vector3 place;
            private XRSimulatedControllerState state;

            public SimulatedController(bool isLeft)
            {
                device = InputSystem.AddDevice<XRSimulatedController>(isLeft ? "Test Left Controller" : "Test Right Controller");
                InputSystem.SetDeviceUsage(device, isLeft ? CommonUsages.LeftHand : CommonUsages.RightHand);
                place = UserRig.Home + new Vector3(isLeft ? 0.25f : -0.25f, 1.15f, -0.25f);
                state.Reset();
                state.trackingState = Tracked;
                state.isTracked = true;
                Aim(place + Vector3.back);
            }

            public bool HoldsTalkButton
            {
                set
                {
                    state = state.WithButton(ControllerButton.PrimaryButton, value);
                    Send();
                }
            }

            public void Dispose() => InputSystem.RemoveDevice(device);

            /// <summary>Points at a control, pulls the trigger, and lets it go.</summary>
            public IEnumerator Press(string id)
            {
                DemoElement element = DemoUi.Find(id);
                Check(element != null && element.CanClick, "no control " + id);
                if (element == null) yield break;

                int clicks = 0;
                void Count(DemoElement e) => clicks++;
                element.Clicked += Count;
                Aim(element.RectTransform.TransformPoint(element.RectTransform.rect.center));
                yield return Wait(0.3f);
                Check(element.Hovered, "the ray is not on " + id);
                Pull(true);
                yield return Frames(4);
                Pull(false);
                yield return Frames(4);
                if (element != null) element.Clicked -= Count;
                Check(clicks == 1, id + " was clicked " + clicks + " times");
                yield return Wait(0.2f);
            }

            private void Aim(Vector3 target)
            {
                Transform space = app.Rig.Origin.CameraFloorOffsetObject.transform;
                state.devicePosition = space.InverseTransformPoint(place);
                state.deviceRotation = Quaternion.Inverse(space.rotation) * Quaternion.LookRotation(target - place);
                Send();
            }

            private void Pull(bool on)
            {
                state.trigger = on ? 1f : 0f;
                state = state.WithButton(ControllerButton.TriggerButton, on);
                Send();
            }

            private void Send() => InputSystem.QueueStateEvent(device, state);
        }
    }
}
