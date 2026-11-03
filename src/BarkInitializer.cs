namespace Cthangover.Mods.NpcBarks
{
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Scenes.Parsing;
    /// <summary>Installs only shared leaves and the director; story content is provided by the game's scenarios.</summary>
    public sealed class BarkInitializer : IModInitializer
    {
        private static bool installed;
        public void OnModLoaded(string id) { }
        public void OnModResourcesReady()
        {
            if (installed) return;
            installed = true;
            LeafParserRegistry.RegisterFirst(new BarkLeafParser(), "npc_barks_base");
            AppServices.Current.RegisterResettable(new BarkMemory());
            ModRoot.AddDeferred(new BarkDirector { Name = "NpcBarks" });
        }
    }
}
