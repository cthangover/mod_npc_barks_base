namespace Cthangover.Mods.NpcBarks
{
    using System;
    using Cthangover.Core.Scenes;

    /// <summary>Immutable spatial syntax; all actor identity is supplied afresh by the evaluating execution.</summary>
    public sealed class BarkCondition : ConditionNode
    {
        private readonly string function;
        private readonly string[] args;
        public BarkCondition(string function, string[] args) { this.function = function; this.args = args; }
        public override bool Evaluate(EvaluationContext context)
        {
            if (context.Services is not BarkWorldContext world) throw new InvalidOperationException(function + " requires an ambient world context");
            if (!world.TrySpatialPosition(args[0], out var a)) return false;
            if (function == "visible") return true;
            if (function == "moving")
                return world.Space.Moving(world.Resolve(args[0]));
            if (!world.TrySpatialPosition(args[1], out var b)) return false;
            if (function == "can_hear") return world.Space.CanHear(world.Resolve(args[0]), world.Resolve(args[1]));
            var radius = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
            return a.DistanceSquaredTo(b) <= radius * radius;
        }
    }
}
