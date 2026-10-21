using System;

namespace Unity.Cursory.Core
{
    /// <summary>A rectangle on the minimap: its bottom-left corner and size, in minimap pixels.</summary>
    public readonly struct MiniRect : IEquatable<MiniRect>
    {
        public MiniRect(float x, float y, float w, float h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public float X { get; }
        public float Y { get; }
        public float W { get; }
        public float H { get; }

        /// <summary>True when nothing of it lies on the map.</summary>
        public bool IsEmpty => !(W > 0f) || !(H > 0f);

        public bool Equals(MiniRect other) => X == other.X && Y == other.Y && W == other.W && H == other.H;

        public override bool Equals(object obj) => obj is MiniRect other && Equals(other);

        public override int GetHashCode() => ((X.GetHashCode() * 31 + Y.GetHashCode()) * 31 + W.GetHashCode()) * 31 + H.GetHashCode();

        public override string ToString() => FormattableString.Invariant($"({X:0.##}, {Y:0.##}) {W:0.##}x{H:0.##}");
    }

    /// <summary>
    /// World pixels to minimap pixels (Cursory's <c>drawMinimap</c>): the whole world scaled onto a
    /// <see cref="Width"/> × <see cref="Height"/> map whose origin is its bottom-left corner, y up like the world.
    /// Small geometry keeps a <see cref="MinFeaturePx"/> floor so it never vanishes, and the camera's view is drawn
    /// as a rectangle clipped to the map.
    /// </summary>
    public sealed class MinimapTransform
    {
        /// <summary>Cursory's minimap edge, in screen pixels.</summary>
        public const float DefaultSizePx = 160f;

        /// <summary>The smallest a wall, goal or body is drawn, in minimap pixels.</summary>
        public const float MinFeaturePx = 2f;

        /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
        public MinimapTransform(float width = DefaultSizePx, float height = DefaultSizePx, float worldWidth = WorldUnits.WorldSizePx, float worldHeight = WorldUnits.WorldSizePx)
        {
            if (!(width > 0f)) throw new ArgumentOutOfRangeException(nameof(width), width, "must be positive");
            if (!(height > 0f)) throw new ArgumentOutOfRangeException(nameof(height), height, "must be positive");
            if (!(worldWidth > 0f)) throw new ArgumentOutOfRangeException(nameof(worldWidth), worldWidth, "must be positive");
            if (!(worldHeight > 0f)) throw new ArgumentOutOfRangeException(nameof(worldHeight), worldHeight, "must be positive");
            Width = width;
            Height = height;
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
        }

        public float Width { get; }
        public float Height { get; }
        public float WorldWidth { get; }
        public float WorldHeight { get; }

        /// <summary>Minimap pixels per world pixel, across.</summary>
        public float ScaleX => Width / WorldWidth;

        /// <summary>Minimap pixels per world pixel, up.</summary>
        public float ScaleY => Height / WorldHeight;

        /// <summary>The minimap point over world point (<paramref name="wx"/>, <paramref name="wy"/>).</summary>
        public (float X, float Y) ToMap(float wx, float wy) => (wx * ScaleX, wy * ScaleY);

        /// <summary>The world point under minimap point (<paramref name="mx"/>, <paramref name="my"/>) (a click on the map).</summary>
        public (float X, float Y) ToWorld(float mx, float my) => (mx / ScaleX, my / ScaleY);

        /// <summary>A world rectangle given by its centre and size, on the map, at least <see cref="MinFeaturePx"/> each way (not clipped).</summary>
        public MiniRect Feature(float cx, float cy, float w, float h)
        {
            var (x, y) = ToMap(cx - w / 2f, cy - h / 2f);
            return new MiniRect(x, y, Math.Max(MinFeaturePx, w * ScaleX), Math.Max(MinFeaturePx, h * ScaleY));
        }

        /// <summary>What the camera sees, as a rectangle on the map, clipped to the map's bounds.</summary>
        public MiniRect Viewport(CameraView camera, float screenW, float screenH)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var v = camera.Visible(screenW, screenH);
            var (x0, y0) = ToMap(v.X, v.Y);
            var (x1, y1) = ToMap(v.X + v.W, v.Y + v.H);
            return Clip(x0, y0, x1, y1);
        }

        /// <summary>The part of the box (<paramref name="x0"/>, <paramref name="y0"/>)-(<paramref name="x1"/>, <paramref name="y1"/>) on the map.</summary>
        public MiniRect Clip(float x0, float y0, float x1, float y1)
        {
            float l = Math.Max(0f, Math.Min(x0, x1)), r = Math.Min(Width, Math.Max(x0, x1));
            float b = Math.Max(0f, Math.Min(y0, y1)), t = Math.Min(Height, Math.Max(y0, y1));
            return new MiniRect(l, b, Math.Max(0f, r - l), Math.Max(0f, t - b));
        }
    }
}
