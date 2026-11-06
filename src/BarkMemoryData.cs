namespace Cthangover.Mods.NpcBarks
{
    using System.Collections.Generic;
    /// <summary>Only stable completion and remaining cooldown persist; transient speech events are never replayed on load.</summary>
    public sealed class BarkMemoryData
    {
        public HashSet<string> Completed { get; set; } = new();
        public Dictionary<string, double> Cooldowns { get; set; } = new();
        /// <summary>Unplayed alternatives and group pauses survive map changes and saved-game loads.</summary>
        public Dictionary<string, BarkVariationState> Variations { get; set; } = new();
    }
}
