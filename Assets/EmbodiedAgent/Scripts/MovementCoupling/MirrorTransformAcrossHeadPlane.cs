using UnityEngine;

/// <summary>
/// Earlier variant of MirrorTransformAcrossHeadPlaneNew: mirrors the source transform across a plane
/// defined by the head and writes the result to the GameObject this script is attached to.
///
/// Default mirror plane:
/// - passes through Head.position
/// - contains Head.forward
/// - contains Head.up
/// - has Head.right as its normal
/// </summary>
[DisallowMultipleComponent]
public class MirrorTransformAcrossHeadPlane : MonoBehaviour
{
    public enum MirrorPlaneMode
    {
        /// <summary>
        /// The plane contains Head.forward and Head.up.
        /// Its normal is Head.right.
        /// It follows the pitch, yaw and roll of the head.
        /// </summary>
        HeadSagittalPlane,

        /// <summary>
        /// A plane that always stays vertical but is aligned with Head.forward.
        /// It does not tilt when the head rolls.
        /// </summary>
        WorldVerticalPlane
    }

    [Header("Required References")]

    [Tooltip("Head transform that defines the mirror plane.")]
    [SerializeField]
    private Transform head;

    [Tooltip("Transform to mirror, for example the left or right hand.")]
    [SerializeField]
    private Transform source;

    [Header("Mirror Plane")]

    [Tooltip("How the mirror plane is defined.")]
    [SerializeField]
    private MirrorPlaneMode planeMode =
        MirrorPlaneMode.HeadSagittalPlane;

    [Tooltip(
        "Optional point the plane passes through. If empty, Head.position is used. " +
        "Assign a separate transform if the plane should pass through the chest or the body centre.")]
    [SerializeField]
    private Transform planeOrigin;

    [Header("Rotation")]

    [Tooltip(
        "Whether to mirror the world rotation of the source as well. " +
        "Disable this to mirror only the position.")]
    [SerializeField]
    private bool mirrorRotation = false;

    [Tooltip(
        "Local Euler offset added after the rotation has been mirrored. " +
        "Use it when the local axes of the left and right hand models differ.")]
    [SerializeField]
    private Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Debug")]

    [Tooltip("Show the mirror plane in the Scene view while the object is selected.")]
    [SerializeField]
    private bool drawPlaneGizmo = true;

    [SerializeField]
    [Min(0.01f)]
    private float gizmoPlaneSize = 0.5f;

    private const float Epsilon = 0.000001f;

    private void LateUpdate()
    {
        ApplyMirror();
    }

    /// <summary>
    /// Computes and applies the mirrored pose once.
    /// LateUpdate is used so that animation, IK and hand tracking have most likely finished.
    /// </summary>
    [ContextMenu("Apply Mirror Now")]
    public void ApplyMirror()
    {
        if (head == null || source == null)
        {
            return;
        }

        Vector3 pointOnPlane = GetPointOnPlane();
        Vector3 planeNormal = GetPlaneNormal();

        if (planeNormal.sqrMagnitude < Epsilon)
        {
            return;
        }

        planeNormal.Normalize();

        // 1. Mirror the world position
        Vector3 mirroredPosition = ReflectPoint(
            source.position,
            pointOnPlane,
            planeNormal
        );

        if (!mirrorRotation)
        {
            transform.position = mirroredPosition;
            return;
        }

        // 2. Mirror the forward and up directions of the source
        Vector3 mirroredForward = ReflectDirection(
            source.forward,
            planeNormal
        );

        Vector3 mirroredUp = ReflectDirection(
            source.up,
            planeNormal
        );

        // A reflection keeps forward and up perpendicular.
        // The check avoids LookRotation errors for degenerate transforms.
        if (mirroredForward.sqrMagnitude < Epsilon ||
            mirroredUp.sqrMagnitude < Epsilon ||
            Vector3.Cross(mirroredForward, mirroredUp).sqrMagnitude < Epsilon)
        {
            transform.position = mirroredPosition;
            return;
        }

        Quaternion mirroredRotation = Quaternion.LookRotation(
            mirroredForward,
            mirroredUp
        );

        // Optional local rotation correction
        mirroredRotation *= Quaternion.Euler(rotationOffsetEuler);

        transform.SetPositionAndRotation(
            mirroredPosition,
            mirroredRotation
        );
    }

    /// <summary>
    /// Returns a point on the mirror plane.
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
    /// Returns the normal of the mirror plane in world space.
    /// </summary>
    private Vector3 GetPlaneNormal()
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
                // to get its direction in the horizontal plane.
                Vector3 horizontalForward =
                    Vector3.ProjectOnPlane(head.forward, worldUp);

                // The projection is close to zero when the head looks straight up or down.
                if (horizontalForward.sqrMagnitude < Epsilon)
                {
                    return head.right.normalized;
                }

                horizontalForward.Normalize();

                // The plane is spanned by worldUp and horizontalForward.
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

    /// <summary>
    /// Mirrors a point in world space across a plane.
    /// </summary>
    private static Vector3 ReflectPoint(
        Vector3 point,
        Vector3 pointOnPlane,
        Vector3 planeNormal)
    {
        Vector3 offsetFromPlane = point - pointOnPlane;

        float signedDistance = Vector3.Dot(
            offsetFromPlane,
            planeNormal
        );

        return point - 2f * signedDistance * planeNormal;
    }

    /// <summary>
    /// Mirrors a direction across a plane.
    /// A direction has no position, so the point on the plane is not needed.
    /// </summary>
    private static Vector3 ReflectDirection(
        Vector3 direction,
        Vector3 planeNormal)
    {
        return direction -
               2f * Vector3.Dot(direction, planeNormal) * planeNormal;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPlaneGizmo || head == null)
        {
            return;
        }

        Vector3 center = GetPointOnPlane();
        Vector3 normal = GetPlaneNormal();

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
                planeForward = head.forward;
            }

            planeForward.Normalize();
        }

        float halfSize = gizmoPlaneSize * 0.5f;

        Vector3 topLeft =
            center + planeUp * halfSize - planeForward * halfSize;

        Vector3 topRight =
            center + planeUp * halfSize + planeForward * halfSize;

        Vector3 bottomLeft =
            center - planeUp * halfSize - planeForward * halfSize;

        Vector3 bottomRight =
            center - planeUp * halfSize + planeForward * halfSize;

        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
        Gizmos.DrawLine(bottomLeft, topLeft);

        // Draw the normal
        Gizmos.DrawRay(
            center,
            normal * gizmoPlaneSize * 0.5f
        );
    }
}
