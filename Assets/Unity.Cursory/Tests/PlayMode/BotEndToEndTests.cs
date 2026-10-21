using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T14, in process: the nightly compose smoke without Docker. A dedicated server (the scene's own
    /// <see cref="NetBootstrap"/> + <see cref="LevelServer"/> on level 2) registers with an in-process rendezvous
    /// service and writes its join code to a file (<see cref="JoinCodeFile"/>); two <see cref="BotDriver"/> clients,
    /// roles A and B, read the code, join by it, grab the Mass 2.0 block's two ends and pull it around the wall onto
    /// the pad. Passing needs the server's own verdict (UC-T9's <see cref="LevelStateSync.LevelSolved"/>), seen by
    /// both bots, and the block's server-side pose inside the goal.
    /// </summary>
    public class BotEndToEndTests : NetcodeIntegrationTest
    {
        private const float BotTimeoutSeconds = 150f;

        private InProcessRendezvousServer _rendezvous;
        private NetBootstrap _serverBootstrap;
        private LevelServer _levelServer;
        private NetSession _a;
        private NetSession _b;
        private string _codeFile;
        private ushort _port;

        public BotEndToEndTests() : base(HostOrServer.Server)
        {
        }

        protected override int NumberOfClients => 2;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            foreach (NetworkManager manager in m_NetworkManagers) manager.NetworkConfig.EnableSceneManagement = false;
            _rendezvous = new InProcessRendezvousServer();
            _port = TestPorts.FreeUdpPort();
            _codeFile = Path.Combine(Application.temporaryCachePath, "uc-t14-join-code-" + System.Guid.NewGuid().ToString("N") + ".txt");

            // The server is wired as Main.unity wires it: NetBootstrap + LevelServer on the NetworkManager's object.
            // NetBootstrap is switched off before its Start (as a bot does), so a headless editor doesn't serve by
            // itself; the test starts the session with the rendezvous settings instead.
            _serverBootstrap = m_ServerNetworkManager.gameObject.AddComponent<NetBootstrap>();
            _serverBootstrap.enabled = false;
            _levelServer = m_ServerNetworkManager.gameObject.AddComponent<LevelServer>();
            typeof(LevelServer).GetField("startLevel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_levelServer, 2);

            // Each bot client: its own session with Cursory's prefabs, as LevelServer registers them on a client.
            _a = PrepareClient(m_ClientNetworkManagers[0]);
            _b = PrepareClient(m_ClientNetworkManagers[1]);
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            _a?.Dispose();
            _b?.Dispose();
            _serverBootstrap?.Session?.Leave();
            _rendezvous?.Dispose();
            _a = _b = null;
            _rendezvous = null;
            try
            {
                if (_codeFile != null && File.Exists(_codeFile)) File.Delete(_codeFile);
            }
            catch (IOException)
            {
            }

            yield return null;
        }

        private static NetSession PrepareClient(NetworkManager manager)
        {
            // NetSession's constructor is internal to the library; a game gets one from NetBootstrap (see CursorAvatarTests).
            var session = (NetSession)System.Activator.CreateInstance(typeof(NetSession), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { manager }, null);
            NetPrefabs.RegisterAll(session);
            manager.NetworkConfig.PlayerPrefab = NetPrefabs.LoadCursorAvatar();
            return session;
        }

        private NetConfig ServerConfig() => new NetConfig
        {
            Topology = Topology.Server,
            Port = _port,
            GameVersion = "uc-t14",
            RendezvousUrl = _rendezvous.Url,
            RendezvousSecret = InProcessRendezvousServer.Secret,
        };

        private NetConfig ClientConfig() => new NetConfig
        {
            GameVersion = "uc-t14",
            RendezvousUrl = _rendezvous.Url,
            JoinCodes = JoinCodeProvider.Rendezvous,
        };

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator TwoBotsJoinByCode_AndSolveLevel2Together()
        {
            yield return null; // LevelServer.Start registers the prefabs before the session starts
            NetSession server = _serverBootstrap.Session;
            JoinCodeFile.Attach(server, _codeFile);
            Assert.That(server.StartServer(ServerConfig()), Is.True, "StartServer");

            // The bots start before the code exists and wait for the file, as they do in the compose stack.
            var botA = BotDriver.Run(m_ClientNetworkManagers[0].gameObject, _a, BotRole.A, null, _codeFile, ClientConfig(), BotTimeoutSeconds);
            var botB = BotDriver.Run(m_ClientNetworkManagers[1].gameObject, _b, BotRole.B, null, _codeFile, ClientConfig(), BotTimeoutSeconds);

            float deadline = Time.realtimeSinceStartup + 20f;
            while (!(server.State == SessionState.Running && _levelServer.IsServing && File.Exists(_codeFile)) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(_levelServer.IsServing, Is.True, "the server never started serving");
            Assert.That(_levelServer.Loader.CurrentLevel, Is.EqualTo(2));
            Assert.That(File.Exists(_codeFile), Is.True, "the server never wrote its join code");
            Assert.That(File.ReadAllText(_codeFile).Trim(), Is.EqualTo(server.PublishedCodeText));
            Assert.That(_rendezvous.Count, Is.EqualTo(1), "the server registered with the rendezvous service");

            deadline = Time.realtimeSinceStartup + BotTimeoutSeconds + 10f;
            while ((botA.State != BotState.Solved || botB.State != BotState.Solved)
                   && botA.State != BotState.Failed && botB.State != BotState.Failed
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(botA.State, Is.EqualTo(BotState.Solved), "bot A: " + botA.FailureReason);
            Assert.That(botB.State, Is.EqualTo(BotState.Solved), "bot B: " + botB.FailureReason);
            Assert.That(_a.State, Is.EqualTo(SessionState.Connected), "bot A joined by code");
            Assert.That(_b.State, Is.EqualTo(SessionState.Connected), "bot B joined by code");
            Assert.That(server.ConnectedClientIds.Count, Is.EqualTo(2));
            Assert.That(botA.LevelNumber, Is.EqualTo(2));

            // The server's truth: both cursors held the block, and its pose sits on the pad.
            BlockGoal goal = botA.Plan.Goal;
            var (x, y, _) = _levelServer.Simulation.World.Pose(goal.TargetBlockId);
            Assert.That(goal.IsSatisfiedBy(x, y), Is.True, "the server's block at (" + x + ", " + y + ") is not on " + goal.Id);
            Assert.That(System.Linq.Enumerable.Count(_levelServer.Simulation.World.Grabbers), Is.EqualTo(2), "both bots hold the block");
        }
    }
}
