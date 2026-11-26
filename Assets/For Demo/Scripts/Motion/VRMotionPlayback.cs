using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

public class VRMotionPlayback : MonoBehaviour
{
    // =========================================================
    // Frame Data
    // =========================================================

    private struct MotionFrame
    {
        public double time;

        public Vector3 headPosition;
        public Quaternion headRotation;

        public Vector3 leftPosition;
        public Quaternion leftRotation;

        public Vector3 rightPosition;
        public Quaternion rightRotation;
    }


    // =========================================================
    // Recording Data
    // =========================================================

    private class RecordingData
    {
        public TextAsset sourceFile;

        public List<MotionFrame> frames =
            new List<MotionFrame>();
    }


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
        "When enabled, keyboard keys 1-6 can also start playback. " +
        "Numpad 1-6 are supported as well."
    )]
    [SerializeField]
    private bool enableKeyboardDebugKeys = true;


    // =========================================================
    // Recording Files
    // =========================================================

    [Header("Recording Files - Random Selection")]

    [Tooltip("Recording file candidate 1.")]
    [SerializeField]
    private TextAsset recordingFile1;


    [Tooltip("Recording file candidate 2.")]
    [SerializeField]
    private TextAsset recordingFile2;


    [Tooltip("Recording file candidate 3.")]
    [SerializeField]
    private TextAsset recordingFile3;


    [Tooltip("Recording file candidate 4.")]
    [SerializeField]
    private TextAsset recordingFile4;


    // =========================================================
    // Playback Rig
    // =========================================================

    [Header("Playback Coordinate Space")]

    [SerializeField]
    private Transform playbackSpace;


    [Header("Playback Targets")]

    [SerializeField]
    private Transform headTarget;

    [SerializeField]
    private Transform leftHandTarget;

    [SerializeField]
    private Transform rightHandTarget;


    // =========================================================
    // Loaded Recordings
    // =========================================================

    private readonly List<RecordingData> loadedRecordings =
        new List<RecordingData>();


    // The recording currently being played.
    private RecordingData activeRecording;


    // =========================================================
    // Runtime Playback State
    // =========================================================

    private bool isPlaying =
        false;


    private double playbackStartTime;


    private int currentFrameIndex =
        0;


    // =========================================================
    // Input Action Tracking
    // =========================================================

    private readonly HashSet<InputAction> actionsEnabledByThisScript =
        new HashSet<InputAction>();


    private readonly HashSet<InputAction> subscribedActions =
        new HashSet<InputAction>();


    private static readonly CultureInfo Invariant =
        CultureInfo.InvariantCulture;


    // =========================================================
    // Unity Lifecycle
    // =========================================================

    private void Awake()
    {
        LoadAllRecordings();
    }


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
        // PC keyboard debug
        // -----------------------------------------------------

        if (enableKeyboardDebugKeys &&
            AnyKeyboardStartKeyPressed())
        {
            TryStartPlayback();
        }
    }


    private void LateUpdate()
    {
        if (!isPlaying ||
            activeRecording == null ||
            activeRecording.frames == null ||
            activeRecording.frames.Count == 0)
        {
            return;
        }


        List<MotionFrame> frames =
            activeRecording.frames;


        double playbackTime =
            Time.realtimeSinceStartupAsDouble
            -
            playbackStartTime;


        MotionFrame lastFrame =
            frames[
                frames.Count - 1
            ];


        // =====================================================
        // Playback Finished
        // =====================================================

        if (playbackTime >=
            lastFrame.time)
        {
            ApplyFrame(
                lastFrame
            );


            isPlaying =
                false;


            Debug.Log(
                "[VR Motion Playback] PLAYBACK FINISHED\n" +
                "Recording: " +
                activeRecording.sourceFile.name
            );


            return;
        }


        // =====================================================
        // Safety for One-Frame Recording
        // =====================================================

        if (frames.Count == 1)
        {
            ApplyFrame(
                frames[0]
            );


            isPlaying =
                false;


            return;
        }


        // =====================================================
        // Find Current Frame Pair
        // =====================================================

        while (
            currentFrameIndex <
            frames.Count - 2
            &&
            frames[
                currentFrameIndex + 1
            ].time < playbackTime
        )
        {
            currentFrameIndex++;
        }


        MotionFrame frameA =
            frames[
                currentFrameIndex
            ];


        MotionFrame frameB =
            frames[
                currentFrameIndex + 1
            ];


        double frameDuration =
            frameB.time -
            frameA.time;


        float t;


        if (frameDuration <=
            0.000001)
        {
            t = 0f;
        }
        else
        {
            t =
                Mathf.Clamp01(
                    (float)(
                        (playbackTime - frameA.time)
                        /
                        frameDuration
                    )
                );
        }


        ApplyInterpolatedFrame(
            frameA,
            frameB,
            t
        );
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
        TryStartPlayback();
    }


    // =========================================================
    // Unified Start Playback Logic
    // =========================================================

    private void TryStartPlayback()
    {
        // While playback is already active,
        // ignore all additional controller and keyboard presses.
        if (isPlaying)
        {
            return;
        }


        StartPlayback();
    }


    // =========================================================
    // Load All Four Recording Files
    // =========================================================

    private void LoadAllRecordings()
    {
        loadedRecordings.Clear();


        TryLoadRecording(
            recordingFile1,
            1
        );


        TryLoadRecording(
            recordingFile2,
            2
        );


        TryLoadRecording(
            recordingFile3,
            3
        );


        TryLoadRecording(
            recordingFile4,
            4
        );


        Debug.Log(
            "[VR Motion Playback] Finished loading recordings.\n" +
            "Valid recordings: " +
            loadedRecordings.Count +
            " / 4"
        );
    }


    // =========================================================
    // Load One Recording
    // =========================================================

    private void TryLoadRecording(
        TextAsset recordingFile,
        int slotNumber
    )
    {
        // Empty slots are allowed.
        if (recordingFile == null)
        {
            Debug.LogWarning(
                "[VR Motion Playback] Recording File " +
                slotNumber +
                " is empty."
            );


            return;
        }


        try
        {
            RecordingData data =
                new RecordingData();


            data.sourceFile =
                recordingFile;


            using (
                StringReader reader =
                    new StringReader(
                        recordingFile.text
                    )
            )
            {
                // Skip CSV header.
                reader.ReadLine();


                string line;

                int lineNumber =
                    1;


                while (
                    (line = reader.ReadLine()) != null
                )
                {
                    lineNumber++;


                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }


                    string[] values =
                        line.Split(',');


                    if (values.Length < 22)
                    {
                        Debug.LogWarning(
                            "[VR Motion Playback] Invalid CSV line.\n" +
                            "File: " +
                            recordingFile.name +
                            "\nLine: " +
                            lineNumber
                        );


                        continue;
                    }


                    MotionFrame frame =
                        new MotionFrame();


                    // =================================================
                    // Time
                    // =================================================

                    frame.time =
                        D(values[0]);


                    // =================================================
                    // Head
                    // =================================================

                    frame.headPosition =
                        new Vector3(
                            F(values[1]),
                            F(values[2]),
                            F(values[3])
                        );


                    frame.headRotation =
                        new Quaternion(
                            F(values[4]),
                            F(values[5]),
                            F(values[6]),
                            F(values[7])
                        );


                    frame.headRotation.Normalize();


                    // =================================================
                    // Left Hand
                    // =================================================

                    frame.leftPosition =
                        new Vector3(
                            F(values[8]),
                            F(values[9]),
                            F(values[10])
                        );


                    frame.leftRotation =
                        new Quaternion(
                            F(values[11]),
                            F(values[12]),
                            F(values[13]),
                            F(values[14])
                        );


                    frame.leftRotation.Normalize();


                    // =================================================
                    // Right Hand
                    // =================================================

                    frame.rightPosition =
                        new Vector3(
                            F(values[15]),
                            F(values[16]),
                            F(values[17])
                        );


                    frame.rightRotation =
                        new Quaternion(
                            F(values[18]),
                            F(values[19]),
                            F(values[20]),
                            F(values[21])
                        );


                    frame.rightRotation.Normalize();


                    data.frames.Add(
                        frame
                    );
                }
            }


            // Only add valid recordings.
            if (data.frames.Count > 0)
            {
                loadedRecordings.Add(
                    data
                );


                double duration =
                    data.frames[
                        data.frames.Count - 1
                    ].time;


                Debug.Log(
                    "[VR Motion Playback] Recording loaded successfully.\n" +
                    "Slot: " +
                    slotNumber +
                    "\nFile: " +
                    recordingFile.name +
                    "\nFrames: " +
                    data.frames.Count +
                    "\nDuration: " +
                    duration.ToString(
                        "F2",
                        Invariant
                    ) +
                    " seconds"
                );
            }
            else
            {
                Debug.LogWarning(
                    "[VR Motion Playback] Recording contains no valid frames.\n" +
                    "File: " +
                    recordingFile.name
                );
            }
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[VR Motion Playback] Failed to load recording.\n" +
                "Slot: " +
                slotNumber +
                "\nFile: " +
                recordingFile.name +
                "\nError:\n" +
                e
            );
        }
    }


    // =========================================================
    // Start Playback
    // =========================================================

    private void StartPlayback()
    {
        if (!ValidateReferences())
        {
            return;
        }


        if (loadedRecordings.Count == 0)
        {
            Debug.LogWarning(
                "[VR Motion Playback] No valid recordings are loaded."
            );


            return;
        }


        // =====================================================
        // RANDOMLY SELECT ONE RECORDING
        // =====================================================

        int randomIndex =
            UnityEngine.Random.Range(
                0,
                loadedRecordings.Count
            );


        activeRecording =
            loadedRecordings[
                randomIndex
            ];


        // =====================================================
        // Start From Beginning
        // =====================================================

        currentFrameIndex =
            0;


        playbackStartTime =
            Time.realtimeSinceStartupAsDouble;


        isPlaying =
            true;


        // Immediately move targets to first pose.
        ApplyFrame(
            activeRecording.frames[0]
        );


        Debug.Log(
            "[VR Motion Playback] PLAYBACK STARTED\n" +
            "Randomly selected recording: " +
            activeRecording.sourceFile.name +
            "\nCandidate index: " +
            (randomIndex + 1) +
            " / " +
            loadedRecordings.Count
        );
    }


    // =========================================================
    // Apply Exact Frame
    // =========================================================

    private void ApplyFrame(
        MotionFrame frame
    )
    {
        ApplyPose(
            headTarget,
            frame.headPosition,
            frame.headRotation
        );


        ApplyPose(
            leftHandTarget,
            frame.leftPosition,
            frame.leftRotation
        );


        ApplyPose(
            rightHandTarget,
            frame.rightPosition,
            frame.rightRotation
        );
    }


    // =========================================================
    // Apply Interpolated Frame
    // =========================================================

    private void ApplyInterpolatedFrame(
        MotionFrame a,
        MotionFrame b,
        float t
    )
    {
        // =====================================================
        // Head
        // =====================================================

        Vector3 headPosition =
            Vector3.Lerp(
                a.headPosition,
                b.headPosition,
                t
            );


        Quaternion headRotation =
            Quaternion.Slerp(
                a.headRotation,
                b.headRotation,
                t
            );


        // =====================================================
        // Left Hand
        // =====================================================

        Vector3 leftPosition =
            Vector3.Lerp(
                a.leftPosition,
                b.leftPosition,
                t
            );


        Quaternion leftRotation =
            Quaternion.Slerp(
                a.leftRotation,
                b.leftRotation,
                t
            );


        // =====================================================
        // Right Hand
        // =====================================================

        Vector3 rightPosition =
            Vector3.Lerp(
                a.rightPosition,
                b.rightPosition,
                t
            );


        Quaternion rightRotation =
            Quaternion.Slerp(
                a.rightRotation,
                b.rightRotation,
                t
            );


        // =====================================================
        // Apply
        // =====================================================

        ApplyPose(
            headTarget,
            headPosition,
            headRotation
        );


        ApplyPose(
            leftHandTarget,
            leftPosition,
            leftRotation
        );


        ApplyPose(
            rightHandTarget,
            rightPosition,
            rightRotation
        );
    }


    // =========================================================
    // Convert Recorded Relative Pose to Playback World Pose
    // =========================================================

    private void ApplyPose(
        Transform target,
        Vector3 relativePosition,
        Quaternion relativeRotation
    )
    {
        target.position =
            playbackSpace.TransformPoint(
                relativePosition
            );


        target.rotation =
            playbackSpace.rotation
            *
            relativeRotation;
    }


    // =========================================================
    // CSV Parsing
    // =========================================================

    private float F(
        string value
    )
    {
        return float.Parse(
            value,
            NumberStyles.Float,
            Invariant
        );
    }


    private double D(
        string value
    )
    {
        return double.Parse(
            value,
            NumberStyles.Float,
            Invariant
        );
    }


    // =========================================================
    // Validation
    // =========================================================

    private bool ValidateReferences()
    {
        if (playbackSpace == null)
        {
            Debug.LogError(
                "[VR Motion Playback] Playback Space is missing."
            );


            return false;
        }


        if (headTarget == null ||
            leftHandTarget == null ||
            rightHandTarget == null)
        {
            Debug.LogError(
                "[VR Motion Playback] One or more playback targets are missing."
            );


            return false;
        }


        return true;
    }
}
