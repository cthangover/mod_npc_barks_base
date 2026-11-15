namespace Cthangover.Mods.NpcBarks
{
    using System.Collections.Generic;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Play;
    using Godot;

    /// <summary>Live roles of one execution. ASTs read it through EvaluationContext.Services, never capture it.</summary>
    public sealed class BarkWorldContext
    {
        public BarkSpatialWorld Space { get; }
        public RpgWorldSession Session => (Space as RpgBarkWorld)?.Session;
        public Dictionary<string, string> Roles { get; }
        public string Scope { get; }
        public BarkWorldContext(RpgWorldSession session, BarkDefinition definition, BarkSpeechEvent source = null)
            : this(new RpgBarkWorld(session), definition, source) { }
        public BarkWorldContext(BarkSpatialWorld space, BarkDefinition definition, BarkSpeechEvent source = null)
        {
            Space = space;
            Roles = new Dictionary<string, string>(definition.Actors);
            Scope = definition.Actors[definition.Anchor];
            if (source != null) Roles["source"] = source.Speaker;
        }
        /// <summary>Resolves a per-run role without retaining a native actor.</summary>
        public string Resolve(string role) => role == "player" ? "player" : Roles.TryGetValue(role ?? "", out var id) ? id : null;
        /// <summary>Unknown and hidden actors have no perceptible position; units belong to the active adapter.</summary>
        public bool TrySpatialPosition(string role, out Vector3 point) => Space.TryPosition(Resolve(role), out point);
        /// <summary>Legacy logical-cell coordinates. Spatial consumers use TrySpatialPosition to retain all three axes.</summary>
        public bool TryPosition(string role, out Vector2 point)
        {
            point = default;
            if (Space is not RpgBarkWorld || !TrySpatialPosition(role, out var spatial)) return false;
            point = new Vector2(spatial.X, spatial.Y); return true;
        }
    }
}
