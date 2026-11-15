namespace Cthangover.Mods.NpcBarks
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Core.Events;
    using Cthangover.Core.Localization;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Dialog;
    using Cthangover.Mods.Dialog.Action;
    using Cthangover.Mods.Dialog.Action.Impls;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>One common DialogRuntime hosted without a modal box. Waits, roles and utterances belong to this run only.</summary>
    public sealed partial class BarkRun : IScenarioExecutionHost, IDialogDebugger
    {
        private readonly BarkDirector director;
        private readonly Dictionary<string, string> looks = new();
        private IActionCommand pending;
        private double remaining;
        private ActionText speech;
        private string utterance;

        public BarkDefinition Definition { get; }
        public BarkWorldContext World { get; }
        public DialogRuntime Runtime { get; } = new();
        public string RunId { get; } = Guid.NewGuid().ToString("N");
        public int Depth { get; }
        public BarkSpeechEvent SourceSpeech { get; }
        public bool Done { get; private set; }
        public string SpeakerId { get; private set; }
        public string Text => speech == null ? null : ResolveText(speech);

        public BarkRun(BarkDirector director, BarkDefinition definition, BarkWorldContext world, BarkSpeechEvent source)
        {
            this.director = director; Definition = definition; World = world; SourceSpeech = source;
            Depth = source?.Depth ?? 0;
            Runtime.Host = this;
            Runtime.ExecutionDebugger = this;
            Runtime.Dialog = BarkProgram.Compile(definition);
        }

        /// <summary>Caller reserves actors and registers this run before any action can emit a speech event.</summary>
        public void Start()
        {
            Runtime.SetDialogQueueAndRun(null, Runtime.Dialog.Queue, null);
            Runtime.Run();
        }

        /// <summary>Advances only the pending action that still belongs to this runtime.</summary>
        public void Tick(double delta)
        {
            if (Done || Runtime.IsHeld || DebugPaused) return;
            foreach (var id in Definition.Actors.Values)
                if (!World.Space.TryPosition(id, out _) || !World.Space.Owns(id)) { Abort(); return; }
            ApplyLooks();
            if (pending == null) return;
            if (!ReferenceEquals(Runtime.CurrentAction, pending)) { pending = null; return; }
            remaining -= delta;
            if (pending is ActionWaitCondition condition)
            {
                var holds = EvaluateCondition(condition.ConditionText);
                if (!holds && remaining > 0) return;
                pending = null;
                var target = holds ? condition.TrueTarget : condition.TimeoutTarget;
                if (string.IsNullOrEmpty(target)) Runtime.Next();
                else { Runtime.TryGoTo(target, true); Runtime.Next(); }
                Runtime.Run();
                return;
            }
            if (remaining > 0) return;
            pending = null;
            if (speech != null) { Emit("speech.finished"); HideSpeech(); }
            Runtime.Next(); Runtime.Run();
        }

        public bool EvaluateCondition(string condition)
        {
            var result = ScenarioCondition.EvaluateWithServices(condition, World.Scope, World);
            if (condition != lastCondition || result != lastResult)
            {
                director.Trace(RunId, "condition " + condition + " = " + result);
                lastCondition = condition; lastResult = result;
            }
            return result;
        }

        public void ShowText(ActionText action, DialogRuntime runtime)
        {
            HideSpeech();
            var role = action.Speaker ?? Definition.Anchor;
            if (!World.Roles.TryGetValue(role, out var id)) throw new InvalidOperationException("unknown speaker " + role);
            SpeakerId = id; speech = action; utterance = Guid.NewGuid().ToString("N");
            action.WaitType = WaitType.WaitEvent;
            remaining = action.Duration ?? Math.Clamp(1.2 + ResolveText(action).Length / 18.0, 2, 8);
            pending = action;
            World.Space.Show(this);
            Emit("speech.started");
        }

        public void Delay(ActionDelay action, DialogRuntime runtime)
        {
            HideSpeech(); pending = action;
            action.WaitType = WaitType.WaitEvent;
            remaining = Math.Max(0, action.WaitTime);
        }

        public void WaitCondition(ActionWaitCondition action, DialogRuntime runtime)
        {
            HideSpeech(); pending = action; remaining = action.Timeout;
        }

        /// <summary>Holds an owned actor's attention until this run releases it.</summary>
        public void Look(string who, string target)
        {
            if (!Definition.Actors.ContainsKey(who ?? "")) throw new InvalidOperationException("npc.look needs an owned actor role");
            looks[who] = target;
            ApplyLooks();
        }

        public void Finished(bool aborted)
        {
            if (Done) return;
            if (aborted && speech != null) Emit("speech.interrupted");
            director.Trace(RunId, aborted ? "interrupted" : "completed");
            Done = true; pending = null; HideSpeech(); looks.Clear();
            foreach (var id in Definition.Actors.Values)
            {
                World.Space.Hold(id, false);
            }
            if (!aborted) BarkMemory.Data.Completed.Add(Definition.Id);
            BarkMemory.Data.Cooldowns[Definition.Id] = aborted ? 2 : Definition.Cooldown;
            director.PauseVariation(Definition, aborted ? 2 : Definition.Cooldown);
            GameLogger.Log("NPC_BARKS", Definition.Id + " run=" + RunId + (aborted ? " interrupted" : " completed"));
        }

        public void Abort() { if (!Done) Runtime.Abort(); }

        private string ResolveText(ActionText action)
        {
            var text = action.LocaleKey == null ? action.Text : GodotTranslationProvider.Instance.Get(action.LocaleKey) ?? action.FallbackText;
            return action.UseProcessText ? Runtime.ProcessText(text) : text;
        }

        private void Emit(string phase)
        {
            if (speech?.Cue == null) return;
            ModEventBus.Publish(new BarkSpeechEvent
            {
                SessionId = director.SessionId, At = director.Clock,
                SenderModId = "npc_barks_base", EventId = Guid.NewGuid().ToString("N"), RunId = RunId,
                UtteranceId = utterance, Speaker = SpeakerId, Map = Definition.Map,
                Cue = speech.Cue, Phase = phase, Depth = Depth + 1,
            });
        }

        private void HideSpeech()
        {
            World.Space.Hide(this);
            speech = null; SpeakerId = null;
        }

        /// <summary>Resolved addressee is shared by speech labels, attention and optional positional effects.</summary>
        public string AddresseeId => SpeakerId != null && looks.TryGetValue(SpeakerRole(), out var target) ? World.Resolve(target) : null;
        public string UtteranceId => utterance;
        private string SpeakerRole() { foreach (var pair in World.Roles) if (pair.Value == SpeakerId) return pair.Key; return ""; }
        private void ApplyLooks()
        {
            foreach (var pair in looks) World.Space.Look(World.Resolve(pair.Key), World.Resolve(pair.Value));
        }
    }
}
