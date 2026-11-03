namespace Cthangover.Mods.NpcBarks
{
    using System;
    using Cthangover.Mods.Dialog;
    using Cthangover.Mods.Dialog.Action;
    using Cthangover.Mods.Dialog.Action.Impls;

    /// <summary>Per-run debugger: clocks, breakpoints and stepping cannot hold another NPC's runtime.</summary>
    public sealed partial class BarkRun
    {
        private bool singleStep;
        private int stepLine;
        private int resumeLine;
        private string lastCondition;
        private bool? lastResult;
        public bool DebugPaused { get; private set; }
        public double Remaining => Math.Max(0, remaining);
        public string WaitDescription => pending is ActionWaitCondition waiting ? waiting.ConditionText
            : speech != null ? "speech" : pending != null ? "delay" : Runtime.IsHeld ? "breakpoint" : "running";

        public bool ShouldHold(DialogRuntime runtime, IActionCommand action)
        {
            if (!director.IsObserved) return false;
            var line = action.SourceLine;
            if (resumeLine == line && line != 0) return false;
            resumeLine = 0;
            if (singleStep)
            {
                if (stepLine == 0) stepLine = line;
                if (line == stepLine) return false;
                singleStep = false; DebugPaused = true;
            }
            if (!DebugPaused && !director.HasBreakpoint(Definition.Id, line)) return false;
            DebugPaused = true;
            director.Trace(RunId, "breakpoint/hold line " + line);
            return true;
        }

        /// <summary>Immediate pause also freezes an already running speech/condition wait.</summary>
        public void PauseDebug() { DebugPaused = true; director.Trace(RunId, "pause"); }

        public void ContinueDebug()
        {
            DebugPaused = false; singleStep = false;
            if (Runtime.IsHeld)
            {
                resumeLine = Runtime.CurrentAction?.SourceLine ?? 0;
                Runtime.IsHeld = false; Runtime.Run();
            }
            director.Trace(RunId, "continue");
        }

        /// <summary>Executes one source command. A false live condition stays waiting rather than inventing a successful result.</summary>
        public void StepDebug()
        {
            if (Done) return;
            singleStep = true; stepLine = 0; DebugPaused = false; resumeLine = 0;
            if (Runtime.IsHeld) { Runtime.IsHeld = false; Runtime.Run(); }
            else if (pending != null)
            {
                if (pending is not ActionWaitCondition) remaining = 0;
                Tick(0);
            }
            else Runtime.Run();
            singleStep = false; DebugPaused = !Done;
            director.Trace(RunId, "step line " + (Runtime.CurrentAction?.SourceLine ?? 0));
        }

        /// <summary>Advances only this execution's clock, then pauses; other NPC clocks use normal world time.</summary>
        public void AdvanceDebugClock()
        {
            if (Done || Runtime.IsHeld) return;
            singleStep = true; stepLine = 0; DebugPaused = false;
            Tick(1);
            singleStep = false; DebugPaused = !Done;
            director.Trace(RunId, "clock +1s");
        }

        public BackgroundRunInfo Snapshot()
        {
            var line = Runtime.CurrentAction?.SourceLine ?? 0;
            var currentLabel = "";
            var rows = Definition.Body.Split('\n');
            for (var i = 0; i < Math.Min(line, rows.Length); i++)
                if (rows[i].TrimStart().StartsWith(':')) currentLabel = rows[i].Trim();
            return new BackgroundRunInfo
            {
                RunId = RunId, ScenarioId = Definition.Id, Line = line, Label = currentLabel,
                Paused = DebugPaused || Runtime.IsHeld, Wait = WaitDescription, Remaining = Remaining,
                Text = Text, Speaker = SpeakerId, Body = Definition.Body,
                Roles = new System.Collections.Generic.Dictionary<string, string>(World.Roles),
                Variables = new System.Collections.Generic.Dictionary<string, string>(Runtime.Variables),
                Details = director.Describe(World, pending is ActionWaitCondition waiting ? waiting.ConditionText
                    : Runtime.CurrentAction is ActionIf branch ? branch.ConditionText : Definition.Condition),
            };
        }
    }
}
