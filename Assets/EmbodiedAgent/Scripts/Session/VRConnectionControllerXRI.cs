using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Opens the microphone in VAD mode at the start and connects or disconnects the Realtime API with a controller button.
/// </summary>
public class VRConnectionControllerXRI : MonoBehaviour
{
    [Header("Component References")]
    [Tooltip("Drag the GameObject with RealtimeAPIWrapper")]
    public RealtimeAPIWrapper apiWrapper;

    [Tooltip("Drag the GameObject with AudioRecorder")]
    public AudioRecorder audioRecorder;

    [Header("Input Settings")]
    [Tooltip("Tie the right controller's A button (Primary Button)")]
    public InputActionReference toggleConnectionAction;

    private void Start()
    {
        // Start in VAD mode and open the microphone
        if (audioRecorder != null)
        {
            audioRecorder.listeningMode = ListeningMode.VAD;
            audioRecorder.StartMicrophone();
            Debug.Log("[VR Controller] Listening mode automatically set to VAD.");
        }
        else
        {
            Debug.LogWarning("[VR Controller] AudioRecorder reference is missing!");
        }
    }

    // Listen for the button while the script is enabled
    private void OnEnable()
    {
        if (toggleConnectionAction != null && toggleConnectionAction.action != null)
        {
            toggleConnectionAction.action.Enable();
            toggleConnectionAction.action.performed += OnTogglePressed;
        }
    }

    // Stop listening when the script is disabled, to avoid leaked subscriptions
    private void OnDisable()
    {
        if (toggleConnectionAction != null && toggleConnectionAction.action != null)
        {
            toggleConnectionAction.action.performed -= OnTogglePressed;
        }
    }

    // Called when the A button is pressed
    private void OnTogglePressed(InputAction.CallbackContext context)
    {
        if (apiWrapper != null)
        {
            Debug.Log("[VR Controller] Right Controller 'A' button pressed! Toggling connection...");
            apiWrapper.ConnectWebSocketButton();
        }
    }
}
