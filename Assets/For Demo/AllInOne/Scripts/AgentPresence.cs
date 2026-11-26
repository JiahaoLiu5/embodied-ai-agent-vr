using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// The visible side of the agent apart from the conversation: shows and hides her, keeps her idle movement alive,
    /// and lets her gesture when there is no voice. Both of her bodies, the captured and the visible one, play the same.
    /// </summary>
    public sealed class AgentPresence : MonoBehaviour
    {
        private const int Layer = 0;
        private const float IdleEnd = 0.94f;
        private const float IdleFade = 0.5f;
        private const float GestureFade = 0.2f;
        private const float ReturnFade = 0.3f;
        private const int GesturesInARow = 3;

        private static readonly int Idle = Animator.StringToHash("Idle");

        [Tooltip("The invisible body that plays the gestures.")]
        [SerializeField] private Animator capture;
        [Tooltip("The visible body, whose head and arms follow the captured one.")]
        [SerializeField] private Animator body;
        [SerializeField] private RealtimeAPIWrapper api;

        private readonly Dictionary<string, float> lengths = new Dictionary<string, float>();
        private readonly List<Renderer> renderers = new List<Renderer>();
        private System.Random random = new System.Random();
        private bool isVisible = true, isGesturing, hasGestured, isFrozen;
        private float timer;
        private int gesturesLeft;

        /// <summary>For automated tests: the states that were asked for.</summary>
        internal event Action<string> Requested;

        /// <summary>For automated tests: a fixed order of gestures.</summary>
        internal int Seed { set { random = new System.Random(value); } }

        internal Animator Capture => capture;

        internal Animator Body => body;

        /// <summary>Whether the user sees the agent. Her movement goes on while she is hidden.</summary>
        public bool Visible
        {
            get { return isVisible; }
            set
            {
                isVisible = value;
                foreach (Renderer r in renderers) r.enabled = value;
            }
        }

        /// <summary>Whether the agent gestures on her own, without speech.</summary>
        public bool OfflineGestures
        {
            get { return isGesturing; }
            set
            {
                if (value == isGesturing) return;
                isGesturing = value;
                gesturesLeft = GesturesInARow;
                timer = 0.5f;
                if (!value && hasGestured) ReturnToIdle();
            }
        }

        /// <summary>Stops all movement of the agent while the session is paused.</summary>
        public bool Frozen
        {
            set
            {
                isFrozen = value;
                capture.speed = body.speed = value ? 0f : 1f;
            }
        }

        private void Awake()
        {
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) renderers.Add(r);
            foreach (AnimationClip clip in capture.runtimeAnimatorController.animationClips) lengths[clip.name] = clip.length;
        }

        private void Update()
        {
            if (isFrozen) return;
            if (isGesturing) ContinueGestures();
            else LoopIdle();
        }

        /// <summary>Length of a state in seconds. The states and their clips have the same names.</summary>
        public float LengthOf(string state) => lengths.TryGetValue(state, out float length) ? length : 0f;

        /// <summary>Blends both bodies to a state.</summary>
        public void Play(string state, float fade = GestureFade)
        {
            Requested?.Invoke(state);
            hasGestured = true;
            if (AgentAnimationController.HasState(capture, state)) capture.CrossFade(state, fade);
            if (AgentAnimationController.HasState(body, state)) body.CrossFade(state, fade);
        }

        public void ReturnToIdle()
        {
            hasGestured = false;
            CrossFadeToIdle(ReturnFade);
        }

        private void ContinueGestures()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;

            IReadOnlyList<string> names = api.GestureNames;
            if (gesturesLeft == 0 || names.Count == 0)
            {
                ReturnToIdle();
                gesturesLeft = GesturesInARow;
                timer = 2f + 2f * (float)random.NextDouble();
                return;
            }

            string gesture = names[random.Next(names.Count)];
            Play(gesture);
            gesturesLeft--;
            timer = Mathf.Max(1f, LengthOf(gesture)) + 1f;
        }

        /// <summary>The idle clip does not loop by itself, so it is blended into its own beginning.</summary>
        private void LoopIdle()
        {
            if (IsAtIdleEnd(capture) && IsAtIdleEnd(body)) CrossFadeToIdle(IdleFade);
        }

        private static bool IsAtIdleEnd(Animator animator)
        {
            if (animator.IsInTransition(Layer)) return false;
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(Layer);
            return state.shortNameHash == Idle && state.normalizedTime > IdleEnd;
        }

        private void CrossFadeToIdle(float seconds)
        {
            capture.CrossFadeInFixedTime(Idle, seconds, Layer, 0f);
            body.CrossFadeInFixedTime(Idle, seconds, Layer, 0f);
        }
    }
}