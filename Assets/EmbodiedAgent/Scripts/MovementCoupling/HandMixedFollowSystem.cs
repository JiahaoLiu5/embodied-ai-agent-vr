using UnityEngine;

/// <summary>
/// Earlier variant: mixes the head-to-hand vectors of two bodies and leaves the hand rotation unchanged.
/// HandMixedFollowSystemFinal is the version used by the Modulation prefab.
/// </summary>
public class HandMixedFollowSystem : MonoBehaviour
{
    public Transform myHead;
    public Transform myHand;
    public Transform partnerHead;
    public Transform partnerHand;
    public Transform modulatedHead;
    public float followRatio = 0.5f;

    void Update()
    {
        if (myHead == null || myHand == null || modulatedHead == null) return;

        // Record the without-modulation hand target position and rotation
        Vector3 myHeadHand = myHand.localPosition - myHead.localPosition;
        Vector3 targetPosition = myHeadHand + modulatedHead.localPosition;
        Quaternion targetRotation = myHand.localRotation;

        if (partnerHead != null && partnerHand != null)
        {
            // Difference in facing directions
            Vector3 modHeadForward = modulatedHead.transform.forward;
            Vector3 modHeadForwardPlane = new Vector3(modHeadForward.x, 0, modHeadForward.z);
            Vector3 modUnitVec = modHeadForwardPlane.normalized;
            Vector3 parHeadForward = partnerHead.transform.forward;
            Vector3 parHeadForwardPlane = new Vector3(parHeadForward.x, 0, parHeadForward.z);
            Vector3 parUnitVec = parHeadForwardPlane.normalized;

            // Calculate the partner's head-hand vector
            Vector3 parHeadHand = partnerHand.localPosition - partnerHead.localPosition;

            // Calculate the transformed head-hand vector
            Quaternion transformRotation = Quaternion.FromToRotation(parUnitVec, modUnitVec);
            Vector3 tranHeadHand = transformRotation * parHeadHand;

            // Calculate the modulated head-hand vector via lerp
            Vector3 modHeadHand = Vector3.Lerp(myHeadHand, tranHeadHand, followRatio);
            targetPosition = modHeadHand + modulatedHead.localPosition;
        }

        transform.localPosition = targetPosition;
        transform.localRotation = targetRotation;
    }
}
