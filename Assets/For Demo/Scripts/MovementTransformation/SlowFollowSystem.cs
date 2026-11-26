using UnityEngine;

/// <summary>
/// Follows the local position and rotation of another transform with exponential smoothing.
/// </summary>
public class SlowFollowSystem : MonoBehaviour
{
    public Transform UpStreamTarget;

    [Tooltip("Time constant of the smoothing in seconds. Larger values follow more slowly; 0 follows at once.")]
    public float lerpTime = 0.5f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool hasTarget;

    void Update()
    {
        if (UpStreamTarget != null)
        {
            targetPosition = UpStreamTarget.localPosition;
            targetRotation = UpStreamTarget.localRotation;
            hasTarget = true;
        }

        if (!hasTarget) return;

        // This form gives the same motion at every frame rate
        float blend = lerpTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / lerpTime) : 1f;
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, blend);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, blend);
    }

    /// <summary>
    /// Takes the pose of the followed transform at once, so that an old pose is not smoothed away slowly.
    /// </summary>
    public void Snap()
    {
        if (UpStreamTarget == null) return;

        targetPosition = UpStreamTarget.localPosition;
        targetRotation = UpStreamTarget.localRotation;
        hasTarget = true;
        transform.localPosition = targetPosition;
        transform.localRotation = targetRotation;
    }
}
