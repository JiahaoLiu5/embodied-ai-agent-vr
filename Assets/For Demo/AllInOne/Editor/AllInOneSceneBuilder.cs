using System;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>
    /// Builds AllInOne.unity from the scene 02_Independent: the same room, bodies, rigs, and transformations, plus the parts of
    /// the integrated demo. Stops when something it needs is missing.
    /// </summary>
    public static class AllInOneSceneBuilder
    {
        private const int PanelLayer = 1;

        private static readonly string[] NotNeeded =
        {
            "Session Manager", "Board", "DataLoggerObject", "DemoUI", "EventSystem", "Agent Recorder and Playback"
        };

        private static readonly string[] Recordings =
        {
            "VRRecording_20260818_144501_RLN", "VRRecording_20260818_163723_LRN",
            "VRRecording_20260818_165120_NRL", "VRRecording_20260818_170932_NLR"
        };

        private static readonly string[] TopicFiles = { "Social media", "Remote work", "Environment and economy", "Personal growth" };

        private static Scene scene;

        [MenuItem(AllInOnePaths.Menu + "Rebuild scene")]
        public static void Build()
        {
            scene = EditorSceneManager.OpenScene(AllInOnePaths.BaseScene, OpenSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
                if (NotNeeded.Contains(root.name)) Object.DestroyImmediate(root);

            GameObject user = Root("XR Origin (XR Rig) ECA 2"), userBody = Root("Skeleton_Rig");
            GameObject agentBody = Root("Agent VR Character IK"), agentCapture = Root("Agent Capture Mixamo");
            GameObject agentRig = Root("XR Origin (XR Rig) Mixamo"), recordingRig = Root("XR Origin (XR Rig) Mixamo Recording");
            GameObject modulationM = Root("Modulation M"), modulationL = Root("Modulation L"), room = Root("HexagonEnv");
            RealtimeAPIWrapper api = Root("RealtimeAPI").GetComponent<RealtimeAPIWrapper>();

            recordingRig.SetActive(false);
            recordingRig.GetComponent<TransformAlignment>().enabled = false;
            modulationM.SetActive(false);
            modulationL.SetActive(false);
            SetUpVoice(api, agentBody);
            SetUpMirrors(room);

            var app = new GameObject("AllInOne");
            AudioSource narration = CreateNarration(app.transform);
            Transform boardParent = new GameObject("Boards").transform;
            boardParent.SetParent(app.transform, false);

            UserRig rig = app.AddComponent<UserRig>();
            Set(rig, ("origin", user.GetComponent<XROrigin>()), ("viewCamera", Child(user, "Camera Offset/Main Camera").GetComponent<Camera>()),
                ("leftController", Child(user, "Camera Offset/Left Controller")), ("rightController", Child(user, "Camera Offset/Right Controller")),
                ("hiddenFromObserver", 1 << PanelLayer));

            var recorded = app.AddComponent<RecordedMotionSource>();
            Set(recorded, ("recordings", Recordings.Select(n => Asset<TextAsset>("Assets/Shared/SampleMotion/" + n + ".csv")).ToArray()),
                ("playbackSpace", Child(recordingRig, "Camera Offset")), ("targets", RigTargets(recordingRig)));

            ConditionSwitcher switcher = AddSwitcher(app, user, userBody, agentBody, agentRig, recordingRig, modulationM, modulationL, recorded);
            AvatarDresser dresser = userBody.AddComponent<AvatarDresser>();
            MirrorFigure figure = CreateFigure(app, user, userBody);

            AudioRecorder recorder = api.audioRecorder;
            var conversation = app.AddComponent<ConversationController>();
            Set(conversation, ("api", api), ("recorder", recorder), ("player", api.audioPlayer), ("narration", narration));
            var speech = app.AddComponent<SpeechCaptions>();
            Set(speech, ("api", api), ("player", api.audioPlayer));
            var presence = app.AddComponent<AgentPresence>();
            Set(presence, ("capture", agentCapture.GetComponent<Animator>()), ("body", agentBody.GetComponent<Animator>()), ("api", api));
            var boards = app.AddComponent<BoardPresenter>();
            Set(boards, ("parent", boardParent));
            var director = app.AddComponent<SessionDirector>();
            Set(director, ("script", Asset<AgentSessionScript>(AllInOnePaths.Session)), ("narration", narration), ("boards", boards),
                ("presence", presence), ("conversation", conversation), ("switcher", switcher));
            var recentre = app.AddComponent<RecentreControl>();
            Set(recentre, ("rig", rig), ("switcher", switcher), ("body", dresser));
            var controller = app.AddComponent<AllInOneController>();
            Set(controller, ("catalog", Asset<AgentAvatarCatalog>(AllInOnePaths.Avatars)),
                ("topics", TopicFiles.Select(f => Asset<AgentTopic>(AllInOnePaths.Topics + f + ".asset")).ToArray()),
                ("freePrompt", Asset<TextAsset>(AllInOnePaths.FreePrompt)), ("rig", rig), ("recentre", recentre), ("switcher", switcher),
                ("conversation", conversation), ("director", director), ("presence", presence), ("boards", boards),
                ("speechCaptions", speech), ("body", dresser), ("figure", figure));

            EditorSceneManager.SaveScene(scene, AllInOnePaths.Scene);
            ListInBuildSettings();
            Debug.Log("[AIO] Scene rebuilt: " + AllInOnePaths.Scene);
        }

        /// <summary>Puts the integrated demo first. All other scenes stay in the list, switched off.</summary>
        private static void ListInBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(AllInOnePaths.Scene, true) };
            list.AddRange(EditorBuildSettings.scenes.Where(s => s.path != AllInOnePaths.Scene).Select(s => new EditorBuildSettingsScene(s.path, false)));
            EditorBuildSettings.scenes = list.ToArray();
        }

        private static void SetUpVoice(RealtimeAPIWrapper api, GameObject agentBody)
        {
            Object.DestroyImmediate(api.GetComponent<VRConnectionControllerXRI>());
            Set(api.audioRecorder, ("listeningMode", (int)ListeningMode.PushToTalk), ("interruptResponseOnNewRecording", true),
                ("endSilenceSeconds", 0.6f), ("computeSpectrum", false));
            Set(api.audioPlayer, ("computeSpectrum", false));

            GameObject audio = api.audioPlayer.gameObject;
            var voice = audio.GetComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.minDistance = 2f;
            voice.maxDistance = 15f;
            voice.rolloffMode = AudioRolloffMode.Linear;

            // The blend shape with this number is not a smile on these heads.
            audio.GetComponent<OVRLipSyncContextMorphTarget>().laughterBlendTarget = -1;

            Transform head = agentBody.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
            Set(audio.AddComponent<FollowTransform>(), ("target", head), ("frame", agentBody.transform), ("offset", new Vector3(0f, 0f, 0.08f)));
        }

        private static void SetUpMirrors(GameObject room)
        {
            Camera[] cameras = room.GetComponentsInChildren<Camera>(true);
            if (cameras.Length != 3) throw new InvalidOperationException("The room needs three mirror cameras.");
            Set(room.AddComponent<MirrorThrottle>(), ("cameras", cameras), ("hidden", 1 << PanelLayer));
        }

        private static AudioSource CreateNarration(Transform parent)
        {
            var narration = new GameObject("Narration").AddComponent<AudioSource>();
            narration.transform.SetParent(parent, false);
            narration.playOnAwake = false;
            narration.spatialBlend = 0f;
            return narration;
        }

        private static ConditionSwitcher AddSwitcher(GameObject app, GameObject user, GameObject userBody, GameObject agentBody, GameObject agentRig,
            GameObject recordingRig, GameObject modulationM, GameObject modulationL, RecordedMotionSource recorded)
        {
            IKTargetFollowVRRig[] userFollowers = userBody.GetComponents<IKTargetFollowVRRig>();
            TransformAlignment[] agentFollowers = agentBody.GetComponents<TransformAlignment>();
            var switcher = app.AddComponent<ConditionSwitcher>();
            Set(switcher, ("modulationM", modulationM), ("modulationL", modulationL), ("recordingRig", recordingRig), ("recorded", recorded),
                ("userRaw", userFollowers.First(f => f.head.vrTarget.root == user.transform)),
                ("userModulated", userFollowers.First(f => f.head.vrTarget.root == modulationL.transform)),
                ("agentRaw", agentFollowers.First(a => a.pairs[0].source.root == agentRig.transform)),
                ("agentModulated", agentFollowers.First(a => a.pairs[0].source.root == modulationM.transform)),
                ("userRig", RigTargets(user)), ("agentRig", RigTargets(agentRig)), ("recordingTargets", RigTargets(recordingRig)));
            return switcher;
        }

        /// <summary>Copies the user's body. The copy follows three targets that the figure moves.</summary>
        private static MirrorFigure CreateFigure(GameObject app, GameObject user, GameObject userBody)
        {
            GameObject copy = Object.Instantiate(userBody);
            SceneManager.MoveGameObjectToScene(copy, scene);
            copy.name = "Mirror Figure";
            foreach (Transform part in copy.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = PanelLayer;

            Transform targets = new GameObject("Mirror Targets").transform;
            Transform[] mirrored = new[] { "Head", "Left", "Right" }.Select(name =>
            {
                Transform target = new GameObject(name).transform;
                target.SetParent(targets, false);
                return target;
            }).ToArray();

            IKTargetFollowVRRig[] followers = copy.GetComponents<IKTargetFollowVRRig>();
            IKTargetFollowVRRig kept = followers.First(f => f.head.vrTarget.root == user.transform);
            foreach (IKTargetFollowVRRig other in followers)
                if (other != kept) Object.DestroyImmediate(other);
            kept.head.vrTarget = mirrored[0];
            kept.leftHand.vrTarget = mirrored[1];
            kept.rightHand.vrTarget = mirrored[2];
            kept.enabled = true;
            copy.SetActive(false);

            IKTargetFollowVRRig source = userBody.GetComponent<IKTargetFollowVRRig>();
            var figure = app.AddComponent<MirrorFigure>();
            Set(figure, ("body", copy.GetComponent<AvatarDresser>()), ("targets", mirrored),
                ("userTargets", new[] { source.head.ikTarget, source.leftHand.ikTarget, source.rightHand.ikTarget }));
            return figure;
        }

        private static Transform[] RigTargets(GameObject rig)
        {
            return new[]
            {
                Child(rig, "Camera Offset/Main Camera/Head VR Target"),
                Child(rig, "Camera Offset/Left Controller/Left Hand VR Target"),
                Child(rig, "Camera Offset/Right Controller/Right Hand VR Target")
            };
        }

        private static GameObject Root(string name)
        {
            GameObject found = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name);
            return found != null ? found : throw new InvalidOperationException(name + " is missing in " + scene.name);
        }

        private static Transform Child(GameObject root, string path)
        {
            Transform found = root.transform.Find(path);
            return found != null ? found : throw new InvalidOperationException(root.name + "/" + path + " is missing");
        }

        private static T Asset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null ? asset : throw new InvalidOperationException(path + " is missing");
        }

        /// <summary>Sets serialized fields of a component.</summary>
        private static void Set(Object target, params (string name, object value)[] fields)
        {
            var serialized = new SerializedObject(target);
            foreach ((string name, object value) in fields)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " does not exist");
                if (value is Array items)
                {
                    property.arraySize = items.Length;
                    for (int i = 0; i < items.Length; i++) Assign(property.GetArrayElementAtIndex(i), items.GetValue(i), name);
                }
                else Assign(property, value, name);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Assign(SerializedProperty property, object value, string name)
        {
            switch (value)
            {
                case bool b: property.boolValue = b; break;
                case int i: property.intValue = i; break;
                case float f: property.floatValue = f; break;
                case Vector3 v: property.vector3Value = v; break;
                case Object o when o != null: property.objectReferenceValue = o; break;
                default: throw new InvalidOperationException(name + " has no value");
            }
        }
    }
}