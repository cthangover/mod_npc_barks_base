namespace Cthangover.Mods.NpcBarks
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public partial class BarkDirector
    {
        private readonly Random variationRandom = new();

        private void ScanNearby(List<BarkDefinition> nearby)
        {
            foreach (var definition in nearby.Where(d => string.IsNullOrWhiteSpace(d.Variation))) TryStart(definition, null);
            foreach (var group in nearby.Where(d => !string.IsNullOrWhiteSpace(d.Variation)).GroupBy(d => d.Map+"|"+d.Variation))
            {
                if (!BarkMemory.Data.Variations.TryGetValue(group.Key, out var state))
                    BarkMemory.Data.Variations[group.Key] = state = new BarkVariationState();
                if (state.Cooldown > 0) continue;
                var definitionsById = group.ToDictionary(d => d.Id);
                foreach (var id in state.Order(definitionsById.Keys, variationRandom))
                {
                    if (!TryStart(definitionsById[id], null)) continue;
                    state.Started(id);
                    break;
                }
            }
        }

        /// <summary>Applies a pause to the whole variation group, including after interruption.</summary>
        public void PauseVariation(BarkDefinition definition, double seconds)
        {
            if (string.IsNullOrWhiteSpace(definition.Variation)) return;
            var key = definition.Map+"|"+definition.Variation;
            if (!BarkMemory.Data.Variations.TryGetValue(key, out var state))
                BarkMemory.Data.Variations[key] = state = new BarkVariationState();
            state.Cooldown = Math.Max(state.Cooldown, seconds);
        }
    }
}
