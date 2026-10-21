using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MindAttic.Unity.Multiplayer;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Cursory.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T6: the server's Physics2D sim keeps Mass the one legible dial (BIBLE UC-LAW-2). One cursor moves
    /// a Mass 1.0 block but not a Mass 2.0 one; two cursors move the 2.0. The world steps only on server ticks
    /// (Script mode, 1/30 s per tick), and grabs land on the rim (UC-LAW-5), which is what gives a body torque.
    /// </summary>
    public class ServerPhysicsTests
    {
        private const string BlockId = "b";
        private const ulong CursorA = 1;
        private const ulong CursorB = 2;

        /// <summary>Two seconds of server ticks.</summary>
        private const int TwoSeconds = 2 * SimulationRate.TicksPerSecond;

        /// <summary>A pull this far past the anchor saturates the grab at its 60 N cap.</summary>
        private const float FarPullPx = 300f;

        private SimulationMode2D _mode;
        private ServerSimulation _sim;
        private ManualTickClock _clock;
        private GameObject _netGo;

        [SetUp]
        public void SetUp()
        {
            _mode = Physics2D.simulationMode;
            _clock = new ManualTickClock(SimulationRate.TicksPerSecond);
            _sim = new ServerSimulation();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _sim?.Dispose();
            _sim = null;
            if (_netGo != null)
            {
                Object.Destroy(_netGo);
                _netGo = null;
            }
            yield return null;
            Physics2D.simulationMode = _mode;
        }

        private static LevelData OneBlock(float mass, float w = 100f, float h = 100f) => new LevelData
        {
            Number = 1,
            Title = "UC-T6",
            Blocks = new List<Block> { new Block { Id = BlockId, X = 0f, Y = 0f, W = w, H = h, Mass = mass } },
        };

        private PhysicsWorld Load(LevelData level)
        {
            _sim.World = new PhysicsWorld(level, "UC-T6 world");
            _sim.Attach(_clock);
            return _sim.World;
        }

        private static float Distance(PhysicsWorld world)
        {
            var (x, y, _) = world.Pose(BlockId);
            return Mathf.Sqrt(x * x + y * y);
        }

        /// <summary>Grabs the block's right edge at height <paramref name="y"/> and pulls flat out to the right.</summary>
        private static void PullRight(PhysicsWorld world, ulong cursor, float y)
        {
            Assert.That(world.Grab(cursor, BlockId, 60f, y), Is.True);
            Assert.That(world.MoveCursor(cursor, 50f + FarPullPx, y), Is.True);
        }

        [Test]
        public void OneCursor_MovesMassOne()
        {
            var world = Load(OneBlock(1.0f));
            PullRight(world, CursorA, 0f);

            _clock.Step(TwoSeconds);

            var (x, y, _) = world.Pose(BlockId);
            Assert.That(x, Is.GreaterThan(20f), "60 N beats Mass 1.0's 40 N of friction");
            Assert.That(Mathf.Abs(y), Is.LessThan(1f), "a pull through the centre moves it straight");
        }

        [Test]
        public void OneCursor_DoesNotMoveMassTwo()
        {
            var world = Load(OneBlock(2.0f));
            PullRight(world, CursorA, 0f);

            _clock.Step(TwoSeconds);

            Assert.That(Distance(world), Is.LessThan(1f), "60 N cannot beat Mass 2.0's 80 N of friction");
            Assert.That(Mathf.Abs(world.Pose(BlockId).Angle), Is.LessThan(0.01f));
        }

        [Test]
        public void TwoCursors_MoveMassTwo()
        {
            var world = Load(OneBlock(2.0f));
            PullRight(world, CursorA, -30f);
            PullRight(world, CursorB, 30f);

            _clock.Step(TwoSeconds);

            var (x, _, angle) = world.Pose(BlockId);
            Assert.That(x, Is.GreaterThan(20f), "120 N beats Mass 2.0's 80 N of friction");
            Assert.That(Mathf.Abs(angle), Is.LessThan(0.05f), "two matched grabs either side of the centre don't turn it");
        }

        [Test]
        public void ReleasingOneOfTwo_StopsMassTwo()
        {
            var world = Load(OneBlock(2.0f));
            PullRight(world, CursorA, -30f);
            PullRight(world, CursorB, 30f);
            _clock.Step(TwoSeconds);
            Assert.That(world.Release(CursorB), Is.True);

            _clock.Step(TwoSeconds);   // coasts to a stop under friction and damping
            var settled = world.Pose(BlockId).X;
            _clock.Step(TwoSeconds);

            Assert.That(world.Pose(BlockId).X - settled, Is.LessThan(1f), "one cursor alone holds it no further");
        }

        [Test]
        public void Grab_LandsOnTheNearestEdge()
        {
            var world = Load(OneBlock(1.0f, 300f, 40f));

            Assert.That(world.Grab(CursorA, BlockId, 140f, 5f), Is.True);   // inside, 10 px from the right edge

            Assert.That(world.TryGetGrab(CursorA, out var id, out var anchor), Is.True);
            Assert.That(id, Is.EqualTo(BlockId));
            Assert.That(anchor.X, Is.EqualTo(150f).Within(1e-3f));
            Assert.That(anchor.Y, Is.EqualTo(5f).Within(1e-3f));
        }

        [Test]
        public void EdgeGrab_TurnsTheBody()
        {
            // A long bar grabbed at its end and pulled sideways: 60 N at 1.5 m is 90 N·m, past its 48 N·m of
            // rotational friction, so it turns (anti-clockwise for a pull up on the right end).
            var world = Load(OneBlock(1.0f, 300f, 40f));
            Assert.That(world.Grab(CursorA, BlockId, 160f, 0f), Is.True);
            Assert.That(world.MoveCursor(CursorA, 150f, FarPullPx), Is.True);

            _clock.Step(TwoSeconds);

            Assert.That(world.Pose(BlockId).Angle, Is.GreaterThan(0.1f));
        }

        [Test]
        public void Bodies_CarryTheMappedTuning()
        {
            var world = Load(OneBlock(2.0f));
            var rb = world.Rigidbody(BlockId);
            var friction = rb.GetComponent<FrictionJoint2D>();
            Assert.That(world.Grab(CursorA, BlockId, 60f, 0f), Is.True);
            var grab = rb.GetComponent<TargetJoint2D>();

            Assert.That(rb.mass, Is.EqualTo(10f).Within(1e-4f), "rb.mass = Mass × 5");
            Assert.That(rb.gravityScale, Is.EqualTo(0f));
            Assert.That(rb.linearDamping, Is.EqualTo(1.5f));
            Assert.That(rb.angularDamping, Is.EqualTo(2.0f));
            Assert.That(friction.connectedBody, Is.Null, "friction is against the world");
            Assert.That(friction.maxForce, Is.EqualTo(80f).Within(1e-4f), "maxForce = Mass × 40");
            Assert.That(friction.maxTorque, Is.EqualTo(96f).Within(1e-4f), "maxTorque = maxForce × 1.2");
            Assert.That(grab.maxForce, Is.EqualTo(60f));
            Assert.That(grab.frequency, Is.EqualTo(5f));
            Assert.That(grab.dampingRatio, Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(grab.autoConfigureTarget, Is.False);
            Assert.That(grab.anchor.x, Is.EqualTo(0.5f).Within(1e-5f), "100 px per metre: the right edge of a 100 px block");
        }

        [UnityTest]
        public IEnumerator WithoutTicks_NothingMoves()
        {
            var world = Load(OneBlock(1.0f));
            PullRight(world, CursorA, 0f);

            Assert.That(Physics2D.simulationMode, Is.EqualTo(SimulationMode2D.Script));
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

            Assert.That(Distance(world), Is.LessThan(1e-3f), "Physics2D only steps on a server tick");
            Assert.That(_sim.StepCount, Is.EqualTo(0));
        }

        [Test]
        public void EachTick_StepsOneThirtiethOfASecond()
        {
            Load(OneBlock(1.0f));
            var seen = new List<long>();
            _sim.Stepped += seen.Add;

            Assert.That(_clock.Advance(1.0 + 1e-9), Is.EqualTo(SimulationRate.TicksPerSecond));

            Assert.That(_sim.StepCount, Is.EqualTo(SimulationRate.TicksPerSecond));
            Assert.That(seen.Count, Is.EqualTo(SimulationRate.TicksPerSecond));
            Assert.That(SimulationRate.TickSeconds, Is.EqualTo(1f / 30f));
        }

        [Test]
        public void Detach_StopsStepping()
        {
            var world = Load(OneBlock(1.0f));
            PullRight(world, CursorA, 0f);
            _sim.Detach();

            _clock.Step(TwoSeconds);

            Assert.That(_sim.IsAttached, Is.False);
            Assert.That(Distance(world), Is.LessThan(1e-3f));
        }

        /// <summary>
        /// The real thing: a dedicated server's <see cref="NetSession"/> ticks NGO's NetworkTickSystem at 30 Hz,
        /// and the bound simulation steps once per tick while the session runs, and stops when it ends.
        /// </summary>
        [UnityTest]
        public IEnumerator BoundToAServerSession_StepsOnTheNetworkTick()
        {
            _netGo = new GameObject("UC-T6 server");
            _netGo.SetActive(false);
            var manager = _netGo.AddComponent<NetworkManager>();
            _netGo.AddComponent<UnityTransport>();
            var bootstrap = _netGo.AddComponent<NetBootstrap>();
            if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.EnableSceneManagement = false;
            // Its own port, so it never meets another test or a running game on the default one.
            typeof(NetBootstrap).GetField("port", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, 47961);
            typeof(NetBootstrap).GetField("topology", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, Topology.Server);
            typeof(NetBootstrap).GetField("gameVersion", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bootstrap, "uc-t6");
            _netGo.SetActive(true);

            var session = bootstrap.Session;
            _sim.World = new PhysicsWorld(OneBlock(1.0f), "UC-T6 world");
            PullRight(_sim.World, CursorA, 0f);
            _sim.Bind(session);
            Assert.That(_sim.IsAttached, Is.False, "not before the session runs");

            yield return null;   // a headless editor serves by itself from NetBootstrap.Start
            if (session.State == SessionState.Idle) Assert.That(bootstrap.StartServer(), Is.True, "StartServer");

            float deadline = Time.realtimeSinceStartup + 5f;
            while (session.State != SessionState.Running && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(session.State, Is.EqualTo(SessionState.Running));
            Assert.That(session.IsServer, Is.True);
            Assert.That(_sim.IsAttached, Is.True, "a server session attaches its clock");
            Assert.That(session.Clock.TickRate, Is.EqualTo(SimulationRate.TicksPerSecond));

            long startSteps = _sim.StepCount;
            long startTick = session.Clock.CurrentTick;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 1.5f) yield return null;

            long steps = _sim.StepCount - startSteps;
            Assert.That(steps, Is.EqualTo(session.Clock.CurrentTick - startTick), "one step per network tick");
            Assert.That(steps, Is.InRange(30, 60), "about 30 ticks a second");
            Assert.That(_sim.World.Pose(BlockId).X, Is.GreaterThan(1f), "the server's ticks move the grabbed body");

            Assert.That(session.Leave(), Is.True);
            deadline = Time.realtimeSinceStartup + 5f;
            while (_sim.IsAttached && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(_sim.IsAttached, Is.False, "the session ending detaches the clock");
        }
    }
}
