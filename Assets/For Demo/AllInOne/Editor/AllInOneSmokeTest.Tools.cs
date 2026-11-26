using System;
using System.Collections;
using System.IO;
using System.Linq;
using DemoKit;
using TMPro;
using UnityEngine;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>Helpers of the play mode tests.</summary>
    public static partial class AllInOneSmokeTest
    {
        private const int ShotWidth = 1600;
        private const int ShotHeight = 900;

        private static AllInOneController app;
        private static Camera[] mirrors;
        private static RenderTexture shotTexture;

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        /// <summary>Waits for something that takes real time, such as the network or another thread.</summary>
        private static IEnumerator WaitFor(Func<bool> condition, float seconds, string problem)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                {
                    Check(false, problem);
                    yield break;
                }
                yield return null;
            }
        }

        private static IEnumerator Click(string id)
        {
            Check(DemoUi.Click(id), "cannot click " + id);
            yield return null;
        }

        private static IEnumerator WaitForTransition()
        {
            yield return null;
            yield return WaitFor(() => !app.IsBusy, 20f, "a transition did not end");
            yield return Frames(2);
        }

        /// <summary>From the menu: opens a page of the start panel.</summary>
        private static IEnumerator GoToPage(int page)
        {
            while (app.Menu.Page < page) yield return Click("start/next");
            while (app.Menu.Page > page) yield return Click("start/back");
            yield return Wait(0.4f);
        }

        private static IEnumerator BackToMenu()
        {
            if (app.Phase == AppPhase.Menu) yield break;
            app.LeaveAtOnce();
            yield return WaitForTransition();
            Check(app.Phase == AppPhase.Menu, "not back in the menu");
        }

        private static IEnumerator StartFree(MovementMode mode = MovementMode.Independent)
        {
            yield return BackToMenu();
            app.Choices.IsSession = false;
            app.Choices.Mode = mode;
            app.Begin();
            yield return WaitForTransition();
            Check(app.Phase == AppPhase.Free, "the free conversation did not start");
        }

        private static IEnumerator StartSession(string topic, SessionPace pace, MovementMode mode)
        {
            yield return BackToMenu();
            app.Choices.IsSession = true;
            app.Choices.Topic = app.Topics.First(t => t.id == topic);
            app.Choices.Pace = pace;
            app.Choices.Mode = mode;
            app.Begin();
            yield return WaitForTransition();
            Check(app.Phase == AppPhase.Session, "the session did not start");
        }

        // Layout ----------------------------------------------------------------------------------------------

        private static void CheckLayout(DemoPanel panel, string what)
        {
            panel.Layout();
            foreach (string problem in DemoUi.FindOverflow(panel)) Check(false, what + ": " + problem);
            foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(false))
                Check(!HasBrokenWord(text), what + ": a word is broken in " + text.text);
            float height = panel.RectTransform.rect.height;
            Check(height <= 1100f, what + " is " + height.ToString("0") + " units tall");
        }

        /// <summary>True when a line ends inside a word.</summary>
        private static bool HasBrokenWord(TMP_Text text)
        {
            TMP_TextInfo info = text.textInfo;
            for (int line = 0; line < info.lineCount - 1; line++)
            {
                int last = info.lineInfo[line].lastCharacterIndex, next = info.lineInfo[line + 1].firstCharacterIndex;
                if (char.IsLetterOrDigit(info.characterInfo[last].character) && char.IsLetterOrDigit(info.characterInfo[next].character)) return true;
            }
            return false;
        }

        /// <summary>The rectangle of a panel or a board in the picture of a camera.</summary>
        private static Rect ViewRect(RectTransform transform, Camera camera)
        {
            var corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            Vector3[] points = corners.Select(camera.WorldToViewportPoint).ToArray();
            return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }

        /// <summary>Checks that no panel lies over the board as seen from the user's place.</summary>
        private static void CheckNoOverlap(string what)
        {
            GameObject board = app.Boards.Shown;
            if (board == null) return;
            Camera camera = app.Rig.ViewCamera;
            Rect boardRect = ViewRect((RectTransform)board.transform.Find("Panel"), camera);
            foreach (DemoPanel panel in DemoUi.ShownPanels)
            {
                // Captions may cover the lowest line of a board on a desktop, toasts appear above it.
                if (panel.Id.StartsWith("captions") || panel.Id.StartsWith("toast")) continue;
                Check(!ViewRect(panel.RectTransform, camera).Overlaps(boardRect), what + ": " + panel.Id + " covers the board");
            }
        }

        // Movement --------------------------------------------------------------------------------------------

        /// <summary>The largest movement and rotation of some transforms between two frames.</summary>
        private sealed class StepMeter
        {
            private readonly Transform[] parts;
            private readonly Vector3[] positions;
            private readonly Quaternion[] rotations;
            private bool hasSample;

            public StepMeter(params Transform[] parts)
            {
                this.parts = parts;
                positions = new Vector3[parts.Length];
                rotations = new Quaternion[parts.Length];
            }

            public float LargestStep { get; private set; }

            public float LargestTurn { get; private set; }

            public void Sample()
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (hasSample)
                    {
                        LargestStep = Mathf.Max(LargestStep, Vector3.Distance(positions[i], parts[i].position));
                        LargestTurn = Mathf.Max(LargestTurn, Quaternion.Angle(rotations[i], parts[i].rotation));
                    }
                    positions[i] = parts[i].position;
                    rotations[i] = parts[i].rotation;
                }
                hasSample = true;
            }

            public void Reset()
            {
                hasSample = false;
                LargestStep = LargestTurn = 0f;
            }
        }

        private static float YawTo(Vector3 position)
        {
            Vector3 direction = position - UserRig.Home;
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static string Centimetres(float metres) => (metres * 100f).ToString("0.00") + " cm";

        private static Transform[] IkTargets(GameObject body)
        {
            return new[]
            {
                body.transform.Find("VR IK Rig/Head IK/Head Target"),
                body.transform.Find("VR IK Rig/Left Arm IK/Left Arm IK_target"),
                body.transform.Find("VR IK Rig/Right Arm IK/Right Arm IK_target")
            };
        }

        private static Transform Bone(GameObject body, string name) => body.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        // Pictures --------------------------------------------------------------------------------------------

        private static void Shot(string name) => Shot(name, app.Rig.ViewCamera);

        /// <summary>A picture from a place that nobody stands at, with or without panels.</summary>
        private static void ShotFrom(string name, Vector3 position, Vector3 target, float fieldOfView, bool withPanels = false)
        {
            var camera = new GameObject("Shot Camera").AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.LookAt(target);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            if (!withPanels) camera.cullingMask &= ~(1 << app.Menu.Panel.gameObject.layer);
            camera.enabled = false;
            Shot(name, camera);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }

        private static void Shot(string name, Camera camera)
        {
            if (shotTexture == null)
                shotTexture = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
            Canvas.ForceUpdateCanvases();
            foreach (Camera mirror in mirrors) mirror.Render();

            RenderTexture before = camera.targetTexture;
            camera.targetTexture = shotTexture;
            camera.Render();
            camera.targetTexture = before;

            RenderTexture.active = shotTexture;
            var picture = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0f, 0f, ShotWidth, ShotHeight), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), picture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(picture);
        }
    }
}
