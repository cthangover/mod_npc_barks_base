using System;
using System.Collections.Generic;
using Cthangover.Core.Mods;
using Cthangover.Core.Scenes;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Play;
using Godot;

namespace Cthangover.Mods.NpcBarks
{
    /// <summary>
    /// Historical RPG behaviour: cell distances, route holds, cardinal facing and the existing speech prefab.
    /// </summary>
    public sealed class RpgBarkWorld : BarkSpatialWorld
    {
        private readonly RpgWorldHost host;
        private readonly Dictionary<BarkRun, Control> bubbles = new();
        private Control layer;

        /// <summary>
        /// Borrowed historical RPG session supplying logical cell positions and route-held state.
        /// </summary>
        public RpgWorldSession Session { get; }

        /// <summary>
        /// Current RPG map identity, preserving the common director’s definition/event matching contract.
        /// </summary>
        public override string MapId
        {
            get
            {
                return Session.Map.ID;
            }
        }

        /// <summary>
        /// Borrows a cell-world session and optional visual host; headless conditions work without constructing
        /// speech presentation.
        /// </summary>
        public RpgBarkWorld(RpgWorldSession session, RpgWorldHost host = null)
        {
            Session = session;
            this.host = host;
        }

        /// <summary>
        /// Maps visible logical cell coordinates onto XY with zero Z; the player uses the existing session
        /// player position.
        /// </summary>
        public override bool TryPosition(string id, out Vector3 point)
        {
            point = default;
            if (id == null)
            {
                return false;
            }

            if (id == "player")
            {
                point = new Vector3(Session.Player.Position.X, Session.Player.Position.Y, 0);
                return true;
            }

            var actor = Session.Find(id);
            if (actor?.Visible != true)
            {
                return false;
            }

            point = new Vector3(actor.Actor.Position.X, actor.Actor.Position.Y, 0);
            return true;
        }

        /// <summary>
        /// Uses the existing player/instance movement state without advancing either controller.
        /// </summary>
        public override bool Moving(string id)
        {
            return id == "player" ? Session.Player.IsMoving : Session.Find(id)?.Actor.IsMoving == true;
        }

        /// <summary>
        /// Reads the existing NPC RouteHeld flag; player and missing-instance behavior remain unchanged.
        /// </summary>
        public override bool Held(string id)
        {
            return Session.Find(id)?.RouteHeld == true;
        }

        /// <summary>
        /// Writes the existing instance RouteHeld flag when the actor exists, retaining the legacy route
        /// reservation behavior.
        /// </summary>
        public override void Hold(string id, bool enabled)
        {
            var actor = Session.Find(id);
            if (actor != null)
            {
                actor.RouteHeld = enabled;
            }
        }

        /// <summary>
        /// Faces a stationary visible NPC cardinally toward the target’s cell without overriding a moving
        /// actor.
        /// </summary>
        public override void Look(string id, string target)
        {
            var actor = Session.Find(id);
            if (actor?.Visible != true || actor.Actor.IsMoving || !TryPosition(target, out var targetPosition))
            {
                return;
            }

            var difference = new Vector2(targetPosition.X, targetPosition.Y) - actor.Actor.Position;
            if (difference.LengthSquared() < 0.01f)
            {
                return;
            }

            actor.Actor.Face(
                Math.Abs(difference.X) > Math.Abs(difference.Y)
                    ? difference.X > 0 ? RpgDirection.Right : RpgDirection.Left
                    : difference.Y > 0 ? RpgDirection.Down : RpgDirection.Up
            );
        }

        /// <summary>
        /// Forwards the existing simulation-pause flag only when a visual RPG host was supplied.
        /// </summary>
        public override void SetPaused(bool enabled)
        {
            if (host != null)
            {
                host.SimulationPaused = enabled;
            }
        }

        /// <summary>
        /// Instantiates the unchanged speech prefab under an owned presentation layer; a context without a
        /// visual host creates no nodes.
        /// </summary>
        public override void Show(BarkRun run)
        {
            if (host == null)
            {
                return;
            }

            if (layer == null)
            {
                layer = TscnScenes.LoadAndBuild("scenes/ui/npc_speech_layer.tscn") as Control
                    ?? throw new InvalidOperationException("npc speech layer prefab is missing");
                host.AddChild(layer);
            }

            var bubble = TscnScenes.LoadAndBuild("scenes/ui/npc_speech.tscn") as Control
                ?? throw new InvalidOperationException("npc speech prefab is missing");
            layer.AddChild(bubble);
            bubbles.Add(run, bubble);
        }

        /// <summary>
        /// Queues the occurrence’s owned bubble for deletion and removes its presentation mapping.
        /// </summary>
        public override void Hide(BarkRun run)
        {
            if (bubbles.Remove(run, out var bubble) && GodotObject.IsInstanceValid(bubble))
            {
                bubble.QueueFree();
            }
        }

        /// <summary>
        /// Projects borrowed live runs into nonoverlapping cell-world bubbles without advancing scenario time.
        /// </summary>
        public override void Render(IReadOnlyList<BarkRun> runs, bool paused)
        {
            if (host == null)
            {
                return;
            }

            var occupied = new List<Rect2>();
            foreach (var run in runs)
            {
                RenderBubble(run, host, paused, occupied);
            }
        }

        /// <summary>
        /// Drops bubble mappings and queues the owned layer tree for deletion; the session and host remain
        /// borrowed.
        /// </summary>
        public override void Release()
        {
            bubbles.Clear();
            if (GodotObject.IsInstanceValid(layer))
            {
                layer.QueueFree();
            }

            layer = null;
        }

        /// <summary>
        /// Screen placement is presentation-only; hearing is a separate logical-cell test.
        /// </summary>
        private void RenderBubble(BarkRun run, RpgWorldHost host, bool paused, List<Rect2> occupied)
        {
            if (!bubbles.TryGetValue(run, out var bubble) || run.Text == null || run.Done)
            {
                return;
            }

            var actor = Session.Find(run.SpeakerId);
            var visible = !paused && actor?.Visible == true && host.View?.Presentation != null
                && actor.Actor.Position.DistanceSquaredTo(Session.Player.Position) <= 64;
            bubble.Visible = visible;
            if (!visible)
            {
                return;
            }

            var label = bubble.GetNode<Label>("Margin/Text");
            label.Text = run.Text;
            bubble.Size = new Vector2(280, Math.Max(60, label.GetMinimumSize().Y + 24));
            var position = actor.Actor.Position;
            var point = host.View.Presentation.ProjectToView(new Vector3(position.X + 0.5f, position.Y + 0.5f, 2.5f));
            var bounds = new Rect2(Vector2.Zero, host.Size);
            if (!bounds.HasPoint(point))
            {
                bubble.Visible = false;
                return;
            }

            var location = new Vector2(
                Math.Clamp(point.X - 140, 8, Math.Max(8, host.Size.X - 290)),
                Math.Clamp(point.Y - bubble.Size.Y - 16, 8, Math.Max(8, host.Size.Y - bubble.Size.Y - 8))
            );
            var rectangle = new Rect2(location, bubble.Size);
            for (var attempt = 0; attempt <= occupied.Count; attempt++)
            {
                var collision = false;
                foreach (var other in occupied)
                {
                    if (!rectangle.Grow(6).Intersects(other))
                    {
                        continue;
                    }

                    rectangle.Position = new Vector2(rectangle.Position.X, other.Position.Y - bubble.Size.Y - 14);
                    collision = true;
                    break;
                }

                if (!collision)
                {
                    break;
                }
            }

            if (rectangle.Position.Y < 8)
            {
                bubble.Visible = false;
                return;
            }

            bubble.Position = rectangle.Position;
            occupied.Add(rectangle);
            bubble.GetNode<Line2D>("Connector").Points = new[]
            {
                new Vector2(bubble.Size.X / 2, bubble.Size.Y + 10),
                point - rectangle.Position + new Vector2(0, 8)
            };
        }

        /// <summary>
        /// Preserves the existing periodic nearby NPC cardinal glance, skipping held, hidden, moving or routed
        /// actors.
        /// </summary>
        public override void PassiveAttention(double glance)
        {
            var look = glance % 3 < 0.6;
            foreach (var actor in Session.Instances)
            {
                if (actor.RouteHeld || !actor.Visible || actor.Actor.IsMoving
                    || actor.Current.Form != Cthangover.Mods.Rpg.Model.RpgObjectForm.Actor
                    || actor.Current.Category != "npc"
                    || actor.Current.Route != null)
                {
                    continue;
                }

                var difference = Session.Player.Position - actor.Actor.Position;
                if (difference.LengthSquared() > 25 || difference.LengthSquared() < 0.01f)
                {
                    continue;
                }

                if (!look)
                {
                    actor.Actor.Face(actor.Current.Facing);
                    continue;
                }

                actor.Actor.Face(
                    Math.Abs(difference.X) > Math.Abs(difference.Y)
                    ? difference.X > 0 ? RpgDirection.Right : RpgDirection.Left
                    : difference.Y > 0 ? RpgDirection.Down : RpgDirection.Up
                );
            }
        }
    }
}
