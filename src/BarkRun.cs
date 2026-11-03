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
        private Control bubble;
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
                if (World.Session.Find(id)?.Visible != true) { Abort(); return; }
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
            bubble = director.CreateBubble();
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
                var actor = World.Session.Find(id);
                if (actor != null) actor.RouteHeld = false;
            }
            if (!aborted) BarkMemory.Data.Completed.Add(Definition.Id);
            BarkMemory.Data.Cooldowns[Definition.Id] = aborted ? 2 : Definition.Cooldown;
            GameLogger.Log("NPC_BARKS", Definition.Id + " run=" + RunId + (aborted ? " interrupted" : " completed"));
        }

        public void Abort() { if (!Done) Runtime.Abort(); }

        /// <summary>Screen placement is presentation-only; hearing is a separate logical-cell test.</summary>
        public void Render(RpgWorldHost host, bool paused, List<Rect2> occupied)
        {
            if (bubble == null || speech == null || Done) return;
            var actor = World.Session.Find(SpeakerId);
            var visible = !paused && actor?.Visible == true && host.View?.Presentation != null
                && actor.Actor.Position.DistanceSquaredTo(World.Session.Player.Position) <= 64;
            bubble.Visible = visible;
            if (!visible) return;
            var label = bubble.GetNode<Label>("Margin/Text");
            label.Text = ResolveText(speech);
            bubble.Size = new Vector2(280, Math.Max(60, label.GetMinimumSize().Y + 24));
            var position = actor.Actor.Position;
            var point = host.View.Presentation.ProjectToView(new Vector3(position.X + 0.5f, position.Y + 0.5f, 2.5f));
            var bounds = new Rect2(Vector2.Zero, host.Size);
            if (!bounds.HasPoint(point)) { bubble.Visible = false; return; }
            var location = new Vector2(Math.Clamp(point.X - 140, 8, Math.Max(8, host.Size.X - 290)),
                Math.Clamp(point.Y - bubble.Size.Y - 16, 8, Math.Max(8, host.Size.Y - bubble.Size.Y - 8)));
            var rectangle = new Rect2(location, bubble.Size);
            for (var attempt = 0; attempt <= occupied.Count; attempt++)
            {
                var collision = false;
                foreach (var other in occupied)
                {
                    if (!rectangle.Grow(6).Intersects(other)) continue;
                    rectangle.Position = new Vector2(rectangle.Position.X, other.Position.Y - bubble.Size.Y - 14);
                    collision = true;
                    break;
                }
                if (!collision) break;
            }
            if (rectangle.Position.Y < 8) { bubble.Visible = false; return; }
            bubble.Position = rectangle.Position;
            occupied.Add(rectangle);
            bubble.GetNode<Line2D>("Connector").Points = new[] {
                new Vector2(bubble.Size.X / 2, bubble.Size.Y + 10), point - rectangle.Position + new Vector2(0, 8) };
        }

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
            if (bubble != null) { bubble.QueueFree(); bubble = null; }
            speech = null; SpeakerId = null;
        }

        private void ApplyLooks()
        {
            foreach (var pair in looks)
            {
                var actor = World.Session.Find(World.Roles[pair.Key]);
                if (actor?.Visible != true || actor.Actor.IsMoving || !World.TryPosition(pair.Value, out var target)) continue;
                var difference = target - actor.Actor.Position;
                if (difference.LengthSquared() < 0.01f) continue;
                actor.Actor.Face(Math.Abs(difference.X) > Math.Abs(difference.Y)
                    ? difference.X > 0 ? RpgDirection.Right : RpgDirection.Left
                    : difference.Y > 0 ? RpgDirection.Down : RpgDirection.Up);
            }
        }
    }
}
