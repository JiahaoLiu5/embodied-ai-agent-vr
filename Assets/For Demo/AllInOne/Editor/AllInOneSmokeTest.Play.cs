using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemoKit;
using UnityEngine;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>Tests of the menu, the avatars, and the movement transformations.</summary>
    public static partial class AllInOneSmokeTest
    {
        private const int MovementRate = 72;
        private const int MenuRate = 30;

        private static string savedAvatar;
        private static int savedCaptions;
        private static bool hasSavedPreferences;

        private static IEnumerator DesktopTests()
        {
            yield return PlayTest("T4 boot and layout", TestBoot);
            yield return PlayTest("T5 avatars", TestAvatars);
            yield return PlayTest("T6 modes", TestModes);
            yield return PlayTest("T7 strength", TestStrength);
            yield return PlayTest("T8 noise", TestNoise);
            yield return PlayTest("T9 free conversation without a key", TestFreeWithoutKey);
            yield return PlayTest("T10 conversation with a server", TestConversation);
            yield return PlayTest("T11 lip sync", TestLipSync);
            yield return PlayTest("T12 quick session", TestQuickSession);
            yield return PlayTest("T13 study session", TestStudySession);
            yield return PlayTest("T14 session control", TestSessionControl);
            yield return PlayTest("T15 back to the menu", TestBackToMenu);
            yield return PlayTest("T16 idle", TestIdle);
            yield return PlayTest("T18 pictures", TakePictures);
            yield return PlayTest("T20 hurried user", TestHurriedUser);
            yield return PlayTest("T21 bad connections", TestBadConnections);
            yield return PlayTest("T22 low frame rate", TestLowFrameRate);
            yield return PlayTest("T23 scene loaded again", TestReload);
            if (SpeechFiles != null) yield return PlayTest("T19 live conversation", TestLive);
            RestorePreferences();
        }

        /// <summary>Waits for the start panel. Every play mode test starts with it, so that each one can run alone.</summary>
        private static IEnumerator Boot()
        {
            Time.captureFramerate = MenuRate;
            if (app != null) yield break;

            app = UnityEngine.Object.FindAnyObjectByType<AllInOneController>();
            mirrors = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Where(c => c.name.StartsWith("HexCam")).ToArray();
            SavePreferences();
            float started = Time.time;
            yield return WaitFor(() => app.Phase == AppPhase.Menu, 60f, "no start panel");
            Check(Time.time - started < 3f, "the start panel took " + (Time.time - started) + " s");
            app.Presence.Seed = 1;
            UnityEngine.Random.InitState(1);
            if (app.Rig.Desktop != null) app.Rig.Desktop.UserControl = false;
            yield return Wait(1.5f);
        }

        private static void SavePreferences()
        {
            if (hasSavedPreferences) return;
            hasSavedPreferences = true;
            savedAvatar = PlayerPrefs.GetString("aio.avatar", "");
            savedCaptions = PlayerPrefs.GetInt("aio.captions", 1);
        }

        private static void RestorePreferences()
        {
            if (!hasSavedPreferences) return;
            PlayerPrefs.SetString("aio.avatar", savedAvatar);
            PlayerPrefs.SetInt("aio.captions", savedCaptions);
        }

        // Menu ------------------------------------------------------------------------------------------------

        private static IEnumerator TestBoot()
        {
            yield return Boot();
            AgentChoices choices = app.Choices;
            Check(choices.Mode == MovementMode.Independent && Mathf.Approximately(choices.Strength, 0.3f), "default movement");
            Check(!choices.IsSession && choices.Topic.id == "social" && choices.Pace == SessionPace.Study, "default experience");
            Check(app.Switcher.Mode == MovementMode.Independent, "applied movement");

            DemoPanel start = app.Menu.Panel;
            string[] names = { "avatar", "movement", "experience", "ready" };
            for (int page = 0; page < names.Length; page++)
            {
                yield return GoToPage(page);
                CheckLayout(start, "start, " + names[page]);
            }
            yield return TestExperiencePage(start, choices);
            yield return TestDialogs();

            yield return GoToPage(StartPanel.ReadyPage);
            yield return Click("start/key-help");
            Check(app.Dialogs.Current == app.Dialogs.KeyHelp && !start.IsShown, "key help");
            yield return Click("keyhelp/check");
            yield return Click("keyhelp/close");
            Check(app.Dialogs.Current == null && start.IsShown, "key help stays open");
            yield return GoToPage(StartPanel.AvatarPage);
            yield return Click("start/recentre");
            yield return Wait(1f);
        }

        private static IEnumerator TestExperiencePage(DemoPanel start, AgentChoices choices)
        {
            yield return GoToPage(StartPanel.ExperiencePage);
            float height = start.RectTransform.rect.height;
            yield return Click("start/experience.session");
            foreach (AgentTopic topic in app.Topics)
            {
                yield return Click("start/topic." + topic.id);
                Check(choices.Topic == topic, "topic " + topic.id);
                CheckLayout(start, "start, experience, " + topic.id);
                Check(start.RectTransform.rect.height - height < 2f, "the start panel grows with " + topic.id);
            }
            yield return Click("start/pace.quick-preview");
            Check(choices.Pace == SessionPace.Quick, "pace");
            CheckLayout(start, "start, quick preview");
            yield return Click("start/pace.study-timing");
            yield return Click("start/topic.social");
            yield return Click("start/experience.free");
            Check(!choices.IsSession, "free conversation");
        }

        private static IEnumerator TestDialogs()
        {
            foreach (RealtimeProblem problem in Enum.GetValues(typeof(RealtimeProblem)))
            {
                if (problem == RealtimeProblem.None) continue;
                app.Dialogs.OpenProblem(problem, "A text of the service with some words.");
                yield return Wait(0.3f);
                CheckLayout(app.Dialogs.Problem, "problem " + problem);
                app.Dialogs.CloseAll();
                yield return Wait(0.3f);
            }
            foreach (Action open in new Action[] { app.Dialogs.OpenKeyHelp, app.Dialogs.OpenConfirm, app.Dialogs.OpenFinished })
            {
                open();
                yield return Wait(0.3f);
                CheckLayout(app.Dialogs.Current, app.Dialogs.Current.Id);
                app.Dialogs.CloseAll();
                yield return Wait(0.3f);
            }
        }

        // Avatars ---------------------------------------------------------------------------------------------

        private static IEnumerator TestAvatars()
        {
            yield return Boot();
            yield return BackToMenu();
            yield return GoToPage(StartPanel.AvatarPage);
            app.Rig.TurnTo(YawTo(app.Figure.Body.transform.position));
            yield return Wait(1.5f);
            float largest = 0f;
            foreach (int number in new[] { 1, 4, 2, 6, 3, 5, 1, 4, 1, 2, 5, 3 })
            {
                yield return Click("start/avatar.avatar-" + number);
                yield return Frames(12);
                AgentAvatarCatalog.Entry avatar = app.Catalog.Find("avatar-" + number);
                Check(app.Choices.Avatar == avatar && (number == 1 || PlayerPrefs.GetString("aio.avatar") == avatar.id), "choice of avatar " + number);
                largest = Mathf.Max(largest, CheckBody(app.Body, avatar, "body, avatar " + number));
                largest = Mathf.Max(largest, CheckBody(app.Figure.Body, avatar, "figure, avatar " + number));
                CheckFigure("figure, avatar " + number);
            }
            Note("largest distance of a hand to its target " + Centimetres(largest));
        }

        private static float CheckBody(AvatarDresser body, AgentAvatarCatalog.Entry avatar, string what)
        {
            string[] expected = avatar.prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.name).OrderBy(n => n).ToArray();
            string[] worn = body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled && r.sharedMesh != null).Select(r => r.name).OrderBy(n => n).ToArray();
            Check(expected.SequenceEqual(worn), what + " wears " + string.Join(" ", worn.Except(expected)) + " and misses " + string.Join(" ", expected.Except(worn)));

            Dictionary<string, Transform> bones = body.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach (Transform source in avatar.prefab.GetComponentsInChildren<Transform>(true))
            {
                bool isBone = source != avatar.prefab.transform && source.GetComponent<SkinnedMeshRenderer>() == null && source.name != "Hips" && !source.name.StartsWith("Armature");
                if (!isBone || !bones.TryGetValue(source.name, out Transform bone)) continue;
                float offset = Vector3.Distance(source.localPosition, bone.localPosition);
                Check(offset < 0.006f, what + ": " + source.name + " is off by " + (offset * 1000f).ToString("0.0") + " mm");
            }

            Transform[] targets = IkTargets(body.gameObject);
            float left = Vector3.Distance(bones["LeftHand"].position, targets[1].position), right = Vector3.Distance(bones["RightHand"].position, targets[2].position);
            Check(left < 0.03f && right < 0.03f, what + ": hands are off by " + Centimetres(left) + " and " + Centimetres(right));
            foreach (string foot in new[] { "LeftFoot", "RightFoot" })
            {
                float y = bones[foot].position.y;
                Check(y > 0.06f && y < 0.16f, what + ": " + foot + " at " + y.ToString("0.000") + " m");
            }
            return Mathf.Max(left, right);
        }

        /// <summary>The hands of the figure are the reflected hands of the user, also in their rotation.</summary>
        private static void CheckFigure(string what)
        {
            Transform[] user = IkTargets(app.Body.gameObject), mine = IkTargets(app.Figure.Body.gameObject);
            Vector3 normal = app.Figure.Normal;
            foreach ((int from, int to) in new[] { (1, 2), (2, 1) })
            {
                Quaternion expected = Quaternion.LookRotation(Vector3.Reflect(user[from].forward, normal), Vector3.Reflect(user[from].up, normal));
                Check(Quaternion.Angle(expected, mine[to].rotation) < 2f, what + ": rotation of a hand");
            }
            Check(Vector3.Dot(app.Figure.Body.transform.forward, -normal) > 0.9f, what + " does not face the user");
        }

        // Movement --------------------------------------------------------------------------------------------

        private static IEnumerator TestModes()
        {
            yield return Boot();
            yield return BackToMenu();
            Time.captureFramerate = MovementRate;
            yield return GoToPage(StartPanel.MovementPage);
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Talk;
            yield return WaitFor(() => app.Switcher.Recorded.IsLoaded, 60f, "recordings are not loaded");
            yield return Wait(1f);

            var user = new StepMeter(IkTargets(app.Body.gameObject));
            var agent = new StepMeter(IkTargets(app.Presence.Body.gameObject));
            foreach (string mode in new[] { "independent", "mimicry", "independent", "leading", "noise", "mimicry", "leading", "independent", "noise", "independent" })
            {
                user.Reset();
                agent.Reset();
                yield return Click("start/movement." + mode);
                for (float t = 0f; t < 2.2f || app.Switcher.IsSwitching; t += Time.deltaTime)
                {
                    user.Sample();
                    agent.Sample();
                    yield return null;
                }
                Check(user.LargestStep < 0.015f, mode + ": the user's avatar moves " + Centimetres(user.LargestStep) + " in a frame");
                Check(user.LargestTurn < 12f, mode + ": the user's avatar turns " + user.LargestTurn.ToString("0.0") + " degrees in a frame");
                Check(agent.LargestStep < 0.05f, mode + ": the agent moves " + Centimetres(agent.LargestStep) + " in a frame");
                CheckEndState(mode);
                Note(mode + ": user " + Centimetres(user.LargestStep) + ", agent " + Centimetres(agent.LargestStep) + " per frame");
            }

            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.RaiseRight;
            app.Presence.OfflineGestures = false;
            foreach (MovementMode mode in new[] { MovementMode.Leading, MovementMode.Mimicry })
            {
                yield return Click("start/movement." + AgentDemoTexts.ModeId(mode));
                yield return Wait(4f);
                CheckFormula(mode == MovementMode.Mimicry ? app.Switcher.UnitM : app.Switcher.UnitL, mode.ToString());
            }

            // A request during a switch is served afterwards, and the last one wins.
            app.ChooseMode(MovementMode.Independent);
            app.ChooseMode(MovementMode.Noise);
            app.ChooseMode(MovementMode.Leading);
            yield return WaitFor(() => !app.Switcher.IsSwitching, 20f, "switching does not end");
            Check(app.Switcher.Mode == MovementMode.Leading, "the last request was not served");

            yield return TestMissingRecordings();
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Idle;
            yield return Click("start/movement.independent");
            yield return WaitFor(() => !app.Switcher.IsSwitching, 20f, "switching does not end");
        }

        private static IEnumerator TestMissingRecordings()
        {
            TextAsset[] files = app.Switcher.Recorded.Recordings;
            app.Switcher.Recorded.Recordings = Array.Empty<TextAsset>();
            int changes = 0;
            void Count(MovementMode mode) => changes++;
            app.Switcher.Changed += Count;
            app.ChooseMode(MovementMode.Noise);
            yield return Wait(1f);
            Check(DemoToast.Current == AgentDemoTexts.NoRecordings, "no message about missing recordings");
            yield return Wait(2f);
            app.Switcher.Changed -= Count;
            Check(app.Switcher.Mode == MovementMode.Independent && !app.Switcher.IsSwitching && changes == 1, "without recordings: " + app.Switcher.Mode + " after " + changes + " changes");
            app.Switcher.Recorded.Recordings = files;
            yield return app.Switcher.Recorded.Load();
        }

        /// <summary>Compares the switched components with the study condition of the same name.</summary>
        private static void CheckEndState(string mode)
        {
            ConditionSwitcher s = app.Switcher;
            bool isLeading = mode == "leading", isNoise = mode == "noise", isMimicry = mode == "mimicry";
            Check(AgentDemoTexts.ModeId(s.Mode) == mode, mode + " is not applied");
            Check(s.UnitM.Root.activeSelf == isMimicry && s.UnitL.Root.activeSelf == (isLeading || isNoise), mode + ": active transformations");
            foreach (IKTargetFollowVRRig follower in app.Body.GetComponents<IKTargetFollowVRRig>())
                Check(follower.enabled == (follower.head.vrTarget.root.name == "Modulation L" == (isLeading || isNoise)), mode + ": what the user's avatar follows");
            foreach (TransformAlignment follower in app.Presence.Body.GetComponents<TransformAlignment>())
                Check(follower.enabled == (follower.pairs[0].source.root.name == "Modulation M" == isMimicry), mode + ": what the agent follows");
            string partner = s.UnitL.Root.GetComponent<TransformAlignment>().pairs[3].source.root.name;
            if (isLeading || isNoise) Check(partner.EndsWith("Recording") == isNoise, mode + ": partner " + partner);
            Check(s.Recorded.IsPlaying == isNoise, mode + ": recording");
        }

        /// <summary>Compares the output with the definition of the transformation.</summary>
        private static void CheckFormula(ModulationUnit unit, string what)
        {
            Transform Part(string name) => unit.Root.transform.Find(name);
            Vector3 head = Part("Head").localPosition, partnerHead = Part("Head Partner Slow").localPosition;
            Check(Vector3.Distance(Part("Head Modulated").localPosition, head) < 0.005f, what + ": the head was moved");
            Quaternion mixed = Quaternion.Slerp(Part("Head").localRotation, Part("Head Partner Mirror").localRotation, unit.Ratio);
            Check(Quaternion.Angle(Part("Head Modulated").localRotation, mixed) < 2f, what + ": rotation of the head");
            foreach ((string own, string other) in new[] { ("Left Hand", "Right Hand"), ("Right Hand", "Left Hand") })
            {
                Vector3 offset = Part(other + " Partner Slow").localPosition - partnerHead;
                offset.z = -offset.z;
                Vector3 expected = head + Vector3.Lerp(Part(own).localPosition - head, offset, unit.Ratio);
                float error = Vector3.Distance(Part(own + " Modulated").localPosition, expected);
                Check(error < 0.005f, what + ": " + own + " is off by " + (error * 1000f).ToString("0.0") + " mm");
            }
        }

        private static IEnumerator TestStrength()
        {
            yield return Boot();
            yield return BackToMenu();
            Time.captureFramerate = MovementRate;
            yield return GoToPage(StartPanel.MovementPage);
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Talk;
            yield return Click("start/movement.leading");
            yield return WaitFor(() => !app.Switcher.IsSwitching, 20f, "switching does not end");

            Transform unit = app.Switcher.UnitL.Root.transform;
            var meter = new StepMeter(IkTargets(app.Body.gameObject));
            foreach (float strength in new[] { 1f, 0f, 0.3f })
            {
                app.ChooseStrength(strength);
                meter.Reset();
                for (float t = 0f; t < 1.4f; t += Time.deltaTime)
                {
                    meter.Sample();
                    yield return null;
                }
                Check(meter.LargestStep < 0.015f, "strength " + strength + ": " + Centimetres(meter.LargestStep) + " in a frame");
                float[] ratios = unit.GetComponentsInChildren<HandMixedFollowSystemFinal>().Select(h => h.followRatio).Append(HeadRatio(unit, "Head Modulated")).ToArray();
                Check(ratios.Length == 3 && ratios.All(v => Mathf.Abs(v - strength) < 0.001f), "ratios " + string.Join(", ", ratios));
                Check(Mathf.Approximately(HeadRatio(unit, "Head Plane"), 1f), "Head Plane");
                if (strength == 0f) CheckOwnPose();
                Note("strength " + strength + ": " + Centimetres(meter.LargestStep) + " per frame");
            }

            DemoUi.Get<DemoStepper>("start/strength").SetSilently(30f);
            yield return Click("start/strength.plus");
            Check(Mathf.Approximately(app.Choices.Strength, 0.4f), "strength of the menu " + app.Choices.Strength);
            yield return Click("start/strength.minus");
            app.Rig.Desktop.CurrentMotion = DemoDesktopRig.Motion.Idle;
            yield return Click("start/movement.independent");
            yield return WaitFor(() => !app.Switcher.IsSwitching, 20f, "switching does not end");
        }

        private static float HeadRatio(Transform unit, string part) => unit.Find(part).GetComponents<HeadRotationMixedFollowSystem>().First(h => h.enabled).followRatio;

        private static void CheckOwnPose()
        {
            Transform[] targets = IkTargets(app.Body.gameObject);
            string[] raw = { "Left Controller/Left Hand VR Target", "Right Controller/Right Hand VR Target" };
            for (int i = 0; i < 2; i++)
            {
                float distance = Vector3.Distance(targets[i + 1].position, app.Rig.Origin.transform.Find("Camera Offset/" + raw[i]).position);
                Check(distance < 0.01f, "at 0 % a hand is off by " + Centimetres(distance));
            }
        }

        private static IEnumerator TestNoise()
        {
            yield return Boot();
            yield return BackToMenu();
            Time.captureFramerate = MovementRate;
            RecordedMotionSource recorded = app.Switcher.Recorded;
            yield return WaitFor(() => recorded.IsLoaded, 60f, "recordings are not loaded");
            Check(recorded.Loaded.Count == 4, recorded.Loaded.Count + " recordings");
            Note("rows " + string.Join(", ", recorded.Loaded.Select(l => l.Count)));

            yield return GoToPage(StartPanel.MovementPage);
            yield return Click("start/movement.noise");
            yield return WaitFor(() => !app.Switcher.IsSwitching, 20f, "switching does not end");
            Check(recorded.Elapsed > 170f, "the preview starts at " + recorded.Elapsed);
            Transform rig = GameObject.Find("XR Origin (XR Rig) Mixamo Recording").transform;
            Transform[] hands = { rig.Find("Camera Offset/Left Controller/Left Hand VR Target"), rig.Find("Camera Offset/Right Controller/Right Hand VR Target") };
            for (int index = 0; index < 4; index++)
            {
                recorded.Play(RecordedStart.Conversation, index);
                yield return null;
                yield return CheckRecorded(hands, 60f, "recording " + (index + 1), true);
            }
            recorded.Play(RecordedStart.Session, 0);
            Check(recorded.Elapsed < 0.1f, "the session starts at " + recorded.Elapsed);
            yield return null;
            yield return CheckRecorded(hands, 3f, "session", false);
            yield return Click("start/movement.independent");
            yield return WaitFor(() => !app.Switcher.IsSwitching, 20f, "switching does not end");
        }

        private static IEnumerator CheckRecorded(Transform[] hands, float seconds, string what, bool mustMove)
        {
            var meter = new StepMeter(hands);
            float rest = 0f, longestRest = 0f, travel = 0f, second = 0f;
            Vector3 left = hands[0].position, right = hands[1].position;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                meter.Sample();
                travel += Vector3.Distance(left, hands[0].position) + Vector3.Distance(right, hands[1].position);
                left = hands[0].position;
                right = hands[1].position;
                second += Time.deltaTime;
                if (second >= 1f)
                {
                    rest = travel < 0.03f ? rest + 1f : 0f;
                    longestRest = Mathf.Max(longestRest, rest);
                    second = travel = 0f;
                }
                yield return null;
            }
            // The recorded hands themselves move up to 4.7 cm per frame, a jump would be much larger.
            Check(meter.LargestStep < 0.06f, what + ": " + Centimetres(meter.LargestStep) + " in a frame");
            if (mustMove) Check(longestRest <= 5f, what + ": the hands rest for " + longestRest + " s");
            Note(what + ": longest rest " + longestRest + " s, largest step " + Centimetres(meter.LargestStep));
        }
    }
}
