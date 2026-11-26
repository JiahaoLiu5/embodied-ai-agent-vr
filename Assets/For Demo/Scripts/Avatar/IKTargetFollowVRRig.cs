using UnityEngine;

[System.Serializable]
public class VRMap
{
    public Transform vrTarget;
    public Transform ikTarget;
    public Vector3 trackingPositionOffset;
    public Vector3 trackingRotationOffset;

    public Vector3 MappedPosition => vrTarget.TransformPoint(trackingPositionOffset);

    public void Map()
    {
        if (vrTarget == null || ikTarget == null) return;

        ikTarget.position = MappedPosition;
        ikTarget.rotation = vrTarget.rotation * Quaternion.Euler(trackingRotationOffset);
    }
}

/// <summary>
/// Moves the IK targets of a humanoid avatar to the tracked head and hands and turns the body towards the head's yaw.
/// </summary>
public class IKTargetFollowVRRig : MonoBehaviour
{
    // Frame rate at which turnSmoothness is the share of the remaining turn per frame
    private const float ReferenceFrameRate = 72f;

    [Range(0,1)]
    [Tooltip("Share of the remaining turn made in one frame at 72 frames per second. Other frame rates turn at the same speed.")]
    public float turnSmoothness = 0.1f;
    public VRMap head;
    public VRMap leftHand;
    public VRMap rightHand;

    public Vector3 headBodyPositionOffset;

    [Tooltip("Added to the yaw of the head, in degrees.")]
    public float headBodyYawOffset;

    void LateUpdate()
    {
        if (head.vrTarget == null || head.ikTarget == null) return;

        // Place the body first: the IK targets may be children of this object
        transform.position = head.MappedPosition + headBodyPositionOffset;
        float yaw = head.vrTarget.eulerAngles.y + headBodyYawOffset;
        float blend = 1f - Mathf.Pow(1f - turnSmoothness, Time.deltaTime * ReferenceFrameRate);
        transform.rotation = Quaternion.Lerp(transform.rotation,Quaternion.Euler(transform.eulerAngles.x, yaw, transform.eulerAngles.z),blend);

        head.Map();
        leftHand.Map();
        rightHand.Map();
    }
}
