using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemoKit;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>
    /// Tests of what a person with a headset and two controllers meets: recentring, whether every control can be reached
    /// and read from the start place, and how much there is to draw.
    /// </summary>
    public static partial class AllInOneSmokeTest
    {
        // Millimetres at a distance of one metre
        private const float SmallestText = 13.5f;
        private const float SmallestTag = 10f;
        private const float SteepestRay = 72f;
        private const float SmallestControl = 1.2f;
        private const float BoardAngle = 43f;

        // A view about as wide as that of a headset
        private const float HeadsetFieldOfView = 96f;
        private const float HeadsetAspect = 1.1f;
        private const int MostTriangles = 260000;
        private const int MostDraws = 160;

        private static float smallestText;

        private static IEnumerator HeadsetTests()
        {
            yield return PlayTest("T17 recentring with a headset", TestRecentring);
            yield return PlayTest("T24 reach and reading", TestReach);
            yield return PlayTest("T25 cost of the picture", TestCost);
            yield return PlayTest("T26 controllers", TestControllers);
            RestorePreferences();
        }

        // Recentring ------------------------------------------------------------------------------------------

        private static IEnumerator TestRecentring()
        {
            yield return Boot();
            Check(app.Rig.IsHeadset && app.Rig.Desktop == null, "the headset is not simulated");
            Time.captureFramerate = MovementRate;
            Transform space = app.Rig.Origin.CameraFloorOffsetObject.transform, head = app.Rig.ViewCamera.transform;
            head.localPosition = new Vector3(0.4f, -0.2f, 0.3f);
            head.localRotation = Quaternion.Euler(0f, 50f, 0f);
            PutHand(space, "Left Controller", head, -0.25f);
            PutHand(space, "Right Controller", head, 0.25f);
            yield return Frames(5);
            yield return GoToPage(StartPanel.MovementPage);
            yield return Click("start/movement.leading");
            yield return WaitFor(() => !app.Switcher.IsSwitching, 20f, "switching does not end");

            app.Recentring.Recentre();
            yield return Frames(3);
            var meter = new StepMeter(IkTargets(app.Body.gameObject));
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                meter.Sample();
                yield return null;
            }
            Check(Vector3.Distance(head.position, app.HeadHome) < 0.01f, "the head is at " + head.position);
            Check(Vector3.Angle(head.forward, Vector3.back) < 2f, "the head looks to " + head.forward);
            Check(meter.LargestStep < 0.03f, "after recentring: " + Centimetres(meter.LargestStep) + " in a frame");
            Note("largest step after recentring " + Centimetres(meter.LargestStep));
            foreach (string foot in new[] { "LeftFoot", "RightFoot" })
            {
                float y = Bone(app.Body.gameObject, foot).position.y;
                Check(y > 0.06f && y < 0.16f, foot + " at " + y);
            }
            Shot("headset_movement");
            yield return Click("start/movement.independent");
        }

        private static void PutHand(Transform space, string name, Transform head, float side)
        {
            Transform hand = space.Find(name);
            hand.localPosition = head.localPosition + head.localRotation * new Vector3(side, -0.55f, 0.3f);
            hand.localRotation = head.localRotation * Quaternion.Euler(20f, 0f, 0f);
        }

        // Reach and reading -----------------------------------------------------------------------------------

        private static IEnumerator TestReach()
        {
            yield return Boot();
            UseNoKey();
            smallestText = float.MaxValue;
            app.Recentring.Recentre();
            app.Choices.IsSession = true;
            app.Choices.Mode = MovementMode.Leading;
            app.Menu.Show(StartPanel.AvatarPage);
            for (int page = StartPanel.AvatarPage; page <= StartPanel.ReadyPage; page++)
            {
                yield return GoToPage(page);
                yield return Wait(0.4f);
                CheckReach(app.Menu.Panel, "start, page " + (page + 1));
            }
            yield return Click("start/key-help");
            yield return Wait(0.5f);
            CheckReach(app.Dialogs.KeyHelp, "key help");
            yield return Click("keyhelp/close");

            yield return StartFree(MovementMode.Leading);
            yield return Wait(0.5f);
            Check(DemoToast.Current == AgentDemoTexts.PanelHint, "no hint where the panel is");
            CheckReach(app.Talk.Panel, "talk");
            Shot("headset_talk");
            app.Dialogs.OpenProblem(RealtimeProblem.SessionEnded, "");
            yield return Wait(0.5f);
            CheckReach(app.Dialogs.Problem, "problem");
            app.Dialogs.CloseAll();
            yield return ReachSession();
            app.Choices.Mode = MovementMode.Independent;
            yield return BackToMenu();
            Note("smallest text " + smallestText.ToString("0.0") + " mm at 1 m");
        }

        private static IEnumerator ReachSession()
        {
            yield return StartSession("environment", SessionPace.Study, MovementMode.Leading);
            foreach (string step in new[] { "warmup-board", "preparation", "debate" })
            {
                yield return Next(step);
                yield return Wait(0.5f);
                CheckReach(app.Session.Panel, "session, " + step);
                CheckBesideBoard(app.Session.Panel, step);
            }
            yield return Click("session/menu");
            yield return Wait(0.5f);
            CheckReach(app.Dialogs.Confirm, "leave");
            yield return Click("confirm/stay");
            while (app.Director.IsRunning) app.Director.Next();
            yield return Wait(0.5f);
            CheckReach(app.Dialogs.Finished, "finished");
        }

        /// <summary>Both hands can point at every control, the controls are large enough, and the texts can be read.</summary>
        private static void CheckReach(DemoPanel panel, string what)
        {
            Vector3 waist = UserRig.Home + new Vector3(0f, 1.15f, -0.25f);
            foreach (Vector3 hand in new[] { waist + Vector3.right * 0.25f, waist + Vector3.left * 0.25f })
            {
                foreach (DemoElement e in DemoUi.Elements)
                {
                    if (e.Panel != panel || !e.gameObject.activeInHierarchy) continue;
                    Vector3 centre = e.RectTransform.TransformPoint(e.RectTransform.rect.center);
                    var ray = new Ray(hand, centre - hand);
                    bool isHit = DemoInput.Raycast(ray, 5f, ~0, out DemoHit hit);
                    Check(isHit && hit.element == e, what + ": a ray does not reach " + e.Id + " but " + (hit.element != null ? hit.element.Id : "nothing"));
                    Check(Vector3.Angle(ray.direction, panel.transform.forward) < SteepestRay, what + ": " + e.Id + " is seen too steeply");
                    Vector2 size = Vector2.Scale(e.RectTransform.rect.size, e.RectTransform.lossyScale);
                    float angle = Mathf.Min(size.x, size.y) / Vector3.Distance(app.HeadHome, centre) * Mathf.Rad2Deg;
                    Check(angle > SmallestControl, what + ": " + e.Id + " is only " + angle.ToString("0.0") + " degrees large");
                }
            }
            CheckTextSizes(panel, what);
            CheckNotHidden(panel, what);
        }

        private static void CheckTextSizes(DemoPanel panel, string what)
        {
            foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                float size = text.fontSize * text.rectTransform.lossyScale.y / Vector3.Distance(app.HeadHome, text.transform.position) * 1000f;
                // The tag of the default choice may be smaller, it only repeats what the selection shows.
                bool isTag = text.text == "Default";
                if (!isTag) smallestText = Mathf.Min(smallestText, size);
                Check(size >= (isTag ? SmallestTag : SmallestText), what + ": \"" + text.text + "\" is only " + size.ToString("0.0") + " mm tall at 1 m");
            }
        }

        /// <summary>No other panel stands between the eyes and this one.</summary>
        private static void CheckNotHidden(DemoPanel panel, string what)
        {
            Rect r = panel.RectTransform.rect;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    Vector3 point = panel.RectTransform.TransformPoint(r.center + new Vector2(x * r.width, y * r.height) * 0.48f);
                    bool isHit = DemoInput.Raycast(new Ray(app.HeadHome, point - app.HeadHome), 5f, ~0, out DemoHit hit);
                    Check(isHit && hit.panel == panel, what + ": hidden behind " + (hit.panel != null ? hit.panel.Id : "nothing"));
                }
            }
        }

        private static void CheckBesideBoard(DemoPanel panel, string what)
        {
            if (app.Boards.Shown == null) return;
            var corners = new Vector3[4];
            panel.RectTransform.GetWorldCorners(corners);
            float nearest = corners.Min(c => AngleFromAhead(c));
            ((RectTransform)app.Boards.Shown.transform.Find("Panel")).GetWorldCorners(corners);
            float board = corners.Max(c => AngleFromAhead(c));
            Check(board < BoardAngle + 1f && nearest > board, what + ": the session panel begins at " + nearest.ToString("0") + " degrees, the board ends at " + board.ToString("0"));
        }

        /// <summary>How far the user has to turn to the side for a point, in degrees.</summary>
        private static float AngleFromAhead(Vector3 point)
        {
            Vector3 direction = Vector3.ProjectOnPlane(point - app.HeadHome, Vector3.up);
            return Vector3.Angle(Vector3.back, direction);
        }

        // Cost ------------------------------------------------------------------------------------------------

        private static IEnumerator TestCost()
        {
            yield return Boot();
            UseNoKey();
            yield return BackToMenu();
            app.Recentring.Recentre();
            yield return GoToPage(StartPanel.MovementPage);
            yield return MeasureCost("movement page");
            yield return StartFree();
            yield return MeasureCost("free conversation");
            app.SelfView = true;
            yield return MeasureCost("free conversation with self view");
            yield return StartSession("work", SessionPace.Study, MovementMode.Mimicry);
            yield return Next("preparation");
            yield return MeasureCost("preparation");
            yield return BackToMenu();
            NoteTextures();
        }

        /// <summary>What the headset has to draw in a frame: the view and one of the mirrors.</summary>
        private static IEnumerator MeasureCost(string what)
        {
            yield return Wait(0.5f);
            Camera view = app.Rig.ViewCamera;
            (int draws, int triangles) main = Cost(view, Matrix4x4.Perspective(HeadsetFieldOfView, HeadsetAspect, view.nearClipPlane, view.farClipPlane));
            (int draws, int triangles)[] others = mirrors.Select(m => Cost(m, m.projectionMatrix)).ToArray();
            int draws = main.draws + others.Max(o => o.draws), triangles = main.triangles + others.Max(o => o.triangles);
            Check(triangles < MostTriangles && draws < MostDraws, what + ": " + draws + " draws, " + triangles + " triangles");
            int canvases = DemoUi.ShownPanels.Count();
            Note(what + ": view " + main.draws + " draws, " + main.triangles / 1000 + " k triangles, with a mirror " + draws + " and " + triangles / 1000 + " k, panels " + canvases);

            var allocated = new List<long>();
            using (var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                for (int i = 0; i < 40; i++)
                {
                    yield return null;
                    if (recorder.Valid) allocated.Add(recorder.LastValue);
                }
            }
            allocated.Sort();
            if (allocated.Count > 0) Note(what + ": " + allocated[allocated.Count / 2] + " bytes of garbage per frame in the Editor");
        }

        private static (int draws, int triangles) Cost(Camera camera, Matrix4x4 projection)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(projection * camera.worldToCameraMatrix);
            int draws = 0;
            long indices = 0;
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                bool isSeen = r.enabled && (camera.cullingMask & 1 << r.gameObject.layer) != 0 && GeometryUtility.TestPlanesAABB(planes, r.bounds);
                if (!isSeen) continue;
                Mesh mesh = r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;
                draws += r.sharedMaterials.Length;
                for (int i = 0; i < mesh.subMeshCount; i++) indices += mesh.GetIndexCount(i);
            }
            return (draws, (int)(indices / 3));
        }

        /// <summary>The memory of all textures that the scene needs, computed from their formats.</summary>
        private static void NoteTextures()
        {
            long bytes = 0;
            IEnumerable<Texture2D> textures = AssetDatabase.GetDependencies(AllInOnePaths.Scene, true)
                .Where(p => !p.EndsWith(".unity")).SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<Texture2D>().Distinct();
            foreach (Texture2D texture in textures)
            {
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                    bytes += GraphicsFormatUtility.ComputeMipmapSize(Mathf.Max(1, texture.width >> mip), Mathf.Max(1, texture.height >> mip), texture.graphicsFormat);
            }
            Note("textures of the scene: " + bytes / 1048576 + " MB");
        }
    }
}
