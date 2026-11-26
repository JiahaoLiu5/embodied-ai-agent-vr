using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>Where recorded movement starts.</summary>
    public enum RecordedStart
    {
        /// <summary>At the beginning of the recording, as in the study.</summary>
        Session,
        /// <summary>Inside the conversation part, without long rests, so that the movement can be seen at once.</summary>
        Conversation
    }

    /// <summary>
    /// Moves three targets along a recorded head and hand movement. The files are read in the background, one of them is
    /// picked at random, and playback loops with soft transitions.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class RecordedMotionSource : MonoBehaviour
    {
        private const float PreloadDelay = 2f;
        private const float ConversationStart = 170f;
        private const float ConversationReserve = 120f;
        private const float ActiveTravel = 0.10f;
        private const int LongestRest = 3;

        [SerializeField] private TextAsset[] recordings;
        [SerializeField] private Transform playbackSpace;
        [Tooltip("Head, left hand, right hand.")]
        [SerializeField] private Transform[] targets;
        [SerializeField] private float blendSeconds = 0.5f;

        private readonly List<MotionRecording> loaded = new List<MotionRecording>();
        private MotionRecording current;
        private RecordedStart start;
        private float time, oldTime, blend = 1f;
        private int cursor, oldCursor;
        private bool isLoading;

        public bool IsLoaded => !isLoading && loaded.Count > 0;

        public bool IsPlaying { get; private set; }

        public bool Paused { get; set; }

        /// <summary>Position in the recording, in seconds.</summary>
        public float Elapsed => time;

        /// <summary>For automated tests.</summary>
        internal TextAsset[] Recordings { get { return recordings; } set { recordings = value; loaded.Clear(); } }

        internal IReadOnlyList<MotionRecording> Loaded => loaded;

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(PreloadDelay);
            yield return Load();
        }

        /// <summary>
        /// Waits until all recordings are read. Reading goes on when the caller is stopped, so a later call can wait for it.
        /// </summary>
        public IEnumerator Load()
        {
            if (!isLoading && loaded.Count == 0 && recordings != null) StartCoroutine(Read());
            while (isLoading) yield return null;
        }

        private IEnumerator Read()
        {
            isLoading = true;
            foreach (TextAsset file in recordings)
            {
                if (file == null) continue;
                string text = file.text;
                yield return null;

                Task<MotionRecording> parsing = Task.Run(() => MotionRecording.Parse(text));
                while (!parsing.IsCompleted) yield return null;

                if (parsing.Status == TaskStatus.RanToCompletion && parsing.Result.Count > 1) loaded.Add(parsing.Result);
                else Debug.LogWarning("[RecordedMotionSource] Could not read " + file.name + ".", this);
            }
            isLoading = false;
        }

        /// <summary>Starts a recording, by default a random one.</summary>
        public void Play(RecordedStart from, int index = -1)
        {
            if (!IsLoaded) return;

            current = loaded[index >= 0 ? index % loaded.Count : Random.Range(0, loaded.Count)];
            start = from;
            time = from == RecordedStart.Session ? 0f : RandomConversationTime();
            blend = 1f;
            cursor = 0;
            IsPlaying = true;
            Paused = false;
            Write(current.At(time, ref cursor));
        }

        public void Stop()
        {
            IsPlaying = false;
            Paused = false;
        }

        private void LateUpdate()
        {
            if (!IsPlaying || Paused) return;

            float step = Time.deltaTime;
            time += step;
            oldTime += step;
            if (blend < 1f) blend = Mathf.MoveTowards(blend, 1f, step / Mathf.Max(0.01f, blendSeconds));
            else SkipIfNeeded();

            MotionPose pose = current.At(time, ref cursor);
            if (blend < 1f)
            {
                MotionPose old = current.At(oldTime, ref oldCursor);
                pose = MotionPose.Lerp(old, pose, Mathf.SmoothStep(0f, 1f, blend));
            }
            Write(pose);
        }

        private void SkipIfNeeded()
        {
            if (time >= current.Duration - blendSeconds)
            {
                JumpTo(start == RecordedStart.Session ? 0f : RandomConversationTime());
            }
            else if (start == RecordedStart.Conversation)
            {
                int second = (int)time;
                int rest = current.RestingSeconds(second);
                if (rest > LongestRest) JumpTo(Mathf.Min(second + rest, current.Duration - 2f * blendSeconds));
            }
        }

        private void JumpTo(float newTime)
        {
            oldTime = time;
            oldCursor = cursor;
            time = newTime;
            cursor = 0;
            blend = 0f;
        }

        private float RandomConversationTime()
        {
            float latest = Mathf.Max(ConversationStart, current.Duration - ConversationReserve);
            int second = (int)Random.Range(ConversationStart, latest);
            return Mathf.Min(current.NextActiveSecond(second, ActiveTravel), Mathf.Max(0f, current.Duration - 2f));
        }

        private void Write(in MotionPose pose)
        {
            Write(targets[0], pose.head, pose.headRotation);
            Write(targets[1], pose.left, pose.leftRotation);
            Write(targets[2], pose.right, pose.rightRotation);
        }

        private void Write(Transform target, Vector3 position, Quaternion rotation)
        {
            target.SetPositionAndRotation(playbackSpace.TransformPoint(position), playbackSpace.rotation * rotation);
        }
    }
}