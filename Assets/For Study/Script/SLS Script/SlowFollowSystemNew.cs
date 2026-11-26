using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlowFollowSystemNew : MonoBehaviour
{
    public Transform UpStreamTarget;
    public float lerpTime = 0.5f;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    void Update()
    {
        if (UpStreamTarget != null)
        {
            targetPosition = UpStreamTarget.localPosition;
            targetRotation = UpStreamTarget.localRotation;
        }
        transform.localPosition = targetPosition;
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, Time.deltaTime / lerpTime);
    }
}

