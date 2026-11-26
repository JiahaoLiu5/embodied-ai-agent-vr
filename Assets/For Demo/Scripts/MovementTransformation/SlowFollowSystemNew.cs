using UnityEngine;

/// <summary>
/// Variant of SlowFollowSystem that follows the position at once and smooths only the rotation.
/// </summary>
public class SlowFollowSystemNew : MonoBehaviour
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
        transform.localPosition = targetPosition;
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, blend);
    }
}
