using System;
using System.Collections;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// Switches the movement transformation at run time without visible jumps: the old influence fades to zero, the new
    /// chain is prepared at ratio zero, the consumers are exchanged in one frame, and the new influence fades in.
    /// The end states are those of the four study conditions.
    /// </summary>
    public sealed class ConditionSwitcher : MonoBehaviour
    {
        public const float DefaultStrength = 0.3f;

        private const int MaximumWaitFrames = 120;

        [Header("Transformations")]
        [SerializeField] private GameObject modulationM;
        [SerializeField] private GameObject modulationL;
        [SerializeField] private GameObject recordingRig;
        [SerializeField] private RecordedMotionSource recorded;

        [Header("Who reads which pose")]
        [SerializeField] private IKTargetFollowVRRig userRaw;
        [SerializeField] private IKTargetFollowVRRig userModulated;
        [SerializeField] private TransformAlignment agentRaw;
        [SerializeField] private TransformAlignment agentModulated;

        [Header("Sources: head, left hand, right hand")]
        [SerializeField] private Transform[] userRig;
        [SerializeField] private Transform[] agentRig;
        [SerializeField] private Transform[] recordingTargets;

        [Header("Timing")]
        [SerializeField] private float rampSeconds = 0.35f;
        [SerializeField] private float strengthPerSecond = 1f;

        private ModulationUnit unitM, unitL;
        private float strength = DefaultStrength;
        private int waitFrames;

        /// <summary>The mode that is fully applied.</summary>
        public MovementMode Mode { get; private set; }

        /// <summary>The mode that was asked for last.</summary>
        public MovementMode Target { get; private set; }

        public bool IsSwitching { get; private set; }

        /// <summary>Share of the other movement, 0 to 1. Changes are applied smoothly.</summary>
        public float Strength { get { return strength; } set { strength = Mathf.Clamp01(value); } }

        /// <summary>Where recorded movement starts when Noise is switched on.</summary>
        public RecordedStart NoiseStart { get; set; } = RecordedStart.Conversation;

        public RecordedMotionSource Recorded => recorded;

        /// <summary>Raised when a mode is fully applied.</summary>
        public event Action<MovementMode> Changed;

        /// <summary>Raised when Noise was asked for and there are no recordings.</summary>
        public event Action RecordingsMissing;

        internal ModulationUnit UnitM => unitM;
        internal ModulationUnit UnitL => unitL;

        private void Awake()
        {
            unitM = Wrap(modulationM);
            unitL = Wrap(modulationL);
            if (unitM == null || unitL == null)
            {
                Debug.LogError("[ConditionSwitcher] A Modulation object is missing or incomplete. Build the scene again.", this);
                enabled = false;
                return;
            }
            Apply(MovementMode.Independent);
        }

        private void Update()
        {
            ModulationUnit unit = Unit(Mode);
            if (IsSwitching || unit == null) return;

            // After Refresh the hands follow the own hands until their rotation has settled.
            if (!unit.IsReady && waitFrames < MaximumWaitFrames)
            {
                waitFrames++;
                return;
            }
            unit.Ratio = Mathf.MoveTowards(unit.Ratio, strength, strengthPerSecond * Time.deltaTime);
        }

        /// <summary>Asks for a mode. Instant applies it in this frame, for use while the view is covered.</summary>
        public void SetMode(MovementMode mode, bool instant = false)
        {
            if (!enabled) return;

            Target = mode;
            if (instant && (mode != MovementMode.Noise || recorded.IsLoaded))
            {
                StopAllCoroutines();
                IsSwitching = false;
                Prepare(mode, strength);
                Apply(mode);
                return;
            }
            if (!IsSwitching && mode != Mode) StartCoroutine(Run());
        }

        /// <summary>Starts the mixing again without old poses, after the user was moved or tracking jumped.</summary>
        public void Refresh()
        {
            ModulationUnit unit = Unit(Mode);
            if (unit == null || IsSwitching) return;

            waitFrames = 0;
            unit.Ratio = 0f;
            unit.Prewarm();
        }

        private IEnumerator Run()
        {
            IsSwitching = true;
            while (Target != Mode)
            {
                MovementMode target = Target;
                if (target == MovementMode.Noise && !recorded.IsLoaded)
                {
                    yield return recorded.Load();
                    if (!recorded.IsLoaded)
                    {
                        RecordingsMissing?.Invoke();
                        target = MovementMode.Independent;
                        if (Target == MovementMode.Noise) Target = target;
                    }
                }

                ModulationUnit from = Unit(Mode), to = Unit(target);
                if (from != null) yield return Ramp(from, 0f);

                Prepare(target, 0f);
                for (int frame = 0; to != null && frame < MaximumWaitFrames && !to.IsReady; frame++) yield return null;

                Apply(target);
                if (to != null) yield return Ramp(to, strength);
            }
            IsSwitching = false;
        }

        /// <summary>Starts the chain of a mode at the given ratio. Nothing reads its output yet.</summary>
        private void Prepare(MovementMode mode, float ratio)
        {
            if (mode == MovementMode.Noise)
            {
                recordingRig.SetActive(true);
                recorded.Play(NoiseStart);
            }

            ModulationUnit unit = Unit(mode);
            if (unit == null) return;

            waitFrames = 0;
            unit.Ratio = ratio;
            unit.SetPartner(mode == MovementMode.Mimicry ? userRig : mode == MovementMode.Leading ? agentRig : recordingTargets);
            if (!unit.Root.activeSelf) unit.Root.SetActive(true);
            unit.Prewarm();
        }

        /// <summary>Exchanges the poses that the two bodies read, and switches off what is not needed any more.</summary>
        private void Apply(MovementMode mode)
        {
            bool userIsModulated = mode == MovementMode.Leading || mode == MovementMode.Noise;
            userRaw.enabled = !userIsModulated;
            userModulated.enabled = userIsModulated;
            agentRaw.enabled = mode != MovementMode.Mimicry;
            agentModulated.enabled = mode == MovementMode.Mimicry;

            ModulationUnit unit = Unit(mode);
            if (unit != unitM) unitM.Root.SetActive(false);
            if (unit != unitL) unitL.Root.SetActive(false);
            if (mode != MovementMode.Noise)
            {
                recorded.Stop();
                recordingRig.SetActive(false);
            }

            Mode = mode;
            Changed?.Invoke(mode);
        }

        private IEnumerator Ramp(ModulationUnit unit, float value)
        {
            float change = Mathf.Abs(value - unit.Ratio);
            float speed = change / Mathf.Max(rampSeconds, change / strengthPerSecond);
            while (!Mathf.Approximately(unit.Ratio, value))
            {
                yield return null;
                unit.Ratio = Mathf.MoveTowards(unit.Ratio, value, speed * Time.deltaTime);
            }
        }

        private static ModulationUnit Wrap(GameObject root)
        {
            ModulationUnit unit = root != null ? new ModulationUnit(root) : null;
            return unit != null && unit.IsComplete ? unit : null;
        }

        private ModulationUnit Unit(MovementMode mode)
        {
            if (mode == MovementMode.Independent) return null;
            return mode == MovementMode.Mimicry ? unitM : unitL;
        }
    }
}