using System.Collections.Generic;
using Unity.Cursory.Core;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// UC-T8, UC-T33: the cursor's body and rope as every peer sees them, and the player's one cursor setting. The server
    /// writes <see cref="Position"/> (the cursor's physical body) and <see cref="Tether"/> (its rope's chain) after every
    /// tick; each player chooses, for their own cursor only, whether their pointer collides with walls and shapes
    /// (<see cref="CursorCollision"/>, radius <see cref="Tuning.CursorRadius"/>). It is <c>[Synced]</c> on this avatar, so
    /// every peer sees each player's own choice. (Cursory had one room-wide boolean, which every player's toggle overwrote.)
    /// </summary>
    /// <example>
    /// The pause menu (UC-T11) flips the local player's own setting; any peer reads anyone's, and draws anyone's rope.
    /// <code>
    /// CursorAvatar me = CursorAvatar.Local(session);
    /// me.SetCursorCollision(!me.CursorCollision);   // [ToServer]: the server writes CursorCollision, every peer sees it
    /// var rope = new List&lt;Vector2&gt;();
    /// if (CursorAvatar.ForClient(session, otherId).TryGetRope(rope)) Draw(rope);
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>The rope (UC-T33): while this cursor holds a block, shape or wall, <see cref="PhysicsWorld"/> ties its body
    /// to the rim with a max-length rope that wraps the body's corners (always on; the UC-T8 per-player "segmented
    /// tether" switch is gone). <see cref="Tether"/> is the chain from the rim anchor through any caught corners, world
    /// pixels; <see cref="TryGetRope"/> adds the cursor's end, and <see cref="TetherRenderer"/> draws it.</para>
    /// <para>Cursor collision runs on the server on every <see cref="Move"/>: a colliding player's pointer is pushed out of
    /// walls and shapes before the cursor's spring is aimed at it.</para>
    /// </remarks>
    public partial class CursorAvatar
    {
        /// <summary>This player's choice: their pointer is a disc that walls and shapes push out. Off by default. Set it with <see cref="SetCursorCollision"/>.</summary>
        [Synced] private bool _cursorCollision;

        /// <summary>The rope every peer draws for this cursor; empty when it holds nothing a rope goes to. The server writes it each tick.</summary>
        [Synced] private TetherSync _tether;

        private readonly List<(float X, float Y)> _chain = new List<(float X, float Y)>();
        private ServerSimulation _tetherSim;
        private bool _pointerKnown;

        /// <summary>Turns this player's cursor collision on or off (the owner calls it; the server writes <see cref="CursorCollision"/>).</summary>
        [ToServer]
        public partial void SetCursorCollision(bool on);

        /// <summary>
        /// Any peer: the rope to draw, in world pixels, into <paramref name="points"/> (cleared first): the chain of
        /// <see cref="Tether"/>, then the cursor (<see cref="Position"/>). False (and nothing added) when this cursor has no rope.
        /// </summary>
        public bool TryGetRope(List<Vector2> points)
        {
            if (points == null) throw new System.ArgumentNullException(nameof(points));
            points.Clear();
            TetherSync tether = Tether;
            if (tether.Count == 0) return false;
            for (var i = 0; i < tether.Count; i++) points.Add(tether[i]);
            points.Add(Position);
            return true;
        }

        private void OnSetCursorCollision(bool on)
        {
            CursorCollision = on;
            if (on)
            {
                Vector2 resolved = ConstrainPointer(Pointer);
                if (resolved != Pointer)
                {
                    Pointer = resolved;
                    PhysicsWorld world = World;
                    if (world != null) world.MoveCursor(OwnerClientId, resolved.x, resolved.y);
                    else Position = resolved;
                }
            }
        }

        /// <summary>Server: what a raw <see cref="Move"/> becomes for this player: out of walls and shapes if they collide.</summary>
        private Vector2 ConstrainPointer(Vector2 worldPos)
        {
            PhysicsWorld world = World;
            if (CursorCollision && world != null)
            {
                var (cx, cy) = world.ResolveCursor(worldPos.x, worldPos.y);
                worldPos = new Vector2(cx, cy);
            }

            return worldPos;
        }

        /// <summary>
        /// Server: brings <see cref="Position"/> and <see cref="Tether"/> up to date with the cursor's body and rope in the
        /// current world (after a grab, a release, and every tick). A world rebuilt since the last tick has no body for this
        /// cursor yet; it gets one at the player's pointer.
        /// </summary>
        private void RefreshTether()
        {
            if (!IsServer || !IsSpawned) return;
            FollowSimulation();

            PhysicsWorld world = World;
            TetherSync next = default;
            if (world != null && !world.IsDisposed)
            {
                if (_pointerKnown && !world.HasCursor(OwnerClientId)) world.MoveCursor(OwnerClientId, Pointer.x, Pointer.y);
                if (world.TryGetCursor(OwnerClientId, out var at))
                {
                    var position = new Vector2(at.X, at.Y);
                    if (position != Position) Position = position;
                }

                if ((Held == GrabKind.Block || Held == GrabKind.Shape || Held == GrabKind.Wall) && world.TryGetRope(OwnerClientId, _chain))
                {
                    next = TetherSync.From(_chain);
                }
            }

            if (!next.Equals(Tether)) Tether = next;
        }

        /// <summary>Server: ticks <see cref="RefreshTether"/> from whichever simulation this session is bound to now (it can be bound after this avatar spawns).</summary>
        private void FollowSimulation()
        {
            if (!IsServer) return;
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
