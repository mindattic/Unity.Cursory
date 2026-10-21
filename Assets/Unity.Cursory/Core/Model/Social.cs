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
        public float X;
        public float Y;
        public long Tick;

        /// <summary>True while the whistle is younger than <see cref="BroadcastTicks"/>.</summary>
        public bool IsLive(long nowTick) => nowTick - Tick < BroadcastTicks;
    }
}
