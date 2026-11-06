namespace Cthangover.Mods.NpcBarks
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>A saved shuffle bag. Ineligible alternatives remain available; only a started run consumes its entry.</summary>
    public sealed class BarkVariationState
    {
        public List<string> Remaining { get; set; } = new();
        public string Previous { get; set; }
        public double Cooldown { get; set; }

        /// <summary>Returns this round's order, excluding removed definitions and avoiding a boundary repeat.</summary>
        public IReadOnlyList<string> Order(IEnumerable<string> available, Random random)
        {
            var ids = available.Distinct(StringComparer.Ordinal).ToList();
            Remaining.RemoveAll(id => !ids.Contains(id));
            if (Remaining.Count == 0)
            {
                Remaining.AddRange(ids);
                for (var i = Remaining.Count-1; i > 0; i--)
                {
                    var next = random.Next(i+1);
                    (Remaining[i], Remaining[next]) = (Remaining[next], Remaining[i]);
                }
                if (Remaining.Count > 1 && Remaining[0] == Previous)
                    (Remaining[0], Remaining[1]) = (Remaining[1], Remaining[0]);
            }
            return Remaining.ToArray();
        }

        /// <summary>Called after actor reservation succeeds, never just because a scenario was considered.</summary>
        public void Started(string id)
        {
            Remaining.Remove(id);
            Previous = id;
        }
    }
}
