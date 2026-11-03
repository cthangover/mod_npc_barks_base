namespace Cthangover.Mods.NpcBarks
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Mods.Dialog;
    using Cthangover.Mods.Dialog.Scenarios;
    using Cthangover.Mods.Dialog.Action.Impls;
    using Cthangover.Core.Scenarios;
    using Cthangover.Core.Scenes;

    /// <summary>Validates the supported ambient subset, then compiles through the existing scenario parser.</summary>
    public static class BarkProgram
    {
        /// <summary>Every execution gets fresh action instances; queues are never shared between NPCs.</summary>
        public static DialogQueue Compile(BarkDefinition definition)
        {
            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in definition.Body.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith(':') && !labels.Add(line.Substring(1))) throw new FormatException("duplicate label " + line);
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(':')) continue;
                var positional = new List<string>(); var named = new Dictionary<string, string>();
                var command = ScenarioParser.SplitLine(line, positional, named, out var target);
                if (command is not ("text" or "ptext" or "when" or "goto" or "delay" or "flag" or "set" or "cset" or "end" or "action"))
                    throw new FormatException("unsupported ambient command: " + command);
                if (command == "action" && (positional.Count != 1 || positional[0] != "npc.look"))
                    throw new FormatException("prototype supports only action npc.look");
                if (command is "text" or "ptext")
                {
                    if (named.TryGetValue("who", out var who) && !definition.Actors.ContainsKey(who)) throw new FormatException("unknown speaker " + who);
                    if (named.TryGetValue("wait", out var wait) && wait == "false") throw new FormatException("ambient speech requires a reading wait");
                }
                if (command == "when") ConditionParser.Parse(string.Join(" ", positional));
                if (command == "action")
                {
                    if (!named.TryGetValue("who", out var who) || !definition.Actors.ContainsKey(who)) throw new FormatException("npc.look requires a known who role");
                    if (!named.TryGetValue("target", out var look) || (look is not ("player" or "source") && !definition.Actors.ContainsKey(look))) throw new FormatException("npc.look requires a known target role");
                }
            }
            ConditionParser.Parse(definition.Condition);
            var queue = new ScenarioConverter().Convert(definition.Body);
            foreach (var action in queue.Queue)
            {
                if (action is ActionGoTo go) Check(labels, go.GoTo);
                if (action is ActionIf branch) { Check(labels, branch.TrueGoTo); Check(labels, branch.FalseGoTo); }
                if (action is ActionWaitCondition waiting) { Check(labels, waiting.TrueTarget); Check(labels, waiting.TimeoutTarget); }
            }
            queue.Scope = definition.Actors[definition.Anchor];
            return queue;
        }

        private static void Check(HashSet<string> labels, string target)
        {
            if (!string.IsNullOrEmpty(target) && !labels.Contains(target)) throw new FormatException("missing label " + target);
        }

        /// <summary>Replaces one labeled body, preserving its anchor. Patches never target unstable line numbers.</summary>
        public static string Patch(string body, string patchSource)
        {
            var (meta, replacement) = ScenarioMetadata.ParseMetadata(patchSource);
            if (meta.TryGetValue("append_blocks", out var append) && append == "true")
            {
                if (!replacement.TrimStart().StartsWith(':')) throw new FormatException("append_blocks requires explicit labels");
                // Require a terminal end so appending cannot change the old program's fall-through.
                if (!body.TrimEnd().EndsWith("end", StringComparison.Ordinal)) throw new FormatException("append_blocks requires the original body to end with end");
                return body.TrimEnd() + "\n" + replacement;
            }
            var remove = meta.TryGetValue("remove", out var label);
            if (!remove && !meta.TryGetValue("replace", out label)) throw new FormatException("patch requires replace, remove or append_blocks");
            var lines = new List<string>(body.Split('\n'));
            var start = lines.FindIndex(new BarkLabelMatcher(label).Matches);
            if (start < 0) throw new FormatException("patch label not found: " + label);
            var end = start + 1;
            while (end < lines.Count && !lines[end].TrimStart().StartsWith(':')) end++;
            if (!remove)
                foreach (var line in replacement.Split('\n'))
                    if (line.TrimStart().StartsWith(':')) throw new FormatException("replace body cannot introduce labels; use append_blocks");
            lines.RemoveRange(remove ? start : start + 1, end - start - (remove ? 0 : 1));
            if (!remove) lines.InsertRange(start + 1, replacement.TrimEnd().Split('\n'));
            return string.Join("\n", lines);
        }
    }
}
