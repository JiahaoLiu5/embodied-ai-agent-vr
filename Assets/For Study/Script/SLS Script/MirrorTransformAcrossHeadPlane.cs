using UnityEngine;

/// <summary>
/// 将 Source Transform 相对于 Head 所定义的镜像平面进行镜像，
/// 并把镜像结果赋值给当前挂载此脚本的 GameObject。
///
/// 默认镜像平面：
/// - 经过 Head.position
/// - 包含 Head.forward
/// - 包含 Head.up
/// - 法线为 Head.right
/// </summary>
[DisallowMultipleComponent]
public class MirrorTransformAcrossHeadPlane : MonoBehaviour
{
    public enum MirrorPlaneMode
    {
        /// <summary>
        /// 平面包含 Head.forward 和 Head.up。
        /// 平面法线为 Head.right。
        /// 平面会跟随 Head 的俯仰、偏航和侧倾。
        /// </summary>
        HeadSagittalPlane,

        /// <summary>
        /// 一个始终保持竖直、但方向与 Head.forward 对齐的平面。
        /// 它不会随着 Head 的侧倾而倾斜。
        /// </summary>
        WorldVerticalPlane
    }

    [Header("Required References")]

    [Tooltip("用于定义镜像平面的 Head Transform。")]
    [SerializeField]
    private Transform head;

    [Tooltip("需要被镜像的原始 Transform，例如 Left Hand 或 Right Hand。")]
    [SerializeField]
    private Transform source;

    [Header("Mirror Plane")]

    [Tooltip("镜像平面的定义方式。")]
    [SerializeField]
    private MirrorPlaneMode planeMode =
        MirrorPlaneMode.HeadSagittalPlane;

    [Tooltip(
        "可选的平面中心点。若为空，则使用 Head.position。" +
        "如果希望镜像平面经过胸口或身体中心，可以指定一个单独的 Transform。")]
    [SerializeField]
    private Transform planeOrigin;

    [Header("Rotation")]

    [Tooltip(
        "是否同时镜像 Source 的世界旋转。" +
        "如果只需要镜像位置，可关闭。")]
    [SerializeField]
    private bool mirrorRotation = false;

    [Tooltip(
        "镜像旋转完成后附加的局部欧拉角偏移。" +
        "当左右手模型的局部坐标轴不完全一致时可以使用。")]
    [SerializeField]
    private Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Debug")]

    [Tooltip("选中物体时，在 Scene 窗口显示镜像平面。")]
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
    /// 立即进行一次镜像计算。
    /// 使用 LateUpdate 是为了尽量确保动画、IK 和手部追踪已经更新完成。
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

        // 1. 镜像世界坐标位置
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

        // 2. 镜像 Source 的 forward 与 up 方向
        Vector3 mirroredForward = ReflectDirection(
            source.forward,
            planeNormal
        );

        Vector3 mirroredUp = ReflectDirection(
            source.up,
            planeNormal
        );

        // 正常情况下反射操作会保持 forward 和 up 相互垂直。
        // 这里增加检查，避免异常 Transform 导致 LookRotation 报错。
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

        // 可选的局部旋转修正
        mirroredRotation *= Quaternion.Euler(rotationOffsetEuler);

        transform.SetPositionAndRotation(
            mirroredPosition,
            mirroredRotation
        );
    }

    /// <summary>
    /// 获得镜像平面上的一个点。
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
    /// 获得镜像平面的世界坐标法线。
    /// </summary>
    private Vector3 GetPlaneNormal()
    {
        switch (planeMode)
        {
            case MirrorPlaneMode.HeadSagittalPlane:
            {
                // 平面由 Head.forward 和 Head.up 张成，
                // 所以法线是 Head.right。
                return head.right.normalized;
            }

            case MirrorPlaneMode.WorldVerticalPlane:
            {
                Vector3 worldUp = Vector3.up;

                // 去掉 Head.forward 的竖直分量，
                // 得到其在水平面上的方向。
                Vector3 horizontalForward =
                    Vector3.ProjectOnPlane(head.forward, worldUp);

                // Head 接近垂直向上或向下时，水平投影会接近零。
                if (horizontalForward.sqrMagnitude < Epsilon)
                {
                    return head.right.normalized;
                }

                horizontalForward.Normalize();

                // 该平面由 worldUp 和 horizontalForward 张成。
                // Cross(up, forward) 得到平面的右侧法线。
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
    /// 将世界坐标中的一个点相对于指定平面进行镜像。
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
    /// 将一个方向向量相对于指定平面镜像。
    /// 方向向量没有位置，因此不需要减去平面中心点。
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

        // 绘制法线方向
        Gizmos.DrawRay(
            center,
            normal * gizmoPlaneSize * 0.5f
        );
    }
}