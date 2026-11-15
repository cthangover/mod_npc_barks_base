using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Scenes;
using Cthangover.Core.Scenarios;
using Cthangover.Core.Settings;
using Cthangover.Mods.Dialog;
using Cthangover.Mods.Rpg;
using Godot;

namespace Cthangover.Mods.NpcBarks
{
    /// <summary>
    /// Transactional catalog refresh and author controls. Existing runs retain their exact compiled program
    /// until explicitly restarted.
    /// </summary>
    public partial class BarkDirector
    {
        private const string StatusRunning = "running";

        private const string StatusNoWorld = "no_world";

        private const string StatusWrongMap = "wrong_map";

        private const string StatusStopped = "stopped";

        private const string StatusOnce = "once";

        private const string StatusCooldown = "cooldown";

        private const string StatusMissing = "actor_missing";

        private const string StatusMoving = "moving";

        private const string StatusBusy = "busy";

        private const string StatusTrigger = "trigger";

        private const string StatusReady = "ready";

        private const string StatusConditionFalse = "condition_false";

        private const string CommandCancel = "cancel_preview";

        private const string CommandPause = "pause";

        private const string CommandContinue = "continue";

        private const string CommandStep = "step";

        private const string CommandClock = "clock";

        private const string CommandStop = "stop";

        private const string CommandRestart = "restart";

        private readonly HashSet<Node> observers = new();
        private readonly HashSet<string> suppressed = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<int>> breakpoints = new(StringComparer.Ordinal);
        private readonly List<string> trace = new();
        private BarkDefinition pendingPreview;
        private bool pendingCompany;
        private bool worldPaused;

        /// <summary>
        /// Read-only bounded chronological trace used by the background scenario studio; entries use the
        /// director’s simulation clock.
        /// </summary>
        public IReadOnlyList<string> Events
        {
            get
            {
                return trace;
            }
        }

        /// <summary>
        /// Whether any author tool currently observes the director and may hold debug execution.
        /// </summary>
        public bool IsObserved
        {
            get
            {
                return observers.Count > 0;
            }
        }

        /// <summary>
        /// Tool-controlled world pause forwarded to the active adapter; releasing observers clears only this
        /// control path.
        /// </summary>
        public bool WorldPaused
        {
            get
            {
                return worldPaused;
            }

            set
            {
                worldPaused = value;
                session?.SetPaused(value);
            }
        }

        /// <summary>
        /// Adds or removes an author-tool owner and preserves the existing RPG input observation path; the last
        /// release resumes held debug runs.
        /// </summary>
        public void Observe(Node owner, bool enabled)
        {
            if (owner == null)
            {
                return;
            }

            if (enabled)
            {
                observers.Add(owner);
            }
            else
            {
                observers.Remove(owner);
            }

            RpgInput.Observe(owner, enabled);
            if (observers.Count == 0)
            {
                WorldPaused = false;
                foreach (var run in runs.ToArray())
                {
                    if (!run.Done && (run.DebugPaused || run.Runtime.IsHeld))
                    {
                        run.ContinueDebug();
                    }
                }
            }
        }

        private void ReleaseObservers()
        {
            foreach (var observer in observers)
            {
                RpgInput.Observe(observer, false);
            }

            observers.Clear();
            WorldPaused = false;
        }

        /// <summary>
        /// Appends a bounded timestamped execution/catalogue trace, retaining at most 200 entries.
        /// </summary>
        public void Trace(string run, string detail)
        {
            trace.Add(
                Clock.ToString("F2", CultureInfo.InvariantCulture) + " ["
                + (run?.Substring(0, Math.Min(8, run.Length)) ?? "catalog")
                + "] "
                + detail
            );
            if (trace.Count > 200)
            {
                trace.RemoveAt(0);
            }
        }

        /// <summary>
        /// Checks a positive source-line breakpoint for a stable scenario identity without modifying the
        /// catalogue.
        /// </summary>
        public bool HasBreakpoint(string id, int line)
        {
            return !string.IsNullOrEmpty(id) && line > 0 && breakpoints.TryGetValue(id, out var rows) && rows.Contains(line);
        }

        /// <summary>
        /// Adds or removes a positive source-line breakpoint; invalid identities/lines leave the set unchanged.
        /// </summary>
        public void ToggleBreakpoint(string id, int line)
        {
            if (line <= 0 || string.IsNullOrEmpty(id))
            {
                return;
            }

            if (!breakpoints.TryGetValue(id, out var rows))
            {
                breakpoints[id] = rows = new();
            }

            if (!rows.Remove(line))
            {
                rows.Add(line);
            }
        }

        /// <summary>
        /// Builds independent author-facing scenario rows from the current spatial context, condition and
        /// persisted cooldown state.
        /// </summary>
        public IReadOnlyList<BackgroundScenarioInfo> Catalog()
        {
            var result = new List<BackgroundScenarioInfo>();
            foreach (var definition in definitions)
            {
                var context = session == null ? null : new BarkWorldContext(session, definition, SourceFor(definition));
                result.Add(
                    new BackgroundScenarioInfo
                {
                    Id = definition.Id,
                    ModId = definition.ModId,
                    Path = definition.Path,
                    Map = definition.Map,
                    Source = definition.Source,
                    Body = definition.Body,
                    Status = Status(definition, context),
                    Details = context == null ? "" : Describe(context, definition.Condition) + "cooldown="
                    + (BarkMemory.Data.Cooldowns.TryGetValue(definition.Id, out var seconds)
                        ? seconds.ToString("F2", CultureInfo.InvariantCulture)
                        : "0")
                    + (definition.On == null
                        ? ""
                        : "\ntrigger=" + definition.On + " cue=" + definition.Cue + " source=" + definition.SourceActor)
                    + "\n"
                }
                );
            }

            return result;
        }

        /// <summary>
        /// Returns snapshots of live executions for the studio without exposing the mutable run collection.
        /// </summary>
        public IReadOnlyList<BackgroundRunInfo> Executions()
        {
            var result = new List<BackgroundRunInfo>();
            foreach (var run in runs)
            {
                if (!run.Done)
                {
                    result.Add(run.Snapshot());
                }
            }

            return result;
        }

        /// <summary>
        /// Formats current role existence, player distance in adapter units, evaluated conditions and relevant
        /// flags for author diagnostics.
        /// </summary>
        public string Describe(BarkWorldContext context, string condition)
        {
            var result = new StringBuilder();
            foreach (var role in context.Roles)
            {
                result.Append(role.Key).Append(" = ").Append(role.Value)
                    .Append("; visible=").Append(context.Space.TryPosition(role.Value, out _));
                if (context.TrySpatialPosition(role.Key, out var position))
                {
                    result.Append("; distance(player)=").Append(
                        position.DistanceTo(context.Space.TryPosition("player", out var player) ? player : position)
                        .ToString("F2", CultureInfo.InvariantCulture)
                    );
                }

                result.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(condition))
            {
                result.Append("condition: ").Append(condition).Append(" = ")
                    .Append(ScenarioCondition.EvaluateWithServices(condition, context.Scope, context))
                    .AppendLine();
            }

            if (GameData.Instance?.Runtime?.Flags != null)
            {
                foreach (var flag in GameData.Instance.Runtime.Flags)
                {
                    if (flag.Key.StartsWith(context.Scope + ":", StringComparison.Ordinal)
                        || (!string.IsNullOrEmpty(condition) && condition.Contains(flag.Key, StringComparison.Ordinal)))
                    {
                        result.Append(flag.Key).Append(" = ").AppendLine(flag.Value);
                    }
                }
            }

            return result.ToString();
        }

        private string Status(BarkDefinition definition, BarkWorldContext context)
        {
            foreach (var run in runs)
            {
                if (!run.Done && run.Definition.Id == definition.Id)
                {
                    return StatusRunning;
                }
            }

            if (session == null)
            {
                return StatusNoWorld;
            }

            if (definition.Map != session.MapId)
            {
                return StatusWrongMap;
            }

            if (suppressed.Contains(definition.Id))
            {
                return StatusStopped;
            }

            if (definition.Once && BarkMemory.Data.Completed.Contains(definition.Id))
            {
                return StatusOnce;
            }

            if (BarkMemory.Data.Cooldowns.TryGetValue(definition.Id, out var cooldown) && cooldown > 0)
            {
                return StatusCooldown;
            }

            foreach (var actor in definition.Actors.Values)
            {
                if (!session.TryPosition(actor, out _))
                {
                    return StatusMissing;
                }

                if (session.Moving(actor))
                {
                    return StatusMoving;
                }

                if (session.Held(actor))
                {
                    return StatusBusy;
                }
            }

            if (definition.On != null)
            {
                return StatusTrigger;
            }

            return ScenarioCondition.EvaluateWithServices(definition.Condition, context.Scope, context)
                ? StatusReady
                : StatusConditionFalse;
        }

        /// <summary>
        /// Compiles a candidate catalogue transactionally with the supplied file override; errors leave the
        /// live catalogue/runs unchanged.
        /// </summary>
        public bool Validate(string modId, string path, string text, out string error)
        {
            try
            {
                BuildCatalog(modId, path, text);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Publishes a completely built replacement catalogue only after successful validation; existing runs
        /// retain their compiled programs.
        /// </summary>
        public bool Reload(out string error)
        {
            try
            {
                ModManager.Instance.GetResolver<Cthangover.Core.Mods.Resolvers.FileListResolver>()?.ClearCache();
                var candidate = BuildCatalog(null, null, null);
                definitions.Clear();
                definitions.AddRange(candidate);
                byMap.Clear();
                bySpeech.Clear();
                foreach (var definition in definitions)
                {
                    var index = definition.On == null ? byMap : bySpeech;
                    var key = definition.On == null ? definition.Map : definition.Map + "|" + definition.On + "|" + definition.Cue;
                    if (!index.TryGetValue(key, out var rows))
                    {
                        index[key] = rows = new();
                    }

                    rows.Add(definition);
                }

                error = null;
                Trace(null, "catalog applied: " + definitions.Count);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Trace(null, "catalog rejected: " + error);
                return false;
            }
        }

        private List<BarkDefinition> BuildCatalog(string overrideMod, string overridePath, string overrideText)
        {
            var result = new List<BarkDefinition>();
            var seenOverride = false;
            foreach (var file in Files.Collect("scenarios"))
            {
                if (!file.Key.EndsWith(".scenario", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var source = Read(file.Value.ModId, file.Value.FullPath, overrideMod, overridePath, overrideText);
                if (file.Value.ModId == overrideMod && file.Value.FullPath == overridePath)
                {
                    seenOverride = true;
                }

                var definition = BuildDefinition(
                    file.Key.Substring(0, file.Key.Length - 9),
                    file.Value.ModId,
                    file.Value.FullPath,
                    source,
                    overrideMod,
                    overridePath,
                    overrideText
                );
                if (definition != null)
                {
                    result.Add(definition);
                }
            }

            if (!seenOverride && overrideText != null && overridePath.StartsWith("scenarios/", StringComparison.Ordinal))
            {
                var definition = BuildDefinition(
                    overridePath.Substring(10, overridePath.Length - 19),
                    overrideMod,
                    overridePath,
                    overrideText,
                    overrideMod,
                    overridePath,
                    overrideText
                );
                if (definition != null)
                {
                    result.Add(definition);
                }
            }

            result.Sort(Compare);
            return result;
        }

        private BarkDefinition BuildDefinition(
            string id,
            string modId,
            string path,
            string source,
            string overrideMod,
            string overridePath,
            string overrideText
        )
        {
            var definition = BarkDefinition.Parse(id, source);
            if (definition == null)
            {
                return null;
            }

            definition.ModId = modId;
            definition.Path = path;
            foreach (var owner in ModRegistry.Instance.GetOrderedModIds())
            {
                var paths = new List<string>();
                foreach (var file in Files.Collect("patches/scenarios"))
                {
                    if (file.Value.ModId == owner && file.Key.EndsWith(".scenario", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(file.Key);
                    }
                }

                const string patchPrefix = "patches/scenarios/";
                if (owner == overrideMod && overrideText != null
                    && overridePath.StartsWith(patchPrefix, StringComparison.Ordinal))
                {
                    var added = overridePath.Substring(patchPrefix.Length);
                    if (!paths.Contains(added))
                    {
                        paths.Add(added);
                    }
                }

                paths.Sort(StringComparer.Ordinal);
                foreach (var patch in paths)
                {
                    var text = Files.Collect("patches/scenarios").TryGetValue(patch, out var entry)
                        ? Read(entry.ModId, entry.FullPath, overrideMod, overridePath, overrideText)
                        : overrideText;
                    var(metadata, _) = ScenarioMetadata.ParseMetadata(text);
                    if (metadata.TryGetValue("patch_of", out var target) && target == id)
                    {
                        definition.Body = BarkProgram.Patch(definition.Body, text);
                    }
                }
            }

            BarkProgram.Compile(definition);
            return definition;
        }

        private static string Read(string mod, string path, string overrideMod, string overridePath, string text)
        {
            return text != null && mod == overrideMod && path == overridePath ? text : ModManager.Instance.ReadFileText(mod, path);
        }

        /// <summary>
        /// Queues a validated author preview, switching to its logical scene when necessary; participant
        /// acquisition waits for the next unblocked world tick.
        /// </summary>
        public bool Play(string modId, string path, string text, bool company, out string error)
        {
            try
            {
                if (session == null)
                {
                    throw new InvalidOperationException("Open a running game first");
                }

                var id = path.StartsWith("scenarios/", StringComparison.Ordinal) ? path.Substring(10, path.Length - 19) : path;
                var definition = BuildDefinition(id, modId, path, text, modId, path, text)
                    ?? throw new InvalidOperationException("This is not an ambient scenario");
                if (definition.On != null && definition.SourceActor == null)
                {
                    throw new InvalidOperationException("Preview of an event scenario needs source_actor");
                }

                if (session.MapId != definition.Map)
                {
                    GetTree().Root.GetNode<SceneManager>("SceneManager").SwitchScene(definition.Map);
                }

                pendingPreview = definition;
                pendingCompany = company;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void StartPendingPreview()
        {
            if (pendingPreview == null || pendingPreview.Map != session.MapId)
            {
                return;
            }

            var definition = pendingPreview;
            var company = pendingCompany;
            pendingPreview = null;
            foreach (var run in runs.ToArray())
            {
                foreach (var actor in definition.Actors.Values)
                {
                    if (!run.Done && run.Definition.Actors.ContainsValue(actor))
                    {
                        run.Abort();
                        break;
                    }
                }
            }

            try
            {
                ForceStart(definition, SourceFor(definition), false);
                if (company && byMap.TryGetValue(session.MapId, out var neighbors))
                {
                    foreach (var other in neighbors)
                    {
                        if (other.Id != definition.Id)
                        {
                            TryStart(other, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace(null, "preview rejected: " + ex.Message);
            }
        }

        private static BarkSpeechEvent SourceFor(BarkDefinition definition)
        {
            return definition.SourceActor == null ? null : new BarkSpeechEvent
            {
                Speaker = definition.SourceActor,
                Map = definition.Map
            };
        }

        private void ForceStart(BarkDefinition definition, BarkSpeechEvent source, bool hold)
        {
            foreach (var id in definition.Actors.Values)
            {
                if (!session.TryPosition(id, out _) || session.Moving(id) || !session.CanReserve(id))
                {
                    throw new InvalidOperationException("Participant is unavailable: " + id);
                }
            }

            foreach (var id in definition.Actors.Values)
            {
                foreach (var active in runs)
                {
                    if (!active.Done && active.Definition.Actors.ContainsValue(id))
                    {
                        throw new InvalidOperationException("Participant is busy: " + id);
                    }
                }
            }

            suppressed.Remove(definition.Id);
            var run = new BarkRun(this, definition, new BarkWorldContext(session, definition, source), source);
            foreach (var id in definition.Actors.Values)
            {
                session.Hold(id, true);
            }

            runs.Add(run);
            if (hold)
            {
                run.PauseDebug();
            }

            try
            {
                run.Start();
            }
            catch
            {
                run.Abort();
                throw;
            }

            Trace(run.RunId, "preview " + definition.Id);
        }

        /// <summary>
        /// Applies the supported debugger command to a live run or pending preview, returning a readable error
        /// without changing another execution.
        /// </summary>
        public bool Command(string runId, string command, out string error)
        {
            try
            {
                if (command == CommandCancel)
                {
                    if (pendingPreview?.Id == runId)
                    {
                        pendingPreview = null;
                    }

                    suppressed.Add(runId);
                    foreach (var run in runs.ToArray())
                    {
                        if (!run.Done && run.Definition.Id == runId)
                        {
                            run.Abort();
                        }
                    }

                    error = null;
                    return true;
                }

                BarkRun selected = null;
                foreach (var run in runs)
                {
                    if (!run.Done && run.RunId == runId)
                    {
                        selected = run;
                    }
                }

                if (selected == null)
                {
                    throw new InvalidOperationException("Execution has ended");
                }

                switch (command)
                {
                    case CommandPause:
                        selected.PauseDebug();
                        break;
                    case CommandContinue:
                        selected.ContinueDebug();
                        break;
                    case CommandStep:
                        selected.StepDebug();
                        break;
                    case CommandClock:
                        selected.AdvanceDebugClock();
                        break;
                    case CommandStop:
                        suppressed.Add(selected.Definition.Id);
                        selected.Abort();
                        break;
                    case CommandRestart:
                        var definition = selected.Definition;
                        foreach (var current in definitions)
                        {
                            if (current.Id == definition.Id)
                            {
                                definition = current;
                            }
                        }

                        var source = selected.SourceSpeech;
                        var paused = selected.DebugPaused;
                        selected.Abort();
                        ForceStart(definition, source, paused);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown command " + command);
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
