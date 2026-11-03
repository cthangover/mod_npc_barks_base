namespace Cthangover.Mods.NpcBarks
{
    using System;
    using Cthangover.Core.Actions;
    using Cthangover.Mods.Dialog;

    /// <summary>The standard action registry dispatches attention to this particular ambient execution.</summary>
    public sealed class NpcLookAction : IScenarioAction
    {
        public string Name => "npc.look";
        public void Run(IActionContext context)
        {
            if (context is not IHostedScenarioContext hosted || hosted.Host is not BarkRun run)
                throw new InvalidOperationException("npc.look requires an ambient scenario host");
            run.Look(context.GetParam("who"), context.GetParam("target"));
        }
    }
}
