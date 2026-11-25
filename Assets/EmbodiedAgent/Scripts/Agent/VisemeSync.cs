using UnityEngine;

/// <summary>
/// Copies the viseme weights of the mesh driven by the lip sync to other meshes, such as teeth and tongue.
/// </summary>
public class VisemeSync : MonoBehaviour
{
    [Tooltip("Mesh driven directly by OVR LipSync, for example Head_Mesh")]
    public SkinnedMeshRenderer sourceMesh;

    [Tooltip("Other meshes that follow the mouth, for example Teeth_Mesh and Tongue_Mesh")]
    public SkinnedMeshRenderer[] targetMeshes;

    [Tooltip("Viseme blend shapes to copy. Three vowels have two names in use (viseme_I and viseme_ih, " +
             "viseme_O and viseme_oh, viseme_U and viseme_ou); if a mesh does not have one, the other is tried.")]
    public string[] visemeNames = new string[]
    {
        "viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH",
        "viseme_DD", "viseme_kk", "viseme_CH", "viseme_SS",
        "viseme_nn", "viseme_RR", "viseme_aa", "viseme_E",
        "viseme_I", "viseme_O", "viseme_U"
    };

    // Runs in LateUpdate so that the lip sync has already set the weights of the source mesh
    private void LateUpdate()
    {
        if (sourceMesh == null || sourceMesh.sharedMesh == null) return;
        if (visemeNames == null || targetMeshes == null) return;

        foreach (string viseme in visemeNames)
        {
            // Skip visemes the source mesh does not have
            int sourceIndex = FindViseme(sourceMesh.sharedMesh, viseme);
            if (sourceIndex == -1) continue;

            // Weight set by the lip sync in this frame
            float weight = sourceMesh.GetBlendShapeWeight(sourceIndex);

            // Copy it to the meshes that follow
            foreach (var target in targetMeshes)
            {
                if (target == null || target.sharedMesh == null) continue;

                int targetIndex = FindViseme(target.sharedMesh, viseme);
                if (targetIndex != -1)
                {
                    target.SetBlendShapeWeight(targetIndex, weight);
                }
            }
        }
    }

    /// <summary>
    /// Index of a viseme in a mesh, or -1. Tries the alternative name if the mesh does not have the given one.
    /// </summary>
    private static int FindViseme(Mesh mesh, string viseme)
    {
        if (string.IsNullOrEmpty(viseme)) return -1;

        int index = mesh.GetBlendShapeIndex(viseme);
        if (index != -1) return index;

        string alternative = GetAlternativeName(viseme);
        return alternative != null ? mesh.GetBlendShapeIndex(alternative) : -1;
    }

    private static string GetAlternativeName(string viseme)
    {
        switch (viseme)
        {
            case "viseme_I": return "viseme_ih";
            case "viseme_O": return "viseme_oh";
            case "viseme_U": return "viseme_ou";
            case "viseme_ih": return "viseme_I";
            case "viseme_oh": return "viseme_O";
            case "viseme_ou": return "viseme_U";
            default: return null;
        }
    }
}
