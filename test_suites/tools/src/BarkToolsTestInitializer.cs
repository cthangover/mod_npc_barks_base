namespace Cthangover.Mods.NpcBarks.ToolTests
{
    using Cthangover.Core.Mods;
    using Godot;
    /// <summary>Opt-in end-to-end authoring test; no test driver is mounted in normal play.</summary>
    public sealed class BarkToolsTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string id) { }
        public void OnModResourcesReady()
        {
            if (queued) return;
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg == "--itest=npc_barks_tools")
            { queued = true; ModRoot.AddDeferred(new BarkToolsTestDriver()); return; }
        }
    }
}
