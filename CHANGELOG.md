# Changelog

This project uses [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-11-24

First public release.

- Realtime voice client for the OpenAI Realtime API: streaming audio in both directions, adaptive voice activity detection, interruption, and reconnect.
- Gestures chosen by the model through one function call per turn. The gesture list in the Inspector defines the values the model may use.
- API key lookup outside the scene: Inspector field, `OPENAI_API_KEY`, or the git-ignored file `openai_api_key.txt`.
- Visible IK body and invisible performer body, lip sync on three meshes, and blinking.
- Movement coupling between user and agent: independent, agent follows user, user's avatar follows agent, or a recorded source.
- Desktop demo with an orbit camera, a gesture panel that works without a key, and a conversation button.
- Motion recorder and player, 50 Hz movement log, and a guided-session controller.

The editor project, the references in scenes and prefabs, and the desktop demo were checked. The headset scenes were not re-run on devices for this release.
