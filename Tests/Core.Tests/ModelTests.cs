using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    public class TuningTests
    {
        [Test]
        public void Cursory_feel_constants_are_kept()
        {
            Assert.That(Tuning.GrabMaxForce, Is.EqualTo(60f));
            Assert.That(Tuning.ForcePerMass, Is.EqualTo(40f));
            Assert.That(Tuning.InertiaKgPerMass, Is.EqualTo(5f));
            Assert.That(Tuning.FrictionTorqueScale, Is.EqualTo(1.2f));
            Assert.That(Tuning.LinearDamping, Is.EqualTo(1.5f));
            Assert.That(Tuning.AngularDamping, Is.EqualTo(2.0f));
            Assert.That(Tuning.RopeLengthPx, Is.EqualTo(300f));
            Assert.That(Tuning.CursorRadius, Is.EqualTo(10f));
            Assert.That(Tuning.WireSnapRadius, Is.EqualTo(90f));
        }

        [Test]
        public void The_cursor_is_a_light_body_on_a_capped_spring()
        {
            Assert.That(Tuning.CursorMassKg, Is.EqualTo(0.1f));
            Assert.That(Tuning.CursorFrequency, Is.EqualTo(6f));
            Assert.That(Tuning.CursorDamping, Is.EqualTo(1f));
            Assert.That(Tuning.CursorLinearDamping, Is.EqualTo(30f));
            Assert.That(Tuning.CursorStiffness, Is.EqualTo(0.1f * (12f * (float)Math.PI) * (12f * (float)Math.PI)).Within(1e-3f));
            Assert.That(Tuning.CursorSaturationPx, Is.InRange(30f, 60f), "the pull caps a short way past the rope's end");
        }

        [TestCase(-10f, 0f)]
        [TestCase(0f, 0f)]
        [TestCase(10f, 14.21f)]
        [TestCase(1000f, 60f)]
        public void Cursor_pull_grows_with_the_stretch_up_to_the_cap(float stretchPx, float newtons)
        {
            Assert.That(Tuning.CursorPull(stretchPx), Is.EqualTo(newtons).Within(0.01f));
        }

        [Test]
        public void One_grab_tops_out_at_one_and_a_half_mass_units()
        {
            Assert.That(Tuning.SingleGrabMaxMass, Is.EqualTo(1.5f));
        }

        [Test]
        public void Physics2D_mapping_scales_with_Mass()
        {
            Assert.That(Tuning.FrictionForce(2f), Is.EqualTo(80f));
            Assert.That(Tuning.FrictionTorque(2f), Is.EqualTo(96f).Within(1e-4f));
            Assert.That(Tuning.RigidbodyMass(2f), Is.EqualTo(10f));
        }

        [Test]
        public void One_cursor_moves_one_but_not_two_and_two_cursors_move_two()
        {
            // UC-LAW-2's regression line, as arithmetic on the force caps.
            Assert.That(Tuning.FrictionForce(1f), Is.LessThan(Tuning.GrabMaxForce));
            Assert.That(Tuning.FrictionForce(2f), Is.GreaterThan(Tuning.GrabMaxForce));
            Assert.That(Tuning.FrictionForce(2f), Is.LessThan(2 * Tuning.GrabMaxForce));
        }

        [TestCase(0.5f, 1)]
        [TestCase(1.0f, 1)]
        [TestCase(1.3f, 1)]
        [TestCase(1.5f, 2)]
        [TestCase(2.0f, 2)]
        [TestCase(2.6f, 2)]
        [TestCase(3.0f, 3)]
        [TestCase(4.0f, 3)]
        public void Cursors_needed_is_the_fewest_whose_pulls_sum_past_Mass(float mass, int expected)
        {
            Assert.That(Tuning.CursorsNeeded(mass), Is.EqualTo(expected));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void Cursors_needed_refuses_a_non_positive_Mass(float mass)
        {
            Assert.That(() => Tuning.CursorsNeeded(mass), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(1.0f, true)]
        [TestCase(1.1f, true)]
        [TestCase(1.3f, true)]
        [TestCase(2.0f, true)]
        [TestCase(2.2f, true)]
        [TestCase(2.6f, true)]
        [TestCase(0.2f, true)]
        [TestCase(1.4f, false)]
        [TestCase(1.5f, false)]
        [TestCase(1.6f, false)]
        [TestCase(2.9f, false)]
        [TestCase(3.0f, false)]
        [TestCase(0f, false)]
        [TestCase(-2f, false)]
        public void Every_Cursory_level_mass_is_clear_of_the_thresholds(float mass, bool clear)
        {
            Assert.That(Tuning.IsClearOfThresholds(mass), Is.EqualTo(clear));
        }
    }

    public class HexColorTests
    {
        [Test]
        public void Reads_RRGGBB_in_either_case()
        {
            Assert.That(HexColor.TryParse("#D85A30", out var r, out var g, out var b), Is.True);
            Assert.That((r, g, b), Is.EqualTo(((byte)0xD8, (byte)0x5A, (byte)0x30)));
            Assert.That(HexColor.TryParse("#caa472", out r, out g, out b), Is.True);
            Assert.That((r, g, b), Is.EqualTo(((byte)0xCA, (byte)0xA4, (byte)0x72)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("D85A30")]
        [TestCase("#D85A3")]
        [TestCase("#D85A300")]
        [TestCase("#G85A30")]
        public void Refuses_anything_else(string hex)
        {
            Assert.That(HexColor.IsValid(hex), Is.False);
        }
    }

    public class GoalTests
    {
        private static readonly BlockGoal Goal = new BlockGoal { Id = "g", X = 6500, Y = 5000, W = 700, H = 700, TargetBlockId = "b" };

        [Test]
        public void A_block_goal_is_solved_while_the_block_centre_is_strictly_inside()
        {
            Assert.That(Goal.IsSatisfiedBy(6500, 5000), Is.True);
            Assert.That(Goal.IsSatisfiedBy(6849, 5349), Is.True);
            Assert.That(Goal.IsSatisfiedBy(6850, 5000), Is.False, "on the edge is outside");
            Assert.That(Goal.IsSatisfiedBy(3500, 5000), Is.False);
        }

        private static Shape LShape() => new Shape
        {
            Id = "L", Mass = 2,
            Pieces = new List<ShapePiece>
            {
                new ShapePiece { LocalX = 0, LocalY = 0, HalfW = 220, HalfH = 55 },
                new ShapePiece { LocalX = -165, LocalY = -165, HalfW = 55, HalfH = 165 },
            },
        };

        [Test]
        public void A_piece_centre_turns_with_its_shape()
        {
            var piece = new ShapePiece { LocalX = 100, LocalY = 0 };
            var (x, y) = piece.WorldCentre(1000, 1000, (float)(Math.PI / 2));
            Assert.That(x, Is.EqualTo(1000f).Within(1e-3f));
            Assert.That(y, Is.EqualTo(1100f).Within(1e-3f));
        }

        [Test]
        public void A_shape_goal_needs_every_piece_centre_inside()
        {
            var goal = new ShapeGoal { Id = "g", X = 7200, Y = 5000, W = 1000, H = 1000, TargetShapeId = "L" };
            var shape = LShape();
            Assert.That(goal.IsSatisfiedBy(shape, 7200, 5000, 0), Is.True);
            // The origin is inside, but the foot (165 px left and down) is not.
            Assert.That(goal.IsSatisfiedBy(shape, 6800, 5000, 0), Is.False);
            // Turned half a revolution, the foot swings right and up, back inside.
            Assert.That(goal.IsSatisfiedBy(shape, 6800, 5000, (float)Math.PI), Is.True);
        }

        [Test]
        public void A_shape_with_no_pieces_never_solves_a_goal()
        {
            var goal = new ShapeGoal { Id = "g", X = 0, Y = 0, W = 100, H = 100 };
            Assert.That(goal.IsSatisfiedBy(new Shape(), 0, 0, 0), Is.False);
            Assert.That(goal.IsSatisfiedBy(null, 0, 0, 0), Is.False);
        }
    }

    public class CircuitModelTests
    {
        [Test]
        public void A_wire_end_with_no_terminal_is_loose()
        {
            var wire = new Wire { Id = "w" };
            Assert.That(wire.IsAPlugged || wire.IsBPlugged || wire.IsConnected, Is.False);
            wire.ATerminalId = "bat+";
            Assert.That(wire.IsAPlugged, Is.True);
            Assert.That(wire.IsConnected, Is.False);
            wire.BTerminalId = "r-a";
            Assert.That(wire.IsConnected, Is.True);
            wire.BTerminalId = null;
            Assert.That(wire.IsBPlugged, Is.False, "null and empty both mean loose");
        }

        [Test]
        public void Component_kinds_and_polarities_keep_stable_wire_values()
        {
            Assert.That((int)ComponentKind.Battery, Is.EqualTo(0));
            Assert.That((int)ComponentKind.Resistor, Is.EqualTo(1));
            Assert.That((int)ComponentKind.Bulb, Is.EqualTo(2));
            Assert.That((int)TerminalPolarity.None, Is.EqualTo(0));
            Assert.That((int)TerminalPolarity.Positive, Is.EqualTo(1));
            Assert.That((int)TerminalPolarity.Negative, Is.EqualTo(2));
        }
    }

    public class WhistleTests
    {
        [Test]
        public void A_whistle_is_live_for_one_second()
        {
            var whistle = new Whistle { PlayerId = 2, Tick = 60, X = 10, Y = 20 };
            Assert.That(Whistle.BroadcastTicks, Is.EqualTo(SimulationRate.TicksPerSecond));
            Assert.That(whistle.IsLive(60), Is.True);
            Assert.That(whistle.IsLive(89), Is.True);
            Assert.That(whistle.IsLive(90), Is.False);
        }
    }
}
