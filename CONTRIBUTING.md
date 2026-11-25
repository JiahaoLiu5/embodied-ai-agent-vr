# Contributing

Bug reports, fixes and documentation improvements are welcome. Please open an issue before you start a larger change.

## Set-up

Use Unity **6000.4.4f1**. Clone the repository, open the folder in Unity Hub, and open `Assets/EmbodiedAgent/Demo/DesktopDemo.unity`.

## Rules

- **Keep references intact.** Do not rename or move a script, rename a class or a serialized field, or delete a `.meta` file. Scenes and prefabs refer to them.
- **Never commit a key.** Use `OPENAI_API_KEY` or `openai_api_key.txt` (git-ignored). Leave the `Api Key` field empty.
- **Never commit data of people.** Motion logs and recordings belong outside the repository.
- **Do not import the XR Interaction Toolkit sample Starter Assets.** It has the same GUID as the input actions of this project.
- **Keep behaviour stable.** Changes to smoothing, mirroring or blending alter the movement. Describe them in the pull request and make them optional where possible.
- **Comments, tooltips and log messages in English.** Plain language.
- **Third-party content** needs an entry in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and a licence that allows redistribution.

## Before you open a pull request

1. The project compiles without errors for Windows and Android.
2. The scenes you touched open without missing scripts or references.
3. The gesture panel of the desktop demo works. If you changed the voice part, hold a short conversation.
4. If you changed VR behaviour, say which headset you used. If you could not test on a headset, say so.
5. [CHANGELOG.md](CHANGELOG.md) has an entry.

By contributing you agree that your code is released under the [MIT licence](LICENSE).
