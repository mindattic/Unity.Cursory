using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>Which half of a two-bot team a headless bot client plays (UC-T14): A takes one end of the leading edge, B the other.</summary>
    public enum BotRole
    {
        A = 0,
        B = 1,
    }

    /// <summary>
    /// How two headless bot clients solve a one-block level together with no messages between them (UC-T14): both
    /// read the same Core <see cref="LevelData"/>, grab the block's goal-facing edge a quarter of its height either
    /// side of the centre (role A above, role B below, so the two pulls are parallel and add no torque), and pull
    /// toward the same waypoint, offset by their own anchor. Each bot advances the route from the block's replicated
    /// position alone, so both agree on the waypoint without coordinating. Engine-free, so the plan itself is proved
    /// by <c>dotnet test</c> against the solvability harness before any bot runs it in Unity.
    /// </summary>
    public sealed class BotPlan
    {
        /// <summary>How close (world pixels) the block's centre must come to a waypoint before the bots head for the next.</summary>
        public const float WaypointReach = 250f;

        // Levels whose straight line to the goal is blocked: waypoints around the walls (as in SolvabilityTests).
        private static readonly Dictionary<int, (float X, float Y)[]> Routes = new Dictionary<int, (float X, float Y)[]>
        {
            // L2: over the top of the central wall, then down onto the pad.
            [2] = new[] { (4400f, 3900f), (5800f, 3900f), (6700f, 5000f) },
            // L8: below the first jutting wall, above the second, then across to the pad.
            [8] = new[] { (4800f, 5650f), (5800f, 4350f), (7200f, 5000f) },
        };

        private readonly (float X, float Y)[] _route;
        private int _progress;
        private float _headingX;
        private float _headingY;

        private BotPlan(Block block, BlockGoal goal, (float X, float Y)[] route)
        {
            Block = block;
            Goal = goal;
            _route = route;
            float dx = route[0].X - block.X;
            EdgeSign = dx < 0f ? -1f : 1f;
            _headingX = EdgeSign;
        }

        /// <summary>The block both bots move.</summary>
        public Block Block { get; }

        /// <summary>The goal it has to reach.</summary>
        public BlockGoal Goal { get; }

        /// <summary>The waypoints, in order; the last is the goal's centre.</summary>
        public IReadOnlyList<(float X, float Y)> Route => _route;

        /// <summary>The index of the waypoint being pulled toward now.</summary>
        public int Progress => _progress;

        /// <summary>+1 when the leading edge is the block's right (+x) edge, -1 for its left.</summary>
        public float EdgeSign { get; }

        /// <summary>
        /// The plan for <paramref name="level"/>: its single block goal, the block that goal names and the level's route
        /// (or straight at the goal). False, with a reason, for a level two bots cannot solve this way (no block goal,
        /// more than one, or a goal naming a block that is not there).
        /// </summary>
        public static bool TryCreate(LevelData level, out BotPlan plan, out string why)
        {
            plan = null;
            if (level == null)
            {
                why = "no level";
                return false;
            }

            if (level.BlockGoals == null || level.BlockGoals.Count != 1)
            {
                why = "level " + level.Number + " has " + (level.BlockGoals == null ? 0 : level.BlockGoals.Count) + " block goals; the bots solve exactly one";
                return false;
            }

            BlockGoal goal = level.BlockGoals[0];
            Block block = null;
            foreach (Block b in level.Blocks)
            {
                if (b.Id == goal.TargetBlockId) block = b;
            }

            if (block == null)
            {
                why = "level " + level.Number + "'s goal names block '" + goal.TargetBlockId + "', which it does not have";
                return false;
            }

            (float X, float Y)[] route;
            if (!Routes.TryGetValue(level.Number, out route)) route = new[] { (goal.X, goal.Y) };
            plan = new BotPlan(block.Clone(), goal.Clone(), route);
            why = null;
            return true;
        }

        /// <summary>Reads a role as typed on a command line (<c>A</c>/<c>B</c>, <c>1</c>/<c>2</c>, any case); false for anything else.</summary>
        public static bool TryParseRole(string text, out BotRole role)
        {
            role = BotRole.A;
            switch ((text ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "A":
                case "1":
                    role = BotRole.A;
                    return true;
                case "B":
                case "2":
                    role = BotRole.B;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Where <paramref name="role"/>'s grab sits relative to the block's centre (world pixels, unrotated): its end of the leading edge.</summary>
        public (float X, float Y) AnchorOffset(BotRole role)
        {
            float quarter = Block.H * 0.25f;
            return (EdgeSign * Block.W * 0.5f, role == BotRole.A ? quarter : -quarter);
        }

        /// <summary>Where <paramref name="role"/> clicks to grab the block, with its centre at (<paramref name="blockX"/>, <paramref name="blockY"/>).</summary>
        public (float X, float Y) GrabPoint(BotRole role, float blockX, float blockY)
        {
            var (ox, oy) = AnchorOffset(role);
            return (blockX + ox, blockY + oy);
        }

        /// <summary>
        /// Where <paramref name="role"/>'s pointer goes this tick, with the block's centre at (<paramref name="blockX"/>,
        /// <paramref name="blockY"/>): the current waypoint plus the role's anchor offset, so both grabs pull the same way,
        /// led a rope's length (<see cref="Tuning.RopeLengthPx"/>) further on toward the waypoint, since the cursor pulls
        /// from the far end of its rope (UC-T33). The pull fades as the block closes on the waypoint, so it comes to rest
        /// on it. Moves on to the next waypoint once the block is within <see cref="WaypointReach"/> of this one.
        /// </summary>
        public (float X, float Y) CursorTarget(BotRole role, float blockX, float blockY)
        {
            while (_progress < _route.Length - 1 && Distance(blockX, blockY, _route[_progress].X, _route[_progress].Y) < WaypointReach)
            {
                _progress++;
            }

            var (wx, wy) = _route[_progress];
            var (ox, oy) = AnchorOffset(role);
            var (lx, ly) = Lead(blockX, blockY, wx, wy);
            return (wx + ox + lx, wy + oy + ly);
        }

        /// <summary>A rope's length toward (<paramref name="toX"/>, <paramref name="toY"/>) from the block, keeping the last heading once the block is on it.</summary>
        private (float X, float Y) Lead(float blockX, float blockY, float toX, float toY)
        {
            var d = Distance(blockX, blockY, toX, toY);
            if (d > 1f)
            {
                _headingX = (toX - blockX) / d;
                _headingY = (toY - blockY) / d;
            }

            return (_headingX * Tuning.RopeLengthPx, _headingY * Tuning.RopeLengthPx);
        }

        /// <summary>True when a block centred at (<paramref name="blockX"/>, <paramref name="blockY"/>) solves the goal.</summary>
        public bool IsSolved(float blockX, float blockY) => Goal.IsSatisfiedBy(blockX, blockY);

        private static float Distance(float ax, float ay, float bx, float by) =>
            (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }
}
