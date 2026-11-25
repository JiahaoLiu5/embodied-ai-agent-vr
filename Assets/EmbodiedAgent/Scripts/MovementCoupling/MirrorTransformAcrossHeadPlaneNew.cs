using UnityEngine;

/// <summary>
/// Mirrors the position and the rotation of a source transform
/// and writes the result to the GameObject this script is attached to.
///
/// Position:
/// - always uses the head as the reference
/// - the plane passes through Head.position or Plane Origin
/// - the plane is either HeadSagittalPlane or WorldVerticalPlane
///
/// Rotation:
/// - uses a separate Rotation Reference Frame
/// - left/right, up/down and front/back can be mirrored independently
///
/// The references for position and rotation are independent of each other.
/// </summary>
[DisallowMultipleComponent]
public class MirrorTransformAcrossHeadPlaneNew : MonoBehaviour
{
    public enum MirrorPlaneMode
    {
        /// <summary>
        /// The position mirror plane contains Head.forward and Head.up.
        /// Its normal is Head.right.
        ///
        /// The plane follows the head completely:
        /// - yaw
        /// - pitch
        /// - roll
        /// </summary>
        HeadSagittalPlane,

        /// <summary>
        /// The position mirror plane always stays vertical,
        /// but its forward direction follows the horizontal heading of the head.
        ///
        /// The plane does not tilt when the head rolls.
        /// </summary>
        WorldVerticalPlane
    }

    [Header("Required References")]

    [Tooltip(
        "Head transform that defines the position mirror plane.")]
    [SerializeField]
    private Transform head;

    [Tooltip(
        "Transform to mirror, for example the left or right hand.")]
    [SerializeField]
    private Transform source;

    // =============================================================
    // Position Mirror
    // =============================================================

    [Header("Position Mirror Plane")]

    [Tooltip(
        "How the position mirror plane is defined.")]
    [SerializeField]
    private MirrorPlaneMode planeMode =
        MirrorPlaneMode.HeadSagittalPlane;

    [Tooltip(
        "Optional point the position mirror plane passes through." +
        "\n\nIf empty, Head.position is used." +
        "\n\nAssign a separate transform if the plane should pass " +
        "through the chest or the body centre.")]
    [SerializeField]
    private Transform planeOrigin;

    // =============================================================
    // Rotation Mirror
    // =============================================================

    [Header("Rotation Mirror")]

    [Tooltip(
        "Whether to mirror the world rotation of the source as well." +
        "\n\nIf disabled, the script mirrors only the position.")]
    [SerializeField]
    private bool mirrorRotation = false;

    [Tooltip(
        "Defines the left/right, up/down and front/back axes for mirroring the rotation." +
        "\n\nIt is independent of the position mirror plane." +
        "\n\nRecommended:" +
        "\n- Avatar Root" +
        "\n- XR Origin" +
        "\n- Chest" +
        "\n- a node at the body centre" +
        "\n\nIf empty, the head is used, as in the previous version of this script.")]
    [SerializeField]
    private Transform rotationReferenceFrame;

    [Header("Rotation Mirror Dimensions")]

    [Tooltip(
        "Mirror the left/right part of the rotation." +
        "\n\nUses Rotation Reference Frame.right as the normal of the mirror plane.")]
    [SerializeField]
    private bool mirrorLeftRightRotation = true;

    [Tooltip(
        "Mirror the up/down part of the rotation." +
        "\n\nUses Rotation Reference Frame.up as the normal of the mirror plane.")]
    [SerializeField]
    private bool mirrorUpDownRotation = false;

    [Tooltip(
        "Mirror the front/back part of the rotation." +
        "\n\nUses Rotation Reference Frame.forward as the normal of the mirror plane.")]
    [SerializeField]
    private bool mirrorFrontBackRotation = false;

    [Tooltip(
        "Local Euler offset added after the rotation has been mirrored." +
        "\n\nUse it when the local axes of the source and the target " +
        "are defined differently.")]
    [SerializeField]
    private Vector3 rotationOffsetEuler = Vector3.zero;

    // =============================================================
    // Debug
    // =============================================================

    [Header("Position Plane Debug")]

    [Tooltip(
        "Show the position mirror plane in the Scene view while the object is selected.")]
    [SerializeField]
    private bool drawPlaneGizmo = true;

    [SerializeField]
    [Min(0.01f)]
    private float gizmoPlaneSize = 0.5f;

    [Header("Rotation Reference Debug")]

    [Tooltip(
        "Show the three axes of the rotation reference frame while the object is selected.")]
    [SerializeField]
    private bool drawRotationReferenceAxesGizmo = true;

    [SerializeField]
    [Min(0.01f)]
    private float rotationAxesGizmoLength = 0.25f;

    private const float Epsilon = 0.000001f;

    private void LateUpdate()
    {
        ApplyMirror();
    }

    /// <summary>
    /// Computes and applies the mirrored position and rotation once.
    ///
    /// LateUpdate is used so that these have most likely finished for the current frame:
    /// - Animator
    /// - IK
    /// - XR Tracking
    /// - Hand Tracking
    /// </summary>
    [ContextMenu("Apply Mirror Now")]
    public void ApplyMirror()
    {
        if (head == null || source == null)
        {
            return;
        }

        // =========================================================
        // 1. Position
        // =========================================================
        //
        // The position uses only the mirror plane defined by the head.
        // This part is unchanged from the previous version.

        Vector3 pointOnPlane = GetPointOnPlane();
        Vector3 positionPlaneNormal = GetPositionPlaneNormal();

        if (positionPlaneNormal.sqrMagnitude < Epsilon)
        {
            return;
        }

        positionPlaneNormal.Normalize();

        Vector3 mirroredPosition = ReflectPoint(
            source.position,
            pointOnPlane,
            positionPlaneNormal
        );

        // If the rotation is not mirrored,
        // set only the position.
        if (!mirrorRotation)
        {
            transform.position = mirroredPosition;
            return;
        }

        // =========================================================
        // 2. Reference axes for the rotation
        // =========================================================
        //
        // The rotation does not use the axes of the position mirror plane.
        // It uses the separate rotation reference frame.

        GetRotationReferenceAxes(
            out Vector3 rightAxis,
            out Vector3 upAxis,
            out Vector3 forwardAxis
        );

        // Start from the current world forward and up
        // of the source.
        Vector3 mirroredForward = source.forward;
        Vector3 mirroredUp = source.up;

        // =========================================================
        // 3. Left/right part of the rotation
        // =========================================================
        //
        // Invert the components of forward and up
        // along Reference Frame.right.

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
        // 4. Up/down part of the rotation
        // =========================================================
        //
        // Invert the components of forward and up
        // along Reference Frame.up.

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
        // 5. Front/back part of the rotation
        // =========================================================
        //
        // Invert the components of forward and up
        // along Reference Frame.forward.

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

        // =========================================================
        // 6. Safety checks
        // =========================================================

        if (mirroredForward.sqrMagnitude < Epsilon ||
            mirroredUp.sqrMagnitude < Epsilon)
        {
            transform.position = mirroredPosition;
            return;
        }

        mirroredForward.Normalize();
        mirroredUp.Normalize();

        // Remove small floating point errors
        // so that forward and up are orthogonal.
        Vector3.OrthoNormalize(
            ref mirroredForward,
            ref mirroredUp
        );

        if (Vector3.Cross(
                mirroredForward,
                mirroredUp
            ).sqrMagnitude < Epsilon)
        {
            transform.position = mirroredPosition;
            return;
        }

        // =========================================================
        // 7. Rebuild the mirrored quaternion
        // =========================================================

        Quaternion mirroredRotation = Quaternion.LookRotation(
            mirroredForward,
            mirroredUp
        );

        // Add the local rotation correction.
        mirroredRotation *= Quaternion.Euler(
            rotationOffsetEuler
        );

        // Set the world position and the world rotation.
        transform.SetPositionAndRotation(
            mirroredPosition,
            mirroredRotation
        );
    }

    // =============================================================
    // Position Mirror Helpers
    // =============================================================

    /// <summary>
    /// Returns a point on the position mirror plane.
    /// </summary>
    private Vector3 GetPointOnPlane()
    {
        if (planeOrigin != null)
        {
            return planeOrigin.position;
        }

        return head.position;
    }

    /// <summary>
    /// Returns the normal of the position mirror plane in world space.
    ///
    /// The normal always uses the head as the reference
    /// and does not depend on the rotation reference frame.
    /// </summary>
    private Vector3 GetPositionPlaneNormal()
    {
        switch (planeMode)
        {
            case MirrorPlaneMode.HeadSagittalPlane:
            {
                // The plane is spanned by Head.forward and Head.up,
                // so its normal is Head.right.
                return head.right.normalized;
            }

            case MirrorPlaneMode.WorldVerticalPlane:
            {
                Vector3 worldUp = Vector3.up;

                // Remove the vertical part of Head.forward
                // to get the heading of the head in the horizontal plane.
                Vector3 horizontalForward =
                    Vector3.ProjectOnPlane(
                        head.forward,
                        worldUp
                    );

                // The projection can be very small when the head
                // looks almost straight up or down.
                if (horizontalForward.sqrMagnitude < Epsilon)
                {
                    Vector3 horizontalRight =
                        Vector3.ProjectOnPlane(
                            head.right,
                            worldUp
                        );

                    if (horizontalRight.sqrMagnitude < Epsilon)
                    {
                        return Vector3.right;
                    }

                    return horizontalRight.normalized;
                }

                horizontalForward.Normalize();

                // World up and horizontal forward span the vertical plane.
                // Cross(up, forward) is its normal, pointing right.
                return Vector3.Cross(
                    worldUp,
                    horizontalForward
                ).normalized;
            }

            default:
                return head.right.normalized;
        }
    }

    // =============================================================
    // Rotation Mirror Helpers
    // =============================================================

    /// <summary>
    /// Returns the rotation reference frame in use.
    ///
    /// Priority:
    /// 1. the assigned Rotation Reference Frame
    /// 2. the head
    ///
    /// The head is the fallback for compatibility with the previous version.
    /// </summary>
    private Transform GetActiveRotationReferenceFrame()
    {
        if (rotationReferenceFrame != null)
        {
            return rotationReferenceFrame;
        }

        return head;
    }

    /// <summary>
    /// Returns the three world space axes used for mirroring the rotation.
    ///
    /// These axes are independent of the position mirror plane.
    /// </summary>
    private void GetRotationReferenceAxes(
        out Vector3 rightAxis,
        out Vector3 upAxis,
        out Vector3 forwardAxis)
    {
        Transform activeReference =
            GetActiveRotationReferenceFrame();

        if (activeReference != null)
        {
            rightAxis =
                activeReference.right.normalized;

            upAxis =
                activeReference.up.normalized;

            forwardAxis =
                activeReference.forward.normalized;
        }
        else
        {
            // The head is a required reference,
            // so this branch is normally not reached.
            // The world axes are a safe fallback.
            rightAxis = Vector3.right;
            upAxis = Vector3.up;
            forwardAxis = Vector3.forward;
        }
    }

    // =============================================================
    // Reflection Math
    // =============================================================

    /// <summary>
    /// Mirrors a point in world space across a plane.
    ///
    /// mirroredPoint =
    /// point - 2 * signedDistance * planeNormal
    /// </summary>
    private static Vector3 ReflectPoint(
        Vector3 point,
        Vector3 pointOnPlane,
        Vector3 planeNormal)
    {
        Vector3 offsetFromPlane =
            point - pointOnPlane;

        float signedDistance = Vector3.Dot(
            offsetFromPlane,
            planeNormal
        );

        return point -
               2f *
               signedDistance *
               planeNormal;
    }

    /// <summary>
    /// Mirrors a direction in world space across a plane.
    ///
    /// planeNormal is the normal of that plane.
    ///
    /// reflectedDirection =
    /// direction - 2 * dot(direction, normal) * normal
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
               Vector3.Dot(
                   direction,
                   planeNormal
               ) *
               planeNormal;
    }

    // =============================================================
    // Gizmos
    // =============================================================

    private void OnDrawGizmosSelected()
    {
        DrawPositionPlaneGizmo();
        DrawRotationReferenceAxesGizmo();
    }

    /// <summary>
    /// Draws the position mirror plane.
    /// </summary>
    private void DrawPositionPlaneGizmo()
    {
        if (!drawPlaneGizmo || head == null)
        {
            return;
        }

        Vector3 center = GetPointOnPlane();
        Vector3 normal = GetPositionPlaneNormal();

        if (normal.sqrMagnitude < Epsilon)
        {
            return;
        }

        normal.Normalize();

        Vector3 planeUp;
        Vector3 planeForward;

        if (planeMode == MirrorPlaneMode.HeadSagittalPlane)
        {
            planeUp = head.up.normalized;
            planeForward = head.forward.normalized;
        }
        else
        {
            planeUp = Vector3.up;

            planeForward = Vector3.ProjectOnPlane(
                head.forward,
                Vector3.up
            );

            if (planeForward.sqrMagnitude < Epsilon)
            {
                planeForward = Vector3.Cross(
                    normal,
                    Vector3.up
                );
            }

            if (planeForward.sqrMagnitude < Epsilon)
            {
                planeForward = Vector3.forward;
            }

            planeForward.Normalize();
        }

        float halfSize =
            gizmoPlaneSize * 0.5f;

        Vector3 topLeft =
            center +
            planeUp * halfSize -
            planeForward * halfSize;

        Vector3 topRight =
            center +
            planeUp * halfSize +
            planeForward * halfSize;

        Vector3 bottomLeft =
            center -
            planeUp * halfSize -
            planeForward * halfSize;

        Vector3 bottomRight =
            center -
            planeUp * halfSize +
            planeForward * halfSize;

        Gizmos.color = Color.yellow;

        Gizmos.DrawLine(
            topLeft,
            topRight
        );

        Gizmos.DrawLine(
            topRight,
            bottomRight
        );

        Gizmos.DrawLine(
            bottomRight,
            bottomLeft
        );

        Gizmos.DrawLine(
            bottomLeft,
            topLeft
        );

        // Draw the normal of the position mirror plane.
        Gizmos.color = Color.magenta;

        Gizmos.DrawRay(
            center,
            normal * gizmoPlaneSize * 0.5f
        );
    }

    /// <summary>
    /// Draws the three axes of the rotation reference frame.
    ///
    /// Red: right
    /// Green: up
    /// Blue: forward
    /// </summary>
    private void DrawRotationReferenceAxesGizmo()
    {
        if (!drawRotationReferenceAxesGizmo)
        {
            return;
        }

        Transform activeReference =
            GetActiveRotationReferenceFrame();

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
            origin = head != null
                ? head.position
                : transform.position;

            right = Vector3.right;
            up = Vector3.up;
            forward = Vector3.forward;
        }

        // Red: left/right
        Gizmos.color = Color.red;

        Gizmos.DrawRay(
            origin,
            right * rotationAxesGizmoLength
        );

        // Green: up/down
        Gizmos.color = Color.green;

        Gizmos.DrawRay(
            origin,
            up * rotationAxesGizmoLength
        );

        // Blue: front/back
        Gizmos.color = Color.blue;

        Gizmos.DrawRay(
            origin,
            forward * rotationAxesGizmoLength
        );
    }
}
