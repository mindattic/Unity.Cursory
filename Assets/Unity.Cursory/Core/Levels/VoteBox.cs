using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// Runs the room's one vote at a time and applies it to a <see cref="LevelLoader"/> (Cursory's
    /// <c>StartResetVote</c>, <c>StartLevelVote</c>, <c>CastVote</c> and the vote timeout). The roster when a
    /// vote starts is its voter list, the starter counts as Yes, and it passes at a 2/3 quorum: a reset
    /// re-seeds the level, a level vote loads the target. It is dropped as soon as it can no longer pass,
    /// or after <see cref="Vote.TimeoutTicks"/>. Players are identified by their network client id.
    /// </summary>
    public sealed class VoteBox
    {
        private readonly LevelLoader loader;
        private Vote active;

        public VoteBox(LevelLoader loader)
        {
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        /// <summary>A copy of the vote in progress, or null when there is none.</summary>
        public Vote Current => active == null ? null : Copy(active);

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
            Resolve();
        }

        /// <summary>Drops the vote once <see cref="Vote.TimeoutTicks"/> have passed since it started. Call every tick.</summary>
        public void Tick(long nowTick)
        {
            if (active != null && active.IsExpired(nowTick)) active = null;
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
            Resolve();
            return true;
        }

        private void Resolve()
        {
            switch (active.Outcome)
            {
                case VoteOutcome.Passed:
                    var passed = active;
                    active = null;
                    if (passed.Kind == VoteKind.Reset) loader.Reset();
                    else loader.Load(passed.TargetLevel);
                    break;
                case VoteOutcome.Rejected:
                    active = null;
                    break;
            }
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
