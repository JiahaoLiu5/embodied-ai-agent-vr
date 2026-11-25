using UnityEngine;

/// <summary>
/// Runs a headset scene on a desktop: the user's rig holds a resting pose, the orbit camera shows the agent,
/// and a button starts or ends the live conversation. Gestures are in the <see cref="GesturePreview"/> panel.
/// </summary>
[DefaultExecutionOrder(-200)]
public class DesktopDemo : MonoBehaviour
{
    [Tooltip("Root of the user's XR rig.")]
    public Transform userRig;
    public RealtimeAPIWrapper api;

    private Transform leftHand, rightHand;

    private void Awake()
    {
        if (userRig == null) return;
        foreach (Camera cam in userRig.GetComponentsInChildren<Camera>(true))
        {
            // The camera's transform stays active because the head target follows it.
            cam.enabled = false;
            foreach (var listener in cam.GetComponents<AudioListener>()) listener.enabled = false;
        }
        leftHand = FreeController("Left Controller");
        rightHand = FreeController("Right Controller");
    }

    private Transform FreeController(string controllerName)
    {
        foreach (Transform t in userRig.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != controllerName) continue;
            foreach (MonoBehaviour behaviour in t.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
            return t;
        }
        return null;
    }

    private void Update()
    {
        float t = Time.time;
        Rest(leftHand, -1f, Mathf.Sin(t));
        Rest(rightHand, 1f, Mathf.Sin(t * 1.2f));
    }

    private static void Rest(Transform hand, float side, float sway)
    {
        if (hand == null) return;
        hand.localPosition = new Vector3(side * 0.24f, -0.80f + 0.01f * sway, 0.12f);
        hand.localRotation = Quaternion.Euler(58f, side * 6f, 0f);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 270f, 10f, 260f, 150f), "Conversation", GUI.skin.window);
        if (api == null)
        {
            GUILayout.Label("No RealtimeAPIWrapper in the scene.");
        }
        else
        {
            if (GUILayout.Button(api.IsConnected ? "End conversation" : "Start conversation")) api.ConnectWebSocketButton();
            GUILayout.Label(api.IsConnected ? "Connected. Talk to the agent." : "Needs an OpenAI key, see the README. The gestures on the left work without one.");
        }
        GUILayout.Label("Right-drag: orbit. Scroll: zoom.");
        GUILayout.EndArea();
    }
}
