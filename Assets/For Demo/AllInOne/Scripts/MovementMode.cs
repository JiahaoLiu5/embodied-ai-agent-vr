namespace EmbodiedAgent.AllInOne
{
    /// <summary>The four movement transformations.</summary>
    public enum MovementMode
    {
        /// <summary>No transformation.</summary>
        Independent,
        /// <summary>The user's avatar carries a share of the agent's movement.</summary>
        Leading,
        /// <summary>The agent carries a share of the user's mirrored movement.</summary>
        Mimicry,
        /// <summary>The user's avatar carries a share of recorded movement that is unrelated to the conversation.</summary>
        Noise
    }
}