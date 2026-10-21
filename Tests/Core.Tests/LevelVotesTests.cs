using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    /// <summary>
    /// UM-T54: Cursory's two vote kinds (reset, switch level) as the room's VoteBoard registers them. The vote rules
    /// themselves (quorum, timeout, departures) are Unity.Multiplayer's and tested there (VoteBoxTests).
    /// </summary>
    public class LevelVotesTests
    {
        private LevelLoader loader;

        [SetUp]
        public void SetUp() => loader = new LevelLoader(LevelSeeds.All());

        [Test]
        public void The_questions_read_as_Cursory_wrote_them()
        {
            Assert.That(LevelVotes.ResetTitle, Is.EqualTo("Reset the level?"));
            Assert.That(LevelVotes.LevelTitle(7), Is.EqualTo("Switch to Level 7?"));
            Assert.That(LevelVotes.ResetKind, Is.Not.EqualTo(LevelVotes.LevelKind));
        }

        [Test]
        public void A_level_vote_for_the_current_level_is_refused()
        {
            Assert.That(LevelVotes.CheckLevel(loader, 1), Is.EqualTo("Level 1 is already being played."));
            Assert.That(LevelVotes.CheckLevel(loader, 2), Is.Null);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(15)]
        [TestCase(99)]
        [TestCase(long.MaxValue)]
        public void Out_of_range_level_votes_are_refused(long level)
        {
            Assert.That(LevelVotes.CheckLevel(loader, level), Is.EqualTo("There is no level " + level + "."));
        }

        [Test]
        public void A_passing_level_vote_loads_the_level_and_queues_the_resend_and_banner()
        {
            LevelVotes.ApplyLevel(loader, 2);
            Assert.That(loader.CurrentLevel, Is.EqualTo(2));
            Assert.That(loader.World.Number, Is.EqualTo(2));
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.True);
            Assert.That(loader.ConsumeLevelAnnouncement(), Is.EqualTo(2));
        }

        [Test]
        public void A_passing_reset_reseeds_the_level()
        {
            loader.ConsumeGeometryRebroadcast();
            LevelVotes.ApplyReset(loader);
            Assert.That(loader.CurrentLevel, Is.EqualTo(1));
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.True);
        }
    }
}
