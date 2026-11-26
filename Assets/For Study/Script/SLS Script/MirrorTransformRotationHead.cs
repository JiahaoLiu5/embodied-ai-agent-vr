using UnityEngine;

/// <summary>
/// 计算 Source Head 在旋转层面的三维镜像，
/// 并将镜像后的世界旋转赋值给当前挂载此脚本的 GameObject。
///
/// 本脚本不会修改目标对象的位置。
///
/// 三个可以独立选择的旋转镜像维度：
/// 1. Left / Right
/// 2. Up / Down
/// 3. Front / Back
///
/// 镜像方向以 Reference Frame 的三个世界坐标轴为基准：
/// - Reference Frame.right
/// - Reference Frame.up
/// - Reference Frame.forward
///
/// 如果没有指定 Reference Frame：
/// 1. 优先使用 Source Head 的父物体
/// 2. 如果 Source Head 没有父物体，则使用世界坐标轴
/// </summary>
[DisallowMultipleComponent]
public class MirrorTransformRotationHead : MonoBehaviour
{
    [Header("Required Reference")]

    [Tooltip(
        "需要进行旋转镜像的原始 Head Transform。")]
    [SerializeField]
    private Transform sourceHead;

    [Header("Rotation Reference Frame")]

    [Tooltip(
        "用于定义左右、上下、前后三个镜像方向的参考坐标系。" +
        "\n\n推荐设置为 Avatar Root、XR Origin、胸部或稳定的身体中心节点。" +
        "\n\n不要设置为 Source Head 自身或 Source Head 的子物体。" +
        "\n\n如果为空，则优先使用 Source Head.parent；" +
        "如果 Source Head 没有父物体，则使用世界坐标系。")]
    [SerializeField]
    private Transform referenceFrame;

    [Header("Rotation Mirror Dimensions")]

    [Tooltip(
        "是否镜像旋转在左右方向上的分量。" +
        "\n\n该选项反转方向向量在 Reference Frame.right 上的分量。")]
    [SerializeField]
    private bool mirrorLeftRightRotation = true;

    [Tooltip(
        "是否镜像旋转在上下方向上的分量。" +
        "\n\n该选项反转方向向量在 Reference Frame.up 上的分量。")]
    [SerializeField]
    private bool mirrorUpDownRotation = false;

    [Tooltip(
        "是否镜像旋转在前后方向上的分量。" +
        "\n\n该选项反转方向向量在 Reference Frame.forward 上的分量。")]
    [SerializeField]
    private bool mirrorFrontBackRotation = false;

    [Header("Rotation Offset")]

    [Tooltip(
        "镜像旋转计算完成后，附加到目标对象上的局部旋转偏移。" +
        "\n\n当模型的 Head 本地坐标轴与目标对象不一致时，可以在这里修正。")]
    [SerializeField]
    private Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Debug")]

    [Tooltip(
        "选中目标对象时，是否在 Scene 窗口显示 Reference Frame 的三个参考轴。")]
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
    /// 立即计算并应用一次 Head 旋转镜像。
    ///
    /// 使用 LateUpdate 是为了尽可能确保：
    /// - XR Head Tracking
    /// - Animator
    /// - IK
    /// - 其他追踪脚本
    ///
    /// 已经完成当前帧的旋转更新。
    /// </summary>
    [ContextMenu("Apply Mirror Rotation Now")]
    public void ApplyMirrorRotation()
    {
        if (sourceHead == null)
        {
            return;
        }

        // 获取用于定义镜像维度的三个世界空间参考轴。
        GetReferenceAxes(
            out Vector3 rightAxis,
            out Vector3 upAxis,
            out Vector3 forwardAxis
        );

        // 使用 Source Head 当前的世界 Forward 和 Up 方向
        // 作为重建旋转的基础。
        Vector3 mirroredForward = sourceHead.forward;
        Vector3 mirroredUp = sourceHead.up;

        // =========================================================
        // 1. 左右维度旋转镜像
        // =========================================================
        //
        // 使用 Reference Frame.right 作为镜像法线，
        // 反转 Forward 和 Up 在左右轴上的方向分量。
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
        // 2. 上下维度旋转镜像
        // =========================================================
        //
        // 使用 Reference Frame.up 作为镜像法线，
        // 反转 Forward 和 Up 在上下轴上的方向分量。
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
        // 3. 前后维度旋转镜像
        // =========================================================
        //
        // 使用 Reference Frame.forward 作为镜像法线，
        // 反转 Forward 和 Up 在前后轴上的方向分量。
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

        // 防止无效方向导致 Quaternion.LookRotation 报错。
        if (mirroredForward.sqrMagnitude < Epsilon ||
            mirroredUp.sqrMagnitude < Epsilon)
        {
            return;
        }

        mirroredForward.Normalize();
        mirroredUp.Normalize();

        // 理论上镜像变换会保持两个方向相互垂直。
        // 但浮点数计算可能产生微小误差，
        // 因此在重建 Quaternion 前重新正交化。
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

        // 通过镜像后的 Forward 和 Up 重建有效的世界旋转。
        Quaternion mirroredRotation = Quaternion.LookRotation(
            mirroredForward,
            mirroredUp
        );

        // 应用额外的局部旋转修正。
        mirroredRotation *= Quaternion.Euler(
            rotationOffsetEuler
        );

        // 只修改世界旋转。
        // 此脚本不会读取或修改 transform.position。
        transform.rotation = mirroredRotation;
    }

    /// <summary>
    /// 获取用于三维旋转镜像的参考坐标轴。
    ///
    /// 优先级：
    /// 1. 用户指定的 Reference Frame
    /// 2. Source Head 的父物体
    /// 3. 世界坐标轴
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
            // Source Head 没有父物体，
            // 并且没有手动指定 Reference Frame 时，
            // 使用世界坐标系。
            rightAxis = Vector3.right;
            upAxis = Vector3.up;
            forwardAxis = Vector3.forward;
        }
    }

    /// <summary>
    /// 获取当前实际使用的参考坐标系。
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
    /// 将一个世界空间方向向量相对于指定镜像平面进行反射。
    ///
    /// planeNormal 是镜像平面的法线方向。
    ///
    /// 计算公式：
    ///
    /// reflectedDirection =
    /// direction - 2 × dot(direction, planeNormal) × planeNormal
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
    /// 在 Scene 窗口中显示三个旋转镜像参考轴。
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

        // 红色：左右轴
        Gizmos.color = Color.red;
        Gizmos.DrawRay(
            origin,
            right * gizmoAxisLength
        );

        // 绿色：上下轴
        Gizmos.color = Color.green;
        Gizmos.DrawRay(
            origin,
            up * gizmoAxisLength
        );

        // 蓝色：前后轴
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(
            origin,
            forward * gizmoAxisLength
        );
    }
}






