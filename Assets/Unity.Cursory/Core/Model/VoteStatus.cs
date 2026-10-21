using System;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// What every peer knows about the vote in progress: the counts and countdown a HUD shows, without the
    /// voter lists (those stay on the server; each peer learns its own ballot separately). Immutable.
    /// </summary>
    public sealed class VoteStatus : IEquatable<VoteStatus>
    {
        public VoteStatus(int sequence, VoteKind kind, int targetLevel, ulong startedBy, int yes, int no, int voters, int quorum, int secondsLeft)
        {
            Sequence = sequence;
            Kind = kind;
            TargetLevel = targetLevel;
            StartedBy = startedBy;
            Yes = yes;
            No = no;
            Voters = voters;
            Quorum = quorum;
            SecondsLeft = secondsLeft;
        }

        /// <summary>Which vote this is: 1 for the room's first, counting up, so a peer can tell a new vote from an update.</summary>
        public int Sequence { get; }
        public VoteKind Kind { get; }
        /// <summary>For <see cref="VoteKind.SelectLevel"/>, the level it would load; 0 for a reset.</summary>
        public int TargetLevel { get; }
        /// <summary>Client id of the player who started it.</summary>
        public ulong StartedBy { get; }
        public int Yes { get; }
        public int No { get; }
        /// <summary>How many players may vote (the roster when it started, less anyone who has left since).</summary>
        public int Voters { get; }
        /// <summary>Yes votes needed to pass: ceil(2/3 × <see cref="Voters"/>).</summary>
        public int Quorum { get; }
        /// <summary>Whole seconds until it lapses, rounded up.</summary>
        public int SecondsLeft { get; }
        /// <summary>Voters who have voted neither way.</summary>
        public int Undecided => Voters - Yes - No;

        /// <summary>The status of <paramref name="vote"/> at <paramref name="nowTick"/>.</summary>
        public static VoteStatus Of(Vote vote, int sequence, long nowTick)
        {
            if (vote == null) throw new ArgumentNullException(nameof(vote));
            return new VoteStatus(sequence, vote.Kind, vote.TargetLevel, vote.StartedBy, vote.Yes.Count, vote.No.Count,
                vote.Voters.Count, vote.Quorum, vote.SecondsLeft(nowTick));
        }

        public bool Equals(VoteStatus other) =>
            other != null && Sequence == other.Sequence && Kind == other.Kind && TargetLevel == other.TargetLevel &&
            StartedBy == other.StartedBy && Yes == other.Yes && No == other.No && Voters == other.Voters &&
            Quorum == other.Quorum && SecondsLeft == other.SecondsLeft;

        public override bool Equals(object obj) => Equals(obj as VoteStatus);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = Sequence;
                h = h * 31 + (int)Kind;
                h = h * 31 + TargetLevel;
                h = h * 31 + StartedBy.GetHashCode();
                h = h * 31 + Yes;
                h = h * 31 + No;
                h = h * 31 + Voters;
                h = h * 31 + Quorum;
                return h * 31 + SecondsLeft;
            }
        }

        public override string ToString() =>
            $"Vote #{Sequence} {Kind}{(Kind == VoteKind.SelectLevel ? " L" + TargetLevel : "")}: {Yes} yes, {No} no of {Voters} (needs {Quorum}), {SecondsLeft}s left";
    }
}
