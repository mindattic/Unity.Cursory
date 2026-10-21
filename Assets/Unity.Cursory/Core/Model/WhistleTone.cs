using System;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// Each player's whistle pitch (Cursory's <c>playWhistle</c> in room-core.js): one of twelve tones,
    /// 440 Hz plus 35 Hz a step, swept up to 1.5× in 80 ms and down to 0.9× by 350 ms under a short
    /// attack/decay envelope. Cursory keyed the tone off the player's colour; the port keys it off the
    /// player's seat (or client id), so two players never share a tone until there are more than twelve.
    /// </summary>
    public static class WhistleTone
    {
        /// <summary>How many distinct tones there are.</summary>
        public const int Count = 12;

        /// <summary>The lowest tone's base pitch, in Hz.</summary>
        public const float BaseHz = 440f;

        /// <summary>Hz between neighbouring tones.</summary>
        public const float StepHz = 35f;

        /// <summary>The whistle's whole length in seconds (oscillator stop time).</summary>
        public const float DurationSeconds = 0.45f;

        /// <summary>The tone for player <paramref name="key"/> (a seat index or client id): <c>key mod Count</c>.</summary>
        public static int For(ulong key) => (int)(key % Count);

        /// <summary>The base pitch of tone <paramref name="tone"/>, in Hz (wrapped into 0 to <see cref="Count"/> - 1).</summary>
        public static float Hz(int tone) => BaseHz + Wrap(tone) * StepHz;

        /// <summary>
        /// Renders tone <paramref name="tone"/> as mono samples in -1..1 at <paramref name="sampleRate"/>, for an
        /// audio clip: a sine whose pitch rises to 1.5× by 80 ms and falls to 0.9× by 350 ms, with a 20 ms attack
        /// and a decay to silence by 400 ms. It peaks at full scale; Cursory's 0.12 gain is left to the player's
        /// volume.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is below 1000.</exception>
        public static float[] Render(int tone, int sampleRate)
        {
            if (sampleRate < 1000) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "use an audio sample rate");
            var baseHz = Hz(tone);
            var samples = new float[(int)Math.Ceiling(DurationSeconds * sampleRate)];
            double phase = 0;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (double)i / sampleRate;
                phase += 2 * Math.PI * baseHz * PitchScale(t) / sampleRate;
                samples[i] = (float)(Math.Sin(phase) * Envelope(t));
            }
            return samples;
        }

        /// <summary>Pitch multiple at <paramref name="t"/> seconds: exponential 1 → 1.5 over 80 ms, then 1.5 → 0.9 by 350 ms.</summary>
        public static double PitchScale(double t)
        {
            if (t <= 0) return 1;
            if (t < 0.08) return Math.Pow(1.5, t / 0.08);
            if (t < 0.35) return 1.5 * Math.Pow(0.9 / 1.5, (t - 0.08) / 0.27);
            return 0.9;
        }

        /// <summary>Gain at <paramref name="t"/> seconds, peaking at 1: exponential rise over 20 ms, decay to near silence by 400 ms.</summary>
        public static double Envelope(double t)
        {
            const double floor = 0.0001 / 0.12;
            if (t <= 0) return floor;
            if (t < 0.02) return floor * Math.Pow(1 / floor, t / 0.02);
            if (t < 0.4) return Math.Pow(floor, (t - 0.02) / 0.38);
            return 0;
        }

        private static int Wrap(int tone) => ((tone % Count) + Count) % Count;
    }
}
