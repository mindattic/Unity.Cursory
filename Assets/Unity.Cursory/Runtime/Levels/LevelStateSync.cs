using System;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// The level's win state, replicated from the server to every client (BIBLE UC-LAW-1, UC-LAW-4's "goal and
    /// circuit state"): which goals are solved, whether the bulb is lit, whether the level is won, and where every
    /// wire end lies and what it is plugged into. One server-owned instance per session; the server writes it from
    /// a <see cref="LevelProgress"/> through <see cref="Publish"/> (driven each tick by <see cref="LevelStateServer"/>)
    /// and clients only read it.
    /// </summary>
    /// <remarks>
    /// <para>Goals are numbered as <see cref="LevelProgress"/> numbers them (block goals, then shape goals), goal i
    /// in bit i of <see cref="SolvedMask"/>. A client knows the ids from its own copy of level
    /// <see cref="LevelNumber"/> (<see cref="LevelLibrary"/>).</para>
    /// <para>Wires are not static geometry, so they ride their own reliable call: <see cref="WiresMoved"/> to every
    /// client whenever an end moves or plugs in, and <see cref="WiresFor"/> to a late joiner, which asks with
    /// <see cref="RequestWires"/> as it spawns. Each wire is one <see cref="Vector4"/> (A.x, A.y, B.x, B.y in world
    /// pixels, UC-LAW-3) and two plug slots (end A, end B), each the index of its terminal in the level's
    /// <see cref="LevelData.Terminals"/> or -1 when loose.</para>
    /// </remarks>
    public partial class LevelStateSync : NetEntity
    {
        /// <summary>A plug slot's value for a loose wire end.</summary>
        public const int Loose = -1;

#pragma warning disable CS0169, CS0414 // declarations the generator reads; the values live in the generated network variables
        [Synced(OnChanged = nameof(LevelNumberChanged))] private int _levelNumber;
        [Synced] private int _goalCount;
        [Synced(OnChanged = nameof(SolvedMaskChanged))] private int _solvedMask;
        [Synced] private bool _hasCircuit;
        [Synced(OnChanged = nameof(BulbLitChanged))] private bool _bulbLit;
        [Synced(OnChanged = nameof(LevelSolvedChanged))] private bool _levelSolved;
#pragma warning restore CS0169, CS0414

        private Vector4[] _wireEnds = new Vector4[0];
        private int[] _wirePlugs = new int[0];

        /// <summary>Raised on every peer when a goal, the bulb, the win or the level changes.</summary>
        public event Action StateChanged;

        /// <summary>Raised on every peer the moment the level becomes won (false to true).</summary>
        public event Action Won;

        /// <summary>Raised on a client (and the host) when wire state arrives.</summary>
        public event Action WiresChanged;

        /// <summary>Every wire's ends (A.x, A.y, B.x, B.y), in the level's wire order, as last received (or published, on the server).</summary>
        public Vector4[] WireEnds => _wireEnds;

        /// <summary>Every wire's plug slots, two per wire (A then B): a terminal index or <see cref="Loose"/>.</summary>
        public int[] WirePlugs => _wirePlugs;

        /// <summary>Whether goal <paramref name="index"/> is solved, from <see cref="SolvedMask"/>.</summary>
        public bool IsGoalSolved(int index) => index >= 0 && index < 31 && (SolvedMask & (1 << index)) != 0;

        // --- server -> clients ---------------------------------------------------------------------------------

        /// <summary>Every client: the wires changed.</summary>
        [ToClients]
        public partial void WiresMoved(Vector4[] ends, int[] plugs);

        /// <summary>One client (a late joiner): the wires as they are now.</summary>
        [ToClient]
        public partial void WiresFor(ulong target, Vector4[] ends, int[] plugs);

        // --- client -> server ----------------------------------------------------------------------------------

        /// <summary>A client that has just spawned this entity asks for the current wires.</summary>
        [ToServer(RequireOwnership = false, RateLimit = 2)]
        public partial void RequestWires();

        /// <summary>
        /// Server only: writes <paramref name="progress"/>'s last evaluation into the synced state and, when any wire
        /// end moved or (un)plugged since the last call, sends the wires to every client. Call it after
        /// <see cref="LevelProgress.Evaluate"/>, once per tick.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="progress"/> is null.</exception>
        public void Publish(LevelProgress progress)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            var level = progress.World;
            if (LevelNumber != level.Number) LevelNumber = level.Number;
            if (GoalCount != progress.GoalCount) GoalCount = progress.GoalCount;
            if (HasCircuit != progress.HasCircuit) HasCircuit = progress.HasCircuit;
            var mask = progress.SolvedMask;
            if (SolvedMask != mask) SolvedMask = mask;
            if (BulbLit != progress.BulbLit) BulbLit = progress.BulbLit;
            var won = progress.IsLevelSolved;
            if (LevelSolved != won) LevelSolved = won;

            Encode(level, out var ends, out var plugs);
            if (Same(ends, _wireEnds) && Same(plugs, _wirePlugs)) return;
            _wireEnds = ends;
            _wirePlugs = plugs;
            if (IsSpawned) WiresMoved(ends, plugs);
        }

        /// <summary>The wire state of <paramref name="level"/> in this entity's wire format.</summary>
        public static void Encode(LevelData level, out Vector4[] ends, out int[] plugs)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            ends = new Vector4[level.Wires.Count];
            plugs = new int[level.Wires.Count * 2];
            for (var i = 0; i < level.Wires.Count; i++)
            {
                var w = level.Wires[i];
                ends[i] = new Vector4(w.Ax, w.Ay, w.Bx, w.By);
                plugs[2 * i] = TerminalIndex(level, w.ATerminalId);
                plugs[2 * i + 1] = TerminalIndex(level, w.BTerminalId);
            }
        }

        /// <summary>The id of the terminal plug slot <paramref name="plug"/> names in <paramref name="level"/>, or empty when loose.</summary>
        public static string TerminalId(LevelData level, int plug)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            return plug >= 0 && plug < level.Terminals.Count ? level.Terminals[plug].Id : "";
        }

        protected override void OnSpawned()
        {
            if (!IsServer) RequestWires();
        }

        private void OnRequestWires(NetSender sender)
        {
            WiresFor(sender, _wireEnds, _wirePlugs);
        }

        private void OnWiresMoved(Vector4[] ends, int[] plugs)
        {
            Receive(ends, plugs);
        }

        private void OnWiresFor(Vector4[] ends, int[] plugs)
        {
            Receive(ends, plugs);
        }

        private void Receive(Vector4[] ends, int[] plugs)
        {
            if (IsServer) return; // the server's copy is already the truth
            _wireEnds = ends ?? new Vector4[0];
            _wirePlugs = plugs ?? new int[0];
            WiresChanged?.Invoke();
        }

        private void LevelNumberChanged(int previous, int current) => StateChanged?.Invoke();

        private void SolvedMaskChanged(int previous, int current) => StateChanged?.Invoke();

        private void BulbLitChanged(bool previous, bool current) => StateChanged?.Invoke();

        private void LevelSolvedChanged(bool previous, bool current)
        {
            StateChanged?.Invoke();
            if (!previous && current) Won?.Invoke();
        }

        private static int TerminalIndex(LevelData level, string terminalId)
        {
            if (string.IsNullOrEmpty(terminalId)) return Loose;
            for (var i = 0; i < level.Terminals.Count; i++)
                if (level.Terminals[i].Id == terminalId) return i;
            return Loose;
        }

        private static bool Same<T>(T[] a, T[] b) where T : IEquatable<T>
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }
    }
}
