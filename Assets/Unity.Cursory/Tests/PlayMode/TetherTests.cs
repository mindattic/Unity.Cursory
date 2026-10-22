using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T8, UC-T33: <see cref="PhysicsWorld"/>'s side of the rope: its wrap re-anchors the rope's joint on the caught
    /// corner with the length the corners leave, and cursor collision.
    /// </summary>
    public class TetherPhysicsTests
    {
        private const string BlockId = "b";
        private const string ShapeId = "s";
        private const ulong Cursor = 1;

        private PhysicsWorld _world;

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
            _world = null;
        }

        private static LevelData Level() => new LevelData
        {
            Number = 1,
            Title = "UC-T8",
            Blocks = new List<Block> { new Block { Id = BlockId, X = 0f, Y = 0f, W = 100f, H = 100f, Mass = 2.0f } },
            Shapes = new List<Shape>
            {
                new Shape
                {
                    Id = ShapeId, X = 1000f, Y = 0f, Mass = 2.0f,
                    Pieces = new List<ShapePiece> { new ShapePiece { LocalX = 0f, LocalY = 0f, HalfW = 100f, HalfH = 20f } },
                },
            },
            Walls = new List<Wall> { new Wall { Id = "w", X = -1000f, Y = 0f, W = 50f, H = 400f } },
        };

        /// <summary>Puts <see cref="Cursor"/>'s body at (<paramref name="x"/>, <paramref name="y"/>), as if it had got there, and re-wraps.</summary>
        private void PutCursorAt(float x, float y)
        {
            _world.CursorBody(Cursor).position = new Vector2(WorldUnits.ToMeters(x), WorldUnits.ToMeters(y));
            _world.RefreshRopes();
        }

        [Test]
        public void TheRopeWraps_AndItsJointPullsTheCaughtCorner()
        {
            _world = new PhysicsWorld(Level(), "UC-T8 world");
            Assert.That(_world.Grab(Cursor, BlockId, 60f, 0f), Is.True);
            PutCursorAt(-130f, -80f);   // round behind the block
            var chain = new List<(float X, float Y)>();

            Assert.That(_world.TryGetRope(Cursor, chain), Is.True);

            Assert.That(chain, Has.Count.EqualTo(2));
            Assert.That(chain[0].X, Is.EqualTo(50f).Within(1e-3f));
            Assert.That(chain[1].X, Is.EqualTo(50f).Within(1e-3f));
            Assert.That(chain[1].Y, Is.EqualTo(-50f).Within(1e-3f), "caught on the bottom-right corner");
            var rope = _world.CursorBody(Cursor).GetComponent<DistanceJoint2D>();
            Assert.That(rope.connectedBody, Is.SameAs(_world.Rigidbody(BlockId)));
            Assert.That(rope.connectedAnchor.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(rope.connectedAnchor.y, Is.EqualTo(-0.5f).Within(1e-5f), "the rope now pulls the corner");
            Assert.That(rope.distance, Is.EqualTo(WorldUnits.ToMeters(Tuning.RopeLengthPx - 50f)).Within(1e-5f),
                "the 50 px down the edge to the corner is rope no longer free");
            Assert.That(_world.TryGetGrab(Cursor, out _, out var anchor), Is.True);
            Assert.That(anchor, Is.EqualTo((50f, 0f)), "the grab's own rim anchor is unchanged");
        }

        [Test]
        public void AStraightRope_PullsOnTheRimAnchor()
        {
            _world = new PhysicsWorld(Level(), "UC-T8 world");
            _world.Grab(Cursor, BlockId, 60f, 0f);
            PutCursorAt(-130f, -80f);
            PutCursorAt(250f, 0f);   // straight out again: the corner lets go
            var chain = new List<(float X, float Y)>();

            Assert.That(_world.TryGetRope(Cursor, chain), Is.True);

            Assert.That(chain, Has.Count.EqualTo(1), "a straight rope is just its anchor");
            var rope = _world.CursorBody(Cursor).GetComponent<DistanceJoint2D>();
            Assert.That(rope.connectedAnchor.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(rope.connectedAnchor.y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(rope.maxDistanceOnly, Is.True, "a rope, not a rod: slack it pulls nothing");
            Assert.That(rope.distance, Is.EqualTo(WorldUnits.ToMeters(Tuning.RopeLengthPx)).Within(1e-5f));
        }

        [Test]
        public void TryGetRope_IsFalseWhenNothingIsHeld()
        {
            _world = new PhysicsWorld(Level(), "UC-T8 world");
            var chain = new List<(float X, float Y)> { (1f, 1f) };
            Assert.That(_world.TryGetRope(Cursor, chain), Is.False);
            Assert.That(chain, Is.Empty);
            Assert.That(_world.RopeLength(Cursor), Is.EqualTo(-1f));
        }

        [Test]
        public void Release_UntiesTheRope_ButKeepsTheCursor()
        {
            _world = new PhysicsWorld(Level(), "UC-T8 world");
            _world.Grab(Cursor, BlockId, 60f, 0f);

            Assert.That(_world.Release(Cursor), Is.True);

            Assert.That(_world.TryGetGrab(Cursor, out _, out _), Is.False);
            Assert.That(_world.HasCursor(Cursor), Is.True, "the cursor is free again, not gone");
            Assert.That(_world.CursorBody(Cursor).GetComponent<DistanceJoint2D>().enabled, Is.False, "out of the very next step");
            Assert.That(_world.Release(Cursor), Is.False);
        }

        [Test]
        public void ResolveCursor_PushesOutOfWallsAndShapes_ButNotBlocks()
        {
            _world = new PhysicsWorld(Level(), "UC-T8 world");

            Assert.That(_world.ResolveCursor(-1000f, 30f).X, Is.EqualTo(-1000f + 25f + Tuning.CursorRadius).Within(1e-3f));
            Assert.That(_world.ResolveCursor(1000f, 5f).Y, Is.EqualTo(20f + Tuning.CursorRadius).Within(1e-3f));
            Assert.That(_world.ResolveCursor(0f, 0f), Is.EqualTo((0f, 0f)), "a block is not solid to the cursor, as in Cursory");
            Assert.That(_world.WallCentre("w"), Is.EqualTo((-1000f, 0f)));
        }
    }

    /// <summary>
    /// UC-T8, UC-T33 over the network: a dedicated server and two clients. The bodies are Mass 2.0, which one cursor
    /// cannot move, so a rope's shape holds still while it is checked.
    /// </summary>
    public class TetherAvatarTests : CursorAvatarTestBase
    {
        private ushort _port;

        private NetSession _server;
        private NetSession _a;
        private NetSession _b;

        private CursorAvatar _aOnServer;
        private CursorAvatar _bOnServer;
        private CursorAvatar _onA;
        private CursorAvatar _onB;

        public TetherAvatarTests() : base(HostOrServer.Server)
        {
        }

        protected override int NumberOfClients => 2;

        protected override bool CanStartServerAndClients() => false;

        protected override LevelData Level()
        {
            LevelData level = OneLevel();
            level.Blocks[0].Mass = 2.0f;
            level.Shapes[0].Mass = 2.0f;
            return level;
        }

        protected override void OnServerAndClientsCreated()
        {
            _port = FreePort.Udp();
            base.OnServerAndClientsCreated();
            _server = TestSessions.Create(m_ServerNetworkManager);
            _a = TestSessions.Create(m_ClientNetworkManagers[0]);
            _b = TestSessions.Create(m_ClientNetworkManagers[1]);
            PreparePeer(_server, m_ServerNetworkManager);
            PreparePeer(_a, m_ClientNetworkManagers[0]);
            PreparePeer(_b, m_ClientNetworkManagers[1]);
        }

        protected override IEnumerator OnTearDown()
        {
            _a?.Dispose();
            _b?.Dispose();
            _server?.Dispose();
            _a = _b = _server = null;
            yield return base.OnTearDown();
        }

        private NetConfig Config() => new NetConfig { Port = _port, GameVersion = "uc-t8" };

        private IEnumerator StartAndJoinBoth()
        {
            Assert.That(_server.StartServer(Config()), Is.True);
            yield return WaitForConditionOrTimeOut(() => _server.State == SessionState.Running);
            AssertOnTimeout("the server never reached Running");
            BindWorld(_server);

            Assert.That(_a.JoinByAddress(NetConfig.LoopbackAddress, _port, Config()), Is.True);
            Assert.That(_b.JoinByAddress(NetConfig.LoopbackAddress, _port, Config()), Is.True);
            yield return WaitForConditionOrTimeOut(() => _a.State == SessionState.Connected && _b.State == SessionState.Connected);
            AssertOnTimeout("both clients never connected");

            yield return WaitForAvatar(m_ServerNetworkManager, _a.LocalClientId, x => _aOnServer = x);
            yield return WaitForAvatar(m_ServerNetworkManager, _b.LocalClientId, x => _bOnServer = x);
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _a.LocalClientId, x => _onA = x);
            yield return WaitForAvatar(m_ClientNetworkManagers[1], _b.LocalClientId, x => _onB = x);
        }

        [UnityTest]
        public IEnumerator CursorCollision_IsEachPlayersOwn_AndEveryPeerSeesIt()
        {
            yield return StartAndJoinBoth();
            Assert.That(_onA.CursorCollision, Is.False, "off by default");

            _onB.SetCursorCollision(true);

            yield return WaitForConditionOrTimeOut(() => _bOnServer.CursorCollision);
            AssertOnTimeout("the server never took the setting");
            Assert.That(_aOnServer.CursorCollision, Is.False, "B's collision toggle is not A's");

            CursorAvatar bOnA = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _b.LocalClientId, x => bOnA = x);
            yield return WaitForConditionOrTimeOut(() => bOnA.CursorCollision && _onB.CursorCollision);
            AssertOnTimeout("the setting never replicated to every peer");

            _onB.SetCursorCollision(false);
            yield return WaitForConditionOrTimeOut(() => !bOnA.CursorCollision);
            AssertOnTimeout("turning it off never replicated");
        }

        [UnityTest]
        public IEnumerator ANonOwner_CannotChangeAnotherPlayersSettings()
        {
            yield return StartAndJoinBoth();
            CursorAvatar aOnB = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[1], _a.LocalClientId, x => aOnB = x);
            var rejections = new List<RpcRejection>();
            _server.RpcRejected += rejections.Add;

            aOnB.SetCursorCollision(true);

            yield return WaitForConditionOrTimeOut(() => rejections.Count == 1);
            AssertOnTimeout("the server never refused B setting A's collision");
            Assert.That(rejections[0].Verdict, Is.EqualTo(RpcVerdict.NotOwner));
            Assert.That(_aOnServer.CursorCollision, Is.False);
        }

        [UnityTest]
        public IEnumerator CursorCollision_PushesOnlyThatPlayersPointerOutOfAWall()
        {
            yield return StartAndJoinBoth();
            _onA.SetCursorCollision(true);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.CursorCollision);
            AssertOnTimeout("A's collision never reached the server");

            var insideTheWall = new Vector2(OriginX - 400f, OriginY);
            _onA.Move(insideTheWall);
            _onB.Move(insideTheWall);

            var ejected = new Vector2(OriginX - 400f + 25f + Tuning.CursorRadius, OriginY);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.Pointer == ejected && _bOnServer.Pointer == insideTheWall);
            AssertOnTimeout("A should point at the wall's edge and B inside it (A " + _aOnServer.Pointer + ", B " + _bOnServer.Pointer + ")");
            yield return WaitForConditionOrTimeOut(() => _onA.Pointer == ejected && Vector2.Distance(_onA.Position, ejected) < 2f);
            AssertOnTimeout("A's own client never saw its cursor settle outside the wall (" + _onA.Position + ")");
        }

        /// <summary>UC-T33: the cursor is the server's body, following the pointer; every peer sees both.</summary>
        [UnityTest]
        public IEnumerator AFreeCursor_FollowsItsPointer_OnEveryPeer()
        {
            yield return StartAndJoinBoth();
            CursorAvatar aOnB = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[1], _a.LocalClientId, x => aOnB = x);
            var target = new Vector2(OriginX + 700f, OriginY + 300f);

            _onA.Move(target);

            yield return WaitForConditionOrTimeOut(() => Vector2.Distance(aOnB.Position, target) < 2f && aOnB.Pointer == target);
            AssertOnTimeout("B never saw A's cursor arrive at its pointer (" + aOnB.Position + ")");
            Assert.That(Sim.World.TryGetCursor(_a.LocalClientId, out var body), Is.True, "the cursor is a body in the server's world");
            Assert.That(Vector2.Distance(new Vector2(body.X, body.Y), target), Is.LessThan(2f));
        }

        /// <summary>UC-T33: a rope wraps on every player's grab (the UC-T8 switch is gone): pulled round, it catches the corner.</summary>
        [UnityTest]
        public IEnumerator EveryRope_WrapsRoundTheBody()
        {
            yield return StartAndJoinBoth();
            _onA.Move(new Vector2(OriginX + 60f, OriginY));
            _onA.Grab(BlockId, OriginX + 60f, OriginY);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.Held == GrabKind.Block);
            AssertOnTimeout("the grab should land");

            // Round under the block and back behind it, slowly enough that the cursor follows (the rope slack all the way).
            Vector2[] path = { new Vector2(OriginX + 150f, OriginY - 120f), new Vector2(OriginX, OriginY - 150f), new Vector2(OriginX - 100f, OriginY - 80f) };
            foreach (Vector2 p in path)
            {
                yield return WaitForConditionOrTimeOut(() =>
                {
                    _onA.Move(p);   // unreliable: resend until it lands
                    return Vector2.Distance(_aOnServer.Position, p) < 3f;
                });
                AssertOnTimeout("A's cursor never reached " + p + " (it is at " + _aOnServer.Position + ")");
            }

            yield return WaitForConditionOrTimeOut(() => _aOnServer.Tether.Count == 2);
            AssertOnTimeout("A's rope never caught the block's corner (" + _aOnServer.Tether.Count + " points)");
            Assert.That(_aOnServer.Tether.Contact, Is.EqualTo(new Vector2(OriginX + 50f, OriginY - 50f)), "the bottom-right corner");
            Vector2 ropeAnchor = Sim.World.CursorBody(_a.LocalClientId).GetComponent<DistanceJoint2D>().connectedAnchor;
            Assert.That(ropeAnchor.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(ropeAnchor.y, Is.EqualTo(-0.5f).Within(1e-4f), "A's rope pulls the corner it caught");

            yield return WaitForConditionOrTimeOut(() => _onA.Tether.Count == 2);
            AssertOnTimeout("the wrapped rope never replicated to its owner");
        }

        [UnityTest]
        public IEnumerator TheRope_HoldsTheCursorWithinItsLength_WhileThePointerRunsOn()
        {
            yield return StartAndJoinBoth();
            _onA.Move(new Vector2(OriginX + 60f, OriginY));
            _onA.Grab(BlockId, OriginX + 60f, OriginY);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.Tether.Count == 1);
            AssertOnTimeout("the grab never made a rope");

            _onA.Move(new Vector2(OriginX + 2000f, OriginY));

            var ropeEnd = new Vector2(OriginX + 50f + Tuning.RopeLengthPx, OriginY);
            yield return WaitForConditionOrTimeOut(() => Vector2.Distance(_aOnServer.Position, ropeEnd) < 1f);
            AssertOnTimeout("the cursor should stop at the rope's end (" + _aOnServer.Position + ")");
            Assert.That(_aOnServer.Pointer, Is.EqualTo(new Vector2(OriginX + 2000f, OriginY)), "the pointer runs on: it is only the ghost");

            yield return WaitForConditionOrTimeOut(() => Vector2.Distance(_onA.Position, ropeEnd) < 1f);
            AssertOnTimeout("the held position never replicated");
            var rope = new List<Vector2>();
            Assert.That(_onA.TryGetRope(rope), Is.True);
            Assert.That(rope, Has.Count.EqualTo(2));
            Assert.That(Vector2.Distance(rope[0], new Vector2(OriginX + 50f, OriginY)), Is.LessThan(1f), "from the rim anchor (the rope's snap may nudge the block a fraction of a pixel)");
            Assert.That(Vector2.Distance(rope[0], rope[1]), Is.LessThanOrEqualTo(Tuning.RopeLengthPx + 0.01f));
        }

        [UnityTest]
        public IEnumerator WallGrab_RopeRunsToTheWallsRim_AndHoldsTheCursor()
        {
            yield return StartAndJoinBoth();
            _onA.Move(new Vector2(OriginX - 400f, OriginY + 150f));
            _onA.GrabWall(WallId, OriginX - 400f, OriginY + 150f);

            yield return WaitForConditionOrTimeOut(() => _onA.Tether.Count == 1);
            AssertOnTimeout("the wall grab's rope never reached A");
            var rim = new Vector2(OriginX - 400f + 25f, OriginY + 150f);
            Assert.That(_onA.Tether.Contact, Is.EqualTo(rim), "on the wall's rim nearest the click");

            _onA.Move(new Vector2(OriginX + 2000f, OriginY + 150f));
            yield return WaitForConditionOrTimeOut(() => Vector2.Distance(_aOnServer.Position, rim + new Vector2(Tuning.RopeLengthPx, 0f)) < 1f);
            AssertOnTimeout("a wall's rope should hold the cursor at its end (" + _aOnServer.Position + ")");
        }

        [UnityTest]
        public IEnumerator Rope_IsDrawnOnClients_InThePlayersColour_AndClearedOnRelease()
        {
            yield return StartAndJoinBoth();
            CursorAvatar aOnB = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[1], _a.LocalClientId, x => aOnB = x);

            Assert.That(_aOnServer.GetComponent<TetherRenderer>(), Is.Null, "a dedicated server draws nothing");
            TetherRenderer onA = _onA.GetComponent<TetherRenderer>();
            TetherRenderer seenByB = aOnB.GetComponent<TetherRenderer>();
            Assert.That(onA, Is.Not.Null, "A draws its own rope");
            Assert.That(seenByB, Is.Not.Null, "and B draws A's");
            yield return null;   // LateUpdate picks up the synced player colour
            Assert.That(onA.Color, Is.EqualTo(_onA.PlayerColor), "UC-T11: the player's colour (their own, or their seat's fallback)");
            Assert.That(seenByB.Color, Is.EqualTo(onA.Color), "every peer draws A's rope in A's colour");
            onA.Redraw();
            Assert.That(onA.Line.enabled, Is.False, "no rope while A holds nothing");

            _onA.Move(new Vector2(OriginX + 60f, OriginY));
            _onA.Grab(BlockId, OriginX + 60f, OriginY);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.Held == GrabKind.Block);
            AssertOnTimeout("the grab never landed");
            var target = new Vector2(OriginX + 200f, OriginY);
            yield return WaitForConditionOrTimeOut(() =>
            {
                _onA.Move(target);
                return aOnB.Tether.Count == 1 && Vector2.Distance(aOnB.Position, target) < 1f;
            });
            AssertOnTimeout("B never saw A's rope (" + aOnB.Tether.Count + " points, cursor at " + aOnB.Position + ")");

            seenByB.Redraw();
            Assert.That(seenByB.Line.enabled, Is.True);
            Assert.That(seenByB.Line.positionCount, Is.EqualTo(2), "anchor, then cursor");
            Assert.That(seenByB.Line.GetPosition(0).x, Is.EqualTo(WorldUnits.ToMeters(OriginX + 50f)).Within(1e-4f));
            Assert.That(seenByB.Line.GetPosition(1).x, Is.EqualTo(WorldUnits.ToMeters(aOnB.Position.x)).Within(1e-4f), "to the cursor's body");
            Assert.That((Color32)seenByB.Line.startColor, Is.EqualTo((Color32)seenByB.Color), "the line keeps colour in 8 bits a channel");

            _onA.Release();
            yield return WaitForConditionOrTimeOut(() => aOnB.Tether.Count == 0);
            AssertOnTimeout("releasing never cleared the rope");
            seenByB.Redraw();
            Assert.That(seenByB.Line.enabled, Is.False);
        }
    }
}
