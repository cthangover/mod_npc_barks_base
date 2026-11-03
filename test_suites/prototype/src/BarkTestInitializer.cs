namespace Cthangover.Mods.NpcBarks.Prototype
{
    using Cthangover.Core.Mods;
    using Godot;
    /// <summary>Explicit opt-in only: the fixture driver never runs in a player's game.</summary>
    public sealed class BarkTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string id) { }
        public void OnModResourcesReady()
        {
            if (queued) return;
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg == "--itest=npc_barks") { queued = true; ModRoot.AddDeferred(new BarkTestDriver()); return; }
        }
    }
}
