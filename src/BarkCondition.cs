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
            if (!world.TryPosition(args[0], out var a)) return false;
            if (function == "visible") return true;
            if (function == "moving")
                return args[0] == "player" ? world.Session.Player.IsMoving : world.Session.Find(world.Roles[args[0]]).Actor.IsMoving;
            if (!world.TryPosition(args[1], out var b)) return false;
            var radius = function == "near" ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 8;
            // Prototype hearing is explicitly distance-based; no wall/door acoustics are implied.
            return a.DistanceSquaredTo(b) <= radius * radius;
        }
    }
}
