using System.Collections.Generic;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Play;
using Godot;

namespace Cthangover.Mods.NpcBarks
{
    /// <summary>
    /// Live roles of one execution. ASTs read it through EvaluationContext.Services, never capture it.
    /// </summary>
    public sealed class BarkWorldContext
    {
        /// <summary>
        /// Borrowed active adapter supplying all runtime spatial identity, reservation and perception services.
        /// </summary>
        public BarkSpatialWorld Space { get; }

        /// <summary>
        /// Legacy RPG session when this context uses the cell adapter; spatial consumers use Space and may
        /// receive null here.
        /// </summary>
        public RpgWorldSession Session
        {
            get
            {
                return (Space as RpgBarkWorld)?.Session;
            }
        }

        /// <summary>
        /// Per-run logical role mapping copied from the definition, including the initiating source speaker
        /// when present.
        /// </summary>
        public Dictionary<string, string> Roles { get; }

        /// <summary>
        /// Definition anchor actor identity used as the ordinary scoped-condition/flag context.
        /// </summary>
        public string Scope { get; }

        /// <summary>
        /// Copies definition roles for one run, optionally adding its source speaker; the legacy session
        /// overload constructs the same cell-space adapter.
        /// </summary>
        public BarkWorldContext(RpgWorldSession session, BarkDefinition definition, BarkSpeechEvent source = null)
            : this(new RpgBarkWorld(session), definition, source)
        {
        }

        /// <summary>
        /// Copies definition roles for one run, optionally adding its source speaker; the legacy session
        /// overload constructs the same cell-space adapter.
        /// </summary>
        public BarkWorldContext(BarkSpatialWorld space, BarkDefinition definition, BarkSpeechEvent source = null)
        {
            Space = space;
            Roles = new Dictionary<string, string>(definition.Actors);
            Scope = definition.Actors[definition.Anchor];
            if (source != null)
            {
                Roles["source"] = source.Speaker;
            }
        }

        /// <summary>
        /// Resolves a per-run role without retaining a native actor.
        /// </summary>
        public string Resolve(string role)
        {
            return role == "player" ? "player" : Roles.TryGetValue(role ?? "", out var id) ? id : null;
        }

        /// <summary>
        /// Unknown and hidden actors have no perceptible position; units belong to the active adapter.
        /// </summary>
        public bool TrySpatialPosition(string role, out Vector3 point)
        {
            return Space.TryPosition(Resolve(role), out point);
        }

        /// <summary>
        /// Legacy logical-cell coordinates. Spatial consumers use TrySpatialPosition to retain all three axes.
        /// </summary>
        public bool TryPosition(string role, out Vector2 point)
        {
            point = default;
            if (Space is not RpgBarkWorld || !TrySpatialPosition(role, out var spatial))
            {
                return false;
            }

            point = new Vector2(spatial.X, spatial.Y);
            return true;
        }
    }
}
