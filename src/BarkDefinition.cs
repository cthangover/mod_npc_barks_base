namespace Cthangover.Mods.NpcBarks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Cthangover.Core.Scenarios;

    /// <summary>An ambient .scenario header and body. Identity is its group-relative path, never a line index.</summary>
    public sealed class BarkDefinition
    {
        public string Id { get; private set; }
        public string Map { get; private set; }
        public string Body { get; set; }
        public string ModId { get; set; }
        public string Path { get; set; }
        public string Source { get; set; }
        public string Condition { get; private set; }
        public string Anchor { get; private set; }
        public string On { get; private set; }
        public string Cue { get; private set; }
        public string SourceActor { get; private set; }
        /// <summary>Optional fair rotation shared by alternative scenarios for the same participants.</summary>
        public string Variation { get; private set; }
        public double Cooldown { get; private set; }
        public bool Once { get; private set; }
        public int Priority { get; private set; }
        public Dictionary<string, string> Actors { get; } = new(StringComparer.Ordinal);

        /// <summary>Returns null for other channels. Invalid ambient metadata is rejected before it can run.</summary>
        public static BarkDefinition Parse(string id, string source)
        {
            var (meta, body) = ScenarioMetadata.ParseMetadata(source);
            if (!meta.TryGetValue("channel", out var channel) || channel != "ambient") return null;
            var result = new BarkDefinition { Id = id, Body = body, Source = source, Cooldown = 30 };
            meta.TryGetValue("scene", out var map); result.Map = map;
            meta.TryGetValue("condition", out var condition); result.Condition = condition;
            meta.TryGetValue("anchor", out var anchor); result.Anchor = anchor;
            meta.TryGetValue("on", out var on); result.On = on;
            meta.TryGetValue("cue", out var cue); result.Cue = cue;
            meta.TryGetValue("source_actor", out var actor); result.SourceActor = actor;
            meta.TryGetValue("variation", out var variation); result.Variation = variation;
            if (meta.TryGetValue("cooldown", out var cooldown)) result.Cooldown = Number(cooldown);
            if (meta.TryGetValue("once", out var once)) result.Once = bool.Parse(once);
            if (meta.TryGetValue("priority", out var priority)) result.Priority = int.Parse(priority, CultureInfo.InvariantCulture);
            foreach (var pair in meta)
                if (pair.Key.StartsWith("actor.", StringComparison.Ordinal)) result.Actors.Add(pair.Key.Substring(6), pair.Value);
            if (string.IsNullOrWhiteSpace(map) || result.Actors.Count == 0) throw new FormatException(id + ": scene and actor roles are required");
            foreach (var pair in result.Actors)
            {
                result.Anchor ??= pair.Key;
                if (pair.Key is "player" or "source") throw new FormatException(id + ": player and source are reserved roles");
                if (!pair.Value.StartsWith(map + "/", StringComparison.Ordinal)) throw new FormatException(id + ": actor belongs to another map");
            }
            if (!result.Actors.ContainsKey(result.Anchor)) throw new FormatException(id + ": anchor must name an actor role");
            if (on != null && on is not ("speech.started" or "speech.finished")) throw new FormatException(id + ": unsupported on event");
            if (on != null && string.IsNullOrEmpty(cue)) throw new FormatException(id + ": a speech trigger requires cue");
            return result;
        }

        private static double Number(string text)
        {
            var value = double.Parse(text, CultureInfo.InvariantCulture);
            if (!double.IsFinite(value) || value < 0) throw new FormatException("cooldown must be finite and non-negative");
            return value;
        }
    }
}
