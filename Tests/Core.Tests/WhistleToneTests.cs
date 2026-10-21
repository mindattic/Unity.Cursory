using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    public class WhistleToneTests
    {
        [Test]
        public void Twelve_tones_440_hz_up_in_35_hz_steps_as_Cursory()
        {
            Assert.That(WhistleTone.Count, Is.EqualTo(12));
            Assert.That(WhistleTone.Hz(0), Is.EqualTo(440f));
            Assert.That(WhistleTone.Hz(11), Is.EqualTo(440f + 11 * 35f));
            Assert.That(WhistleTone.Hz(12), Is.EqualTo(440f), "wraps");
            Assert.That(WhistleTone.Hz(-1), Is.EqualTo(WhistleTone.Hz(11)));
        }

        [Test]
        public void Different_seats_get_different_tones_until_there_are_more_than_twelve()
        {
            var tones = Enumerable.Range(0, 12).Select(i => WhistleTone.For((ulong)i)).ToList();
            Assert.That(tones, Is.Unique);
            Assert.That(WhistleTone.For(12), Is.EqualTo(WhistleTone.For(0)));
            Assert.That(WhistleTone.For(ulong.MaxValue), Is.InRange(0, 11));
        }

        [Test]
        public void The_sweep_and_envelope_follow_Cursory()
        {
            Assert.That(WhistleTone.PitchScale(0), Is.EqualTo(1).Within(1e-9));
            Assert.That(WhistleTone.PitchScale(0.08), Is.EqualTo(1.5).Within(1e-9));
            Assert.That(WhistleTone.PitchScale(0.35), Is.EqualTo(0.9).Within(1e-9));
            Assert.That(WhistleTone.Envelope(0.02), Is.EqualTo(1).Within(1e-9));
            Assert.That(WhistleTone.Envelope(0.41), Is.EqualTo(0));
        }

        [Test]
        public void Render_makes_a_bounded_clip_of_the_whistle_length()
        {
            var samples = WhistleTone.Render(3, 44100);
            Assert.That(samples.Length, Is.EqualTo((int)System.Math.Ceiling(0.45 * 44100)));
            Assert.That(samples.Max(), Is.InRange(0.5f, 1f));
            Assert.That(samples.Min(), Is.InRange(-1f, -0.5f));
            Assert.That(samples.Skip((int)(0.41 * 44100)).All(s => s == 0f), "silent after the decay");
            Assert.That(() => WhistleTone.Render(0, 10), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_higher_tone_crosses_zero_more_often()
        {
            static int Crossings(float[] s) => Enumerable.Range(1, s.Length - 1).Count(i => (s[i - 1] < 0) != (s[i] < 0));
            Assert.That(Crossings(WhistleTone.Render(11, 44100)), Is.GreaterThan(Crossings(WhistleTone.Render(0, 44100))));
        }
    }
}
