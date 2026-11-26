using UnityEngine;
using System.Collections;
using System.Collections.Generic;


[RequireComponent(typeof(Animator))]
public class AgentAnimationController : MonoBehaviour
{
    [Tooltip("拖入挂载了 AudioSource 的 GameObject")]
    public AudioSource agentAudioSource;

    [Tooltip("【新增】拖入挂载了 AudioPlayer 的 GameObject，用于获取更底层的音频缓冲状态")]
    public AudioPlayer audioPlayer; 
    
    private Animator animator;
    private Coroutine currentSequenceRoutine;

    private void Start()
    {
        animator = GetComponent<Animator>();
        RealtimeAPIWrapper.OnAnimationSequenceReceived += PlayAnimationSequence;
    }

    private void OnDestroy()
    {
        RealtimeAPIWrapper.OnAnimationSequenceReceived -= PlayAnimationSequence;
    }

    private void PlayAnimationSequence(List<string> animNames)
    {
        if (currentSequenceRoutine != null)
        {
            StopCoroutine(currentSequenceRoutine);
        }
        currentSequenceRoutine = StartCoroutine(SmartSequenceRoutine(animNames));
    }

    private IEnumerator SmartSequenceRoutine(List<string> animNames)
    {
        Debug.Log($"<color=cyan>[GPT Animation] 收到序列: {string.Join(", ", animNames)}</color>");

        // 等待网络音频流真正开始播放（最多等 3 秒）
        float waitStartTimer = 0f;
        while (!IsAudioStreamActive() && waitStartTimer < 3f)
        {
            waitStartTimer += Time.deltaTime;
            yield return null; 
        }

        // 如果等了 3 秒还是没声音，直接退回 Idle 并结束
        if (!IsAudioStreamActive())
        {
            ForceReturnToIdle();
            yield break;
        }

        // ---------------------------------------------------------
        // 阶段 1：按顺序播放动作数组，加入中断检测
        // ---------------------------------------------------------
        for (int i = 0; i < animNames.Count; i++)
        {
            string animName = animNames[i];
            animator.CrossFade(animName, 0.2f);
            
            // yield return new WaitForSeconds(0.1f); 
            yield return null;

            float animLength = 0f;
            if (animator.IsInTransition(0))
            {
                animLength = animator.GetNextAnimatorStateInfo(0).length;
            }
            else
            {
                animLength = animator.GetCurrentAnimatorStateInfo(0).length;
            }

            float waitTime = Mathf.Max(0, animLength + 1f);    //change
            Debug.Log(waitTime);

            float timer = 0f;
            float silenceTimer = 0f; 
            float gracePeriod = 0.3f; // 允许最多 0.6 秒的网络延迟断档或换气停顿
            bool audioStoppedEarly = false;

            while (timer < waitTime)
            {
                timer += Time.deltaTime;
                
                if (!IsAudioStreamActive())
                {
                    silenceTimer += Time.deltaTime;
                    if (silenceTimer > gracePeriod)
                    {
                        audioStoppedEarly = true; // 静音超过 0.6 秒，确信话已说完
                        break; 
                    }
                }
                else
                {
                    silenceTimer = 0f; 
                }
                yield return null;
            }

            // 【关键修复 1】：话已说完，立刻强行平滑过渡回 Idle，并彻底退出协程！
            if (audioStoppedEarly) 
            {
                Debug.Log("<color=yellow>[GPT Animation] 话已说完，强行将手势拉回 Idle。</color>");
                ForceReturnToIdle();
                yield break; 
            }
        }

        // ---------------------------------------------------------
        // 阶段 2：数组播完了，但声音还在继续 -> 追加 Talking (7) 兜底
        // ---------------------------------------------------------
        yield return new WaitForSeconds(0.1f);
        if (IsAudioStreamActive())
        {
            Debug.Log("<color=orange>[GPT Animation] 声音仍在继续，追加 Talking (7) 兜底。</color>");
            animator.CrossFade("Talking (7)", 0.2f);

            // 【关键修复 2】：对 Talking (7) 也进行实时检测，防止 Talking (7) 播到一半声音停了
            float talking7Length = animator.GetCurrentAnimatorStateInfo(0).length;
            float timer = 0f;
            float silenceTimer = 0f;

            Debug.Log(talking7Length);
            
            while (timer < talking7Length)
            {
                timer += Time.deltaTime;
                if (!IsAudioStreamActive())
                {
                    silenceTimer += Time.deltaTime;
                    if (silenceTimer > 0.3f)
                    {
                        Debug.Log("<color=yellow>[GPT Animation] Talking (7) 期间话已说完，强行拉回 Idle。</color>");
                        ForceReturnToIdle();
                        yield break;
                    }
                }
                else
                {
                    silenceTimer = 0f;
                }
                yield return null;
            }
        }

        // 阶段 3：自然播完，确保回归 Idle
        ForceReturnToIdle();
    }

    /// <summary>
    /// 【新增辅助方法】：强行将 Avatar 在 0.3 秒内平滑拉回 Idle 待机状态
    /// </summary>
    private void ForceReturnToIdle()
    {
        if (animator != null)
        {
            animator.CrossFade("Idle", 0.3f);
        }
    }

    /// <summary>
    /// 极其鲁棒的音频状态检测，完全信任 AudioPlayer 的底盘缓冲状态
    /// </summary>
    private bool IsAudioStreamActive()
    {
        if (audioPlayer != null && audioPlayer.IsAudioPlaying()) 
            return true;

        return false;
    }
}



