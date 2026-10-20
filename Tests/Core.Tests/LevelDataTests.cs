using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnityCursory.Core.Tests
{
    /// <summary>Cursory's own levels, rewritten as <see cref="LevelData"/>, to show the schema holds them.</summary>
    internal static class CursoryLevels
    {
        // RoomState.SeedLevel1
        public static LevelData Level1() => new LevelData
        {
            Number = 1,
            Title = "Drop it on the pad",
            Blocks = { new Block { Id = "L1-block", X = 3500, Y = 5000, W = 160, H = 160, Mass = 1, Color = "#D85A30" } },
            BlockGoals = { new BlockGoal { Id = "L1-goal", X = 6500, Y = 5000, W = 700, H = 700, TargetBlockId = "L1-block" } },
            Labels = { new WorldLabel { Id = "L1-label", X = 5000, Y = 3500, Title = "Level 1 — Drop it on the pad" } },
        };

        // RoomState.SeedLevel3
        public static LevelData Level3() => new LevelData
        {
            Number = 3,
            Title = "Pivot the couch",
            Shapes =
            {
                new Shape
                {
                    Id = "L3-shape", X = 2800, Y = 5000, Mass = 2, Color = "#7FBF5A",
                    Pieces =
                    {
                        new ShapePiece { LocalX = 0, LocalY = 0, HalfW = 220, HalfH = 55 },
                        new ShapePiece { LocalX = -165, LocalY = -165, HalfW = 55, HalfH = 165 },
                    },
                },
            },
            Walls = { new Wall { Id = "L3-wall", X = 5000, Y = 5000, W = 180, H = 700 } },
            ShapeGoals = { new ShapeGoal { Id = "L3-goal", X = 7200, Y = 5000, W = 1000, H = 1000, TargetShapeId = "L3-shape" } },
            Labels = { new WorldLabel { Id = "L3-label", X = 5000, Y = 3300, Title = "Level 3 — Pivot the couch" } },
        };

        // RoomState.SeedLevel14
        public static LevelData Level14() => new LevelData
        {
            Number = 14,
            Title = "Light the bulb",
            Terminals =
            {
                new Terminal { Id = "bat+", X = 3000, Y = 4650, Polarity = TerminalPolarity.Positive },
                new Terminal { Id = "bat-", X = 3000, Y = 5350, Polarity = TerminalPolarity.Negative },
                new Terminal { Id = "r-a", X = 4700, Y = 3900 },
                new Terminal { Id = "r-b", X = 5300, Y = 3900 },
                new Terminal { Id = "b-a", X = 6500, Y = 4400 },
                new Terminal { Id = "b-b", X = 7100, Y = 4400 },
            },
            Components =
            {
                new CircuitComponent { Kind = ComponentKind.Battery, Id = "battery", X = 3000, Y = 5000, W = 360, H = 900, Label = "Battery", TerminalIds = { "bat+", "bat-" } },
                new CircuitComponent { Kind = ComponentKind.Resistor, Id = "resistor", X = 5000, Y = 3900, W = 700, H = 200, Label = "Resistor", TerminalIds = { "r-a", "r-b" } },
                new CircuitComponent { Kind = ComponentKind.Bulb, Id = "bulb", X = 6800, Y = 4400, W = 360, H = 360, Label = "Bulb", TerminalIds = { "b-a", "b-b" } },
            },
            Wires =
            {
                new Wire { Id = "w1", Color = "#d98a3d", Ax = 3400, Ay = 6100, Bx = 3900, By = 6100 },
                new Wire { Id = "w2", Color = "#caa472", Ax = 4600, Ay = 6300, Bx = 5100, By = 6300 },
                new Wire { Id = "w3", Color = "#8fb3d9", Ax = 5800, Ay = 6100, Bx = 6300, By = 6100 },
            },
            Labels = { new WorldLabel { Id = "L14-label", X = 5000, Y = 2900, Title = "Level 14 — Light the bulb" } },
        };
    }

    public class LevelDataTests
    {
        private static IEnumerable<TestCaseData> CursoryLevelCases()
        {
            yield return new TestCaseData(CursoryLevels.Level1()).SetName("Level 1 (block and goal) is valid");
            yield return new TestCaseData(CursoryLevels.Level3()).SetName("Level 3 (compound shape, wall, shape goal) is valid");
            yield return new TestCaseData(CursoryLevels.Level14()).SetName("Level 14 (circuit) is valid");
        }

        [TestCaseSource(nameof(CursoryLevelCases))]
        public void Cursory_levels_fit_the_schema(LevelData level)
        {
            Assert.That(level.Validate(), Is.Empty);
        }

        [Test]
        public void An_empty_level_cannot_be_won()
        {
            Assert.That(new LevelData().Validate(), Has.Some.Contains("can never be won"));
        }

        [Test]
        public void Ids_must_be_present_and_unique_across_the_level()
        {
            var level = CursoryLevels.Level3();
            level.Walls.Add(new Wall { Id = "L3-shape", X = 100, Y = 100, W = 10, H = 10 });
            level.Labels.Add(new WorldLabel { Id = "", X = 100, Y = 100 });
            var errors = level.Validate();
            Assert.That(errors, Has.Some.Contains("'L3-shape' is used more than once"));
            Assert.That(errors, Has.Some.Contains("A label has no id"));
        }

        [Test]
        public void A_level_mass_on_a_threshold_is_refused()
        {
            var level = CursoryLevels.Level1();
            level.Blocks[0].Mass = 1.5f;
            Assert.That(level.Validate(), Has.Some.Contains("cursor-count threshold"));
            level.Blocks[0].Mass = 0f;
            Assert.That(level.Validate(), Has.Some.Contains("positive Mass"));
        }

        [Test]
        public void Goals_must_target_a_body_of_their_kind()
        {
            var level = CursoryLevels.Level3();
            level.ShapeGoals[0].TargetShapeId = "nope";
            level.Blocks.Add(new Block { Id = "b", X = 100, Y = 100, W = 10, H = 10, Mass = 1 });
            level.BlockGoals.Add(new BlockGoal { Id = "bg", X = 100, Y = 100, W = 50, H = 50, TargetBlockId = "L3-shape" });
            var errors = level.Validate();
            Assert.That(errors, Has.Some.Contains("Shape goal 'L3-goal' targets 'nope'"));
            Assert.That(errors, Has.Some.Contains("Block goal 'bg' targets 'L3-shape', which is not a block"));
        }

        [Test]
        public void Sizes_positions_and_colours_are_checked()
        {
            var level = CursoryLevels.Level1();
            level.Blocks[0].W = 0;
            level.Blocks[0].X = 10001;
            level.Blocks[0].Color = "red";
            level.Walls.Add(new Wall { Id = "w", X = -1, Y = 5, W = 10, H = -10 });
            var errors = level.Validate();
            Assert.That(errors, Has.Some.Contains("Block 'L1-block' must have a positive size"));
            Assert.That(errors, Has.Some.Contains("Block 'L1-block' at (10001, 5000) is outside"));
            Assert.That(errors, Has.Some.Contains("colour 'red' is not #RRGGBB"));
            Assert.That(errors, Has.Some.Contains("Wall 'w' must have a positive size"));
            Assert.That(errors, Has.Some.Contains("Wall 'w' at (-1, 5) is outside"));
        }

        [Test]
        public void A_shape_needs_pieces_with_size()
        {
            var level = CursoryLevels.Level3();
            level.Shapes[0].Pieces[1].HalfH = 0;
            Assert.That(level.Validate(), Has.Some.Contains("Shape 'L3-shape' piece 1 must have a positive size"));
            level.Shapes[0].Pieces.Clear();
            Assert.That(level.Validate(), Has.Some.Contains("Shape 'L3-shape' has no pieces"));
        }

        [Test]
        public void A_circuit_needs_one_battery_one_resistor_and_one_bulb()
        {
            var level = CursoryLevels.Level14();
            level.Components.RemoveAll(c => c.Kind == ComponentKind.Resistor);
            level.Terminals.RemoveAll(t => t.Id.StartsWith("r-"));
            Assert.That(level.Validate(), Has.Some.Contains("exactly one Resistor, not 0"));
        }

        [Test]
        public void A_battery_needs_a_positive_and_a_negative_post_and_nothing_else_has_polarity()
        {
            var level = CursoryLevels.Level14();
            level.Terminals.Single(t => t.Id == "bat-").Polarity = TerminalPolarity.Positive;
            level.Terminals.Single(t => t.Id == "b-a").Polarity = TerminalPolarity.Negative;
            var errors = level.Validate();
            Assert.That(errors, Has.Some.Contains("Battery 'battery' needs one positive and one negative terminal"));
            Assert.That(errors, Has.Some.Contains("Bulb 'bulb' terminals must have no polarity"));
        }

        [Test]
        public void Terminals_resolve_and_belong_to_exactly_one_component()
        {
            var level = CursoryLevels.Level14();
            level.Components.Single(c => c.Kind == ComponentKind.Bulb).TerminalIds[1] = "r-b";
            level.Wires[0].ATerminalId = "ghost";
            level.Components.Single(c => c.Kind == ComponentKind.Resistor).TerminalIds.Add("bat+");
            var errors = level.Validate();
            Assert.That(errors, Has.Some.Contains("Terminal 'r-b' belongs to both"));
            Assert.That(errors, Has.Some.Contains("Terminal 'bat+' belongs to both 'battery' and 'resistor'"));
            Assert.That(errors, Has.Some.Contains("Resistor 'resistor' needs exactly two terminals, not 3"));
            Assert.That(errors, Has.Some.Contains("Terminal 'b-b' does not belong to any component"));
            Assert.That(errors, Has.Some.Contains("Wire 'w1' end A is plugged into 'ghost'"));
        }

        [Test]
        public void Geometry_is_the_walls_and_labels_only()
        {
            var level = CursoryLevels.Level3();
            var geometry = level.Geometry();
            Assert.That(geometry.LevelNumber, Is.EqualTo(3));
            Assert.That(geometry.Walls.Select(w => w.Id), Is.EqualTo(new[] { "L3-wall" }));
            Assert.That(geometry.Labels.Select(l => l.Id), Is.EqualTo(new[] { "L3-label" }));
            geometry.Walls[0].X = 1;
            Assert.That(level.Walls[0].X, Is.EqualTo(5000f), "geometry is a copy");
        }

        [Test]
        public void Clone_is_deep_so_play_never_changes_the_level_asset()
        {
            var level = CursoryLevels.Level14();
            level.Shapes.Add(CursoryLevels.Level3().Shapes[0]);
            var copy = level.Clone();

            copy.Wires[0].ATerminalId = "bat+";
            copy.Components[0].TerminalIds[0] = "x";
            copy.Shapes[0].Pieces[0].LocalX = 99;
            copy.Labels.Clear();

            Assert.That(level.Wires[0].IsAPlugged, Is.False);
            Assert.That(level.Components[0].TerminalIds[0], Is.EqualTo("bat+"));
            Assert.That(level.Shapes[0].Pieces[0].LocalX, Is.EqualTo(0f));
            Assert.That(level.Labels, Has.Count.EqualTo(1));
            Assert.That(copy.Number, Is.EqualTo(14));
            Assert.That(copy.Title, Is.EqualTo("Light the bulb"));
        }

        [Test]
        public void The_dead_Cursory_types_are_not_ported()
        {
            var types = typeof(LevelData).Assembly.GetTypes().Select(t => t.Name).ToArray();
            Assert.That(types, Has.None.EqualTo("SwitchTile"));
            Assert.That(types, Has.None.EqualTo("Door"));
            Assert.That(types, Has.None.EqualTo("ShapeAttachment"));
        }
    }
}
