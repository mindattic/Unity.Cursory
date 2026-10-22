using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T33: the physical cursor and the real rope, on the server's <see cref="PhysicsWorld"/> stepped by a manual tick
    /// clock. A cursor is a light body pulled toward its pointer by a spring capped at the player's pull; a grab is a
    /// max-length rope from it to the body's rim that wraps the body's corners. So a free cursor moves like a cursor, a
    /// heavy load holds or drags it, a rope pulled round a corner turns the body toward the player, two players pulling
    /// opposite ends of a bar turn it where one cannot, and a rope never stretches past its length.
    /// </summary>
    public class PhysicalCursorTests
    {
        private const ulong A = 1;
        private const ulong B = 2;
        private const ulong C = 3;
        private const string BlockId = "b";

        private SimulationMode2D _mode;
        private ServerSimulation _sim;
        private ManualTickClock _clock;

        [SetUp]
        public void SetUp()
        {
            _mode = Physics2D.simulationMode;
            _clock = new ManualTickClock(SimulationRate.TicksPerSecond);
            _sim = new ServerSimulation();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _sim?.Dispose();
            _sim = null;
            yield return null;
            Physics2D.simulationMode = _mode;
        }

        private PhysicsWorld Load(LevelData level)
        {
            _sim.World = new PhysicsWorld(level, "UC-T33 world");
            _sim.Attach(_clock);
            return _sim.World;
        }

        private static LevelData OneBlock(float mass, float size) => new LevelData
        {
            Number = 1,
            Title = "UC-T33",
            Blocks = new List<Block> { new Block { Id = BlockId, X = 0f, Y = 0f, W = size, H = size, Mass = mass } },
        };

        private static int Ticks(float seconds) => Mathf.RoundToInt(seconds * SimulationRate.TicksPerSecond);

        private static Vector2 CursorAt(PhysicsWorld world, ulong cursor)
        {
            Assert.That(world.TryGetCursor(cursor, out var at), Is.True, "cursor " + cursor + " has no body");
            return new Vector2(at.X, at.Y);
        }

        private static Vector2 Position(PhysicsWorld world, string id)
        {
            var (x, y, _) = world.Pose(id);
            return new Vector2(x, y);
        }

        [Test]
        public void AFreeCursor_FollowsItsPointerLikeACursor()
        {
            var world = Load(OneBlock(1.0f, 100f));
            Assert.That(world.MoveCursor(A, 1000f, 1000f), Is.True);
            Assert.That(CursorAt(world, A), Is.EqualTo(new Vector2(1000f, 1000f)), "a new cursor starts at its pointer");

            world.MoveCursor(A, 1500f, 1200f);
            _clock.Step(Ticks(0.5f));

            Assert.That(Vector2.Distance(CursorAt(world, A), new Vector2(1500f, 1200f)), Is.LessThan(2f), "half a second later it is there");
            Assert.That(Position(world, BlockId), Is.EqualTo(Vector2.zero), "a free cursor touches nothing");
        }

        /// <summary>A load heavier than one player's pull: the rope holds the cursor at its end, short of the pointer.</summary>
        [Test]
        public void AHeavyBlock_HoldsALoneCursorAtItsRopesEnd()
        {
            var world = Load(OneBlock(2.0f, 100f));
            Assert.That(world.Grab(A, BlockId, 60f, 0f), Is.True);
            world.MoveCursor(A, 2000f, 0f);

            _clock.Step(Ticks(2f));

            Vector2 cursor = CursorAt(world, A);
            Assert.That(cursor.x, Is.EqualTo(50f + Tuning.RopeLengthPx).Within(1f), "held at the rope's end");
            Assert.That(2000f - cursor.x, Is.GreaterThan(1500f), "nowhere near the pointer");
            Assert.That(Position(world, BlockId).magnitude, Is.LessThan(1f), "60 N cannot beat Mass 2.0's 80 N of friction");
        }

        /// <summary>Tug of war: two pull one way, one the other; the load goes with the two and drags the lone cursor along.</summary>
        [Test]
        public void TugOfWar_TheStrongerSideDragsTheLoneCursorTowardIt()
        {
            var world = Load(OneBlock(1.0f, 100f));
            world.Grab(A, BlockId, 60f, -30f);
            world.Grab(B, BlockId, 60f, 30f);
            world.Grab(C, BlockId, -60f, 0f);
            world.MoveCursor(A, 1500f, -30f);
            world.MoveCursor(B, 1500f, 30f);
            world.MoveCursor(C, -1500f, 0f);
            _clock.Step(Ticks(0.5f));
            Vector2 lone = CursorAt(world, C);
            float block = Position(world, BlockId).x;

            _clock.Step(Ticks(3f));

            Assert.That(Position(world, BlockId).x - block, Is.GreaterThan(50f), "120 N one way, 60 N the other: Mass 1.0 moves with the two");
            Assert.That(CursorAt(world, C).x - lone.x, Is.GreaterThan(50f), "the lone cursor is dragged away from its own pointer, toward the load");
            Assert.That(world.RopeLength(C), Is.EqualTo(Tuning.RopeLengthPx).Within(1f), "its rope taut all the way");
        }

        /// <summary>
        /// A corner grab, walked round the next corner with the rope slack, then pulled: the rope pulls the corner it caught,
        /// so the block spins clockwise, toward the player. (Straight from the grab, the same pull would turn it the other way.)
        /// </summary>
        [Test]
        public void ACornerGrab_PulledRoundACorner_SpinsTheBodyTowardThePlayer()
        {
            var world = Load(OneBlock(1.0f, 160f));
            Assert.That(world.Grab(A, BlockId, 90f, 90f), Is.True);
            Assert.That(world.TryGetGrab(A, out _, out var anchor), Is.True);
            Assert.That(anchor, Is.EqualTo((80f, 80f)), "the top-right corner");

            // Walk down the right side and round under the block, the rope slack all the way.
            var pointer = new Vector2(90f, 90f);
            Vector2[] path = { new Vector2(250f, 80f), new Vector2(230f, -150f), new Vector2(130f, -190f), new Vector2(90f, -200f), new Vector2(40f, -200f) };
            foreach (Vector2 waypoint in path)
            {
                while (pointer != waypoint)
                {
                    pointer = Vector2.MoveTowards(pointer, waypoint, 8f);
                    world.MoveCursor(A, pointer.x, pointer.y);
                    _clock.Step(1);
                }
            }

            _clock.Step(Ticks(0.5f));
            var chain = new List<(float X, float Y)>();
            Assert.That(world.TryGetRope(A, chain), Is.True);
            Assert.That(chain, Has.Count.EqualTo(2), "the rope has caught a second corner");
            Assert.That(chain[1].X, Is.EqualTo(80f).Within(1f));
            Assert.That(chain[1].Y, Is.EqualTo(-80f).Within(1f), "the bottom-right one");
            Assert.That(Mathf.Abs(world.Pose(BlockId).Angle), Is.LessThan(0.02f), "a slack rope pulled nothing on the way round");

            world.MoveCursor(A, -1500f, -1700f);
            _clock.Step(Ticks(1.5f));

            Assert.That(world.Pose(BlockId).Angle, Is.LessThan(-0.2f), "pulled on the caught corner, the block spins clockwise, toward the player");
        }

        [Test]
        public void TheRope_NeverStretchesPastItsLength()
        {
            // Mass 2.0: one cursor neither drags nor turns it, so the cursor swings round it and the rope wraps its corners.
            var world = Load(OneBlock(2.0f, 200f));
            world.Grab(A, BlockId, 110f, 110f);
            float longest = 0f;
            int mostPoints = 0;
            var chain = new List<(float X, float Y)>();
            for (int tick = 0; tick < Ticks(6f); tick++)
            {
                // Swing the pointer round the block, far outside the rope's reach, twice.
                float a = tick * 4f * Mathf.PI / Ticks(6f);
                world.MoveCursor(A, 900f * Mathf.Cos(a), 900f * Mathf.Sin(a));
                _clock.Step(1);

                float length = world.RopeLength(A);
                Assert.That(length, Is.LessThanOrEqualTo(Tuning.RopeLengthPx + 0.05f), "tick " + tick);
                longest = Mathf.Max(longest, length);
                world.TryGetRope(A, chain);
                mostPoints = Mathf.Max(mostPoints, chain.Count);
            }

            Assert.That(longest, Is.GreaterThan(Tuning.RopeLengthPx - 1f), "the rope was pulled taut");
            Assert.That(mostPoints, Is.GreaterThanOrEqualTo(2), "and wrapped round a corner on the way");
        }

        /// <summary>
        /// The owner's example: a long bar (280 × 40, Mass 2.0) across the mouth of a narrow vertical hall. One player pulling
        /// an end flat out, always square to the bar, cannot turn it (60 N at 1.4 m is 84 N·m against 96 of friction) or drag
        /// it (60 N against 80); two players on opposite ends, pulling opposite ways, turn it in place (168 N·m, no net force)
        /// and then pull it up into the hall together (120 N).
        /// </summary>
        [Test]
        public void OnePlayer_CannotTurnALongBarIntoAHall_ButTwoOnOppositeEndsCan()
        {
            var alone = Load(HallPlay.Level());
            HallPlay.GrabEnds(alone, A, null);
            bool soloSolved = Play(alone, A, null);
            Assert.That(soloSolved, Is.False, "one player got the bar into the hall");
            Assert.That(Mathf.Abs(alone.Pose(HallPlay.BarId).Angle), Is.LessThan(0.05f), "one player cannot even start turning it");

            _sim.World.Dispose();
            var together = Load(HallPlay.Level());
            HallPlay.GrabEnds(together, A, B);
            Assert.That(Play(together, A, B), Is.True,
                "two players never got it in: the bar is at " + Position(together, HallPlay.BarId) + ", " + together.Pose(HallPlay.BarId).Angle * Mathf.Rad2Deg + "°");
        }

        /// <summary>Plays <see cref="HallPlay"/> for up to <see cref="HallPlay.Seconds"/>; true once the bar is in the hall.</summary>
        private bool Play(PhysicsWorld world, ulong right, ulong? left)
        {
            float before = world.Pose(HallPlay.BarId).Angle;
            for (int tick = 0; tick < Ticks(HallPlay.Seconds); tick++)
            {
                var (x, y, angle) = world.Pose(HallPlay.BarId);
                if (HallPlay.IsInTheHall(x, y, angle)) return true;
                float spin = (angle - before) / SimulationRate.TickSeconds;
                before = angle;
                Vector2 leftCursor = left.HasValue ? CursorAt(world, left.Value) : Vector2.zero;
                var plan = HallPlay.Pointers(x, y, angle, spin, CursorAt(world, right), leftCursor, left.HasValue);
                world.MoveCursor(right, plan.Right.x, plan.Right.y);
                if (left.HasValue) world.MoveCursor(left.Value, plan.Left.x, plan.Left.y);
                _clock.Step(1);
            }

            var (fx, fy, fa) = world.Pose(HallPlay.BarId);
            return HallPlay.IsInTheHall(fx, fy, fa);
        }
    }

    /// <summary>
    /// The hall puzzle of UC-T33 and how its players play it, shared by the physics test and the networked bot test: the
    /// level, where each player grabs, where each points given the bar's pose, and when the bar is in.
    /// </summary>
    internal static class HallPlay
    {
        public const string BarId = "bar";
        public const float BarLength = 280f;
        public const float BarThickness = 40f;

        /// <summary>The hall's inner half-width: 60 px either side of x = 0, so only an upright bar fits.</summary>
        public const float HallHalfWidth = 60f;

        /// <summary>The hall's mouth: walls run up from here.</summary>
        public const float HallMouthY = 200f;

        /// <summary>How long a team has (the N of the ticket).</summary>
        public const float Seconds = 10f;

        /// <summary>How close to upright the bar has to stand before the team pulls it up the hall.</summary>
        public const float UprightDegrees = 6f;

        /// <summary>The fastest the team lets the bar turn (rad/s) before easing off, so it doesn't swing past upright.</summary>
        public const float MaxSpin = 0.5f;

        /// <summary>How hard a player steers their end toward the hall's centre line while pulling up it.</summary>
        public const float Steer = 4f;

        /// <summary>The bar across the hall's mouth at the world's centre, offset by (<paramref name="ox"/>, <paramref name="oy"/>).</summary>
        public static LevelData Level(float ox = 0f, float oy = 0f) => new LevelData
        {
            Number = 1,
            Title = "UC-T33 hall",
            Blocks = new List<Block> { new Block { Id = BarId, X = ox, Y = oy, W = BarLength, H = BarThickness, Mass = 2.0f } },
            BlockGoals = new List<BlockGoal>
            {
                new BlockGoal { Id = "in-the-hall", X = ox, Y = oy + HallMouthY + 500f, W = HallHalfWidth * 2f, H = 1000f, TargetBlockId = BarId },
            },
            Walls = new List<Wall>
            {
                new Wall { Id = "hall-left", X = ox - HallHalfWidth - 20f, Y = oy + HallMouthY + 500f, W = 40f, H = 1000f },
                new Wall { Id = "hall-right", X = ox + HallHalfWidth + 20f, Y = oy + HallMouthY + 500f, W = 40f, H = 1000f },
            },
        };

        /// <summary>The right player grabs the bar's right end, the left player (if any) its left end.</summary>
        public static void GrabEnds(PhysicsWorld world, ulong right, ulong? left)
        {
            Assert.That(world.Grab(right, BarId, BarLength * 0.5f + 10f, 0f), Is.True);
            if (left.HasValue) Assert.That(world.Grab(left.Value, BarId, -BarLength * 0.5f - 10f, 0f), Is.True);
        }

        /// <summary>
        /// Where each player points with the bar at (<paramref name="x"/>, <paramref name="y"/>) turned by
        /// <paramref name="angle"/> and turning at <paramref name="spin"/> rad/s, their cursors at
        /// <paramref name="rightCursor"/> and <paramref name="leftCursor"/>, relative to the hall's centre line at
        /// <paramref name="hallX"/>. Turning: each pulls their end square to the bar, the right end toward upright, the left
        /// end (when there is a second player) the opposite way, a counter-force couple; while the bar already turns faster
        /// than <see cref="MaxSpin"/> they ease off (point at their own cursors, so the ropes go slack), as players do to
        /// stop it swinging past. Once it stands within <see cref="UprightDegrees"/> of upright both pull up the hall, each
        /// steering their end back over the hall's centre line.
        /// </summary>
        public static (Vector2 Right, Vector2 Left) Pointers(float x, float y, float angle, float spin, Vector2 rightCursor, Vector2 leftCursor,
            bool twoPlayers, float hallX = 0f)
        {
            const float Far = 1200f;
            float half = BarLength * 0.5f;
            var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var square = new Vector2(-along.y, along.x);
            var centre = new Vector2(x, y);
            Vector2 rightEnd = centre + along * half;
            Vector2 leftEnd = centre - along * half;
            float error = Mathf.PI * 0.5f - angle;
            if (Mathf.Abs(error) < UprightDegrees * Mathf.Deg2Rad)
            {
                // Pull up the hall, each end steered over its centre line, which also squares the bar up as it goes in.
                float up = y + 1500f;
                return (new Vector2(hallX + (hallX - rightEnd.x) * Steer, up), new Vector2(hallX + (hallX - leftEnd.x) * Steer, up));
            }

            float turn = Mathf.Sign(error);
            if (spin * turn > MaxSpin) return (rightCursor, leftCursor);
            return (rightEnd + square * (Far * turn), twoPlayers ? leftEnd - square * (Far * turn) : leftCursor);
        }

        /// <summary>True when the bar stands upright (within 10°) with its centre up in the hall.</summary>
        public static bool IsInTheHall(float x, float y, float angle, float hallX = 0f, float hallY = 0f)
        {
            return Mathf.Abs(Mathf.Abs(angle) - Mathf.PI * 0.5f) < 10f * Mathf.Deg2Rad
                && Mathf.Abs(x - hallX) < HallHalfWidth
                && y - hallY > HallMouthY + 100f;
        }
    }
}
