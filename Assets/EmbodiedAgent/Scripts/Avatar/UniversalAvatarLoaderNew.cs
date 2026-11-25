using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Gives an avatar in the scene the body of another avatar with the same bone names: copies bone positions and
/// scales, transfers the skinned meshes and selects the matching humanoid definition.
/// </summary>
public class UniversalAvatarLoaderNew : MonoBehaviour
{
    [Header("1. Drag in Source Prefab")]
    [Tooltip("Drag the Prefab you made from the GLB here")]
    [SerializeField] private GameObject sourcePrefab;

    [Header("2. Target Avatar in Scene")]
    [SerializeField] private GameObject targetAvatar;

    [Header("3. Gender & Skeleton Config (Auto-switch)")]
    [Tooltip("If auto-detection fails, force as male?")]
    [SerializeField] private bool forceMale = false;
    [Tooltip("Drag the Avatar asset of the male humanoid model here")]
    [SerializeField] private Avatar maleAvatarRig;
    [Tooltip("Drag the Avatar asset of the female humanoid model here")]
    [SerializeField] private Avatar femaleAvatarRig;

    [Header("Settings")]
    [SerializeField] private bool loadOnStart = true;

    private void Start()
    {
        if (loadOnStart) LoadAndRetarget();
    }

    [ContextMenu("Load and Align")]
    public void LoadAndRetarget()
    {
        if (sourcePrefab == null)
        {
            Debug.LogError("Source Prefab is empty!");
            return;
        }

        // 1. Choose the humanoid definition
        DetermineAndSwapAvatar(sourcePrefab.name);

        // 2. Instantiate the source
        GameObject tempAvatar = Instantiate(sourcePrefab);
        tempAvatar.name = "Temp_Source_Avatar";
        tempAvatar.SetActive(true);

        if (targetAvatar != null)
        {
            // 3. Align the bones
            Debug.Log("[1/2] Physically aligning bones...");
            AlignSkeletonToSource(tempAvatar, targetAvatar);

            // 4. Transfer the meshes
            Debug.Log("[2/2] Transferring meshes...");
            RetargetMesh(tempAvatar, targetAvatar);
        }

        // 5. Clean up
        Destroy(tempAvatar);
        Debug.Log("Loading completed successfully from Prefab!");
    }

    /// <summary>
    /// Checks the name for "female" first, because the word "female" contains "male".
    /// </summary>
    private void DetermineAndSwapAvatar(string nameString)
    {
        if (targetAvatar == null) return;

        Animator targetAnim = targetAvatar.GetComponent<Animator>();
        if (targetAnim == null) return;

        // The forceMale setting in the Inspector is the fallback
        bool isMale = forceMale;
        string fileName = nameString.ToLower();

        // "female" and "feminine" must be checked first, because "female" contains "male"
        if (fileName.Contains("feminine") || fileName.Contains("female"))
        {
            isMale = false;
            Debug.Log($"Female keyword detected, switching skeleton to Female: {fileName}");
        }
        else if (fileName.Contains("masculine") || fileName.Contains("male"))
        {
            isMale = true;
            Debug.Log($"Male keyword detected, switching skeleton to Male: {fileName}");
        }
        else
        {
            // Neither keyword found: keep the forceMale setting
            Debug.LogWarning($"Prefab Name '{fileName}' no gender keyword found. Using Default Force Male: {forceMale}");
        }

        // Apply the humanoid definition
        if (isMale)
        {
            if (maleAvatarRig != null) targetAnim.avatar = maleAvatarRig;
            else Debug.LogError("Missing Male Avatar Rig!");
        }
        else
        {
            if (femaleAvatarRig != null) targetAnim.avatar = femaleAvatarRig;
            else Debug.LogError("Missing Female Avatar Rig!");
        }
    }

    private void AlignSkeletonToSource(GameObject sourceRoot, GameObject targetRoot)
    {
        var sourceBones = sourceRoot.GetComponentsInChildren<Transform>();
        var targetBones = targetRoot.GetComponentsInChildren<Transform>();

        Dictionary<string, Transform> targetMap = new Dictionary<string, Transform>();
        foreach (var t in targetBones) targetMap[t.name] = t;

        foreach (var sourceBone in sourceBones)
        {
            if (sourceBone == sourceRoot.transform) continue;

            Transform targetBone = null;
            string boneName = sourceBone.name;

            if (targetMap.ContainsKey(boneName))
            {
                targetBone = targetMap[boneName];
            }
            else
            {
                string alias = GetBoneAlias(boneName);
                if (!string.IsNullOrEmpty(alias) && targetMap.ContainsKey(alias))
                {
                    targetBone = targetMap[alias];
                }
            }

            if (targetBone != null)
            {
                targetBone.localPosition = sourceBone.localPosition;
                targetBone.localScale = sourceBone.localScale;
            }
        }
    }

    private string GetBoneAlias(string name)
    {
        if (name == "LeftEye") return "EyeLeft";
        if (name == "RightEye") return "EyeRight";
        if (name == "EyeLeft") return "LeftEye";
        if (name == "EyeRight") return "RightEye";
        return null;
    }

    private void RetargetMesh(GameObject sourceRoot, GameObject targetRoot)
    {
        Dictionary<string, Transform> targetBoneMap = new Dictionary<string, Transform>();
        foreach (var bone in targetRoot.GetComponentsInChildren<Transform>(true))
            if (!targetBoneMap.ContainsKey(bone.name)) targetBoneMap.Add(bone.name, bone);

        var sourceRenderers = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var sourceRenderer in sourceRenderers)
        {
            Transform existingChild = targetRoot.transform.Find(sourceRenderer.gameObject.name);
            GameObject newSubMesh = existingChild != null ? existingChild.gameObject : new GameObject(sourceRenderer.gameObject.name);
            if (existingChild == null) newSubMesh.transform.SetParent(targetRoot.transform);

            newSubMesh.transform.localPosition = Vector3.zero;
            newSubMesh.transform.localRotation = Quaternion.identity;
            newSubMesh.transform.localScale = Vector3.one;

            var targetRenderer = newSubMesh.GetComponent<SkinnedMeshRenderer>();
            if (targetRenderer == null) targetRenderer = newSubMesh.AddComponent<SkinnedMeshRenderer>();

            targetRenderer.sharedMesh = sourceRenderer.sharedMesh;
            targetRenderer.materials = sourceRenderer.materials;
            targetRenderer.updateWhenOffscreen = true;

            Transform[] newBones = new Transform[sourceRenderer.bones.Length];
            for (int i = 0; i < sourceRenderer.bones.Length; i++)
            {
                Transform oldBone = sourceRenderer.bones[i];
                Transform foundBone = null;

                if (oldBone != null)
                {
                    if (targetBoneMap.ContainsKey(oldBone.name)) foundBone = targetBoneMap[oldBone.name];
                    else
                    {
                        string alias = GetBoneAlias(oldBone.name);
                        if (alias != null && targetBoneMap.ContainsKey(alias)) foundBone = targetBoneMap[alias];
                    }
                }
                newBones[i] = foundBone != null ? foundBone : targetRoot.transform;
            }
            targetRenderer.bones = newBones;

            if (sourceRenderer.rootBone != null)
            {
                if (targetBoneMap.ContainsKey(sourceRenderer.rootBone.name))
                    targetRenderer.rootBone = targetBoneMap[sourceRenderer.rootBone.name];
            }
        }
    }
}
