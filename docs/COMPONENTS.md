# Components

Every first-party script of `Assets/For Demo`, with its purpose, its Inspector fields, and who uses it. "Default" is the value in the code. Values set in the scenes or prefabs are named separately. References to other objects (Transforms, components) have no default.

- [Realtime client and audio](#realtime-client-and-audio)
- [Agent](#agent)
- [Avatar](#avatar)
- [Movement transformation](#movement-transformation)
- [Motion](#motion)
- [Session](#session)
- [All-in-One](#all-in-one)
- [Desktop demo](#desktop-demo)
- [DemoKit](#demokit)
- [Third-party components used in the scenes](#third-party-components-used-in-the-scenes)

## Realtime client and audio

In `Assets/For Demo/Realtime`, based on OpenAI-Realtime-API-for-Unity by Martin Pluisch and modified. The prefab `RealtimeAPI` (with its child `Audio`) is in every demo scene. The package in `Packages` is the client of `Assets/For Study` and is not described here.

### RealtimeAPIWrapper

[Source](../Assets/For%20Demo/Realtime/RealtimeAPIWrapper.cs). WebSocket client for the OpenAI Realtime API: connection, session set-up, sending speech, receiving audio, transcript and tool calls.

| Field | Default | Notes |
| --- | --- | --- |
| `apiKey` | placeholder | Leave empty. Use `OPENAI_API_KEY` or `openai_api_key.txt`. A key in this field is saved in the scene and in every build |
| `model` | `gpt-realtime-2.1-mini` | Model name, appended to the endpoint |
| `endpoint` | `wss://api.openai.com/v1/realtime` | Address without the model parameter |
| `systemInstructions` | a short assistant prompt | Scenes: a neutral prompt for "Amy" with a gesture guide. `AllInOne` replaces it when a conversation starts |
| `voice` | `shimmer` | alloy, ash, ballad, cedar, coral, echo, marin, sage, shimmer, verse |
| `gestureNames` | 24 names | Offered to the model as the values of the `trigger_animation` tool |
| `followUpAfterSpeech` | off | Ask for another reply after a tool call even when the model has already spoken. Off avoids answers in two parts |
| `audioPlayer`, `audioRecorder` | | Set in the prefab |

Public members: `ConnectWebSocketButton()`, `Connect()`, `Disconnect()`, `InterruptResponse()`, `SendSystemNote(string)`, `ForgetPendingResponse()`, `IsConnected`, `State`, `IsResponsePending`, `ReceivedMilliseconds`, `HasApiKey`, `Source` (where the key was found, never the key), `SystemInstructions`, `Voice`, `Model`, `GestureNames`, and static events, among them `OnWebSocketConnected`, `OnWebSocketClosed`, `OnSessionUpdated`, `OnResponseCreated`, `OnResponseDone`, `OnResponseCancelled`, `OnTranscriptReceived(string)`, `OnAnimationSequenceReceived(List<string>)`, `OnProblem(RealtimeProblem, string)`.

Used by: `AgentAnimationController`, `ConversationController`, `SpeechCaptions`, `AgentPresence`, `VRConnectionControllerXRI`, `SessionController`, `DesktopDemo`, `GesturePreview`, `DemoIntegration`.

### AudioRecorder

[Source](../Assets/For%20Demo/Realtime/Audio/AudioRecorder.cs). Microphone capture, voice activity detection and push to talk.

| Field | Default | Scenes | Notes |
| --- | --- | --- | --- |
| `listeningMode` | `PushToTalk` | `VAD` | `AllInOne`: `PushToTalk` until a conversation starts, so that the microphone stays closed |
| `sampleRate` | 24000 | | Hz |
| `microphoneDeviceName` | empty | | Empty uses the first device |
| `interruptResponseOnNewRecording` | off | on | Barge-in |
| `microphoneRingBufferSeconds` | 10 | | Length of the looping clip |
| `pushToTalkMaxSeconds` | 120 | | |
| `pushToTalkMinSeconds` | 0.3 | | Shorter recordings are dropped |
| `minimumStartDbFs` | -50 | | Lowest level that can start speech |
| `minimumContinueDbFs` | -56 | | Lowest level that counts as speech once started |
| `startMarginAboveNoiseDb` | 12 | | Start threshold above the noise floor |
| `continueMarginAboveNoiseDb` | 6 | | Continue threshold above the noise floor |
| `vadHighPassHz` | 80 | | Measurement only; 0 switches it off |
| `levelSmoothingSeconds` | 0.08 | | Time constant of the level |
| `speechStartHoldSeconds` | 0.08 | | Time above the start threshold before speech counts as started |
| `endSilenceSeconds` | 1.6 | 0.6 | Silence that ends an utterance |
| `preRollSeconds` | 0.30 | | Audio kept from before the start |
| `maximumUtteranceSeconds` | 120 | | Safety limit |
| `initialNoiseFloorDbFs` | -60 | | |
| `noiseAdaptationSeconds` | 1.5 | | How fast the noise floor follows |
| `noiseUpdateGuardDb` | 3 | | Distance to the start threshold below which the floor is updated |
| `fftSampleSize` | 1024 | | Bars of the debug panel |
| `computeSpectrum` | on | off in `AllInOne` | Only needed for the bars |
| Runtime VAD diagnostics | | | Read-only live values: level, noise floor, thresholds, silence, recording state |

Public members: `StartRecording()`, `StopRecording()`, `StartMicrophone()`, `StopMicrophone()`, `ResetNoiseCalibration()`, `InterruptAgent`, `Muted`, `MicState`, `IsMicrophoneOpen`, `HasWrongSampleRate`, read-only properties for the live values, static events `OnAudioRecorded(string)`, `OnVADRecordingStarted`, `OnVADRecordingEnded`, `OnPushToTalkStarted`.

### AudioPlayer

[Source](../Assets/For%20Demo/Realtime/Audio/AudioPlayer.cs). Buffers and plays the agent's speech.

| Field | Default | Notes |
| --- | --- | --- |
| `sampleRate` | 24000 | Hz |
| `fftSampleSize` | 1024 | Bars of the debug panel |
| `computeSpectrum` | on | Off in `AllInOne` |

Public members: `EnqueueAudioData(byte[])`, `CancelAudioPlayback()`, `ClearAudioBuffer()`, `ResetCancelPending()`, `IsAudioPlaying()`, `BeginResponse()`, `PlayedMilliseconds`, event `OnPlaybackFinished`. Used by `RealtimeAPIWrapper`, `AudioRecorder`, `AgentAnimationController`, `ConversationController`, `SpeechCaptions`, and `DemoIntegration`.

### RealtimeStatus

[Source](../Assets/For%20Demo/Realtime/RealtimeStatus.cs). The enums `RealtimeState`, `KeySource`, and `RealtimeProblem`, and `RealtimeDiagnosis`, which turns exceptions and errors of the service into problems and removes everything that looks like a key from texts.

### AudioProcessingUtils, ListeningMode

[`AudioProcessingUtils`](../Assets/For%20Demo/Realtime/Audio/AudioProcessingUtils.cs) converts between PCM16 and float samples and computes the spectrum for the debug panel. `SimpleVad` and `HighPassFilter` belong to the upstream package and are not called any more. [`ListeningMode`](../Assets/For%20Demo/Realtime/Audio/ListeningMode.cs) is the enum `PushToTalk`, `VAD`.

## Agent

### AgentAnimationController

[Source](../Assets/For%20Demo/Scripts/Agent/AgentAnimationController.cs). Plays the requested gestures in order for as long as the agent speaks. Needs an Animator on the same object.

| Field | Default | Notes |
| --- | --- | --- |
| `agentAudioSource` | | Not read; kept for existing references |
| `audioPlayer` | | Tells whether the agent is speaking |
| `idleState` | `Idle` | State to return to |
| `fallbackGesture` | `Talking (7)` | Played once if the gestures end before the speech |

`HasState(Animator, string)` is public and static. Used on `Agent Capture Mixamo`, `Agent VR Character IK` and the inactive `Agent Capture Mixamo Test`.

### GesturePreview

[Source](../Assets/For%20Demo/Scripts/Agent/GesturePreview.cs). Optional panel that lists `RealtimeAPIWrapper.GestureNames` and plays the clicked gesture on every active `AgentAnimationController`. Needs no key and no connection. Drawn with IMGUI, so it is not visible in a headset.

| Field | Default | Notes |
| --- | --- | --- |
| `toggleKey` | `G` | Shows or hides the panel |
| `showOnStart` | off | On in the desktop demo |

Used in the scene `DesktopDemo`. Add it to an empty object in any other scene.

### VisemeSync

[Source](../Assets/For%20Demo/Scripts/Agent/VisemeSync.cs). Copies viseme weights from the head mesh to other meshes by blend shape name.

| Field | Default | Notes |
| --- | --- | --- |
| `sourceMesh` | | `Head_Mesh` of the visible body |
| `targetMeshes` | | `Teeth_Mesh`, `Tongue_Mesh` |
| `visemeNames` | 15 Avaturn names | The alternative vowel names are tried automatically |

Used on the `Audio` object.

### EyeAnimationHandlerNew

[Source](../Assets/For%20Demo/Scripts/Agent/EyeAnimationHandlerNew.cs), namespace `ReadyPlayerMe.Core`. Blinking.

| Field | Default | Notes |
| --- | --- | --- |
| `blinkSpeed` | 0.1 | Seconds to close and to open |
| `blinkInterval` | 3 | Seconds between blinks, constant |
| `maxBlinkWeight` | 1 | 1 for meshes with weights from 0 to 1, 100 for 0 to 100 |
| `enableEyeRotation` | off | Reserved, has no effect |
| `debugForceClose` | off | Keeps the eyes closed |
| `headMesh` | empty | Found automatically |

Used on `Agent VR Character IK` and `Skeleton_Rig`.

## Avatar

### UniversalAvatarLoaderNew

[Source](../Assets/For%20Demo/Scripts/Avatar/UniversalAvatarLoaderNew.cs). Gives an avatar in the scene the body of another avatar with the same bone names.

| Field | Default | Notes |
| --- | --- | --- |
| `sourcePrefab` | | Scene 01: `Male 005` |
| `targetAvatar` | | `Skeleton_Rig` |
| `forceMale` | off | Used if the prefab name contains neither "female"/"feminine" nor "male"/"masculine" |
| `maleAvatarRig`, `femaleAvatarRig` | | Humanoid definitions in `Assets/Shared/Avatars/Humanoid Avatar Model` |
| `loadOnStart` | on | Otherwise call `LoadAndRetarget()` or use the context menu **Load and Align** |

Active in scene 01. Present but disabled in scene 03.

### IKTargetFollowVRRig

[Source](../Assets/For%20Demo/Scripts/Avatar/IKTargetFollowVRRig.cs). Moves the IK targets of an avatar to three tracked points and turns the body towards the head's yaw.

| Field | Default | Scenes | Notes |
| --- | --- | --- | --- |
| `turnSmoothness` | 0.1 | 0.1 | Share of the remaining turn per frame at 72 frames per second |
| `head`, `leftHand`, `rightHand` | | | `vrTarget`, `ikTarget`, `trackingPositionOffset`, `trackingRotationOffset` (0) |
| `headBodyPositionOffset` | 0, 0, 0 | 0, -0.54, 0 | Body position relative to the head |
| `headBodyYawOffset` | 0 | 0 | Degrees |

`Skeleton_Rig` has two: one reads the user rig, the other the outputs of `Modulation L`. One of them is enabled. Further disabled instances on the agent are not used.

### IKFootSolver

[Source](../Assets/For%20Demo/Scripts/Avatar/IKFootSolver.cs). Steps a foot IK target when the body has moved away from it.

| Field | Default | User's avatar | Agent |
| --- | --- | --- | --- |
| `terrainLayer` | nothing | Default layer | Default layer |
| `body`, `otherFoot` | | set | set |
| `speed` | 4 | 4 | 4 |
| `stepDistance` | 0.2 | 0.2 | 0.2 |
| `stepLength`, `sideStepLength` | 0.2, 0.1 | 0.2, 0.1 | 0.2, 0.1 |
| `stepHeight` | 0.3 | 0.1 | 0.1 |
| `footOffset`, `footRotOffset` | 0, 0 | 0, (-120, 180, 0) | 0, (-120, 180, 0) |
| `footYPosOffset` | 0.1 | 0.1 | 0 (0.1 in scene 01) |
| `rayStartYOffset` | 0 | 0 | 2 |
| `rayLength` | 1.5 | 1.5 | 3.5 (3 in scene 01) |

`isMovingForward` is a live value set by the script. `Snap()` puts the foot below the body at once, for example after a teleport.

## Movement transformation

All used in `Assets/For Demo/Prefabs/Modulation M.prefab`, which is instantiated twice per scene as `Modulation M` (Mimicry) and `Modulation L` (Leading and Noise). "Partner" is the body whose movement is laid on top.

### TransformAlignment

[Source](../Assets/For%20Demo/Scripts/MovementTransformation/TransformAlignment.cs). Copies world poses from source to target transforms. It is the patch cable between the rigs, the transformation, and the IK targets.

| Field | Default | Notes |
| --- | --- | --- |
| `pairs` | empty | Each with `source`, `target`, `enabled`, `alignPosition`, `alignRotation`, `alignScale` (all on) |
| `updateMode` | `LateUpdate` | `Update`, `LateUpdate`, `FixedUpdate` |
| `alignInEditMode` | on | |

`AlignNow()` copies at once. Used on the agent rig and the recording rig (agent's clips to rig; off on the recording rig in `02_Noise` and `AllInOne`), on each Modulation root (six inputs) and twice on `Agent VR Character IK` (agent rig or Modulation M outputs to IK targets).

### SlowFollowSystem

[Source](../Assets/For%20Demo/Scripts/MovementTransformation/SlowFollowSystem.cs). Exponential smoothing of a local pose.

| Field | Default | Prefab |
| --- | --- | --- |
| `UpStreamTarget` | | partner head, left, right |
| `lerpTime` | 0.5 s | 0.2 s |

`Snap()` takes the followed pose at once. Used on `Head Partner Slow`, `Left Hand Partner Slow`, `Right Hand Partner Slow`.

### MirrorTransformRotationHead

[Source](../Assets/For%20Demo/Scripts/MovementTransformation/MirrorTransformRotationHead.cs). Mirrors the rotation of a head.

| Field | Default | Prefab |
| --- | --- | --- |
| `sourceHead` | | `Head Partner Slow` |
| `referenceFrame` | | Modulation root |
| `mirrorLeftRightRotation` | on | off |
| `mirrorUpDownRotation` | off | off |
| `mirrorFrontBackRotation` | off | on |
| `rotationOffsetEuler` | 0, 0, 0 | 0, 0, 0 |
| `drawReferenceAxesGizmo`, `gizmoAxisLength` | on, 0.25 | on, 0.5 |

Used on `Head Partner Mirror`.

### MirrorTransformAcrossHeadPlaneNew

[Source](../Assets/For%20Demo/Scripts/MovementTransformation/MirrorTransformAcrossHeadPlaneNew.cs). Mirrors the position of a hand across a plane through the head, and optionally its rotation in another frame.

| Field | Default | Prefab |
| --- | --- | --- |
| `head`, `source` | | `Head Partner Slow`, the smoothed partner hand |
| `planeMode` | `HeadSagittalPlane` | `WorldVerticalPlane` |
| `planeOrigin` | empty (head position) | empty |
| `mirrorRotation` | off | on |
| `rotationReferenceFrame` | empty (head) | Modulation root |
| `mirrorLeftRightRotation`, `mirrorUpDownRotation`, `mirrorFrontBackRotation` | on, off, off | off, off, on |
| `rotationOffsetEuler` | 0, 0, 0 | 0, 0, 0 |
| `drawPlaneGizmo`, `gizmoPlaneSize` | on, 0.5 | on, 0.5 |
| `drawRotationReferenceAxesGizmo`, `rotationAxesGizmoLength` | on, 0.25 | on, 0.5 |

Used on `Left Hand Partner Mirror` and `Right Hand Partner Mirror`.

### HeadRotationMixedFollowSystem

[Source](../Assets/For%20Demo/Scripts/MovementTransformation/HeadRotationMixedFollowSystem.cs). Keeps the own head position and mixes the rotation.

| Field | Default | Prefab |
| --- | --- | --- |
| `myHead`, `partnerHead` | | `Head`, `Head Partner Mirror` |
| `followRatio` | 0.5 | 0.3 on `Head Modulated`, 1 on `Head Plane` |

`Apply()` computes the pose at once.

### HandMixedFollowSystemFinal

[Source](../Assets/For%20Demo/Scripts/MovementTransformation/HandMixedFollowSystemFinal.cs). Mixes position and rotation of a hand. Execution order 10000.

| Field | Default | Prefab |
| --- | --- | --- |
| `myHead`, `myHand`, `partnerHead`, `partnerHand`, `modulatedHead` | | `Head`, own hand, `Head Partner Slow`, mirrored opposite hand, `Head Plane` |
| `followRatio` | 0.5 | 0.3 |
| `preventSlerpFlip` | on | on |
| `branchSwitchHysteresis` | 1 degree | 1 |
| `initializationDelayFrames` | 5 | 5 |
| `requiredStableFrames` | 3 | 3 |
| `initializationStabilityAngle` | 8 degrees | 8 |
| `maximumInitializationWaitFrames` | 90 | 90 |
| `followMyHandWhileInitializing` | on | on |

`ResetRotationContinuity()` starts the initialization again, `IsReady` tells whether it is over, and `Snap()` writes the position for the current inputs. The remaining fields are read-only debug values. Used on `Left Hand Modulated` and `Right Hand Modulated`.

### Earlier variants

Kept for comparison. They sit disabled on objects of the prefab and can replace the enabled component.

| Script | Difference | Fields (default) |
| --- | --- | --- |
| [`HandMixedFollowSystem`](../Assets/For%20Demo/Scripts/MovementTransformation/HandMixedFollowSystem.cs) | Mixes the position only, hand rotation unchanged | Five references, `followRatio` (0.5) |
| [`HandMixedFollowSystemNew`](../Assets/For%20Demo/Scripts/MovementTransformation/HandMixedFollowSystemNew.cs) | Adds up mixed rotation increments | References, `followRatio` (0.5), `useContinuousRotation` (on), `maxInputAngularSpeed` (0) |
| [`HandMixedFollowSystemUltimate`](../Assets/For%20Demo/Scripts/MovementTransformation/HandMixedFollowSystemUltimate.cs) | As Final, without the start-up phase, in Update | References, `followRatio` (0.5), `preventSlerpFlip` (on), `branchSwitchHysteresis` (1) |
| [`HeadAddedFollowSystem`](../Assets/For%20Demo/Scripts/MovementTransformation/HeadAddedFollowSystem.cs) | Adds a share of the partner's horizontal head movement to the own head position | `myHead`, `partnerHead`, `addRatio` (0.5) |
| [`MirrorTransformAcrossHeadPlane`](../Assets/For%20Demo/Scripts/MovementTransformation/MirrorTransformAcrossHeadPlane.cs) | Mirrors position and rotation in the same plane | `head`, `source`, `planeMode` (`HeadSagittalPlane`), `planeOrigin`, `mirrorRotation` (off), `rotationOffsetEuler` (0), `drawPlaneGizmo` (on), `gizmoPlaneSize` (0.5) |
| [`SlowFollowSystemNew`](../Assets/For%20Demo/Scripts/MovementTransformation/SlowFollowSystemNew.cs) | Smooths the rotation only | `UpStreamTarget`, `lerpTime` (0.5 s) |

## Motion

### VRMotionRecorder

[Source](../Assets/For%20Demo/Scripts/Motion/VRMotionRecorder.cs). Records head and hands to a CSV file. One recording per run, from the first button or key until Play mode or the application ends.

| Field | Default | Scene 03 | Notes |
| --- | --- | --- | --- |
| `xAction` ... `rightTriggerAction` | | six actions | Any of them starts the recording |
| `enableKeyboardDebugKeys` | on | on | Keys 1 to 6, also on the numeric keypad |
| `recordSpace` | | `Camera Offset` of the agent rig | Poses are stored relative to it |
| `headTarget`, `leftHandTarget`, `rightHandTarget` | | agent rig | |
| `sampleRate` | 72 | 60 | Highest number of rows per second |
| `flushIntervalSeconds` | 1 | 5 | How often data is written to disk |
| `filePrefix` | `VRRecording` | `VRRecording` | |

Active in scene 03, inactive in the `02_` scenes.

### VRMotionPlayback

[Source](../Assets/For%20Demo/Scripts/Motion/VRMotionPlayback.cs). Plays one of up to four recordings on three targets. It picks one at random at each start, interpolates between rows, and holds the last pose at the end. A new press then starts again.

| Field | Default | Notes |
| --- | --- | --- |
| `xAction` ... `rightTriggerAction` | | Any of them starts the playback |
| `enableKeyboardDebugKeys` | on | Keys 1 to 6 |
| `recordingFile1` to `recordingFile4` | | TextAssets; scenes use the files in `Assets/Shared/SampleMotion` |
| `playbackSpace` | | `Camera Offset` of the recording rig |
| `headTarget`, `leftHandTarget`, `rightHandTarget` | | Recording rig |

Active in `02_Noise`. The chosen file is written to the Console only. `AllInOne` uses [`RecordedMotionSource`](#movement-and-avatars) instead.

### VRMovementDataLog

[Source](../Assets/For%20Demo/Scripts/Motion/VRMovementDataLog.cs). Writes 12 transforms per fixed step to a CSV file. Columns: see [ARCHITECTURE.md](ARCHITECTURE.md#motion-files).

| Field | Notes |
| --- | --- |
| `realHuman...`, `realAgent...` | Head, left and right hand of the user rig and the agent rig |
| `modulatedHuman...`, `modulatedAgent...` | IK targets of the user's avatar and the visible agent |
| `leftControllerX` ... `rightControllerTrigger` | Any of these actions starts the log |

On the object `DataLoggerObject` in the `02_` and `03_` scenes.

## Session

### VRConnectionControllerXRI

[Source](../Assets/For%20Demo/Scripts/Session/VRConnectionControllerXRI.cs). Sets the recorder to VAD and opens the microphone at the start. A controller button connects or disconnects.

| Field | Scene 01 |
| --- | --- |
| `apiWrapper`, `audioRecorder` | `RealtimeAPI`, `Audio` |
| `toggleConnectionAction` | `XRI Right Interaction/ToggleConnection` (A on the right controller) |

Enabled in scene 01. Disabled in the other single-purpose scenes. Not in `AllInOne`.

### SessionController

[Source](../Assets/For%20Demo/Scripts/Session/SessionController.cs). The guided session of the `02_` and `03_` scenes: avatar choice, warm-up, preparation, timed conversation, end. The steps are listed in [ARCHITECTURE.md](ARCHITECTURE.md#guided-session).

| Field | Default | Notes |
| --- | --- | --- |
| `apiWrapper` | | Connected and disconnected by the session |
| `agentAnimatorCore`, `agentAnimatorCoreTorso` | | Animators of the invisible performer and of the visible agent |
| `backgroundAudioSource` | | Plays the narration clips |
| `targetHumanAvatar`, `forceMale`, `maleAvatarRig`, `femaleAvatarRig` | | As in `UniversalAvatarLoaderNew` |
| `avatarPrefab1_LeftX` to `avatarPrefab6_RightTrigger` | | The six avatars in `Assets/Shared/Avatars/Final` |
| `btnLeftX` to `btnRightTrigger` | | Actions `SelectAvatar1` to `SelectAvatar6` |
| `enableKeyboardNumbersToStart` | on | Keys 1 to 6 |
| `panel1`, `panel2`, `panelWarning3Min`, `panelWarning1Min` | | Boards in `Assets/For Demo/UI/Boards` and its subfolder; reminders in `Assets/Shared/UI/Boards` |
| `countdownText` | | Optional text with the remaining time, not set in the scenes |
| `audio1_...` to `audio6_...` | | Six optional narration clips, empty in this release |
| `time0_AvatarLoadDelay` to `time4_PrepDuration`, and the conversation length `time5_...` | 3, 5, 10, 3, 30, 180 s | Scenes: 5, 13, 23, 3, 79, 305 s |
| `familiarizationActions` | 4 placeholders | Scenes: six warm-up states |

The two reminders are shown at exactly 180 s and 60 s, so use whole seconds for the conversation length. On the object `Session Manager` in the `02_` and `03_` scenes.

## All-in-One

In [`Assets/For Demo/AllInOne`](../Assets/For%20Demo/AllInOne), namespace `EmbodiedAgent.AllInOne`. Most parts are components of the object `AllInOne` in `AllInOne.unity`. `AvatarDresser` is on `Skeleton_Rig` and `Mirror Figure`, `MirrorThrottle` on `HexagonEnv`, and `FollowTransform` on `Audio`. The scene builder sets their references. How they work together: [ALL_IN_ONE.md](ALL_IN_ONE.md).

### Flow and input

| Script | Purpose | Fields (default) |
| --- | --- | --- |
| [`AllInOneController`](../Assets/For%20Demo/AllInOne/Scripts/AllInOneController.cs) | Phases Boot, Menu, Free, Session, Finished; connects panels and parts | `catalog`, `topics`, `freePrompt`, and the parts below |
| [`AgentChoices`](../Assets/For%20Demo/AllInOne/Scripts/AgentChoices.cs) | What the user has chosen | Plain class |
| [`AgentDemoTexts`](../Assets/For%20Demo/AllInOne/Scripts/AgentDemoTexts.cs) | All texts of the panels | Constants |
| [`UserRig`](../Assets/For%20Demo/AllInOne/Scripts/UserRig.cs) | Headset and controllers, or a simulated body on a desktop; pointers, push to talk, desktop keys | `origin`, `viewCamera`, `leftController`, `rightController`, `hiddenFromObserver` |
| [`RecentreControl`](../Assets/For%20Demo/AllInOne/Scripts/RecentreControl.cs) | Puts the user on the start place, also after the system has recentred | `rig`, `switcher`, `body` |

### Panels

| Script | Purpose |
| --- | --- |
| [`StartPanel`](../Assets/For%20Demo/AllInOne/Scripts/StartPanel.cs) | Pages Avatar, Movement, Experience, Start |
| [`TalkPanel`](../Assets/For%20Demo/AllInOne/Scripts/TalkPanel.cs) | Side panel of the free conversation |
| [`SessionPanel`](../Assets/For%20Demo/AllInOne/Scripts/SessionPanel.cs) | Side panel of the full session |
| [`Dialogs`](../Assets/For%20Demo/AllInOne/Scripts/Dialogs.cs) | Key help, problem, leave, finished |

### Movement and avatars

| Script | Purpose | Fields (default) |
| --- | --- | --- |
| [`ConditionSwitcher`](../Assets/For%20Demo/AllInOne/Scripts/ConditionSwitcher.cs) | Changes mode and strength at run time without jumps: `SetMode(MovementMode, bool instant)`, `Strength`, `Refresh()`, events `Changed`, `RecordingsMissing` | Scene references, `rampSeconds` (0.35), `strengthPerSecond` (1) |
| [`MovementMode`](../Assets/For%20Demo/AllInOne/Scripts/MovementMode.cs) | The enum `Independent`, `Leading`, `Mimicry`, `Noise` | |
| [`ModulationUnit`](../Assets/For%20Demo/AllInOne/Scripts/ModulationUnit.cs) | One Modulation object: `Ratio`, `SetPartner()`, `Prewarm()`, `IsReady` | Plain class |
| [`RecordedMotionSource`](../Assets/For%20Demo/AllInOne/Scripts/RecordedMotionSource.cs), [`MotionRecording`](../Assets/For%20Demo/AllInOne/Scripts/MotionRecording.cs) | Plays a random recording in a loop on three targets | `recordings`, `playbackSpace`, `targets`, `blendSeconds` (0.5) |
| [`AvatarDresser`](../Assets/For%20Demo/AllInOne/Scripts/AvatarDresser.cs) | Gives a rigged body the look of an avatar: `Dress(entry)` | |
| [`AgentAvatarCatalog`](../Assets/For%20Demo/AllInOne/Scripts/AgentAvatarCatalog.cs) | Asset with the avatars | For each: `id`, `label`, `prefab`, `definition`, `thumbnail` |
| [`MirrorFigure`](../Assets/For%20Demo/AllInOne/Scripts/MirrorFigure.cs) | Copy of the user's body that moves like its reflection, with the transformation | `body`, `targets`, `userTargets`, `home` (0, 0, 0.5) |
| [`MirrorThrottle`](../Assets/For%20Demo/AllInOne/Scripts/MirrorThrottle.cs) | Keeps the mirrors of the room affordable on a headset | `cameras`, `hidden`, `farClip` (15 m), `headsetSize` (800 x 500) |

### Conversation and session

| Script | Purpose | Fields (default) |
| --- | --- | --- |
| [`ConversationController`](../Assets/For%20Demo/AllInOne/Scripts/ConversationController.cs) | Opens and closes connection and microphone, states, greeting, one reconnect, problems: `Begin(instructions, isFree)`, `End()`, `Suspend()`, `Unsuspend()`, `Retry()`, `PressToTalk(bool)` | `api`, `recorder`, `player`, `narration` |
| [`SpeechCaptions`](../Assets/For%20Demo/AllInOne/Scripts/SpeechCaptions.cs), [`CaptionService`](../Assets/For%20Demo/AllInOne/Scripts/CaptionService.cs) | Captions of the agent and of the narration | `api`, `player` |
| [`FollowTransform`](../Assets/For%20Demo/AllInOne/Scripts/FollowTransform.cs) | Keeps the voice at the agent's mouth | `target`, `frame`, `offset` |
| [`AgentPresence`](../Assets/For%20Demo/AllInOne/Scripts/AgentPresence.cs) | Shows and hides the agent, loops the idle movement, gestures without speech, warm-up movements | `capture`, `body`, `api` |
| [`SessionDirector`](../Assets/For%20Demo/AllInOne/Scripts/SessionDirector.cs) | Runs the full session on one clock: `Begin(topic, pace)`, `Pause()`, `Resume()`, `Next()`, `Restart()`, `Stop()` | `script`, `narration`, `boards`, `presence`, `conversation`, `switcher` |
| [`AgentSessionScript`](../Assets/For%20Demo/AllInOne/Scripts/AgentSessionScript.cs) | Asset with the steps of the session | `steps`, `reminders` (180 s, 60 s), `reminderBoards`, `reminderSeconds` (3) |
| [`AgentTopic`](../Assets/For%20Demo/AllInOne/Scripts/AgentTopic.cs) | Asset with one topic | See [ALL_IN_ONE.md](ALL_IN_ONE.md#session-as-data) |
| [`BoardPresenter`](../Assets/For%20Demo/AllInOne/Scripts/BoardPresenter.cs) | Shows copies of the boards and reminders | `parent` |

### Editor

| Script | Purpose |
| --- | --- |
| [`AllInOneSceneBuilder`](../Assets/For%20Demo/AllInOne/Editor/AllInOneSceneBuilder.cs) | Builds `AllInOne.unity` from `02_Independent.unity` |
| [`StudyContentImporter`](../Assets/For%20Demo/AllInOne/Editor/StudyContentImporter.cs) | Writes session and topics from the original scenes |
| [`AvatarThumbnailBaker`](../Assets/For%20Demo/AllInOne/Editor/AvatarThumbnailBaker.cs) | Renders the pictures of the avatars and writes the catalog |
| [`BuildAllInOne`](../Assets/For%20Demo/AllInOne/Editor/BuildAllInOne.cs) | Android and Windows builds that stop when a key would be included |
| [`AllInOneSmokeTest`](../Assets/For%20Demo/AllInOne/Editor/AllInOneSmokeTest.cs) (11 files), [`RealtimeMockServer`](../Assets/For%20Demo/AllInOne/Editor/RealtimeMockServer.cs), [`TestDriver`](../Assets/For%20Demo/AllInOne/Scripts/TestDriver.cs) | Automated tests with a local WebSocket server |
| [`AllInOnePaths`](../Assets/For%20Demo/AllInOne/Editor/AllInOnePaths.cs) | Folders and files that the parts above use |

## Desktop demo

| Script | Purpose | Fields (default) |
| --- | --- | --- |
| [`DesktopDemo`](../Assets/For%20Demo/DesktopDemo/Scripts/DesktopDemo.cs) | Turns off the user rig's camera image and controller tracking, holds the hands in a resting pose, draws the conversation panel | `userRig`, `api` |
| [`OrbitCamera`](../Assets/For%20Demo/DesktopDemo/Scripts/OrbitCamera.cs) | Right-drag to orbit, scroll to zoom | `target` (0, 1.2, -0.5), `distance` (2.1), `yaw` (198), `pitch` (8), `maxDistance` (3.6; 2.6 in the demo scene) |
| [`DesktopDemoBuilder`](../Assets/For%20Demo/DesktopDemo/Editor/DesktopDemoBuilder.cs) | Menu **Embodied Agent > Rebuild Desktop Demo**: builds `DesktopDemo.unity` from `02_Independent.unity` | Editor only |

## DemoKit

Interface and input kit in [`Assets/For Demo/DemoKit`](../Assets/For%20Demo/DemoKit), namespace `DemoKit`, see [ARCHITECTURE.md](ARCHITECTURE.md#demokit).

| Folder | Main classes |
| --- | --- |
| `Runtime/UI` | `DemoUi` (create, find, click), `DemoBuilder`, `DemoPanel`, `DemoPages`, `DemoLabel`, `DemoButton`, `DemoCard`, `DemoScrollGrid`, `DemoStepper`, `DemoSlider`, `DemoSwitch`, `DemoToggleRow`, `DemoStatus`, `DemoProgressBar`, `DemoProgressDots`, `DemoCaptionBar`, `DemoToast`, `DemoHotspot` |
| `Runtime/Input` | `DemoInput`, `DemoPointerRay`, `DemoPointerPoke`, `DemoPointerMouse`, `DemoPress`, `DemoPoseDriver` |
| `Runtime/Rig` | `DemoDesktopRig`, `DemoFade`, `DemoXr` |
| `Runtime/Core` | `DemoTheme` (colours, sizes, fonts), `DemoSprites` (icons), shapes, easing, clock |
| `Runtime/Sample`, `Editor`, `Fonts` | Sample panel; menu **Demo Kit**; Inter (SIL Open Font License 1.1) |

## Third-party components used in the scenes

| Component | Where | Purpose |
| --- | --- | --- |
| [`DemoIntegration`](../Assets/For%20Demo/RealtimeDemoUI/Scripts/DemoIntegration.cs) | `DemoUI` canvas (screen space), not in `AllInOne` and `DesktopDemo` | Debug panel with connect button, listening mode, push to talk (space bar), events and transcript. Not visible in a headset. Its energy read-out is not connected to the voice detection |
| `OVRLipSyncContext`, [`OVRLipSyncContextMorphTarget`](../Assets/ThirdParty/Oculus/LipSync/Scripts/OVRLipSyncContextMorphTarget.cs) | `Audio` object | Lip sync. The second file is modified, see [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) |
| Animation Rigging | Visible agent body, user's avatar | Multi-parent constraint (head), two-bone IK (arms, legs) |
| XR Interaction Toolkit, Input System | User rig | Input actions, tracked pose drivers |
