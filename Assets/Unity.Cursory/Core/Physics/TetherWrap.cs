using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// One rope's wrap round the body it holds (UC-T8, always on since UC-T33; ported from Cursory's
    /// <c>RoomState.UpdateTether</c>): the rope runs from the grab's rim anchor round the body's corners it has caught,
    /// then on to the cursor. Each update first unwinds (drops the last caught corner once the rope no longer bends round
    /// it the way it did when it caught, i.e. it has swung back past straight) and then wraps (while the last segment cuts
    /// clearly into the body, catches the corner the rope slides to: of those it reaches without cutting the body, the
    /// one most nearly toward the cursor).
    /// The last caught corner, or the anchor when none is, is the <see cref="Contact"/>: where the rope's physical joint
    /// pulls, so pulling round a corner turns the body until the rope is straight again.
    /// </summary>
    /// <remarks>
    /// Everything is in the body's own frame (world pixels, unrotated, from the body's origin), so a caught corner
    /// rides the body as it turns; the caller turns the cursor into that frame and the result back out
    /// (<see cref="GrabGeometry.ToLocal"/>, <see cref="GrabGeometry.ToWorld"/>). The wrap test narrows the body by
    /// <see cref="CatchInset"/>, as Cursory does, so the slight turn of a normal off-centre pull never catches a corner,
    /// and a corner that has just let go (the rope straight through it, on the rim) is not caught again at once.
    /// <para>One deliberate change from Cursory: its unwind test asked whether the rope still cut the body widened by
    /// 6 px, but the rope always starts on the rim, inside that margin, so the test always passed and a caught corner
    /// never let go. Unwinding on the bend flipping is the rope straightening, which is what the margin was after.</para>
    /// </remarks>
    public sealed class TetherWrap
    {
        /// <summary>The most corners one rope catches, so its whole chain (anchor plus corners) fits a fixed-size sync.</summary>
        public const int MaxCorners = 8;

        /// <summary>How far into the body (px) the rope must cut before it catches a corner.</summary>
        public const float CatchInset = 12f;

        private const int MaxCatchesPerUpdate = 8;

        /// <summary>Two corner points closer than this (px) are the same point.</summary>
        private const float SameCorner = 0.5f;

        private readonly List<int> _caught = new List<int>();

        /// <summary>Per caught corner, which way the rope turned there when it caught (+1 or -1).</summary>
        private readonly List<int> _bends = new List<int>();

        /// <summary>The caught corners, anchor-first, as indices into the corner list the updates are given. Empty = a straight rope.</summary>
        public IReadOnlyList<int> Corners => _caught;

        /// <summary>The index of the corner the rope pulls on, or -1 when it pulls on the anchor itself.</summary>
        public int ContactCorner => _caught.Count > 0 ? _caught[_caught.Count - 1] : -1;

        /// <summary>Lets go of every caught corner: a straight rope again.</summary>
        public void Clear()
        {
            _caught.Clear();
            _bends.Clear();
        }

        /// <summary>
        /// Unwinds then wraps the rope for a cursor at (<paramref name="cursorX"/>, <paramref name="cursorY"/>), all in
        /// the body's own frame: the rope starts at the anchor (<paramref name="anchorX"/>, <paramref name="anchorY"/>)
        /// and may catch any of <paramref name="corners"/>; the body is the union of <paramref name="rects"/>.
        /// A cursor over the body itself (it passes over bodies, as in Cursory) catches nothing new: the rope rises from the
        /// rim to it. Nor does a rope catch a corner that would take its chain past <paramref name="maxChainLength"/>: a
        /// real rope can't reach round a corner further away than its length.
        /// </summary>
        /// <returns>True when the <see cref="ContactCorner"/> changed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="corners"/> or <paramref name="rects"/> is null.</exception>
        public bool Update(float anchorX, float anchorY, IReadOnlyList<(float X, float Y)> corners, IReadOnlyList<BodyRect> rects,
            float cursorX, float cursorY, float maxChainLength = float.PositiveInfinity)
        {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (rects == null) throw new ArgumentNullException(nameof(rects));
            var before = ContactCorner;
            foreach (var index in _caught)
            {
                if (index < 0 || index >= corners.Count)
                {
                    Clear();   // the corner list changed under us: start straight
                    break;
                }
            }

            while (_caught.Count > 0)
            {
                (float X, float Y) prev = _caught.Count >= 2 ? corners[_caught[_caught.Count - 2]] : (anchorX, anchorY);
                (float X, float Y) corner = corners[_caught[_caught.Count - 1]];
                if (Bend(prev.X, prev.Y, corner.X, corner.Y, cursorX, cursorY) * _bends[_bends.Count - 1] > 0f) break;
                _caught.RemoveAt(_caught.Count - 1);
                _bends.RemoveAt(_bends.Count - 1);
            }

            if (Inside(rects, cursorX, cursorY)) return ContactCorner != before;

            var chainLength = 0f;
            (float X, float Y) walked = (anchorX, anchorY);
            foreach (var index in _caught)
            {
                chainLength += Distance(walked, corners[index]);
                walked = corners[index];
            }

            for (var guard = 0; guard < MaxCatchesPerUpdate && _caught.Count < MaxCorners; guard++)
            {
                (float X, float Y) last = _caught.Count > 0 ? corners[_caught[_caught.Count - 1]] : (anchorX, anchorY);
                if (!Cuts(rects, last.X, last.Y, cursorX, cursorY, -CatchInset)) break;
                var best = Wrapped(corners, rects, _caught.Count > 0 ? _caught[_caught.Count - 1] : -1, last.X, last.Y, cursorX, cursorY);
                if (best < 0 || _caught.Contains(best)) break;   // no new corner to catch, or it would loop
                var reach = Distance(last, corners[best]);
                if (chainLength + reach > maxChainLength) break;   // further round than the rope is long
                chainLength += reach;
                var bend = Math.Sign(Bend(last.X, last.Y, corners[best].X, corners[best].Y, cursorX, cursorY));
                if (bend == 0) break;   // the rope runs straight through that corner: nothing to wrap
                _caught.Add(best);
                _bends.Add(bend);
            }

            return ContactCorner != before;
        }

        /// <summary>Where the rope pulls, in the body's own frame: the last caught corner, else the anchor.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="corners"/> is null while a corner is caught.</exception>
        public (float X, float Y) Contact(float anchorX, float anchorY, IReadOnlyList<(float X, float Y)> corners)
        {
            var index = ContactCorner;
            if (index < 0) return (anchorX, anchorY);
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            return index < corners.Count ? corners[index] : (anchorX, anchorY);
        }

        /// <summary>The rope's whole chain in the body's own frame: the anchor, then every caught corner. Cleared and refilled.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
        public void Chain(float anchorX, float anchorY, IReadOnlyList<(float X, float Y)> corners, List<(float X, float Y)> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            into.Add((anchorX, anchorY));
            if (corners == null) return;
            foreach (var index in _caught)
            {
                if (index >= 0 && index < corners.Count) into.Add(corners[index]);
            }
        }

        /// <summary>Which way the rope turns at (<paramref name="cx"/>, <paramref name="cy"/>) going from the previous point on to the cursor: the 2D cross product's sign.</summary>
        private static float Bend(float px, float py, float cx, float cy, float tx, float ty)
        {
            return (cx - px) * (ty - cy) - (cy - py) * (tx - cx);
        }

        private static float Distance((float X, float Y) a, (float X, float Y) b) =>
            (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) is over the body, clear of its edge by <see cref="CatchInset"/>.</summary>
        private static bool Inside(IReadOnlyList<BodyRect> rects, float x, float y)
        {
            foreach (var r in rects)
            {
                if (Math.Abs(x - r.Cx) < r.HalfW - CatchInset && Math.Abs(y - r.Cy) < r.HalfH - CatchInset) return true;
            }

            return false;
        }

        private static bool Cuts(IReadOnlyList<BodyRect> rects, float fromX, float fromY, float toX, float toY, float margin)
        {
            foreach (var r in rects)
            {
                if (TetherGeometry.SegmentRectEntry(fromX - r.Cx, fromY - r.Cy, toX - r.Cx, toY - r.Cy, r.HalfW + margin, r.HalfH + margin, out _))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The corner a rope from (<paramref name="fromX"/>, <paramref name="fromY"/>) toward the cursor bends round when it
        /// would cut the body: of the corners it can reach without cutting the body (along an edge or through open space),
        /// the one whose direction is closest to the cursor's, the nearer on a tie. The rope starts on the rim, so this is
        /// the corner it slides to as the cursor swings round. -1 when there is none.
        /// </summary>
        private static int Wrapped(IReadOnlyList<(float X, float Y)> corners, IReadOnlyList<BodyRect> rects, int skip,
            float fromX, float fromY, float cursorX, float cursorY)
        {
            var dx = cursorX - fromX;
            var dy = cursorY - fromY;
            var dLen = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dLen < 1e-4f) return -1;

            var best = -1;
            var bestCos = -2f;
            var bestLen = float.MaxValue;
            for (var i = 0; i < corners.Count; i++)
            {
                if (i == skip) continue;
                var kx = corners[i].X - fromX;
                var ky = corners[i].Y - fromY;
                var kLen = (float)Math.Sqrt(kx * kx + ky * ky);
                if (kLen < SameCorner) continue;   // the point the rope already sits on (an anchor on a corner)
                if (Cuts(rects, fromX, fromY, corners[i].X, corners[i].Y, -CatchInset)) continue;
                var cos = (kx * dx + ky * dy) / (kLen * dLen);
                if (cos > bestCos + 1e-5f || (Math.Abs(cos - bestCos) <= 1e-5f && kLen < bestLen))
                {
                    best = i;
                    bestCos = cos;
                    bestLen = kLen;
                }
            }

            return best;
        }
    }
}
