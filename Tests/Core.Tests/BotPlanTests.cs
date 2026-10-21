using NUnit.Framework;
using Unity.Cursory.Core.Tests.Solvability;

namespace Unity.Cursory.Core.Tests
{
    /// <summary>
    /// UC-T14: the plan two headless bot clients follow solves level 2 (Mass 2.0, needs both) in the solvability
    /// harness, needs both of them, and parses the -bot role the way a command line types it.
    /// </summary>
    public class BotPlanTests
    {
        [Test]
        public void Level2_plan_targets_its_block_and_goal_around_the_wall()
        {
            Assert.That(BotPlan.TryCreate(LevelSeeds.Level(2), out var plan, out var why), Is.True, why);
            Assert.That(plan.Block.Id, Is.EqualTo("L2-block"));
            Assert.That(plan.Goal.Id, Is.EqualTo("L2-goal"));
            Assert.That(plan.Route.Count, Is.EqualTo(3));
            Assert.That(plan.Route[plan.Route.Count - 1], Is.EqualTo((plan.Goal.X, plan.Goal.Y)));
            Assert.That(plan.EdgeSign, Is.EqualTo(1f), "the goal is to the right, so the bots grab the right edge");

            var a = plan.GrabPoint(BotRole.A, plan.Block.X, plan.Block.Y);
            var b = plan.GrabPoint(BotRole.B, plan.Block.X, plan.Block.Y);
            Assert.That(a.X, Is.EqualTo(plan.Block.X + plan.Block.W / 2f));
            Assert.That(b.X, Is.EqualTo(a.X));
            Assert.That(a.Y - b.Y, Is.EqualTo(plan.Block.H / 2f), "A and B take opposite ends of the edge");
        }

        [Test]
        public void Two_bots_following_the_plan_solve_level2()
        {
            Assert.That(Drive(LevelSeeds.Level(2), BotRole.A, BotRole.B), Is.True);
        }

        [Test]
        public void One_bot_alone_cannot_solve_level2()
        {
            Assert.That(Drive(LevelSeeds.Level(2), BotRole.A), Is.False);
        }

        [Test]
        public void A_level_with_no_single_block_goal_has_no_plan()
        {
            Assert.That(BotPlan.TryCreate(LevelSeeds.Level(3), out _, out var why), Is.False, "level 3 is a shape goal");
            Assert.That(why, Does.Contain("block goals"));
            Assert.That(BotPlan.TryCreate(null, out _, out _), Is.False);
        }

        [TestCase("A", BotRole.A)]
        [TestCase("b", BotRole.B)]
        [TestCase(" 1 ", BotRole.A)]
        [TestCase("2", BotRole.B)]
        public void Roles_parse_from_the_command_line(string text, BotRole expected)
        {
            Assert.That(BotPlan.TryParseRole(text, out var role), Is.True);
            Assert.That(role, Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("C")]
        public void Other_roles_are_refused(string text)
        {
            Assert.That(BotPlan.TryParseRole(text, out _), Is.False);
        }

        /// <summary>Each role has its own plan instance, as each bot does; they share only the block's position.</summary>
        private static bool Drive(LevelData level, params BotRole[] roles)
        {
            var room = new PullRoom(level);
            var plans = new BotPlan[roles.Length];
            for (var i = 0; i < roles.Length; i++)
            {
                Assert.That(BotPlan.TryCreate(level, out plans[i], out var why), Is.True, why);
                var (bx, by) = room.Position(plans[i].Block.Id);
                var (gx, gy) = plans[i].GrabPoint(roles[i], bx, by);
                Assert.That(room.Grab(roles[i].ToString(), plans[i].Block.Id, gx, gy), Is.True);
            }

            for (var tick = 0; tick < 4000; tick++)
            {
                var (bx, by) = room.Position(plans[0].Block.Id);
                for (var i = 0; i < roles.Length; i++)
                {
                    var (tx, ty) = plans[i].CursorTarget(roles[i], bx, by);
                    room.SetCursor(roles[i].ToString(), tx, ty);
                }

                room.Step();
                var (x, y) = room.Position(plans[0].Block.Id);
                if (plans[0].IsSolved(x, y)) return true;
            }

            return false;
        }
    }
}
