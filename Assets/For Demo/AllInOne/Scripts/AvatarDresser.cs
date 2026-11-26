using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// Gives a rigged body in the scene the look of one of the avatars: humanoid definition, bone lengths, and meshes.
    /// The IK of the body keeps running, so the avatar can be changed at any time.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class AvatarDresser : MonoBehaviour
    {
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private Animator animator;
        private IKFootSolver[] feet;

        /// <summary>The avatar that is worn, or null.</summary>
        public AgentAvatarCatalog.Entry Worn { get; private set; }

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (animator != null) return;
            animator = GetComponent<Animator>();
            feet = GetComponentsInChildren<IKFootSolver>(true);
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(bone.name)) bones.Add(bone.name, bone);
        }

        public void Dress(AgentAvatarCatalog.Entry avatar)
        {
            if (avatar == null || avatar.prefab == null || avatar == Worn) return;
            Initialize();

            // The definition first: with it the hips follow the root of the body.
            if (animator.avatar != avatar.definition) animator.avatar = avatar.definition;
            AlignBones(avatar.prefab);
            SetMeshes(avatar.prefab);
            Worn = avatar;
            if (isActiveAndEnabled) StartCoroutine(SnapFeet());
        }

        /// <summary>Puts the feet on the floor below the body.</summary>
        public IEnumerator SnapFeet()
        {
            // The body follows the head in LateUpdate, and the feet know their spacing only after their Start.
            yield return null;
            yield return null;
            foreach (IKFootSolver foot in feet) foot.Snap();
        }

        private void AlignBones(GameObject prefab)
        {
            foreach (Transform source in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (source == prefab.transform || source.GetComponent<SkinnedMeshRenderer>() != null) continue;
                Transform bone = FindBone(source.name);
                if (bone == null) continue;
                bone.localPosition = source.localPosition;
                bone.localScale = source.localScale;
            }
        }

        private void SetMeshes(GameObject prefab)
        {
            var used = new HashSet<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer source in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                SkinnedMeshRenderer target = FindOrCreateRenderer(source.name);
                Transform[] sourceBones = source.bones;
                var targetBones = new Transform[sourceBones.Length];
                for (int i = 0; i < sourceBones.Length; i++)
                {
                    Transform bone = sourceBones[i] != null ? FindBone(sourceBones[i].name) : null;
                    targetBones[i] = bone != null ? bone : transform;
                }
                target.sharedMesh = source.sharedMesh;
                target.sharedMaterials = source.sharedMaterials;
                target.bones = targetBones;
                target.rootBone = source.rootBone != null ? FindBone(source.rootBone.name) : null;
                target.updateWhenOffscreen = true;
                target.gameObject.layer = gameObject.layer;
                target.enabled = true;
                ClearBlendShapes(target);
                used.Add(target);
            }

            // Hair and other parts of the previous avatar that this one does not have
            foreach (SkinnedMeshRenderer other in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (used.Contains(other)) continue;
                other.enabled = false;
                other.sharedMesh = null;
            }
        }

        private SkinnedMeshRenderer FindOrCreateRenderer(string meshName)
        {
            Transform child = transform.Find(meshName);
            if (child == null)
            {
                child = new GameObject(meshName).transform;
                child.SetParent(transform, false);
            }
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return child.TryGetComponent(out SkinnedMeshRenderer existing) ? existing : child.gameObject.AddComponent<SkinnedMeshRenderer>();
        }

        private static void ClearBlendShapes(SkinnedMeshRenderer target)
        {
            int count = target.sharedMesh != null ? target.sharedMesh.blendShapeCount : 0;
            for (int i = 0; i < count; i++) target.SetBlendShapeWeight(i, 0f);
        }

        private Transform FindBone(string boneName)
        {
            if (bones.TryGetValue(boneName, out Transform bone)) return bone;
            string alias = Alias(boneName);
            return alias != null && bones.TryGetValue(alias, out bone) ? bone : null;
        }

        private static string Alias(string boneName)
        {
            switch (boneName)
            {
                case "LeftEye": return "EyeLeft";
                case "RightEye": return "EyeRight";
                case "EyeLeft": return "LeftEye";
                case "EyeRight": return "RightEye";
                default: return null;
            }
        }
    }
}