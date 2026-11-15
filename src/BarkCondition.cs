using System;
using Cthangover.Core.Scenes;

namespace Cthangover.Mods.NpcBarks
{
    /// <summary>
    /// Immutable spatial syntax; all actor identity is supplied afresh by the evaluating execution.
    /// </summary>
    public sealed class BarkCondition : ConditionNode
    {
        private const string VisibleFunction = "visible";

        private const string MovingFunction = "moving";

        private const string HearingFunction = "can_hear";

        private readonly string function;
        private readonly string[] args;

        /// <summary>
        /// Retains parsed function/arguments only; actor identity and positions are resolved afresh from each
        /// evaluating run.
        /// </summary>
        public BarkCondition(string function, string[] args)
        {
            this.function = function;
            this.args = args;
        }

        /// <summary>
        /// Evaluates legacy visible/moving/near/can_hear syntax through the active spatial adapter; near
        /// retains its integer radius in adapter units.
        /// </summary>
        public override bool Evaluate(EvaluationContext context)
        {
            if (context.Services is not BarkWorldContext world)
            {
                throw new InvalidOperationException(function + " requires an ambient world context");
            }

            if (!world.TrySpatialPosition(args[0], out var a))
            {
                return false;
            }

            if (function == VisibleFunction)
            {
                return true;
            }

            if (function == MovingFunction)
            {
                return world.Space.Moving(world.Resolve(args[0]));
            }

            if (!world.TrySpatialPosition(args[1], out var b))
            {
                return false;
            }

            if (function == HearingFunction)
            {
                return world.Space.CanHear(world.Resolve(args[0]), world.Resolve(args[1]));
            }

            var radius = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
            return a.DistanceSquaredTo(b) <= radius * radius;
        }
    }
}
