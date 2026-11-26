using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Plays the gestures requested by the model one after the other for as long as the agent is speaking.
/// Gesture names are names of states on the first layer of the Animator.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AgentAnimationController : MonoBehaviour
{
    [Tooltip("Not read by this script. Kept so that existing scenes keep their reference.")]
    public AudioSource agentAudioSource;

    [Tooltip("AudioPlayer that plays the agent's voice. Its buffer state tells whether the agent is speaking.")]
    public AudioPlayer audioPlayer;

    [Tooltip("Animator state the agent returns to when it stops speaking.")]
    [SerializeField] private string idleState = "Idle";

    [Tooltip("Animator state played once if the requested gestures end before the speech does.")]
    [SerializeField] private string fallbackGesture = "Talking (7)";

    private const int BaseLayer = 0;

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

    /// <summary>
    /// Whether the first layer of the Animator has a state with this name.
    /// Animator.CrossFade only logs a warning for unknown names, so callers check first.
    /// </summary>
    public static bool HasState(Animator animator, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return false;
        if (!animator.isActiveAndEnabled || animator.layerCount <= BaseLayer) return false;

        string fullName = animator.GetLayerName(BaseLayer) + "." + stateName;
        return animator.HasState(BaseLayer, Animator.StringToHash(stateName)) ||
               animator.HasState(BaseLayer, Animator.StringToHash(fullName));
    }

    private void PlayAnimationSequence(List<string> animNames)
    {
        if (!isActiveAndEnabled) return;

        if (currentSequenceRoutine != null)
        {
            StopCoroutine(currentSequenceRoutine);
        }
        currentSequenceRoutine = StartCoroutine(SmartSequenceRoutine(animNames));
    }

    private IEnumerator SmartSequenceRoutine(List<string> animNames)
    {
        Debug.Log($"<color=cyan>[GPT Animation] Sequence received: {string.Join(", ", animNames)}</color>");

        // Wait until the audio stream really starts playing (3 seconds at most)
        float waitStartTimer = 0f;
        while (!IsAudioStreamActive() && waitStartTimer < 3f)
        {
            waitStartTimer += Time.deltaTime;
            yield return null;
        }

        // Still silent after 3 seconds: return to idle and stop
        if (!IsAudioStreamActive())
        {
            ForceReturnToIdle();
            yield break;
        }

        // ---------------------------------------------------------
        // Stage 1: play the gestures in order and watch for the end of the speech
        // ---------------------------------------------------------
        for (int i = 0; i < animNames.Count; i++)
        {
            string animName = animNames[i];

            // The model may ask for a gesture this Animator does not have
            if (!HasState(animator, animName))
            {
                Debug.LogWarning($"[GPT Animation] '{animName}' is not a state of {animator.name}. Skipped.", this);
                continue;
            }

            animator.CrossFade(animName, 0.2f);

            yield return null;

            float animLength = 0f;
            if (animator.IsInTransition(BaseLayer))
            {
                animLength = animator.GetNextAnimatorStateInfo(BaseLayer).length;
            }
            else
            {
                animLength = animator.GetCurrentAnimatorStateInfo(BaseLayer).length;
            }

            float waitTime = Mathf.Max(0, animLength + 1f);

            float timer = 0f;
            float silenceTimer = 0f;
            float gracePeriod = 0.3f; // Network gaps and short pauses up to this length (seconds) do not end the speech
            bool audioStoppedEarly = false;

            while (timer < waitTime)
            {
                timer += Time.deltaTime;

                if (!IsAudioStreamActive())
                {
                    silenceTimer += Time.deltaTime;
                    if (silenceTimer > gracePeriod)
                    {
                        audioStoppedEarly = true; // Silent for longer than the grace period: the agent has finished speaking
                        break;
                    }
                }
                else
                {
                    silenceTimer = 0f;
                }
                yield return null;
            }

            // The agent has finished speaking: blend back to idle at once and stop
            if (audioStoppedEarly)
            {
                Debug.Log("<color=yellow>[GPT Animation] Speech finished, returning to idle.</color>");
                ForceReturnToIdle();
                yield break;
            }
        }

        // ---------------------------------------------------------
        // Stage 2: all gestures played but the agent is still speaking -> add the fallback gesture
        // ---------------------------------------------------------
        yield return new WaitForSeconds(0.1f);
        if (IsAudioStreamActive() && HasState(animator, fallbackGesture))
        {
            Debug.Log($"<color=orange>[GPT Animation] Speech continues, adding {fallbackGesture}.</color>");
            animator.CrossFade(fallbackGesture, 0.2f);

            // Watch the audio here as well, so that the gesture does not continue after the speech
            float fallbackLength = animator.GetCurrentAnimatorStateInfo(BaseLayer).length;
            float timer = 0f;
            float silenceTimer = 0f;

            while (timer < fallbackLength)
            {
                timer += Time.deltaTime;
                if (!IsAudioStreamActive())
                {
                    silenceTimer += Time.deltaTime;
                    if (silenceTimer > 0.3f)
                    {
                        Debug.Log($"<color=yellow>[GPT Animation] Speech finished during {fallbackGesture}, returning to idle.</color>");
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

        // Stage 3: everything has been played, return to idle
        ForceReturnToIdle();
    }

    /// <summary>
    /// Blends the avatar back to its idle state
    /// </summary>
    private void ForceReturnToIdle()
    {
        if (HasState(animator, idleState))
        {
            animator.CrossFade(idleState, 0.3f);
        }
    }

    /// <summary>
    /// Whether the agent is speaking, according to the buffer state of the AudioPlayer
    /// </summary>
    private bool IsAudioStreamActive()
    {
        if (audioPlayer != null && audioPlayer.IsAudioPlaying())
            return true;

        return false;
    }
}
