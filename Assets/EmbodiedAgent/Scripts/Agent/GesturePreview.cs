using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Optional panel that lists the gestures offered to the model and plays the clicked one on the agent.
/// It needs no API key and no network connection.
///
/// The panel is drawn with IMGUI, so it appears in the Game view and in desktop builds, not inside a headset.
/// </summary>
public class GesturePreview : MonoBehaviour
{
    [Tooltip("Key that shows or hides the panel.")]
    [SerializeField] private Key toggleKey = Key.G;

    [Tooltip("Whether the panel is visible at the start.")]
    [SerializeField] private bool showOnStart = false;

    private const float Margin = 10f;
    private const float PanelWidth = 240f;
    private const float MaximumPanelHeight = 640f;

    private RealtimeAPIWrapper apiWrapper;
    private AgentAnimationController[] agents = new AgentAnimationController[0];
    private bool isVisible;
    private Vector2 scrollPosition;
    private string message = "";

    private void Start()
    {
        SetVisible(showOnStart);
    }

    private void Update()
    {
        if (toggleKey == Key.None || Keyboard.current == null) return;

        if (Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            SetVisible(!isVisible);
        }
    }

    private void SetVisible(bool visible)
    {
        isVisible = visible;
        if (!visible) return;

        // Search again each time the panel opens, because agents may have been added or removed
        apiWrapper = FindAnyObjectByType<RealtimeAPIWrapper>();
#if UNITY_6000_4_OR_NEWER
        agents = FindObjectsByType<AgentAnimationController>();
#else
        agents = FindObjectsByType<AgentAnimationController>(FindObjectsSortMode.None);
#endif
        message = "";
    }

    private void OnGUI()
    {
        if (!isVisible) return;

        float height = Mathf.Min(MaximumPanelHeight, Screen.height - 2f * Margin);
        GUILayout.BeginArea(new Rect(Margin, Margin, PanelWidth, height), "Gesture preview", GUI.skin.window);

        IReadOnlyList<string> gestureNames = apiWrapper != null ? apiWrapper.GestureNames : null;
        if (gestureNames == null)
        {
            GUILayout.Label("No RealtimeAPIWrapper in the scene.");
        }
        else
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            foreach (string gestureName in gestureNames)
            {
                if (GUILayout.Button(gestureName)) Play(gestureName);
            }
            GUILayout.EndScrollView();
        }

        GUILayout.Label(message.Length > 0 ? message : $"Press {toggleKey} to hide this panel.");
        GUILayout.EndArea();
    }

    /// <summary>
    /// Plays a gesture on the Animator of every AgentAnimationController in the scene.
    /// </summary>
    private void Play(string gestureName)
    {
        int played = 0;
        foreach (AgentAnimationController agent in agents)
        {
            if (agent == null) continue;

            Animator animator = agent.GetComponent<Animator>();
            if (!AgentAnimationController.HasState(animator, gestureName)) continue;

            animator.CrossFade(gestureName, 0.2f);
            played++;
        }

        message = played > 0 ? "Playing: " + gestureName : "No active agent has this gesture: " + gestureName;
    }
}
