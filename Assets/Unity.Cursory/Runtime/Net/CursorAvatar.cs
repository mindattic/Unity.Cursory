using System;
using System.Collections.Generic;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>What a cursor currently holds, server side only (not replicated; the rope every peer draws is <see cref="CursorAvatar.Tether"/>, UC-T8).</summary>
    public enum GrabKind
    {
        None = 0,
        Block = 1,
        Shape = 2,
        Wall = 3,
        WireEnd = 4,
    }

    /// <summary>
    /// The player object: one cursor per connected player (BIBLE UC-§2, UC-LAW-1). A client sends only its raw
    /// position and one-shot grab/release intents (<see cref="Move"/>, <see cref="Grab"/>, <see cref="GrabShape"/>,
    /// <see cref="GrabWall"/>, <see cref="GrabWireEnd"/>, <see cref="Release"/>); the server decides what that does
    /// against the current level's <see cref="PhysicsWorld"/> (via <see cref="ServerSimulation.For"/>) and
    /// replicates the result. <see cref="Position"/> is the one thing every peer needs to draw this cursor.
    /// </summary>
    /// <example>
    /// Driving a cursor from code (a bot, a test, a replay) is the same as driving it from input: find the
    /// local player's cursor and call the intents on it. On a host the call runs the server handler directly.
    /// <code>
    /// CursorAvatar me = CursorAvatar.Local(session);   // null until the player object has spawned
    /// me.Move(new Vector2(5000f, 5000f));              // world pixels
    /// me.Grab("b1", 5060f, 5000f);                     // the raw click picks the rim anchor
    /// me.Move(new Vector2(5400f, 5000f));              // drag
    /// me.Release();
    /// </code>
    /// </example>
    /// <remarks>
    /// <para><see cref="Grab"/> and <see cref="GrabShape"/> both land on <see cref="PhysicsWorld.Grab"/> (it keys
    /// blocks and shapes the same way); the two RPCs exist because the client already knows which kind of body it
    /// clicked (Cursory's own <c>TryAttach</c> / <c>TryAttachShape</c> split) and because the server checks the kind
    /// matches, so a stale client cannot grab a shape through <see cref="Grab"/> after a reset relabels an id.</para>
    /// <para><see cref="GrabWall"/> lands the click on the wall's rim (UC-LAW-5) like any other grab, but a wall
    /// never moves, so nothing is added to <see cref="PhysicsWorld"/>: holding one is bookkeeping for a future
    /// tether (UC-T8). <see cref="GrabWireEnd"/> picks up a circuit wire's end through the session's
    /// <see cref="ServerSimulation.Progress"/> (UC-T9): it unplugs, follows <see cref="Move"/>, and on
    /// <see cref="Release"/> snaps onto a terminal within <see cref="Tuning.WireSnapRadius"/>.</para>
    /// <para>Static geometry (BIBLE UC-LAW-4) rides on this entity too: the server calls
    /// <see cref="ReceiveGeometry"/> on an avatar as soon as it spawns, and again on every avatar whenever
    /// <see cref="LevelLoader.ConsumeGeometryRebroadcast"/> says a level load or reset happened
    /// (<see cref="GeometryBroadcast"/>) -- never on the per-tick body sync.</para>
    /// </remarks>
    public partial class CursorAvatar : NetEntity
    {
        private static readonly Dictionary<NetSession, List<CursorAvatar>> s_BySession = new Dictionary<NetSession, List<CursorAvatar>>();
        private static readonly IReadOnlyList<CursorAvatar> s_None = Array.Empty<CursorAvatar>();

        /// <summary>Raised on every peer when a cursor spawns there (presentation, UC-T11; bots, UC-T14).</summary>
        public static event Action<CursorAvatar> Spawned;

        /// <summary>Raised on every peer just before a cursor despawns there.</summary>
        public static event Action<CursorAvatar> Despawned;

        /// <summary>Every cursor spawned on this peer under <paramref name="session"/>, in spawn order; empty when there are none.</summary>
        public static IReadOnlyList<CursorAvatar> All(NetSession session)
        {
            List<CursorAvatar> avatars;
            return session != null && s_BySession.TryGetValue(session, out avatars) ? avatars : s_None;
        }

        /// <summary>
        /// This peer's own cursor under <paramref name="session"/> (the one it owns and may call intents on); null
        /// on a dedicated server, which has no player, and before the player object has spawned.
        /// </summary>
        public static CursorAvatar Local(NetSession session)
        {
            foreach (CursorAvatar avatar in All(session))
            {
                if (avatar.IsOwner) return avatar;
            }

            return null;
        }

        /// <summary>The cursor owned by client <paramref name="clientId"/> under <paramref name="session"/>, as this peer sees it; null if none.</summary>
        public static CursorAvatar ForClient(NetSession session, ulong clientId)
        {
            foreach (CursorAvatar avatar in All(session))
            {
                if (avatar.OwnerClientId == clientId) return avatar;
            }

            return null;
        }

        private GrabKind _heldKind;
        private string _heldId;
        private Vector2? _heldAnchor;
        private bool _heldWireEndA;

        /// <summary>Server side: what this cursor currently holds; <see cref="GrabKind.None"/> when it holds nothing.</summary>
        public GrabKind Held => _heldKind;

        /// <summary>Server side: the id of whatever <see cref="Held"/> names; null when it holds nothing.</summary>
        public string HeldId => _heldId;

        /// <summary>
        /// Server side: for a <see cref="GrabKind.Wall"/> hold, where the grab landed on the wall's rim (UC-LAW-5,
        /// body-local world pixels, as <see cref="PhysicsWorld.WallAnchor"/> returns it); null otherwise (a
        /// block/shape anchor is <see cref="PhysicsWorld.TryGetGrab"/>'s job, and a wire end has none).
        /// </summary>
        public Vector2? HeldAnchor => _heldAnchor;

        /// <summary>Server side: for a <see cref="GrabKind.WireEnd"/> hold, true when it is end A, false for end B.</summary>
        public bool HeldWireEndA => _heldWireEndA;

        /// <summary>The current level's physics, or null off the server (or before one is bound for this session).</summary>
        private PhysicsWorld World
        {
            get
            {
                ServerSimulation sim = ServerSimulation.For(Session);
                return sim != null ? sim.World : null;
            }
        }

        /// <summary>The current level's goals and live wires (UC-T9), or null off the server or when nothing tracks them.</summary>
        private LevelProgress Progress
        {
            get
            {
                ServerSimulation sim = ServerSimulation.For(Session);
                return sim != null ? sim.Progress : null;
            }
        }

        // client -> server -------------------------------------------------------------------------------------

        /// <summary>The owner's raw cursor position, in world pixels (BIBLE UC-LAW-1: the server never trusts a body's position from a client, only this).</summary>
        [ToServer(Delivery = Delivery.Unreliable, RateLimit = 30)]
        public partial void Move(Vector2 worldPos);

        /// <summary>Grabs block <paramref name="blockId"/> at the point on its rim nearest the click (UC-LAW-5).</summary>
        [ToServer]
        public partial void Grab(string blockId, float clickX, float clickY);

        /// <summary>Grabs compound shape <paramref name="shapeId"/> at the point on its rim nearest the click (UC-LAW-5).</summary>
        [ToServer]
        public partial void GrabShape(string shapeId, float clickX, float clickY);

        /// <summary>Grabs wall <paramref name="wallId"/> at the point on its rim nearest the click; a wall never moves (UC-T8 tether only).</summary>
        [ToServer]
        public partial void GrabWall(string wallId, float clickX, float clickY);

        /// <summary>Grabs end A (<paramref name="endA"/> true) or end B of wire <paramref name="wireId"/>: it unplugs, follows the cursor and snaps on release.</summary>
        [ToServer]
        public partial void GrabWireEnd(string wireId, bool endA);

        /// <summary>Lets go of whatever this cursor holds. Harmless when it holds nothing.</summary>
        [ToServer]
        public partial void Release();

        // server -> owner ----------------------------------------------------------------------------------------

        /// <summary>The current level's static geometry (BIBLE UC-LAW-4): sent once when this avatar spawns, and again after every load or reset.</summary>
        [ToOwner]
        public partial void ReceiveGeometry(WallSync[] walls, LabelSync[] labels, int levelNumber);

        // synced state ---------------------------------------------------------------------------------------------

        /// <summary>Where every peer draws this cursor, in world pixels. The server writes it from <see cref="Move"/>.</summary>
        [Synced] private Vector2 _position;

        /// <summary>The most recently received <see cref="ReceiveGeometry"/>, on every peer; null before the first one arrives.</summary>
        public LevelGeometrySnapshot Geometry { get; private set; }

        // handlers ---------------------------------------------------------------------------------------------

        protected override void OnSpawned()
        {
            NetSession session = Session;
            if (session == null)
            {
                return;
            }

            List<CursorAvatar> avatars;
            if (!s_BySession.TryGetValue(session, out avatars))
            {
                avatars = new List<CursorAvatar>();
                s_BySession[session] = avatars;
            }

            if (IsServer) AssignIdentity();   // UC-T11: the seat's name and colour, for every peer to draw
            avatars.Add(this);
            Spawned?.Invoke(this);

            if (!IsServer)
            {
                return;
            }

            ServerSimulation sim = ServerSimulation.For(session);
            if (sim != null && sim.Loader != null)
            {
                SendGeometry(sim.Loader.World.Geometry());
            }
        }

        protected override void OnDespawned()
        {
            Despawned?.Invoke(this);
            StopFollowingSimulation();
            NetSession session = Session;
            List<CursorAvatar> avatars;
            if (session != null && s_BySession.TryGetValue(session, out avatars))
            {
                avatars.Remove(this);
                if (avatars.Count == 0)
                {
                    s_BySession.Remove(session);
                }
            }

            if (IsServer)
            {
                ReleaseHeld();
            }
        }

        private void OnMove(Vector2 worldPos, NetSender sender)
        {
            if (!float.IsFinite(worldPos.x) || !float.IsFinite(worldPos.y))
            {
                return;
            }

            worldPos = ConstrainCursor(worldPos);   // UC-T8: this player's cursor collision, then the leash
            Position = worldPos;
            World?.MoveCursor(OwnerClientId, worldPos.x, worldPos.y);
            if (_heldKind == GrabKind.WireEnd) Progress?.MoveCursor(OwnerClientId, worldPos.x, worldPos.y);
        }

        private void OnGrab(string blockId, float clickX, float clickY, NetSender sender)
        {
            PhysicsWorld world = World;
            if (world == null || !world.HasBody(blockId) || world.IsShape(blockId))
            {
                return;
            }

            if (world.Grab(OwnerClientId, blockId, clickX, clickY))
            {
                SetHeld(GrabKind.Block, blockId);
            }
        }

        private void OnGrabShape(string shapeId, float clickX, float clickY, NetSender sender)
        {
            PhysicsWorld world = World;
            if (world == null || !world.HasBody(shapeId) || !world.IsShape(shapeId))
            {
                return;
            }

            if (world.Grab(OwnerClientId, shapeId, clickX, clickY))
            {
                SetHeld(GrabKind.Shape, shapeId);
            }
        }

        private void OnGrabWall(string wallId, float clickX, float clickY, NetSender sender)
        {
            PhysicsWorld world = World;
            if (world == null || !world.HasWall(wallId))
            {
                return;
            }

            var (x, y) = world.WallAnchor(wallId, clickX, clickY);
            ReleaseHeld();
            SetHeld(GrabKind.Wall, wallId, new Vector2(x, y));
        }

        private void OnGrabWireEnd(string wireId, bool endA, NetSender sender)
        {
            ServerSimulation sim = ServerSimulation.For(Session);
            if (sim == null)
            {
                return;
            }

            LevelProgress progress = sim.Progress;
            if (progress != null ? progress.FindWire(wireId) == null : (sim.Loader == null || !HasWire(sim.Loader.World, wireId)))
            {
                return;
            }

            ReleaseHeld();
            // Picking the end up unplugs it; from here it follows Move and snaps onto a terminal on Release (UC-T9).
            progress?.GrabWireEnd(OwnerClientId, wireId, endA ? WireEnd.A : WireEnd.B);
            SetHeld(GrabKind.WireEnd, wireId);
            _heldWireEndA = endA;
        }

        private static bool HasWire(LevelData level, string wireId)
        {
            if (level == null || level.Wires == null || string.IsNullOrEmpty(wireId))
            {
                return false;
            }

            foreach (Wire wire in level.Wires)
            {
                if (wire.Id == wireId) return true;
            }

            return false;
        }

        private void OnRelease()
        {
            ReleaseHeld();
        }

        private void OnReceiveGeometry(WallSync[] walls, LabelSync[] labels, int levelNumber)
        {
            Geometry = new LevelGeometrySnapshot(levelNumber, walls, labels);
        }

        private void SendGeometry(LevelGeometry geometry)
        {
            ReceiveGeometry(GeometryWire.Walls(geometry), GeometryWire.Labels(geometry), geometry.LevelNumber);
        }

        private void SetHeld(GrabKind kind, string id, Vector2? anchor = null)
        {
            _heldKind = kind;
            _heldId = id;
            _heldAnchor = anchor;
            _heldWireEndA = false;
            RefreshTether();
        }

        private void ReleaseHeld()
        {
            if (_heldKind == GrabKind.Block || _heldKind == GrabKind.Shape)
            {
                World?.Release(OwnerClientId);
            }
            else if (_heldKind == GrabKind.WireEnd)
            {
                Progress?.ReleaseWireEnd(OwnerClientId);   // snaps onto a terminal within WireSnapRadius (UC-T9)
            }

            _heldKind = GrabKind.None;
            _heldId = null;
            _heldAnchor = null;
            _heldWireEndA = false;
            RefreshTether();
        }

        /// <summary>
        /// Server side: every avatar spawned under <paramref name="session"/> lets go of whatever it holds, as before a
        /// level's world is torn down (<see cref="ServerSimulation.RebuildWorld"/>). Safe with a null session.
        /// </summary>
        internal static void ReleaseAll(NetSession session)
        {
            List<CursorAvatar> avatars;
            if (session == null || !s_BySession.TryGetValue(session, out avatars))
            {
                return;
            }

            foreach (CursorAvatar avatar in new List<CursorAvatar>(avatars))
            {
                if (avatar.IsServer) avatar.ReleaseHeld();
            }
        }

        /// <summary>Server side: sends <paramref name="geometry"/> to every avatar spawned under <paramref name="session"/> (<see cref="GeometryBroadcast"/>).</summary>
        internal static void BroadcastGeometry(NetSession session, LevelGeometry geometry)
        {
            List<CursorAvatar> avatars;
            if (session == null || geometry == null || !s_BySession.TryGetValue(session, out avatars))
            {
                return;
            }

            // Copy: a handler could despawn an avatar (not expected today, but OnReceiveGeometry must not corrupt this loop).
            var targets = new List<CursorAvatar>(avatars);
            foreach (CursorAvatar avatar in targets)
            {
                if (!avatar.IsServer || !avatar.IsSpawned) continue;
                avatar.SendGeometry(geometry);
            }
        }
    }

    /// <summary>What <see cref="CursorAvatar.ReceiveGeometry"/> delivered: a level's static geometry (BIBLE UC-LAW-4), as every peer holds it.</summary>
    public sealed class LevelGeometrySnapshot
    {
        public LevelGeometrySnapshot(int levelNumber, WallSync[] walls, LabelSync[] labels)
        {
            LevelNumber = levelNumber;
            Walls = walls ?? System.Array.Empty<WallSync>();
            Labels = labels ?? System.Array.Empty<LabelSync>();
        }

        public int LevelNumber { get; }
        public WallSync[] Walls { get; }
        public LabelSync[] Labels { get; }
    }
}
