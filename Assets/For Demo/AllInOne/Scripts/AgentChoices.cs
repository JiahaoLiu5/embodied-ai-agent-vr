namespace EmbodiedAgent.AllInOne
{
    /// <summary>What the user has chosen. Everything except the avatar starts with its default when the app starts.</summary>
    public sealed class AgentChoices
    {
        public AgentAvatarCatalog.Entry Avatar { get; set; }

        public MovementMode Mode { get; set; } = MovementMode.Independent;

        /// <summary>Share of the other movement, 0 to 1.</summary>
        public float Strength { get; set; } = ConditionSwitcher.DefaultStrength;

        public bool IsSession { get; set; }

        public AgentTopic Topic { get; set; }

        public SessionPace Pace { get; set; } = SessionPace.Study;
    }
}