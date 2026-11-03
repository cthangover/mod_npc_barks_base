namespace Cthangover.Mods.NpcBarks
{
    using System.Collections.Generic;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Play;
    using Godot;

    /// <summary>Live roles of one execution. ASTs read it through EvaluationContext.Services, never capture it.</summary>
    public sealed class BarkWorldContext
    {
        public RpgWorldSession Session { get; }
        public Dictionary<string, string> Roles { get; }
        public string Scope { get; }
        public BarkWorldContext(RpgWorldSession session, BarkDefinition definition, BarkSpeechEvent source = null)
        {
            Session = session;
            Roles = new Dictionary<string, string>(definition.Actors);
            Scope = definition.Actors[definition.Anchor];
            if (source != null) Roles["source"] = source.Speaker;
        }
        /// <summary>Unknown and hidden actors have no perceptible position.</summary>
        public bool TryPosition(string role, out Vector2 point)
        {
            point = default;
            if (role == "player") { point = Session.Player.Position; return true; }
            if (!Roles.TryGetValue(role, out var id)) return false;
            var actor = Session.Find(id);
            if (actor?.Visible != true) return false;
            point = actor.Actor.Position; return true;
        }
    }
}
