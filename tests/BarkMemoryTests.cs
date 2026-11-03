namespace Cthangover.Mods.NpcBarks.Tests
{
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Settings;

    /// <summary>Saved completion and remaining cooldown survive both JSON and the actual mod slot contract.</summary>
    public sealed class BarkMemoryTests
    {
        [ModTest("barks/history_json_and_slot_round_trip")]
        static void RoundTrip(ModTestContext ctx)
        {
            var memory = new BarkMemory(); memory.ResetForNewGame();
            try
            {
                BarkMemory.Data.Completed.Add("jrp/company");
                BarkMemory.Data.Cooldowns["jrp/company"] = 12.75;
                var restored = JsonSerializer.Deserialize<BarkMemoryData>(JsonSerializer.Serialize(BarkMemory.Data));
                ModAssert.True(restored.Completed.Contains("jrp/company"), "completed key survives JSON");
                ModAssert.CloseTo(12.75, restored.Cooldowns["jrp/company"], what: "remaining seconds survive JSON");
                var slot = new ModSlot(); memory.OnSave(slot);
                var json = JsonSerializer.Serialize(slot);
                var roundTrip = JsonSerializer.Deserialize<ModSlot>(json);
                memory.ResetForNewGame();
                ModAssert.True(memory.OnLoad(roundTrip, out var error), "slot accepts its schema");
                ModAssert.True(BarkMemory.Data.Completed.Contains("jrp/company"), "completion restored through OnLoad");
                ModAssert.CloseTo(12.75, BarkMemory.Data.Cooldowns["jrp/company"], what: "cooldown restored through OnLoad");
            }
            finally { memory.ResetForNewGame(); }
        }
    }
}
