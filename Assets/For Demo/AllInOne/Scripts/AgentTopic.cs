using System;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>One debate topic: what the boards show, the order of the warm-up, and the instructions of the agent.</summary>
    [CreateAssetMenu(menuName = "Embodied Agent/Topic")]
    public sealed class AgentTopic : ScriptableObject
    {
        public string id;
        [Tooltip("Letter of the topic in the names of the study scenes.")]
        public string studyLetter;
        public string title;
        [TextArea] public string question;
        public string userPosition;
        public string agentPosition;
        public GameObject warmUpBoard;
        public GameObject instructionBoard;
        [Tooltip("Animator states the agent plays in the warm-up, in order.")]
        public string[] warmUpActions = Array.Empty<string>();
        [Tooltip("System instructions of the agent.")]
        public TextAsset instructions;
    }
}