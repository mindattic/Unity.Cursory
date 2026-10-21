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
    /// UC-T10: votes and whistles over a real Unity Transport on loopback. A Host and two Clients (Ann, Bob)
    /// through <see cref="NetSession"/>, one server-owned <see cref="VoteBoard"/> served with the real level list.
    /// A passing vote resets or loads the level on the server; every peer sees the vote's state and end; a whistle reaches every peer with the whistler's tone and colour.
    /// </summary>
    public class VoteBoardTests : NetSessionTest
    {
        private NetSession _host;
        private NetSession _a;
        private NetSession _b;
        private GameObject _prefab;

        private VoteBoard _onHost;
        private VoteBoard _onA;
        private VoteBoard _onB;

        private LevelLoader _loader;
        private ManualTickClock _clock;

        private readonly Dictionary<VoteBoard, List<(VoteKind Kind, int Level, VoteEnding Ending)>> _ended = new Dictionary<VoteBoard, List<(VoteKind, int, VoteEnding)>>();
        private readonly Dictionary<VoteBoard, List<Whistle>> _whistles = new Dictionary<VoteBoard, List<Whistle>>();

        private static readonly PlayerColor AnnColor = new PlayerColor(0xE2, 0x4B, 0x4A);
        private static readonly PlayerColor BobColor = new PlayerColor(0x1D, 0x9E, 0x75);

        public VoteBoardTests() : base(Topology.Host, clients: 2)
        {
        }

        protected override string GameVersion => "uc-t10";

        protected override void OnSessionsCreated()
        {
            _host = Server;
            _a = Clients[0];
            _b = Clients[1];
            _host.ProtocolHash = _a.ProtocolHash = _b.ProtocolHash = new ProtocolHash(0xC10);
            _host.Identity = new PlayerIdentity("host-token", "Host", PlayerColor.White);
            _a.Identity = new PlayerIdentity("ann-token", "Ann", AnnColor);
            _b.Identity = new PlayerIdentity("bob-token", "Bob", BobColor);
            _prefab = CreatePrefab<VoteBoard>();
        }

        protected override IEnumerator OnSetup()
        {
            _ended.Clear();
            _whistles.Clear();
            yield return null;
        }

        protected override IEnumerator OnSessionsTearDown()
        {
            _a = _b = _host = null;
            yield return null;
        }

        /// <summary>
        /// Host up, Ann and Bob joined, the board spawned and seen by both, served on the host with the 14 levels. With <paramref name="manualClock"/> the timeout runs on a clock the test steps.
        /// </summary>
        private IEnumerator StartBoard(bool manualClock = false)
        {
            yield return StartAndJoinAll();

            _onHost = Spawn<VoteBoard>(_prefab);
            Awaiting<VoteBoard> onA = WaitForCopy(_a, _onHost);
            Awaiting<VoteBoard> onB = WaitForCopy(_b, _onHost);
            yield return onA;
            yield return onB;
            _onA = onA.Result;
            _onB = onB.Result;

            foreach (var board in new[] { _onHost, _onA, _onB })
            {
                var ended = _ended[board] = new List<(VoteKind, int, VoteEnding)>();
                var whistles = _whistles[board] = new List<Whistle>();
                board.VoteEnded += (k, l, e) => ended.Add((k, l, e));
                board.Whistled += w => whistles.Add(w);
            }

            _loader = new LevelLoader(LevelSeeds.All());
            _loader.ConsumeGeometryRebroadcast();
            _clock = manualClock ? new ManualTickClock(SimulationRate.TicksPerSecond) : null;
            _onHost.Serve(_loader, _clock);
        }

        private IEnumerable<VoteBoard> All => new[] { _onHost, _onA, _onB };

        private IEnumerator WaitEverywhere(System.Func<VoteBoard, bool> condition, string what)
        {
            yield return WaitUntil(() => All.All(condition), () => what + " (host " + _onHost.Status + ", Ann " + _onA.Status + ", Bob " + _onB.Status + ")");
        }

        private static IEnumerator Settle()
        {
            for (int i = 0; i < 20; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator A_level_vote_reaches_every_peer_and_passing_loads_the_level_on_the_server()
        {
            yield return StartBoard();

            _onA.StartLevelVote(3);

            yield return WaitEverywhere(b => b.Status != null && b.Status.Yes == 1 && b.ChoiceOf(_a.LocalClientId) == VoteChoice.Yes,
                "every peer sees Ann's level vote with her Yes");
            foreach (var board in All)
            {
                var s = board.Status;
                Assert.That(s.Kind, Is.EqualTo(VoteKind.SelectLevel));
                Assert.That(s.TargetLevel, Is.EqualTo(3));
                Assert.That(s.StartedBy, Is.EqualTo(_a.LocalClientId));
                Assert.That(s.Voters, Is.EqualTo(3));
                Assert.That(s.Quorum, Is.EqualTo(2), "ceil(2/3 x 3)");
                Assert.That(s.SecondsLeft, Is.InRange(14, 15));
            }
            Assert.That(_onA.LocalChoice, Is.EqualTo(VoteChoice.Yes));
            Assert.That(_onB.LocalChoice, Is.EqualTo(VoteChoice.Undecided));
            Assert.That(_onHost.LocalChoice, Is.EqualTo(VoteChoice.Undecided), "the host's own player votes too");
            Assert.That(_loader.CurrentLevel, Is.EqualTo(1), "nothing happens before it passes");

            _onB.CastVote(true);

            yield return WaitEverywhere(b => b.Status == null && _ended[b].Count == 1, "the vote ended everywhere");
            foreach (var board in All)
                Assert.That(_ended[board][0], Is.EqualTo((VoteKind.SelectLevel, 3, VoteEnding.Passed)));
            Assert.That(_loader.CurrentLevel, Is.EqualTo(3), "the passing vote loaded level 3 on the server");
            Assert.That(_loader.World.Number, Is.EqualTo(3));
            Assert.That(_loader.ConsumeGeometryRebroadcast(), Is.True, "and queued the geometry resend");
        }

        [UnityTest]
        public IEnumerator A_reset_vote_is_rejected_early_once_its_quorum_is_unreachable()
        {
            yield return StartBoard();
            _onHost.StartResetVote();
            yield return WaitEverywhere(b => b.Status != null, "every peer sees the host's reset vote");

            _onA.CastVote(false);
            yield return WaitEverywhere(b => b.Status != null && b.Status.No == 1, "Ann's No");
            Assert.That(_onB.ChoiceOf(_a.LocalClientId), Is.EqualTo(VoteChoice.No));

            _onB.CastVote(false);
            yield return WaitEverywhere(b => b.Status == null && _ended[b].Count == 1, "the vote ended everywhere");
            Assert.That(_ended[_onB][0], Is.EqualTo((VoteKind.Reset, 0, VoteEnding.Rejected)));
            Assert.That(_loader.ConsumeGeometryRebroadcast(), Is.False, "a rejected reset does nothing");
        }

        [UnityTest]
        public IEnumerator A_reset_vote_that_passes_reseeds_the_level()
        {
            yield return StartBoard();
            _onB.StartResetVote();
            yield return WaitEverywhere(b => b.Status != null, "the reset vote");
            _onHost.CastVote(true);
            yield return WaitEverywhere(b => _ended[b].Count == 1, "the vote ended everywhere");
            Assert.That(_ended[_onA][0].Ending, Is.EqualTo(VoteEnding.Passed));
            Assert.That(_loader.CurrentLevel, Is.EqualTo(1));
            Assert.That(_loader.ConsumeGeometryRebroadcast(), Is.True, "the level was re-seeded");
        }

        [UnityTest]
        public IEnumerator A_player_leaving_mid_vote_recomputes_the_quorum()
        {
            yield return StartBoard();
            _onA.StartResetVote();
            yield return WaitEverywhere(b => b.Status != null && b.Status.Quorum == 2, "the vote, quorum 2 of 3");

            _b.Leave();
            yield return WaitUntil(() => _onHost.Status != null && _onHost.Status.Voters == 2 && _onA.Status != null && _onA.Status.Voters == 2, () => "Bob's departure never reached the vote (host " + _onHost.Status + ", Ann " + _onA.Status + ")");
            Assert.That(_onA.Status.Quorum, Is.EqualTo(2), "ceil(2/3 x 2)");

            _onHost.CastVote(true);
            yield return WaitUntil(() => _ended[_onHost].Count == 1 && _ended[_onA].Count == 1, "the vote never passed");
            Assert.That(_ended[_onA][0].Ending, Is.EqualTo(VoteEnding.Passed));
        }

        [UnityTest]
        public IEnumerator An_undecided_vote_times_out_after_fifteen_seconds_of_ticks()
        {
            yield return StartBoard(manualClock: true);
            _onA.StartLevelVote(2);
            yield return WaitEverywhere(b => b.Status != null && b.Status.SecondsLeft == 15, "the vote, 15 s left");

            _clock.Step(5 * SimulationRate.TicksPerSecond);
            yield return WaitEverywhere(b => b.Status != null && b.Status.SecondsLeft == 10, "the countdown at 10 s");

            _clock.Step(Vote.TimeoutTicks - 5 * SimulationRate.TicksPerSecond - 1);
            yield return Settle();
            Assert.That(_onB.Status, Is.Not.Null, "one tick short of the timeout");

            _clock.Step();
            yield return WaitEverywhere(b => b.Status == null && _ended[b].Count == 1, "the vote lapsed everywhere");
            Assert.That(_ended[_onB][0], Is.EqualTo((VoteKind.SelectLevel, 2, VoteEnding.TimedOut)));
            Assert.That(_loader.CurrentLevel, Is.EqualTo(1));

            _onB.StartResetVote();
            yield return WaitEverywhere(b => b.Status != null && b.Status.Sequence == 2, "a new vote can start once it lapses");
        }

        [UnityTest]
        public IEnumerator Refused_starts_change_nothing()
        {
            yield return StartBoard();
            _onA.StartLevelVote(1);  // the current level
            _onA.StartLevelVote(99); // no such level
            yield return Settle();
            Assert.That(All.All(b => b.Status == null), Is.True);

            _onA.StartResetVote();
            yield return WaitEverywhere(b => b.Status != null, "Ann's vote");
            _onB.StartLevelVote(4); // one vote at a time
            yield return Settle();
            Assert.That(_onHost.Status.Kind, Is.EqualTo(VoteKind.Reset));
            Assert.That(_onHost.Status.Sequence, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator A_whistle_reaches_every_peer_with_the_whistlers_tone_and_colour()
        {
            yield return StartBoard();

            _onB.Whistle(new Vector2(120f, -40f));
            yield return WaitEverywhere(b => _whistles[b].Count == 1, "Bob's whistle on every peer");
            foreach (var board in All)
            {
                var w = _whistles[board][0];
                Assert.That(w.PlayerId, Is.EqualTo(_b.LocalClientId));
                Assert.That(w.X, Is.EqualTo(120f));
                Assert.That(w.Y, Is.EqualTo(-40f));
                Assert.That(w.Color, Is.EqualTo("#1D9E75"), "Bob's colour");
            }

            _onA.Whistle(new Vector2(1f, 2f));
            yield return WaitEverywhere(b => _whistles[b].Count == 2, "Ann's whistle on every peer");
            var bobTone = _whistles[_onA][0].Tone;
            var annTone = _whistles[_onA][1].Tone;
            Assert.That(annTone, Is.Not.EqualTo(bobTone), "each player has their own tone");
            Assert.That(_whistles[_onHost][1].Tone, Is.EqualTo(annTone), "and every peer hears the same one");
            Assert.That(_whistles[_onB][1].Color, Is.EqualTo("#E24B4A"));
            Assert.That(annTone, Is.InRange(0, WhistleTone.Count - 1));

            _onA.Whistle(new Vector2(float.NaN, 0f));
            yield return Settle();
            Assert.That(_whistles[_onB], Has.Count.EqualTo(2), "a non-finite whistle is dropped by the server");
        }
    }
}
