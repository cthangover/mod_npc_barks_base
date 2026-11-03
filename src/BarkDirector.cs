namespace Cthangover.Mods.NpcBarks
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Core.Events;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Scenarios;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Base;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Play;
    using Godot;

    /// <summary>Coordinates concurrent common runtimes and atomically reserves owned participants. Speech triggers are deferred and bounded.</summary>
    public partial class BarkDirector : ModNode, Cthangover.Mods.Dialog.IBackgroundScenarioTools
    {
        private readonly List<BarkDefinition> definitions = new();
        private readonly Dictionary<string, List<BarkDefinition>> byMap = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<BarkDefinition>> bySpeech = new(StringComparer.Ordinal);
        private readonly List<BarkRun> runs = new();
        private readonly Queue<BarkSpeechEvent> events = new();
        private readonly HashSet<string> eventIds = new();
        private readonly Queue<string> recentIds = new();
        private RpgWorldSession session;
        private Control layer;
        private double scan;
        private double glance;
        private bool paused;
        public static BarkDirector Current { get; private set; }
        public IReadOnlyList<BarkRun> Runs => runs;
        public string SessionId { get; private set; }
        public double Clock { get; private set; }

        protected override void OnEnterTree()
        {
            Current = this;
            Cthangover.Mods.Dialog.BackgroundScenarios.Provider = this;
            RpgWorldHost.WorldUpdated += OnWorld;
            RpgWorldHost.InteractionStarting += OnInteraction;
            ModEventBus.Subscribe<BarkSpeechEvent>(OnSpeech);
            ModEventBus.Subscribe<SceneExitedEvent>(OnSceneExited);
            ModEventBus.Subscribe<ModSetReloadedEvent>(OnReload);
            ModEventBus.Subscribe<ScenarioContentChangedEvent>(OnContentChanged);
        }
        protected override void OnReady() { LoadDefinitions(); }
        protected override void OnExitTree()
        {
            Clear();
            RpgWorldHost.WorldUpdated -= OnWorld;
            RpgWorldHost.InteractionStarting -= OnInteraction;
            ModEventBus.Unsubscribe<BarkSpeechEvent>(OnSpeech);
            ModEventBus.Unsubscribe<SceneExitedEvent>(OnSceneExited);
            ModEventBus.Unsubscribe<ModSetReloadedEvent>(OnReload);
            ModEventBus.Unsubscribe<ScenarioContentChangedEvent>(OnContentChanged);
            ReleaseObservers();
            if (ReferenceEquals(Cthangover.Mods.Dialog.BackgroundScenarios.Provider, this)) Cthangover.Mods.Dialog.BackgroundScenarios.Provider = null;
            if (Current == this) Current = null;
        }
        protected override void OnProcess(double delta)
        {
            var host = RpgWorldHost.Current;
            if (host?.Session != session) return;
            var occupied = new List<Rect2>();
            foreach (var run in runs) run.Render(host, paused && !WorldPaused, occupied);
        }

        /// <summary>Cancels all runs and drops transient triggers; saved completion history is owned separately.</summary>
        public void Clear()
        {
            foreach (var run in runs) run.Abort();
            runs.Clear(); events.Clear(); eventIds.Clear(); recentIds.Clear();
            suppressed.Clear(); WorldPaused = false;
            if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
            layer = null; session = null; scan = 0; Clock = 0; SessionId = Guid.NewGuid().ToString("N");
        }

        /// <summary>Creates only the dynamic row; its layout and visual style are in the mod scene prefab.</summary>
        public Control CreateBubble()
        {
            var bubble = TscnScenes.LoadAndBuild("scenes/ui/npc_speech.tscn") as Control;
            if (bubble == null) throw new InvalidOperationException("npc speech prefab is missing");
            layer.AddChild(bubble);
            return bubble;
        }

        private void OnWorld(RpgWorldHost host, double delta, bool blocked)
        {
            if (session != host.Session)
            {
                Clear(); session = host.Session;
                layer = new Control { Name = "NpcSpeechLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
                host.AddChild(layer);
                layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            }
            paused = WorldPaused || RpgInput.IsSimulationBlocked(host) || host.View?.Presentation == null;
            if (paused) return;
            StartPendingPreview();
            Clock += delta;
            var keys = new List<string>(BarkMemory.Data.Cooldowns.Keys);
            foreach (var key in keys) BarkMemory.Data.Cooldowns[key] = Math.Max(0, BarkMemory.Data.Cooldowns[key] - delta);
            // Only events queued before this update may start runs. Events emitted below wait for the next update.
            var count = events.Count;
            while (count-- > 0)
            {
                var speech = events.Dequeue();
                if (speech.Map != session.Map.ID || speech.SessionId != SessionId || Clock - speech.At > 2 || speech.Depth > 8) continue;
                if (bySpeech.TryGetValue(speech.Map + "|" + speech.Phase + "|" + speech.Cue, out var responses))
                    foreach (var definition in responses)
                        if (definition.SourceActor == null || definition.SourceActor == speech.Speaker) TryStart(definition, speech);
            }
            foreach (var run in runs.ToArray())
            {
                try { run.Tick(delta); }
                catch (Exception ex) { GameLogger.Log("NPC_BARKS", run.Definition.Id + ": " + ex.Message, LogLevel.Error); run.Abort(); }
            }
            runs.RemoveAll(IsDone);
            scan -= delta;
            if (scan > 0) return;
            scan = 0.1;
            if (byMap.TryGetValue(session.Map.ID, out var nearby))
                foreach (var definition in nearby) TryStart(definition, null);
            glance += 0.1;
            PassiveAttention();
        }

        private void TryStart(BarkDefinition definition, BarkSpeechEvent speech)
        {
            if (suppressed.Contains(definition.Id) || definition.Map != session.Map.ID || runs.Count >= 32) return;
            if (definition.Once && BarkMemory.Data.Completed.Contains(definition.Id)) return;
            if (BarkMemory.Data.Cooldowns.TryGetValue(definition.Id, out var cooldown) && cooldown > 0) return;
            foreach (var run in runs)
            {
                if (!run.Done && run.Definition.Id == definition.Id) return;
                if (run.Done) continue;
                foreach (var actor in definition.Actors.Values)
                    if (run.Definition.Actors.ContainsValue(actor)) return;
            }
            foreach (var actor in definition.Actors.Values)
                if (session.Find(actor)?.Visible != true || session.Find(actor).Actor.IsMoving) return;
            var context = new BarkWorldContext(session, definition, speech);
            try
            {
                if (!ScenarioCondition.EvaluateWithServices(definition.Condition, context.Scope, context)) return;
                var candidate = new BarkRun(this, definition, context, speech);
                foreach (var actor in definition.Actors.Values) session.Find(actor).RouteHeld = true;
                runs.Add(candidate);
                try { candidate.Start(); }
                catch { candidate.Abort(); throw; }
                GameLogger.Log("NPC_BARKS", "started " + definition.Id + " run=" + candidate.RunId);
            }
            catch (Exception ex)
            {
                BarkMemory.Data.Cooldowns[definition.Id] = 30;
                GameLogger.Log("NPC_BARKS", definition.Id + ": " + ex.Message, LogLevel.Error);
            }
        }

        private void PassiveAttention()
        {
            var look = glance % 3 < 0.6;
            foreach (var actor in session.Instances)
            {
                if (actor.RouteHeld || !actor.Visible || actor.Actor.IsMoving || actor.Current.Form != Cthangover.Mods.Rpg.Model.RpgObjectForm.Actor
                    || actor.Current.Category != "npc" || actor.Current.Route != null) continue;
                var difference = session.Player.Position - actor.Actor.Position;
                if (difference.LengthSquared() > 25 || difference.LengthSquared() < 0.01f) continue;
                if (!look) { actor.Actor.Face(actor.Current.Facing); continue; }
                actor.Actor.Face(Math.Abs(difference.X) > Math.Abs(difference.Y)
                    ? difference.X > 0 ? RpgDirection.Right : RpgDirection.Left
                    : difference.Y > 0 ? RpgDirection.Down : RpgDirection.Up);
            }
        }

        private void OnInteraction(RpgWorldHost host, RpgInstance actor)
        {
            foreach (var run in runs)
                if (run.Definition.Actors.ContainsValue(actor.Id)) run.Abort();
        }
        private void OnSpeech(BarkSpeechEvent speech)
        {
            if (session == null || speech.Map != session.Map.ID || speech.SessionId != SessionId || events.Count >= 128 || !eventIds.Add(speech.EventId)) return;
            recentIds.Enqueue(speech.EventId);
            if (recentIds.Count > 4096) eventIds.Remove(recentIds.Dequeue());
            Trace(speech.RunId, speech.Phase + " cue=" + speech.Cue + " speaker=" + speech.Speaker);
            events.Enqueue(speech);
        }
        private void OnSceneExited(SceneExitedEvent evt) { Clear(); }
        private void OnContentChanged(ScenarioContentChangedEvent evt) { LoadDefinitions(); }
        private void OnReload(ModSetReloadedEvent evt) { if (evt.Applied) { Clear(); LoadDefinitions(); } }
        private static bool IsDone(BarkRun run) => run.Done;
        private static int Compare(BarkDefinition a, BarkDefinition b)
        {
            var order = a.Priority.CompareTo(b.Priority);
            return order == 0 ? string.CompareOrdinal(a.Id, b.Id) : order;
        }

        private void LoadDefinitions()
        {
            if (!Reload(out var error)) GameLogger.Log("NPC_BARKS", error, LogLevel.Error);
        }
    }
}
