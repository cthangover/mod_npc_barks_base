namespace Cthangover.Mods.NpcBarks
{
    using System.Collections.Generic;
    using Godot;
    /// <summary>One live world adapter. Distances use adapter units; visibility is actor existence, never camera visibility.
    /// Reservations and look requests belong to a run. Optional presentation hooks keep the historical cell renderer isolated.</summary>
    public abstract class BarkSpatialWorld
    {
        public abstract string MapId { get; }
        public abstract bool TryPosition(string id, out Vector3 position);
        public abstract bool Moving(string id);
        public virtual bool CanReserve(string id) => true;
        public virtual bool Owns(string id) => true;
        public abstract bool Held(string id);
        public abstract void Hold(string id, bool enabled);
        public abstract void Look(string id, string target);
        public virtual bool CanHear(string listener, string speaker)
            => TryPosition(listener, out var a) && TryPosition(speaker, out var b) && a.DistanceSquaredTo(b) <= 64;
        public virtual void SetPaused(bool enabled) { }
        public virtual void Show(BarkRun run) { }
        public virtual void Hide(BarkRun run) { }
        public virtual void Render(IReadOnlyList<BarkRun> runs, bool paused) { }
        public virtual void PassiveAttention(double glance) { }
        public virtual void Release() { }
    }
}
