using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// Runs the room's one vote at a time and applies it to a <see cref="LevelLoader"/> (Cursory's
    /// <c>StartResetVote</c>, <c>StartLevelVote</c>, <c>CastVote</c> and the vote timeout). The roster when a
    /// vote starts is its voter list, the starter counts as Yes, and it passes at a 2/3 quorum: a reset
    /// re-seeds the level, a level vote loads the target. It is dropped as soon as it can no longer pass,
    /// or after <see cref="Vote.TimeoutTicks"/>. A voter who leaves is dropped from it and the quorum is
    /// recomputed from who is left (<see cref="RemoveVoter"/>). Players are identified by their network client id.
    /// <see cref="Changed"/> and <see cref="Ended"/> report every change, for the network layer and the HUD.
    /// </summary>
    public sealed class VoteBox
    {
        private readonly LevelLoader loader;
        private Vote active;

        /// <summary>Raised after every change to the vote in progress (start, ballot, departure, end) with <see cref="Current"/>: null once it ends.</summary>
        public event Action<Vote> Changed;

        /// <summary>Raised when a vote ends, after a passing vote's effect has been applied to the loader.</summary>
        public event Action<Vote, VoteEnding> Ended;

        public VoteBox(LevelLoader loader)
        {
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        /// <summary>A copy of the vote in progress, or null when there is none.</summary>
        public Vote Current => active == null ? null : Copy(active);

        /// <summary>True while a vote is running.</summary>
        public bool IsActive => active != null;

        /// <summary>The running vote's number (1 for the first vote, counting up); the last vote's once it has ended.</summary>
        public int Sequence { get; private set; }

        /// <summary>The running vote's status at <paramref name="nowTick"/>, or null when there is none.</summary>
        public VoteStatus Status(long nowTick) => active == null ? null : VoteStatus.Of(active, Sequence, nowTick);

        /// <summary>Where <paramref name="player"/> stands on the running vote; <see cref="VoteChoice.NotAVoter"/> when none runs.</summary>
        public VoteChoice ChoiceOf(ulong player) => active == null ? VoteChoice.NotAVoter : active.ChoiceOf(player);

        /// <summary>Starts a reset vote. False when a vote is already running or the starter is not in <paramref name="roster"/>.</summary>
        public bool StartReset(ulong starter, IReadOnlyCollection<ulong> roster, long nowTick) =>
            Start(starter, roster, nowTick, VoteKind.Reset, 0);

        /// <summary>
        /// Starts a vote to switch to <paramref name="targetLevel"/>. False for a level that does not exist or
        /// is already loaded (a no-op vote must not block a real reset), or when <see cref="StartReset"/> would be.
        /// </summary>
        public bool StartLevel(ulong starter, int targetLevel, IReadOnlyCollection<ulong> roster, long nowTick)
        {
            if (!loader.Exists(targetLevel) || targetLevel == loader.CurrentLevel) return false;
            return Start(starter, roster, nowTick, VoteKind.SelectLevel, targetLevel);
        }

        /// <summary>
        /// Records <paramref name="voter"/>'s Yes or No. Voting again replaces the earlier choice; a player who
        /// joined after the vote started is ignored.
        /// </summary>
        public void Cast(ulong voter, bool yes)
        {
            if (active == null || !active.Voters.Contains(voter)) return;
            active.Yes.Remove(voter);
            active.No.Remove(voter);
            (yes ? active.Yes : active.No).Add(voter);
            Changed?.Invoke(Current);
            Resolve();
        }

        /// <summary>
        /// Drops <paramref name="player"/> (who left the room) from the running vote and recomputes the quorum from
        /// the voters who are left, which can pass or reject it at once. A vote whose last voter leaves is abandoned.
        /// </summary>
        public void RemoveVoter(ulong player)
        {
            if (active == null || !active.Voters.Remove(player)) return;
            active.Yes.Remove(player);
            active.No.Remove(player);
            if (active.Voters.Count == 0)
            {
                End(VoteEnding.Abandoned);
                return;
            }
            active.Quorum = Vote.QuorumFor(active.Voters.Count);
            Changed?.Invoke(Current);
            Resolve();
        }

        /// <summary>Drops the vote once <see cref="Vote.TimeoutTicks"/> have passed since it started. Call every tick.</summary>
        public void Tick(long nowTick)
        {
            if (active != null && active.IsExpired(nowTick)) End(VoteEnding.TimedOut);
        }

        private bool Start(ulong starter, IReadOnlyCollection<ulong> roster, long nowTick, VoteKind kind, int targetLevel)
        {
            if (active != null || roster == null || roster.Count == 0) return false;
            var voters = new List<ulong>();
            foreach (var id in roster)
                if (!voters.Contains(id)) voters.Add(id);
            if (!voters.Contains(starter)) return false;

            active = new Vote
            {
                Kind = kind,
                TargetLevel = targetLevel,
                StartedAtTick = nowTick,
                StartedBy = starter,
                Voters = voters,
                Yes = new List<ulong> { starter },
                No = new List<ulong>(),
                Quorum = Vote.QuorumFor(voters.Count),
            };
            Sequence++;
            Changed?.Invoke(Current);
            Resolve();
            return true;
        }

        private void Resolve()
        {
            switch (active.Outcome)
            {
                case VoteOutcome.Passed:
                    if (active.Kind == VoteKind.Reset) loader.Reset();
                    else loader.Load(active.TargetLevel);
                    End(VoteEnding.Passed);
                    break;
                case VoteOutcome.Rejected:
                    End(VoteEnding.Rejected);
                    break;
            }
        }

        private void End(VoteEnding ending)
        {
            var ended = active;
            active = null;
            Changed?.Invoke(null);
            Ended?.Invoke(ended, ending);
        }

        private static Vote Copy(Vote v) => new Vote
        {
            Kind = v.Kind,
            TargetLevel = v.TargetLevel,
            StartedAtTick = v.StartedAtTick,
            StartedBy = v.StartedBy,
            Voters = new List<ulong>(v.Voters),
            Yes = new List<ulong>(v.Yes),
            No = new List<ulong>(v.No),
            Quorum = v.Quorum,
        };
    }
}
