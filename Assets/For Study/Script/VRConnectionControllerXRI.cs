using UnityEngine;
using UnityEngine.InputSystem; 

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
        // 强制系统在启动时进入 VAD 模式并开启麦克风
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

    // 启用脚本时，开始监听按键事件
    private void OnEnable()
    {
        if (toggleConnectionAction != null)
        {
            toggleConnectionAction.action.Enable();
            toggleConnectionAction.action.performed += OnTogglePressed;
        }
    }

    // 禁用脚本时，注销监听，防止内存泄漏
    private void OnDisable()
    {
        if (toggleConnectionAction != null)
        {
            toggleConnectionAction.action.performed -= OnTogglePressed;
        }
    }

    // 当 A 键被按下时触发的方法
    private void OnTogglePressed(InputAction.CallbackContext context)
    {
        if (apiWrapper != null)
        {
            Debug.Log("[VR Controller] Right Controller 'A' button pressed! Toggling connection...");
            apiWrapper.ConnectWebSocketButton();
        }
    }
}