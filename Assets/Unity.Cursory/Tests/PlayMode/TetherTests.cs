using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>UC-T8: <see cref="PhysicsWorld"/>'s side of the tether: the rope's contact re-anchors the grab, and cursor collision.</summary>
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

        [Test]
        public void SegmentedTether_ReanchorsTheGrabOnTheCaughtCorner()
        {
            _world = new PhysicsWorld(Level(), "UC-T8 world");
            Assert.That(_world.Grab(Cursor, BlockId, 60f, 0f), Is.True);
            Assert.That(_world.MoveCursor(Cursor, -230f, -30f), Is.True);   // round behind the block
            var chain = new List<(float X, float Y)>();

            Assert.That(_world.UpdateTether(Cursor, true, chain), Is.True);

            Assert.That(chain, Has.Count.EqualTo(2));
            Assert.That(chain[0].X, Is.EqualTo(50f).Within(1e-3f));
            Assert.That(chain[1].X, Is.EqualTo(50f).Within(1e-3f));
            Assert.That(chain[1].Y, Is.EqualTo(-50f).Within(1e-3f), "caught on the bottom-right corner");
            var joint = _world.Rigidbody(BlockId).GetComponent<TargetJoint2D>();
            Assert.That(joint.anchor.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(joint.anchor.y, Is.EqualTo(-0.5f).Within(1e-5f), "the grab now pulls the corner");
            Assert.That(_world.TryGetGrab(Cursor, out _, out var anchor), Is.True);
            Assert.That(anchor, Is.EqualTo((50f, 0f)), "the grab's own rim anchor is unchanged");
        }

        [Test]
        public void StraightTether_PullsOnTheRimAnchor()
        {
            _world = new PhysicsWorld(Level(), "UC-T8 world");
            _world.Grab(Cursor, BlockId, 60f, 0f);
            _world.MoveCursor(Cursor, -230f, -30f);
            _world.UpdateTether(Cursor, true);
            var chain = new List<(float X, float Y)>();

            Assert.That(_world.UpdateTether(Cursor, false, chain), Is.True);

            Assert.That(chain, Has.Count.EqualTo(1), "a straight rope is just its anchor");
            var joint = _world.Rigidbody(BlockId).GetComponent<TargetJoint2D>();
            Assert.That(joint.anchor.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(joint.anchor.y, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void UpdateTether_IsFalseWhenNothingIsHeld()
        {
            _world = new PhysicsWorld(Level(), "UC-T8 world");
            var chain = new List<(float X, float Y)> { (1f, 1f) };
            Assert.That(_world.UpdateTether(Cursor, true, chain), Is.False);
            Assert.That(chain, Is.Empty);
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
    /// UC-T8 over the network: a dedicated server and two clients, each choosing their own tether settings. The
    /// bodies are Mass 2.0, which one cursor cannot move, so a rope's shape holds still while it is checked.
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
            _port = TestPorts.FreeUdpPort();
            base.OnServerAndClientsCreated();
            _server = NewSession(m_ServerNetworkManager);
            _a = NewSession(m_ClientNetworkManagers[0]);
            _b = NewSession(m_ClientNetworkManagers[1]);
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
        public IEnumerator Settings_AreEachPlayersOwn_AndEveryPeerSeesThem()
        {
            yield return StartAndJoinBoth();
            Assert.That(_onA.SegmentedTether || _onA.CursorCollision, Is.False, "both settings start off");

            _onA.SetSegmentedTether(true);
            _onB.SetCursorCollision(true);

            yield return WaitForConditionOrTimeOut(() => _aOnServer.SegmentedTether && _bOnServer.CursorCollision);
            AssertOnTimeout("the server never took the settings");
            Assert.That(_aOnServer.CursorCollision, Is.False, "B's collision toggle is not A's");
            Assert.That(_bOnServer.SegmentedTether, Is.False, "A's rope toggle is not B's");

            CursorAvatar aOnB = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[1], _a.LocalClientId, x => aOnB = x);
            yield return WaitForConditionOrTimeOut(() => aOnB.SegmentedTether && _onA.SegmentedTether && _onB.CursorCollision);
            AssertOnTimeout("the settings never replicated to every peer");

            _onA.SetSegmentedTether(false);
            yield return WaitForConditionOrTimeOut(() => !aOnB.SegmentedTether);
            AssertOnTimeout("turning it off never replicated");
            Assert.That(_bOnServer.CursorCollision, Is.True, "and B's setting is untouched");
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
        public IEnumerator CursorCollision_PushesOnlyThatPlayersCursorOutOfAWall()
        {
            yield return StartAndJoinBoth();
            _onA.SetCursorCollision(true);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.CursorCollision);
            AssertOnTimeout("A's collision never reached the server");

            var insideTheWall = new Vector2(OriginX - 400f, OriginY);
            _onA.Move(insideTheWall);
            _onB.Move(insideTheWall);

            var ejected = new Vector2(OriginX - 400f + 25f + Tuning.CursorRadius, OriginY);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.Position == ejected && _bOnServer.Position == insideTheWall);
            AssertOnTimeout("A should rest on the wall's edge and B inside it (A " + _aOnServer.Position + ", B " + _bOnServer.Position + ")");
            yield return WaitForConditionOrTimeOut(() => _onA.Position == ejected);
            AssertOnTimeout("A's own client never saw its cursor pushed out");
        }

        [UnityTest]
        public IEnumerator SegmentedTether_WrapsOnlyThatPlayersRope()
        {
            yield return StartAndJoinBoth();
            _onA.SetSegmentedTether(true);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.SegmentedTether);
            AssertOnTimeout("A's rope setting never reached the server");

            // Both grab the middle of a right edge, then pull round behind the body, so a straight rope cuts it.
            _onA.Move(new Vector2(OriginX + 60f, OriginY));
            _onA.Grab(BlockId, OriginX + 60f, OriginY);
            _onB.Move(new Vector2(OriginX + 460f, OriginY));
            _onB.GrabShape(ShapeId, OriginX + 460f, OriginY);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.Held == GrabKind.Block && _bOnServer.Held == GrabKind.Shape);
            AssertOnTimeout("both grabs should land");

            _onA.Move(new Vector2(OriginX - 230f, OriginY - 30f));
            _onB.Move(new Vector2(OriginX + 400f - 230f, OriginY - 30f));

            yield return WaitForConditionOrTimeOut(() => _aOnServer.Tether.Count == 2);
            AssertOnTimeout("A's segmented rope never caught the block's corner (" + _aOnServer.Tether.Count + " points)");
            Assert.That(_aOnServer.Tether.Contact, Is.EqualTo(new Vector2(OriginX + 50f, OriginY - 50f)), "the bottom-right corner");
            Vector2 jointAnchor = Sim.World.Rigidbody(BlockId).GetComponent<TargetJoint2D>().anchor;
            Assert.That(jointAnchor.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(jointAnchor.y, Is.EqualTo(-0.5f).Within(1e-4f), "A's grab pulls the corner it caught");

            yield return WaitForConditionOrTimeOut(() => _bOnServer.Position == new Vector2(OriginX + 170f, OriginY - 30f));
            AssertOnTimeout("B's move never landed");
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(_bOnServer.Tether.Count, Is.EqualTo(1), "B's rope stays straight: B never turned segmenting on");
            Vector2 shapeAnchor = Sim.World.Rigidbody(ShapeId).GetComponent<TargetJoint2D>().anchor;
            Assert.That(shapeAnchor.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(shapeAnchor.y, Is.EqualTo(0f).Within(1e-4f), "B's grab still pulls its rim anchor");

            yield return WaitForConditionOrTimeOut(() => _onA.Tether.Count == 2 && _onB.Tether.Count == 1);
            AssertOnTimeout("the ropes never replicated to their owners");
        }

        [UnityTest]
        public IEnumerator Leash_HoldsTheCursorWithinMaxPullOfTheContact()
        {
            yield return StartAndJoinBoth();
            _onA.Move(new Vector2(OriginX + 60f, OriginY));
            _onA.Grab(BlockId, OriginX + 60f, OriginY);
            yield return WaitForConditionOrTimeOut(() => _aOnServer.Tether.Count == 1);
            AssertOnTimeout("the grab never made a rope");

            _onA.Move(new Vector2(OriginX + 2000f, OriginY));

            var leashEnd = new Vector2(OriginX + 50f + Tuning.MaxPullPx, OriginY);
            yield return WaitForConditionOrTimeOut(() => Vector2.Distance(_aOnServer.Position, leashEnd) < 0.01f);
            AssertOnTimeout("the cursor should stop at the leash end (" + _aOnServer.Position + ")");

            yield return WaitForConditionOrTimeOut(() => Vector2.Distance(_onA.Position, leashEnd) < 0.01f);
            AssertOnTimeout("the leashed position never replicated");
            var rope = new List<Vector2>();
            Assert.That(_onA.TryGetRope(rope), Is.True);
            Assert.That(rope, Has.Count.EqualTo(2));
            Assert.That(rope[0], Is.EqualTo(new Vector2(OriginX + 50f, OriginY)), "from the rim anchor");
            Assert.That(Vector2.Distance(rope[0], rope[1]), Is.LessThanOrEqualTo(Tuning.MaxPullPx + 0.01f));
        }

        [UnityTest]
        public IEnumerator WallGrab_RopeRunsToTheWallsRim()
        {
            yield return StartAndJoinBoth();
            _onA.Move(new Vector2(OriginX - 400f, OriginY + 150f));
            _onA.GrabWall(WallId, OriginX - 400f, OriginY + 150f);

            yield return WaitForConditionOrTimeOut(() => _onA.Tether.Count == 1);
            AssertOnTimeout("the wall grab's rope never reached A");
            Assert.That(_onA.Tether.Contact, Is.EqualTo(new Vector2(OriginX - 400f + 25f, OriginY + 150f)), "on the wall's rim nearest the click");
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
            _onA.Move(new Vector2(OriginX + 200f, OriginY));
            yield return WaitForConditionOrTimeOut(() => aOnB.Tether.Count == 1 && aOnB.Position == new Vector2(OriginX + 200f, OriginY));
            AssertOnTimeout("B never saw A's rope");

            seenByB.Redraw();
            Assert.That(seenByB.Line.enabled, Is.True);
            Assert.That(seenByB.Line.positionCount, Is.EqualTo(2), "anchor, then cursor");
            Assert.That(seenByB.Line.GetPosition(0).x, Is.EqualTo(WorldUnits.ToMeters(OriginX + 50f)).Within(1e-4f));
            Assert.That(seenByB.Line.GetPosition(1).x, Is.EqualTo(WorldUnits.ToMeters(OriginX + 200f)).Within(1e-4f));
            Assert.That((Color32)seenByB.Line.startColor, Is.EqualTo((Color32)seenByB.Color), "the line keeps colour in 8 bits a channel");

            _onA.Release();
            yield return WaitForConditionOrTimeOut(() => aOnB.Tether.Count == 0);
            AssertOnTimeout("releasing never cleared the rope");
            seenByB.Redraw();
            Assert.That(seenByB.Line.enabled, Is.False);
        }
    }
}
