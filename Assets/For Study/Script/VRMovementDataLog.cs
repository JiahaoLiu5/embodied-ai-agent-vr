using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class VRMovementDataLog : MonoBehaviour
{
    [Header("Real Human Transforms")]
    [SerializeField] private Transform realHumanHead;
    [SerializeField] private Transform realHumanLHand;
    [SerializeField] private Transform realHumanRHand;

    [Header("Real Agent Transforms")]
    [SerializeField] private Transform realAgentHead;
    [SerializeField] private Transform realAgentLHand;
    [SerializeField] private Transform realAgentRHand;

    [Header("Modulated Human Transforms")]
    [SerializeField] private Transform modulatedHumanHead;
    [SerializeField] private Transform modulatedHumanLHand;
    [SerializeField] private Transform modulatedHumanRHand;

    [Header("Modulated Agent Transforms")]
    [SerializeField] private Transform modulatedAgentHead;
    [SerializeField] private Transform modulatedAgentLHand;
    [SerializeField] private Transform modulatedAgentRHand;

    [Header("Input Actions to Start Recording")]
    [SerializeField] private InputActionReference leftControllerX;
    [SerializeField] private InputActionReference leftControllerY;
    [SerializeField] private InputActionReference leftControllerTrigger;
    [SerializeField] private InputActionReference rightControllerA;
    [SerializeField] private InputActionReference rightControllerB;
    [SerializeField] private InputActionReference rightControllerTrigger;

    private string folderName = "VR_Interaction_Movement_Data";
    private string filePath;
    private StreamWriter streamWriter;

    private bool isRecording = false;
    private float recordingStartTime = 0f;

    private void OnEnable()
    {
        // Bind the start recording function to the input actions
        BindInputAction(leftControllerX);
        BindInputAction(leftControllerY);
        BindInputAction(leftControllerTrigger);
        BindInputAction(rightControllerA);
        BindInputAction(rightControllerB);
        BindInputAction(rightControllerTrigger);
    }

    private void OnDisable()
    {
        // Unbind the input actions to prevent memory leaks
        UnbindInputAction(leftControllerX);
        UnbindInputAction(leftControllerY);
        UnbindInputAction(leftControllerTrigger);
        UnbindInputAction(rightControllerA);
        UnbindInputAction(rightControllerB);
        UnbindInputAction(rightControllerTrigger);
    }

    private void BindInputAction(InputActionReference actionReference)
    {
        if (actionReference != null && actionReference.action != null)
        {
            actionReference.action.Enable();
            actionReference.action.performed += StartRecording;
        }
    }

    private void UnbindInputAction(InputActionReference actionReference)
    {
        if (actionReference != null && actionReference.action != null)
        {
            actionReference.action.performed -= StartRecording;
            actionReference.action.Disable();
        }
    }

    private void StartRecording(InputAction.CallbackContext context)
    {
        if (isRecording) return; // Prevent multiple initializations

        isRecording = true;
        recordingStartTime = Time.time; // Treat this exact moment as Time = 0s

        // 1. Setup File Path
        string folderPath = Path.Combine(Application.persistentDataPath, folderName);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string sceneName = SceneManager.GetActiveScene().name;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssffff");
        string fileName = $"Movement_{sceneName}_{timestamp}.csv";
        filePath = Path.Combine(folderPath, fileName);

        Debug.Log($"[VRMovementDataLog] Recording Started! Saving to: {filePath}");

        // 2. Initialize StreamWriter and write header
        streamWriter = new StreamWriter(filePath, false); // false means overwrite if exists (it shouldn't due to timestamp)
        
        string header = "Frame, RelativeTime, Distance_XZ, ViewAngle, " +
                        "RH_Head_X, RH_Head_Y, RH_Head_Z, RH_Head_Pitch, RH_Head_Yaw, RH_Head_Roll, " +
                        "RH_LHand_X, RH_LHand_Y, RH_LHand_Z, RH_LHand_Pitch, RH_LHand_Yaw, RH_LHand_Roll, " +
                        "RH_RHand_X, RH_RHand_Y, RH_RHand_Z, RH_RHand_Pitch, RH_RHand_Yaw, RH_RHand_Roll, " +
                        
                        "RA_Head_X, RA_Head_Y, RA_Head_Z, RA_Head_Pitch, RA_Head_Yaw, RA_Head_Roll, " +
                        "RA_LHand_X, RA_LHand_Y, RA_LHand_Z, RA_LHand_Pitch, RA_LHand_Yaw, RA_LHand_Roll, " +
                        "RA_RHand_X, RA_RHand_Y, RA_RHand_Z, RA_RHand_Pitch, RA_RHand_Yaw, RA_RHand_Roll, " +
                        
                        "MH_Head_X, MH_Head_Y, MH_Head_Z, MH_Head_Pitch, MH_Head_Yaw, MH_Head_Roll, " +
                        "MH_LHand_X, MH_LHand_Y, MH_LHand_Z, MH_LHand_Pitch, MH_LHand_Yaw, MH_LHand_Roll, " +
                        "MH_RHand_X, MH_RHand_Y, MH_RHand_Z, MH_RHand_Pitch, MH_RHand_Yaw, MH_RHand_Roll, " +
                        
                        "MA_Head_X, MA_Head_Y, MA_Head_Z, MA_Head_Pitch, MA_Head_Yaw, MA_Head_Roll, " +
                        "MA_LHand_X, MA_LHand_Y, MA_LHand_Z, MA_LHand_Pitch, MA_LHand_Yaw, MA_LHand_Roll, " +
                        "MA_RHand_X, MA_RHand_Y, MA_RHand_Z, MA_RHand_Pitch, MA_RHand_Yaw, MA_RHand_Roll";
                        
        streamWriter.WriteLine(header);
    }

    private void FixedUpdate()
    {
        // Only log data if the recording has been triggered and all necessary transforms are assigned
        if (!isRecording || streamWriter == null) return;
        
        // Calculate Relative Time
        float relativeTime = Time.time - recordingStartTime;

        // Calculate 2D Distance (X-Z plane) between Real Human Head and Modulated Agent Head
        float distanceXZ = 0f;
        if (realHumanHead != null && modulatedAgentHead != null)
        {
            Vector2 rhPos2D = new Vector2(realHumanHead.position.x, realHumanHead.position.z);
            Vector2 maPos2D = new Vector2(modulatedAgentHead.position.x, modulatedAgentHead.position.z);
            distanceXZ = Vector2.Distance(rhPos2D, maPos2D);
        }

        // Calculate View Angle (Real Human looking at Modulated Agent)
        float viewAngle = CalculateViewAngle(realHumanHead, modulatedAgentHead);

        // Build the data string row
        string dataRow = $"{Time.frameCount}, {relativeTime:F4}, {distanceXZ:F4}, {viewAngle:F2}, " +
                         FormatTransform(realHumanHead) +
                         FormatTransform(realHumanLHand) +
                         FormatTransform(realHumanRHand) +
                         FormatTransform(realAgentHead) +
                         FormatTransform(realAgentLHand) +
                         FormatTransform(realAgentRHand) +
                         FormatTransform(modulatedHumanHead) +
                         FormatTransform(modulatedHumanLHand) +
                         FormatTransform(modulatedHumanRHand) +
                         FormatTransform(modulatedAgentHead) +
                         FormatTransform(modulatedAgentLHand) +
                         FormatTransformLast(modulatedAgentRHand); // Last one without trailing comma

        // Write to stream immediately
        streamWriter.WriteLine(dataRow);
    }

    /// <summary>
    /// Calculates the view angle between the viewer and target on the X-Z plane.
    /// Positive if the target is on the right of the viewer's forward direction, negative if on the left.
    /// </summary>
    private float CalculateViewAngle(Transform viewer, Transform target)
    {
        if (viewer == null || target == null) return 0f;

        // Project coordinates onto the X,Z plane
        Vector3 vPos = new Vector3(viewer.position.x, 0, viewer.position.z);
        Vector3 tPos = new Vector3(target.position.x, 0, target.position.z);
        
        Vector3 dirToTarget = (tPos - vPos).normalized;
        Vector3 viewDir = new Vector3(viewer.forward.x, 0, viewer.forward.z).normalized;

        // Calculate absolute angle (0 - 180)
        float angle = Vector3.Angle(viewDir, dirToTarget);

        // Use 2D cross product to determine Left or Right
        // Cross Product Y component for 2D vectors in XZ plane: (Ax * Bz) - (Az * Bx)
        float crossProduct = (viewDir.x * dirToTarget.z) - (viewDir.z * dirToTarget.x);

        // If crossProduct is positive, target is to the right. If negative, target is to the left.
        return crossProduct >= 0 ? angle : -angle;
    }

    // Helper functions to format Transform Pos and Rot to CSV string to keep FixedUpdate clean
    private string FormatTransform(Transform t)
    {
        if (t == null) return "0,0,0,0,0,0, ";
        return $"{t.position.x:F4}, {t.position.y:F4}, {t.position.z:F4}, " +
               $"{t.eulerAngles.x:F4}, {t.eulerAngles.y:F4}, {t.eulerAngles.z:F4}, ";
    }

    private string FormatTransformLast(Transform t)
    {
        if (t == null) return "0,0,0,0,0,0";
        return $"{t.position.x:F4}, {t.position.y:F4}, {t.position.z:F4}, " +
               $"{t.eulerAngles.x:F4}, {t.eulerAngles.y:F4}, {t.eulerAngles.z:F4}";
    }

    private void OnDestroy()
    {
        // Extremely important to flush and close the file when the app/scene stops
        if (streamWriter != null)
        {
            streamWriter.Flush();
            streamWriter.Close();
            streamWriter.Dispose();
            Debug.Log("[VRMovementDataLog] StreamWriter closed successfully.");
        }
    }
}