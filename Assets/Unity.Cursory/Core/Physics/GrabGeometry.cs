using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// Where a grab lands (BIBLE UC-LAW-5, ported from Cursory's <c>TryAttach</c>, <c>TryAttachShape</c> and
    /// <c>ProjectToEdge</c>): the click is turned into the body's own frame and projected onto the nearest point
    /// of the body's rim, so you grab the edge, not the interior, and an off-centre grab gives the body torque.
    /// Everything is in world pixels; a body-local point is measured from the body's origin (a block's centre,
    /// a shape's <see cref="Shape.X"/>/<see cref="Shape.Y"/>), unrotated.
    /// </summary>
    public static class GrabGeometry
    {
        /// <summary>
        /// The point on the rim of a box with half-extents <paramref name="halfW"/> × <paramref name="halfH"/>
        /// centred on the origin nearest the local point (<paramref name="localX"/>, <paramref name="localY"/>):
        /// clamp it into the box, then push it out to whichever edge is closest. Ties go right, left, top (+y),
        /// bottom (-y), as in Cursory.
        /// </summary>
        public static (float X, float Y) ProjectToEdge(float localX, float localY, float halfW, float halfH)
        {
            var lx = Clamp(localX, -halfW, halfW);
            var ly = Clamp(localY, -halfH, halfH);
            var toLeft = lx + halfW;
            var toRight = halfW - lx;
            var toBottom = ly + halfH;
            var toTop = halfH - ly;
            var min = Math.Min(Math.Min(toLeft, toRight), Math.Min(toBottom, toTop));
            if (min == toRight) lx = halfW;
            else if (min == toLeft) lx = -halfW;
            else if (min == toTop) ly = halfH;
            else ly = -halfH;
            return (lx, ly);
        }

        /// <summary>
        /// The body-local grab anchor on a <paramref name="w"/> × <paramref name="h"/> block whose centre is at
        /// (<paramref name="bodyX"/>, <paramref name="bodyY"/>) turned by <paramref name="angle"/> radians, for a
        /// click at (<paramref name="clickX"/>, <paramref name="clickY"/>).
        /// </summary>
        public static (float X, float Y) BlockAnchor(float w, float h, float bodyX, float bodyY, float angle, float clickX, float clickY)
        {
            var (lx, ly) = ToLocal(bodyX, bodyY, angle, clickX, clickY);
            return ProjectToEdge(lx, ly, w * 0.5f, h * 0.5f);
        }

        /// <summary>
        /// The body-local grab anchor on a compound shape made of <paramref name="pieces"/>, whose origin is at
        /// (<paramref name="bodyX"/>, <paramref name="bodyY"/>) turned by <paramref name="angle"/>: the rim of the
        /// piece nearest the click (by clamped distance). False when the shape has no pieces.
        /// </summary>
        public static bool TryShapeAnchor(IReadOnlyList<ShapePiece> pieces, float bodyX, float bodyY, float angle,
            float clickX, float clickY, out (float X, float Y) anchor)
        {
            anchor = default;
            if (pieces == null || pieces.Count == 0) return false;
            var (lx, ly) = ToLocal(bodyX, bodyY, angle, clickX, clickY);

            ShapePiece best = null;
            var bestD = float.PositiveInfinity;
            float bestCx = 0f, bestCy = 0f;
            foreach (var p in pieces)
            {
                if (p == null) continue;
                var cx = Clamp(lx, p.LocalX - p.HalfW, p.LocalX + p.HalfW);
                var cy = Clamp(ly, p.LocalY - p.HalfH, p.LocalY + p.HalfH);
                var d = (cx - lx) * (cx - lx) + (cy - ly) * (cy - ly);
                if (d < bestD) { bestD = d; best = p; bestCx = cx; bestCy = cy; }
            }
            if (best == null) return false;

            var (ex, ey) = ProjectToEdge(bestCx - best.LocalX, bestCy - best.LocalY, best.HalfW, best.HalfH);
            anchor = (best.LocalX + ex, best.LocalY + ey);
            return true;
        }

        /// <summary>A body-local point back in world pixels, for a body at (<paramref name="bodyX"/>, <paramref name="bodyY"/>) turned by <paramref name="angle"/>.</summary>
        public static (float X, float Y) ToWorld(float bodyX, float bodyY, float angle, float localX, float localY)
        {
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            return (bodyX + localX * cos - localY * sin, bodyY + localX * sin + localY * cos);
        }

        /// <summary>A world point in the frame of a body at (<paramref name="bodyX"/>, <paramref name="bodyY"/>) turned by <paramref name="angle"/>.</summary>
        public static (float X, float Y) ToLocal(float bodyX, float bodyY, float angle, float worldX, float worldY)
        {
            var dx = worldX - bodyX;
            var dy = worldY - bodyY;
            var cos = (float)Math.Cos(-angle);
            var sin = (float)Math.Sin(-angle);
            return (dx * cos - dy * sin, dx * sin + dy * cos);
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
