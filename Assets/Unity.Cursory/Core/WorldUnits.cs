using System;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// The one boundary between Cursory's world pixels and Unity's metres (BIBLE UC-LAW-3).
    /// Gameplay data, levels and the wire speak world pixels; Physics2D and transforms run in
    /// metres, one Unity unit per metre, at <see cref="PixelsPerMeter"/> pixels per metre.
    /// </summary>
    public static class WorldUnits
    {
        /// <summary>World pixels per metre (Unity unit). Kept from Cursory so every ported constant still holds.</summary>
        public const float PixelsPerMeter = 100f;

        /// <summary>The shared world is a square this many world pixels on a side, as in Cursory.</summary>
        public const float WorldSizePx = 10000f;

        /// <summary>World pixels to metres.</summary>
        public static float ToMeters(float px) => px / PixelsPerMeter;

        /// <summary>Metres to world pixels.</summary>
        public static float ToPixels(float meters) => meters * PixelsPerMeter;

        /// <summary>
        /// The orthographic camera size (half the visible height, in metres) that shows one world
        /// pixel per screen pixel at <paramref name="zoom"/> 1; zoom 2 shows half as much world.
        /// </summary>
        public static float OrthographicSize(float screenHeightPx, float zoom)
        {
            if (!(screenHeightPx > 0f)) throw new ArgumentOutOfRangeException(nameof(screenHeightPx), screenHeightPx, "must be positive");
            if (!(zoom > 0f)) throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "must be positive");
            return ToMeters(screenHeightPx * 0.5f / zoom);
        }
    }
}
