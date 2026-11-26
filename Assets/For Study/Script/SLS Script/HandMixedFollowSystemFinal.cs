using UnityEngine;

/// <summary>
/// Mixes the position and absolute local rotation of two tracked hands.
///
/// Rotation continuity is handled by constructing both the short-arc and
/// long-arc absolute Slerp candidates, then selecting the branch that is
/// continuous with the previous output.
///
/// Startup is treated separately: the script waits until the tracked hand
/// rotations have been available and reasonably stable before locking the
/// initial Slerp branch. This prevents an uninitialized first-frame pose from
/// becoming the continuity reference.
/// </summary>
[DefaultExecutionOrder(10000)]
public class HandMixedFollowSystemFinal : MonoBehaviour
{
    [Header("References")]
    public Transform myHead;
    public Transform myHand;
    public Transform partnerHead;
    public Transform partnerHand;
    public Transform modulatedHead;

    [Header("Mixing")]
    [Range(0f, 1f)]
    public float followRatio = 0.5f;

    [Header("Rotation Flip Prevention")]
    [Tooltip(
        "Uses current absolute rotations every frame and chooses between " +
        "the short and long interpolation arcs according to continuity."
    )]
    public bool preventSlerpFlip = true;

    [Tooltip(
        "Prevents branch selection from oscillating when the two candidates " +
        "are almost equally close to the previous output."
    )]
    [Min(0f)]
    public float branchSwitchHysteresis = 1f;

    [Header("Startup Initialization")]
    [Tooltip(
        "Minimum number of LateUpdate frames to wait after enabling the script. " +
        "This gives XR tracking, animation, networking, and IK scripts time to " +
        "write their real hand poses."
    )]
    [Min(0)]
    public int initializationDelayFrames = 5;

    [Tooltip(
        "After the initial delay, both hand rotations must remain within the " +
        "stability threshold for this many consecutive frames before the first " +
        "Slerp branch is locked."
    )]
    [Min(1)]
    public int requiredStableFrames = 3;

    [Tooltip(
        "Maximum allowed per-frame rotation change, in degrees, while deciding " +
        "whether the two input rotations are stable enough to initialize."
    )]
    [Min(0f)]
    public float initializationStabilityAngle = 8f;

    [Tooltip(
        "Maximum number of frames to wait for stable input. This prevents the " +
        "script from waiting forever when the hands are already moving."
    )]
    [Min(1)]
    public int maximumInitializationWaitFrames = 90;

    [Tooltip(
        "While waiting for valid initialization, use myHand.localRotation as " +
        "the temporary output instead of locking an uncertain Slerp branch."
    )]
    public bool followMyHandWhileInitializing = true;

    [Header("Optional Debug")]
    [SerializeField]
    private bool rotationContinuityInitialized;

    [SerializeField]
    private bool usingLongArc;

    [SerializeField]
    private bool waitingForStableInitialization;

    [SerializeField]
    private int initializationFramesElapsed;

    [SerializeField]
    private int stableInitializationFrames;

    [SerializeField]
    private float initializationMyHandChange;

    [SerializeField]
    private float initializationPartnerHandChange;

    [SerializeField]
    private float shortArcFrameChange;

    [SerializeField]
    private float longArcFrameChange;

    private Quaternion previousTargetRotation;

    private bool hasInitializationInputSample;
    private Quaternion previousInitializationMyHandRotation;
    private Quaternion previousInitializationPartnerHandRotation;

    private const float QuaternionEpsilon = 0.000001f;
    private const float AxisEpsilon = 0.00001f;

    private void OnEnable()
    {
        ResetRotationContinuity();
    }

    /// <summary>
    /// LateUpdate is intentional. It allows most tracking, networking,
    /// animation and IK scripts that run in Update to finish first.
    /// </summary>
    private void LateUpdate()
    {
        if (!ReferencesAreValid())
        {
            return;
        }

        float ratio = Mathf.Clamp01(followRatio);

        Vector3 targetPosition = CalculateTargetPosition(ratio);
        Quaternion targetRotation = CalculateTargetRotation(ratio);

        transform.localPosition = targetPosition;
        transform.localRotation = targetRotation;
    }

    private bool ReferencesAreValid()
    {
        return myHead != null &&
               myHand != null &&
               partnerHead != null &&
               partnerHand != null &&
               modulatedHead != null;
    }

    /// <summary>
    /// Preserves the position calculation from the original script.
    /// </summary>
    private Vector3 CalculateTargetPosition(float ratio)
    {
        Vector3 myHeadHand =
            myHand.localPosition - myHead.localPosition;

        Vector3 modHeadForward = modulatedHead.forward;
        Vector3 modHeadForwardPlane = new Vector3(
            modHeadForward.x,
            0f,
            modHeadForward.z
        );

        Vector3 partnerHeadForward = partnerHead.forward;
        Vector3 partnerHeadForwardPlane = new Vector3(
            partnerHeadForward.x,
            0f,
            partnerHeadForward.z
        );

        Quaternion transformRotation = Quaternion.identity;

        if (modHeadForwardPlane.sqrMagnitude > AxisEpsilon &&
            partnerHeadForwardPlane.sqrMagnitude > AxisEpsilon)
        {
            transformRotation = Quaternion.FromToRotation(
                partnerHeadForwardPlane.normalized,
                modHeadForwardPlane.normalized
            );
        }

        Vector3 partnerHeadHand =
            partnerHand.localPosition - partnerHead.localPosition;

        Vector3 transformedPartnerHeadHand =
            transformRotation * partnerHeadHand;

        Vector3 modulatedHeadHand = Vector3.Lerp(
            myHeadHand,
            transformedPartnerHeadHand,
            ratio
        );

        return modulatedHeadHand + modulatedHead.localPosition;
    }

    private Quaternion CalculateTargetRotation(float ratio)
    {
        Quaternion myRotation =
            NormalizeQuaternion(myHand.localRotation);

        Quaternion partnerRotation =
            NormalizeQuaternion(partnerHand.localRotation);

        if (!preventSlerpFlip)
        {
            waitingForStableInitialization = false;

            return Quaternion.Slerp(
                myRotation,
                partnerRotation,
                ratio
            );
        }

        if (!rotationContinuityInitialized)
        {
            bool initializationReady =
                UpdateStartupInitializationState(
                    myRotation,
                    partnerRotation
                );

            if (!initializationReady)
            {
                waitingForStableInitialization = true;

                return followMyHandWhileInitializing
                    ? myRotation
                    : transform.localRotation;
            }

            waitingForStableInitialization = false;
        }

        return CalculateContinuousAbsoluteSlerp(
            myRotation,
            partnerRotation,
            ratio
        );
    }

    /// <summary>
    /// Waits until the startup inputs are no longer the transient first-frame
    /// values produced before tracking/animation/networking has initialized.
    /// </summary>
    private bool UpdateStartupInitializationState(
        Quaternion currentMyRotation,
        Quaternion currentPartnerRotation
    )
    {
        initializationFramesElapsed++;

        if (!hasInitializationInputSample)
        {
            StoreInitializationInputSample(
                currentMyRotation,
                currentPartnerRotation
            );

            initializationMyHandChange = 0f;
            initializationPartnerHandChange = 0f;
            stableInitializationFrames = 0;

            return false;
        }

        initializationMyHandChange = Quaternion.Angle(
            previousInitializationMyHandRotation,
            currentMyRotation
        );

        initializationPartnerHandChange = Quaternion.Angle(
            previousInitializationPartnerHandRotation,
            currentPartnerRotation
        );

        StoreInitializationInputSample(
            currentMyRotation,
            currentPartnerRotation
        );

        if (initializationFramesElapsed <= initializationDelayFrames)
        {
            stableInitializationFrames = 0;
            return false;
        }

        bool inputsAreStable =
            initializationMyHandChange <= initializationStabilityAngle &&
            initializationPartnerHandChange <= initializationStabilityAngle;

        if (inputsAreStable)
        {
            stableInitializationFrames++;
        }
        else
        {
            stableInitializationFrames = 0;
        }

        bool stableRequirementReached =
            stableInitializationFrames >=
            Mathf.Max(1, requiredStableFrames);

        bool maximumWaitReached =
            initializationFramesElapsed >=
            Mathf.Max(
                initializationDelayFrames + 1,
                maximumInitializationWaitFrames
            );

        return stableRequirementReached || maximumWaitReached;
    }

    private void StoreInitializationInputSample(
        Quaternion myRotation,
        Quaternion partnerRotation
    )
    {
        previousInitializationMyHandRotation = myRotation;
        previousInitializationPartnerHandRotation = partnerRotation;
        hasInitializationInputSample = true;
    }

    /// <summary>
    /// Uses current absolute input rotations every frame.
    ///
    /// The first call after startup initialization deliberately selects the
    /// conventional shortest arc. Only subsequent calls use the previous
    /// output to choose between the short and long branches.
    ///
    /// No per-frame rotation delta is integrated, so long-term rotational
    /// drift is not accumulated.
    /// </summary>
    private Quaternion CalculateContinuousAbsoluteSlerp(
        Quaternion fromRotation,
        Quaternion toRotation,
        float ratio
    )
    {
        CalculateAbsoluteSlerpCandidates(
            fromRotation,
            toRotation,
            ratio,
            out Quaternion shortArcCandidate,
            out Quaternion longArcCandidate
        );

        if (!rotationContinuityInitialized)
        {
            // At startup there is no valid historical branch information.
            // After waiting for stable inputs, use the standard shortest arc
            // as the unambiguous initial convention.
            rotationContinuityInitialized = true;
            previousTargetRotation = shortArcCandidate;

            usingLongArc = false;
            shortArcFrameChange = 0f;
            longArcFrameChange = Quaternion.Angle(
                shortArcCandidate,
                longArcCandidate
            );

            return shortArcCandidate;
        }

        shortArcFrameChange = Quaternion.Angle(
            previousTargetRotation,
            shortArcCandidate
        );

        longArcFrameChange = Quaternion.Angle(
            previousTargetRotation,
            longArcCandidate
        );

        float hysteresis =
            Mathf.Max(0f, branchSwitchHysteresis);

        if (usingLongArc)
        {
            if (shortArcFrameChange + hysteresis <
                longArcFrameChange)
            {
                usingLongArc = false;
            }
        }
        else
        {
            if (longArcFrameChange + hysteresis <
                shortArcFrameChange)
            {
                usingLongArc = true;
            }
        }

        Quaternion selectedRotation = usingLongArc
            ? longArcCandidate
            : shortArcCandidate;

        previousTargetRotation = selectedRotation;

        return selectedRotation;
    }

    /// <summary>
    /// Constructs two absolute interpolation candidates:
    /// - the standard shortest route;
    /// - the complementary route to the same final orientation.
    /// </summary>
    private static void CalculateAbsoluteSlerpCandidates(
        Quaternion fromRotation,
        Quaternion toRotation,
        float ratio,
        out Quaternion shortArcCandidate,
        out Quaternion longArcCandidate
    )
    {
        fromRotation = NormalizeQuaternion(fromRotation);
        toRotation = NormalizeQuaternion(toRotation);

        Quaternion relativeRotation = NormalizeQuaternion(
            Quaternion.Inverse(fromRotation) * toRotation
        );

        // q and -q represent the same physical orientation.
        // Positive w makes this the <= 180-degree representation.
        if (relativeRotation.w < 0f)
        {
            relativeRotation = NegateQuaternion(relativeRotation);
        }

        float clampedW =
            Mathf.Clamp(relativeRotation.w, -1f, 1f);

        float halfAngleRadians =
            Mathf.Acos(clampedW);

        float sinHalfAngle =
            Mathf.Sin(halfAngleRadians);

        if (Mathf.Abs(sinHalfAngle) < AxisEpsilon)
        {
            shortArcCandidate = Quaternion.Slerp(
                fromRotation,
                toRotation,
                ratio
            );

            longArcCandidate = shortArcCandidate;
            return;
        }

        Vector3 relativeAxis = new Vector3(
            relativeRotation.x,
            relativeRotation.y,
            relativeRotation.z
        ) / sinHalfAngle;

        relativeAxis.Normalize();

        float shortArcAngle =
            2f * halfAngleRadians * Mathf.Rad2Deg;

        float longArcAngle =
            shortArcAngle - 360f;

        Quaternion shortArcStep = Quaternion.AngleAxis(
            shortArcAngle * ratio,
            relativeAxis
        );

        Quaternion longArcStep = Quaternion.AngleAxis(
            longArcAngle * ratio,
            relativeAxis
        );

        shortArcCandidate = NormalizeQuaternion(
            fromRotation * shortArcStep
        );

        longArcCandidate = NormalizeQuaternion(
            fromRotation * longArcStep
        );
    }

    /// <summary>
    /// Re-enters startup initialization. Call this after recentering,
    /// teleporting, replacing a tracked hand, reconnecting a remote user,
    /// or regaining tracking after a discontinuity.
    /// </summary>
    public void ResetRotationContinuity()
    {
        rotationContinuityInitialized = false;
        previousTargetRotation = Quaternion.identity;

        usingLongArc = false;
        waitingForStableInitialization = true;

        initializationFramesElapsed = 0;
        stableInitializationFrames = 0;
        initializationMyHandChange = 0f;
        initializationPartnerHandChange = 0f;

        hasInitializationInputSample = false;
        previousInitializationMyHandRotation = Quaternion.identity;
        previousInitializationPartnerHandRotation = Quaternion.identity;

        shortArcFrameChange = 0f;
        longArcFrameChange = 0f;
    }

    private static Quaternion NormalizeQuaternion(
        Quaternion value
    )
    {
        float magnitude = Mathf.Sqrt(
            value.x * value.x +
            value.y * value.y +
            value.z * value.z +
            value.w * value.w
        );

        if (magnitude < QuaternionEpsilon)
        {
            return Quaternion.identity;
        }

        float inverseMagnitude = 1f / magnitude;

        return new Quaternion(
            value.x * inverseMagnitude,
            value.y * inverseMagnitude,
            value.z * inverseMagnitude,
            value.w * inverseMagnitude
        );
    }

    private static Quaternion NegateQuaternion(
        Quaternion value
    )
    {
        return new Quaternion(
            -value.x,
            -value.y,
            -value.z,
            -value.w
        );
    }
}
