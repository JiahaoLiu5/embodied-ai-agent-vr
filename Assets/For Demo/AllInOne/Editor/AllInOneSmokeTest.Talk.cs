using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemoKit;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>Tests of the free conversation, with a server on this computer instead of the service.</summary>
    public static partial class AllInOneSmokeTest
    {
        private const int VoiceRate = 24000;
        private const string FakeKey = "sk-" + "FAKE0123456789abcdefghij";

        private static RealtimeAPIWrapper Api => UnityEngine.Object.FindAnyObjectByType<RealtimeAPIWrapper>();

        private static void UseServer(RealtimeMockServer server)
        {
            RealtimeAPIWrapper.KeyOverride = () => "mock";
            Api.Endpoint = server.Url;
        }

        private static void UseNoKey() => RealtimeAPIWrapper.KeyOverride = () => null;

        private static string StatusText(string id)
        {
            DemoStatus status = DemoUi.Get<DemoStatus>(id);
            return status != null ? status.Value : null;
        }

        private static IEnumerator WaitForState(ConversationState state, float seconds = 6f)
        {
            yield return WaitFor(() => app.Conversation.State == state, seconds, "the conversation is " + app.Conversation.State + " instead of " + state);
        }

        private static IEnumerator TestFreeWithoutKey()
        {
            yield return Boot();
            UseNoKey();
            yield return StartFree();
            Check(app.Conversation.State == ConversationState.NoKey && StatusText("talk/state") == "Cannot talk without a key", "state " + StatusText("talk/state"));
            CheckLayout(app.Talk.Panel, "talk");
            yield return TestKeyHelp();
            int idle = Animator.StringToHash("Idle");
            yield return WaitFor(() => app.Presence.Capture.GetCurrentAnimatorStateInfo(0).shortNameHash != idle, 30f, "the agent does not gesture");

            foreach (MovementMode mode in new[] { MovementMode.Leading, MovementMode.Mimicry, MovementMode.Noise, MovementMode.Independent })
            {
                yield return Click("talk/mode." + AgentDemoTexts.ModeId(mode));
                yield return Wait(0.2f);
                yield return WaitFor(() => !app.Switcher.IsSwitching, 20f, "switching does not end");
                Check(app.Switcher.Mode == mode, "live switch to " + mode);
            }
            yield return Click("talk/mode.leading");
            yield return Click("talk/strength.plus");
            Check(Mathf.Approximately(app.Choices.Strength, 0.4f), "live strength");

            yield return Click("talk/talkmode.push-to-talk");
            Check(app.Conversation.PushToTalk && DemoUi.Get<DemoLabel>("talk/talk-hint").Text == AgentDemoTexts.PushToTalkDesktop, "push to talk");
            CheckLayout(app.Talk.Panel, "talk, push to talk");
            yield return Click("talk/talkmode.hands-free");
            yield return Click("talk/interrupt");
            Check(!app.Conversation.InterruptAgent, "interrupt switch");
            CheckLayout(app.Talk.Panel, "talk, no interruption");
            yield return Click("talk/interrupt");
            yield return Click("talk/selfview");
            Check(app.Figure.IsShown, "self view");
            yield return Click("talk/selfview");
            yield return Click("talk/captions");
            Check(!app.Captions.On, "captions switch");
            yield return Click("talk/captions");
            yield return Click("talk/menu");
            yield return WaitForTransition();
            Check(app.Phase == AppPhase.Menu && app.Switcher.Mode == MovementMode.Independent && !app.Presence.Visible, "menu after the conversation");
            Check(DemoUi.Get<DemoScrollGrid>("start/movement").SelectedId == "leading" && Mathf.Approximately(DemoUi.Get<DemoStepper>("start/strength").Value, 40f),
                "the menu does not show what was chosen in the conversation");
            app.ChooseStrength(ConditionSwitcher.DefaultStrength);
            app.ChooseMode(MovementMode.Independent);
        }

        private static IEnumerator TestKeyHelp()
        {
            Check(!DemoUi.Get<DemoButton>("talk/talk").Interactable && DemoUi.IsVisible("talk/key-help"), "without a key the panel does not offer help");
            yield return Click("talk/key-help");
            Check(app.Dialogs.Current == app.Dialogs.KeyHelp, "key help in the conversation");
            yield return Wait(0.3f);
            RealtimeAPIWrapper.KeyOverride = () => "mock";
            yield return Click("keyhelp/check");
            Check(app.Dialogs.Current == null && app.Conversation.State == ConversationState.Offline, "the new key was not noticed");
            Check(DemoUi.Get<DemoButton>("talk/talk").Interactable && !DemoUi.IsVisible("talk/key-help"), "the panel still asks for a key");
            CheckLayout(app.Talk.Panel, "talk with a key");
            UseNoKey();
        }

        // A whole conversation --------------------------------------------------------------------------------

        private static IEnumerator TestConversation()
        {
            yield return Boot();
            Time.captureFramerate = 0;
            var keyLeaks = new List<string>();
            void Watch(string message, string trace, LogType type)
            {
                if (message.Contains("sk-")) keyLeaks.Add(message);
            }
            Application.logMessageReceived += Watch;
            using (var server = new RealtimeMockServer())
            {
                UseServer(server);
                yield return StartFree();
                yield return TestSessionSetup(server);
                yield return TestAnswer(server);
                yield return TestToolCalls(server);
                yield return TestInterruption(server);
                yield return TestProblems(server);
                yield return TestApplicationPause(server);
                yield return BackToMenu();
            }
            Application.logMessageReceived -= Watch;
            Check(keyLeaks.Count == 0, "the log contains something like a key");
            UseNoKey();
        }

        private static IEnumerator TestSessionSetup(RealtimeMockServer server)
        {
            yield return WaitFor(() => server.Count("session.update") == 1, 10f, "no session.update");
            JToken session = server.Last("session.update")["session"];
            string prompt = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(AllInOnePaths.FreePrompt).text;
            Check((string)session["instructions"] == prompt, "prompt");
            Check((string)session["audio"]["output"]["voice"] == "shimmer", "voice");
            JToken names = session["tools"][0]["parameters"]["properties"]["animation_sequence"]["items"]["enum"];
            Check(names.Count() == 24, names.Count() + " gestures");

            yield return WaitFor(() => server.Count("response.create") == 1, 5f, "no greeting");
            JToken greeting = server.Last("conversation.item.create")["item"];
            Check((string)greeting["role"] == "system" && (string)greeting["content"][0]["text"] == AgentDemoTexts.GreetingNote, "greeting");
            yield return WaitForState(ConversationState.Thinking);
            Check(DemoUi.Get<DemoButton>("talk/talk").Label == "Stop talking", "talk button");
        }

        private static IEnumerator TestAnswer(RealtimeMockServer server)
        {
            server.Send(Created("r1"));
            for (int i = 0; i < 4; i++) server.Send(Audio("r1", "i1", 0.5f));
            server.Send(Transcript("r1", "Hello, I am Amy. "));
            yield return WaitForState(ConversationState.AgentSpeaking);
            yield return Wait(0.3f);
            Check(app.Captions.Bar.Text == "Amy: Hello, I am Amy." && app.Captions.Bar.IsPlaying, "caption: " + app.Captions.Bar.Text);
            server.Send(Transcript("r1", "What would you like to talk about today?"));
            server.Send(Done("r1"));
            yield return Wait(0.3f);
            Check(app.Captions.Bar.Text.EndsWith("today?"), "the caption does not grow: " + app.Captions.Bar.Text);
            yield return WaitForState(ConversationState.Listening);
            yield return Wait(1.6f);
            Check(!app.Captions.Bar.IsPlaying, "the caption stays");

            // A language without characters in the font has no caption.
            server.Send(Created("r1b"));
            server.Send(Audio("r1b", "i1b", 0.8f));
            server.Send(Transcript("r1b", "\u4f60\u597d\uff0c\u6211\u662f\u827e\u7c73\u3002"));
            server.Send(Done("r1b"));
            yield return WaitForState(ConversationState.AgentSpeaking);
            yield return Wait(0.3f);
            Check(!app.Captions.Bar.IsPlaying, "a caption without characters: " + app.Captions.Bar.Text);
            yield return WaitForState(ConversationState.Listening);
        }

        private static IEnumerator TestToolCalls(RealtimeMockServer server)
        {
            server.Forget();
            yield return Speak(0.8f);
            yield return WaitFor(() => server.Count("response.create") == 1, 5f, "no request after an utterance");
            JObject[] sent = server.All();
            int item = Array.FindIndex(sent, m => (string)m["type"] == "conversation.item.create" && (string)m["item"]["content"]?[0]?["type"] == "input_audio");
            int request = Array.FindIndex(sent, m => (string)m["type"] == "response.create");
            Check(item >= 0 && request > item && ((string)sent[item]["item"]["content"][0]["audio"]).Length > 10000, "the utterance was not sent");

            // Only a call: it is answered, and the model is asked to speak.
            server.Forget();
            server.Send(Created("r2"));
            server.Send(Call("r2", "c1", "Waving", "Talking (3)"));
            server.Send(Done("r2"));
            yield return WaitFor(() => server.Count("response.create") == 1, 5f, "no follow-up after a call");
            Check((string)server.Last("conversation.item.create")["item"]["call_id"] == "c1", "the call was not answered");

            // Speech and a call in one response: no second answer.
            server.Forget();
            server.Send(Created("r3"));
            for (int i = 0; i < 6; i++) server.Send(Audio("r3", "i3", 0.5f));
            server.Send(Call("r3", "c2", "Head Nod Yes", "Talking (1)"));
            server.Send(Done("r3"));
            yield return WaitForState(ConversationState.AgentSpeaking);
            yield return WaitFor(() => IsIn(app.Presence.Capture, "Head Nod Yes") && IsIn(app.Presence.Body, "Head Nod Yes"), 4f, "the gesture does not play on both bodies");
            yield return WaitFor(() => (string)server.Last("conversation.item.create")?["item"]?["call_id"] == "c2", 3f, "the second call was not answered");
            yield return Wait(0.3f);
            Check(server.Count("response.create") == 0, "a second answer was requested");
            yield return WaitForState(ConversationState.Listening, 8f);
        }

        private static IEnumerator TestInterruption(RealtimeMockServer server)
        {
            server.Forget();
            server.Send(Created("r4"));
            for (int i = 0; i < 10; i++) server.Send(Audio("r4", "i4", 0.5f));
            yield return WaitForState(ConversationState.AgentSpeaking);
            yield return Wait(1f);
            yield return Speak(0.5f, 0f);
            yield return WaitForState(ConversationState.UserSpeaking, 1f);
            Check(!Api.audioPlayer.IsAudioPlaying(), "the agent goes on speaking");
            yield return WaitFor(() => server.Count("response.cancel") == 1 && server.Count("conversation.item.truncate") == 1, 3f, "no cancel and truncate");
            JObject truncate = server.Last("conversation.item.truncate");
            int heard = truncate != null ? (int)truncate["audio_end_ms"] : -1;
            Check((string)truncate?["item_id"] == "i4" && heard > 700 && heard < 3000, "truncated at " + heard + " ms");
            Note("truncated at " + heard + " ms");

            server.Send(Audio("r4", "i4", 0.5f));
            server.Send(Done("r4", "cancelled"));
            yield return Speak(0f);
            Check(!Api.audioPlayer.IsAudioPlaying(), "late audio is played");
            Check(server.Count("response.cancel") == 1, server.Count("response.cancel") + " cancels");
        }

        private static IEnumerator TestProblems(RealtimeMockServer server)
        {
            server.Drop();
            yield return WaitFor(() => server.Connections == 2, 8f, "no reconnect");
            yield return WaitFor(() => app.Conversation.IsConnected, 5f, "not connected again");
            yield return Wait(0.5f);
            Check(server.Count("session.update") >= 1 && app.Dialogs.Current == null, "after the reconnect");
            server.Drop();
            yield return WaitFor(() => app.Dialogs.Current == app.Dialogs.Problem, 5f, "no problem panel");
            Check(app.Conversation.Problem == RealtimeProblem.ConnectionLost && server.Connections == 2, "problem " + app.Conversation.Problem);
            yield return Wait(0.4f);

            yield return Click("problem/retry");
            yield return WaitFor(() => server.Connections == 3, 8f, "no connection after Try again");
            yield return WaitFor(() => app.Conversation.IsConnected, 5f, "not connected after Try again");
            server.Send(new JObject
            {
                ["type"] = "error",
                ["error"] = new JObject { ["code"] = "invalid_api_key", ["message"] = "Incorrect API key provided: " + FakeKey + "." }
            });
            yield return WaitFor(() => app.Dialogs.Current == app.Dialogs.Problem, 5f, "no panel for a wrong key");
            Check(app.Conversation.Problem == RealtimeProblem.KeyRejected, "problem " + app.Conversation.Problem);
            string texts = string.Join(" ", app.Dialogs.Problem.GetComponentsInChildren<TMPro.TMP_Text>(true).Select(t => t.text));
            Check(!texts.Contains("sk-") && texts.Contains(AgentDemoTexts.ProblemTitle(RealtimeProblem.KeyRejected)), "text of the problem panel");
            Check(!RealtimeDiagnosis.Sanitize("x " + FakeKey + " Bearer abc task-based").Contains("FAKE") && RealtimeDiagnosis.Sanitize("task-based") == "task-based", "sanitizing");

            server.AcceptsConnections = false;
            yield return Click("problem/retry");
            yield return WaitFor(() => app.Dialogs.Current == app.Dialogs.Problem, 20f, "no panel for a refused connection");
            Note("refused connection: " + app.Conversation.Problem);
            server.AcceptsConnections = true;
            yield return Click("problem/menu");
            yield return WaitForTransition();
        }

        /// <summary>A headset that is taken off ends the free conversation, and nothing connects again by itself.</summary>
        private static IEnumerator TestApplicationPause(RealtimeMockServer server)
        {
            yield return StartFree();
            yield return WaitFor(() => app.Conversation.IsConnected, 8f, "not connected before the pause");
            int connections = server.Connections;
            app.SendMessage("OnApplicationPause", true);
            yield return Wait(0.5f);
            app.SendMessage("OnApplicationPause", false);
            yield return Wait(2f);
            Check(app.Conversation.State == ConversationState.Offline && server.Connections == connections, "after a pause of the app: " + app.Conversation.State);
            Check(DemoUi.Get<DemoButton>("talk/talk").Label == "Start talking", "talk button after a pause of the app");
        }

        private static bool IsIn(Animator animator, string state)
        {
            int hash = Animator.StringToHash(state);
            return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == hash);
        }

        /// <summary>Feeds sound and then silence into the microphone path, as fast as they would arrive.</summary>
        private static IEnumerator Speak(float seconds, float silence = 1.8f)
        {
            AudioRecorder recorder = Api.audioRecorder;
            for (float t = 0f; t < seconds + silence;)
            {
                var chunk = new float[Mathf.Max(1, Mathf.RoundToInt(Time.deltaTime * VoiceRate))];
                for (int i = 0; t < seconds && i < chunk.Length; i++) chunk[i] = 0.3f * (UnityEngine.Random.value * 2f - 1f);
                recorder.FeedSamples(chunk);
                t += Time.deltaTime;
                yield return null;
            }
        }

        // Messages of the service -----------------------------------------------------------------------------

        private static JObject Created(string id) => new JObject { ["type"] = "response.created", ["response"] = new JObject { ["id"] = id } };

        private static JObject Done(string id, string status = "completed") =>
            new JObject { ["type"] = "response.done", ["response"] = new JObject { ["id"] = id, ["status"] = status } };

        private static JObject Transcript(string response, string text) =>
            new JObject { ["type"] = "response.output_audio_transcript.delta", ["response_id"] = response, ["delta"] = text };

        private static JObject Audio(string response, string item, float seconds)
        {
            var samples = new float[Mathf.RoundToInt(seconds * VoiceRate)];
            for (int i = 0; i < samples.Length; i++) samples[i] = 0.2f * Mathf.Sin(2f * Mathf.PI * 180f * i / VoiceRate);
            return new JObject
            {
                ["type"] = "response.output_audio.delta", ["response_id"] = response, ["item_id"] = item,
                ["delta"] = AudioProcessingUtils.ConvertFloatToPCM16AndBase64(samples)
            };
        }

        private static JObject Call(string response, string id, params string[] gestures)
        {
            return new JObject
            {
                ["type"] = "response.function_call_arguments.done", ["response_id"] = response, ["call_id"] = id, ["name"] = "trigger_animation",
                ["arguments"] = new JObject { ["animation_sequence"] = new JArray(gestures.Cast<object>().ToArray()) }.ToString()
            };
        }

        // Lip sync --------------------------------------------------------------------------------------------

        private static IEnumerator TestLipSync()
        {
            yield return Boot();
            UseNoKey();
            yield return StartFree();
            Time.captureFramerate = 0;
            double before = AudioSettings.dspTime;
            yield return WaitReal(0.6f);
            if (AudioSettings.dspTime - before < 0.1)
            {
                Note("skipped: there is no audio device");
                yield return BackToMenu();
                yield break;
            }

            AudioClip clip = app.Director.Script.steps.First(s => s.id == "welcome").clip;
            clip.LoadAudioData();
            yield return WaitFor(() => clip.loadState == AudioDataLoadState.Loaded, 10f, "the clip is not loaded");
            byte[] voice = ToVoice(clip, 4f);
            Api.audioPlayer.ResetCancelPending();
            Api.audioPlayer.EnqueueAudioData(voice);

            string[] meshes = { "Head_Mesh", "Teeth_Mesh", "Tongue_Mesh" };
            var largest = new float[meshes.Length];
            bool hasPicture = false;
            for (float end = Time.realtimeSinceStartup + 4f; Time.realtimeSinceStartup < end;)
            {
                for (int i = 0; i < meshes.Length; i++)
                    largest[i] = Mathf.Max(largest[i], LargestViseme(Bone(app.Presence.Body.gameObject, meshes[i]).GetComponent<SkinnedMeshRenderer>()));
                if (!hasPicture && largest[0] > 0.5f)
                {
                    ShotFrom("lip_sync", new Vector3(0.12f, 1.64f, 0.35f), new Vector3(0f, 1.63f, -0.45f), 27f);
                    hasPicture = true;
                }
                yield return null;
            }
            for (int i = 0; i < meshes.Length; i++) Check(largest[i] > 0.2f, meshes[i] + ": largest viseme " + largest[i].ToString("0.00"));
            Note("largest visemes " + string.Join(", ", largest.Select(v => v.ToString("0.00"))));
            yield return BackToMenu();
        }

        private static IEnumerator WaitReal(float seconds)
        {
            for (float end = Time.realtimeSinceStartup + seconds; Time.realtimeSinceStartup < end;) yield return null;
        }

        private static float LargestViseme(SkinnedMeshRenderer mesh)
        {
            float largest = 0f;
            for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
            {
                string shape = mesh.sharedMesh.GetBlendShapeName(i);
                if (shape.Contains("viseme") && !shape.EndsWith("sil")) largest = Mathf.Max(largest, mesh.GetBlendShapeWeight(i));
            }
            return largest;
        }

        /// <summary>The beginning of a clip in the format of the service.</summary>
        private static byte[] ToVoice(AudioClip clip, float seconds)
        {
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            int count = Mathf.RoundToInt(Mathf.Min(seconds, clip.length) * VoiceRate);
            var mono = new float[count];
            for (int i = 0; i < count; i++)
            {
                int frame = Mathf.Min(clip.samples - 1, (int)((long)i * clip.frequency / VoiceRate));
                for (int c = 0; c < clip.channels; c++) mono[i] += data[frame * clip.channels + c] / clip.channels;
            }
            return Convert.FromBase64String(AudioProcessingUtils.ConvertFloatToPCM16AndBase64(mono));
        }
    }
}
