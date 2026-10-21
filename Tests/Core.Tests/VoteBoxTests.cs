using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    /// <summary>
    /// UC-T10: what the networked vote board needs from <see cref="VoteBox"/> beyond Cursory's own rules
    /// (those are <see cref="VoteAndLevelTests"/>): departures recompute the quorum, every change is reported,
    /// and the status a HUD shows.
    /// </summary>
    public class VoteBoxTests
    {
        private const ulong U1 = 1, U2 = 2, U3 = 3, U4 = 4;

        private LevelLoader loader;
        private VoteBox votes;
        private readonly List<Vote> changes = new List<Vote>();
        private readonly List<(Vote Vote, VoteEnding Ending)> endings = new List<(Vote, VoteEnding)>();

        [SetUp]
        public void SetUp()
        {
            loader = new LevelLoader(LevelSeeds.All());
            votes = new VoteBox(loader);
            changes.Clear();
            endings.Clear();
            votes.Changed += v => changes.Add(v);
            votes.Ended += (v, e) => endings.Add((v, e));
        }

        [Test]
        public void A_departure_recomputes_the_quorum_and_can_pass_the_vote()
        {
            votes.StartLevel(U1, 4, new[] { U1, U2, U3, U4 }, 0); // quorum ceil(8/3) = 3
            votes.Cast(U2, true);
            Assert.That(votes.Current.Quorum, Is.EqualTo(3));

            votes.RemoveVoter(U4); // 3 voters: quorum 2, already met
            Assert.That(votes.Current, Is.Null);
            Assert.That(loader.CurrentLevel, Is.EqualTo(4), "the passing vote loaded its level");
            Assert.That(endings.Single().Ending, Is.EqualTo(VoteEnding.Passed));
        }

        [Test]
        public void A_departing_yes_voter_takes_their_vote_with_them()
        {
            votes.StartReset(U1, new[] { U1, U2, U3 }, 0); // quorum 2
            votes.Cast(U2, true);
            Assert.That(votes.Current, Is.Null, "2 of 3 passed");

            votes.StartReset(U1, new[] { U1, U2, U3 }, 0);
            votes.RemoveVoter(U1); // the starter leaves: 0 yes of 2, quorum 2
            var v = votes.Current;
            Assert.That(v, Is.Not.Null);
            Assert.That(v.Voters, Is.EquivalentTo(new[] { U2, U3 }));
            Assert.That(v.Yes, Is.Empty);
            Assert.That(v.Quorum, Is.EqualTo(2));
        }

        [Test]
        public void A_departure_can_make_the_quorum_unreachable()
        {
            votes.StartReset(U1, new[] { U1, U2, U3 }, 0); // quorum 2
            votes.Cast(U2, false);
            votes.RemoveVoter(U1); // U2 No, U3 undecided: quorum 2 of 2 can't be reached
            Assert.That(votes.Current, Is.Null);
            Assert.That(endings.Single().Ending, Is.EqualTo(VoteEnding.Rejected));
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.False);
        }

        [Test]
        public void A_vote_whose_last_voter_leaves_is_abandoned()
        {
            votes.StartReset(U1, new[] { U1, U2, U3 }, 0);
            votes.RemoveVoter(U1); // 0 yes, 2 undecided, quorum 2: still pending
            votes.RemoveVoter(U2); // 0 yes, 1 undecided, quorum 1: still pending
            Assert.That(votes.Current, Is.Not.Null);
            Assert.That(votes.Current.Quorum, Is.EqualTo(1));
            votes.RemoveVoter(U3);
            Assert.That(votes.Current, Is.Null);
            Assert.That(endings.Single().Ending, Is.EqualTo(VoteEnding.Abandoned));
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.False);
        }

        [Test]
        public void A_starter_who_changes_their_mind_and_a_departure_reject_it()
        {
            votes.StartReset(U1, new[] { U1, U2, U3 }, 0);
            votes.Cast(U1, false); // 0 yes, 1 no, 2 undecided: quorum 2 still reachable
            Assert.That(votes.Current, Is.Not.Null);
            votes.RemoveVoter(U2); // quorum 2 of 2, 0 yes + 1 undecided: rejected
            Assert.That(endings.Single().Ending, Is.EqualTo(VoteEnding.Rejected));
        }

        [Test]
        public void Removing_someone_off_the_roster_changes_nothing()
        {
            votes.StartReset(U1, new[] { U1, U2 }, 0);
            changes.Clear();
            votes.RemoveVoter(U4);
            votes.RemoveVoter(U4);
            Assert.That(changes, Is.Empty);
            Assert.That(votes.Current.Voters, Has.Count.EqualTo(2));
        }

        [Test]
        public void Every_change_is_reported_and_the_end_says_how()
        {
            votes.StartReset(U1, new[] { U1, U2, U3 }, 10);
            votes.Cast(U2, false);
            votes.Tick(10 + Vote.TimeoutTicks);

            Assert.That(changes.Select(c => c == null ? -1 : c.Yes.Count + 10 * c.No.Count), Is.EqualTo(new[] { 1, 11, -1 }),
                "start (1 yes), ballot (1 yes 1 no), then null at the end");
            Assert.That(endings.Single().Ending, Is.EqualTo(VoteEnding.TimedOut));
            Assert.That(endings.Single().Vote.No, Is.EqualTo(new[] { U2 }), "the ended vote as it stood");
        }

        [Test]
        public void A_passing_vote_has_applied_its_effect_before_it_is_reported()
        {
            int levelWhenEnded = 0;
            votes.Ended += (v, e) => levelWhenEnded = loader.CurrentLevel;
            votes.StartLevel(U1, 7, new[] { U1 }, 0);
            Assert.That(levelWhenEnded, Is.EqualTo(7));
        }

        [Test]
        public void Each_vote_gets_the_next_sequence_number()
        {
            Assert.That(votes.Sequence, Is.EqualTo(0));
            votes.StartReset(U1, new[] { U1 }, 0);
            Assert.That(votes.Sequence, Is.EqualTo(1));
            votes.StartReset(U1, new[] { U1, U2 }, 0);
            Assert.That(votes.Sequence, Is.EqualTo(2));
            Assert.That(votes.StartReset(U2, new[] { U1, U2 }, 0), Is.False);
            Assert.That(votes.Sequence, Is.EqualTo(2), "a refused start is not a vote");
        }

        [Test]
        public void Status_and_choices_are_what_a_hud_shows()
        {
            Assert.That(votes.Status(0), Is.Null);
            Assert.That(votes.ChoiceOf(U1), Is.EqualTo(VoteChoice.NotAVoter));

            votes.StartLevel(U2, 9, new[] { U1, U2, U3 }, 100);
            votes.Cast(U3, false);
            var status = votes.Status(100 + SimulationRate.TicksPerSecond + 1);
            Assert.That(status, Is.EqualTo(new VoteStatus(1, VoteKind.SelectLevel, 9, U2, 1, 1, 3, 2, 14)));
            Assert.That(status.Undecided, Is.EqualTo(1));
            Assert.That(votes.ChoiceOf(U1), Is.EqualTo(VoteChoice.Undecided));
            Assert.That(votes.ChoiceOf(U2), Is.EqualTo(VoteChoice.Yes));
            Assert.That(votes.ChoiceOf(U3), Is.EqualTo(VoteChoice.No));
            Assert.That(votes.ChoiceOf(U4), Is.EqualTo(VoteChoice.NotAVoter));
        }

        [TestCase(0, 15)]
        [TestCase(1, 15)]
        [TestCase(30, 14)]
        [TestCase(449, 1)]
        [TestCase(450, 0)]
        [TestCase(9999, 0)]
        public void The_countdown_rounds_up_to_whole_seconds(int elapsed, int seconds)
        {
            var vote = new Vote { StartedAtTick = 1000 };
            Assert.That(vote.SecondsLeft(1000 + elapsed), Is.EqualTo(seconds));
            Assert.That(vote.TicksLeft(1000 + elapsed), Is.EqualTo(System.Math.Max(0, Vote.TimeoutTicks - elapsed)));
        }
    }

    public class WhistleToneTests
    {
        [Test]
        public void Twelve_tones_440_hz_up_in_35_hz_steps_as_Cursory()
        {
            Assert.That(WhistleTone.Count, Is.EqualTo(12));
            Assert.That(WhistleTone.Hz(0), Is.EqualTo(440f));
            Assert.That(WhistleTone.Hz(11), Is.EqualTo(440f + 11 * 35f));
            Assert.That(WhistleTone.Hz(12), Is.EqualTo(440f), "wraps");
            Assert.That(WhistleTone.Hz(-1), Is.EqualTo(WhistleTone.Hz(11)));
        }

        [Test]
        public void Different_seats_get_different_tones_until_there_are_more_than_twelve()
        {
            var tones = Enumerable.Range(0, 12).Select(i => WhistleTone.For((ulong)i)).ToList();
            Assert.That(tones, Is.Unique);
            Assert.That(WhistleTone.For(12), Is.EqualTo(WhistleTone.For(0)));
            Assert.That(WhistleTone.For(ulong.MaxValue), Is.InRange(0, 11));
        }

        [Test]
        public void The_sweep_and_envelope_follow_Cursory()
        {
            Assert.That(WhistleTone.PitchScale(0), Is.EqualTo(1).Within(1e-9));
            Assert.That(WhistleTone.PitchScale(0.08), Is.EqualTo(1.5).Within(1e-9));
            Assert.That(WhistleTone.PitchScale(0.35), Is.EqualTo(0.9).Within(1e-9));
            Assert.That(WhistleTone.Envelope(0.02), Is.EqualTo(1).Within(1e-9));
            Assert.That(WhistleTone.Envelope(0.41), Is.EqualTo(0));
        }

        [Test]
        public void Render_makes_a_bounded_clip_of_the_whistle_length()
        {
            var samples = WhistleTone.Render(3, 44100);
            Assert.That(samples.Length, Is.EqualTo((int)System.Math.Ceiling(0.45 * 44100)));
            Assert.That(samples.Max(), Is.InRange(0.5f, 1f));
            Assert.That(samples.Min(), Is.InRange(-1f, -0.5f));
            Assert.That(samples.Skip((int)(0.41 * 44100)).All(s => s == 0f), "silent after the decay");
            Assert.That(() => WhistleTone.Render(0, 10), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_higher_tone_crosses_zero_more_often()
        {
            static int Crossings(float[] s) => Enumerable.Range(1, s.Length - 1).Count(i => (s[i - 1] < 0) != (s[i] < 0));
            Assert.That(Crossings(WhistleTone.Render(11, 44100)), Is.GreaterThan(Crossings(WhistleTone.Render(0, 44100))));
        }
    }
}
