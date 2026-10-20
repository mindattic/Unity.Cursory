using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnityCursory.Core.Tests.Solvability
{
    /// <summary>
    /// Ported from Cursory.Tests/SolvabilityTests.cs. Two virtual cursors grab each level's bodies by the
    /// edge and follow a route of waypoints toward the goal through <see cref="PullRoom"/>, and the goal has
    /// to solve. That catches "too heavy to ever move", "gap too tight to pass" and "goal unreachable" without
    /// the editor or two humans.
    ///
    /// As in Cursory, the block levels get a full auto-solve; the rotation and threading shape levels (3, 9,
    /// 12) can't be solved by a straight two-cursor pull (Spinner jams on purpose until you turn the bar), so
    /// they only have to prove the body is movable and makes real progress. Level 14 is the circuit, with no
    /// body to move; its wiring is checked in <see cref="LevelSeedTests"/>.
    /// </summary>
    public class SolvabilityTests
    {
        private const int MaxTicks = 4000;

        // Routes for levels whose straight line to the goal is blocked: waypoints that go around the walls.
        // Levels not listed drive straight at the goal centre.
        private static readonly Dictionary<int, (float X, float Y)[]> Routes = new Dictionary<int, (float X, float Y)[]>
        {
            // L2: over the top of the central wall, then down to the pad.
            [2] = new[] { (4400f, 3900f), (5800f, 3900f), (6700f, 5000f) },
            // L8: below the first jutting wall, above the second, then across to the pad.
            [8] = new[] { (4800f, 5650f), (5800f, 4350f), (7200f, 5000f) },
        };

        [TestCase(1)] [TestCase(2)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        [TestCase(7)] [TestCase(8)] [TestCase(10)] [TestCase(11)] [TestCase(13)]
        public void Block_level_is_solvable_by_two_cursors(int number)
        {
            var room = new PullRoom(LevelSeeds.Level(number));
            var goals = room.Level.BlockGoals;
            Assert.That(goals, Is.Not.Empty, $"level {number} has no block goal");

            // One goal: both cursors on that block. Two goals: a cursor each.
            var assignments = goals.Count == 1
                ? new[] { (Goal: goals[0], User: "u1"), (Goal: goals[0], User: "u2") }
                : new[] { (Goal: goals[0], User: "u1"), (Goal: goals[1], User: "u2") };

            // Grab the goal-facing (leading) edge, as Cursory's harness does.
            foreach (var (goal, user) in assignments)
            {
                var b = room.Level.Blocks.Single(x => x.Id == goal.TargetBlockId);
                var dir = Math.Sign(goal.X - b.X);
                if (dir == 0) dir = 1;
                Assert.That(room.Grab(user, b.Id, b.X + dir * b.W / 2f, b.Y), Is.True, $"{user} failed to grab {b.Id}");
            }

            var solved = DriveToGoals(room, number, assignments);
            var diag = string.Join("; ", goals.Select(g =>
            {
                var (x, y) = room.Position(g.TargetBlockId);
                return $"{g.TargetBlockId} at ({x:F0},{y:F0}) goal ({g.X:F0},{g.Y:F0}) solved={g.IsSatisfiedBy(x, y)}";
            }));
            Assert.That(solved, Is.True, $"level {number} did not solve in {MaxTicks} ticks: {diag}");
        }

        /// <summary>
        /// Rotation and threading shape levels: a straight pull can't finesse them, so just prove the shape is
        /// movable: two cursors make real progress toward the goal (it isn't too heavy or stuck).
        /// </summary>
        [TestCase(3)] [TestCase(9)] [TestCase(12)]
        public void Shape_level_body_is_movable(int number)
        {
            var room = new PullRoom(LevelSeeds.Level(number));
            Assert.That(room.Level.ShapeGoals, Is.Not.Empty, $"level {number} has no shape goal");
            var goal = room.Level.ShapeGoals[0];
            var shape = room.Level.Shapes.Single(s => s.Id == goal.TargetShapeId);
            var startDist = Dist(shape.X, shape.Y, goal.X, goal.Y);

            // Grab both ends of the shape's span and pull both toward the goal.
            Assert.That(room.Grab("u1", shape.Id, shape.X - 250, shape.Y), Is.True);
            Assert.That(room.Grab("u2", shape.Id, shape.X + 250, shape.Y), Is.True);
            for (var i = 0; i < 1500; i++)
            {
                room.SetCursor("u1", goal.X, goal.Y);
                room.SetCursor("u2", goal.X, goal.Y);
                room.Step();
            }

            var (x, y) = room.Position(shape.Id);
            var endDist = Dist(x, y, goal.X, goal.Y);
            // 500 px of progress proves the body moves. A puzzle like Spinner then jams the straight pull at
            // the slot until a human turns it, so reaching the goal is not required here.
            Assert.That(endDist, Is.LessThan(startDist - 500), $"level {number} shape barely moved: {startDist:F0} -> {endDist:F0} px to goal");
        }

        /// <summary>The levels tuned for two keep that promise in the harness too: one cursor can't budge their heavy body.</summary>
        [TestCase(2)] [TestCase(4)] [TestCase(5)] [TestCase(7)] [TestCase(8)] [TestCase(10)] [TestCase(13)]
        public void One_cursor_cannot_move_a_cooperative_block(int number)
        {
            var room = new PullRoom(LevelSeeds.Level(number));
            var goal = room.Level.BlockGoals.Single();
            var b = room.Level.Blocks.Single(x => x.Id == goal.TargetBlockId);
            Assert.That(room.Grab("u1", b.Id, b.X + b.W / 2f, b.Y), Is.True);
            for (var i = 0; i < 300; i++)
            {
                room.SetCursor("u1", goal.X, goal.Y);
                room.Step();
            }
            Assert.That(room.Position(b.Id), Is.EqualTo((b.X, b.Y)), $"one cursor moved level {number}'s Mass {b.Mass} block");
        }

        /// <summary>The harness catches what it is for: a block too big for its gap, or too heavy for two, never solves.</summary>
        [Test]
        public void A_gap_too_tight_or_a_block_too_heavy_does_not_solve()
        {
            var tight = LevelSeeds.Level(13);
            tight.Blocks[0].W = tight.Blocks[0].H = 320; // the gap is 300 tall
            Assert.That(SolveSingleGoal(tight), Is.False, "a 320-pixel block passed a 300-pixel gap");

            var heavy = LevelSeeds.Level(5);
            heavy.Blocks[0].Mass = 3.2f; // two grabs top out at 3.0
            Assert.That(SolveSingleGoal(heavy), Is.False, "two cursors moved Mass 3.2");

            Assert.That(SolveSingleGoal(LevelSeeds.Level(13)), Is.True, "the real level 13 still solves");
        }

        private static bool SolveSingleGoal(LevelData level)
        {
            var room = new PullRoom(level);
            var goal = room.Level.BlockGoals.Single();
            var b = room.Level.Blocks.Single(x => x.Id == goal.TargetBlockId);
            room.Grab("u1", b.Id, b.X + b.W / 2f, b.Y);
            room.Grab("u2", b.Id, b.X + b.W / 2f, b.Y);
            return DriveToGoals(room, level.Number, new[] { (goal, "u1"), (goal, "u2") });
        }

        /// <summary>The harness itself honours BIBLE UC-LAW-2: one cursor moves Mass 1.0 but not 2.0; two move 2.0.</summary>
        [Test]
        public void Harness_follows_the_Mass_rule()
        {
            Assert.That(Moved(mass: 1.0f, cursors: 1), Is.True, "one cursor moves 1.0");
            Assert.That(Moved(mass: 2.0f, cursors: 1), Is.False, "one cursor does not move 2.0");
            Assert.That(Moved(mass: 2.0f, cursors: 2), Is.True, "two cursors move 2.0");
            Assert.That(Moved(mass: 3.2f, cursors: 2), Is.False, "two cursors do not move 3.2");
        }

        private static bool Moved(float mass, int cursors)
        {
            var level = new LevelData
            {
                Blocks = { new Block { Id = "b", X = 5000, Y = 5000, W = 200, H = 200, Mass = mass } },
            };
            var room = new PullRoom(level);
            for (var c = 0; c < cursors; c++) room.Grab($"u{c}", "b", 5100, 5000);
            for (var i = 0; i < 60; i++)
            {
                for (var c = 0; c < cursors; c++) room.SetCursor($"u{c}", 9000, 5000);
                room.Step();
            }
            return room.Position("b").X > 5001f;
        }

        private static bool DriveToGoals(PullRoom room, int number, (BlockGoal Goal, string User)[] assignments)
        {
            // Each cursor walks its route; cursors on one block share the same route progress.
            Routes.TryGetValue(number, out var waypoints);
            var progress = assignments.ToDictionary(a => a.User, _ => 0);

            for (var tick = 0; tick < MaxTicks; tick++)
            {
                foreach (var (goal, user) in assignments)
                {
                    var (bx, by) = room.Position(goal.TargetBlockId);
                    var route = waypoints ?? new[] { (goal.X, goal.Y) };
                    var i = Math.Min(progress[user], route.Length - 1);
                    var (wx, wy) = route[i];
                    if (i < route.Length - 1 && Dist(bx, by, wx, wy) < 250) progress[user] = i + 1;
                    room.SetCursor(user, wx, wy);
                }
                room.Step();
                if (room.AllGoalsSolved()) return true;
            }
            return false;
        }

        private static float Dist(float ax, float ay, float bx, float by) =>
            (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }
}
