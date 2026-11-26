using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Rebuilds the desktop demo scene from the scene 02_Independent: same agent, room, and rigs, without a headset.</summary>
public static class DesktopDemoBuilder
{
    private const string Root = "Assets/For Demo/";
    private const string SourceScene = Root + "Scenes/02_Independent.unity";
    private const string ScenePath = Root + "Scenes/DesktopDemo.unity";
    private const string UserRigName = "XR Origin (XR Rig) ECA 2";

    [MenuItem("Embodied Agent/Rebuild Desktop Demo")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();

        // The session flow and the boards belong to the headset scenes.
        string[] headsetOnly = { "Session Manager", "Board", "DataLoggerObject", "DemoUI" };
        foreach (GameObject go in roots)
            if (headsetOnly.Contains(go.name)) Object.DestroyImmediate(go);

        roots = scene.GetRootGameObjects();
        var camera = new GameObject("Demo Camera", typeof(Camera), typeof(AudioListener), typeof(OrbitCamera)) { tag = "MainCamera" };
        camera.GetComponent<Camera>().fieldOfView = 40f;
        camera.GetComponent<Camera>().nearClipPlane = 0.05f;
        camera.GetComponent<Camera>().depth = 10f;
        camera.GetComponent<OrbitCamera>().maxDistance = 2.6f;

        var demo = new GameObject("Desktop Demo").AddComponent<DesktopDemo>();
        demo.userRig = roots.First(r => r.name == UserRigName).transform;
        demo.api = roots.Select(r => r.GetComponentInChildren<RealtimeAPIWrapper>(true)).First(a => a != null);

        var panel = new SerializedObject(demo.gameObject.AddComponent<GesturePreview>());
        panel.FindProperty("showOnStart").boolValue = true;
        panel.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("Desktop demo rebuilt: " + ScenePath);
    }
}
