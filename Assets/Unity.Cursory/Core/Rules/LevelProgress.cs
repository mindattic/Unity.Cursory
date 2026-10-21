using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// Where body <paramref name="bodyId"/> is now: its origin in world pixels and its angle in radians. False
    /// when there is no such body. The server answers it from its Physics2D world (<c>PhysicsWorld.Pose</c>).
    /// </summary>
    public delegate bool PoseLookup(string bodyId, out float x, out float y, out float angle);

    /// <summary>
    /// The server's win detection for one played level (Cursory's per-tick goal pass and circuit), engine-free:
    /// each block goal is solved while its block's centre is strictly inside, each shape goal while every piece
    /// centre of its shape is, and the bulb is lit while the wires close the series loop. It also owns the wires'
    /// live state: a cursor picks up a wire end (unplugging it), the end follows that cursor, and releasing it
    /// snaps it onto a terminal within <see cref="Tuning.WireSnapRadius"/>.
    /// </summary>
    /// <remarks>
    /// Goals are numbered in one list, the level's <see cref="LevelData.BlockGoals"/> first and then its
    /// <see cref="LevelData.ShapeGoals"/>; <see cref="SolvedMask"/> carries goal i in bit i, which is what the
    /// network syncs. Build a new one from every fresh <see cref="LevelLoader.World"/> (load or reset).
    /// </remarks>
    public sealed class LevelProgress
    {
        /// <summary>The most goals a level may have, so that <see cref="SolvedMask"/> fits one int.</summary>
        public const int MaxGoals = 31;

        private readonly LevelData world;
        private readonly bool[] solved;
        private readonly Dictionary<ulong, (Wire Wire, WireEnd End)> held = new Dictionary<ulong, (Wire, WireEnd)>();
        private readonly Dictionary<string, Wire> wiresById = new Dictionary<string, Wire>(StringComparer.Ordinal);

        /// <summary>Tracks <paramref name="world"/>, the live copy of the level being played; its wires are changed in place.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
        /// <exception cref="ArgumentException">The level has more than <see cref="MaxGoals"/> goals.</exception>
        public LevelProgress(LevelData world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            GoalCount = world.BlockGoals.Count + world.ShapeGoals.Count;
            if (GoalCount > MaxGoals) throw new ArgumentException($"A level may have at most {MaxGoals} goals, not {GoalCount}.", nameof(world));
            solved = new bool[GoalCount];
            foreach (var w in world.Wires) wiresById[w.Id] = w;
            HasCircuit = world.Components.Count > 0;
        }

        /// <summary>The level being tracked.</summary>
        public LevelData World => world;

        /// <summary>How many goals the level has (block goals, then shape goals).</summary>
        public int GoalCount { get; }

        /// <summary>True when the level has a circuit, so a lit bulb is part of winning it.</summary>
        public bool HasCircuit { get; }

        /// <summary>Whether the bulb was lit at the last <see cref="Evaluate"/>.</summary>
        public bool BulbLit { get; private set; }

        /// <summary>Goal i's solved state at the last <see cref="Evaluate"/> in bit i.</summary>
        public int SolvedMask
        {
            get
            {
                var mask = 0;
                for (var i = 0; i < solved.Length; i++) if (solved[i]) mask |= 1 << i;
                return mask;
            }
        }

        /// <summary>
        /// True when the level is won: every goal solved and, if it has a circuit, the bulb lit. A level with
        /// neither is never won.
        /// </summary>
        public bool IsLevelSolved => IsWon(GoalCount, SolvedMask, HasCircuit, BulbLit);

        /// <summary>The win rule over synced state, so a client can decide it from the same bits the server sends.</summary>
        public static bool IsWon(int goalCount, int solvedMask, bool hasCircuit, bool bulbLit)
        {
            if (goalCount == 0 && !hasCircuit) return false;
            var all = goalCount >= 32 ? -1 : (1 << goalCount) - 1;
            return (solvedMask & all) == all && (!hasCircuit || bulbLit);
        }

        /// <summary>The goal id at <paramref name="index"/> in the goal list.</summary>
        public string GoalId(int index)
        {
            if (index < 0 || index >= GoalCount) throw new ArgumentOutOfRangeException(nameof(index), index, $"goals run from 0 to {GoalCount - 1}");
            var blockGoals = world.BlockGoals.Count;
            return index < blockGoals ? world.BlockGoals[index].Id : world.ShapeGoals[index - blockGoals].Id;
        }

        /// <summary>Whether goal <paramref name="index"/> was solved at the last <see cref="Evaluate"/>.</summary>
        public bool IsGoalSolved(int index)
        {
            if (index < 0 || index >= GoalCount) throw new ArgumentOutOfRangeException(nameof(index), index, $"goals run from 0 to {GoalCount - 1}");
            return solved[index];
        }

        /// <summary>Whether the goal with id <paramref name="goalId"/> was solved at the last <see cref="Evaluate"/>; false for an unknown id.</summary>
        public bool IsGoalSolved(string goalId)
        {
            for (var i = 0; i < GoalCount; i++)
                if (GoalId(i) == goalId) return solved[i];
            return false;
        }

        /// <summary>
        /// Re-decides every goal from the bodies' poses and the bulb from the wires (one server tick's goal pass).
        /// A goal whose target body <paramref name="poses"/> doesn't know is unsolved. Returns true when any goal,
        /// the bulb or the win changed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="poses"/> is null.</exception>
        public bool Evaluate(PoseLookup poses)
        {
            if (poses == null) throw new ArgumentNullException(nameof(poses));
            var beforeMask = SolvedMask;
            var beforeLit = BulbLit;

            var i = 0;
            foreach (var g in world.BlockGoals)
                solved[i++] = poses(g.TargetBlockId, out var x, out var y, out _) && g.IsSatisfiedBy(x, y);

            foreach (var g in world.ShapeGoals)
            {
                var shape = FindShape(g.TargetShapeId);
                solved[i++] = shape != null && poses(g.TargetShapeId, out var x, out var y, out var angle)
                    && g.IsSatisfiedBy(shape, x, y, angle);
            }

            BulbLit = HasCircuit && CircuitRules.IsBulbLit(world.Components, world.Terminals, world.Wires);
            return beforeMask != SolvedMask || beforeLit != BulbLit;
        }

        /// <summary>A <see cref="PoseLookup"/> over the bodies' positions as written in <paramref name="level"/> (their starting poses).</summary>
        public static PoseLookup AuthoredPoses(LevelData level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            return (string id, out float x, out float y, out float angle) =>
            {
                foreach (var b in level.Blocks)
                    if (b.Id == id) { x = b.X; y = b.Y; angle = b.Angle; return true; }
                foreach (var s in level.Shapes)
                    if (s.Id == id) { x = s.X; y = s.Y; angle = s.Angle; return true; }
                x = y = angle = 0f;
                return false;
            };
        }

        /// <summary>
        /// <paramref name="cursor"/> picks up <paramref name="end"/> of wire <paramref name="wireId"/>, which unplugs
        /// that end. Whatever wire end the cursor held before is released (and snaps) first; a cursor that held the
        /// same end loses it. False, and nothing changes, when there is no such wire.
        /// </summary>
        public bool GrabWireEnd(ulong cursor, string wireId, WireEnd end)
        {
            if (wireId == null || !wiresById.TryGetValue(wireId, out var wire)) return false;
            ReleaseWireEnd(cursor);
            ulong? other = null;
            foreach (var kv in held)
                if (kv.Value.Wire == wire && kv.Value.End == end) other = kv.Key;
            if (other.HasValue) held.Remove(other.Value);

            if (end == WireEnd.A) wire.ATerminalId = "";
            else wire.BTerminalId = "";
            held[cursor] = (wire, end);
            return true;
        }

        /// <summary>The held wire end follows <paramref name="cursor"/> to (<paramref name="x"/>, <paramref name="y"/>) world pixels. False when it holds none.</summary>
        public bool MoveCursor(ulong cursor, float x, float y)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) return false;
            if (!held.TryGetValue(cursor, out var h)) return false;
            if (h.End == WireEnd.A)
            {
                h.Wire.Ax = x;
                h.Wire.Ay = y;
            }
            else
            {
                h.Wire.Bx = x;
                h.Wire.By = y;
            }
            return true;
        }

        /// <summary>
        /// <paramref name="cursor"/> lets go of its wire end, which plugs into the nearest terminal within
        /// <see cref="Tuning.WireSnapRadius"/> or stays loose where it was dropped. False when it held none.
        /// </summary>
        public bool ReleaseWireEnd(ulong cursor)
        {
            if (!held.TryGetValue(cursor, out var h)) return false;
            held.Remove(cursor);
            CircuitRules.Snap(h.Wire, h.End, world.Terminals);
            return true;
        }

        /// <summary>Which wire end <paramref name="cursor"/> holds. False when it holds none.</summary>
        public bool TryGetHeldWireEnd(ulong cursor, out string wireId, out WireEnd end)
        {
            if (held.TryGetValue(cursor, out var h))
            {
                wireId = h.Wire.Id;
                end = h.End;
                return true;
            }
            wireId = null;
            end = default;
            return false;
        }

        /// <summary>The live wire <paramref name="wireId"/>, or null.</summary>
        public Wire FindWire(string wireId) => wireId != null && wiresById.TryGetValue(wireId, out var w) ? w : null;

        private Shape FindShape(string id)
        {
            foreach (var s in world.Shapes)
                if (s.Id == id) return s;
            return null;
        }
    }
}
