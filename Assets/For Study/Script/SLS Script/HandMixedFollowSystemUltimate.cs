using UnityEngine;

public class HandMixedFollowSystemUltimate : MonoBehaviour
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
        "Still uses the current absolute rotations of myHand and partnerHand " +
        "every frame. It chooses between the short and long interpolation arcs " +
        "according to continuity with the previous modulated-hand rotation."
    )]
    public bool preventSlerpFlip = true;

    [Tooltip(
        "Prevents branch selection from oscillating when the two candidates " +
        "are almost equally close to the previous output. Usually 0.5-2 degrees."
    )]
    [Min(0f)]
    public float branchSwitchHysteresis = 1f;

    [Header("Optional Debug")]
    [SerializeField]
    private bool usingLongArc;

    [SerializeField]
    private float shortArcFrameChange;

    [SerializeField]
    private float longArcFrameChange;

    private bool rotationContinuityInitialized;
    private Quaternion previousTargetRotation;

    private const float QuaternionEpsilon = 0.000001f;
    private const float AxisEpsilon = 0.00001f;

    private void OnEnable()
    {
        ResetRotationContinuity();
    }

    private void Update()
    {
        if (myHead == null ||
            myHand == null ||
            partnerHead == null ||
            partnerHand == null ||
            modulatedHead == null)
        {
            return;
        }

        float ratio = Mathf.Clamp01(followRatio);

        // =============================================================
        // Position calculation:
        // preserves the original position-modulation logic.
        // =============================================================

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

        // Avoid invalid FromToRotation input when a head points almost
        // perfectly upward or downward.
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

        Vector3 targetPosition =
            modulatedHeadHand + modulatedHead.localPosition;

        // =============================================================
        // Rotation calculation:
        // still calculated from the CURRENT ABSOLUTE local rotations.
        // No per-frame rotation delta is accumulated.
        // =============================================================

        Quaternion targetRotation;

        if (preventSlerpFlip)
        {
            targetRotation = CalculateContinuousAbsoluteSlerp(
                myHand.localRotation,
                partnerHand.localRotation,
                ratio
            );
        }
        else
        {
            // Original behavior.
            targetRotation = Quaternion.Slerp(
                myHand.localRotation,
                partnerHand.localRotation,
                ratio
            );
        }

        transform.localPosition = targetPosition;
        transform.localRotation = targetRotation;
    }

    /// <summary>
    /// Uses the current absolute input rotations every frame.
    ///
    /// Unity's Quaternion.Slerp always selects the shortest arc. When the
    /// relative rotation crosses 180 degrees, that shortest arc changes
    /// sides and the interpolated result can jump.
    ///
    /// This method explicitly constructs:
    /// 1. the shortest-arc candidate;
    /// 2. the complementary long-arc candidate.
    ///
    /// It then selects the candidate that is more continuous with the
    /// previous output rotation.
    ///
    /// This is NOT rotation-delta integration, so it does not accumulate
    /// long-term rotational drift.
    /// </summary>
    private Quaternion CalculateContinuousAbsoluteSlerp(
        Quaternion fromRotation,
        Quaternion toRotation,
        float ratio
    )
    {
        fromRotation = NormalizeQuaternion(fromRotation);
        toRotation = NormalizeQuaternion(toRotation);

        Quaternion relativeRotation = NormalizeQuaternion(
            Quaternion.Inverse(fromRotation) * toRotation
        );

        // q and -q represent the same final physical orientation.
        // Force the relative quaternion to represent an angle <= 180 degrees,
        // then explicitly derive the complementary long route.
        if (relativeRotation.w < 0f)
        {
            relativeRotation = NegateQuaternion(relativeRotation);
        }

        float clampedW = Mathf.Clamp(relativeRotation.w, -1f, 1f);
        float halfAngleRadians = Mathf.Acos(clampedW);
        float sinHalfAngle = Mathf.Sin(halfAngleRadians);

        Quaternion shortArcCandidate;
        Quaternion longArcCandidate;

        if (Mathf.Abs(sinHalfAngle) < AxisEpsilon)
        {
            // The rotations are almost identical. The rotation axis is
            // undefined here, and there is no meaningful branch distinction.
            shortArcCandidate = Quaternion.Slerp(
                fromRotation,
                toRotation,
                ratio
            );

            longArcCandidate = shortArcCandidate;
        }
        else
        {
            Vector3 relativeAxis = new Vector3(
                relativeRotation.x,
                relativeRotation.y,
                relativeRotation.z
            ) / sinHalfAngle;

            relativeAxis.Normalize();

            float shortArcAngle =
                2f * halfAngleRadians * Mathf.Rad2Deg;

            // Both routes end at the same physical target orientation:
            // shortArcAngle and shortArcAngle - 360 degrees.
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

        if (!rotationContinuityInitialized)
        {
            previousTargetRotation = shortArcCandidate;
            rotationContinuityInitialized = true;

            usingLongArc = false;
            shortArcFrameChange = 0f;
            longArcFrameChange = 0f;

            return shortArcCandidate;
        }

        // Quaternion.Angle compares the complete 3D orientation, including
        // wrist roll. Comparing only transform.forward would miss roll.
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
            // Remain on the long branch unless the short branch becomes
            // clearly more continuous.
            if (shortArcFrameChange + hysteresis <
                longArcFrameChange)
            {
                usingLongArc = false;
            }
        }
        else
        {
            // Switch to the long branch only when it is clearly more
            // continuous than the short branch.
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
    /// Call this after teleporting, re-centering, replacing a tracked hand,
    /// or regaining tracking after a discontinuity.
    /// </summary>
    public void ResetRotationContinuity()
    {
        rotationContinuityInitialized = false;
        previousTargetRotation = Quaternion.identity;

        usingLongArc = false;
        shortArcFrameChange = 0f;
        longArcFrameChange = 0f;
    }

    private static Quaternion NormalizeQuaternion(Quaternion value)
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

    private static Quaternion NegateQuaternion(Quaternion value)
    {
        return new Quaternion(
            -value.x,
            -value.y,
            -value.z,
            -value.w
        );
    }
}
