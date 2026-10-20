using System;
using NUnit.Framework;

namespace UnityCursory.Core.Tests
{
    public class WorldUnitsTests
    {
        [Test]
        public void One_metre_is_one_hundred_world_pixels_as_in_Cursory()
        {
            Assert.That(WorldUnits.ToMeters(100f), Is.EqualTo(1f));
            Assert.That(WorldUnits.ToPixels(1f), Is.EqualTo(100f));
        }

        [TestCase(0f)]
        [TestCase(37.5f)]
        [TestCase(-1234f)]
        [TestCase(10000f)]
        public void Pixels_survive_the_round_trip_through_metres(float px)
        {
            Assert.That(WorldUnits.ToPixels(WorldUnits.ToMeters(px)), Is.EqualTo(px).Within(1e-3f));
        }

        [Test]
        public void The_world_is_one_hundred_metres_square()
        {
            Assert.That(WorldUnits.ToMeters(WorldUnits.WorldSizePx), Is.EqualTo(100f));
        }

        [Test]
        public void Zoom_one_shows_one_world_pixel_per_screen_pixel()
        {
            // 1080 screen pixels tall = 1080 world pixels = 10.8 m, so half-height 5.4 m.
            Assert.That(WorldUnits.OrthographicSize(1080f, 1f), Is.EqualTo(5.4f).Within(1e-5f));
        }

        [Test]
        public void Zooming_in_shows_less_world()
        {
            Assert.That(WorldUnits.OrthographicSize(1080f, 2f), Is.EqualTo(2.7f).Within(1e-5f));
            Assert.That(WorldUnits.OrthographicSize(1080f, 0.5f), Is.EqualTo(10.8f).Within(1e-5f));
        }

        [TestCase(0f, 1f)]
        [TestCase(1080f, 0f)]
        [TestCase(1080f, -1f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(1080f, float.NaN)]
        public void A_non_positive_screen_or_zoom_is_refused(float screenHeightPx, float zoom)
        {
            Assert.That(() => WorldUnits.OrthographicSize(screenHeightPx, zoom), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    public class SimulationRateTests
    {
        [Test]
        public void The_server_ticks_thirty_times_a_second()
        {
            Assert.That(SimulationRate.TicksPerSecond, Is.EqualTo(30));
            Assert.That(SimulationRate.TickSeconds * SimulationRate.TicksPerSecond, Is.EqualTo(1f).Within(1e-6f));
        }
    }
}
