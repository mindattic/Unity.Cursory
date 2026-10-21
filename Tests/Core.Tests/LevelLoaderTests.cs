using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    public class LevelLoaderTests
    {
        [Test]
        public void Starts_on_level_one_with_a_live_copy()
        {
            var loader = new LevelLoader(LevelSeeds.All());
            Assert.That(loader.LevelCount, Is.EqualTo(14));
            Assert.That(loader.CurrentLevel, Is.EqualTo(1));
            Assert.That(loader.World.Number, Is.EqualTo(1));
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.False, "a client gets the geometry on join; nothing is pending yet");
            Assert.That(loader.ConsumeLevelAnnouncement(), Is.EqualTo(0));
        }

        [Test]
        public void Play_never_changes_the_level_definitions()
        {
            var levels = LevelSeeds.All();
            var loader = new LevelLoader(levels);
            loader.World.Blocks[0].X = 1;
            levels[0].Blocks[0].X = 2;
            Assert.That(loader.Definition(1).Blocks[0].X, Is.EqualTo(3500f));
            loader.Reset();
            Assert.That(loader.World.Blocks[0].X, Is.EqualTo(3500f), "reset puts the level back");
        }

        [Test]
        public void Load_switches_level_resends_geometry_and_announces_it()
        {
            var loader = new LevelLoader(LevelSeeds.All());
            loader.Load(9);
            Assert.That(loader.CurrentLevel, Is.EqualTo(9));
            Assert.That(loader.World.Shapes.Single().Id, Is.EqualTo("L9-bar"));
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.True);
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.False, "read once");
            Assert.That(loader.ConsumeLevelAnnouncement(), Is.EqualTo(9));
            Assert.That(loader.ConsumeLevelAnnouncement(), Is.EqualTo(0), "read once");
        }

        [Test]
        public void Reset_resends_geometry_without_the_banner()
        {
            var loader = new LevelLoader(LevelSeeds.All());
            loader.Reset();
            Assert.That(loader.ConsumeGeometryRebroadcast(), Is.True);
            Assert.That(loader.ConsumeLevelAnnouncement(), Is.EqualTo(0));
        }

        [TestCase(0, 1)]
        [TestCase(-3, 1)]
        [TestCase(99, 14)]
        public void Load_clamps_to_the_level_range(int asked, int loaded)
        {
            var loader = new LevelLoader(LevelSeeds.All());
            loader.Load(asked);
            Assert.That(loader.CurrentLevel, Is.EqualTo(loaded));
        }

        [Test]
        public void Refuses_missing_out_of_order_or_invalid_levels()
        {
            Assert.That(() => new LevelLoader(Array.Empty<LevelData>()), Throws.ArgumentException.With.Message.Contains("no levels"));

            var swapped = LevelSeeds.All();
            (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
            Assert.That(() => new LevelLoader(swapped), Throws.ArgumentException.With.Message.Contains("Level 1 is numbered 2"));

            var gap = LevelSeeds.All();
            gap[4] = null;
            Assert.That(() => new LevelLoader(gap), Throws.ArgumentException.With.Message.Contains("Level 5 is missing"));

            var heavy = LevelSeeds.All();
            heavy[1].Blocks[0].Mass = 3f;
            Assert.That(() => new LevelLoader(heavy), Throws.ArgumentException.With.Message.Contains("Level 2: Block 'L2-block' Mass 3"));
        }

        [Test]
        public void Definition_is_a_copy_and_checks_its_number()
        {
            var loader = new LevelLoader(LevelSeeds.All());
            loader.Definition(4).Blocks[0].Mass = 9;
            Assert.That(loader.Definition(4).Blocks[0].Mass, Is.EqualTo(2.2f));
            Assert.That(loader.Exists(14), Is.True);
            Assert.That(loader.Exists(15), Is.False);
            Assert.That(() => loader.Definition(15), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
