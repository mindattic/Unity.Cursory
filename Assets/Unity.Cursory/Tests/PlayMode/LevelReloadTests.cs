using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T20: a level reset or switch rebuilds the server's physics. On the tick after the loader loads or resets,
    /// <see cref="ServerSimulation"/> makes every cursor let go, tears down the old <see cref="PhysicsWorld"/> (bodies,
    /// walls, joints, networked copies) and builds the new level's, then resends the geometry. Host + one client.
    /// </summary>
    public class LevelReloadTests : CursorAvatarTestBase
    {
        private const ushort Port = 47994;
        private const string SecondBlockId = "b2";

        private NetSession _host;
        private NetSession _client;

        public LevelReloadTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 1;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            base.OnServerAndClientsCreated();
            _host = NewSession(m_ServerNetworkManager);
            _client = NewSession(m_ClientNetworkManagers[0]);
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

        private static NetConfig Config() => new NetConfig { Port = Port, GameVersion = "uc-t20" };

        /// <summary>Level 2: a different block, the same shape, and a second wall.</summary>
        private static LevelData SecondLevel()
        {
            LevelData level = OneLevel();
            level.Number = 2;
            level.Title = "UC-T20";
            level.Blocks[0].Id = SecondBlockId;
            level.Blocks[0].X = OriginX - 200f;
            level.BlockGoals[0].TargetBlockId = SecondBlockId;
            level.Walls.Add(new Wall { Id = "w2", X = OriginX + 800f, Y = OriginY, W = 50f, H = 400f });
            return level;
        }

        private IEnumerator StartWithGrab(System.Action<CursorAvatar, CursorAvatar> assign)
        {
            Assert.That(_host.StartHost(Config()), Is.True);
            yield return WaitForConditionOrTimeOut(() => _host.State == SessionState.Running);
            AssertOnTimeout("the host never reached Running");
            BindWorld(_host);
            Sim.Loader = new LevelLoader(new[] { OneLevel(), SecondLevel() });

            Assert.That(_client.JoinByAddress(NetConfig.LoopbackAddress, Port, Config()), Is.True);
            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected);
            AssertOnTimeout("the client never connected");

            CursorAvatar onClient = null;
            CursorAvatar onServer = null;
            yield return WaitForAvatar(m_ClientNetworkManagers[0], _client.LocalClientId, a => onClient = a);
            yield return WaitForAvatar(m_ServerNetworkManager, _client.LocalClientId, a => onServer = a);
            yield return WaitForConditionOrTimeOut(() => onClient.Geometry != null);
            AssertOnTimeout("the client never got the first level's geometry");
            assign(onClient, onServer);
        }

        /// <summary>Grabs the block's right edge and pulls until the server's body has visibly moved.</summary>
        private IEnumerator GrabAndPull(CursorAvatar onClient, CursorAvatar onServer, string blockId, float blockX)
        {
            onClient.Grab(blockId, blockX + 50f, OriginY);
            yield return WaitForConditionOrTimeOut(() => onServer.Held == GrabKind.Block && Sim.World.Grabbers.Contains(_client.LocalClientId));
            AssertOnTimeout("the server never recorded the grab");
            onClient.Move(new Vector2(blockX + 400f, OriginY));
            yield return WaitForConditionOrTimeOut(() => Sim.World.Pose(blockId).X - blockX > 5f);
            AssertOnTimeout("the grab never moved the block");
        }

        private IEnumerator WaitForRebuild(PhysicsWorld old)
        {
            yield return WaitForConditionOrTimeOut(() => Sim.World != null && !ReferenceEquals(Sim.World, old));
            AssertOnTimeout("the world was never rebuilt");
        }

        [UnityTest]
        public IEnumerator AResetMidGrab_ReleasesTheCursor_AndRebuildsTheLevelFromItsStart()
        {
            CursorAvatar onClient = null;
            CursorAvatar onServer = null;
            yield return StartWithGrab((c, s) => { onClient = c; onServer = s; });
            yield return GrabAndPull(onClient, onServer, BlockId, OriginX);

            PhysicsWorld old = Sim.World;
            ulong oldBlockObject = BlockNetworkObjectId;
            var rebuilt = new List<PhysicsWorld>();
            Sim.WorldRebuilt += rebuilt.Add;

            Sim.Loader.Reset();
            yield return WaitForRebuild(old);

            Assert.That(rebuilt, Is.EqualTo(new[] { Sim.World }), "one rebuild, announced");
            Assert.That(old.IsDisposed, Is.True, "the old world is torn down");
            Assert.That(onServer.Held, Is.EqualTo(GrabKind.None), "the cursor let go");
            Assert.That(onServer.HeldId, Is.Null);
            Assert.That(Sim.World.Grabbers, Is.Empty, "no grab joint carries over");
            var (x, y, angle) = Sim.World.Pose(BlockId);
            Assert.That(x, Is.EqualTo(OriginX).Within(0.5f), "the block is back where the level starts it");
            Assert.That(y, Is.EqualTo(OriginY).Within(0.5f));
            Assert.That(angle, Is.EqualTo(0f).Within(0.01f));
            Assert.That(Sim.World.BodySync, Is.SameAs(old.BodySync), "the new bodies are networked the same way");

            yield return WaitForConditionOrTimeOut(() => !m_ClientNetworkManagers[0].SpawnManager.SpawnedObjects.ContainsKey(oldBlockObject));
            AssertOnTimeout("the old level's networked block never despawned on the client");

            onClient.Move(new Vector2(OriginX + 400f, OriginY));
            for (int i = 0; i < 15; i++) yield return null;
            Assert.That(Sim.World.Pose(BlockId).X, Is.EqualTo(OriginX).Within(0.5f), "moving the cursor no longer drags anything");

            yield return GrabAndPull(onClient, onServer, BlockId, OriginX);
        }

        [UnityTest]
        public IEnumerator ALevelSwitchMidGrab_BuildsTheNewLevelsBodiesAndWalls_AndResendsItsGeometry()
        {
            CursorAvatar onClient = null;
            CursorAvatar onServer = null;
            yield return StartWithGrab((c, s) => { onClient = c; onServer = s; });
            yield return GrabAndPull(onClient, onServer, BlockId, OriginX);

            PhysicsWorld old = Sim.World;
            Sim.Loader.Load(2);
            yield return WaitForRebuild(old);

            Assert.That(onServer.Held, Is.EqualTo(GrabKind.None), "the cursor let go");
            Assert.That(Sim.World.Grabbers, Is.Empty);
            Assert.That(Sim.World.BodyIds, Is.EquivalentTo(new[] { SecondBlockId, ShapeId }), "level 2's bodies, not level 1's");
            Assert.That(Sim.World.HasWall(WallId) && Sim.World.HasWall("w2"), Is.True, "level 2's walls");
            Assert.That(Sim.World.Pose(SecondBlockId).X, Is.EqualTo(OriginX - 200f).Within(0.5f));

            yield return WaitForConditionOrTimeOut(() => onClient.Geometry.LevelNumber == 2 && onClient.Geometry.Walls.Length == 2);
            AssertOnTimeout("level 2's geometry never reached the client");

            yield return GrabAndPull(onClient, onServer, SecondBlockId, OriginX - 200f);
        }
    }
}
