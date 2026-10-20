using NUnit.Framework;

namespace UnityCursory.Core.Tests
{
    /// <summary>Ported from Cursory.Tests/VoteAndLevelTests.cs, over <see cref="VoteBox"/> and <see cref="LevelLoader"/>.</summary>
    public class VoteAndLevelTests
    {
        private const ulong U1 = 1, U2 = 2, U3 = 3, Latecomer = 9;

        private LevelLoader loader;
        private VoteBox votes;

        [SetUp]
        public void SetUp()
        {
            loader = new LevelLoader(LevelSeeds.All());
            votes = new VoteBox(loader);
        }

        /// <summary>One player: quorum ceil(2/3 x 1) = 1, so the starter's own Yes passes it at once.</summary>
        [Test]
        public void Solo_room_reset_vote_passes_immediately()
        {
            Assert.That(votes.StartReset(U1, new[] { U1 }, nowTick: 0), Is.True);
            Assert.That(votes.Current, Is.Null, "a one-voter quorum resolves in the same call");
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.True, "the reset re-seeded the level");
        }

        /// <summary>Two voters: quorum ceil(2/3 x 2) = 2, so the starter alone is not enough.</summary>
        [Test]
        public void Two_voter_reset_needs_both_to_pass()
        {
            Assert.That(votes.StartReset(U1, new[] { U1, U2 }, 0), Is.True);
            var v = votes.Current;
            Assert.That(v, Is.Not.Null);
            Assert.That(v.Quorum, Is.EqualTo(2));
            Assert.That(v.Yes, Has.Member(U1));
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.False, "nothing happens until it passes");

            votes.Cast(U2, true);
            Assert.That(votes.Current, Is.Null);
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.True);
        }

        /// <summary>A second voter saying No in a two-player room makes the quorum unreachable, so the vote ends early.</summary>
        [Test]
        public void Vote_rejected_when_quorum_unreachable()
        {
            votes.StartReset(U1, new[] { U1, U2 }, 0);
            votes.Cast(U2, false);
            Assert.That(votes.Current, Is.Null, "1 Yes + 1 No of 2 voters: quorum 2 can no longer be reached");
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.False, "a rejected reset does nothing");
        }

        /// <summary>A passing level-switch vote moves the current level and queues the geometry resend and the banner.</summary>
        [Test]
        public void Level_switch_vote_moves_current_level()
        {
            Assert.That(loader.CurrentLevel, Is.EqualTo(1));
            Assert.That(votes.StartLevel(U1, 2, new[] { U1 }, 0), Is.True);
            Assert.That(loader.CurrentLevel, Is.EqualTo(2));
            Assert.That(loader.World.Number, Is.EqualTo(2));
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.True);
            Assert.That(loader.ConsumeLevelAnnouncement(), Is.EqualTo(2));
        }

        /// <summary>A vote for the level already loaded is refused, so a no-op vote can't stall a real reset.</summary>
        [Test]
        public void Level_vote_for_current_level_is_rejected()
        {
            Assert.That(votes.StartLevel(U1, 1, new[] { U1 }, 0), Is.False);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(15)]
        [TestCase(99)]
        public void Out_of_range_level_vote_is_rejected(int target)
        {
            Assert.That(votes.StartLevel(U1, target, new[] { U1 }, 0), Is.False);
            Assert.That(votes.Current, Is.Null);
        }

        // Beyond Cursory's file: the rest of RoomState's vote rules.

        [Test]
        public void Only_one_vote_runs_at_a_time_and_only_a_roster_member_starts_one()
        {
            Assert.That(votes.StartReset(Latecomer, new[] { U1, U2 }, 0), Is.False);
            Assert.That(votes.StartReset(U1, new[] { U1, U2 }, 0), Is.True);
            Assert.That(votes.StartLevel(U2, 3, new[] { U1, U2 }, 0), Is.False);
        }

        [Test]
        public void Latecomers_are_ignored_and_a_revote_replaces_the_earlier_choice()
        {
            votes.StartLevel(U1, 5, new[] { U1, U2, U3 }, 0); // quorum 2
            votes.Cast(Latecomer, true);
            Assert.That(votes.Current.Yes, Has.Count.EqualTo(1));
            votes.Cast(U2, false);
            Assert.That(votes.Current, Is.Not.Null, "1 Yes + 1 No + 1 undecided of 3 can still reach 2");
            votes.Cast(U2, true);
            Assert.That(votes.Current, Is.Null);
            Assert.That(loader.CurrentLevel, Is.EqualTo(5));
        }

        [Test]
        public void An_undecided_vote_lapses_after_fifteen_seconds()
        {
            votes.StartReset(U1, new[] { U1, U2 }, nowTick: 100);
            votes.Tick(100 + Vote.TimeoutTicks - 1);
            Assert.That(votes.Current, Is.Not.Null);
            votes.Tick(100 + Vote.TimeoutTicks);
            Assert.That(votes.Current, Is.Null);
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.False);
            Assert.That(votes.StartReset(U2, new[] { U1, U2 }, 600), Is.True, "a new vote can start once it lapses");
        }

        [Test]
        public void Current_is_a_copy()
        {
            votes.StartReset(U1, new[] { U1, U2 }, 0);
            votes.Current.Yes.Add(U2);
            Assert.That(votes.Current.Yes, Has.Count.EqualTo(1));
        }
    }
}
