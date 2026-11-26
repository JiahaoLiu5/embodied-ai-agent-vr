using System;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>The avatars the user can choose from.</summary>
    [CreateAssetMenu(menuName = "Embodied Agent/Avatar Catalog")]
    public sealed class AgentAvatarCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public string label;
            public GameObject prefab;
            [Tooltip("Humanoid definition of the skeleton of this avatar.")]
            public Avatar definition;
            public Texture2D thumbnail;
        }

        public Entry[] avatars = Array.Empty<Entry>();

        /// <summary>The entry with this id, else the first one.</summary>
        public Entry Find(string id)
        {
            foreach (Entry entry in avatars)
                if (entry.id == id) return entry;
            return avatars.Length > 0 ? avatars[0] : null;
        }
    }
}