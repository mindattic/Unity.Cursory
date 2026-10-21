using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T10 / UM-T54: the room's whistles on Cursory's <see cref="WhistleBoard"/>, and its reset and level votes as
    /// kinds on Unity.Multiplayer's <see cref="VoteBoard"/> (<see cref="LevelServer.AddVoteKinds"/>), over a real Unity
    /// Transport on loopback: a Host and two Clients (Ann, Bob). The vote rules themselves are the library's, tested there.
    /// </summary>
    public class WhistleBoardTests : NetSessionTest
    {
        private NetSession _host;
        private NetSession _a;
        private NetSession _b;
        private GameObject _whistlePrefab;

        private WhistleBoard _whistlesOnHost;
        private VoteBoard _votesOnHost;
        private readonly List<WhistleBoard> _whistleBoards = new List<WhistleBoard>();
        private readonly List<VoteBoard> _voteBoards = new List<VoteBoard>();
        private readonly Dictionary<WhistleBoard, List<Whistle>> _whistles = new Dictionary<WhistleBoard, List<Whistle>>();
        private readonly Dictionary<VoteBoard, List<VoteResult>> _ended = new Dictionary<VoteBoard, List<VoteResult>>();
        private readonly List<string> _refusedA = new List<string>();
        private LevelLoader _loader;

        private static readonly PlayerColor AnnColor = new PlayerColor(0xE2, 0x4B, 0x4A);
        private static readonly PlayerColor BobColor = new PlayerColor(0x1D, 0x9E, 0x75);

        public WhistleBoardTests() : base(Topology.Host, clients: 2)
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
            _whistlePrefab = CreatePrefab<WhistleBoard>();
            foreach (NetSession session in Sessions) VoteBoard.RegisterPrefab(session);
        }

        protected override IEnumerator OnSessionsTearDown()
        {
            _whistleBoards.Clear();
            _voteBoards.Clear();
            _whistles.Clear();
            _ended.Clear();
            _refusedA.Clear();
            _a = _b = _host = null;
            yield return null;
        }

        /// <summary>Host up, Ann and Bob joined, both boards spawned and seen by both, the votes served on the host with the 14 levels.</summary>
        private IEnumerator StartBoards()
        {
            yield return StartAndJoinAll();

            _loader = new LevelLoader(LevelSeeds.All());
            _loader.ConsumeGeometryRebroadcast();
            _whistlesOnHost = Spawn<WhistleBoard>(_whistlePrefab);
            _votesOnHost = LevelServer.AddVoteKinds(VoteBoard.Spawn(_host), _loader);

            _whistleBoards.Add(_whistlesOnHost);
            _voteBoards.Add(_votesOnHost);
            foreach (NetSession client in new[] { _a, _b })
            {
                Awaiting<WhistleBoard> whistles = WaitForCopy(client, _whistlesOnHost);
                Awaiting<VoteBoard> votes = WaitForCopy(client, _votesOnHost);
                yield return whistles;
                yield return votes;
                _whistleBoards.Add(whistles.Result);
                _voteBoards.Add(votes.Result);
            }

            foreach (WhistleBoard board in _whistleBoards)
            {
                var list = _whistles[board] = new List<Whistle>();
                board.Whistled += w => list.Add(w);
            }

            foreach (VoteBoard board in _voteBoards)
            {
                var list = _ended[board] = new List<VoteResult>();
                board.VoteEnded += r => list.Add(r);
            }

            _voteBoards[1].VoteRefused += (why, reason) => _refusedA.Add(reason);
        }

        private static IEnumerator Settle()
        {
            for (int i = 0; i < 20; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator A_level_vote_reads_as_Cursory_and_passing_loads_the_level_on_the_server()
        {
            yield return StartBoards();
            VoteBoard onA = _voteBoards[1];
            VoteBoard onB = _voteBoards[2];

            onA.StartVote(LevelVotes.LevelKind, 3);
            yield return WaitUntil(() => _voteBoards.All(b => b.Status != null), "every peer sees Ann's level vote");
            foreach (VoteBoard board in _voteBoards)
            {
                Assert.That(board.Status.Title, Is.EqualTo("Switch to Level 3?"));
                Assert.That(board.Status.Number, Is.EqualTo(3));
                Assert.That(board.Status.Quorum, Is.EqualTo(2), "ceil(2/3 x 3)");
            }

            Assert.That(_loader.CurrentLevel, Is.EqualTo(1), "nothing happens before it passes");
            onB.CastVote(true);
            yield return WaitUntil(() => _voteBoards.All(b => _ended[b].Count == 1), "the vote ended everywhere");
            Assert.That(_ended[onB][0].Ending, Is.EqualTo(VoteEnding.Passed));
            Assert.That(_loader.CurrentLevel, Is.EqualTo(3), "the passing vote loaded level 3 on the server");
            Assert.That(_loader.ConsumeGeometryRebroadcast(), Is.True, "and queued the geometry resend");
        }

        [UnityTest]
        public IEnumerator A_reset_vote_that_passes_reseeds_the_level()
        {
            yield return StartBoards();
            _voteBoards[2].StartVote(LevelVotes.ResetKind);
            yield return WaitUntil(() => _voteBoards.All(b => b.Status != null), "the reset vote");
            Assert.That(_voteBoards[1].Status.Title, Is.EqualTo("Reset the level?"));
            _votesOnHost.CastVote(true);
            yield return WaitUntil(() => _voteBoards.All(b => _ended[b].Count == 1), "the vote ended everywhere");
            Assert.That(_loader.CurrentLevel, Is.EqualTo(1));
            Assert.That(_loader.ConsumeGeometryRebroadcast(), Is.True, "the level was re-seeded");
        }

        [UnityTest]
        public IEnumerator A_level_vote_for_the_current_or_a_missing_level_is_refused_with_why()
        {
            yield return StartBoards();
            _voteBoards[1].StartVote(LevelVotes.LevelKind, 1);
            _voteBoards[1].StartVote(LevelVotes.LevelKind, 99);
            yield return WaitUntil(() => _refusedA.Count == 2, () => "Ann heard " + string.Join(", ", _refusedA));
            Assert.That(_refusedA, Is.EqualTo(new[] { "Level 1 is already being played.", "There is no level 99." }));
            yield return Settle();
            Assert.That(_voteBoards.All(b => b.Status == null), Is.True);
        }

        [UnityTest]
        public IEnumerator A_whistle_reaches_every_peer_with_the_whistlers_tone_and_colour()
        {
            yield return StartBoards();
            WhistleBoard onA = _whistleBoards[1];
            WhistleBoard onB = _whistleBoards[2];

            onB.Whistle(new Vector2(120f, -40f));
            yield return WaitUntil(() => _whistleBoards.All(b => _whistles[b].Count == 1), "Bob's whistle on every peer");
            foreach (WhistleBoard board in _whistleBoards)
            {
                var w = _whistles[board][0];
                Assert.That(w.PlayerId, Is.EqualTo(_b.LocalClientId));
                Assert.That(w.X, Is.EqualTo(120f));
                Assert.That(w.Y, Is.EqualTo(-40f));
                Assert.That(w.Color, Is.EqualTo("#1D9E75"), "Bob's colour");
            }

            onA.Whistle(new Vector2(1f, 2f));
            yield return WaitUntil(() => _whistleBoards.All(b => _whistles[b].Count == 2), "Ann's whistle on every peer");
            var bobTone = _whistles[onA][0].Tone;
            var annTone = _whistles[onA][1].Tone;
            Assert.That(annTone, Is.Not.EqualTo(bobTone), "each player has their own tone");
            Assert.That(_whistles[_whistlesOnHost][1].Tone, Is.EqualTo(annTone), "and every peer hears the same one");
            Assert.That(_whistles[onB][1].Color, Is.EqualTo("#E24B4A"));
            Assert.That(annTone, Is.InRange(0, WhistleTone.Count - 1));

            onA.Whistle(new Vector2(float.NaN, 0f));
            yield return Settle();
            Assert.That(_whistles[onB], Has.Count.EqualTo(2), "a non-finite whistle is dropped by the server");
        }
    }
}
