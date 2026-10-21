using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>What a passing <see cref="Vote"/> does.</summary>
    public enum VoteKind
    {
        /// <summary>Re-seed the current level.</summary>
        Reset = 0,
        /// <summary>Switch to <see cref="Vote.TargetLevel"/>.</summary>
        SelectLevel = 1,
    }

    /// <summary>Where a <see cref="Vote"/> stands.</summary>
    public enum VoteOutcome
    {
        Pending = 0,
        Passed = 1,
        /// <summary>Too many No votes for the quorum to be reached any more.</summary>
        Rejected = 2,
    }

    /// <summary>How a vote ended, as reported by <see cref="VoteBox.Ended"/>.</summary>
    public enum VoteEnding
    {
        /// <summary>Yes reached the quorum; the vote's effect was applied.</summary>
        Passed = 0,
        /// <summary>Too many No votes (or departures) for the quorum to be reached.</summary>
        Rejected = 1,
        /// <summary><see cref="Vote.TimeoutTicks"/> passed without a decision.</summary>
        TimedOut = 2,
        /// <summary>Every voter left.</summary>
        Abandoned = 3,
    }

    /// <summary>Where one player stands on a <see cref="Vote"/>.</summary>
    public enum VoteChoice
    {
        /// <summary>Not on the vote's roster (joined after it started), or no vote is running.</summary>
        NotAVoter = 0,
        Undecided = 1,
        Yes = 2,
        No = 3,
    }

    /// <summary>
    /// A room vote (Cursory's <c>RoomVote</c>). The roster at the start is the voter list; the starter counts
    /// as Yes; it passes at a 2/3 quorum of that roster and times out after <see cref="TimeoutTicks"/>.
    /// Players are identified by their network client id.
    /// </summary>
    public sealed class Vote
    {
        /// <summary>Ticks before an undecided vote lapses: 15 s at 30 Hz.</summary>
        public const int TimeoutTicks = 15 * SimulationRate.TicksPerSecond;

        public VoteKind Kind;
        /// <summary>For <see cref="VoteKind.SelectLevel"/>, the level to load (1-based).</summary>
        public int TargetLevel;
        public long StartedAtTick;
        public ulong StartedBy;
        public List<ulong> Voters = new List<ulong>();
        public List<ulong> Yes = new List<ulong>();
        public List<ulong> No = new List<ulong>();
        /// <summary>Yes votes needed to pass: <see cref="QuorumFor"/> of the voter count.</summary>
        public int Quorum;

        /// <summary>Yes votes needed from <paramref name="voterCount"/> voters: ceil(2/3 × count).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="voterCount"/> is below one.</exception>
        public static int QuorumFor(int voterCount)
        {
            if (voterCount < 1) throw new ArgumentOutOfRangeException(nameof(voterCount), voterCount, "a vote needs at least one voter");
            return (voterCount * 2 + 2) / 3;
        }

        /// <summary>Voters who have voted neither Yes nor No.</summary>
        public int Undecided => Voters.Count - Yes.Count - No.Count;

        /// <summary>Passed once Yes reaches the quorum; rejected once it no longer can; otherwise pending.</summary>
        public VoteOutcome Outcome
        {
            get
            {
                if (Yes.Count >= Quorum) return VoteOutcome.Passed;
                if (Yes.Count + Undecided < Quorum) return VoteOutcome.Rejected;
                return VoteOutcome.Pending;
            }
        }

        /// <summary>True once <see cref="TimeoutTicks"/> have passed since the vote started.</summary>
        public bool IsExpired(long nowTick) => nowTick - StartedAtTick >= TimeoutTicks;

        /// <summary>Ticks until the vote lapses at <paramref name="nowTick"/>; 0 once it has.</summary>
        public long TicksLeft(long nowTick) => Math.Max(0, StartedAtTick + TimeoutTicks - nowTick);

        /// <summary>Whole seconds until the vote lapses, rounded up (15 at the start, 0 once lapsed): a HUD countdown.</summary>
        public int SecondsLeft(long nowTick) => (int)((TicksLeft(nowTick) + SimulationRate.TicksPerSecond - 1) / SimulationRate.TicksPerSecond);

        /// <summary>Where <paramref name="player"/> stands on this vote.</summary>
        public VoteChoice ChoiceOf(ulong player)
        {
            if (Yes.Contains(player)) return VoteChoice.Yes;
            if (No.Contains(player)) return VoteChoice.No;
            return Voters.Contains(player) ? VoteChoice.Undecided : VoteChoice.NotAVoter;
        }
    }

    /// <summary>
    /// A whistle: a player clicked empty space at (<see cref="X"/>, <see cref="Y"/>). Clients draw a ripple
    /// in <see cref="Color"/> and play the player's tone. The only communication in the game.
    /// </summary>
    public sealed class Whistle
    {
        /// <summary>Ticks a whistle stays live on the server (about a second at 30 Hz).</summary>
        public const int BroadcastTicks = 30;

        public ulong PlayerId;
        public string Color = "#7F77DD";
        /// <summary>The player's tone, 0 to <see cref="WhistleTone.Count"/> - 1 (<see cref="WhistleTone.For"/>).</summary>
        public int Tone;
        public float X;
        public float Y;
        public long Tick;

        /// <summary>True while the whistle is younger than <see cref="BroadcastTicks"/>.</summary>
        public bool IsLive(long nowTick) => nowTick - Tick < BroadcastTicks;
    }
}
