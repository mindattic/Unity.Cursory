using System.Collections;
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
    /// UC-T33 end to end, on the library's <see cref="NetSessionTest"/>: a dedicated server plays the hall puzzle (a long,
    /// heavy bar across the mouth of a narrow vertical hall, <see cref="HallPlay"/>) and bot clients drive their cursors
    /// through <see cref="CursorAvatar"/> intents only, each steering by its own replicated copy of the bar. One bot pulling
    /// an end flat out cannot turn the bar into the hall in <see cref="HallPlay.Seconds"/>; two bots on opposite ends,
    /// pulling against each other, turn it and pull it in.
    /// </summary>
    public class HallBotTests : NetSessionTest
    {
        private const float Ox = 5000f;
        private const float Oy = 5000f;

        /// <summary>How often a bot sends its pointer, per second (<see cref="CursorAvatar.Move"/> takes up to 30).</summary>
        private const float MovesPerSecond = 20f;

        private NgoBodySync _bodySync;
        private ServerSimulation _sim;

        public HallBotTests() : base(Topology.Server, clients: 2)
        {
        }

        protected override string GameVersion => "uc-t33";

        protected override void OnSessionsCreated()
        {
            _bodySync = NetPrefabs.RegisterAll(Server);
            foreach (var client in Clients) NetPrefabs.RegisterAll(client);
            foreach (NetworkManager manager in m_NetworkManagers) manager.NetworkConfig.PlayerPrefab = NetPrefabs.LoadCursorAvatar();
        }

        protected override IEnumerator OnSessionsTearDown()
        {
            _sim?.Dispose();
            _sim = null;
            yield break;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator OneBot_CannotTurnTheBarIntoTheHall()
        {
            CursorAvatar right = null;
            Transform bar = null;
            yield return Serve(a => right = a, b => bar = b, 0);
            yield return Grab(right, BarEndClick(+1f));

            bool inTheHall = false;
            yield return Play(right, bar, null, null, done => inTheHall = done);

            var (x, y, angle) = _sim.World.Pose(HallPlay.BarId);
            Assert.That(inTheHall, Is.False, "one bot got the bar into the hall");
            Assert.That(Mathf.Abs(angle), Is.LessThan(0.05f), "one bot cannot even start turning it (it is at " + angle * Mathf.Rad2Deg + "°)");
            Assert.That(Vector2.Distance(new Vector2(x, y), new Vector2(Ox, Oy)), Is.LessThan(2f), "nor drag it");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator TwoBotsOnOppositeEnds_TurnTheBarAndPullItIntoTheHall()
        {
            CursorAvatar right = null, left = null;
            Transform rightsBar = null, leftsBar = null;
            yield return Serve(a => right = a, b => rightsBar = b, 0);
            yield return Serve(a => left = a, b => leftsBar = b, 1);
            yield return Grab(right, BarEndClick(+1f));
            yield return Grab(left, BarEndClick(-1f));

            bool inTheHall = false;
            yield return Play(right, rightsBar, left, leftsBar, done => inTheHall = done);

            var (x, y, angle) = _sim.World.Pose(HallPlay.BarId);
            Assert.That(inTheHall, Is.True, "two bots never got the bar in: it is at (" + x + ", " + y + "), " + angle * Mathf.Rad2Deg + "°");
        }

        private static Vector2 BarEndClick(float side) => new Vector2(Ox + side * (HallPlay.BarLength * 0.5f + 10f), Oy);

        /// <summary>Starts the server with the hall (once), then waits for client <paramref name="index"/>'s cursor and its copy of the bar.</summary>
        private IEnumerator Serve(System.Action<CursorAvatar> cursor, System.Action<Transform> bar, int index)
        {
            if (_sim == null)
            {
                yield return StartAndJoinAll();
                _sim = new ServerSimulation { Loader = new LevelLoader(new[] { HallPlay.Level(Ox, Oy) }) };
                _sim.World = new PhysicsWorld(_sim.Loader.World, "UC-T33 hall", _bodySync);
                _sim.Bind(Server);
            }

            NetSession client = Clients[index];
            yield return WaitUntil(() => CursorAvatar.Local(client) != null, "bot " + index + "'s cursor never spawned");
            cursor(CursorAvatar.Local(client));
            Transform copy = null;
            yield return WaitUntil(() => client.Objects.TryGet(HallPlay.BarId, out copy) && copy != null, "bot " + index + " never saw the bar");
            bar(copy);
        }

        /// <summary>A bot points at an end of the bar and grabs it there; waits until the server holds it.</summary>
        private IEnumerator Grab(CursorAvatar me, Vector2 click)
        {
            me.Move(click);
            me.Grab(HallPlay.BarId, click.x, click.y);
            CursorAvatar onServer = CursorAvatar.ForClient(Server, me.OwnerClientId);
            yield return WaitUntil(() => onServer != null && onServer.Held == GrabKind.Block, "the server never saw bot " + me.OwnerClientId + "'s grab");
        }

        /// <summary>
        /// Each bot steers by its own replicated copy of the bar (<see cref="HallPlay.Pointers"/>) for up to
        /// <see cref="HallPlay.Seconds"/>; reports whether the server's bar got into the hall.
        /// </summary>
        private IEnumerator Play(CursorAvatar right, Transform rightsBar, CursorAvatar left, Transform leftsBar, System.Action<bool> report)
        {
            float end = Time.realtimeSinceStartup + HallPlay.Seconds;
            float nextMove = 0f;
            var rightBot = new Bot(right, rightsBar, left != null, true);
            var leftBot = left != null ? new Bot(left, leftsBar, true, false) : null;
            while (Time.realtimeSinceStartup < end)
            {
                var (sx, sy, sa) = _sim.World.Pose(HallPlay.BarId);
                if (HallPlay.IsInTheHall(sx, sy, sa, Ox, Oy))
                {
                    report(true);
                    yield break;
                }

                if (Time.realtimeSinceStartup >= nextMove)
                {
                    nextMove = Time.realtimeSinceStartup + 1f / MovesPerSecond;
                    rightBot.Steer();
                    leftBot?.Steer();
                }

                yield return null;
            }

            var (x, y, angle) = _sim.World.Pose(HallPlay.BarId);
            report(HallPlay.IsInTheHall(x, y, angle, Ox, Oy));
        }

        /// <summary>One bot: its cursor, its own copy of the bar, its end, and how fast it last saw the bar turning.</summary>
        private sealed class Bot
        {
            private readonly CursorAvatar _me;
            private readonly Transform _bar;
            private readonly bool _twoPlayers;
            private readonly bool _rightEnd;
            private float _lastAngle;
            private float _lastTime = -1f;

            public Bot(CursorAvatar me, Transform bar, bool twoPlayers, bool rightEnd)
            {
                _me = me;
                _bar = bar;
                _twoPlayers = twoPlayers;
                _rightEnd = rightEnd;
            }

            public void Steer()
            {
                float x = WorldUnits.ToPixels(_bar.position.x);
                float y = WorldUnits.ToPixels(_bar.position.y);
                float angle = Mathf.DeltaAngle(0f, _bar.eulerAngles.z) * Mathf.Deg2Rad;
                float now = Time.realtimeSinceStartup;
                float spin = _lastTime >= 0f && now > _lastTime ? (angle - _lastAngle) / (now - _lastTime) : 0f;
                _lastAngle = angle;
                _lastTime = now;
                var plan = HallPlay.Pointers(x, y, angle, spin, _me.Position, _me.Position, _twoPlayers, Ox);
                _me.Move(_rightEnd ? plan.Right : plan.Left);
            }
        }
    }
}
