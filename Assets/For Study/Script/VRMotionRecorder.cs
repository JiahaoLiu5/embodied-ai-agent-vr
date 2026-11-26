using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class VRMotionRecorder : MonoBehaviour
{
    // =========================================================
    // Quest Controller Input Actions
    // =========================================================

    [Header("Left Controller Input")]

    [SerializeField]
    private InputActionReference xAction;

    [SerializeField]
    private InputActionReference yAction;

    [SerializeField]
    private InputActionReference leftTriggerAction;


    [Header("Right Controller Input")]

    [SerializeField]
    private InputActionReference aAction;

    [SerializeField]
    private InputActionReference bAction;

    [SerializeField]
    private InputActionReference rightTriggerAction;


    // =========================================================
    // PC Keyboard Debug
    // =========================================================

    [Header("PC Keyboard Debug")]

    [Tooltip(
        "When enabled, keyboard keys 1-6 can also start recording. " +
        "Numpad 1-6 are supported as well."
    )]
    [SerializeField]
    private bool enableKeyboardDebugKeys = true;


    // =========================================================
    // Recording Coordinate Space
    // =========================================================

    [Header("Recording Coordinate Space")]

    [Tooltip("Usually Camera Offset of your live XR Origin.")]
    [SerializeField]
    private Transform recordSpace;


    // =========================================================
    // Tracked Targets
    // =========================================================

    [Header("Tracked Targets")]

    [SerializeField]
    private Transform headTarget;

    [SerializeField]
    private Transform leftHandTarget;

    [SerializeField]
    private Transform rightHandTarget;


    // =========================================================
    // Recording Settings
    // =========================================================

    [Header("Recording Settings")]

    [Tooltip("Maximum desired samples per second.")]
    [SerializeField]
    [Min(1f)]
    private float sampleRate = 72f;


    [Tooltip("How often buffered data is forced to disk.")]
    [SerializeField]
    [Min(0.1f)]
    private float flushIntervalSeconds = 1f;


    [SerializeField]
    private string filePrefix = "VRRecording";


    // =========================================================
    // Runtime State
    // =========================================================

    private bool isRecording = false;

    // Only one recording can be started during each program run.
    private bool hasRecordedThisRun = false;


    private StreamWriter writer;


    private double recordingStartTime;

    private double nextSampleTime;

    private double lastFlushTime;


    private string currentFilePath;


    // =========================================================
    // Input Action Tracking
    // =========================================================

    // Keeps track of actions enabled by THIS script,
    // so we only disable the ones we enabled ourselves.
    private readonly HashSet<InputAction> actionsEnabledByThisScript =
        new HashSet<InputAction>();


    // Prevent duplicate subscriptions if the same action
    // is accidentally assigned to multiple Inspector slots.
    private readonly HashSet<InputAction> subscribedActions =
        new HashSet<InputAction>();


    private static readonly CultureInfo Invariant =
        CultureInfo.InvariantCulture;


    // =========================================================
    // Unity Lifecycle
    // =========================================================

    private void OnEnable()
    {
        SubscribeAction(xAction);
        SubscribeAction(yAction);
        SubscribeAction(leftTriggerAction);

        SubscribeAction(aAction);
        SubscribeAction(bAction);
        SubscribeAction(rightTriggerAction);
    }


    private void OnDisable()
    {
        UnsubscribeAllActions();
    }


    // =========================================================
    // Update
    // =========================================================

    private void Update()
    {
        // -----------------------------------------------------
        // PC keyboard debug:
        //
        // 1 / 2 / 3 / 4 / 5 / 6
        //
        // or
        //
        // Numpad 1 / 2 / 3 / 4 / 5 / 6
        // -----------------------------------------------------

        if (enableKeyboardDebugKeys &&
            AnyKeyboardStartKeyPressed())
        {
            TryStartRecording();
        }
    }


    private void LateUpdate()
    {
        if (!isRecording ||
            writer == null)
        {
            return;
        }


        double now =
            Time.realtimeSinceStartupAsDouble;


        double elapsed =
            now -
            recordingStartTime;


        // =====================================================
        // Record Pose
        // =====================================================

        if (elapsed >= nextSampleTime)
        {
            WriteCurrentFrame(
                elapsed
            );


            double interval =
                1.0 / sampleRate;


            // If Unity missed one or more desired sampling slots,
            // do not duplicate the same physical pose.
            //
            // Instead, advance to the next future sample time.
            do
            {
                nextSampleTime += interval;
            }
            while (nextSampleTime <= elapsed);
        }


        // =====================================================
        // Periodically Flush Data to Disk
        // =====================================================

        if (now - lastFlushTime >=
            flushIntervalSeconds)
        {
            writer.Flush();

            lastFlushTime = now;
        }
    }


    // =========================================================
    // Keyboard Input
    // =========================================================

    private bool AnyKeyboardStartKeyPressed()
    {
        Keyboard keyboard =
            Keyboard.current;


        if (keyboard == null)
        {
            return false;
        }


        // Main keyboard number row
        bool mainNumberRow =
            keyboard.digit1Key.wasPressedThisFrame ||
            keyboard.digit2Key.wasPressedThisFrame ||
            keyboard.digit3Key.wasPressedThisFrame ||
            keyboard.digit4Key.wasPressedThisFrame ||
            keyboard.digit5Key.wasPressedThisFrame ||
            keyboard.digit6Key.wasPressedThisFrame;


        // Numpad
        bool numpad =
            keyboard.numpad1Key.wasPressedThisFrame ||
            keyboard.numpad2Key.wasPressedThisFrame ||
            keyboard.numpad3Key.wasPressedThisFrame ||
            keyboard.numpad4Key.wasPressedThisFrame ||
            keyboard.numpad5Key.wasPressedThisFrame ||
            keyboard.numpad6Key.wasPressedThisFrame;


        return mainNumberRow || numpad;
    }


    // =========================================================
    // Input Action Setup
    // =========================================================

    private void SubscribeAction(
        InputActionReference actionReference
    )
    {
        if (actionReference == null ||
            actionReference.action == null)
        {
            return;
        }


        InputAction action =
            actionReference.action;


        // Prevent duplicate subscriptions.
        if (subscribedActions.Contains(action))
        {
            return;
        }


        if (!action.enabled)
        {
            action.Enable();

            actionsEnabledByThisScript.Add(
                action
            );
        }


        action.performed +=
            OnControllerStartPressed;


        subscribedActions.Add(
            action
        );
    }


    private void UnsubscribeAllActions()
    {
        foreach (
            InputAction action
            in subscribedActions
        )
        {
            if (action != null)
            {
                action.performed -=
                    OnControllerStartPressed;
            }
        }


        foreach (
            InputAction action
            in actionsEnabledByThisScript
        )
        {
            if (action != null &&
                action.enabled)
            {
                action.Disable();
            }
        }


        subscribedActions.Clear();

        actionsEnabledByThisScript.Clear();
    }


    // =========================================================
    // Quest Controller Input
    // =========================================================

    private void OnControllerStartPressed(
        InputAction.CallbackContext context
    )
    {
        TryStartRecording();
    }


    // =========================================================
    // Unified Start Recording Logic
    // =========================================================

    private void TryStartRecording()
    {
        // -----------------------------------------------------
        // First valid button/key press:
        //
        //     Start recording
        //
        // Any later press during this program run:
        //
        //     Ignore
        //
        // Recording ends only when the program / Play Mode exits.
        // -----------------------------------------------------

        if (hasRecordedThisRun ||
            isRecording)
        {
            return;
        }


        StartRecording();
    }


    // =========================================================
    // Start Recording
    // =========================================================

    private void StartRecording()
    {
        if (!ValidateReferences())
        {
            return;
        }


        string folderPath =
            GetRecordingFolder();


        Directory.CreateDirectory(
            folderPath
        );


        string timestamp =
            DateTime.Now.ToString(
                "yyyyMMdd_HHmmss",
                Invariant
            );


        string fileName =
            filePrefix +
            "_" +
            timestamp +
            ".csv";


        currentFilePath =
            Path.Combine(
                folderPath,
                fileName
            );


        try
        {
            writer =
                new StreamWriter(
                    currentFilePath,
                    false,
                    new UTF8Encoding(false),
                    65536
                );


            WriteHeader();


            recordingStartTime =
                Time.realtimeSinceStartupAsDouble;


            nextSampleTime =
                0.0;


            lastFlushTime =
                recordingStartTime;


            isRecording =
                true;


            hasRecordedThisRun =
                true;


            // Capture initial pose immediately.
            WriteCurrentFrame(
                0.0
            );


            nextSampleTime =
                1.0 / sampleRate;


            writer.Flush();


            Debug.Log(
                "[VR Motion Recorder] RECORDING STARTED\n" +
                "File: " +
                currentFilePath
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[VR Motion Recorder] Failed to start recording:\n" +
                e
            );


            isRecording =
                false;


            writer =
                null;
        }
    }


    // =========================================================
    // Record Current Pose
    // =========================================================

    private void WriteCurrentFrame(
        double time
    )
    {
        Pose headPose =
            GetRelativePose(
                headTarget
            );


        Pose leftPose =
            GetRelativePose(
                leftHandTarget
            );


        Pose rightPose =
            GetRelativePose(
                rightHandTarget
            );


        string line =
            string.Join(
                ",",

                // Time
                time.ToString(
                    "F6",
                    Invariant
                ),


                // Head Position
                F(headPose.position.x),
                F(headPose.position.y),
                F(headPose.position.z),

                // Head Rotation
                F(headPose.rotation.x),
                F(headPose.rotation.y),
                F(headPose.rotation.z),
                F(headPose.rotation.w),


                // Left Hand Position
                F(leftPose.position.x),
                F(leftPose.position.y),
                F(leftPose.position.z),

                // Left Hand Rotation
                F(leftPose.rotation.x),
                F(leftPose.rotation.y),
                F(leftPose.rotation.z),
                F(leftPose.rotation.w),


                // Right Hand Position
                F(rightPose.position.x),
                F(rightPose.position.y),
                F(rightPose.position.z),

                // Right Hand Rotation
                F(rightPose.rotation.x),
                F(rightPose.rotation.y),
                F(rightPose.rotation.z),
                F(rightPose.rotation.w)
            );


        writer.WriteLine(
            line
        );
    }


    // =========================================================
    // Convert World Pose to Recording-Space Pose
    // =========================================================

    private Pose GetRelativePose(
        Transform target
    )
    {
        Vector3 relativePosition =
            recordSpace.InverseTransformPoint(
                target.position
            );


        Quaternion relativeRotation =
            Quaternion.Inverse(
                recordSpace.rotation
            )
            *
            target.rotation;


        relativeRotation.Normalize();


        return new Pose(
            relativePosition,
            relativeRotation
        );
    }


    // =========================================================
    // CSV
    // =========================================================

    private void WriteHeader()
    {
        writer.WriteLine(
            "time," +

            "head_px,head_py,head_pz," +
            "head_qx,head_qy,head_qz,head_qw," +

            "left_px,left_py,left_pz," +
            "left_qx,left_qy,left_qz,left_qw," +

            "right_px,right_py,right_pz," +
            "right_qx,right_qy,right_qz,right_qw"
        );
    }


    private string F(
        float value
    )
    {
        return value.ToString(
            "R",
            Invariant
        );
    }


    // =========================================================
    // Finish Recording
    // =========================================================

    private void FinishRecording()
    {
        if (!isRecording)
        {
            return;
        }


        isRecording =
            false;


        try
        {
            if (writer != null)
            {
                double elapsed =
                    Time.realtimeSinceStartupAsDouble
                    -
                    recordingStartTime;


                // Save one final pose.
                WriteCurrentFrame(
                    elapsed
                );


                writer.Flush();

                writer.Close();

                writer.Dispose();


                writer =
                    null;
            }


            Debug.Log(
                "[VR Motion Recorder] RECORDING FINISHED\n" +
                "Saved to: " +
                currentFilePath
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[VR Motion Recorder] Error while closing recording:\n" +
                e
            );
        }
    }


    // =========================================================
    // Application Exit
    // =========================================================

    private void OnApplicationQuit()
    {
        FinishRecording();
    }


    private void OnDestroy()
    {
        // Backup protection for Unity lifecycle edge cases.
        FinishRecording();
    }


    private void OnApplicationPause(
        bool pauseStatus
    )
    {
        if (pauseStatus &&
            isRecording &&
            writer != null)
        {
            writer.Flush();
        }
    }


    private void OnApplicationFocus(
        bool hasFocus
    )
    {
        if (!hasFocus &&
            isRecording &&
            writer != null)
        {
            writer.Flush();
        }
    }


    // =========================================================
    // Save Location
    // =========================================================

    private string GetRecordingFolder()
    {
#if UNITY_EDITOR

        // Unity Editor:
        //
        // YourProject/
        //     Assets/
        //         VRRecordings/

        return Path.Combine(
            Application.dataPath,
            "VRRecordings"
        );

#else

        // Quest / Android:
        //
        // Application.persistentDataPath/
        //     VRRecordings/

        return Path.Combine(
            Application.persistentDataPath,
            "VRRecordings"
        );

#endif
    }


    // =========================================================
    // Validation
    // =========================================================

    private bool ValidateReferences()
    {
        if (recordSpace == null)
        {
            Debug.LogError(
                "[VR Motion Recorder] Record Space is missing."
            );

            return false;
        }


        if (headTarget == null ||
            leftHandTarget == null ||
            rightHandTarget == null)
        {
            Debug.LogError(
                "[VR Motion Recorder] One or more tracked targets are missing."
            );

            return false;
        }


        return true;
    }
}