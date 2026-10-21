using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T13: the dedicated server's scene host. When the NetBootstrap session runs as a server, LevelServer loads
    /// the level library, builds level 1's PhysicsWorld and steps it on the network tick; the session ending stops it.
    /// </summary>
    public class LevelServerTests
    {
        private GameObject _netGo;
        private SimulationMode2D _mode;

        [SetUp]
        public void SetUp()
        {
            _mode = Physics2D.simulationMode;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_netGo != null)
            {
                Object.Destroy(_netGo);
                _netGo = null;
            }
            yield return null;
            Physics2D.simulationMode = _mode;
        }

        [UnityTest]
        public IEnumerator AServerSession_ServesLevelOne_AndStopsWhenItEnds()
        {
            _netGo = new GameObject("UC-T13 server");
            _netGo.SetActive(false);
            var manager = _netGo.AddComponent<NetworkManager>();
            _netGo.AddComponent<UnityTransport>();
            var bootstrap = _netGo.AddComponent<NetBootstrap>();
            var server = _netGo.AddComponent<LevelServer>();
            if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.EnableSceneManagement = false;
            typeof(NetBootstrap).GetField("port", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, 47962);
            typeof(NetBootstrap).GetField("topology", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, Topology.Server);
            typeof(NetBootstrap).GetField("gameVersion", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, "uc-t13");
            _netGo.SetActive(true);

            var session = bootstrap.Session;
            Assert.That(server.IsServing, Is.False, "nothing is served before the session runs");

            yield return null;   // a headless editor serves by itself from NetBootstrap.Start
            if (session.State == SessionState.Idle) Assert.That(bootstrap.StartServer(), Is.True, "StartServer");

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!(session.State == SessionState.Running && server.IsServing) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(session.State, Is.EqualTo(SessionState.Running));
            Assert.That(server.IsServing, Is.True, "a server session builds the level's world");
            Assert.That(server.Loader.CurrentLevel, Is.EqualTo(1));
            Assert.That(server.Simulation.IsAttached, Is.True, "the world steps on the network tick");
            Assert.That(ServerSimulation.For(session), Is.SameAs(server.Simulation), "CursorAvatar finds the served simulation");
            Assert.That(server.Simulation.Loader, Is.SameAs(server.Loader), "GeometryBroadcast reads the loader");
            Assert.That(manager.NetworkConfig.PlayerPrefab, Is.Not.Null.And.Matches<GameObject>(p => p.GetComponent<CursorAvatar>() != null),
                "the cursor avatar is the player object");

            long steps = server.Simulation.StepCount;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 0.5f) yield return null;
            Assert.That(server.Simulation.StepCount, Is.GreaterThan(steps), "ticks step the world");

            Assert.That(session.Leave(), Is.True);
            deadline = Time.realtimeSinceStartup + 5f;
            while (server.IsServing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(server.IsServing, Is.False, "the session ending disposes the world");
        }
    }
}
