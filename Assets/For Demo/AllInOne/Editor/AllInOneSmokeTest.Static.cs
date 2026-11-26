using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>Tests of the content and of the scene files, without play mode.</summary>
    public static partial class AllInOneSmokeTest
    {
        private const string Controller = "Assets/Shared/Animations/Core/Torso/AgentAnimatorCoreTorso.controller";
        private const float WarmUpOverlap = 0.2f;

        private static readonly string[] ForbiddenTypes =
        {
            "SessionController", "VRMovementDataLog", "VRMotionRecorder", "VRMotionPlayback", "DemoIntegration", "VRConnectionControllerXRI"
        };

        private static readonly Dictionary<string, (string warmUp, string instruction, string actions)> ExpectedTopics =
            new Dictionary<string, (string, string, string)>
            {
                { "S", ("11cdef1658fe28045b58b3915f7a441d", "d73f6ad9b73d6e846b8bf3800fc2eba5", "Right Hand Raising,Left Hand Raising,Neck Stretching,Neck Stretching 2,Standard Idle,Look Around") },
                { "W", ("c77dc5c5276a99e41a486f02291413c8", "e7f0a3d3edd6756429325b73a6dcb48c", "Left Hand Raising,Right Hand Raising,Neck Stretching,Neck Stretching 2,Standard Idle,Look Around") },
                { "E", ("b6a2ebc17c87b3149a6524aa4bb8f2a6", "baec447acffbb8c42b9821d235e5b26d", "Neck Stretching,Neck Stretching 2,Left Hand Raising,Right Hand Raising,Standard Idle,Look Around") },
                { "G", ("178cc1620f536954687595236408f744", "99c19e33d1838d64284bf14fa7ac8601", "Neck Stretching,Neck Stretching 2,Right Hand Raising,Left Hand Raising,Standard Idle,Look Around") }
            };

        // The objects that differ between the conditions, by file id, and their values for M, L, I, N (study scenes)
        private static readonly (string id, string field, string values)[] ConditionTable =
        {
            ("6094179411900915664", "instance", "1000"),
            ("1266740224", "instance", "0101"),
            ("467970614", "m_Enabled", "1010"),
            ("467970615", "m_Enabled", "0101"),
            ("2075723661", "m_Enabled", "0111"),
            ("754257952", "m_Enabled", "1000"),
            ("584001971", "m_IsActive", "0001"),
            ("1464266162", "m_IsActive", "0001"),
            ("1042135785", "instance", "0001"),
            ("1042135790", "m_Enabled", "1110")
        };

        private static void RunStaticTests()
        {
            StaticTest("T1 content", TestContent);
            StaticTest("T2 scene", TestScene);
            StaticTest("T3 study fidelity", TestStudyFidelity);
        }

        private static void TestContent()
        {
            foreach ((string path, int length, string hash) in StudyContentImporter.PromptFiles())
            {
                string text = AssetDatabase.LoadAssetAtPath<TextAsset>(path).text;
                Check(text.Length == length && StudyContentImporter.Hash(text) == hash, path + " is not the study prompt");
            }

            AgentTopic[] topics = AssetDatabase.FindAssets("t:AgentTopic").Select(g => AssetDatabase.LoadAssetAtPath<AgentTopic>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            Check(topics.Length == 4, topics.Length + " topics");
            foreach (AgentTopic topic in topics)
            {
                (string warmUp, string instruction, string actions) = ExpectedTopics[topic.studyLetter];
                Check(Guid(topic.warmUpBoard) == warmUp, topic.name + ": warm-up board");
                Check(Guid(topic.instructionBoard) == instruction, topic.name + ": instruction board");
                Check(string.Join(",", topic.warmUpActions) == actions, topic.name + ": warm-up order");
                Check(topic.instructions != null && topic.question.Length > 0 && topic.userPosition.Length > 0 && topic.agentPosition.Length > 0, topic.name + ": texts");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<AgentAvatarCatalog>(AllInOnePaths.Avatars);
            Check(catalog.avatars.Length == 6, catalog.avatars.Length + " avatars");
            foreach (AgentAvatarCatalog.Entry a in catalog.avatars)
                Check(a.prefab != null && a.definition != null && a.definition.isHuman && a.thumbnail != null, a.id + " is incomplete");

            var script = AssetDatabase.LoadAssetAtPath<AgentSessionScript>(AllInOnePaths.Session);
            float[] clips = script.steps.Where(s => s.clip != null).Select(s => s.clip.length).ToArray();
            float[] expected = { 11.26f, 13.49f, 1.32f, 15.00f, 3.86f, 4.37f };
            Check(clips.Length == 6 && clips.Zip(expected, (a, b) => Mathf.Abs(a - b)).All(d => d < 0.05f), "narration: " + string.Join(", ", clips));
            float[] times = script.steps.Where(s => s.studySeconds > 0f).Select(s => s.studySeconds).ToArray();
            Check(times.SequenceEqual(new[] { 5f, 13f, 23f, 3f, 79f, 305f }), "times: " + string.Join(", ", times));
            Check(script.reminders.SequenceEqual(new[] { 180f, 60f }) && script.reminderBoards.All(b => b != null), "reminders");

            float warmUpSeconds = WarmUpSeconds(topics[0]);
            float study = script.steps.Sum(s => Length(s, SessionPace.Study, warmUpSeconds)), quick = script.steps.Sum(s => Length(s, SessionPace.Quick, warmUpSeconds));
            Check(Mathf.Abs(study - 466.3f) < 1f && Mathf.Abs(quick - 202.8f) < 1f, "session length " + study + " s, " + quick + " s");
            Note("session " + study.ToString("0.0") + " s, quick preview " + quick.ToString("0.0") + " s");
        }

        private static float WarmUpSeconds(AgentTopic topic)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            return topic.warmUpActions.Sum(a => controller.animationClips.First(c => c.name == a).length - WarmUpOverlap);
        }

        private static float Length(AgentSessionScript.Step step, SessionPace pace, float warmUp)
        {
            if (step.kind == AgentSessionScript.Kind.WarmUp) return warmUp;
            return step.kind == AgentSessionScript.Kind.ClipLength ? step.clip.length : step.Seconds(pace);
        }

        private static void TestScene()
        {
            Scene scene = EditorSceneManager.OpenScene(AllInOnePaths.Scene, OpenSceneMode.Single);
            Transform[] objects = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach (Transform t in objects)
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "missing script on " + t.name);

            Component[] components = objects.SelectMany(t => t.GetComponents<Component>()).Where(c => c != null).ToArray();
            foreach (Component c in components)
            {
                Check(c.GetType().Assembly.GetName().Name != "Assembly-CSharp", c.GetType().Name + " comes from Assembly-CSharp");
                Check(!ForbiddenTypes.Contains(c.GetType().Name), c.GetType().Name + " on " + c.name);
                CheckReferences(c);
            }
            Check(components.OfType<AudioListener>().Count(l => l.isActiveAndEnabled) == 1, "audio listeners");
            Check(components.OfType<Camera>().Count(c => c.CompareTag("MainCamera")) == 1, "main cameras");

            foreach (RealtimeAPIWrapper api in components.OfType<RealtimeAPIWrapper>())
                Check(BuildAllInOne.HasNoKey(api), "the key field of " + api.name + " is not empty");
            Check(!Regex.IsMatch(File.ReadAllText(AllInOnePaths.Scene), BuildAllInOne.KeyPattern), "the scene file contains something like a key");

            EditorBuildSettingsScene[] list = EditorBuildSettings.scenes;
            Check(list.Length > 1 && list[0].path == AllInOnePaths.Scene && list[0].enabled && list.Skip(1).All(s => !s.enabled), "build settings");
            Check(list.Select(s => s.path).Distinct().Count() == list.Length && list.All(s => File.Exists(s.path)), "entries of the build settings");
        }

        private static void CheckReferences(Component c)
        {
            SerializedProperty p = new SerializedObject(c).GetIterator();
            while (p.NextVisible(true))
            {
                bool isMissing = p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue == null && p.objectReferenceEntityIdValue != EntityId.None;
                Check(!isMissing, "missing reference " + c.name + " " + c.GetType().Name + "." + p.propertyPath);
            }
        }

        private static void TestStudyFidelity()
        {
            string[] scenes = Directory.GetFiles(AllInOnePaths.StudyScenes, "*.unity").OrderBy(f => f).ToArray();
            Check(scenes.Length == 16, scenes.Length + " study scenes");
            foreach (string path in scenes)
            {
                string text = File.ReadAllText(path).Replace("\r\n", "\n"), name = Path.GetFileNameWithoutExtension(path);
                int condition = "MLIN".IndexOf(name[3]);
                foreach ((string id, string field, string values) in ConditionTable)
                {
                    string document = Document(text, id);
                    string pattern = field == "instance" ? "propertyPath: m_IsActive\n\\s+value: (\\d)" : "\n  " + field + ": (\\d)";
                    Match match = Regex.Match(document, pattern);
                    Check(match.Success && match.Groups[1].Value[0] == values[condition], name + ": " + id + " " + field);
                }
                CheckPartner(text, name, name[3] == 'N');
            }
            CompareModulation();
        }

        /// <summary>The partner of Modulation L is the recording in Noise and the agent otherwise.</summary>
        private static void CheckPartner(string text, string name, bool isRecording)
        {
            string unit = Document(text, "1266740224");
            for (int pair = 3; pair <= 5; pair++)
            {
                Match source = Regex.Match(unit, "propertyPath: pairs\\.Array\\.data\\[" + pair + "\\]\\.source\n\\s+value:[^\n]*\n\\s+objectReference: {fileID: (\\d+)");
                Match owner = Regex.Match(Document(text, source.Groups[1].Value), "m_PrefabInstance: {fileID: (\\d+)");
                Check(owner.Success && owner.Groups[1].Value == "1042135785" == isRecording, name + ": partner " + pair);
            }
        }

        private static void CompareModulation()
        {
            string demo = ActiveValues("Assets/For Demo/Prefabs/Modulation M.prefab"), study = ActiveValues("Assets/For Study/Prefab/Modulation M.prefab");
            Check(demo == study, "Modulation prefab: " + demo + " against " + study);
            Check(study == "followRatio 0.3 x3, followRatio 1 x1, lerpTime 0.2 x3", "values: " + study);
            Note("active values: " + study);
        }

        private static string ActiveValues(string path)
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            IEnumerable<string> values = text.Split(new[] { "--- !u!" }, StringSplitOptions.None)
                .Where(d => d.StartsWith("114 ") && d.Contains("\n  m_Enabled: 1"))
                .SelectMany(d => Regex.Matches(d, "\n  (followRatio|lerpTime): ([0-9.]+)").Cast<Match>())
                .Select(m => m.Groups[1].Value + " " + float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            return string.Join(", ", values.GroupBy(v => v).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " x" + g.Count()));
        }

        private static string Document(string text, string id)
        {
            Match start = Regex.Match(text, "--- !u!\\d+ &" + id + "( stripped)?\n");
            if (!start.Success) return "";
            int end = text.IndexOf("\n--- !u!", start.Index + start.Length, StringComparison.Ordinal);
            return text.Substring(start.Index, (end < 0 ? text.Length : end) - start.Index);
        }

        private static string Guid(UnityEngine.Object asset) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
    }
}
