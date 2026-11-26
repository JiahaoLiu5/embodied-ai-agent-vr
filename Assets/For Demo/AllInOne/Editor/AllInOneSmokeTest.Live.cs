using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoKit;
using UnityEngine;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>
    /// A free conversation and a debate with the real service, together less than a minute long. The test runs only when
    /// two spoken sentences are given on the command line (-aioLive QUESTION,STATEMENT, WAV files with 16 bit, mono,
    /// 24000 Hz), and it uses the key of this computer.
    /// </summary>
    public static partial class AllInOneSmokeTest
    {
        private const float FreeSeconds = 34f;
        private const float DebateSeconds = 24f;
        private const int ChunkHeader = 8;

        /// <summary>The files of the spoken sentences, or null.</summary>
        private static string[] SpeechFiles
        {
            get
            {
                string[] arguments = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(arguments, "-aioLive");
                return at >= 0 && at + 1 < arguments.Length ? arguments[at + 1].Split(',') : null;
            }
        }

        /// <summary>Watches what the agent does in her turns.</summary>
        private sealed class TurnRecord : IDisposable
        {
            public readonly List<string> gestures = new List<string>();
            public readonly List<string> problems = new List<string>();
            public float viseme, loudness, speakingAt = -1f;
            public bool moved;
            public string transcript = "";
            private readonly SkinnedMeshRenderer head;
            private readonly float[] samples = new float[512];

            public TurnRecord()
            {
                head = Bone(app.Presence.Body.gameObject, "Head_Mesh").GetComponent<SkinnedMeshRenderer>();
                RealtimeAPIWrapper.OnAnimationSequenceReceived += AddGestures;
                RealtimeAPIWrapper.OnProblem += AddProblem;
                RealtimeAPIWrapper.OnTranscriptReceived += AddText;
            }

            public void Dispose()
            {
                RealtimeAPIWrapper.OnAnimationSequenceReceived -= AddGestures;
                RealtimeAPIWrapper.OnProblem -= AddProblem;
                RealtimeAPIWrapper.OnTranscriptReceived -= AddText;
            }

            public void Begin()
            {
                gestures.Clear();
                viseme = loudness = 0f;
                speakingAt = -1f;
                moved = false;
                transcript = "";
            }

            public void Sample()
            {
                if (app.Conversation.State == ConversationState.AgentSpeaking && speakingAt < 0f) speakingAt = Time.realtimeSinceStartup;
                viseme = Mathf.Max(viseme, LargestViseme(head));
                Api.audioPlayer.GetComponent<AudioSource>().GetOutputData(samples, 0);
                loudness = Mathf.Max(loudness, samples.Max());
                moved |= gestures.Any(g => IsIn(app.Presence.Capture, g) && IsIn(app.Presence.Body, g));
            }

            private void AddGestures(List<string> names) => gestures.AddRange(names);

            private void AddProblem(RealtimeProblem problem, string detail) => problems.Add(problem.ToString());

            private void AddText(string part) => transcript += part;
        }

        private static IEnumerator TestLive()
        {
            yield return Boot();
            float[] question = ReadSpeech(SpeechFiles[0]), statement = ReadSpeech(SpeechFiles[1]);
            Time.captureFramerate = 0;
            RealtimeAPIWrapper.KeyOverride = null;
            Check(app.Conversation.HasKey, "there is no key on this computer");
            using (var turn = new TurnRecord())
            {
                if (app.Conversation.HasKey)
                {
                    yield return Converse(turn, question);
                    app.Conversation.End();
                    yield return Debate(turn, statement);
                }
                app.Conversation.End();
                Check(turn.problems.Count == 0, "problems: " + string.Join(", ", turn.problems));
            }
            UseNoKey();
            yield return Wait(2f);
            yield return BackToMenu();
        }

        private static IEnumerator Converse(TurnRecord turn, float[] speech)
        {
            float started = Time.realtimeSinceStartup;
            yield return StartFree(MovementMode.Mimicry);
            yield return WaitForConnection();
            if (!app.Conversation.IsConnected) yield break;
            Note("connected after " + Seconds(started));

            float end = started + FreeSeconds;
            yield return FollowTurn(turn, "greeting", Time.realtimeSinceStartup, end, true);

            // A question, then an interruption of the answer with the same words.
            yield return Say(speech);
            float asked = Time.realtimeSinceStartup;
            Check(app.Conversation.State == ConversationState.Thinking || app.Conversation.State == ConversationState.AgentSpeaking, "after the question: " + app.Conversation.State);
            yield return FollowTurn(turn, "answer", asked, end, false);
            Shot("live_answer");
            ShotFrom("live_answer_side", SidePlace, SideTarget, SideView);
            Note(Api.audioPlayer.IsAudioPlaying() ? "interrupted the answer" : "the answer was too short for an interruption");
            yield return Say(speech, () => app.Conversation.State == ConversationState.UserSpeaking && !Api.audioPlayer.IsAudioPlaying());
            asked = Time.realtimeSinceStartup;
            yield return FollowTurn(turn, "second answer", asked, end, false);
            Note("free conversation of " + Seconds(started));
        }

        /// <summary>The debate of the full session: the user makes a statement, the agent answers, and the time is up.</summary>
        private static IEnumerator Debate(TurnRecord turn, float[] statement)
        {
            yield return StartSession("social", SessionPace.Quick, MovementMode.Leading);
            while (app.Director.Step.id != "debate") yield return Click("session/next-step");
            float started = Time.realtimeSinceStartup;
            yield return WaitForConnection();
            if (!app.Conversation.IsConnected) yield break;
            yield return WaitForState(ConversationState.Listening, 10f);
            yield return Say(statement);
            yield return FollowTurn(turn, "debate", Time.realtimeSinceStartup, started + DebateSeconds, false);
            Shot("live_debate");
            yield return Click("session/next-step");
            yield return Wait(0.5f);
            Check(!app.Conversation.IsConnected && !Api.audioPlayer.IsAudioPlaying(), "the debate goes on after its end");
            Note("debate of " + Seconds(started));
        }

        private static IEnumerator WaitForConnection()
        {
            yield return WaitFor(() => app.Conversation.IsConnected || app.Conversation.Problem != RealtimeProblem.None, 20f, "no connection");
            Check(app.Conversation.IsConnected, "problem " + app.Conversation.Problem);
        }

        /// <summary>Follows a turn of the agent to its end, or only its first seconds.</summary>
        private static IEnumerator FollowTurn(TurnRecord turn, string what, float asked, float end, bool toTheEnd)
        {
            turn.Begin();
            bool isOver = false;
            while (!isOver && Time.realtimeSinceStartup < end)
            {
                turn.Sample();
                float speaking = turn.speakingAt > 0f ? Time.realtimeSinceStartup - turn.speakingAt : 0f;
                bool isSilent = turn.speakingAt > 0f && app.Conversation.State == ConversationState.Listening;
                bool isWaiting = turn.speakingAt < 0f && Time.realtimeSinceStartup - asked < 12f;
                isOver = turn.speakingAt > 0f ? isSilent || speaking > (toTheEnd ? 14f : 3.5f) : !isWaiting;
                yield return null;
            }
            Check(turn.speakingAt > 0f, what + ": no voice");
            Check(turn.loudness > 0.01f, what + ": the voice is silent");
            Check(turn.viseme > 0.2f, what + ": largest viseme " + turn.viseme.ToString("0.00"));
            Check(turn.transcript.Length > 0, what + ": no transcript");
            Check(turn.gestures.Count > 0 && turn.gestures.All(g => Api.GestureNames.Contains(g)), what + ": gestures " + string.Join(", ", turn.gestures));
            Check(turn.moved, what + ": no gesture was played");
            Note(what + ": voice after " + (turn.speakingAt - asked).ToString("0.0") + " s, largest viseme " + turn.viseme.ToString("0.00") +
                 ", gestures " + string.Join(", ", turn.gestures) + ", " + turn.transcript.Length + " characters");
        }

        /// <summary>
        /// Speaks into the microphone path at the speed of a person, then is silent until the turn has ended. The condition
        /// must hold when the speech is noticed.
        /// </summary>
        private static IEnumerator Say(float[] speech, Func<bool> onStart = null)
        {
            AudioRecorder recorder = Api.audioRecorder;
            bool hasStarted = false;
            float started = Time.realtimeSinceStartup;
            int total = speech.Length + VoiceRate, sent = 0;
            while (sent < total)
            {
                int due = Mathf.Min(total, (int)((Time.realtimeSinceStartup - started) * VoiceRate));
                if (due > sent)
                {
                    var chunk = new float[due - sent];
                    if (sent < speech.Length) Array.Copy(speech, sent, chunk, 0, Mathf.Min(chunk.Length, speech.Length - sent));
                    recorder.FeedSamples(chunk);
                    sent = due;
                }
                if (!hasStarted && recorder.IsVADRecording)
                {
                    hasStarted = true;
                    yield return null;
                    if (onStart != null) Check(onStart(), "the conversation does not notice the user");
                }
                yield return null;
            }
            Check(hasStarted, "the speech was not noticed");
            yield return WaitFor(() => !recorder.IsVADRecording, 3f, "the end of the speech was not noticed");
        }

        /// <summary>The samples of a WAV file with 16 bit and one channel.</summary>
        private static float[] ReadSpeech(string file)
        {
            byte[] bytes = File.ReadAllBytes(file);
            int at = 12;
            while (System.Text.Encoding.ASCII.GetString(bytes, at, 4) != "data") at += ChunkHeader + BitConverter.ToInt32(bytes, at + 4);
            var samples = new float[BitConverter.ToInt32(bytes, at + 4) / 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, at + ChunkHeader + 2 * i) / 32768f;
            return samples;
        }

        private static string Seconds(float since) => (Time.realtimeSinceStartup - since).ToString("0.0") + " s";
    }
}
