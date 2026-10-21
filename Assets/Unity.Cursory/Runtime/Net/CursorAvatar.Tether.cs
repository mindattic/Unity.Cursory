using System.Collections.Generic;
using Unity.Cursory.Core;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// UC-T8: the cursor's tether and its two per-player settings. Each player chooses, for their own cursor only,
    /// whether their rope is segmented (catches on body corners, <see cref="TetherWrap"/>) and whether their cursor
    /// collides with walls and shapes (<see cref="CursorCollision"/>, radius <see cref="Tuning.CursorRadius"/>). Both
    /// are <c>[Synced]</c> on this avatar, so every peer sees each player's own choice; the server honours each
    /// cursor's settings in physics. (Cursory had one room-wide boolean for each, which every player's toggle overwrote.)
    /// </summary>
    /// <example>
    /// The pause menu (UC-T11) flips the local player's own settings; any peer reads anyone's.
    /// <code>
    /// CursorAvatar me = CursorAvatar.Local(session);
    /// me.SetSegmentedTether(true);     // [ToServer]: the server writes SegmentedTether, every peer sees it
    /// me.SetCursorCollision(!me.CursorCollision);
    /// bool theirs = CursorAvatar.ForClient(session, otherId).SegmentedTether;
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>The rope: while this cursor holds a block, shape or wall, the server keeps <see cref="Tether"/> (the chain
    /// from the rim anchor through any caught corners, world pixels) up to date every tick, re-anchors the grab's
    /// joint on the rope's last contact, and leashes <see cref="Position"/> within <see cref="Tuning.MaxPullPx"/> of
    /// that contact: the leash end is exactly where a pull saturates, so the drawn rope and the force agree.
    /// <see cref="TryGetRope"/> gives any peer the polyline to draw (<see cref="TetherRenderer"/> draws it).</para>
    /// <para>Cursor collision runs on the server on every <see cref="Move"/>, before the leash, so a colliding cursor's
    /// replicated position and its grab's target are never inside a wall or a shape.</para>
    /// </remarks>
    public partial class CursorAvatar
    {
        /// <summary>This player's choice: their rope catches on body corners. Off by default. Set it with <see cref="SetSegmentedTether"/>.</summary>
        [Synced] private bool _segmentedTether;

        /// <summary>This player's choice: their cursor is a disc that walls and shapes push out. Off by default. Set it with <see cref="SetCursorCollision"/>.</summary>
        [Synced] private bool _cursorCollision;

        /// <summary>The rope every peer draws for this cursor; empty when it holds nothing a rope goes to. The server writes it each tick.</summary>
        [Synced] private TetherSync _tether;

        private readonly List<(float X, float Y)> _chain = new List<(float X, float Y)>();
        private ServerSimulation _tetherSim;

        /// <summary>Turns this player's segmented tether on or off (the owner calls it; the server writes <see cref="SegmentedTether"/>).</summary>
        [ToServer]
        public partial void SetSegmentedTether(bool on);

        /// <summary>Turns this player's cursor collision on or off (the owner calls it; the server writes <see cref="CursorCollision"/>).</summary>
        [ToServer]
        public partial void SetCursorCollision(bool on);

        /// <summary>
        /// Any peer: the rope to draw, in world pixels, into <paramref name="points"/> (cleared first): the chain of
        /// <see cref="Tether"/>, then the cursor's end, held within <see cref="Tuning.MaxPullPx"/> of the contact.
        /// False (and nothing added) when this cursor has no rope.
        /// </summary>
        public bool TryGetRope(List<Vector2> points)
        {
            if (points == null) throw new System.ArgumentNullException(nameof(points));
            points.Clear();
            TetherSync tether = Tether;
            if (tether.Count == 0) return false;
            for (var i = 0; i < tether.Count; i++) points.Add(tether[i]);
            Vector2 contact = tether.Contact;
            Vector2 cursor = Position;
            var (x, y) = TetherGeometry.Leash(contact.x, contact.y, cursor.x, cursor.y);
            points.Add(new Vector2(x, y));
            return true;
        }

        private void OnSetSegmentedTether(bool on)
        {
            SegmentedTether = on;
            RefreshTether();
        }

        private void OnSetCursorCollision(bool on)
        {
            CursorCollision = on;
            if (on)
            {
                Vector2 resolved = ConstrainCursor(Position);
                if (resolved != Position)
                {
                    Position = resolved;
                    World?.MoveCursor(OwnerClientId, resolved.x, resolved.y);
                }
            }
        }

        /// <summary>Server: what a raw <see cref="Move"/> becomes for this player: out of solids if they collide, then leashed to their rope.</summary>
        private Vector2 ConstrainCursor(Vector2 worldPos)
        {
            PhysicsWorld world = World;
            if (CursorCollision && world != null)
            {
                var (cx, cy) = world.ResolveCursor(worldPos.x, worldPos.y);
                worldPos = new Vector2(cx, cy);
            }

            TetherSync tether = Tether;
            if (tether.Count > 0)
            {
                Vector2 contact = tether.Contact;
                var (lx, ly) = TetherGeometry.Leash(contact.x, contact.y, worldPos.x, worldPos.y);
                worldPos = new Vector2(lx, ly);
            }

            return worldPos;
        }

        /// <summary>
        /// Server: brings <see cref="Tether"/> up to date with what this cursor holds and where the body is now (after a
        /// grab, a release, a settings change, and every tick), re-anchors the grab on the rope's contact, and leashes
        /// <see cref="Position"/> to it.
        /// </summary>
        private void RefreshTether()
        {
            if (!IsServer || !IsSpawned) return;
            FollowSimulation();

            PhysicsWorld world = World;
            TetherSync next = default;
            if ((Held == GrabKind.Block || Held == GrabKind.Shape) && world != null
                && world.UpdateTether(OwnerClientId, SegmentedTether, _chain))
            {
                next = TetherSync.From(_chain);
            }
            else if (Held == GrabKind.Wall && HeldAnchor.HasValue && world != null && world.HasWall(HeldId))
            {
                var (wx, wy) = world.WallCentre(HeldId);
                next = TetherSync.Single(wx + HeldAnchor.Value.x, wy + HeldAnchor.Value.y);
            }

            if (!next.Equals(Tether)) Tether = next;
            if (next.Count == 0) return;

            // The body moved this tick: keep the cursor on the leash. The grab's target needs no clamp of its own: past
            // the leash the pull is already at its cap, along the same line from the contact.
            Vector2 contact = next.Contact;
            Vector2 cursor = Position;
            var (x, y) = TetherGeometry.Leash(contact.x, contact.y, cursor.x, cursor.y);
            var leashed = new Vector2(x, y);
            if (leashed != cursor) Position = leashed;
        }

        /// <summary>Server: ticks <see cref="RefreshTether"/> from whichever simulation this session is bound to now (it can be bound after this avatar spawns).</summary>
        private void FollowSimulation()
        {
            ServerSimulation sim = ServerSimulation.For(Session);
            if (sim == _tetherSim) return;
            StopFollowingSimulation();
            _tetherSim = sim;
            if (sim != null) sim.Stepped += OnSimulationStepped;
        }

        private void StopFollowingSimulation()
        {
            if (_tetherSim != null) _tetherSim.Stepped -= OnSimulationStepped;
            _tetherSim = null;
        }

        private void OnSimulationStepped(long tick)
        {
            if (this == null || !IsSpawned)
            {
                StopFollowingSimulation();
                return;
            }

            RefreshTether();
        }
    }
}
