namespace Cthangover.Mods.NpcBarks.Tests
{
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Dialog;

    /// <summary>Validation is transactional and does not write source files or replace the live catalog.</summary>
    public sealed class BarkAuthoringTests
    {
        [ModTest("barks/authoring_rejects_source_and_new_patch_without_mutating_catalog")]
        static void RejectWithoutMutation(ModTestContext context)
        {
            var tools = BackgroundScenarios.Provider;
            ModAssert.True(tools != null, "authoring provider is registered");
            ModAssert.False(tools.HasBreakpoint(null, 1), "an empty catalog has no breakpoint without a scenario ID");
            var catalog = tools.Catalog();
            ModAssert.True(catalog.Count > 0, "game supplies ambient source");
            var entry = catalog[0]; var before = entry.Body;
            var bad = entry.Source + "\ngoto -> :missing_authoring_target\n";
            ModAssert.False(tools.Validate(entry.ModId, entry.Path, bad, out var error), "dangling target is rejected before Save");
            ModAssert.True(!string.IsNullOrEmpty(error), "validation explains the error");
            var patch = "patch_of: " + entry.Id + "\nreplace: missing_authoring_label\n---\nend\n";
            ModAssert.False(tools.Validate(entry.ModId, "patches/scenarios/never_written_authoring_probe.scenario", patch, out error),
                "a new, not-yet-written patch is also validated against its target");
            var after = tools.Catalog();
            ModAssert.Equal(before, after[0].Body, "failed proposals leave the live compiled source unchanged");
            ModAssert.True(tools.Validate(entry.ModId, entry.Path, entry.Source, out error), "unchanged source remains valid");
        }
    }
}
