using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnityCursory.Core.Tests
{
    /// <summary>Cursory's 14 levels as ported in <see cref="LevelSeeds"/>.</summary>
    public class LevelSeedTests
    {
        private static IEnumerable<int> Numbers() => Enumerable.Range(1, LevelSeeds.Count);

        [Test]
        public void There_are_fourteen_levels_numbered_in_order()
        {
            Assert.That(LevelSeeds.Count, Is.EqualTo(14));
            Assert.That(LevelSeeds.All().Select(l => l.Number), Is.EqualTo(Numbers()));
            Assert.That(() => LevelSeeds.Level(0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => LevelSeeds.Level(15), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCaseSource(nameof(Numbers))]
        public void Level_is_valid(int number)
        {
            Assert.That(LevelSeeds.Level(number).Validate(), Is.Empty);
        }

        [TestCaseSource(nameof(Numbers))]
        public void Level_masses_stay_clear_of_the_one_and_two_cursor_thresholds(int number)
        {
            var level = LevelSeeds.Level(number);
            var masses = level.Blocks.Select(b => b.Mass).Concat(level.Shapes.Select(s => s.Mass));
            foreach (var mass in masses)
            {
                Assert.That(Tuning.IsClearOfThresholds(mass), Is.True, $"Mass {mass} sits too near 1.5 or 3.0");
                Assert.That(Tuning.CursorsNeeded(mass), Is.LessThanOrEqualTo(2), $"Mass {mass} needs more than the two players the levels are tuned for");
            }
        }

        [Test]
        public void Masses_are_Cursorys_own()
        {
            var expected = new Dictionary<string, float>
            {
                ["L1-block"] = 1f, ["L2-block"] = 2f, ["L3-shape"] = 2f, ["L4-block"] = 2.2f, ["L5-block"] = 2.6f,
                ["L6-a"] = 1.1f, ["L6-b"] = 1.1f, ["L7-block"] = 2f, ["L8-block"] = 2f, ["L9-bar"] = 2f,
                ["L10-block"] = 2.6f, ["L11-a"] = 1.3f, ["L11-b"] = 1.3f, ["L12-bar"] = 2f, ["L13-block"] = 2.2f,
            };
            var actual = LevelSeeds.All()
                .SelectMany(l => l.Blocks.Select(b => (b.Id, b.Mass)).Concat(l.Shapes.Select(s => (s.Id, s.Mass))))
                .ToDictionary(x => x.Id, x => x.Mass);
            Assert.That(actual, Is.EquivalentTo(expected));
        }

        [Test]
        public void One_cursor_solos_the_teaching_and_divide_levels_and_the_rest_need_two()
        {
            static int Needed(LevelData l) =>
                l.Blocks.Select(b => b.Mass).Concat(l.Shapes.Select(s => s.Mass)).Select(Tuning.CursorsNeeded).DefaultIfEmpty(0).Max();
            var solo = LevelSeeds.All().Where(l => Needed(l) == 1).Select(l => l.Number);
            var pair = LevelSeeds.All().Where(l => Needed(l) == 2).Select(l => l.Number);
            Assert.That(solo, Is.EqualTo(new[] { 1, 6, 11 }));
            Assert.That(pair, Is.EqualTo(new[] { 2, 3, 4, 5, 7, 8, 9, 10, 12, 13 }));
        }

        [TestCaseSource(nameof(Numbers))]
        public void Every_level_has_its_numbered_signpost(int number)
        {
            var level = LevelSeeds.Level(number);
            var label = level.Labels.Single();
            Assert.That(label.Id, Is.EqualTo($"L{number}-label"));
            Assert.That(label.Title, Is.EqualTo($"Level {number} — {level.Title}"));
            Assert.That(label.Subtitle, Is.Not.Empty);
        }

        [Test]
        public void The_circuit_level_has_enough_loose_wire_to_close_the_loop()
        {
            var level = LevelSeeds.Level(14);
            // Battery + to resistor to bulb to battery - is three wires, one per gap between components.
            Assert.That(level.Wires, Has.Count.EqualTo(level.Components.Count));
            Assert.That(level.Wires.Any(w => w.IsAPlugged || w.IsBPlugged), Is.False, "every wire starts loose");
            foreach (var w in level.Wires)
                foreach (var t in level.Terminals)
                {
                    Assert.That(Dist(w.Ax, w.Ay, t.X, t.Y), Is.GreaterThan(Tuning.WireSnapRadius), $"{w.Id} end A starts snapped to {t.Id}");
                    Assert.That(Dist(w.Bx, w.By, t.X, t.Y), Is.GreaterThan(Tuning.WireSnapRadius), $"{w.Id} end B starts snapped to {t.Id}");
                }
        }

        [Test]
        public void Each_call_returns_a_fresh_copy()
        {
            var a = LevelSeeds.Level(2);
            a.Blocks[0].Mass = 9;
            Assert.That(LevelSeeds.Level(2).Blocks[0].Mass, Is.EqualTo(2f));
        }

        private static float Dist(float ax, float ay, float bx, float by) =>
            (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }
}
