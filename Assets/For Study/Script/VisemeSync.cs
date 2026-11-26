using UnityEngine;

public class VisemeSync : MonoBehaviour
{
    [Tooltip("被 OVR LipSync 直接驱动的主网格 (例如: Head_Mesh)")]
    public SkinnedMeshRenderer sourceMesh;

    [Tooltip("需要同步跟随嘴型运动的其他网格 (例如: Teeth_Mesh, Tongue_Mesh)")]
    public SkinnedMeshRenderer[] targetMeshes;

    [Tooltip("需要同步的 Viseme Blendshape 名称列表")]
    public string[] visemeNames = new string[]
    {
        "viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH",
        "viseme_DD", "viseme_kk", "viseme_CH", "viseme_SS",
        "viseme_nn", "viseme_RR", "viseme_aa", "viseme_E",
        "viseme_ih", "viseme_oh", "viseme_ou"
    };

    // 使用 LateUpdate 确保在 OVR 插件修改完 Head 之后，我们再进行复制
    private void LateUpdate()
    {
        if (sourceMesh == null) return;

        // 遍历所有发音元
        foreach (string viseme in visemeNames)
        {
            // 查找这个名字在源网格中的索引编号
            int sourceIndex = sourceMesh.sharedMesh.GetBlendShapeIndex(viseme);
            if (sourceIndex == -1) continue; // 如果没找到这个表情，就跳过

            // 获取当前这一帧，OVR 插件给头部设定的权重值 (0~100)
            float weight = sourceMesh.GetBlendShapeWeight(sourceIndex);

            // 遍历牙齿、舌头等跟随网格，把权重同步过去
            foreach (var target in targetMeshes)
            {
                if (target == null) continue;
                
                int targetIndex = target.sharedMesh.GetBlendShapeIndex(viseme);
                if (targetIndex != -1)
                {
                    target.SetBlendShapeWeight(targetIndex, weight);
                }
            }
        }
    }
}

