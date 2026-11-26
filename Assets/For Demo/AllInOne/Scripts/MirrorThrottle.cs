using System.Collections.Generic;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// Keeps the mirror cameras of the room affordable: panels are left out, the view ends behind the walls, and on a
    /// headset the cameras take turns and render into smaller textures. The shared materials are not changed.
    /// </summary>
    public sealed class MirrorThrottle : MonoBehaviour
    {
        private static readonly int MainTexture = Shader.PropertyToID("_MainTex");

        [SerializeField] private Camera[] cameras;
        [Tooltip("Layers that the mirrors do not show.")]
        [SerializeField] private LayerMask hidden;
        [SerializeField] private float farClip = 15f;
        [SerializeField] private Vector2Int headsetSize = new Vector2Int(800, 500);

        private readonly List<RenderTexture> created = new List<RenderTexture>();
        private bool takeTurns;

        private void Awake()
        {
            foreach (Camera mirror in cameras)
            {
                mirror.cullingMask &= ~hidden.value;
                mirror.farClipPlane = farClip;
            }

            takeTurns = Application.isMobilePlatform;
            if (!takeTurns) return;
            Renderer[] surfaces = GetComponentsInChildren<Renderer>(true);
            foreach (Camera mirror in cameras) UseSmallTexture(mirror, surfaces);
        }

        private void Update()
        {
            if (!takeTurns) return;
            int turn = Time.frameCount % cameras.Length;
            for (int i = 0; i < cameras.Length; i++) cameras[i].enabled = i == turn;
        }

        private void OnDestroy()
        {
            foreach (RenderTexture texture in created)
            {
                texture.Release();
                Destroy(texture);
            }
        }

        private void UseSmallTexture(Camera mirror, Renderer[] surfaces)
        {
            RenderTexture original = mirror.targetTexture;
            if (original == null) return;

            RenderTextureDescriptor size = original.descriptor;
            size.width = headsetSize.x;
            size.height = headsetSize.y;
            var small = new RenderTexture(size) { name = original.name + " (headset)" };
            created.Add(small);
            mirror.targetTexture = small;

            var block = new MaterialPropertyBlock();
            foreach (Renderer surface in surfaces)
            {
                Material[] materials = surface.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null || !material.HasProperty(MainTexture) || material.GetTexture(MainTexture) != original) continue;
                    block.SetTexture(MainTexture, small);
                    surface.SetPropertyBlock(block, slot);
                }
            }
        }
    }
}