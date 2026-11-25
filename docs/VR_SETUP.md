# VR setup

How to run the scenes on a Meta Quest 3, as a standalone app or linked to a PC.

> The VR scenes were not built or re-run on headsets after the clean-up for this release. The steps below follow from the project settings in this repository. Please open an issue if one of them does not match what you see.

## What the project already contains

| Setting | Value |
| --- | --- |
| XR Plug-in Management | OpenXR loader for Windows and Android, initialised on startup |
| OpenXR interaction profiles | Oculus Touch Controller Profile, on for Windows and Android; Meta Quest Touch Plus Controller Profile, on for Android |
| OpenXR feature **Meta Quest Support** (Android) | on |
| OpenXR render mode | Single Pass Instanced |
| Scripting backend, architecture | IL2CPP, ARM64 |
| Android API level | Minimum 32, target 34 |
| Graphics API (Android) | Vulkan, then OpenGLES3 |
| Colour space | Linear |
| Active Input Handling | Both (the desktop debug panel uses the old input class) |
| Package name | `com.jiahaoliu.embodiedaiagentvr` |
| Android manifest | `Assets/Plugins/Android/AndroidManifest.xml`, lists Quest, Quest 2, Quest Pro, Quest 3 and Quest 3S |
| Scenes in the build | The six scenes and the desktop demo are listed, only `01_ConversationalAgent` is ticked |

## Standalone build for Quest

1. **Install modules.** In Unity Hub add to Unity 6000.4.4f1: Android Build Support with OpenJDK and Android SDK and NDK tools.
2. **Prepare the headset.** Turn on developer mode, connect the USB cable and accept the debugging prompt in the headset.
3. **Switch platform.** **File > Build Profiles > Android > Switch Platform**. The first switch imports the textures again and takes a while.
4. **OpenXR settings.** **Edit > Project Settings > XR Plug-in Management**, Android tab:
   - **OpenXR** is ticked.
   - On the **OpenXR** page below it, Android tab, check that **Meta Quest Support** is ticked and that **Oculus Touch Controller Profile** and **Meta Quest Touch Plus Controller Profile** are listed.
   - Open **Project Validation** and apply the suggested fixes for Android.
5. **Player settings.** Change **Company Name**, **Product Name** and **Package Name** to your own. Set **Internet Access** to **Require** (see Permissions).
6. **Choose the scene.** There is no menu and no scene change at run time: each scene is its own app. In **Build Profiles > Scene List** tick exactly one scene. If you want several of them on one headset, give each build its own product name and package name.
7. **Build And Run.**
8. **Copy the API key** (next section), then start the app again.

### API key on the headset

The app reads `openai_api_key.txt` from `Application.persistentDataPath`. Start the app once so that the folder exists, then:

```text
adb push openai_api_key.txt /sdcard/Android/data/<package name>/files/openai_api_key.txt
```

Use the package name of your build. Do not put the key into the `Api Key` field for a build: it would be stored inside the APK. Delete the file when you hand the headset on:

```text
adb shell rm /sdcard/Android/data/<package name>/files/openai_api_key.txt
```

### Permissions

| Permission | Where it comes from |
| --- | --- |
| Microphone | Unity adds it because the code uses the microphone. `AudioRecorder` asks the user at the start and waits for the answer |
| Internet | The manifest does not name it. With **Internet Access: Auto** Unity decides from the code; **Require** makes it certain |

## PC VR (Link)

1. Make the headset's runtime the active OpenXR runtime on the PC.
2. Open a scene and press **Play** in the Editor. Keep the Windows platform selected.
3. The key is read from `OPENAI_API_KEY` or from `openai_api_key.txt` in the project folder, as on a desktop.
4. Check which microphone Windows reports first. `AudioRecorder` uses the first device unless `microphoneDeviceName` is set.

## Controls

| Scene | Control | Action |
| --- | --- | --- |
| `01_ConversationalAgent` | A (right controller) | Connect, and disconnect |
| `02_...`, `03_...` | X, Y, left trigger, A, B, right trigger | Choose avatar 1 to 6. This also starts the guided session and the movement log |
| `02_Coupling_RecordedSource` | The same buttons | Also start the playback of one recording |
| `03_MotionRecording` | The same buttons | Also start the recording |
| `02_...`, `03_...` in the Editor | Keys 1 to 6 | As the six buttons, but the movement log does not start |
| All numbered scenes, in the Editor | Debug panel | **connect**, listening mode, space bar for push to talk |

Input actions: `Assets/EmbodiedAgent/Input/XRI Default Input Actions.inputactions`, maps `XRI Left Interaction` (`SelectAvatar1` to `SelectAvatar3`) and `XRI Right Interaction` (`SelectAvatar4` to `SelectAvatar6`, `ToggleConnection`). The file has the same GUID as the file in the XR Interaction Toolkit sample **Starter Assets**. Do not import that sample into this project.

In the `02_` and `03_` scenes the session opens the connection itself, about two and a half minutes after the avatar choice (the sum of the steps before the conversation). To talk at once, set `time0` to `time4` of the guided-session controller to 0 and empty its list of warm-up movements, or click **connect** in the Editor.

## Before you start a conversation

- Wear earphones. There is no echo cancellation, and the agent's own voice can start a turn.
- Stand at the origin of the tracking space and recentre. The rig uses the Device tracking origin with a fixed camera height of 1.71 m.
- A quiet room helps. The detector learns the background level while nobody speaks.

## Performance

- The hexagonal room renders three mirror cameras into 1600 x 1000 textures every frame. To save time, deactivate `HexCam1`, `HexCam2` and `HexCam3` in `HexagonEnv`, or lower the size of the render textures in `Environment/Mirrors`.
- The avatars are detailed Avaturn models with facial blend shapes. Two bodies with a face are visible in the conversation scenes, plus the mirrors.
- `Assets/Settings` has separate URP assets for mobile and PC.

## Getting data off the headset

```text
adb pull /sdcard/Android/data/<package name>/files/VR_Interaction_Movement_Data
adb pull /sdcard/Android/data/<package name>/files/VRRecordings
```

These files describe how a person or the agent moved. Handle the files of people as personal data.

## Troubleshooting

| Symptom | Likely cause and fix |
| --- | --- |
| Console: "No API key found" | Set `OPENAI_API_KEY` and restart Unity Hub and the Editor, or add `openai_api_key.txt`. On the headset push the file and restart the app |
| Console: "websocket connection failed" | No internet, wrong key, no access to the model, or a wrong `Model` or `Endpoint`. On the headset set **Internet Access** to **Require** |
| "openai error: ..." in the Console | Message of the server. Check the model name, your account's access and its limits |
| Connected, but no reaction to speech | Wrong microphone or a quiet signal. Select the `Audio` object and watch **Runtime VAD diagnostics**: `Current Input Db Fs` must rise above `Current Start Threshold Db Fs` |
| "No microphone is available yet" | Permission not granted or no device. The recorder tries again every second |
| "The microphone records at ... Hz" | The device does not accept 24 kHz. Choose another microphone, otherwise speech is sent at the wrong speed |
| The agent interrupts or answers itself | It hears its own voice. Use earphones, or turn off `interruptResponseOnNewRecording` |
| Amy answers while you are still thinking | Raise `endSilenceSeconds` |
| Amy speaks but does not gesture | No tool call. Keep the animation section in the prompt, and check the Console for names that are "not a state" |
| Amy gestures but the mouth does not move | The lip sync plugin is missing on your platform (only Windows x64 and Android arm64 are included), or the mesh references on `Audio` are empty |
| The mouth opens far too much | The blend shapes of your avatar use weights from 0 to 100, while the modified file writes 0 to 1. See [CUSTOMIZE.md](CUSTOMIZE.md) |
| No debug panel in the headset | It is a screen-space canvas. It is visible in the Editor and in desktop builds only |
| A does not connect in a `02_` scene | There A chooses avatar 4. `VRConnectionControllerXRI` is on in scene 01 only |
| Controllers do not move the avatar | The app has no focus, or the OpenXR interaction profile is off. Check the Android tab of the OpenXR page |
| The app starts as a flat window | **Meta Quest Support** has been turned off, or OpenXR is not ticked on the Android tab |
| Warning about Active Input Handling on Android | The setting is **Both**, because the debug panel and the event system use the old input class. Ignore the warning, or port these two to the Input System first |
| Gradle: duplicate `libopenxr_loader.so` | `Assets/Plugins/Android/launcherTemplate.gradle` resolves this. Keep **Custom Launcher Gradle Template** on |
| The avatar stands too high or too low | Recentre, or change the camera offset on `XR Origin (XR Rig) ECA 2` |
| Console messages about OpenXR on a PC without a headset | Harmless. Clear **Initialize XR on Startup** on the Windows tab |
