using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// A second copy of the user's body that faces the user like a reflection. It is driven by the IK targets of the
    /// user's avatar, so it also shows what the movement transformation does to it.
    /// </summary>
    [DefaultExecutionOrder(9000)]
    public sealed class MirrorFigure : MonoBehaviour
    {
        [SerializeField] private AvatarDresser body;
        [Tooltip("What the figure follows: head, left hand, right hand.")]
        [SerializeField] private Transform[] targets;
        [Tooltip("IK targets of the user's avatar: head, left hand, right hand.")]
        [SerializeField] private Transform[] userTargets;
        [Tooltip("Where the user stands.")]
        [SerializeField] private Vector3 home = new Vector3(0f, 0f, 0.5f);

        private Vector3 normal, middle;

        public bool IsShown => body.gameObject.activeSelf;

        public AvatarDresser Body => body;

        /// <summary>Direction from the user to the figure. The mirror plane is perpendicular to it.</summary>
        internal Vector3 Normal => normal;

        /// <summary>Shows the figure at a place on the floor, turned toward the user.</summary>
        public void Show(Vector3 place)
        {
            normal = Vector3.ProjectOnPlane(place - home, Vector3.up).normalized;
            middle = (place + home) * 0.5f;
            Follow();
            body.gameObject.SetActive(true);
            StartCoroutine(body.SnapFeet());
        }

        public void Hide() => body.gameObject.SetActive(false);

        private void LateUpdate()
        {
            if (IsShown) Follow();
        }

        private void Follow()
        {
            // The reflection of the right hand is the left hand of the figure.
            Reflect(userTargets[0], targets[0]);
            Reflect(userTargets[2], targets[1]);
            Reflect(userTargets[1], targets[2]);
        }

        private void Reflect(Transform source, Transform target)
        {
            Vector3 position = source.position - 2f * Vector3.Dot(source.position - middle, normal) * normal;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Reflect(source.forward, normal), Vector3.Reflect(source.up, normal));
            target.SetPositionAndRotation(position, rotation);
        }
    }
}