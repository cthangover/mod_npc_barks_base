namespace Cthangover.Mods.NpcBarks.Tests
{
    using System;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Scenes;
    using Cthangover.Mods.Dialog;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Mods.Rpg.Model;
    using Godot;

    /// <summary>Author-facing scenario contracts: addressed patches, ordinary branching and per-call spatial roles.</summary>
    public sealed class BarkProgramTests
    {
        private static BarkDefinition Definition(string body) => BarkDefinition.Parse("fixture/test",
            "scene: map\nchannel: ambient\nactor.a: map/a\n---\n" + body);

        [ModTest("barks/dsl_branch_and_addressed_patch")]
        static void Patch(ModTestContext ctx)
        {
            var definition = Definition(":start\ngoto -> :answer\n:answer\nset outcome old\nend\n");
            definition.Body = BarkProgram.Patch(definition.Body, "patch_of: fixture/test\nreplace: answer\n---\nset outcome new\nend\n");
            var queue = BarkProgram.Compile(definition);
            var runtime = new DialogRuntime { Dialog = queue };
            runtime.SetDialogQueueAndRun(null, queue.Queue, null); runtime.Run();
            ModAssert.Equal("new", runtime.GetVariable("outcome"), "the normal runtime executes the replaced block");
            ModAssert.True(definition.Body.Contains("goto -> :answer"), "neighboring source block survives");
        }

        [ModTest("barks/rejects_dangling_target_and_unknown_command")]
        static void RejectInvalid(ModTestContext ctx)
        {
            var missing = false; var unknown = false;
            try { BarkProgram.Compile(Definition("goto -> :missing\nend")); } catch (FormatException) { missing = true; }
            try { BarkProgram.Compile(Definition("txet \"typo\"\nend")); } catch (FormatException) { unknown = true; }
            ModAssert.True(missing, "dangling labels are rejected before any effects");
            ModAssert.True(unknown, "misspelled commands are not silently skipped");
        }

        [ModTest("barks/same_ast_resolves_independent_live_roles")]
        static void IndependentContexts(ModTestContext ctx)
        {
            var map = new RpgMap { ID = "map", Width = 20, Height = 10 };
            var session = new RpgWorldSession(map, FindObject, new[] {
                new RpgPlacement { ID = "map/a", Object = "npc", X = 2, Y = 2 },
                new RpgPlacement { ID = "map/b", Object = "npc", X = 15, Y = 2 } }, Evaluate);
            session.Relocate(new Vector2I(2, 3), Cthangover.Mods.Rpg.RpgDirection.Down);
            var first = new BarkWorldContext(session, Definition("end"));
            var other = BarkDefinition.Parse("other", "scene: map\nchannel: ambient\nactor.a: map/b\n---\nend");
            var second = new BarkWorldContext(session, other);
            const string condition = "near('a', 'player', 3)";
            ModAssert.True(ScenarioCondition.EvaluateWithServices(condition, first.Scope, first), "first actor is nearby");
            ModAssert.False(ScenarioCondition.EvaluateWithServices(condition, second.Scope, second), "same cached expression reads the second role");
            session.Relocate(new Vector2I(15, 3), Cthangover.Mods.Rpg.RpgDirection.Down);
            ModAssert.True(ScenarioCondition.EvaluateWithServices(condition, second.Scope, second), "cached expression reads current positions");
        }
        private static RpgObject FindObject(string id) => new RpgObject { ID = "npc", Form = RpgObjectForm.Actor };
        private static bool Evaluate(string condition, string scope) => true;
    }
}
