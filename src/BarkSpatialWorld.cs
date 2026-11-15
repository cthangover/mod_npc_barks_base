using System.Collections.Generic;
using Godot;

namespace Cthangover.Mods.NpcBarks
{
    /// <summary>One live world adapter. Distances use adapter units; visibility is actor existence, never camera visibility.
    /// Reservations and look requests belong to a run. Optional presentation hooks keep the historical cell renderer isolated.</summary>
    public abstract class BarkSpatialWorld
    {
        /// <summary>
        /// Stable logical map/scene identity for matching definitions and rejecting events from another spatial
        /// world.
        /// </summary>
        public abstract string MapId { get; }

        /// <summary>
        /// Resolves perceptible actor existence and coordinates in adapter units; camera visibility must not
        /// hide an otherwise present actor.
        /// </summary>
        public abstract bool TryPosition(string id, out Vector3 position);

        /// <summary>
        /// Reports actual actor movement so participant reservation does not interrupt traversal.
        /// </summary>
        public abstract bool Moving(string id);

        /// <summary>
        /// Optional stricter admission policy; the permissive default preserves historical cell-world behavior.
        /// </summary>
        public virtual bool CanReserve(string id)
        {
            return true;
        }

        /// <summary>
        /// Optional reservation ownership check; the permissive default preserves historical route-hold
        /// semantics.
        /// </summary>
        public virtual bool Owns(string id)
        {
            return true;
        }

        /// <summary>
        /// Reports whether an actor is currently held by an activity or route reservation.
        /// </summary>
        public abstract bool Held(string id);

        /// <summary>
        /// Acquires or releases this run’s participant reservation according to the active adapter’s ownership
        /// model.
        /// </summary>
        public abstract void Hold(string id, bool enabled);

        /// <summary>
        /// Requests attention from an owned actor to a logical addressee; the adapter chooses cardinal or
        /// semantic-head presentation.
        /// </summary>
        public abstract void Look(string id, string target);

        /// <summary>
        /// Default hearing uses an eight-unit radius and existing actor coordinates; metre-space adapters
        /// override obstruction and gain policy.
        /// </summary>
        public virtual bool CanHear(string listener, string speaker)
        {
            return TryPosition(listener, out var a) && TryPosition(speaker, out var b) && a.DistanceSquaredTo(b) <= 64;
        }

        /// <summary>
        /// Optional adapter-specific world pause; the empty default changes no external simulation state.
        /// </summary>
        public virtual void SetPaused(bool enabled)
        {
        }

        /// <summary>
        /// Optional presentation notification for every text occurrence, including actions without speech-
        /// reaction cues.
        /// </summary>
        public virtual void Show(BarkRun run)
        {
        }

        /// <summary>
        /// Optional notification that the runtime text action ended; a presenter may retain independent reading
        /// time.
        /// </summary>
        public virtual void Hide(BarkRun run)
        {
        }

        /// <summary>
        /// Optional frame presentation hook over borrowed live runs; it must not advance authoritative
        /// simulation.
        /// </summary>
        public virtual void Render(IReadOnlyList<BarkRun> runs, bool paused)
        {
        }

        /// <summary>
        /// Optional idle attention update driven by the director’s existing simulation cadence.
        /// </summary>
        public virtual void PassiveAttention(double glance)
        {
        }

        /// <summary>
        /// Releases only adapter-owned reservations and presentation on world replacement; providers retain
        /// their actor/resource ownership.
        /// </summary>
        public virtual void Release()
        {
        }
    }
}
