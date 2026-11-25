using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Writes the tracked and the modulated head and hand poses of the user and the agent to a CSV file.
/// Logging starts with any of the assigned controller buttons. Rows are written in FixedUpdate, positions are in
/// metres and rotations are Euler angles in degrees, both in world space. Numbers use a decimal point on every
/// system, and the cells of a transform that is not assigned stay empty.
/// </summary>
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

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private string folderName = "VR_Interaction_Movement_Data";
    private string filePath;
    private StreamWriter streamWriter;
    private readonly StringBuilder row = new StringBuilder(1024);

    private bool isRecording = false;
    private float recordingStartTime = 0f;

    // Only actions enabled by this script are disabled again; the others may be in use elsewhere
    private readonly HashSet<InputAction> actionsEnabledByThisScript = new HashSet<InputAction>();

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
        if (actionReference == null || actionReference.action == null) return;

        InputAction action = actionReference.action;
        if (!action.enabled)
        {
            action.Enable();
            actionsEnabledByThisScript.Add(action);
        }
        action.performed += StartRecording;
    }

    private void UnbindInputAction(InputActionReference actionReference)
    {
        if (actionReference == null || actionReference.action == null) return;

        InputAction action = actionReference.action;
        action.performed -= StartRecording;
        if (actionsEnabledByThisScript.Remove(action))
        {
            action.Disable();
        }
    }

    private void StartRecording(InputAction.CallbackContext context)
    {
        if (isRecording) return; // Prevent multiple initializations

        // 1. Setup File Path
        string folderPath = Path.Combine(Application.persistentDataPath, folderName);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string sceneName = SceneManager.GetActiveScene().name;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssffff", Invariant);
        string fileName = $"Movement_{sceneName}_{timestamp}.csv";
        filePath = Path.Combine(folderPath, fileName);

        // 2. Initialize StreamWriter and write header
        streamWriter = new StreamWriter(filePath, false); // false means overwrite if exists (it shouldn't due to timestamp)

        string header = "Frame,RelativeTime,Distance_XZ,ViewAngle," +
                        "RH_Head_X,RH_Head_Y,RH_Head_Z,RH_Head_Pitch,RH_Head_Yaw,RH_Head_Roll," +
                        "RH_LHand_X,RH_LHand_Y,RH_LHand_Z,RH_LHand_Pitch,RH_LHand_Yaw,RH_LHand_Roll," +
                        "RH_RHand_X,RH_RHand_Y,RH_RHand_Z,RH_RHand_Pitch,RH_RHand_Yaw,RH_RHand_Roll," +

                        "RA_Head_X,RA_Head_Y,RA_Head_Z,RA_Head_Pitch,RA_Head_Yaw,RA_Head_Roll," +
                        "RA_LHand_X,RA_LHand_Y,RA_LHand_Z,RA_LHand_Pitch,RA_LHand_Yaw,RA_LHand_Roll," +
                        "RA_RHand_X,RA_RHand_Y,RA_RHand_Z,RA_RHand_Pitch,RA_RHand_Yaw,RA_RHand_Roll," +

                        "MH_Head_X,MH_Head_Y,MH_Head_Z,MH_Head_Pitch,MH_Head_Yaw,MH_Head_Roll," +
                        "MH_LHand_X,MH_LHand_Y,MH_LHand_Z,MH_LHand_Pitch,MH_LHand_Yaw,MH_LHand_Roll," +
                        "MH_RHand_X,MH_RHand_Y,MH_RHand_Z,MH_RHand_Pitch,MH_RHand_Yaw,MH_RHand_Roll," +

                        "MA_Head_X,MA_Head_Y,MA_Head_Z,MA_Head_Pitch,MA_Head_Yaw,MA_Head_Roll," +
                        "MA_LHand_X,MA_LHand_Y,MA_LHand_Z,MA_LHand_Pitch,MA_LHand_Yaw,MA_LHand_Roll," +
                        "MA_RHand_X,MA_RHand_Y,MA_RHand_Z,MA_RHand_Pitch,MA_RHand_Yaw,MA_RHand_Roll";

        streamWriter.WriteLine(header);

        isRecording = true;
        recordingStartTime = Time.time; // Treat this exact moment as Time = 0s
        Debug.Log($"[VRMovementDataLog] Recording Started! Saving to: {filePath}");
    }

    private void FixedUpdate()
    {
        // Only log data if the recording has been triggered
        if (!isRecording || streamWriter == null) return;

        row.Clear();
        row.Append(Time.frameCount.ToString(Invariant));

        // Relative Time
        AppendNumber(Time.time - recordingStartTime, "F4");

        // 2D Distance (X-Z plane) and view angle between Real Human Head and Modulated Agent Head
        if (realHumanHead != null && modulatedAgentHead != null)
        {
            Vector2 rhPos2D = new Vector2(realHumanHead.position.x, realHumanHead.position.z);
            Vector2 maPos2D = new Vector2(modulatedAgentHead.position.x, modulatedAgentHead.position.z);
            AppendNumber(Vector2.Distance(rhPos2D, maPos2D), "F4");

            // Real Human looking at Modulated Agent
            AppendNumber(CalculateViewAngle(realHumanHead, modulatedAgentHead), "F2");
        }
        else
        {
            AppendEmptyCells(2);
        }

        AppendTransform(realHumanHead);
        AppendTransform(realHumanLHand);
        AppendTransform(realHumanRHand);
        AppendTransform(realAgentHead);
        AppendTransform(realAgentLHand);
        AppendTransform(realAgentRHand);
        AppendTransform(modulatedHumanHead);
        AppendTransform(modulatedHumanLHand);
        AppendTransform(modulatedHumanRHand);
        AppendTransform(modulatedAgentHead);
        AppendTransform(modulatedAgentLHand);
        AppendTransform(modulatedAgentRHand);

        streamWriter.WriteLine(row.ToString());
    }

    /// <summary>
    /// Calculates the view angle between the viewer and target on the X-Z plane, in degrees.
    /// Positive if the target is on the left of the viewer's forward direction, negative if on the right.
    /// </summary>
    private float CalculateViewAngle(Transform viewer, Transform target)
    {
        // Project coordinates onto the X,Z plane
        Vector3 vPos = new Vector3(viewer.position.x, 0, viewer.position.z);
        Vector3 tPos = new Vector3(target.position.x, 0, target.position.z);

        Vector3 dirToTarget = (tPos - vPos).normalized;
        Vector3 viewDir = new Vector3(viewer.forward.x, 0, viewer.forward.z).normalized;

        // Calculate absolute angle (0 - 180)
        float angle = Vector3.Angle(viewDir, dirToTarget);

        // Use 2D cross product (Ax * Bz) - (Az * Bx) to determine Left or Right.
        // It is positive when the target is on the left and negative when it is on the right.
        float crossProduct = (viewDir.x * dirToTarget.z) - (viewDir.z * dirToTarget.x);

        return crossProduct >= 0 ? angle : -angle;
    }

    // Helper functions that add cells to the current row, to keep FixedUpdate clean
    private void AppendNumber(float value, string format)
    {
        row.Append(',').Append(value.ToString(format, Invariant));
    }

    private void AppendEmptyCells(int count)
    {
        row.Append(',', count);
    }

    private void AppendTransform(Transform t)
    {
        if (t == null)
        {
            AppendEmptyCells(6);
            return;
        }

        Vector3 position = t.position;
        Vector3 angles = t.eulerAngles;
        AppendNumber(position.x, "F4");
        AppendNumber(position.y, "F4");
        AppendNumber(position.z, "F4");
        AppendNumber(angles.x, "F4");
        AppendNumber(angles.y, "F4");
        AppendNumber(angles.z, "F4");
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        // A paused application may be closed by the system without further notice
        if (pauseStatus && streamWriter != null)
        {
            streamWriter.Flush();
        }
    }

    private void OnApplicationQuit()
    {
        CloseFile();
    }

    private void OnDestroy()
    {
        CloseFile();
    }

    private void CloseFile()
    {
        // Extremely important to flush and close the file when the app/scene stops
        if (streamWriter == null) return;

        isRecording = false;
        streamWriter.Flush();
        streamWriter.Close();
        streamWriter.Dispose();
        streamWriter = null;
        Debug.Log("[VRMovementDataLog] StreamWriter closed successfully.");
    }
}
