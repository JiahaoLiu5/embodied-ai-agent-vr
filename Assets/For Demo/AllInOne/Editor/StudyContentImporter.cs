using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>
    /// Reads what the integrated demo needs from the study scenes and writes it as assets: the instructions of the agent,
    /// the order of the warm-up, boards, narration, and times. The study scenes are opened but never saved. Components
    /// are found by the names of their types, because the demo assemblies do not reference the study scripts.
    /// </summary>
    public static class StudyContentImporter
    {
        private sealed class TopicSource
        {
            public string file, scene, id, letter, title, question, user, agent;
            public int sceneLength, fileLength;
            public string sceneHash, fileHash;
        }

        private const int FreePromptLength = 1448;
        private const string FreePromptHash = "4dee888c1d9aa42be9d677c407c5a221";

        // Waiting times of the quick preview. They do not come from the study.
        private static readonly float[] QuickSeconds = { 2f, 12f, 19.5f, 0f, 0f, 2f, 35f, 94f, 0f };

        internal static readonly string[] Captions =
        {
            "Welcome here. In front of you, you can see your conversational partner. In this session, you will generate ideas, " +
            "discuss and debate with your partner given a specific topic.",
            "Before we start, both of you will perform a series of movements as a warm-up activity. Please remember your order of " +
            "movement from the board before it disappears. Then we will tell you to start.",
            "You can start now.",
            "Great job! Now, you can see the background information and the distinct perspective you will represent on the " +
            "instruction board. You will now have one minute to think of your argument and supporting evidence before we come to the debate.",
            "Excellent. Please start the five-minute debate now.",
            "Time's up! Thanks for your great performance and that's the end of this session."
        };

        private static readonly TopicSource[] Sources =
        {
            new TopicSource
            {
                file = "Social media", scene = "03 I-S", id = "social", letter = "S", title = "Social media",
                question = "Does social media bring people together or drive people further apart?",
                user = "Social media drives people apart.", agent = "Social media brings people together.",
                sceneLength = 10738, sceneHash = "31dd96d49bb906901eb3452bbd7f2405",
                fileLength = 10648, fileHash = "734862f5467aa37ac68eb04876b99972"
            },
            new TopicSource
            {
                file = "Remote work", scene = "07 I-W", id = "work", letter = "W", title = "Remote work",
                question = "Does remote work bring more harm than good or good than harm?",
                user = "Remote work brings more harm than good.", agent = "Remote work brings more good than harm.",
                sceneLength = 10700, sceneHash = "cc5aaf53e9a6a49632b5b9239ff35170",
                fileLength = 10610, fileHash = "9445605930148e2adf647a797718a164"
            },
            new TopicSource
            {
                file = "Environment and economy", scene = "11 I-E", id = "environment", letter = "E", title = "Environment and economy",
                question = "Which should be given greater priority, environmental protection or economic development?",
                user = "Environmental protection should be given greater priority.",
                agent = "Economic development should be given greater priority.",
                sceneLength = 10950, sceneHash = "ca160dbd0a8f4856dd76aa60ae46d457",
                fileLength = 10860, fileHash = "bfa60b904088a36e314eecb1b8afd8ee"
            },
            new TopicSource
            {
                file = "Personal growth", scene = "15 I-G", id = "growth", letter = "G", title = "Personal growth",
                question = "Which is better for personal growth: challenging conditions or favorable conditions?",
                user = "Challenging conditions are better for personal growth.",
                agent = "Favorable conditions are better for personal growth.",
                sceneLength = 10882, sceneHash = "6f3d4d5248a405827c452f0d8d2949e7",
                fileLength = 10792, fileHash = "717761e5569e88d27180e3a73b3e919c"
            }
        };

        [MenuItem(AllInOnePaths.Menu + "Import study content")]
        public static void Import()
        {
            Directory.CreateDirectory(AllInOnePaths.Topics);
            foreach (TopicSource source in Sources)
            {
                WithScene(AllInOnePaths.StudyScenes + source.scene + ".unity", scene =>
                {
                    ImportTopic(source, scene);
                    if (source == Sources[0]) ImportSession(scene);
                });
            }
            WithScene(AllInOnePaths.BaseScene, ImportFreePrompt);
            Debug.Log("[AIO] Study content imported.");
        }

        /// <summary>Lengths and checksums of the prompt files, for the smoke test.</summary>
        internal static (string path, int length, string hash)[] PromptFiles() =>
            Sources.Select(s => (PromptPath(s), s.fileLength, s.fileHash)).ToArray();

        internal static string Hash(string text)
        {
            using (MD5 md5 = MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        private static string PromptPath(TopicSource source) => AllInOnePaths.Topics + source.file + ".prompt.txt";

        private static void ImportTopic(TopicSource source, Scene scene)
        {
            string prompt = new SerializedObject(Find(scene, "RealtimeAPIWrapper")).FindProperty("systemInstructions").stringValue;
            Check(prompt, source.sceneLength, source.sceneHash, source.scene);
            TextAsset promptFile = WriteText(PromptPath(source), prompt);
            Check(promptFile.text, source.fileLength, source.fileHash, promptFile.name);

            var study = new SerializedObject(Find(scene, "StudyController"));
            SerializedProperty actions = study.FindProperty("familiarizationActions");

            var topic = LoadOrCreate<AgentTopic>(AllInOnePaths.Topics + source.file + ".asset");
            topic.id = source.id;
            topic.studyLetter = source.letter;
            topic.title = source.title;
            topic.question = source.question;
            topic.userPosition = source.user;
            topic.agentPosition = source.agent;
            topic.warmUpBoard = Prefab(study, "panel1");
            topic.instructionBoard = Prefab(study, "panel2");
            topic.warmUpActions = Enumerable.Range(0, actions.arraySize).Select(i => actions.GetArrayElementAtIndex(i).stringValue).ToArray();
            topic.instructions = promptFile;
            Save(topic);
        }

        private static void ImportSession(Scene scene)
        {
            var study = new SerializedObject(Find(scene, "StudyController"));
            AudioClip[] clips = new[] { "audio1_Welcome", "audio2_FamiliarizationIntro", "audio3_StartActions", "audio4_PrepDebate", "audio5_StartDebate", "audio6_EndStudy" }
                .Select(name => (AudioClip)Reference(study, name)).ToArray();

            var script = LoadOrCreate<AgentSessionScript>(AllInOnePaths.Session);
            script.steps = new[]
            {
                Step("ready", "Get ready", "The session starts in a moment.", -1, Seconds(study, "time0_AvatarLoadDelay")),
                Step("welcome", "Welcome", "Listen to the instructions.", 0, Seconds(study, "time1")),
                Step("warmup-board", "Warm-up instructions", "Remember the order of the movements on the board.", 1, Seconds(study, "time2"), AgentSessionScript.Kind.Wait, AgentSessionScript.Board.WarmUp),
                Step("warmup-start", "Start", "The warm-up starts now.", 2, 0f, AgentSessionScript.Kind.ClipLength),
                Step("warmup", "Warm-up", "Do the movements in the order you remember. Amy does hers at the same time.", -1, 0f, AgentSessionScript.Kind.WarmUp),
                Step("pause", "Short pause", "Relax your arms.", -1, Seconds(study, "time3")),
                Step("preparation", "Preparation", "Read the board and think about your arguments.", 3, Seconds(study, "time4_PrepDuration"), AgentSessionScript.Kind.Wait, AgentSessionScript.Board.Instruction),
                Step("debate", "Debate", "Talk with Amy. You may interrupt her.", 4, Seconds(study, "time5_DebateDuration"), AgentSessionScript.Kind.Conversation),
                Step("end", "End", "The session ends here.", 5, 0f, AgentSessionScript.Kind.ClipLength)
            };
            for (int i = 0; i < script.steps.Length; i++) script.steps[i].quickSeconds = QuickSeconds[i];
            script.reminders = new[] { 180f, 60f };
            script.reminderBoards = new[] { Prefab(study, "panelWarning3Min"), Prefab(study, "panelWarning1Min") };
            script.reminderSeconds = 3f;
            Save(script);

            AgentSessionScript.Step Step(string id, string title, string hint, int clip, float seconds,
                AgentSessionScript.Kind kind = AgentSessionScript.Kind.Wait, AgentSessionScript.Board board = AgentSessionScript.Board.None)
            {
                return new AgentSessionScript.Step
                {
                    id = id, title = title, hint = hint, studySeconds = seconds, kind = kind, board = board,
                    clip = clip >= 0 ? clips[clip] : null, caption = clip >= 0 ? Captions[clip] : ""
                };
            }
        }

        private static void ImportFreePrompt(Scene scene)
        {
            string prompt = new SerializedObject(Find(scene, "RealtimeAPIWrapper")).FindProperty("systemInstructions").stringValue;
            Check(prompt, FreePromptLength, FreePromptHash, scene.name);
            WriteText(AllInOnePaths.FreePrompt, prompt);
        }

        private static void WithScene(string path, Action<Scene> read)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                read(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static MonoBehaviour Find(Scene scene, string typeName)
        {
            MonoBehaviour found = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(m => m != null && m.GetType().Name == typeName);
            return found != null ? found : throw new InvalidOperationException(typeName + " is missing in " + scene.name);
        }

        private static float Seconds(SerializedObject from, string property) => from.FindProperty(property).floatValue;

        private static Object Reference(SerializedObject from, string property)
        {
            Object value = from.FindProperty(property).objectReferenceValue;
            return value != null ? value : throw new InvalidOperationException(property + " is not set");
        }

        private static GameObject Prefab(SerializedObject from, string property) =>
            (GameObject)PrefabUtility.GetCorrespondingObjectFromSource(Reference(from, property));

        private static void Check(string text, int length, string hash, string what)
        {
            if (text.Length != length || Hash(text) != hash)
                throw new InvalidOperationException("The prompt of " + what + " is not the expected one (" + text.Length + " characters).");
        }

        private static TextAsset WriteText(string path, string text)
        {
            File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }

        internal static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        internal static void Save(Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}