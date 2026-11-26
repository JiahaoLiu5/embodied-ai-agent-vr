using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ReadyPlayerMe.Core
{
    /// <summary>
    /// Makes an avatar blink at a fixed interval by driving the eyeBlinkLeft and eyeBlinkRight blend shapes
    /// on every mesh below this object that has them, for example head, eyelashes and eye shading.
    ///
    /// Adapted from the EyeAnimationHandler of the Ready Player Me Unity SDK. The namespace and the menu entry
    /// are kept.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("Ready Player Me/Eye Animation Handler New", 0)]
    public class EyeAnimationHandlerNew : MonoBehaviour
    {
        private const string BlinkLeftShape = "eyeBlinkLeft";
        private const string BlinkRightShape = "eyeBlinkRight";

        [Header("Blink Settings")]
        [Range(0.01f, 1f), Tooltip("Blink Speed (s)")]
        public float blinkSpeed = 0.1f;

        [Range(0.5f, 10f), Tooltip("Blink Interval (s)")]
        public float blinkInterval = 3f;

        [Header("Weight Settings (Key Setting)")]
        [Tooltip("Max Blink Weight")]
        public float maxBlinkWeight = 1f;

        [Header("Eye Movement (Optional)")]
        [Tooltip("Reserved. This component does not rotate the eyes.")]
        public bool enableEyeRotation = false;

        [Header("Debug")]
        [Tooltip("Debug Force Close")]
        public bool debugForceClose = false;

        [Header("References")]
        [Tooltip("Optional. Found automatically if empty.")]
        [SerializeField]
        private SkinnedMeshRenderer headMesh;

        /// <summary>
        /// A mesh with blink shapes. The indices belong to one mesh and are read again if the mesh is replaced.
        /// </summary>
        private class BlinkTarget
        {
            public SkinnedMeshRenderer renderer;
            public Mesh mesh;
            public int leftIndex = -1;
            public int rightIndex = -1;
        }

        private readonly List<BlinkTarget> blinkTargets = new List<BlinkTarget>();

        // Blink state
        private float blinkTimer;
        private float currentBlinkWeight = 0f;
        private enum BlinkState { Idle, Closing, Opening }
        private BlinkState blinkState = BlinkState.Idle;
        private float blinkLerpTime = 0f;
        private bool isInitialized = false;

        private void Start()
        {
            StartCoroutine(InitializeRoutine());
        }

        private IEnumerator InitializeRoutine()
        {
            // Avatars may be loaded at runtime, so wait until a mesh with blink shapes exists
            while (true)
            {
                // 1. Check the assigned head mesh
                if (HasBlinkShape(headMesh)) break;

                // 2. Search for one
                TryAutoFindMesh();

                // 3. Check the result of the search
                if (HasBlinkShape(headMesh)) break;

                // Not found yet, try again in 0.5 s
                yield return new WaitForSeconds(0.5f);
            }

            CollectBlinkTargets();

            isInitialized = true;
            blinkTimer = blinkInterval;
            Debug.Log($"[EyeHandler] Initialized. Head mesh: {headMesh.name} | Blinking meshes: {blinkTargets.Count} | Max weight: {maxBlinkWeight}");
        }

        private static bool HasBlinkShape(SkinnedMeshRenderer meshRenderer)
        {
            return meshRenderer != null && meshRenderer.sharedMesh != null &&
                   meshRenderer.sharedMesh.GetBlendShapeIndex(BlinkLeftShape) != -1;
        }

        private void TryAutoFindMesh()
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);

            // Prefer a mesh with "Head" in its name, then take any mesh that can blink
            foreach (var r in renderers)
            {
                if (r.name.Contains("Head") && HasBlinkShape(r)) { headMesh = r; return; }
            }
            foreach (var r in renderers)
            {
                if (HasBlinkShape(r)) { headMesh = r; return; }
            }
        }

        private void CollectBlinkTargets()
        {
            blinkTargets.Clear();
            AddBlinkTarget(headMesh);

            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r != headMesh && HasBlinkShape(r)) AddBlinkTarget(r);
            }
        }

        private void AddBlinkTarget(SkinnedMeshRenderer meshRenderer)
        {
            var target = new BlinkTarget { renderer = meshRenderer };
            ReadIndices(target);
            blinkTargets.Add(target);
        }

        private static void ReadIndices(BlinkTarget target)
        {
            target.mesh = target.renderer.sharedMesh;
            target.leftIndex = target.mesh != null ? target.mesh.GetBlendShapeIndex(BlinkLeftShape) : -1;
            target.rightIndex = target.mesh != null ? target.mesh.GetBlendShapeIndex(BlinkRightShape) : -1;
        }

        private void LateUpdate()
        {
            if (!isInitialized) return;

            HandleBlinking();
        }

        private void HandleBlinking()
        {
            // maxBlinkWeight is 1 for meshes whose blend shape weights run from 0 to 1 and 100 for a range of 0 to 100
            float targetWeight = maxBlinkWeight;

            if (debugForceClose)
            {
                ApplyBlinkWeight(targetWeight);
                return;
            }

            if (blinkState == BlinkState.Idle)
            {
                blinkTimer -= Time.deltaTime;
                if (blinkTimer <= 0)
                {
                    blinkState = BlinkState.Closing;
                    blinkLerpTime = 0f;
                }
            }
            else if (blinkState == BlinkState.Closing)
            {
                blinkLerpTime += Time.deltaTime;
                float t = blinkLerpTime / blinkSpeed;
                currentBlinkWeight = Mathf.Lerp(0f, targetWeight, t);

                if (t >= 1f)
                {
                    blinkState = BlinkState.Opening;
                    blinkLerpTime = 0f;
                }
                ApplyBlinkWeight(currentBlinkWeight);
            }
            else if (blinkState == BlinkState.Opening)
            {
                blinkLerpTime += Time.deltaTime;
                float t = blinkLerpTime / blinkSpeed;
                currentBlinkWeight = Mathf.Lerp(targetWeight, 0f, t);

                if (t >= 1f)
                {
                    blinkState = BlinkState.Idle;
                    blinkTimer = blinkInterval;
                    currentBlinkWeight = 0f;
                }
                ApplyBlinkWeight(currentBlinkWeight);
            }
        }

        private void ApplyBlinkWeight(float weight)
        {
            foreach (var target in blinkTargets)
            {
                if (target.renderer == null) continue;
                if (target.renderer.sharedMesh != target.mesh) ReadIndices(target);

                if (target.leftIndex != -1) target.renderer.SetBlendShapeWeight(target.leftIndex, weight);
                if (target.rightIndex != -1) target.renderer.SetBlendShapeWeight(target.rightIndex, weight);
            }
        }
    }
}
