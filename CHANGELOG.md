# Changelog

This project uses [Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-11-25

### Added

- **All-in-One**: one scene in which avatar, movement mode and strength, free conversation or full session, topic, and pace are chosen inside VR or on a desktop. Modes are switched at run time without jumps. With captions, self view, push to talk, key help, builds for Quest and Windows, and 25 automated tests. See [docs/ALL_IN_ONE.md](docs/ALL_IN_ONE.md).
- **DemoKit**: panels, pointers, captions, messages, view fade, and a desktop body, made in code.
- **For Study**: the original scenes of research made with the platform, kept for the record.

### Changed

- Folders: `Assets/For Demo`, `Assets/For Study`, `Assets/Shared`. The demo has its own assemblies and its own copy of the Realtime client in `Assets/For Demo/Realtime`; the package in `Packages` is the client that the scenes of `For Study` were made with, and only they use it.
- Scene names: the four mode scenes are `02_Independent`, `02_Leading`, `02_Mimicry`, `02_Noise`. The scripts behind them are in `Scripts/MovementTransformation`, and the documentation uses the term movement transformation.
- Build list: `AllInOne` is the scene that is built.

### Improved in the Realtime client (all demo scenes)

- Interruption cancels the answer on the server, shortens it there to what was heard, and drops what arrives late.
- A tool call is answered when the response is done. The model is asked to go on only if it has not spoken, so answers no longer come in two parts.
- Problems are reported with their type. Texts of the service are shortened, and everything that looks like a key is removed from log and screen.
- Microphone level in push to talk, shortest push-to-talk recording, mute, key file next to a Windows program.

### Verified

The automated tests pass in the Unity Editor. The conversation was checked with the OpenAI service in three short runs on a desktop. The Windows program was built and started, and the APK was built. Nothing was run on a headset for this release.

## [1.0.0] - 2026-11-24

First public release.

- Realtime voice client for the OpenAI Realtime API: streaming audio in both directions, adaptive voice activity detection, interruption, and reconnect.
- Gestures chosen by the model through one function call per turn. The gesture list in the Inspector defines the values the model may use.
- API key lookup outside the scene: Inspector field, `OPENAI_API_KEY`, or the git-ignored file `openai_api_key.txt`.
- Visible IK body and invisible performer body, lip sync on three meshes, and blinking.
- Movement transformation between user and agent: independent, agent follows user, user's avatar follows agent, or a recorded source.
- Desktop demo with an orbit camera, a gesture panel that works without a key, and a conversation button.
- Motion recorder and player, 50 Hz movement log, and a guided-session controller.

The editor project, the references in scenes and prefabs, and the desktop demo were checked. The headset scenes were not re-run on devices for this release.
