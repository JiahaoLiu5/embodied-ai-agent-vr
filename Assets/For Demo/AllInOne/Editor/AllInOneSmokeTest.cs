using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>
    /// Plays the integrated demo without a person: reads content and scenes, clicks the panels, moves the body, runs both
    /// experiences against a local server, and writes pictures and a result file to Logs/AllInOneTest. Start it with
    /// Unity -batchmode -projectPath P -executeMethod EmbodiedAgent.AllInOne.Editor.AllInOneSmokeTest.Run (no -quit).
    /// Add -aioTests T4,T5 to run only some of the tests. The microphone is never used, and the key of this computer only by
    /// the optional live test (see AllInOneSmokeTest.Live.cs).
    /// </summary>
    [InitializeOnLoad]
    public static partial class AllInOneSmokeTest
    {
        [Serializable]
        private sealed class Result
        {
            public string test;
            public bool passed;
            public List<string> problems = new List<string>();
            public List<string> notes = new List<string>();
        }

        private enum Stage { None, Desktop, Headset }

        // The tests that need the second stage
        private static readonly string[] HeadsetNames = { "T17", "T24", "T25", "T26" };

        private const string StageKey = "aio.stage";
        private const string EndedKey = "aio.ended";
        private const string ResultsKey = "aio.results";
        private const string TestsKey = "aio.tests";
        private const string StartKey = "aio.start";
        private const string Folder = "Logs/AllInOneTest";
        private const double TimeLimit = 20 * 60;

        // Messages of the Editor or of devices that this computer does not have
        private static readonly string[] Ignored =
        {
            "OpenXR", "xrCreateInstance", "Licensing", "icrophone", "UnityEditor.Search", "Curl error", "Unable to start XR", "Failed to initialize XR"
        };

        private static List<Result> results = new List<Result>();
        private static Result current;
        private static TestDriver driver;
        private static Stage stage;

        static AllInOneSmokeTest()
        {
            stage = (Stage)SessionState.GetInt(StageKey, 0);
            if (stage == Stage.None) return;

            results = JsonConvert.DeserializeObject<List<Result>>(SessionState.GetString(ResultsKey, "[]"));
            UseTestDevices();
            EditorApplication.update += Watch;
            Application.logMessageReceived += HandleLog;
        }

        public static void Run()
        {
            Directory.CreateDirectory(Folder);
            string[] arguments = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(arguments, "-aioTests");
            SessionState.SetString(TestsKey, at >= 0 && at + 1 < arguments.Length ? arguments[at + 1] : "");
            SessionState.SetString(StartKey, DateTime.UtcNow.Ticks.ToString());

            SessionState.SetBool(EndedKey, false);
            results.Clear();
            RunStaticTests();
            SessionState.SetString(ResultsKey, JsonConvert.SerializeObject(results));
            EnterStage(Stage.Desktop);
            EditorApplication.update += Watch;
        }

        /// <summary>No real key, no real microphone, and the same random choices every time.</summary>
        private static void UseTestDevices()
        {
            RealtimeAPIWrapper.KeyOverride = () => null;
            AudioRecorder.TestMode = true;
            UserRig.TestHeadset = stage == Stage.Headset;
        }

        private static void EnterStage(Stage next)
        {
            stage = next;
            SessionState.SetInt(StageKey, (int)next);
            UseTestDevices();
            EditorSceneManager.OpenScene(AllInOnePaths.Scene, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Follows the run from the editor. The state is kept outside of the scripts, because they are loaded again for play mode.</summary>
        private static void Watch()
        {
            double seconds = new TimeSpan(DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(StartKey, "0"))).TotalSeconds;
            if (seconds > TimeLimit)
            {
                Finish("The time limit was reached.");
                return;
            }

            if (SessionState.GetBool(EndedKey, false))
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetBool(EndedKey, false);
                if (stage == Stage.Desktop && HeadsetNames.Any(IsWanted)) EnterStage(Stage.Headset);
                else Finish(null);
            }
            else if (EditorApplication.isPlaying && driver == null)
            {
                Application.runInBackground = true;
                driver = TestDriver.Run(stage == Stage.Headset ? HeadsetTests() : DesktopTests());
            }
            else if (EditorApplication.isPlaying && driver.IsDone)
            {
                SessionState.SetString(ResultsKey, JsonConvert.SerializeObject(results));
                SessionState.SetBool(EndedKey, true);
                EditorApplication.ExitPlaymode();
            }
        }

        private static void Finish(string problem)
        {
            EditorApplication.update -= Watch;
            SessionState.SetInt(StageKey, 0);
            if (problem != null) results.Add(new Result { test = "run", problems = { problem } });

            bool passed = results.Count > 0 && results.All(r => r.passed);
            File.WriteAllText(Path.Combine(Folder, "result.json"), JsonConvert.SerializeObject(new { passed, results }, Formatting.Indented));
            foreach (Result result in results)
            {
                Debug.Log("[AIO] " + (result.passed ? "PASS " : "FAIL ") + result.test);
                foreach (string note in result.notes) Debug.Log("[AIO]   " + note);
                foreach (string p in result.problems) Debug.Log("[AIO]   ! " + p);
            }
            Debug.Log("[AIO] " + (passed ? "All tests passed." : "Some tests failed."));
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        }

        // Building blocks of the tests -------------------------------------------------------------------------

        private static bool IsWanted(string test)
        {
            string wanted = SessionState.GetString(TestsKey, "");
            return wanted.Length == 0 || wanted.Split(',').Any(w => test == w || test.StartsWith(w + " "));
        }

        private static void StaticTest(string name, Action test)
        {
            if (!IsWanted(name)) return;
            Open(name);
            try
            {
                test();
            }
            catch (Exception e)
            {
                current.problems.Add(e.GetType().Name + ": " + e.Message);
            }
            Close();
        }

        private static IEnumerator PlayTest(string name, Func<IEnumerator> test)
        {
            if (!IsWanted(name)) yield break;
            Open(name);
            float started = Time.realtimeSinceStartup;
            yield return new TestDriver.Guarded
            {
                routine = test(),
                onError = e => current.problems.Add(e.GetType().Name + ": " + e.Message + " " + FirstLine(e.StackTrace))
            };
            current.notes.Add((Time.realtimeSinceStartup - started).ToString("0") + " s");
            Close();
        }

        private static void Open(string name)
        {
            current = new Result { test = name };
            results.Add(current);
        }

        private static void Close()
        {
            current.passed = current.problems.Count == 0;
            Debug.Log("[AIO] " + (current.passed ? "pass " : "fail ") + current.test + (current.passed ? "" : ": " + string.Join(" | ", current.problems)));
            current = null;
        }

        private static void Check(bool condition, string problem)
        {
            if (!condition && current.problems.Count < 30) current.problems.Add(problem);
        }

        private static void Note(string text) => current.notes.Add(text);

        private static void HandleLog(string message, string trace, LogType type)
        {
            if (current == null || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            if (Ignored.Any(message.Contains)) return;
            Check(false, type + " in the log: " + FirstLine(message) + " @ " + FirstLine(trace));
        }

        private static string FirstLine(string text) => string.IsNullOrEmpty(text) ? "" : text.Split('\n')[0].Trim();
    }
}