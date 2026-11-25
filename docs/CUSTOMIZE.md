# Customize

Short recipes. Make a copy of a scene before you change it (**Edit > Duplicate** on the scene asset), so that the original stays available for comparison. Do not rename scripts, classes or serialized fields: scenes and prefabs refer to them.

1. [Change who the agent is](#1-change-who-the-agent-is)
2. [Tune turn taking](#2-tune-turn-taking)
3. [Add a gesture](#3-add-a-gesture)
4. [Change the coupling](#4-change-the-coupling)
5. [Switch the coupling mode in one scene](#5-switch-the-coupling-mode-in-one-scene)
6. [Add a user avatar](#6-add-a-user-avatar)
7. [Use an agent without coupling](#7-use-an-agent-without-coupling)
8. [Change the guided session or add a step](#8-change-the-guided-session-or-add-a-step)
9. [Use another model or server](#9-use-another-model-or-server)
10. [Record and play your own motion](#10-record-and-play-your-own-motion)
11. [Log extra data](#11-log-extra-data)

## 1. Change who the agent is

Select `RealtimeAPI` in the scene.

- **System Instructions** is the prompt for the whole conversation. The shipped prompt has four parts: role, conversation style, animation choreography, animation guide. Keep the last two, or the model will not choose gestures sensibly.
- **Voice** takes one of the names in the tooltip.

Changes apply at the next connection. The prompt is stored per scene.

## 2. Tune turn taking

Select `RealtimeAPI > Audio`, component `AudioRecorder`.

| You want | Change |
| --- | --- |
| The agent waits while you think | `endSilenceSeconds` up (0.6 in the scenes) |
| A faster reply | `endSilenceSeconds` down |
| The agent always finishes its sentence | `interruptResponseOnNewRecording` off |
| Less reaction to noise | `startMarginAboveNoiseDb` up, or `minimumStartDbFs` up (for example -45) |
| Quiet speech is noticed | `minimumStartDbFs` and `minimumContinueDbFs` down |
| Push to talk | `listeningMode` = `PushToTalk`. In the Editor the debug panel uses the space bar. In a headset call `StartRecording()` and `StopRecording()` from your own input action, and disable `VRConnectionControllerXRI`, which switches to VAD at the start |

Watch **Runtime VAD diagnostics** on the same component while you speak. Wear earphones, so that the agent's voice is not picked up as input.

## 3. Add a gesture

1. Import or make a clip for the avatar's bones. Paths must start with `Armature_1/Hips/...`, as in the shipped clips.
2. Make a second copy without the bones Head and, on both sides, Arm, ForeArm, Hand, UpLeg, Leg and Foot. The visible body takes these from IK.
3. Drag the full clip into `Animations/Core/AgentAnimatorCore` and the second one into `Animations/Core/Torso/AgentAnimatorCoreTorso`. Give both states the same name.
4. In each controller, add a transition from the new state to `Idle` with: **Has Exit Time** on, **Exit Time** 1, **Fixed Duration** on, **Transition Duration** 1 s, **Transition Offset** 0, **Interruption Source** None.
5. Add the state name to **Gesture Names** on `RealtimeAPI`.
6. Describe in the prompt when to use it.

Test without a key: open `Demo/DesktopDemo`, or add the component `GesturePreview` to an empty object, press Play and G, and click the name.

Both idle clips are 8.9 s long with **Loop Time** off. Turn it on in both clips if the agent should keep moving while nobody speaks.

## 4. Change the coupling

Select `Modulation M` (agent follows user) or `Modulation L` (user's avatar follows agent or recording) in the scene, or edit the prefab to change both.

| Goal | Setting |
| --- | --- |
| Strength | `followRatio` on `Head Modulated`, `Left Hand Modulated` and `Right Hand Modulated`. Leave `Head Plane` at 1 |
| Head only | `followRatio` = 0 on the two hands |
| Hands only | `followRatio` = 0 on `Head Modulated` |
| Slower or smoother | `lerpTime` on `Head Partner Slow`, `Left Hand Partner Slow`, `Right Hand Partner Slow` |
| A fixed delay | Replace `SlowFollowSystem` by a script that buffers poses and writes the pose from `n` seconds ago to the same transform |

At run time:

```csharp
foreach (var hand in modulation.GetComponentsInChildren<HandMixedFollowSystemFinal>())
    hand.followRatio = ratio;
foreach (var head in modulation.GetComponentsInChildren<HeadRotationMixedFollowSystem>())
    if (head.name == "Head Modulated") head.followRatio = ratio;
```

Keep the `Modulation` root unrotated. Call `ResetRotationContinuity()` on the hands after a jump in tracking.

## 5. Switch the coupling mode in one scene

Each mode is a set of switches. All scenes `02_...` contain the same objects, so you can set them in any of them.

| Object and component | Independent | Agent follows user | User avatar follows agent | Recorded source |
| --- | --- | --- | --- | --- |
| `Modulation M` (object) | off | on | off | off |
| `Modulation L` (object) | off | off | on | on |
| `Skeleton_Rig`, `IKTargetFollowVRRig` with targets from the user rig | on | on | off | off |
| `Skeleton_Rig`, `IKTargetFollowVRRig` with targets from `Modulation L` | off | off | on | on |
| `Agent VR Character IK`, `TransformAlignment` with sources from the agent rig | on | off | on | on |
| `Agent VR Character IK`, `TransformAlignment` with sources from `Modulation M` | off | on | off | off |
| `Agent Recorder and Playback` and `VR Motion Playback` (objects) | off | off | off | on |
| `XR Origin (XR Rig) Mixamo Recording` (object on, its `TransformAlignment` off) | off | off | off | on |
| `Modulation L`, `TransformAlignment`, pairs 3 to 5, sources | | | Agent rig | Recording rig |

## 6. Add a user avatar

1. Export an avatar with the same bone names as the shipped ones (Avaturn GLB). Put it into `Assets/EmbodiedAgent/Avatars/Final`.
2. Make a prefab of it. The humanoid definition is chosen by the prefab name: it should contain "female" or "male". Otherwise the `forceMale` setting decides.
3. Assign the prefab to one of the six slots of `SessionController`, or to `sourcePrefab` of `UniversalAvatarLoaderNew`.

An avatar with fewer sub-meshes than the previous one leaves the extra meshes of the previous one visible. Delete them on the target if this happens.

To give the **agent** another body, start from the GLB, rebuild the IK rig on it, and set the mesh references of `OVRLipSyncContextMorphTarget` and `VisemeSync`. Check **Viseme To Blend Targets**: the numbers are the blend shape indices of the head mesh. If its blend shapes use weights from 0 to 100, scale the weights in `OVRLipSyncContextMorphTarget` by 100 at the three marked places and set `maxBlinkWeight` to 100.

## 7. Use an agent without coupling

In scene 01 or 03:

1. Deactivate `Agent Capture Mixamo` and `Agent VR Character IK`, activate `Agent Capture Mixamo Test`.
2. On `Audio`, set the meshes of `OVRLipSyncContextMorphTarget` and `VisemeSync` to the head, teeth and tongue of this body.
3. Add `EyeAnimationHandlerNew` to it.

The body then plays the full clips directly, without IK.

## 8. Change the guided session or add a step

Select `Session Manager` (component `SessionController`).

- Times, panels, optional narration clips and the warm-up movements are Inspector fields. Warm-up movements are Animator state names that exist in both controllers.
- Texts are on the board prefabs in `Assets/EmbodiedAgent/UI/Boards` and its subfolder; select a board in the scene to find its prefab. The instruction board contains placeholders: write your own activity, instructions and roles. The warm-up board lists the warm-up movements, so change it together with `familiarizationActions`.
- To talk at once, set `time0` to `time4` to 0 and empty the list of warm-up movements.

To add a step, insert a block in `SessionTimelineRoutine()` of [`SessionController`](../Assets/EmbodiedAgent/Scripts/Session/SessionController.cs):

```csharp
// New step: show a panel for 20 seconds
if (extraPanel != null) extraPanel.SetActive(true);
yield return new WaitForSeconds(20f);
if (extraPanel != null) extraPanel.SetActive(false);
```

Declare `public GameObject extraPanel;` at the end of the field list and hide it in `Start()`.

For your own flow, remove this object and call `Connect()` and `Disconnect()` of `RealtimeAPIWrapper` from your script.

## 9. Use another model or server

- **Model**: change `Model` on `RealtimeAPI`. It is added to the address as `?model=...`. Leave it empty if your server does not need it.
- **Server**: change `Endpoint` to any server that speaks the same events over a WebSocket and accepts an `Authorization: Bearer` header. The events are listed in [ARCHITECTURE.md](ARCHITECTURE.md#realtime-events).
- **Session options** (for example input transcription or tool choice): add them to the `session` object in `SetupSession()` of [`RealtimeAPIWrapper`](../Packages/com.mapluisch.openai-realtime-api-for-unity/Runtime/RealtimeAPIWrapper.cs).
- **More tools**: add an entry in `BuildTools()` and handle its name in `HandleFunctionCall()`. Always answer the call with `SendFunctionCallOutput`.
- **Another speech engine**: feed it from `AudioRecorder.OnAudioRecorded` (base64, PCM16, 24 kHz, mono), and pass its PCM16 audio to `AudioPlayer.EnqueueAudioData`. Lip sync and gesture timing keep working. The gesture event can be raised only from inside `RealtimeAPIWrapper`, so add a public method there that invokes it.

## 10. Record and play your own motion

1. Open `03_MotionRecording`, press Play and press a key from 1 to 6. This starts the guided session and the recording, which runs until you leave Play mode. One file per run.
2. The file is in `Assets/VRRecordings` (git-ignored). Copy it to `Assets/EmbodiedAgent/SampleMotion`.
3. Open `02_Coupling_RecordedSource`, select `Agent Recorder and Playback > VR Motion Playback`, and assign the file to `recordingFile1`. Empty the other three slots if it should always be used.

To record the user instead of the agent, set `recordSpace` on `VR Motion Recorder` to the `Camera Offset` of `XR Origin (XR Rig) ECA 2`, and the three targets to its `... VR Target` objects. Such files are personal data.

The choice between several files is random and is not seeded. For a fixed order, set `UnityEngine.Random.InitState` before the playback starts, or assign one file.

## 11. Log extra data

**Movement**: in [`VRMovementDataLog`](../Assets/EmbodiedAgent/Scripts/Motion/VRMovementDataLog.cs) add a column name to the header string and a matching `AppendNumber(...)` or `AppendTransform(...)` at the end of `FixedUpdate()`. Keep the order the same in both places.

**Conversation**: subscribe to the static events. Example for a new script:

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ConversationLog : MonoBehaviour
{
    private StreamWriter writer;

    private void OnEnable()
    {
        writer = new StreamWriter(Path.Combine(Application.persistentDataPath, "conversation.csv"), true) { AutoFlush = true };
        RealtimeAPIWrapper.OnTranscriptReceived += OnTranscript;
        RealtimeAPIWrapper.OnAnimationSequenceReceived += OnGestures;
        AudioRecorder.OnVADRecordingStarted += OnUserStart;
        AudioRecorder.OnVADRecordingEnded += OnUserEnd;
    }

    private void OnDisable()
    {
        RealtimeAPIWrapper.OnTranscriptReceived -= OnTranscript;
        RealtimeAPIWrapper.OnAnimationSequenceReceived -= OnGestures;
        AudioRecorder.OnVADRecordingStarted -= OnUserStart;
        AudioRecorder.OnVADRecordingEnded -= OnUserEnd;
        writer?.Dispose();
        writer = null;
    }

    private void Write(string type, string text) =>
        writer?.WriteLine(Time.time.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                          "," + type + ",\"" + text.Replace("\"", "\"\"") + "\"");

    private void OnTranscript(string part) => Write("agent", part);
    private void OnGestures(List<string> names) => Write("gestures", string.Join("|", names));
    private void OnUserStart() => Write("user_start", "");
    private void OnUserEnd() => Write("user_end", "");
}
```

Only the agent's speech arrives as text. Tell people what you store, and store no more than you need.
