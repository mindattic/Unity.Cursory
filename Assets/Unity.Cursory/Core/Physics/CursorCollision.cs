using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// Optional cursor collision (UC-T8, ported from Cursory's <c>ResolveOutOfWalls</c> and <c>ResolveOutOfShapes</c>):
    /// the cursor is a disc of <see cref="Tuning.CursorRadius"/>, and a point that lands inside a wall or a compound
    /// shape is pushed out along the shallowest axis to the nearest edge. Nothing is swept: the cursor can still cross
    /// a solid between inputs, it just cannot rest inside one. Blocks are not solid to the cursor, as in Cursory.
    /// World pixels throughout.
    /// </summary>
    public static class CursorCollision
    {
        /// <summary>
        /// Pushes (<paramref name="x"/>, <paramref name="y"/>) out of the box centred on (<paramref name="cx"/>,
        /// <paramref name="cy"/>) with half extents <paramref name="halfW"/> × <paramref name="halfH"/>, inflated by
        /// <paramref name="radius"/>, to the nearest edge. False (and the point unchanged) when the disc is already clear.
        /// </summary>
        public static bool OutOfBox(ref float x, ref float y, float cx, float cy, float halfW, float halfH, float radius = Tuning.CursorRadius)
        {
            var hw = halfW + radius;
            var hh = halfH + radius;
            var dx = x - cx;
            var dy = y - cy;
            if (Math.Abs(dx) >= hw || Math.Abs(dy) >= hh) return false;
            if (hw - Math.Abs(dx) < hh - Math.Abs(dy)) x = cx + (dx >= 0f ? hw : -hw);
            else y = cy + (dy >= 0f ? hh : -hh);
            return true;
        }

        /// <summary>Pushes the point out of every wall in turn (walls are axis-aligned and never move).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="walls"/> is null.</exception>
        public static (float X, float Y) OutOfWalls(IEnumerable<Wall> walls, float x, float y, float radius = Tuning.CursorRadius)
        {
            if (walls == null) throw new ArgumentNullException(nameof(walls));
            foreach (var w in walls)
            {
                if (w == null) continue;
                OutOfBox(ref x, ref y, w.X, w.Y, w.W * 0.5f, w.H * 0.5f, radius);
            }

            return (x, y);
        }

        /// <summary>
        /// Pushes the point out of the first piece it overlaps of a compound shape whose origin is at
        /// (<paramref name="bodyX"/>, <paramref name="bodyY"/>) turned by <paramref name="angle"/> radians: the point
        /// goes into the shape's frame, out of the inflated piece there, and back.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="pieces"/> is null.</exception>
        public static (float X, float Y) OutOfShape(IReadOnlyList<ShapePiece> pieces, float bodyX, float bodyY, float angle,
            float x, float y, float radius = Tuning.CursorRadius)
        {
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));
            var (lx, ly) = GrabGeometry.ToLocal(bodyX, bodyY, angle, x, y);
            foreach (var p in pieces)
            {
                if (p == null) continue;
                if (OutOfBox(ref lx, ref ly, p.LocalX, p.LocalY, p.HalfW, p.HalfH, radius))
                {
                    return GrabGeometry.ToWorld(bodyX, bodyY, angle, lx, ly);
                }
            }

            return (x, y);
        }
    }
}
