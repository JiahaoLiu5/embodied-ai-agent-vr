using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HeadRotationMixedFollowSystem : MonoBehaviour
{
    public Transform myHead;
    public Transform partnerHead;
    public float followRatio = 0.5f;
    private Vector3 targetPosition;
    private Quaternion targetRotation;

    void Update()
    {
        targetRotation = myHead.localRotation;
        targetPosition = myHead.localPosition;

        if (myHead != null && partnerHead != null)
        {
            targetRotation = Quaternion.Slerp(myHead.localRotation, partnerHead.localRotation, followRatio);
        }

        transform.localPosition = targetPosition;
        transform.localRotation = targetRotation;
    }
}



