using UnityEngine;

/// <summary>
/// Copies the local position of one head and mixes its local rotation with the rotation of a partner's head.
/// </summary>
public class HeadRotationMixedFollowSystem : MonoBehaviour
{
    public Transform myHead;
    public Transform partnerHead;

    [Tooltip("Share of the partner's rotation, from 0 (own rotation only) to 1 (partner's rotation only).")]
    public float followRatio = 0.5f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;

    void Update()
    {
        if (myHead == null) return;

        targetRotation = myHead.localRotation;
        targetPosition = myHead.localPosition;

        if (partnerHead != null)
        {
            targetRotation = Quaternion.Slerp(myHead.localRotation, partnerHead.localRotation, followRatio);
        }

        transform.localPosition = targetPosition;
        transform.localRotation = targetRotation;
    }
}
