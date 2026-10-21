using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T9 on the server, no network: <see cref="LevelStateServer"/> runs the goal pass after every Physics2D step,
    /// so a block dragged onto its pad by a real grab solves the goal, and wiring level 14 through
    /// <see cref="LevelProgress"/> lights the bulb.
    /// </summary>
    public class LevelStateServerTests
    {
        private SimulationMode2D _mode;
        private ServerSimulation _sim;
        private ManualTickClock _clock;
        private LevelStateServer _state;

        [SetUp]
        public void SetUp()
        {
            _mode = Physics2D.simulationMode;
            _clock = new ManualTickClock(SimulationRate.TicksPerSecond);
            _sim = new ServerSimulation();
            _sim.Attach(_clock);
            _state = new LevelStateServer(_sim);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _state?.Dispose();
            _sim?.Dispose();
            _state = null;
            _sim = null;
            yield return null;
            Physics2D.simulationMode = _mode;
        }

        private static LevelData BlockAndPad() => new LevelData
        {
            Number = 1,
            Title = "UC-T9",
            Blocks = new List<Block> { new Block { Id = "b", X = 1000f, Y = 1000f, W = 100f, H = 100f, Mass = 1.0f } },
            BlockGoals = new List<BlockGoal> { new BlockGoal { Id = "g", X = 1400f, Y = 1000f, W = 300f, H = 300f, TargetBlockId = "b" } },
        };

        [Test]
        public void A_block_dragged_onto_its_pad_solves_the_goal_on_the_tick_it_arrives()
        {
            var level = BlockAndPad();
            _sim.World = new PhysicsWorld(level, "UC-T9 world");
            var progress = _state.Track(level);
            var won = new List<int>();
            _state.LevelWon += won.Add;
            Assert.That(progress.IsLevelSolved, Is.False);

            // Grab the right edge and pull well past the pad's centre.
            Assert.That(_sim.World.Grab(1, "b", 1050f, 1000f), Is.True);
            _sim.World.MoveCursor(1, 1450f, 1000f);
            var ticks = 0;
            while (!progress.IsGoalSolved("g") && ticks < 4 * SimulationRate.TicksPerSecond)
            {
                _clock.Step(1);
                ticks++;
            }

            Assert.That(progress.IsGoalSolved("g"), Is.True, "the block reached the pad within four seconds");
            var (x, _, _) = _sim.World.Pose("b");
            Assert.That(x, Is.GreaterThan(1250f).And.LessThan(1550f), "solved only once the centre is inside");
            Assert.That(progress.IsLevelSolved, Is.True);
            Assert.That(won, Is.EqualTo(new[] { 1 }), "LevelWon fires once, on the tick of the win");

            _clock.Step(5);
            Assert.That(won.Count, Is.EqualTo(1));
        }

        [Test]
        public void Wiring_level_14_lights_the_bulb_on_the_next_tick()
        {
            var level = LevelSeeds.Level(14).Clone();
            _sim.World = new PhysicsWorld(level, "UC-T9 circuit");
            var progress = _state.Track(level);

            void Connect(string wire, WireEnd end, string terminal)
            {
                var t = level.Terminals.First(x => x.Id == terminal);
                progress.GrabWireEnd(7, wire, end);
                progress.MoveCursor(7, t.X + 30f, t.Y - 30f);   // close enough: snaps within 90 px
                progress.ReleaseWireEnd(7);
            }

            Connect("w1", WireEnd.A, "bat+");
            Connect("w1", WireEnd.B, "r-a");
            Connect("w2", WireEnd.A, "r-b");
            Connect("w2", WireEnd.B, "b-a");
            Connect("w3", WireEnd.A, "b-b");
            Connect("w3", WireEnd.B, "bat-");
            Assert.That(progress.BulbLit, Is.False, "nothing is decided between ticks");

            _clock.Step(1);
            Assert.That(progress.BulbLit, Is.True);
            Assert.That(progress.IsLevelSolved, Is.True);
        }
    }

    /// <summary>
    /// UC-T9 over a real Unity Transport on loopback: a Host and a Client, one server-owned
    /// <see cref="LevelStateSync"/>. Goal and bulb state and the wires reach the client, and a client that joins
    /// late gets the wires as they are.
    /// </summary>
    public class LevelStateSyncTests : NetcodeIntegrationTest
    {
        private GameObject _prefab;
        private ServerSimulation _sim;
        private ManualTickClock _clock;
        private LevelStateServer _state;
        private SimulationMode2D _mode;
        private ushort _port;

        public LevelStateSyncTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 1;

        protected override void OnServerAndClientsCreated()
        {
            foreach (NetworkManager manager in m_NetworkManagers) manager.NetworkConfig.EnableSceneManagement = false;
            _port = TestPorts.UseFreePort(m_NetworkManagers);   // not the harness's fixed 7789 (UC-T21)
            _prefab = CreateNetworkObjectPrefab("LevelStateSync");
            _prefab.AddComponent<LevelStateSync>();
            base.OnServerAndClientsCreated();
        }

        protected override void OnNewClientCreated(NetworkManager networkManager)
        {
            TestPorts.UsePort(networkManager, _port);   // a late joiner dials the same free port
            base.OnNewClientCreated(networkManager);
        }

        protected override IEnumerator OnSetup()
        {
            _mode = Physics2D.simulationMode;
            _clock = new ManualTickClock(SimulationRate.TicksPerSecond);
            _sim = new ServerSimulation();
            _sim.Attach(_clock);
            yield return null;
        }

        protected override IEnumerator OnTearDown()
        {
            _state?.Dispose();
            _sim?.Dispose();
            _state = null;
            _sim = null;
            Physics2D.simulationMode = _mode;
            yield return null;
        }

        private IEnumerator SpawnSync(LevelData level, System.Action<LevelStateSync, LevelStateSync> assign)
        {
            _sim.World = new PhysicsWorld(level, "UC-T9 net world");
            var onServer = SpawnObject(_prefab, m_ServerNetworkManager).GetComponent<LevelStateSync>();
            _state = new LevelStateServer(_sim, onServer);
            _state.Track(level);
            ulong id = onServer.NetworkObjectId;
            NetworkManager client = m_ClientNetworkManagers[0];
            yield return WaitForConditionOrTimeOut(() => client.SpawnManager.SpawnedObjects.ContainsKey(id));
            AssertOnTimeout("the client never saw the LevelStateSync");
            assign(onServer, client.SpawnManager.SpawnedObjects[id].GetComponent<LevelStateSync>());
        }

        [UnityTest]
        public IEnumerator GoalSolved_AndWin_ReachTheClient()
        {
            var level = new LevelData
            {
                Number = 3,
                Blocks = new List<Block>
                {
                    new Block { Id = "a", X = 1000f, Y = 1000f, W = 100f, H = 100f, Mass = 1f },
                    new Block { Id = "b", X = 3000f, Y = 1000f, W = 100f, H = 100f, Mass = 1f },
                },
                BlockGoals = new List<BlockGoal>
                {
                    new BlockGoal { Id = "ga", X = 1000f, Y = 1000f, W = 400f, H = 400f, TargetBlockId = "a" }, // starts solved
                    new BlockGoal { Id = "gb", X = 3400f, Y = 1000f, W = 300f, H = 300f, TargetBlockId = "b" },
                },
            };
            LevelStateSync server = null, client = null;
            yield return SpawnSync(level, (s, c) => { server = s; client = c; });
            int wins = 0;
            client.Won += () => wins++;

            yield return WaitForConditionOrTimeOut(() => client.LevelNumber == 3 && client.GoalCount == 2 && client.SolvedMask == 0b01);
            AssertOnTimeout($"the client never saw goal 0 solved (level {client.LevelNumber}, goals {client.GoalCount}, mask {client.SolvedMask})");
            Assert.That(client.IsGoalSolved(0), Is.True);
            Assert.That(client.IsGoalSolved(1), Is.False);
            Assert.That(client.LevelSolved, Is.False);

            // Drag b onto its pad with a real grab; the server's ticks decide when it lands.
            Assert.That(_sim.World.Grab(1, "b", 3050f, 1000f), Is.True);
            _sim.World.MoveCursor(1, 3450f, 1000f);
            for (int i = 0; i < 4 * SimulationRate.TicksPerSecond && !_state.Progress.IsLevelSolved; i++) _clock.Step(1);
            Assert.That(_state.Progress.IsLevelSolved, Is.True, "the server saw the win");

            yield return WaitForConditionOrTimeOut(() => client.LevelSolved);
            AssertOnTimeout("the win never reached the client");
            Assert.That(client.SolvedMask, Is.EqualTo(0b11));
            Assert.That(server.LevelSolved, Is.True);
            Assert.That(wins, Is.EqualTo(1), "Won fires once on the client");
            Assert.That(LevelProgress.IsWon(client.GoalCount, client.SolvedMask, client.HasCircuit, client.BulbLit), Is.True,
                "a client decides the same win from the synced bits");
        }

        [UnityTest]
        public IEnumerator BulbAndWires_ReachTheClient_AndALateJoinerGetsTheWires()
        {
            var level = LevelSeeds.Level(14).Clone();
            LevelStateSync server = null, client = null;
            yield return SpawnSync(level, (s, c) => { server = s; client = c; });
            Assert.That(server.HasCircuit, Is.True);
            yield return WaitForConditionOrTimeOut(() => client.HasCircuit && client.LevelNumber == 14 && client.WireEnds.Length == 3);
            AssertOnTimeout("the client never saw level 14's circuit and wires");
            Assert.That(client.BulbLit, Is.False, "level 14 starts dark");

            var progress = _state.Progress;
            void Connect(string wire, WireEnd end, string terminal)
            {
                var t = level.Terminals.First(x => x.Id == terminal);
                progress.GrabWireEnd(7, wire, end);
                progress.MoveCursor(7, t.X + 40f, t.Y + 40f);
                progress.ReleaseWireEnd(7);
            }

            Connect("w1", WireEnd.A, "bat+");
            Connect("w1", WireEnd.B, "r-a");
            Connect("w2", WireEnd.A, "r-b");
            Connect("w2", WireEnd.B, "b-a");
            Connect("w3", WireEnd.A, "b-b");
            Connect("w3", WireEnd.B, "bat-");
            _clock.Step(1);
            Assert.That(server.BulbLit, Is.True);

            yield return WaitForConditionOrTimeOut(() => client.BulbLit && client.LevelSolved && client.WirePlugs.Length == 6 && client.WirePlugs.All(p => p >= 0));
            AssertOnTimeout($"the client never saw the lit bulb and plugged wires (lit {client.BulbLit}, plugs [{string.Join(",", client.WirePlugs)}])");
            AssertWiresMatch(level, client);

            // A held end follows its cursor and the client sees it move.
            progress.GrabWireEnd(7, "w2", WireEnd.B);
            progress.MoveCursor(7, 6000f, 7000f);
            _clock.Step(1);
            Assert.That(server.BulbLit, Is.False, "an unplugged end darkens the bulb");
            yield return WaitForConditionOrTimeOut(() => !client.BulbLit && client.WireEnds.Length == 3 && client.WireEnds[1].z == 6000f && client.WireEnds[1].w == 7000f);
            AssertOnTimeout("the client never saw the dragged wire end");
            Assert.That(client.WirePlugs[3], Is.EqualTo(LevelStateSync.Loose));

            // A late joiner asks for the wires as it spawns the entity.
            yield return CreateAndStartNewClient();
            NetworkManager late = m_ClientNetworkManagers[m_ClientNetworkManagers.Length - 1];
            ulong id = server.NetworkObjectId;
            yield return WaitForConditionOrTimeOut(() => late.SpawnManager.SpawnedObjects.ContainsKey(id));
            AssertOnTimeout("the late joiner never saw the LevelStateSync");
            var lateSync = late.SpawnManager.SpawnedObjects[id].GetComponent<LevelStateSync>();
            yield return WaitForConditionOrTimeOut(() => lateSync.WireEnds.Length == 3);
            AssertOnTimeout("the late joiner never got the wires");
            AssertWiresMatch(level, lateSync);
            Assert.That(lateSync.BulbLit, Is.False);
            Assert.That(lateSync.LevelNumber, Is.EqualTo(14));
        }

        private static void AssertWiresMatch(LevelData level, LevelStateSync sync)
        {
            LevelStateSync.Encode(level, out var ends, out var plugs);
            Assert.That(sync.WireEnds, Is.EqualTo(ends));
            Assert.That(sync.WirePlugs, Is.EqualTo(plugs));
            for (int i = 0; i < level.Wires.Count; i++)
            {
                Assert.That(LevelStateSync.TerminalId(level, sync.WirePlugs[2 * i]), Is.EqualTo(level.Wires[i].ATerminalId));
                Assert.That(LevelStateSync.TerminalId(level, sync.WirePlugs[2 * i + 1]), Is.EqualTo(level.Wires[i].BTerminalId));
            }
        }
    }
}
