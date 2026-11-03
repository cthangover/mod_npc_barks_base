namespace Cthangover.Mods.NpcBarks
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Settings;

    /// <summary>Owns the mod save slot and resets live executions when state is replaced in-process.</summary>
    public sealed class BarkMemory : IModSaveable, IResettable
    {
        public static BarkMemoryData Data { get; private set; } = new();
        public string Id => "npc_barks_base";
        public void ResetForNewGame() { BarkDirector.Current?.Clear(); Data = new(); }
        public void OnSave(ModSlot slot)
        {
            if (BarkDirector.Current != null)
                foreach (var run in BarkDirector.Current.Runs)
                    if (!run.Done) Data.Cooldowns[run.Definition.Id] = Math.Max(2, Data.Cooldowns.GetValueOrDefault(run.Definition.Id));
            slot.Data["History"] = new DataBlob(1, Data);
        }
        public bool OnLoad(ModSlot slot, out string error)
        {
            error = null;
            ResetForNewGame();
            if (slot?.Data != null && slot.Data.TryGetValue("History", out var blob))
                Data = blob.ReadJson<BarkMemoryData>() ?? new();
            Data.Completed ??= new(); Data.Cooldowns ??= new();
            return true;
        }
    }
}
