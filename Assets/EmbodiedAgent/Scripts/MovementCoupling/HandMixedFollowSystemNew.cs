using UnityEngine;

/// <summary>
/// Earlier variant: mixes the hand rotations by adding up per-frame rotation increments.
/// HandMixedFollowSystemFinal is the version used by the Modulation prefab.
/// </summary>
public class HandMixedFollowSystemNew : MonoBehaviour
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

    [Header("Continuous Rotation")]
    [Tooltip("Prevents the mixed hand rotation from flipping when the relative hand rotation crosses 180 degrees.")]
    public bool useContinuousRotation = true;

    [Tooltip("Optional protection against tracking glitches. 0 disables the limit.")]
    [Min(0f)]
    public float maxInputAngularSpeed = 0f;

    private bool rotationStateInitialized;
    private Quaternion previousMyHandRotation;
    private Quaternion previousPartnerHandRotation;
    private Quaternion continuousMixedRotation;

    private const float Epsilon = 0.000001f;

    private void OnEnable()
    {
        rotationStateInitialized = false;
    }

    private void Update()
    {
        // Check references before accessing them.
        if (myHead == null || myHand == null ||
            partnerHead == null || partnerHand == null ||
            modulatedHead == null)
        {
            return;
        }

        float ratio = Mathf.Clamp01(followRatio);

        // -----------------------------
        // Position mixing: preserve the original logic.
        // -----------------------------
        Vector3 myHeadHand = myHand.localPosition - myHead.localPosition;

        Vector3 modHeadForwardPlane = Vector3.ProjectOnPlane(
            modulatedHead.forward,
            Vector3.up
        );

        Vector3 partnerHeadForwardPlane = Vector3.ProjectOnPlane(
            partnerHead.forward,
            Vector3.up
        );

        Quaternion headAlignmentRotation = Quaternion.identity;

        // Avoid an invalid FromToRotation when a head is looking almost
        // perfectly upward or downward and its horizontal forward vector
        // becomes nearly zero.
        if (modHeadForwardPlane.sqrMagnitude > Epsilon &&
            partnerHeadForwardPlane.sqrMagnitude > Epsilon)
        {
            headAlignmentRotation = Quaternion.FromToRotation(
                partnerHeadForwardPlane.normalized,
                modHeadForwardPlane.normalized
            );
        }

        Vector3 partnerHeadHand =
            partnerHand.localPosition - partnerHead.localPosition;

        Vector3 transformedPartnerHeadHand =
            headAlignmentRotation * partnerHeadHand;

        Vector3 mixedHeadHand = Vector3.Lerp(
            myHeadHand,
            transformedPartnerHeadHand,
            ratio
        );

        Vector3 targetPosition =
            mixedHeadHand + modulatedHead.localPosition;

        // -----------------------------
        // Rotation mixing.
        // -----------------------------
        Quaternion targetRotation;

        if (useContinuousRotation)
        {
            targetRotation = CalculateContinuousMixedRotation(ratio);
        }
        else
        {
            // Original behavior. This can switch interpolation direction
            // when the relative rotation crosses 180 degrees.
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
    /// Mixes frame-to-frame rotation increments instead of repeatedly
    /// interpolating the two absolute rotations.
    ///
    /// A normal Quaternion.Slerp between two absolute poses always chooses
    /// the shortest arc. When the relative pose crosses 180 degrees, that
    /// shortest arc changes sides and the result can flip. Per-frame deltas
    /// are normally very small, so their path is unambiguous and can be
    /// accumulated continuously.
    /// </summary>
    private Quaternion CalculateContinuousMixedRotation(float ratio)
    {
        Quaternion currentMyRotation =
            NormalizeQuaternion(myHand.localRotation);

        Quaternion currentPartnerRotation =
            NormalizeQuaternion(partnerHand.localRotation);

        if (!rotationStateInitialized)
        {
            previousMyHandRotation = currentMyRotation;
            previousPartnerHandRotation = currentPartnerRotation;

            // At initialization there is no motion history, so use the
            // ordinary blended pose once. All subsequent frames use
            // continuous incremental rotation.
            continuousMixedRotation = Quaternion.Slerp(
                currentMyRotation,
                currentPartnerRotation,
                ratio
            );

            continuousMixedRotation =
                NormalizeQuaternion(continuousMixedRotation);

            rotationStateInitialized = true;
            return continuousMixedRotation;
        }

        // q and -q describe exactly the same physical orientation. Match
        // each new quaternion to the hemisphere of its previous value so an
        // arbitrary quaternion sign change is not interpreted as motion.
        currentMyRotation = MatchQuaternionHemisphere(
            currentMyRotation,
            previousMyHandRotation
        );

        currentPartnerRotation = MatchQuaternionHemisphere(
            currentPartnerRotation,
            previousPartnerHandRotation
        );

        // Parent/local-space rotation increments from the previous frame to
        // the current frame.
        Quaternion myRotationDelta =
            currentMyRotation * Quaternion.Inverse(previousMyHandRotation);

        Quaternion partnerRotationDelta =
            currentPartnerRotation * Quaternion.Inverse(previousPartnerHandRotation);

        myRotationDelta = MakeShortestFrameDelta(myRotationDelta);
        partnerRotationDelta = MakeShortestFrameDelta(partnerRotationDelta);

        // Optional tracking-glitch protection. This does not participate in
        // the 180-degree fix unless a non-zero speed is supplied.
        if (maxInputAngularSpeed > 0f && Time.deltaTime > 0f)
        {
            float maximumStep = maxInputAngularSpeed * Time.deltaTime;
            myRotationDelta = ClampRotationDelta(myRotationDelta, maximumStep);
            partnerRotationDelta = ClampRotationDelta(
                partnerRotationDelta,
                maximumStep
            );
        }

        // Because these are small frame deltas rather than a possibly
        // greater-than-180-degree absolute difference, this Slerp does not
        // change branches when the hands pass the 180-degree boundary.
        Quaternion mixedRotationDelta = Quaternion.Slerp(
            myRotationDelta,
            partnerRotationDelta,
            ratio
        );

        continuousMixedRotation =
            mixedRotationDelta * continuousMixedRotation;

        continuousMixedRotation =
            NormalizeQuaternion(continuousMixedRotation);

        previousMyHandRotation = currentMyRotation;
        previousPartnerHandRotation = currentPartnerRotation;

        return continuousMixedRotation;
    }

    /// <summary>
    /// Call this after manually teleporting/reorienting either hand, or
    /// after replacing one of the tracked hand transforms at runtime.
    /// </summary>
    public void ResetContinuousRotationState()
    {
        rotationStateInitialized = false;
    }

    private static Quaternion MatchQuaternionHemisphere(
        Quaternion value,
        Quaternion reference
    )
    {
        if (Quaternion.Dot(value, reference) < 0f)
        {
            return NegateQuaternion(value);
        }

        return value;
    }

    private static Quaternion MakeShortestFrameDelta(Quaternion delta)
    {
        delta = NormalizeQuaternion(delta);

        // A real frame-to-frame hand movement should normally be below
        // 180 degrees. This also neutralizes a remaining q/-q sign issue.
        if (delta.w < 0f)
        {
            delta = NegateQuaternion(delta);
        }

        return delta;
    }

    private static Quaternion ClampRotationDelta(
        Quaternion delta,
        float maximumDegrees
    )
    {
        if (maximumDegrees <= 0f)
        {
            return Quaternion.identity;
        }

        float angle = Quaternion.Angle(Quaternion.identity, delta);

        if (angle <= maximumDegrees || angle < Epsilon)
        {
            return delta;
        }

        return Quaternion.Slerp(
            Quaternion.identity,
            delta,
            maximumDegrees / angle
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

    private static Quaternion NormalizeQuaternion(Quaternion value)
    {
        float magnitude = Mathf.Sqrt(
            value.x * value.x +
            value.y * value.y +
            value.z * value.z +
            value.w * value.w
        );

        if (magnitude < Epsilon)
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
}
