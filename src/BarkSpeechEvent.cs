namespace Cthangover.Mods.NpcBarks
{
    using Cthangover.Core.Events;

    /// <summary>One non-localized speech occurrence, routed by the director on the next update, never recursively.</summary>
    [EventKey("npc_speech")]
    public sealed class BarkSpeechEvent : ModEvent
    {
        public string EventId { get; init; }
        public string SessionId { get; init; }
        public double At { get; init; }
        public string RunId { get; init; }
        public string UtteranceId { get; init; }
        public string Speaker { get; init; }
        public string Map { get; init; }
        public string Cue { get; init; }
        public string Phase { get; init; }
        public int Depth { get; init; }
    }
}
