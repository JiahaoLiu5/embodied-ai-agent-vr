using UnityEngine;

/// <summary>
/// 将 Source Transform 的位置与旋转进行镜像，
/// 并将结果赋值给当前挂载此脚本的 GameObject。
///
/// Position 镜像：
/// - 始终以 Head 为方向基准
/// - 平面经过 Head.position 或 Plane Origin
/// - 可以选择 HeadSagittalPlane 或 WorldVerticalPlane
///
/// Rotation 镜像：
/// - 使用独立的 Rotation Reference Frame
/// - 可以分别选择左右、上下、前后三个镜像维度
///
/// Position 和 Rotation 的镜像基准彼此独立。
/// </summary>
[DisallowMultipleComponent]
public class MirrorTransformAcrossHeadPlaneNew : MonoBehaviour
{
    public enum MirrorPlaneMode
    {
        /// <summary>
        /// Position 镜像平面包含 Head.forward 和 Head.up。
        /// 平面法线为 Head.right。
        ///
        /// 平面会完整跟随 Head 的：
        /// - 左右转动
        /// - 上下俯仰
        /// - 左右侧倾
        /// </summary>
        HeadSagittalPlane,

        /// <summary>
        /// Position 镜像平面始终保持竖直，
        /// 但其前方向会跟随 Head 的水平朝向。
        ///
        /// Head 侧倾时，镜像平面不会侧倾。
        /// </summary>
        WorldVerticalPlane
    }

    [Header("Required References")]

    [Tooltip(
        "用于定义 Position 镜像平面的 Head Transform。")]
    [SerializeField]
    private Transform head;

    [Tooltip(
        "需要被镜像的原始 Transform，例如 Left Hand 或 Right Hand。")]
    [SerializeField]
    private Transform source;

    // =============================================================
    // Position Mirror
    // =============================================================

    [Header("Position Mirror Plane")]

    [Tooltip(
        "Position 镜像平面的定义方式。")]
    [SerializeField]
    private MirrorPlaneMode planeMode =
        MirrorPlaneMode.HeadSagittalPlane;

    [Tooltip(
        "可选的 Position 镜像平面中心点。" +
        "\n\n如果为空，则使用 Head.position。" +
        "\n\n如果希望镜像平面经过胸口或身体中心，" +
        "可以指定一个单独的 Transform。")]
    [SerializeField]
    private Transform planeOrigin;

    // =============================================================
    // Rotation Mirror
    // =============================================================

    [Header("Rotation Mirror")]

    [Tooltip(
        "是否同时镜像 Source 的世界旋转。" +
        "\n\n关闭后，脚本只镜像 Position。")]
    [SerializeField]
    private bool mirrorRotation = false;

    [Tooltip(
        "用于定义旋转镜像的左右、上下、前后三个参考轴。" +
        "\n\nRotation Reference Frame 与 Position 镜像平面相互独立。" +
        "\n\n推荐设置为：" +
        "\n- Avatar Root" +
        "\n- XR Origin" +
        "\n- Chest" +
        "\n- 身体中心节点" +
        "\n\n如果为空，则自动使用 Head，保持与上一版脚本兼容。")]
    [SerializeField]
    private Transform rotationReferenceFrame;

    [Header("Rotation Mirror Dimensions")]

    [Tooltip(
        "镜像旋转在左右方向上的分量。" +
        "\n\n使用 Rotation Reference Frame.right 作为镜像平面法线。")]
    [SerializeField]
    private bool mirrorLeftRightRotation = true;

    [Tooltip(
        "镜像旋转在上下方向上的分量。" +
        "\n\n使用 Rotation Reference Frame.up 作为镜像平面法线。")]
    [SerializeField]
    private bool mirrorUpDownRotation = false;

    [Tooltip(
        "镜像旋转在前后方向上的分量。" +
        "\n\n使用 Rotation Reference Frame.forward 作为镜像平面法线。")]
    [SerializeField]
    private bool mirrorFrontBackRotation = false;

    [Tooltip(
        "镜像旋转完成后附加的局部欧拉角偏移。" +
        "\n\n当 Source 和目标对象的局部轴定义不同时，" +
        "可以使用此参数进行修正。")]
    [SerializeField]
    private Vector3 rotationOffsetEuler = Vector3.zero;

    // =============================================================
    // Debug
    // =============================================================

    [Header("Position Plane Debug")]

    [Tooltip(
        "选中物体时，是否在 Scene 窗口显示 Position 镜像平面。")]
    [SerializeField]
    private bool drawPlaneGizmo = true;

    [SerializeField]
    [Min(0.01f)]
    private float gizmoPlaneSize = 0.5f;

    [Header("Rotation Reference Debug")]

    [Tooltip(
        "选中物体时，是否显示 Rotation Reference Frame 的三个坐标轴。")]
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
    /// 立即进行一次 Position 和 Rotation 镜像计算。
    ///
    /// 使用 LateUpdate 是为了尽可能确保：
    /// - Animator
    /// - IK
    /// - XR Tracking
    /// - Hand Tracking
    ///
    /// 已完成当前帧的更新。
    /// </summary>
    [ContextMenu("Apply Mirror Now")]
    public void ApplyMirror()
    {
        if (head == null || source == null)
        {
            return;
        }

        // =========================================================
        // 1. Position 镜像
        // =========================================================
        //
        // Position 仍然完全以 Head 定义的镜像平面为基准。
        // 此部分与上一版的计算逻辑保持一致。

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

        // 如果不需要镜像旋转，
        // 则只将 Position 赋值给目标对象。
        if (!mirrorRotation)
        {
            transform.position = mirroredPosition;
            return;
        }

        // =========================================================
        // 2. Rotation 镜像参考轴
        // =========================================================
        //
        // Rotation 不再使用 Position 镜像平面的轴。
        // 它使用独立的 Rotation Reference Frame。

        GetRotationReferenceAxes(
            out Vector3 rightAxis,
            out Vector3 upAxis,
            out Vector3 forwardAxis
        );

        // 使用 Source 当前的世界 Forward 和 Up，
        // 作为重建镜像旋转的基础。
        Vector3 mirroredForward = source.forward;
        Vector3 mirroredUp = source.up;

        // =========================================================
        // 3. 左右维度 Rotation 镜像
        // =========================================================
        //
        // 反转 Forward 和 Up 在 Reference Frame.right
        // 方向上的分量。

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
        // 4. 上下维度 Rotation 镜像
        // =========================================================
        //
        // 反转 Forward 和 Up 在 Reference Frame.up
        // 方向上的分量。

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
        // 5. 前后维度 Rotation 镜像
        // =========================================================
        //
        // 反转 Forward 和 Up 在 Reference Frame.forward
        // 方向上的分量。

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
        // 6. 安全性检查
        // =========================================================

        if (mirroredForward.sqrMagnitude < Epsilon ||
            mirroredUp.sqrMagnitude < Epsilon)
        {
            transform.position = mirroredPosition;
            return;
        }

        mirroredForward.Normalize();
        mirroredUp.Normalize();

        // 消除浮点计算产生的微小误差，
        // 确保 Forward 与 Up 相互正交。
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
        // 7. 重建镜像后的 Quaternion
        // =========================================================

        Quaternion mirroredRotation = Quaternion.LookRotation(
            mirroredForward,
            mirroredUp
        );

        // 附加用户指定的局部旋转修正。
        mirroredRotation *= Quaternion.Euler(
            rotationOffsetEuler
        );

        // 同时设置世界 Position 和世界 Rotation。
        transform.SetPositionAndRotation(
            mirroredPosition,
            mirroredRotation
        );
    }

    // =============================================================
    // Position Mirror Helpers
    // =============================================================

    /// <summary>
    /// 获得 Position 镜像平面上的一个点。
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
    /// 获得 Position 镜像平面的世界坐标法线。
    ///
    /// 此法线始终以 Head 为方向基准，
    /// 与 Rotation Reference Frame 无关。
    /// </summary>
    private Vector3 GetPositionPlaneNormal()
    {
        switch (planeMode)
        {
            case MirrorPlaneMode.HeadSagittalPlane:
            {
                // 平面由 Head.forward 和 Head.up 张成，
                // 因此法线是 Head.right。
                return head.right.normalized;
            }

            case MirrorPlaneMode.WorldVerticalPlane:
            {
                Vector3 worldUp = Vector3.up;

                // 去掉 Head.forward 的竖直分量，
                // 获得 Head 在水平面上的朝向。
                Vector3 horizontalForward =
                    Vector3.ProjectOnPlane(
                        head.forward,
                        worldUp
                    );

                // 当 Head 几乎垂直向上或向下时，
                // 水平投影可能非常小。
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

                // World Up 和 Horizontal Forward 张成竖直平面。
                // Cross(Up, Forward) 得到平面右侧法线。
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
    /// 获得当前实际使用的 Rotation Reference Frame。
    ///
    /// 优先级：
    /// 1. 手动指定的 Rotation Reference Frame
    /// 2. Head
    ///
    /// 使用 Head 作为默认值可以保持与旧版本兼容。
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
    /// 获得 Rotation 镜像使用的三个世界空间参考轴。
    ///
    /// 这些轴独立于 Position 镜像平面。
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
            // 理论上 Head 是 Required Reference，
            // 所以通常不会进入此分支。
            // 这里仍然保留世界坐标系作为安全回退。
            rightAxis = Vector3.right;
            upAxis = Vector3.up;
            forwardAxis = Vector3.forward;
        }
    }

    // =============================================================
    // Reflection Math
    // =============================================================

    /// <summary>
    /// 将世界坐标中的点相对于指定平面进行镜像。
    ///
    /// mirroredPoint =
    /// point - 2 × signedDistance × planeNormal
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
    /// 将一个世界空间方向向量相对于指定平面镜像。
    ///
    /// planeNormal 是该镜像平面的法线。
    ///
    /// reflectedDirection =
    /// direction - 2 × dot(direction, normal) × normal
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
    /// 显示 Position 镜像平面。
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

        // 显示 Position 镜像平面的法线。
        Gizmos.color = Color.magenta;

        Gizmos.DrawRay(
            center,
            normal * gizmoPlaneSize * 0.5f
        );
    }

    /// <summary>
    /// 显示 Rotation Reference Frame 的三个参考轴。
    ///
    /// 红色：Right
    /// 绿色：Up
    /// 蓝色：Forward
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

        // 红色：左右方向
        Gizmos.color = Color.red;

        Gizmos.DrawRay(
            origin,
            right * rotationAxesGizmoLength
        );

        // 绿色：上下方向
        Gizmos.color = Color.green;

        Gizmos.DrawRay(
            origin,
            up * rotationAxesGizmoLength
        );

        // 蓝色：前后方向
        Gizmos.color = Color.blue;

        Gizmos.DrawRay(
            origin,
            forward * rotationAxesGizmoLength
        );
    }
}





