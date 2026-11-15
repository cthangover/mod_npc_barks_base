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

namespace Cthangover.Mods.NpcBarks
{
    /// <summary>
    /// One common DialogRuntime hosted without a modal box. Waits, roles and utterances belong to this run
    /// only.
    /// </summary>
    public sealed partial class BarkRun : IScenarioExecutionHost, IDialogDebugger
    {
        private readonly BarkDirector director;
        private readonly Dictionary<string, string> looks = new();
        private IActionCommand pending;
        private double remaining;
        private ActionText speech;
        private string utterance;

        /// <summary>
        /// Compiled scenario definition retained for this execution even when the author catalogue is reloaded.
        /// </summary>
        public BarkDefinition Definition { get; }

        /// <summary>
        /// Per-run role and spatial services used by actions and conditions; no native actor is captured by the
        /// compiled program.
        /// </summary>
        public BarkWorldContext World { get; }

        /// <summary>
        /// Ordinary dialog runtime hosted by this nonmodal execution, with this object providing actions and
        /// debugger services.
        /// </summary>
        public DialogRuntime Runtime { get; } = new();

        /// <summary>
        /// Transient stable execution identity used by queued speech events and studio commands.
        /// </summary>
        public string RunId { get; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Source reaction depth used to bound recursive speech-trigger chains across director updates.
        /// </summary>
        public int Depth { get; }

        /// <summary>
        /// Borrowed initiating speech occurrence, or null for an ordinary nearby/author-started scenario.
        /// </summary>
        public BarkSpeechEvent SourceSpeech { get; }

        /// <summary>
        /// Whether completion/abort has released participant holds and published the final cooldown/completion
        /// state.
        /// </summary>
        public bool Done { get; private set; }

        /// <summary>
        /// Logical current speaker identity; cleared when the pending text action ends.
        /// </summary>
        public string SpeakerId { get; private set; }

        /// <summary>
        /// Current localized and optionally processed text, resolved through the existing dialog runtime rather
        /// than cached in the AST.
        /// </summary>
        public string Text
        {
            get
            {
                return speech == null ? null : ResolveText(speech);
            }
        }

        /// <summary>
        /// Binds one ordinary dialog runtime to its director, compiled definition and per-run spatial context;
        /// callers reserve actors before Start.
        /// </summary>
        public BarkRun(BarkDirector director, BarkDefinition definition, BarkWorldContext world, BarkSpeechEvent source)
        {
            this.director = director;
            Definition = definition;
            World = world;
            SourceSpeech = source;
            Depth = source?.Depth ?? 0;
            Runtime.Host = this;
            Runtime.ExecutionDebugger = this;
            Runtime.Dialog = BarkProgram.Compile(definition);
        }

        /// <summary>
        /// Caller reserves actors and registers this run before any action can emit a speech event.
        /// </summary>
        public void Start()
        {
            Runtime.SetDialogQueueAndRun(null, Runtime.Dialog.Queue, null);
            Runtime.Run();
        }

        /// <summary>
        /// Advances only the pending action that still belongs to this runtime.
        /// </summary>
        public void Tick(double delta)
        {
            if (Done || Runtime.IsHeld || DebugPaused)
            {
                return;
            }

            foreach (var id in Definition.Actors.Values)
            {
                if (!World.Space.TryPosition(id, out _) || !World.Space.Owns(id))
                {
                    Abort();
                    return;
                }
            }

            ApplyLooks();
            if (pending == null)
            {
                return;
            }

            if (!ReferenceEquals(Runtime.CurrentAction, pending))
            {
                pending = null;
                return;
            }

            remaining -= delta;
            if (pending is ActionWaitCondition condition)
            {
                var holds = EvaluateCondition(condition.ConditionText);
                if (!holds && remaining > 0)
                {
                    return;
                }

                pending = null;
                var target = holds ? condition.TrueTarget : condition.TimeoutTarget;
                if (string.IsNullOrEmpty(target))
                {
                    Runtime.Next();
                }
                else
                {
                    Runtime.TryGoTo(target, true);
                    Runtime.Next();
                }

                Runtime.Run();
                return;
            }

            if (remaining > 0)
            {
                return;
            }

            pending = null;
            if (speech != null)
            {
                Emit("speech.finished");
                HideSpeech();
            }

            Runtime.Next();
            Runtime.Run();
        }

        /// <summary>
        /// Evaluates with this run’s role context and traces only changes in condition text/result for the
        /// author debugger.
        /// </summary>
        public bool EvaluateCondition(string condition)
        {
            var result = ScenarioCondition.EvaluateWithServices(condition, World.Scope, World);
            if (condition != lastCondition || result != lastResult)
            {
                director.Trace(RunId, "condition " + condition + " = " + result);
                lastCondition = condition;
                lastResult = result;
            }

            return result;
        }

        /// <summary>
        /// Replaces prior speech, assigns a fresh occurrence identity and independent action wait, then
        /// notifies the active spatial presenter before emitting the cue.
        /// </summary>
        public void ShowText(ActionText action, DialogRuntime runtime)
        {
            HideSpeech();
            var role = action.Speaker ?? Definition.Anchor;
            if (!World.Roles.TryGetValue(role, out var id))
            {
                throw new InvalidOperationException("unknown speaker " + role);
            }

            SpeakerId = id;
            speech = action;
            utterance = Guid.NewGuid().ToString("N");
            action.WaitType = WaitType.WaitEvent;
            remaining = action.Duration ?? Math.Clamp(1.2 + ResolveText(action).Length / 18.0, 2, 8);
            pending = action;
            World.Space.Show(this);
            Emit("speech.started");
        }

        /// <summary>
        /// Hides active speech and schedules only this runtime’s pending delay with nonnegative remaining
        /// seconds.
        /// </summary>
        public void Delay(ActionDelay action, DialogRuntime runtime)
        {
            HideSpeech();
            pending = action;
            action.WaitType = WaitType.WaitEvent;
            remaining = Math.Max(0, action.WaitTime);
        }

        /// <summary>
        /// Hides active speech and schedules the existing condition action with its authored timeout.
        /// </summary>
        public void WaitCondition(ActionWaitCondition action, DialogRuntime runtime)
        {
            HideSpeech();
            pending = action;
            remaining = action.Timeout;
        }

        /// <summary>
        /// Holds an owned actor's attention until this run releases it.
        /// </summary>
        public void Look(string who, string target)
        {
            if (!Definition.Actors.ContainsKey(who ?? ""))
            {
                throw new InvalidOperationException("npc.look needs an owned actor role");
            }

            looks[who] = target;
            ApplyLooks();
        }

        /// <summary>
        /// Idempotently interrupts or completes the run, releases its holds, clears attention and updates the
        /// existing completion/cooldown/variation state.
        /// </summary>
        public void Finished(bool aborted)
        {
            if (Done)
            {
                return;
            }

            if (aborted && speech != null)
            {
                Emit("speech.interrupted");
            }

            director.Trace(RunId, aborted ? "interrupted" : "completed");
            Done = true;
            pending = null;
            HideSpeech();
            looks.Clear();
            foreach (var id in Definition.Actors.Values)
            {
                World.Space.Hold(id, false);
            }

            if (!aborted)
            {
                BarkMemory.Data.Completed.Add(Definition.Id);
            }

            BarkMemory.Data.Cooldowns[Definition.Id] = aborted ? 2 : Definition.Cooldown;
            director.PauseVariation(Definition, aborted ? 2 : Definition.Cooldown);
            GameLogger.Log("NPC_BARKS", Definition.Id + " run=" + RunId + (aborted ? " interrupted" : " completed"));
        }

        /// <summary>
        /// Requests ordinary dialog-runtime abort once; completion cleanup retains its existing event ordering.
        /// </summary>
        public void Abort()
        {
            if (!Done)
            {
                Runtime.Abort();
            }
        }

        private string ResolveText(ActionText action)
        {
            var text = action.LocaleKey == null
                ? action.Text
                : GodotTranslationProvider.Instance.Get(action.LocaleKey) ?? action.FallbackText;
            return action.UseProcessText ? Runtime.ProcessText(text) : text;
        }

        private void Emit(string phase)
        {
            if (speech?.Cue == null)
            {
                return;
            }

            ModEventBus.Publish(
                new BarkSpeechEvent
            {
                SessionId = director.SessionId,
                At = director.Clock,
                SenderModId = "npc_barks_base",
                EventId = Guid.NewGuid().ToString("N"),
                RunId = RunId,
                UtteranceId = utterance,
                Speaker = SpeakerId,
                Map = Definition.Map,
                Cue = speech.Cue,
                Phase = phase,
                Depth = Depth + 1
            }
            );
        }

        private void HideSpeech()
        {
            World.Space.Hide(this);
            speech = null;
            SpeakerId = null;
        }

        /// <summary>
        /// Resolved addressee is shared by speech labels, attention and optional positional effects.
        /// </summary>
        public string AddresseeId
        {
            get
            {
                return SpeakerId != null && looks.TryGetValue(SpeakerRole(), out var target) ? World.Resolve(target) : null;
            }
        }

        /// <summary>
        /// Transient current text occurrence identity, shared with spatial presentation to prevent duplicate
        /// capture.
        /// </summary>
        public string UtteranceId
        {
            get
            {
                return utterance;
            }
        }

        private string SpeakerRole()
        {
            foreach (var pair in World.Roles)
            {
                if (pair.Value == SpeakerId)
                {
                    return pair.Key;
                }
            }

            return "";
        }

        private void ApplyLooks()
        {
            foreach (var pair in looks)
            {
                World.Space.Look(World.Resolve(pair.Key), World.Resolve(pair.Value));
            }
        }
    }
}
