using System;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    public enum SessionPace { Study, Quick }

    /// <summary>The steps of the full session in order, with narration, texts, boards, and times.</summary>
    [CreateAssetMenu(menuName = "Embodied Agent/Session Script")]
    public sealed class AgentSessionScript : ScriptableObject
    {
        public enum Board { None, WarmUp, Instruction }

        public enum Kind
        {
            /// <summary>Lasts as long as the pace says.</summary>
            Wait,
            /// <summary>Lasts as long as the narration.</summary>
            ClipLength,
            /// <summary>Lasts as long as the movements of the agent.</summary>
            WarmUp,
            /// <summary>The timed conversation.</summary>
            Conversation
        }

        [Serializable]
        public sealed class Step
        {
            public string id;
            public string title;
            public string hint;
            public AudioClip clip;
            [TextArea] public string caption;
            public float studySeconds;
            public float quickSeconds;
            public Board board;
            public Kind kind;

            public float Seconds(SessionPace pace) => pace == SessionPace.Study ? studySeconds : quickSeconds;
        }

        public Step[] steps = Array.Empty<Step>();

        [Header("Time reminders of the conversation")]
        [Tooltip("Seconds that are left when a reminder appears.")]
        public float[] reminders = { 180f, 60f };
        public GameObject[] reminderBoards = Array.Empty<GameObject>();
        public float reminderSeconds = 3f;
    }
}