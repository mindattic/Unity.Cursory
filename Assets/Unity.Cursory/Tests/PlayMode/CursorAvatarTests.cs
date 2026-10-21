using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T7: the cursor as the player object, over a real Unity Transport on loopback. Shared setup for both
    /// topologies below: a <see cref="CursorAvatar"/> player prefab (so <see cref="NetSession"/> spawns one per
    /// connection, UM-T9's seat machinery), a <see cref="NgoBodySync"/>-networked <see cref="PhysicsWorld"/> bound
    /// through <see cref="ServerSimulation"/>, and a <see cref="LevelLoader"/> whose geometry resend reaches every
    /// cursor through <see cref="GeometryBroadcast"/>.
    /// </summary>
    public abstract class CursorAvatarTestBase : NetcodeIntegrationTest
    {
        protected const string BlockId = "b";
        protected const string ShapeId = "s";
        protected const string WallId = "w";
        protected const string WireId = "wire";

        private GameObject _avatarPrefab;
        private GameObject _bodyPrefab;
        protected ServerSimulation Sim;
        protected GeometryBroadcast GeometrySync;

        protected CursorAvatarTestBase(HostOrServer hostOrServer) : base(hostOrServer)
        {
        }

        /// <summary>The world is 10000 px square (<see cref="WorldUnits.WorldSizePx"/>); everything here sits around its centre.</summary>
        protected const float OriginX = 5000f;
        protected const float OriginY = 5000f;

        protected static LevelData OneLevel()
        {
            return new LevelData
            {
                Number = 1,
                Title = "UC-T7",
                Blocks = new List<Block> { new Block { Id = BlockId, X = OriginX, Y = OriginY, W = 100f, H = 100f, Mass = 1.0f } },
                Shapes = new List<Shape>
                {
                    new Shape
                    {
                        Id = ShapeId,
                        X = OriginX + 400f,
                        Y = OriginY,
                        Mass = 1.0f,
                        Pieces = new List<ShapePiece> { new ShapePiece { LocalX = 0f, LocalY = 0f, HalfW = 50f, HalfH = 50f } },
                    },
                },
                Walls = new List<Wall> { new Wall { Id = WallId, X = OriginX - 400f, Y = OriginY, W = 50f, H = 400f } },
                Labels = new List<WorldLabel> { new WorldLabel { Id = "lbl", X = OriginX, Y = OriginY + 300f, Title = "UC-T7", Subtitle = "test" } },
                Wires = new List<Wire> { new Wire { Id = WireId, Ax = OriginX, Ay = OriginY - 600f, Bx = OriginX + 200f, By = OriginY - 600f } },
                BlockGoals = new List<BlockGoal> { new BlockGoal { Id = "g", X = OriginX, Y = 500f, W = 200f, H = 200f, TargetBlockId = BlockId } },
            };
        }

        /// <summary>The level <see cref="BindWorld"/> plays; <see cref="OneLevel"/> unless a suite needs another.</summary>
        protected virtual LevelData Level() => OneLevel();

        protected override void OnServerAndClientsCreated()
        {
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                manager.NetworkConfig.EnableSceneManagement = false;
            }

            _avatarPrefab = new GameObject("CursorAvatar");
            NetworkObject avatarNetworkObject = _avatarPrefab.AddComponent<NetworkObject>();
            NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab(avatarNetworkObject);
            _avatarPrefab.AddComponent<CursorAvatar>();

            _bodyPrefab = NgoBodySync.BuildTemplate();
            NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab(_bodyPrefab.GetComponent<NetworkObject>());

            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            GeometrySync?.Dispose();
            GeometrySync = null;
            Sim?.Dispose();
            Sim = null;
            yield return null;
        }

        /// <summary>Sets up <paramref name="manager"/>'s copy of <paramref name="session"/> to spawn and register the two UC-T7 prefabs. Every peer, before it starts.</summary>
        protected void PreparePeer(NetSession session, NetworkManager manager)
        {
            manager.NetworkConfig.PlayerPrefab = _avatarPrefab;
            session.Prefabs.Register(_avatarPrefab);
            NgoBodySync.Register(session, _bodyPrefab);
        }

        /// <summary>Binds a <see cref="ServerSimulation"/> (networked through <paramref name="session"/>) and a <see cref="GeometryBroadcast"/> to a fresh one-level world.</summary>
        protected void BindWorld(NetSession session)
        {
            var bodySync = new NgoBodySync(session, _bodyPrefab);
            Sim = new ServerSimulation { Loader = new LevelLoader(new[] { Level() }) };
            Sim.World = new PhysicsWorld(Sim.Loader.World, "UC-T7 world", bodySync);
            Sim.Bind(session);
            GeometrySync = new GeometryBroadcast();
            GeometrySync.Bind(Sim);
        }

        /// <summary>The <see cref="NetworkObjectId"/> NGO gave the server's networked block, once <see cref="BindWorld"/> has spawned it.</summary>
        protected ulong BlockNetworkObjectId => Sim.World.Rigidbody(BlockId).GetComponent<NetworkObject>().NetworkObjectId;

        protected static CursorAvatar OwnedAvatarOn(NetworkManager manager, ulong ownerClientId)
        {
            foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned.OwnerClientId != ownerClientId)
                {
                    continue;
                }

                CursorAvatar avatar = spawned.GetComponent<CursorAvatar>();
                if (avatar != null)
                {
                    return avatar;
                }
            }

            return null;
        }

        protected IEnumerator WaitForAvatar(NetworkManager manager, ulong ownerClientId, System.Action<CursorAvatar> assign)
        {
            CursorAvatar found = null;
            yield return WaitForConditionOrTimeOut(() => (found = OwnedAvatarOn(manager, ownerClientId)) != null);
            AssertOnTimeout(manager.name + " never saw a CursorAvatar owned by " + ownerClientId);
            assign(found);
        }

        protected IEnumerator WaitForNetworkObject(NetworkManager manager, ulong networkObjectId, System.Action<NetworkObject> assign)
        {
            NetworkObject found = null;
            yield return WaitForConditionOrTimeOut(() => manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out found));
            AssertOnTimeout(manager.name + " never saw NetworkObject " + networkObjectId);
            assign(found);
        }
    }

    /// <summary>Host + one client (NumberOfClients = 1): one cursor each, owned, moving, grabbing and replicating a networked body.</summary>
    public class CursorAvatarHostAndClientTests : CursorAvatarTestBase
    {
        private ushort _port;

        private NetSession _host;
        private NetSession _client;

        public CursorAvatarHostAndClientTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 1;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            _port = FreePort.Udp();
            base.OnServerAndClientsCreated();
            _host = TestSessions.Create(m_ServerNetworkManager);
            _client = TestSessions.Create(m_ClientNetworkManagers[0]);
            PreparePeer(_host, m_ServerNetworkManager);
            PreparePeer(_client, m_ClientNetworkManagers[0]);
        }

        protected override IEnumerator OnTearDown()
        {
            _client?.Dispose();
            _host?.Dispose();
            _client = _host = null;
            yield return base.OnTearDown();
        }

        private NetConfig Config() => new NetConfig { Port = _port, GameVersion = "uc-t7" };

        private IEnumerator StartAndJoin()
        {
            Assert.That(_host.StartHost(Config()), Is.True);
            yield return WaitForConditionOrTimeOut(() => _host.State == SessionState.Running);
            AssertOnTimeout("the host never reached Running");
            BindWorld(_host);

            Assert.That(_client.JoinByAddress(NetConfig.LoopbackAddress, _port, Config()), Is.True);
            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected);
            AssertOnTimeout("the client never connected");
        }

        [UnityTest]
        public IEnumerator EveryPeerGetsItsOwnOwnedCursor()
        {
            yield return StartAndJoin();

            CursorAvatar onHost = null;
            CursorAvatar onClientsHostCopy = null;
            CursorAvatar onClient = null;
            yield return WaitForAvatar(m_ServerNetworkManager, m_ServerNetworkManager.LocalClientId, a => onHost = a);
            yield return WaitForAvatar(m_ClientNetworkManagers[0], m_ServerNetworkManager.LocalClientId, a => onClientsHostCopy = a);
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _client.LocalClientId, a => onClient = a);

            Assert.That(onHost.IsOwner, Is.True);
            Assert.That(onClientsHostCopy.IsOwner, Is.False, "the client's copy of the host's cursor is not its own");
            Assert.That(onClient.IsOwner, Is.True);
        }

        [UnityTest]
        public IEnumerator Local_FindsEachPeersOwnCursor()
        {
            yield return StartAndJoin();
            yield return WaitForConditionOrTimeOut(() => CursorAvatar.All(_host).Count == 2 && CursorAvatar.All(_client).Count == 2);
            AssertOnTimeout("both peers should see both cursors");

            Assert.That(CursorAvatar.Local(_host).OwnerClientId, Is.EqualTo(m_ServerNetworkManager.LocalClientId));
            Assert.That(CursorAvatar.Local(_client).OwnerClientId, Is.EqualTo(_client.LocalClientId));
            Assert.That(CursorAvatar.ForClient(_client, m_ServerNetworkManager.LocalClientId).IsOwner, Is.False);
        }

        [UnityTest]
        public IEnumerator HostDrivesItsOwnCursorFromCode()
        {
            yield return StartAndJoin();
            CursorAvatar me = null;
            yield return WaitForConditionOrTimeOut(() => (me = CursorAvatar.Local(_host)) != null);
            AssertOnTimeout("the host's own cursor never spawned");

            me.Grab(BlockId, OriginX + 60f, OriginY);
            Assert.That(me.Held, Is.EqualTo(GrabKind.Block), "a host's own intent is handled locally, at once");
            me.Move(new Vector2(OriginX + 300f, OriginY));
            Assert.That(me.Position, Is.EqualTo(new Vector2(OriginX + 300f, OriginY)));
            me.Release();
            Assert.That(me.Held, Is.EqualTo(GrabKind.None));
            Assert.That(Sim.World.TryGetGrab(m_ServerNetworkManager.LocalClientId, out _, out _), Is.False);
        }

        [UnityTest]
        public IEnumerator Move_ReplicatesPositionToEveryPeer()
        {
            yield return StartAndJoin();
            CursorAvatar onClient = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _client.LocalClientId, a => onClient = a);
            CursorAvatar onHostsCopy = null;
            yield return WaitForAvatar(m_ServerNetworkManager, _client.LocalClientId, a => onHostsCopy = a);

            onClient.Move(new Vector2(12f, -34f));

            yield return WaitForConditionOrTimeOut(() => onHostsCopy.Position == new Vector2(12f, -34f));
            AssertOnTimeout("Move never replicated to the host's copy (" + onHostsCopy.Position + ")");
            yield return WaitForConditionOrTimeOut(() => onClient.Position == new Vector2(12f, -34f));
            AssertOnTimeout("Move never replicated back to the owner's own copy");
        }

        [UnityTest]
        public IEnumerator Grab_MovesTheBody_AndReplicatesItToTheClient()
        {
            yield return StartAndJoin();
            CursorAvatar onClient = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _client.LocalClientId, a => onClient = a);

            Rigidbody2D serverBody = Sim.World.Rigidbody(BlockId);
            float startX = serverBody.position.x;

            onClient.Grab(BlockId, OriginX + 60f, OriginY);
            yield return WaitForConditionOrTimeOut(() => Sim.World.TryGetGrab(_client.LocalClientId, out _, out _));
            AssertOnTimeout("the server never recorded the grab");

            onClient.Move(new Vector2(OriginX + 400f, OriginY));

            ulong blockObjectId = BlockNetworkObjectId;
            NetworkObject clientCopy = null;
            yield return WaitForNetworkObject(m_ClientNetworkManagers[0], blockObjectId, n => clientCopy = n);

            float deadline = Time.realtimeSinceStartup + 3f;
            while (serverBody.position.x - startX < 0.2f && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(serverBody.position.x - startX, Is.GreaterThan(0.2f), "the grab should have moved the block (server)");

            deadline = Time.realtimeSinceStartup + 3f;
            // The client draws an interpolated copy a few ticks behind the server, so wait for it to catch up.
            while (clientCopy.transform.position.x - startX < 0.2f && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(clientCopy.transform.position.x - startX, Is.GreaterThan(0.2f),
                "the client's copy of the body should track the server's replicated pose");
            Assert.That(clientCopy.transform.position.x, Is.LessThanOrEqualTo(serverBody.position.x + 0.05f),
                "the client's copy follows the server; it never runs ahead of it");
            Assert.That(clientCopy.GetComponent<Rigidbody2D>().bodyType, Is.EqualTo(RigidbodyType2D.Kinematic),
                "a client never simulates a body (BIBLE UC-LAW-1)");

            onClient.Release();
            yield return WaitForConditionOrTimeOut(() => !Sim.World.TryGetGrab(_client.LocalClientId, out _, out _));
            AssertOnTimeout("the server never released the grab");
        }

        [UnityTest]
        public IEnumerator Geometry_ArrivesOnSpawn()
        {
            yield return StartAndJoin();
            CursorAvatar onClient = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _client.LocalClientId, a => onClient = a);

            yield return WaitForConditionOrTimeOut(() => onClient.Geometry != null);
            AssertOnTimeout("the client's cursor never received the level's geometry");
            Assert.That(onClient.Geometry.LevelNumber, Is.EqualTo(1));
            Assert.That(onClient.Geometry.Walls.Select(w => w.Id), Is.EqualTo(new[] { WallId }));
            Assert.That(onClient.Geometry.Labels.Select(l => l.Title), Is.EqualTo(new[] { "UC-T7" }));
        }
    }

    /// <summary>Dedicated server + two clients (NumberOfClients = 2): two cursors, each owned by its own client, isolated from one another.</summary>
    public class CursorAvatarServerAndClientsTests : CursorAvatarTestBase
    {
        private ushort _port;

        private NetSession _server;
        private NetSession _a;
        private NetSession _b;

        public CursorAvatarServerAndClientsTests() : base(HostOrServer.Server)
        {
        }

        protected override int NumberOfClients => 2;

        protected override bool CanStartServerAndClients() => false;

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

        private NetConfig Config() => new NetConfig { Port = _port, GameVersion = "uc-t7" };

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
        }

        [UnityTest]
        public IEnumerator EachClientOwnsOnlyItsOwnCursor()
        {
            yield return StartAndJoinBoth();

            CursorAvatar aOnServer = null;
            CursorAvatar bOnServer = null;
            yield return WaitForAvatar(m_ServerNetworkManager, _a.LocalClientId, x => aOnServer = x);
            yield return WaitForAvatar(m_ServerNetworkManager, _b.LocalClientId, x => bOnServer = x);

            Assert.That(aOnServer.NetworkObjectId, Is.Not.EqualTo(bOnServer.NetworkObjectId));
            Assert.That(_server.ConnectedClientIds, Is.EquivalentTo(new[] { _a.LocalClientId, _b.LocalClientId }));
        }

        [UnityTest]
        public IEnumerator TwoCursorsGrabTwoDifferentBodies_Independently()
        {
            yield return StartAndJoinBoth();
            CursorAvatar onA = null;
            CursorAvatar onB = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _a.LocalClientId, x => onA = x);
            yield return WaitForAvatar(m_ClientNetworkManagers[1], _b.LocalClientId, x => onB = x);

            onA.Grab(BlockId, OriginX + 60f, OriginY);
            onB.GrabShape(ShapeId, OriginX + 450f, OriginY);

            yield return WaitForConditionOrTimeOut(() =>
                Sim.World.TryGetGrab(_a.LocalClientId, out string aId, out _) && aId == BlockId
                && Sim.World.TryGetGrab(_b.LocalClientId, out string bId, out _) && bId == ShapeId);
            AssertOnTimeout("both grabs should have landed, one per cursor");

            onA.Release();
            yield return WaitForConditionOrTimeOut(() => !Sim.World.TryGetGrab(_a.LocalClientId, out _, out _));
            AssertOnTimeout("releasing one cursor's grab should not touch the other's");
            Assert.That(Sim.World.TryGetGrab(_b.LocalClientId, out string stillHeld, out _), Is.True);
            Assert.That(stillHeld, Is.EqualTo(ShapeId));
        }

        [UnityTest]
        public IEnumerator GrabWall_HoldsTheWallWithNoPhysicsEffect()
        {
            yield return StartAndJoinBoth();
            CursorAvatar aOnServer = null;
            yield return WaitForAvatar(m_ServerNetworkManager, _a.LocalClientId, x => aOnServer = x);
            CursorAvatar onA = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _a.LocalClientId, x => onA = x);

            onA.GrabWall(WallId, OriginX - 400f, OriginY + 150f);

            yield return WaitForConditionOrTimeOut(() => aOnServer.Held == GrabKind.Wall);
            AssertOnTimeout("the server never recorded the wall grab");
            Assert.That(aOnServer.HeldId, Is.EqualTo(WallId));
            Assert.That(aOnServer.HeldAnchor, Is.Not.Null);
            Assert.That(aOnServer.HeldAnchor.Value.y, Is.EqualTo(150f).Within(1e-3f), "the anchor lands on the rim nearest the click");
            Assert.That(Sim.World.TryGetGrab(_a.LocalClientId, out _, out _), Is.False, "a wall never becomes a PhysicsWorld grab");

            onA.Release();
            yield return WaitForConditionOrTimeOut(() => aOnServer.Held == GrabKind.None);
            AssertOnTimeout("Release never cleared the wall hold");
        }

        [UnityTest]
        public IEnumerator Local_IsNullOnADedicatedServer()
        {
            yield return StartAndJoinBoth();
            yield return WaitForConditionOrTimeOut(() => CursorAvatar.All(_server).Count == 2);
            AssertOnTimeout("the server should see both clients' cursors");
            Assert.That(CursorAvatar.Local(_server), Is.Null, "a dedicated server has no player of its own");
            Assert.That(CursorAvatar.ForClient(_server, _b.LocalClientId), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator GrabWireEnd_HoldsOnlyAWireTheLevelHas()
        {
            yield return StartAndJoinBoth();
            CursorAvatar aOnServer = null;
            yield return WaitForAvatar(m_ServerNetworkManager, _a.LocalClientId, x => aOnServer = x);
            CursorAvatar onA = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _a.LocalClientId, x => onA = x);

            onA.GrabWireEnd("no such wire", true);
            onA.GrabWireEnd(WireId, false);

            yield return WaitForConditionOrTimeOut(() => aOnServer.Held == GrabKind.WireEnd);
            AssertOnTimeout("the server never recorded the wire-end grab");
            Assert.That(aOnServer.HeldId, Is.EqualTo(WireId));
            Assert.That(aOnServer.HeldWireEndA, Is.False);

            onA.Release();
            yield return WaitForConditionOrTimeOut(() => aOnServer.Held == GrabKind.None);
            AssertOnTimeout("Release never cleared the wire-end hold");
        }

        /// <summary>UC-T9: a grabbed wire end unplugs, follows the cursor's Move, and snaps onto a terminal within 90 px on Release.</summary>
        [UnityTest]
        public IEnumerator GrabWireEnd_FollowsTheCursor_AndSnapsOntoATerminalOnRelease()
        {
            yield return StartAndJoinBoth();
            var terminal = new Terminal { Id = "t", X = OriginX + 1000f, Y = OriginY - 600f };
            Sim.Loader.World.Terminals.Add(terminal);
            var levelState = new LevelStateServer(Sim);
            try
            {
                levelState.Track(Sim.Loader.World);
                Assert.That(Sim.Progress, Is.SameAs(levelState.Progress));
                Wire wire = levelState.Progress.FindWire(WireId);

                CursorAvatar aOnServer = null;
                yield return WaitForAvatar(m_ServerNetworkManager, _a.LocalClientId, x => aOnServer = x);
                CursorAvatar onA = null;
                yield return WaitForAvatar(m_ClientNetworkManagers[0], _a.LocalClientId, x => onA = x);

                onA.GrabWireEnd(WireId, true);
                yield return WaitForConditionOrTimeOut(() => aOnServer.Held == GrabKind.WireEnd);
                AssertOnTimeout("the server never recorded the wire-end grab");

                var near = new Vector2(terminal.X + 50f, terminal.Y + 40f);   // 64 px off: inside WireSnapRadius
                yield return WaitForConditionOrTimeOut(() =>
                {
                    onA.Move(near);   // unreliable: resend until it lands
                    return wire.Ax == near.x && wire.Ay == near.y;
                });
                AssertOnTimeout("the held end never followed the cursor");
                Assert.That(wire.IsAPlugged, Is.False);

                onA.Release();
                yield return WaitForConditionOrTimeOut(() => wire.ATerminalId == "t");
                AssertOnTimeout("releasing the end near the terminal never plugged it in");
                Assert.That((wire.Ax, wire.Ay), Is.EqualTo((terminal.X, terminal.Y)), "the end moves onto the terminal");
                Assert.That(aOnServer.Held, Is.EqualTo(GrabKind.None));
            }
            finally
            {
                levelState.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator ANonOwner_CannotMoveAnotherCursor()
        {
            yield return StartAndJoinBoth();
            CursorAvatar aOnB = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[1], _a.LocalClientId, x => aOnB = x);
            var rejections = new List<RpcRejection>();
            _server.RpcRejected += rejections.Add;

            aOnB.Move(new Vector2(1f, 1f));

            yield return WaitForConditionOrTimeOut(() => rejections.Count == 1);
            AssertOnTimeout("the server never refused the non-owner's Move");
            Assert.That(rejections[0].Verdict, Is.EqualTo(RpcVerdict.NotOwner));
            Assert.That(rejections[0].SenderClientId, Is.EqualTo(_b.LocalClientId));
        }

        [UnityTest]
        public IEnumerator LevelResetRebroadcastsGeometryToEveryCursor()
        {
            yield return StartAndJoinBoth();
            CursorAvatar onA = null;
            CursorAvatar onB = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _a.LocalClientId, x => onA = x);
            yield return WaitForAvatar(m_ClientNetworkManagers[1], _b.LocalClientId, x => onB = x);
            yield return WaitForConditionOrTimeOut(() => onA.Geometry != null && onB.Geometry != null);
            AssertOnTimeout("both cursors should already have the level's geometry from spawning");

            Sim.Loader.Reset();

            yield return WaitForConditionOrTimeOut(() => onA.Geometry.Walls.Length == 1 && onB.Geometry.Walls.Length == 1);
            AssertOnTimeout("the reset's geometry resend never reached both cursors");
        }
    }
}
