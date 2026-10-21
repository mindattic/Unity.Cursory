using System.Linq;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    /// <summary>
    /// Ported from Cursory.Tests/CircuitTests.cs: level 14's series circuit lights the bulb only when the wires
    /// close battery+ → resistor → bulb → battery− with both the resistor and the bulb in series. Plus the wire
    /// snapping that wiring it relies on (<see cref="Tuning.WireSnapRadius"/>).
    /// </summary>
    public class CircuitTests
    {
        private const ulong U1 = 1, U2 = 2;

        private static LevelProgress BuildLevel14() => new LevelProgress(LevelSeeds.Level(14).Clone());

        /// <summary>Grab a wire end, drag it onto a terminal, and release so it snaps (Cursory's Connect helper).</summary>
        private static void Connect(LevelProgress level, string wireId, WireEnd end, string terminalId)
        {
            var t = level.World.Terminals.First(x => x.Id == terminalId);
            Assert.That(level.GrabWireEnd(U1, wireId, end), Is.True);
            Assert.That(level.MoveCursor(U1, t.X, t.Y), Is.True, "the held end follows the cursor onto the terminal");
            Assert.That(level.ReleaseWireEnd(U1), Is.True, "release snaps it to the terminal under it");
        }

        [Test]
        public void Bulb_lights_on_complete_series_loop()
        {
            var level = BuildLevel14();
            Connect(level, "w1", WireEnd.A, "bat+");
            Connect(level, "w1", WireEnd.B, "r-a");
            Connect(level, "w2", WireEnd.A, "r-b");
            Connect(level, "w2", WireEnd.B, "b-a");
            Connect(level, "w3", WireEnd.A, "b-b");
            Connect(level, "w3", WireEnd.B, "bat-");
            level.Evaluate(LevelProgress.AuthoredPoses(level.World));

            Assert.That(level.BulbLit, Is.True, "complete series loop should light the bulb");
            Assert.That(level.IsLevelSolved, Is.True, "level 14 is won by the lit bulb alone");
        }

        [Test]
        public void Bulb_stays_dark_with_a_gap_in_the_loop()
        {
            var level = BuildLevel14();
            Connect(level, "w1", WireEnd.A, "bat+");
            Connect(level, "w1", WireEnd.B, "r-a");
            Connect(level, "w2", WireEnd.A, "r-b");
            Connect(level, "w2", WireEnd.B, "b-a");
            Connect(level, "w3", WireEnd.A, "b-b");
            // w3 end B left loose.
            level.Evaluate(LevelProgress.AuthoredPoses(level.World));

            Assert.That(level.BulbLit, Is.False, "an open loop must not light the bulb");
            Assert.That(level.IsLevelSolved, Is.False);
        }

        [Test]
        public void Bulb_stays_dark_when_resistor_is_bypassed()
        {
            var level = BuildLevel14();
            Connect(level, "w1", WireEnd.A, "bat+");
            Connect(level, "w1", WireEnd.B, "b-a");
            Connect(level, "w3", WireEnd.A, "b-b");
            Connect(level, "w3", WireEnd.B, "bat-");
            level.Evaluate(LevelProgress.AuthoredPoses(level.World));

            Assert.That(level.BulbLit, Is.False, "resistor must be in series for the bulb to light");
        }

        [Test]
        public void Bulb_stays_dark_when_bulb_is_shorted()
        {
            var level = BuildLevel14();
            Connect(level, "w1", WireEnd.A, "bat+");
            Connect(level, "w1", WireEnd.B, "r-a");
            Connect(level, "w2", WireEnd.A, "r-b");
            Connect(level, "w2", WireEnd.B, "b-a");
            // w3 bridges r-b straight to bat-, so current bypasses the bulb.
            Connect(level, "w3", WireEnd.A, "r-b");
            Connect(level, "w3", WireEnd.B, "bat-");
            level.Evaluate(LevelProgress.AuthoredPoses(level.World));

            Assert.That(level.BulbLit, Is.False, "a bulb with a wire around it is not in series");
        }

        [Test]
        public void Unplugging_a_wire_end_darkens_the_bulb()
        {
            var level = BuildLevel14();
            Connect(level, "w1", WireEnd.A, "bat+");
            Connect(level, "w1", WireEnd.B, "r-a");
            Connect(level, "w2", WireEnd.A, "r-b");
            Connect(level, "w2", WireEnd.B, "b-a");
            Connect(level, "w3", WireEnd.A, "b-b");
            Connect(level, "w3", WireEnd.B, "bat-");
            Assert.That(level.Evaluate(LevelProgress.AuthoredPoses(level.World)), Is.True, "lighting is a change");

            Assert.That(level.GrabWireEnd(U2, "w2", WireEnd.A), Is.True);
            Assert.That(level.FindWire("w2").IsAPlugged, Is.False, "picking an end up unplugs it");
            Assert.That(level.Evaluate(LevelProgress.AuthoredPoses(level.World)), Is.True);
            Assert.That(level.BulbLit, Is.False);
        }

        [Test]
        public void Release_snaps_within_the_radius_and_moves_the_end_onto_the_terminal()
        {
            var level = BuildLevel14();
            Assert.That(level.GrabWireEnd(U1, "w1", WireEnd.A), Is.True);
            level.MoveCursor(U1, 4700f + 60f, 3900f + 60f);   // 84.9 px from r-a: inside 90
            level.ReleaseWireEnd(U1);

            var w = level.FindWire("w1");
            Assert.That(w.ATerminalId, Is.EqualTo("r-a"));
            Assert.That((w.Ax, w.Ay), Is.EqualTo((4700f, 3900f)));
        }

        [Test]
        public void Release_beyond_the_radius_leaves_the_end_loose_where_dropped()
        {
            var level = BuildLevel14();
            level.GrabWireEnd(U1, "w1", WireEnd.B);
            level.MoveCursor(U1, 4700f + 91f, 3900f);
            level.ReleaseWireEnd(U1);

            var w = level.FindWire("w1");
            Assert.That(w.IsBPlugged, Is.False);
            Assert.That((w.Bx, w.By), Is.EqualTo((4791f, 3900f)));
        }

        [Test]
        public void Snap_radius_is_inclusive_and_picks_the_nearest_terminal()
        {
            var terminals = LevelSeeds.Level(14).Terminals;
            Assert.That(Tuning.WireSnapRadius, Is.EqualTo(90f));
            Assert.That(CircuitRules.NearestTerminal(terminals, 4700f, 3900f + 90f)?.Id, Is.EqualTo("r-a"), "exactly 90 px still snaps");
            Assert.That(CircuitRules.NearestTerminal(terminals, 4700f, 3900f + 90.5f), Is.Null);
            // Halfway between r-a (4700) and r-b (5300) neither is within 90; 50 px from r-b it is.
            Assert.That(CircuitRules.NearestTerminal(terminals, 5000f, 3900f), Is.Null);
            Assert.That(CircuitRules.NearestTerminal(terminals, 5250f, 3900f)?.Id, Is.EqualTo("r-b"));
            Assert.That(CircuitRules.NearestTerminal(terminals, 5000f, 3900f, radius: 400f)?.Id, Is.EqualTo("r-b"), "a tie goes to the later terminal, as in Cursory");
        }

        [Test]
        public void A_cursor_holds_one_wire_end_and_grabbing_another_releases_the_first()
        {
            var level = BuildLevel14();
            var bat = level.World.Terminals.First(t => t.Id == "bat+");
            level.GrabWireEnd(U1, "w1", WireEnd.A);
            level.MoveCursor(U1, bat.X, bat.Y);
            Assert.That(level.GrabWireEnd(U1, "w2", WireEnd.B), Is.True);

            Assert.That(level.FindWire("w1").ATerminalId, Is.EqualTo("bat+"), "the first end snapped on release");
            Assert.That(level.TryGetHeldWireEnd(U1, out var wire, out var end), Is.True);
            Assert.That((wire, end), Is.EqualTo(("w2", WireEnd.B)));
        }

        [Test]
        public void A_second_cursor_takes_a_held_end_over()
        {
            var level = BuildLevel14();
            level.GrabWireEnd(U1, "w1", WireEnd.A);
            level.GrabWireEnd(U2, "w1", WireEnd.A);

            Assert.That(level.TryGetHeldWireEnd(U1, out _, out _), Is.False);
            Assert.That(level.MoveCursor(U1, 100f, 100f), Is.False);
            Assert.That(level.MoveCursor(U2, 100f, 100f), Is.True);
            Assert.That((level.FindWire("w1").Ax, level.FindWire("w1").Ay), Is.EqualTo((100f, 100f)));
        }

        [Test]
        public void Unknown_wires_and_empty_hands_are_refused()
        {
            var level = BuildLevel14();
            Assert.That(level.GrabWireEnd(U1, "nope", WireEnd.A), Is.False);
            Assert.That(level.GrabWireEnd(U1, null, WireEnd.A), Is.False);
            Assert.That(level.ReleaseWireEnd(U1), Is.False);
            Assert.That(level.MoveCursor(U1, 1f, 1f), Is.False);
        }

        [Test]
        public void No_circuit_means_never_lit()
        {
            Assert.That(CircuitRules.IsBulbLit(LevelSeeds.Level(1).Components, LevelSeeds.Level(1).Terminals, LevelSeeds.Level(1).Wires), Is.False);
            var level = new LevelProgress(LevelSeeds.Level(1).Clone());
            Assert.That(level.HasCircuit, Is.False);
            level.Evaluate(LevelProgress.AuthoredPoses(level.World));
            Assert.That(level.BulbLit, Is.False);
        }
    }
}
