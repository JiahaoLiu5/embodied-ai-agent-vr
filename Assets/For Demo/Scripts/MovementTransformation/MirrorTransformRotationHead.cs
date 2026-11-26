using UnityEngine;

/// <summary>
/// Mirrors the rotation of a source head in up to three dimensions
/// and writes the mirrored world rotation to the GameObject this script is attached to.
///
/// This script does not change the position of the target.
///
/// Three dimensions can be mirrored independently:
/// 1. Left / Right
/// 2. Up / Down
/// 3. Front / Back
///
/// The mirror directions are the three world space axes of the reference frame:
/// - Reference Frame.right
/// - Reference Frame.up
/// - Reference Frame.forward
///
/// If no reference frame is assigned:
/// 1. the parent of the source head is used
/// 2. if the source head has no parent, the world axes are used
/// </summary>
[DisallowMultipleComponent]
public class MirrorTransformRotationHead : MonoBehaviour
{
    [Header("Required Reference")]

    [Tooltip(
        "Head transform whose rotation is mirrored.")]
    [SerializeField]
    private Transform sourceHead;

    [Header("Rotation Reference Frame")]

    [Tooltip(
        "Reference frame that defines the left/right, up/down and front/back mirror directions." +
        "\n\nRecommended: the avatar root, the XR Origin, the chest or another stable node at the body centre." +
        "\n\nDo not use the source head itself or one of its children." +
        "\n\nIf empty, the parent of the source head is used; " +
        "if the source head has no parent, the world axes are used.")]
    [SerializeField]
    private Transform referenceFrame;

    [Header("Rotation Mirror Dimensions")]

    [Tooltip(
        "Whether to mirror the left/right part of the rotation." +
        "\n\nInverts the components of the direction vectors along Reference Frame.right.")]
    [SerializeField]
    private bool mirrorLeftRightRotation = true;

    [Tooltip(
        "Whether to mirror the up/down part of the rotation." +
        "\n\nInverts the components of the direction vectors along Reference Frame.up.")]
    [SerializeField]
    private bool mirrorUpDownRotation = false;

    [Tooltip(
        "Whether to mirror the front/back part of the rotation." +
        "\n\nInverts the components of the direction vectors along Reference Frame.forward.")]
    [SerializeField]
    private bool mirrorFrontBackRotation = false;

    [Header("Rotation Offset")]

    [Tooltip(
        "Local rotation offset added to the target after the rotation has been mirrored." +
        "\n\nUse it when the local axes of the head model and the target differ.")]
    [SerializeField]
    private Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Debug")]

    [Tooltip(
        "Show the three axes of the reference frame in the Scene view while the object is selected.")]
    [SerializeField]
    private bool drawReferenceAxesGizmo = true;

    [SerializeField]
    [Min(0.01f)]
    private float gizmoAxisLength = 0.25f;

    private const float Epsilon = 0.000001f;

    private void LateUpdate()
    {
        ApplyMirrorRotation();
    }

    /// <summary>
    /// Computes and applies the mirrored head rotation once.
    ///
    /// LateUpdate is used so that these have most likely finished for the current frame:
    /// - XR Head Tracking
    /// - Animator
    /// - IK
    /// - other tracking scripts
    /// </summary>
    [ContextMenu("Apply Mirror Rotation Now")]
    public void ApplyMirrorRotation()
    {
        if (sourceHead == null)
        {
            return;
        }

        // World space axes that define the mirror dimensions.
        GetReferenceAxes(
            out Vector3 rightAxis,
            out Vector3 upAxis,
            out Vector3 forwardAxis
        );

        // Start from the current world forward and up directions
        // of the source head.
        Vector3 mirroredForward = sourceHead.forward;
        Vector3 mirroredUp = sourceHead.up;

        // =========================================================
        // 1. Left/right part of the rotation
        // =========================================================
        //
        // Use Reference Frame.right as the mirror normal and invert
        // the left/right components of forward and up.
        if (mirrorLeftRightRotation)
        {
            mirroredForward = ReflectDirection(
                mirroredForward,
                rightAxis
            );

            mirroredUp = ReflectDirection(
                mirroredUp,
                rightAxis
            );
        }

        // =========================================================
        // 2. Up/down part of the rotation
        // =========================================================
        //
        // Use Reference Frame.up as the mirror normal and invert
        // the up/down components of forward and up.
        if (mirrorUpDownRotation)
        {
            mirroredForward = ReflectDirection(
                mirroredForward,
                upAxis
            );

            mirroredUp = ReflectDirection(
                mirroredUp,
                upAxis
            );
        }

        // =========================================================
        // 3. Front/back part of the rotation
        // =========================================================
        //
        // Use Reference Frame.forward as the mirror normal and invert
        // the front/back components of forward and up.
        if (mirrorFrontBackRotation)
        {
            mirroredForward = ReflectDirection(
                mirroredForward,
                forwardAxis
            );

            mirroredUp = ReflectDirection(
                mirroredUp,
                forwardAxis
            );
        }

        // Prevent errors in Quaternion.LookRotation for invalid directions.
        if (mirroredForward.sqrMagnitude < Epsilon ||
            mirroredUp.sqrMagnitude < Epsilon)
        {
            return;
        }

        mirroredForward.Normalize();
        mirroredUp.Normalize();

        // In theory a reflection keeps the two directions perpendicular.
        // Floating point errors are possible, so they are made
        // orthogonal again before the quaternion is rebuilt.
        Vector3.OrthoNormalize(
            ref mirroredForward,
            ref mirroredUp
        );

        if (Vector3.Cross(
                mirroredForward,
                mirroredUp
            ).sqrMagnitude < Epsilon)
        {
            return;
        }

        // Rebuild a valid world rotation from the mirrored forward and up.
        Quaternion mirroredRotation = Quaternion.LookRotation(
            mirroredForward,
            mirroredUp
        );

        // Add the local rotation correction.
        mirroredRotation *= Quaternion.Euler(
            rotationOffsetEuler
        );

        // Only the world rotation is changed.
        // This script neither reads nor writes transform.position.
        transform.rotation = mirroredRotation;
    }

    /// <summary>
    /// Returns the axes of the reference frame used for mirroring.
    ///
    /// Priority:
    /// 1. the assigned Reference Frame
    /// 2. the parent of the source head
    /// 3. the world axes
    /// </summary>
    private void GetReferenceAxes(
        out Vector3 rightAxis,
        out Vector3 upAxis,
        out Vector3 forwardAxis)
    {
        Transform activeReference = GetActiveReferenceFrame();

        if (activeReference != null)
        {
            rightAxis = activeReference.right.normalized;
            upAxis = activeReference.up.normalized;
            forwardAxis = activeReference.forward.normalized;
        }
        else
        {
            // The source head has no parent
            // and no reference frame is assigned:
            // use the world axes.
            rightAxis = Vector3.right;
            upAxis = Vector3.up;
            forwardAxis = Vector3.forward;
        }
    }

    /// <summary>
    /// Returns the reference frame in use.
    /// </summary>
    private Transform GetActiveReferenceFrame()
    {
        if (referenceFrame != null)
        {
            return referenceFrame;
        }

        if (sourceHead != null && sourceHead.parent != null)
        {
            return sourceHead.parent;
        }

        return null;
    }

    /// <summary>
    /// Reflects a direction in world space across a mirror plane.
    ///
    /// planeNormal is the normal of that plane.
    ///
    /// Formula:
    ///
    /// reflectedDirection =
    /// direction - 2 * dot(direction, planeNormal) * planeNormal
    /// </summary>
    private static Vector3 ReflectDirection(
        Vector3 direction,
        Vector3 planeNormal)
    {
        if (planeNormal.sqrMagnitude < Epsilon)
        {
            return direction;
        }

        planeNormal.Normalize();

        return direction -
               2f *
               Vector3.Dot(direction, planeNormal) *
               planeNormal;
    }

    /// <summary>
    /// Draws the three mirror axes in the Scene view.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawReferenceAxesGizmo)
        {
            return;
        }

        Transform activeReference = GetActiveReferenceFrame();

        Vector3 origin;
        Vector3 right;
        Vector3 up;
        Vector3 forward;

        if (activeReference != null)
        {
            origin = activeReference.position;
            right = activeReference.right.normalized;
            up = activeReference.up.normalized;
            forward = activeReference.forward.normalized;
        }
        else
        {
            origin = sourceHead != null
                ? sourceHead.position
                : transform.position;

            right = Vector3.right;
            up = Vector3.up;
            forward = Vector3.forward;
        }

        // Red: left/right axis
        Gizmos.color = Color.red;
        Gizmos.DrawRay(
            origin,
            right * gizmoAxisLength
        );

        // Green: up/down axis
        Gizmos.color = Color.green;
        Gizmos.DrawRay(
            origin,
            up * gizmoAxisLength
        );

        // Blue: front/back axis
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(
            origin,
            forward * gizmoAxisLength
        );
    }
}
