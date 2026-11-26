using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// One instance of the Modulation prefab: mixes a share of a partner's head and hand movement into one's own.
    /// Wraps the enabled components of the prefab so that they can be prepared, read, and changed together.
    /// </summary>
    public sealed class ModulationUnit
    {
        private const int FirstPartnerPair = 3;

        private readonly TransformAlignment inputs;
        private readonly SlowFollowSystem[] smoothing;
        private readonly MirrorTransformRotationHead headMirror;
        private readonly MirrorTransformAcrossHeadPlaneNew[] handMirrors;
        private readonly HeadRotationMixedFollowSystem headModulated;
        private readonly HeadRotationMixedFollowSystem headPlane;
        private readonly HandMixedFollowSystemFinal[] hands;
        private float ratio;

        public ModulationUnit(GameObject root)
        {
            Root = root;
            inputs = root.GetComponent<TransformAlignment>();
            smoothing = Enabled<SlowFollowSystem>();
            headMirror = Enabled<MirrorTransformRotationHead>().FirstOrDefault();
            handMirrors = Enabled<MirrorTransformAcrossHeadPlaneNew>();
            HeadRotationMixedFollowSystem[] heads = Enabled<HeadRotationMixedFollowSystem>();
            headModulated = heads.FirstOrDefault(h => h.name == "Head Modulated");
            headPlane = heads.FirstOrDefault(h => h.name == "Head Plane");
            hands = Enabled<HandMixedFollowSystemFinal>();
            ratio = headModulated != null ? headModulated.followRatio : 0f;
        }

        public GameObject Root { get; }

        /// <summary>False when the prefab does not have the expected parts.</summary>
        public bool IsComplete =>
            inputs != null && inputs.pairs.Count >= FirstPartnerPair + 3 && smoothing.Length == 3 && headMirror != null &&
            handMirrors.Length == 2 && headModulated != null && headPlane != null && hands.Length == 2;

        /// <summary>Share of the partner's movement, 0 to 1. The plane that turns the partner's hands always follows fully.</summary>
        public float Ratio
        {
            get { return ratio; }
            set
            {
                ratio = Mathf.Clamp01(value);
                headModulated.followRatio = ratio;
                foreach (HandMixedFollowSystemFinal hand in hands) hand.followRatio = ratio;
            }
        }

        /// <summary>True once both hands have settled on a rotation branch.</summary>
        public bool IsReady
        {
            get
            {
                foreach (HandMixedFollowSystemFinal hand in hands)
                    if (!hand.IsReady) return false;
                return true;
            }
        }

        /// <summary>Head, left, and right hand whose movement is mixed in.</summary>
        public void SetPartner(IReadOnlyList<Transform> partner)
        {
            for (int i = 0; i < 3; i++) inputs.pairs[FirstPartnerPair + i].source = partner[i];
        }

        /// <summary>Computes the whole chain once, so that no pose from before is left anywhere in it.</summary>
        public void Prewarm()
        {
            inputs.AlignNow();
            foreach (SlowFollowSystem s in smoothing) s.Snap();
            headMirror.ApplyMirrorRotation();
            foreach (MirrorTransformAcrossHeadPlaneNew m in handMirrors) m.ApplyMirror();
            headModulated.Apply();
            headPlane.Apply();
            foreach (HandMixedFollowSystemFinal hand in hands) hand.Snap();
        }

        private T[] Enabled<T>() where T : Behaviour
        {
            return Root.GetComponentsInChildren<T>(true).Where(c => c.enabled).ToArray();
        }
    }
}