namespace Cthangover.Mods.NpcBarks.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;

    public sealed class BarkVariationStateTests
    {
        [ModTest("barks/variation_uses_all_ten_before_repeating")]
        static void FairRounds(ModTestContext ctx)
        {
            var ids = Enumerable.Range(0,10).Select(i => "market/"+i).ToArray();
            var state = new BarkVariationState();
            var random = new Random(83);
            string last = null;
            for (var round = 0; round < 8; round++)
            {
                var seen = new HashSet<string>();
                for (var i = 0; i < 10; i++)
                {
                    var next = state.Order(ids, random)[0];
                    ModAssert.True(next != last && seen.Add(next), "no repetition within a round or across its boundary");
                    state.Started(next); last = next;
                }
                ModAssert.Equal(10, seen.Count, "all alternatives execute");
            }
        }

        [ModTest("barks/variation_json_keeps_unplayed_choices_and_group_pause")]
        static void SaveRound(ModTestContext ctx)
        {
            var state = new BarkVariationState { Cooldown = 19.5 };
            var ids = new[] { "a", "b", "c", "d" };
            var random = new Random(17);
            var order = state.Order(ids,random).ToArray();
            ModAssert.True(order.SequenceEqual(state.Order(ids,random)), "merely considering a blocked candidate does not consume it");
            state.Started(order[0]);
            var memory = new BarkMemoryData(); memory.Variations["market|shopper"] = state;
            var restored = JsonSerializer.Deserialize<BarkMemoryData>(JsonSerializer.Serialize(memory)).Variations["market|shopper"];
            ModAssert.True(restored.Order(ids,random).SequenceEqual(order.Skip(1)), "save/load continues the same unfinished round");
            ModAssert.Equal(order[0], restored.Previous, "last started scenario survives");
            ModAssert.CloseTo(19.5, restored.Cooldown, what: "shared cooldown survives");
            var definition = BarkDefinition.Parse("test", "scene: market\nchannel: ambient\nactor.a: market/a\nvariation: shopper\n---\nend");
            ModAssert.Equal("shopper", definition.Variation, "scenario header opts into fair rotation");
        }
    }
}
