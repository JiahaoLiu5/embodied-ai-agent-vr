using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>Keeps this object at an offset from another one, for example a voice at a mouth.</summary>
    [DefaultExecutionOrder(20000)]
    public sealed class FollowTransform : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [Tooltip("The axes of the offset.")]
        [SerializeField] private Transform frame;
        [SerializeField] private Vector3 offset;

        private void LateUpdate()
        {
            if (target != null && frame != null) transform.position = target.position + frame.rotation * offset;
        }
    }
}