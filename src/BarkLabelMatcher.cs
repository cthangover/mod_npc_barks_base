namespace Cthangover.Mods.NpcBarks
{
    /// <summary>Named predicate keeps block matching independent of closures over a changing parser context.</summary>
    internal sealed class BarkLabelMatcher
    {
        private readonly string label;
        internal BarkLabelMatcher(string label) { this.label = ":" + label; }
        internal bool Matches(string line) => line.Trim() == label;
    }
}
