using System.Collections;
using UnityEngine;

namespace ReadyPlayerMe.Core
{
    [DisallowMultipleComponent, AddComponentMenu("Ready Player Me/Eye Animation Handler New", 0)]
    public class EyeAnimationHandlerNew : MonoBehaviour
    {
        [Header("Blink Settings")]
        [Range(0.01f, 1f), Tooltip("Blink Speed (s)")]
        public float blinkSpeed = 0.1f;

        [Range(0.5f, 10f), Tooltip("Blink Interval (s)")]
        public float blinkInterval = 3f;
        
        [Header("Weight Settings (Key Setting)")]
        [Tooltip("Max Blink Weight")]
        public float maxBlinkWeight = 1f; 

        [Header("Eye Movement (Optional)")]
        [Tooltip("Enable Eye Rotation")]
        public bool enableEyeRotation = false; 

        [Header("Debug")]
        [Tooltip("Debug Force Close")]
        public bool debugForceClose = false;

        [Header("References")]
        [SerializeField] 
        private SkinnedMeshRenderer headMesh;

        // 内部变量
        private int eyeBlinkLeftIndex = -1;
        private int eyeBlinkRightIndex = -1;

        // 眨眼状态
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
            // 智能查找 Mesh：循环等待直到找到带有 eyeBlinkLeft 的有效网格
            while (true)
            {
                // 1. 检查当前引用的 headMesh 是否有效
                if (headMesh != null && headMesh.sharedMesh != null)
                {
                    if (headMesh.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft") != -1)
                        break; // 找到了，跳出循环
                }
                
                // 2. 尝试自动查找
                TryAutoFindMesh();
                
                // 3. 再次检查自动查找的结果
                if (headMesh != null && headMesh.sharedMesh != null && headMesh.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft") != -1)
                    break;

                // 还没找到，等待 0.5 秒后重试
                yield return new WaitForSeconds(0.5f);
            }

            // 获取 Blendshape 索引
            eyeBlinkLeftIndex = headMesh.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft");
            eyeBlinkRightIndex = headMesh.sharedMesh.GetBlendShapeIndex("eyeBlinkRight");

            isInitialized = true;
            blinkTimer = blinkInterval;
            Debug.Log($"[EyeHandler] 初始化完成。Mesh: {headMesh.name} | MaxWeight: {maxBlinkWeight}");
        }

        private void TryAutoFindMesh()
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            
            // 策略：优先找名字带 "Head" 的，其次找任意带有眨眼功能的
            foreach (var r in renderers)
            {
                if (r.sharedMesh == null) continue;
                if (r.name.Contains("Head") && r.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft") != -1) { headMesh = r; return; }
            }
            foreach (var r in renderers)
            {
                if (r.sharedMesh == null) continue;
                if (r.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft") != -1) { headMesh = r; return; }
            }
        }

        private void LateUpdate()
        {
            if (!isInitialized || headMesh == null) return;

            HandleBlinking();
        }

        private void HandleBlinking()
        {
            // 目标权重由 maxBlinkWeight 决定 (现在默认为 1)
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
            headMesh.SetBlendShapeWeight(eyeBlinkLeftIndex, weight);
            headMesh.SetBlendShapeWeight(eyeBlinkRightIndex, weight);
        }
    }
}