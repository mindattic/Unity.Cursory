using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// An axis-aligned box in a body's own frame (world pixels, unrotated, measured from the body's origin): a block
    /// is one, centred on the origin; a compound shape is one per <see cref="ShapePiece"/>. The tether wraps these.
    /// </summary>
    public readonly struct BodyRect
    {
        public BodyRect(float cx, float cy, float halfW, float halfH)
        {
            Cx = cx;
            Cy = cy;
            HalfW = halfW;
            HalfH = halfH;
        }

        public float Cx { get; }
        public float Cy { get; }
        public float HalfW { get; }
        public float HalfH { get; }
    }

    /// <summary>
    /// The rope between a cursor and what it holds (UC-T8, ported from Cursory's <c>RoomState</c>
    /// <c>LeashAndReport</c>, <c>PullMassFor</c>, <c>SegmentRectEntry</c> and the corner lists): the leash that keeps
    /// a grabbing cursor within <see cref="Tuning.MaxPullPx"/> of its contact, the pull readout that saturates at the
    /// same length, and a body's corners and boxes in its own frame for <see cref="TetherWrap"/>. World pixels throughout.
    /// </summary>
    public static class TetherGeometry
    {
        private const float Tiny = 1e-6f;

        /// <summary>
        /// Holds (<paramref name="x"/>, <paramref name="y"/>) at or inside <paramref name="maxLength"/> of the contact
        /// (<paramref name="contactX"/>, <paramref name="contactY"/>): a point past it slides back along the rope to
        /// the leash end, which is exactly where a pull saturates. A point inside comes back unchanged.
        /// </summary>
        public static (float X, float Y) Leash(float contactX, float contactY, float x, float y, float maxLength = Tuning.MaxPullPx)
        {
            var dx = x - contactX;
            var dy = y - contactY;
            var len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len <= maxLength || len < Tiny) return (x, y);
            return (contactX + dx / len * maxLength, contactY + dy / len * maxLength);
        }

        /// <summary>
        /// A grab's reported pull in mass units: the rope's length over <see cref="Tuning.MaxPullPx"/>, saturated, times
        /// one grab's ceiling (<see cref="Tuning.SingleGrabMaxMass"/>), so it reads straight against a body's Mass.
        /// </summary>
        public static float PullMass(float contactX, float contactY, float x, float y)
        {
            var dx = x - contactX;
            var dy = y - contactY;
            var frac = (float)Math.Sqrt(dx * dx + dy * dy) / Tuning.MaxPullPx;
            if (frac > 1f) frac = 1f;
            return Tuning.SingleGrabMaxMass * frac;
        }

        /// <summary>
        /// Slab clip of the segment (<paramref name="ax"/>, <paramref name="ay"/>) to (<paramref name="bx"/>,
        /// <paramref name="by"/>) against the box [-hw, hw] × [-hh, hh]. True when the segment is inside the box for
        /// more than a sliver of its length; <paramref name="tEnter"/> is where it enters, in [0, 1] (0 when it starts
        /// inside). A box with a non-positive half extent is empty.
        /// </summary>
        public static bool SegmentRectEntry(float ax, float ay, float bx, float by, float hw, float hh, out float tEnter)
        {
            tEnter = 0f;
            if (!(hw > 0f) || !(hh > 0f)) return false;
            float dx = bx - ax, dy = by - ay, tmin = 0f, tmax = 1f;
            if (Math.Abs(dx) < Tiny)
            {
                if (ax < -hw || ax > hw) return false;
            }
            else
            {
                var t1 = (-hw - ax) / dx;
                var t2 = (hw - ax) / dx;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = Math.Max(tmin, t1);
                tmax = Math.Min(tmax, t2);
                if (tmin > tmax) return false;
            }

            if (Math.Abs(dy) < Tiny)
            {
                if (ay < -hh || ay > hh) return false;
            }
            else
            {
                var t1 = (-hh - ay) / dy;
                var t2 = (hh - ay) / dy;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = Math.Max(tmin, t1);
                tmax = Math.Min(tmax, t2);
                if (tmin > tmax) return false;
            }

            tEnter = Math.Max(0f, tmin);
            return tmax > 1e-3f;
        }

        /// <summary>A <paramref name="w"/> × <paramref name="h"/> block's four corners in its own frame, in Cursory's order: (-,-), (+,-), (+,+), (-,+).</summary>
        public static IReadOnlyList<(float X, float Y)> BlockCorners(float w, float h)
        {
            var hw = w * 0.5f;
            var hh = h * 0.5f;
            return new List<(float X, float Y)> { (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh) };
        }

        /// <summary>A <paramref name="w"/> × <paramref name="h"/> block as the one box it is, centred on its origin.</summary>
        public static IReadOnlyList<BodyRect> BlockRects(float w, float h)
        {
            return new List<BodyRect> { new BodyRect(0f, 0f, w * 0.5f, h * 0.5f) };
        }

        /// <summary>Every piece's four corners in the shape's own frame, piece by piece, each in <see cref="BlockCorners"/>'s order.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="pieces"/> is null.</exception>
        public static IReadOnlyList<(float X, float Y)> ShapeCorners(IReadOnlyList<ShapePiece> pieces)
        {
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));
            var corners = new List<(float X, float Y)>(pieces.Count * 4);
            foreach (var p in pieces)
            {
                if (p == null) continue;
                corners.Add((p.LocalX - p.HalfW, p.LocalY - p.HalfH));
                corners.Add((p.LocalX + p.HalfW, p.LocalY - p.HalfH));
                corners.Add((p.LocalX + p.HalfW, p.LocalY + p.HalfH));
                corners.Add((p.LocalX - p.HalfW, p.LocalY + p.HalfH));
            }

            return corners;
        }

        /// <summary>Every piece as a box in the shape's own frame.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="pieces"/> is null.</exception>
        public static IReadOnlyList<BodyRect> ShapeRects(IReadOnlyList<ShapePiece> pieces)
        {
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));
            var rects = new List<BodyRect>(pieces.Count);
            foreach (var p in pieces)
            {
                if (p == null) continue;
                rects.Add(new BodyRect(p.LocalX, p.LocalY, p.HalfW, p.HalfH));
            }

            return rects;
        }
    }
}
