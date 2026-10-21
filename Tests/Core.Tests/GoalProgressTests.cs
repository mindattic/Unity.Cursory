using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    /// <summary>
    /// Win detection over <see cref="LevelProgress"/>: Cursory's per-tick goal pass (a block goal while the
    /// block's centre is strictly inside, a shape goal while every piece centre is) and the level win.
    /// </summary>
    public class GoalProgressTests
    {
        private sealed class Poses
        {
            public readonly Dictionary<string, (float X, float Y, float Angle)> At = new Dictionary<string, (float, float, float)>();

            public bool Lookup(string id, out float x, out float y, out float angle)
            {
                if (id != null && At.TryGetValue(id, out var p))
                {
                    (x, y, angle) = p;
                    return true;
                }
                x = y = angle = 0f;
                return false;
            }
        }

        [Test]
        public void Level1_starts_unsolved_and_is_won_with_the_block_centre_on_the_pad()
        {
            var level = new LevelProgress(LevelSeeds.Level(1).Clone());
            var poses = new Poses();
            poses.At["L1-block"] = (3500f, 5000f, 0f);

            Assert.That(level.Evaluate(poses.Lookup), Is.False, "nothing changed from the unsolved start");
            Assert.That(level.IsLevelSolved, Is.False);

            poses.At["L1-block"] = (6500f, 5000f, 0f);
            Assert.That(level.Evaluate(poses.Lookup), Is.True);
            Assert.That(level.IsGoalSolved("L1-goal"), Is.True);
            Assert.That(level.SolvedMask, Is.EqualTo(1));
            Assert.That(level.IsLevelSolved, Is.True);
        }

        [Test]
        public void Block_goal_is_strict_at_the_edge()
        {
            var level = new LevelProgress(LevelSeeds.Level(1).Clone());   // goal 6500,5000 700x700
            var poses = new Poses();
            poses.At["L1-block"] = (6500f + 350f, 5000f, 0f);
            level.Evaluate(poses.Lookup);
            Assert.That(level.IsGoalSolved(0), Is.False, "a centre on the edge is not inside");

            poses.At["L1-block"] = (6500f + 349f, 5000f, 0f);
            level.Evaluate(poses.Lookup);
            Assert.That(level.IsGoalSolved(0), Is.True);
        }

        [Test]
        public void Two_goal_level_needs_both()
        {
            var data = LevelSeeds.Level(6).Clone();
            Assert.That(data.BlockGoals.Count, Is.EqualTo(2));
            var level = new LevelProgress(data);
            var poses = new Poses();
            var a = data.BlockGoals[0];
            var b = data.BlockGoals[1];
            poses.At[a.TargetBlockId] = (a.X, a.Y, 0f);
            poses.At[b.TargetBlockId] = (0f, 0f, 0f);

            level.Evaluate(poses.Lookup);
            Assert.That(level.SolvedMask, Is.EqualTo(0b01));
            Assert.That(level.IsLevelSolved, Is.False);

            poses.At[b.TargetBlockId] = (b.X, b.Y, 0f);
            level.Evaluate(poses.Lookup);
            Assert.That(level.SolvedMask, Is.EqualTo(0b11));
            Assert.That(level.IsLevelSolved, Is.True);
        }

        [Test]
        public void Shape_goal_needs_every_piece_centre_inside()
        {
            var data = new LevelData
            {
                Number = 1,
                Shapes =
                {
                    new Shape
                    {
                        Id = "L", X = 0f, Y = 0f, Mass = 1f,
                        Pieces =
                        {
                            new ShapePiece { LocalX = 0f, LocalY = 0f, HalfW = 50f, HalfH = 50f },
                            new ShapePiece { LocalX = 300f, LocalY = 0f, HalfW = 50f, HalfH = 50f },
                        },
                    },
                },
                ShapeGoals = { new ShapeGoal { Id = "g", X = 1000f, Y = 1000f, W = 400f, H = 400f, TargetShapeId = "L" } },
            };
            var level = new LevelProgress(data);
            var poses = new Poses();

            poses.At["L"] = (1000f, 1000f, 0f);   // origin inside, second piece at 1300: outside
            level.Evaluate(poses.Lookup);
            Assert.That(level.IsGoalSolved("g"), Is.False, "the origin alone is not enough");

            poses.At["L"] = (850f, 1000f, 0f);    // pieces at 850 and 1150: both inside 800..1200
            level.Evaluate(poses.Lookup);
            Assert.That(level.IsGoalSolved("g"), Is.True);

            poses.At["L"] = (1000f, 850f, (float)System.Math.PI / 2f);   // turned: pieces at y 850 and 1150
            level.Evaluate(poses.Lookup);
            Assert.That(level.IsGoalSolved("g"), Is.True, "the piece offsets turn with the shape");
            Assert.That(level.IsLevelSolved, Is.True);
        }

        [Test]
        public void Goal_order_is_block_goals_then_shape_goals()
        {
            var data = LevelSeeds.Level(3).Clone();
            var level = new LevelProgress(data);
            Assert.That(level.GoalCount, Is.EqualTo(data.BlockGoals.Count + data.ShapeGoals.Count));
            for (var i = 0; i < data.BlockGoals.Count; i++) Assert.That(level.GoalId(i), Is.EqualTo(data.BlockGoals[i].Id));
            for (var i = 0; i < data.ShapeGoals.Count; i++) Assert.That(level.GoalId(data.BlockGoals.Count + i), Is.EqualTo(data.ShapeGoals[i].Id));
        }

        [Test]
        public void A_missing_target_body_leaves_its_goal_unsolved()
        {
            var level = new LevelProgress(LevelSeeds.Level(1).Clone());
            level.Evaluate(new Poses().Lookup);
            Assert.That(level.IsGoalSolved("L1-goal"), Is.False);
            Assert.That(level.IsGoalSolved("not-a-goal"), Is.False);
        }

        [Test]
        public void No_level_starts_solved()
        {
            foreach (var data in LevelSeeds.All())
            {
                var level = new LevelProgress(data.Clone());
                level.Evaluate(LevelProgress.AuthoredPoses(level.World));
                Assert.That(level.SolvedMask, Is.Zero, $"level {data.Number} has a goal solved at the start");
                Assert.That(level.IsLevelSolved, Is.False, $"level {data.Number} starts won");
            }
        }

        [Test]
        public void Win_rule_over_synced_bits()
        {
            Assert.That(LevelProgress.IsWon(0, 0, false, false), Is.False, "nothing to win");
            Assert.That(LevelProgress.IsWon(0, 0, true, true), Is.True);
            Assert.That(LevelProgress.IsWon(2, 0b11, false, false), Is.True);
            Assert.That(LevelProgress.IsWon(2, 0b10, false, false), Is.False);
            Assert.That(LevelProgress.IsWon(1, 0b1, true, false), Is.False, "goals and a dark bulb");
        }
    }
}
