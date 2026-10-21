using System;
using System.Collections.Generic;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// The room's votes and whistles, networked (Cursory's <c>StartResetVote</c>, <c>StartLevelVote</c>,
    /// <c>CastVote</c> and <c>Whistle</c> hub calls). One per scene, spawned and owned by the server, separate from
    /// the player cursors. Any player asks with <see cref="StartResetVote"/>, <see cref="StartLevelVote"/>,
    /// <see cref="CastVote"/> or <see cref="Whistle"/>; the server decides (BIBLE UC-LAW-1) through a Core
    /// <see cref="VoteBox"/>, applies a passing vote to the level loader, and every peer sees the result.
    /// <see cref="LevelServer"/> spawns and serves one in the real game.
    /// </summary>
    /// <remarks>
    /// <para><b>Server:</b> call <see cref="Serve"/> once the board has spawned. The roster is the session's
    /// connected clients when a vote starts; a client who disconnects is dropped from the vote and the quorum is
    /// recomputed. The 15 s timeout runs on the session's tick clock (or the one given to <see cref="Serve"/>).</para>
    /// <para><b>Every peer (the HUD binds here):</b> <see cref="Status"/>, <see cref="LocalChoice"/> and
    /// <see cref="ChoiceOf"/>, raised through <see cref="VoteChanged"/>; <see cref="VoteEnded"/> once per vote;
    /// <see cref="Whistled"/> per whistle, carrying the player's tone (<see cref="WhistleTone"/>) and colour.
    /// The C# events fire on the main thread, at most once a frame for <see cref="VoteChanged"/>.</para>
    /// </remarks>
    public partial class VoteBoard : NetEntity
    {
        /// <summary>Most vote starts a player may send a second.</summary>
        public const int StartRateLimit = 2;

        /// <summary>Most ballots a player may send a second (changing your mind is allowed, spamming isn't).</summary>
        public const int CastRateLimit = 4;

        /// <summary>Most whistles a player may send a second.</summary>
        public const int WhistleRateLimit = 8;

        /// <summary>Largest whistle coordinate the server accepts, in world pixels; beyond it a whistle is clamped.</summary>
        public const float WhistleRangePx = 100000f;

        // --- client -> server -----------------------------------------------------------------------------------

        /// <summary>Asks the server to start a vote to reset the current level. Ignored while a vote runs.</summary>
        [ToServer(RequireOwnership = false, RateLimit = StartRateLimit)]
        public partial void StartResetVote();

        /// <summary>Asks the server to start a vote to switch to <paramref name="level"/> (1-based). Ignored for the current level or one that doesn't exist.</summary>
        [ToServer(RequireOwnership = false, RateLimit = StartRateLimit)]
        public partial void StartLevelVote(int level);

        /// <summary>Votes Yes or No on the running vote; voting again replaces the earlier choice.</summary>
        [ToServer(RequireOwnership = false, RateLimit = CastRateLimit)]
        public partial void CastVote(bool yes);

        /// <summary>Whistles at <paramref name="worldPx"/> (world pixels, BIBLE UC-LAW-3): every peer sees a ripple and hears this player's tone.</summary>
        [ToServer(RequireOwnership = false, RateLimit = WhistleRateLimit)]
        public partial void Whistle(Vector2 worldPx);

        // --- server -> clients ----------------------------------------------------------------------------------

        [ToClients]
        private partial void Ballot(int sequence, ulong voter, int choice);

        [ToClients]
        private partial void Finished(int sequence, int kind, int targetLevel, int ending);

        [ToClients]
        private partial void BroadcastWhistle(ulong player, Vector2 worldPx, int tone, Color32 color);

        // --- synced vote state (written by the server only) ------------------------------------------------------

#pragma warning disable CS0169, CS0414 // declarations the generator reads; the values live in the generated network variables
        [Synced(OnChanged = nameof(MarkBoolDirty))] private bool _syncOpen;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncSequence;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncKind;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncTarget;
        [Synced(OnChanged = nameof(MarkUlongDirty))] private ulong _syncStarter;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncYes;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncNo;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncVoters;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncQuorum;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncSecondsLeft;
#pragma warning restore CS0169, CS0414

        // --- server state ---------------------------------------------------------------------------------------

        private VoteBox _box;
        private LevelLoader _loader;
        private ITickClock _clock;
        private NetSession _servedSession;
        private Func<IReadOnlyCollection<ulong>> _roster;
        /// <summary>The ballots last broadcast for vote <see cref="_sentSequence"/>, so only changes go out.</summary>
        private readonly Dictionary<ulong, VoteChoice> _sent = new Dictionary<ulong, VoteChoice>();
        private int _sentSequence;

        // --- every peer's view ----------------------------------------------------------------------------------

        private readonly Dictionary<ulong, VoteChoice> _ballots = new Dictionary<ulong, VoteChoice>();
        private int _ballotSequence;
        private bool _dirty;
        private VoteStatus _status;

        /// <summary>Raised on every peer when <see cref="Status"/> or a ballot changes, with the new status (null once no vote runs).</summary>
        public event Action<VoteStatus> VoteChanged;

        /// <summary>Raised on every peer once per vote when it ends: its kind, target level (0 for a reset) and how it ended.</summary>
        public event Action<VoteKind, int, VoteEnding> VoteEnded;

        /// <summary>Raised on every peer for each whistle, with who, where (world pixels), their tone and colour, and this peer's clock tick.</summary>
        public event Action<Core.Whistle> Whistled;

        /// <summary>The vote in progress as every peer sees it, or null when none runs.</summary>
        public VoteStatus Status => _status;

        /// <summary>True while a vote runs.</summary>
        public bool IsVoteOpen => _status != null;

        /// <summary>Where this peer's own player stands on the running vote (<see cref="VoteChoice.NotAVoter"/> when they joined after it started, or none runs).</summary>
        public VoteChoice LocalChoice => ChoiceOf(Session != null ? Session.LocalClientId : NetSender.ServerClientId);

        /// <summary>Where player <paramref name="clientId"/> stands on the running vote.</summary>
        public VoteChoice ChoiceOf(ulong clientId)
        {
            if (_status == null || _ballotSequence != _status.Sequence) return VoteChoice.NotAVoter;
            return _ballots.TryGetValue(clientId, out var choice) ? choice : VoteChoice.NotAVoter;
        }

        /// <summary>Server only: the vote rules, once <see cref="Serve"/> has run; null elsewhere.</summary>
        public VoteBox Box => _box;

        /// <summary>
        /// Server only: runs this board's votes against <paramref name="loader"/>. A passing vote resets or loads
        /// the level there. The timeout runs on <paramref name="clock"/>, by default the session's.
        /// The roster is <paramref name="roster"/>, by default the session's connected clients.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="loader"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Not on the server, or no tick clock is available.</exception>
        public void Serve(LevelLoader loader, ITickClock clock = null, Func<IReadOnlyCollection<ulong>> roster = null)
        {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            if (!IsSpawned || !IsServer) throw new InvalidOperationException("VoteBoard.Serve runs on the server, after the board has spawned.");
            StopServing();

            _loader = loader;
            _servedSession = Session;
            _clock = clock ?? _servedSession?.Clock;
            if (_clock == null) throw new InvalidOperationException("VoteBoard.Serve needs a tick clock: the session has none yet, and none was given.");
            _roster = roster ?? (() => _servedSession != null ? (IReadOnlyCollection<ulong>)_servedSession.ConnectedClientIds : Array.Empty<ulong>());

            _box = new VoteBox(loader);
            _box.Changed += OnBoxChanged;
            _box.Ended += OnBoxEnded;
            _clock.Ticked += OnServerTick;
            if (_servedSession != null) _servedSession.ClientDisconnected += OnClientLeft;
        }

        /// <summary>Server only: stops running votes (an open vote is dropped without effect).</summary>
        public void StopServing()
        {
            if (_box == null) return;
            _box.Changed -= OnBoxChanged;
            _box.Ended -= OnBoxEnded;
            _clock.Ticked -= OnServerTick;
            if (_servedSession != null) _servedSession.ClientDisconnected -= OnClientLeft;
            _box = null;
            _loader = null;
            _clock = null;
            _servedSession = null;
            _roster = null;
            if (IsSpawned && IsServer) WriteStatus(null);
        }

        protected override void OnDespawned()
        {
            StopServing();
        }

        private void OnDestroy()
        {
            StopServing();
        }

        private void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            _status = SyncOpen
                ? new VoteStatus(SyncSequence, (VoteKind)SyncKind, SyncTarget, SyncStarter, SyncYes, SyncNo, SyncVoters, SyncQuorum, SyncSecondsLeft)
                : null;
            VoteChanged?.Invoke(_status);
        }

        // --- server handlers ------------------------------------------------------------------------------------

        private void OnStartResetVote(NetSender sender)
        {
            if (!CheckServed(nameof(StartResetVote))) return;
            _box.StartReset(sender.ClientId, _roster(), _clock.CurrentTick);
        }

        private void OnStartLevelVote(int level, NetSender sender)
        {
            if (!CheckServed(nameof(StartLevelVote))) return;
            _box.StartLevel(sender.ClientId, level, _roster(), _clock.CurrentTick);
        }

        private void OnCastVote(bool yes, NetSender sender)
        {
            if (!CheckServed(nameof(CastVote))) return;
            _box.Cast(sender.ClientId, yes);
        }

        private void OnWhistle(Vector2 worldPx, NetSender sender)
        {
            if (float.IsNaN(worldPx.x) || float.IsNaN(worldPx.y) || float.IsInfinity(worldPx.x) || float.IsInfinity(worldPx.y)) return;
            worldPx.x = Mathf.Clamp(worldPx.x, -WhistleRangePx, WhistleRangePx);
            worldPx.y = Mathf.Clamp(worldPx.y, -WhistleRangePx, WhistleRangePx);

            var tone = WhistleTone.For(sender.HasSeat ? (ulong)sender.SeatIndex : sender.ClientId);
            var color = new Color32(255, 255, 255, 255);
            if (Session != null && Session.TryGetSeat(sender.ClientId, out var seat))
            {
                color = new Color32(seat.Color.R, seat.Color.G, seat.Color.B, 255);
            }
            BroadcastWhistle(sender.ClientId, worldPx, tone, color);
        }

        private bool CheckServed(string rpc)
        {
            if (_box != null) return true;
            Debug.LogWarning("[Unity.Cursory] VoteBoard." + rpc + " arrived before VoteBoard.Serve; ignored.", this);
            return false;
        }

        private void OnServerTick(long tick)
        {
            _box.Tick(tick);
            if (_box.IsActive) SyncSecondsLeft = _box.Status(tick).SecondsLeft;
        }

        private void OnClientLeft(ulong clientId)
        {
            _box?.RemoveVoter(clientId);
        }

        private void OnBoxChanged(Vote vote)
        {
            if (vote != null)
            {
                if (_sentSequence != _box.Sequence)
                {
                    _sent.Clear();
                    _sentSequence = _box.Sequence;
                }
                // Departed voters drop off the roster on every peer.
                var gone = new List<ulong>();
                foreach (var id in _sent.Keys)
                    if (!vote.Voters.Contains(id)) gone.Add(id);
                foreach (var id in gone)
                {
                    _sent.Remove(id);
                    Ballot(_sentSequence, id, (int)VoteChoice.NotAVoter);
                }
                foreach (var voter in vote.Voters)
                {
                    var choice = vote.ChoiceOf(voter);
                    if (_sent.TryGetValue(voter, out var known) && known == choice) continue;
                    _sent[voter] = choice;
                    Ballot(_sentSequence, voter, (int)choice);
                }
            }
            WriteStatus(vote == null ? null : VoteStatus.Of(vote, _box.Sequence, _clock.CurrentTick));
        }

        private void OnBoxEnded(Vote vote, VoteEnding ending)
        {
            Finished(_box.Sequence, (int)vote.Kind, vote.TargetLevel, (int)ending);
        }

        private void WriteStatus(VoteStatus status)
        {
            if (status != null)
            {
                SyncSequence = status.Sequence;
                SyncKind = (int)status.Kind;
                SyncTarget = status.TargetLevel;
                SyncStarter = status.StartedBy;
                SyncYes = status.Yes;
                SyncNo = status.No;
                SyncVoters = status.Voters;
                SyncQuorum = status.Quorum;
                SyncSecondsLeft = status.SecondsLeft;
            }
            SyncOpen = status != null;
        }

        // --- every peer -----------------------------------------------------------------------------------------

        private void OnBallot(int sequence, ulong voter, int choice)
        {
            if (sequence != _ballotSequence)
            {
                if (sequence < _ballotSequence) return; // a late ballot from a vote already replaced
                _ballots.Clear();
                _ballotSequence = sequence;
            }
            if ((VoteChoice)choice == VoteChoice.NotAVoter) _ballots.Remove(voter);
            else _ballots[voter] = (VoteChoice)choice;
            _dirty = true;
        }

        private void OnFinished(int sequence, int kind, int targetLevel, int ending)
        {
            VoteEnded?.Invoke((VoteKind)kind, targetLevel, (VoteEnding)ending);
        }

        private void OnBroadcastWhistle(ulong player, Vector2 worldPx, int tone, Color32 color)
        {
            var clock = Session?.Clock;
            Whistled?.Invoke(new Core.Whistle
            {
                PlayerId = player,
                X = worldPx.x,
                Y = worldPx.y,
                Tone = tone,
                Color = "#" + color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2"),
                Tick = clock != null ? clock.CurrentTick : 0,
            });
        }

        private void MarkBoolDirty(bool previous, bool current) => _dirty = true;

        private void MarkIntDirty(int previous, int current) => _dirty = true;

        private void MarkUlongDirty(ulong previous, ulong current) => _dirty = true;
    }
}
