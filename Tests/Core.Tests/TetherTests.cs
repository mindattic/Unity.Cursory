using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    /// <summary>UC-T8: the leash and pull readout, cut tests, and a body's corners and boxes (Cursory's RoomState tether maths).</summary>
    public class TetherGeometryTests
    {
        [Test]
        public void Leash_HoldsAFarCursorAtMaxPull()
        {
            var (x, y) = TetherGeometry.Leash(100f, 100f, 100f + 600f, 100f + 800f);   // 1000 px away
            Assert.That(x, Is.EqualTo(100f + 180f).Within(1e-3f));
            Assert.That(y, Is.EqualTo(100f + 240f).Within(1e-3f), "slid back along the rope to 300 px");
            Assert.That(Tuning.MaxPullPx, Is.EqualTo(300f));
        }

        [TestCase(0f, 0f)]
        [TestCase(120f, -50f)]
        [TestCase(300f, 0f)]
        public void Leash_LeavesACursorInsideAlone(float dx, float dy)
        {
            Assert.That(TetherGeometry.Leash(10f, 20f, 10f + dx, 20f + dy), Is.EqualTo((10f + dx, 20f + dy)));
        }

        [Test]
        public void Leash_TakesAShorterLength()
        {
            var (x, _) = TetherGeometry.Leash(0f, 0f, 50f, 0f, 20f);
            Assert.That(x, Is.EqualTo(20f).Within(1e-4f));
        }

        [TestCase(0f, 0f)]
        [TestCase(150f, 0.75f)]
        [TestCase(300f, 1.5f)]
        [TestCase(900f, 1.5f)]
        public void PullMass_SaturatesAtOneGrabsCeiling(float length, float mass)
        {
            Assert.That(TetherGeometry.PullMass(0f, 0f, 0f, length), Is.EqualTo(mass).Within(1e-4f));
        }

        [Test]
        public void SegmentRectEntry_FindsWhereASegmentEnters()
        {
            Assert.That(TetherGeometry.SegmentRectEntry(-100f, 0f, 100f, 0f, 50f, 50f, out var t), Is.True);
            Assert.That(t, Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void SegmentRectEntry_StartingInsideEntersAtZero()
        {
            Assert.That(TetherGeometry.SegmentRectEntry(0f, 0f, 200f, 0f, 50f, 50f, out var t), Is.True);
            Assert.That(t, Is.EqualTo(0f));
        }

        [TestCase(-100f, 60f, 100f, 60f)]    // passes above
        [TestCase(-100f, -100f, -60f, 100f)] // passes to the left
        [TestCase(-100f, 0f, -60f, 0f)]      // stops short
        public void SegmentRectEntry_MissesABoxItDoesNotCross(float ax, float ay, float bx, float by)
        {
            Assert.That(TetherGeometry.SegmentRectEntry(ax, ay, bx, by, 50f, 50f, out _), Is.False);
        }

        [Test]
        public void SegmentRectEntry_AnEmptyBoxIsNeverCut()
        {
            Assert.That(TetherGeometry.SegmentRectEntry(-100f, 0f, 100f, 0f, -2f, 50f, out _), Is.False,
                "a thin piece inset past nothing is no box at all");
        }

        [Test]
        public void BlockCorners_AreInCursorysOrder()
        {
            Assert.That(TetherGeometry.BlockCorners(100f, 40f),
                Is.EqualTo(new[] { (-50f, -20f), (50f, -20f), (50f, 20f), (-50f, 20f) }));
            var rect = TetherGeometry.BlockRects(100f, 40f)[0];
            Assert.That((rect.Cx, rect.Cy, rect.HalfW, rect.HalfH), Is.EqualTo((0f, 0f, 50f, 20f)));
        }

        [Test]
        public void ShapeCorners_AreEveryPiecesCorners()
        {
            var pieces = new List<ShapePiece>
            {
                new ShapePiece { LocalX = 0f, LocalY = 0f, HalfW = 10f, HalfH = 10f },
                new ShapePiece { LocalX = 20f, LocalY = 0f, HalfW = 10f, HalfH = 5f },
            };

            var corners = TetherGeometry.ShapeCorners(pieces);
            var rects = TetherGeometry.ShapeRects(pieces);

            Assert.That(corners, Has.Count.EqualTo(8));
            Assert.That(corners[4], Is.EqualTo((10f, -5f)));
            Assert.That(corners[6], Is.EqualTo((30f, 5f)));
            Assert.That(rects, Has.Count.EqualTo(2));
            Assert.That(rects[1].Cx, Is.EqualTo(20f));
        }
    }

    /// <summary>UC-T8: the segmented tether catching on, and letting go of, a body's corners (Cursory's UpdateTether).</summary>
    public class TetherWrapTests
    {
        /// <summary>A 100 × 100 block in its own frame, grabbed on the middle of its right edge.</summary>
        private static readonly IReadOnlyList<(float X, float Y)> Corners = TetherGeometry.BlockCorners(100f, 100f);
        private static readonly IReadOnlyList<BodyRect> Rects = TetherGeometry.BlockRects(100f, 100f);
        private const float AnchorX = 50f;
        private const float AnchorY = 0f;

        private static bool Update(TetherWrap wrap, float x, float y) => wrap.Update(AnchorX, AnchorY, Corners, Rects, x, y);

        [Test]
        public void AStraightPull_CatchesNothing()
        {
            var wrap = new TetherWrap();
            Assert.That(Update(wrap, 330f, 0f), Is.False);
            Assert.That(wrap.Corners, Is.Empty);
            Assert.That(wrap.ContactCorner, Is.EqualTo(-1));
            Assert.That(wrap.Contact(AnchorX, AnchorY, Corners), Is.EqualTo((AnchorX, AnchorY)));
        }

        [Test]
        public void AnOffCentrePull_StillCatchesNothing()
        {
            var wrap = new TetherWrap();
            Update(wrap, 250f, -280f);   // steeply off to one side, but never back through the body
            Update(wrap, 60f, 300f);
            Assert.That(wrap.Corners, Is.Empty);
        }

        [Test]
        public void PullingRoundTheBody_CatchesTheCornerItPassesFirst()
        {
            var wrap = new TetherWrap();
            Assert.That(Update(wrap, -230f, -30f), Is.True, "the rope would cut straight through the block");

            Assert.That(wrap.Corners, Is.EqualTo(new[] { 1 }), "the bottom-right corner, nearest where it enters");
            Assert.That(wrap.Contact(AnchorX, AnchorY, Corners), Is.EqualTo((50f, -50f)), "the grab now pulls the corner");
            var chain = new List<(float X, float Y)>();
            wrap.Chain(AnchorX, AnchorY, Corners, chain);
            Assert.That(chain, Is.EqualTo(new[] { (50f, 0f), (50f, -50f) }));
        }

        [Test]
        public void PullingFurtherRound_CatchesASecondCorner()
        {
            var wrap = new TetherWrap();
            Update(wrap, -230f, -30f);
            Update(wrap, -200f, 40f);   // round past the bottom-left corner as well

            Assert.That(wrap.Corners, Is.EqualTo(new[] { 1, 0 }));
            AssertNoSegmentCutsTheBody(wrap, -200f, 40f);
        }

        [Test]
        public void SwingingBack_UnwindsTheCorners()
        {
            var wrap = new TetherWrap();
            Update(wrap, -230f, -30f);
            Update(wrap, -200f, 40f);

            Update(wrap, -230f, -90f);
            Assert.That(wrap.Corners, Is.EqualTo(new[] { 1 }), "back below the bottom edge: the second corner lets go");

            Assert.That(Update(wrap, 330f, 0f), Is.True);
            Assert.That(wrap.Corners, Is.Empty, "straight out again: the rope is straight");
        }

        [Test]
        public void HoldingStill_KeepsTheCatch()
        {
            var wrap = new TetherWrap();
            Update(wrap, -230f, -30f);
            for (var i = 0; i < 5; i++)
            {
                Assert.That(Update(wrap, -230f, -30f), Is.False);
            }

            Assert.That(wrap.Corners, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Clear_StraightensTheRope()
        {
            var wrap = new TetherWrap();
            Update(wrap, -230f, -30f);
            wrap.Clear();
            Assert.That(wrap.Corners, Is.Empty);
        }

        [Test]
        public void ACornerListThatShrank_StartsStraight()
        {
            var wrap = new TetherWrap();
            Update(wrap, -200f, -30f);
            Update(wrap, -200f, 40f);
            var fewer = new List<(float X, float Y)> { Corners[0] };
            wrap.Update(AnchorX, AnchorY, fewer, Rects, 330f, 0f);
            Assert.That(wrap.Corners, Is.Empty);
        }

        [Test]
        public void NeverCatchesMoreThanMaxCorners()
        {
            // A long chain of small pieces gives the rope many corners to catch in one go.
            var pieces = new List<ShapePiece>();
            for (var i = 0; i < 12; i++) pieces.Add(new ShapePiece { LocalX = i * 30f, LocalY = (i % 2) * 40f, HalfW = 14f, HalfH = 14f });
            var corners = TetherGeometry.ShapeCorners(pieces);
            var rects = TetherGeometry.ShapeRects(pieces);
            var wrap = new TetherWrap();
            for (var step = 0; step < 40; step++)
            {
                wrap.Update(-14f, 0f, corners, rects, 400f - step * 25f, step % 2 == 0 ? 400f : -400f);
                Assert.That(wrap.Corners.Count, Is.LessThanOrEqualTo(TetherWrap.MaxCorners));
            }
        }

        [Test]
        public void ShapePieces_AreWrappedLikeABlock()
        {
            // An L: a long bar plus a foot standing on its right end; grabbed on the bar's right end.
            var pieces = new List<ShapePiece>
            {
                new ShapePiece { LocalX = 0f, LocalY = 0f, HalfW = 100f, HalfH = 20f },
                new ShapePiece { LocalX = 80f, LocalY = 60f, HalfW = 20f, HalfH = 40f },
            };
            var corners = TetherGeometry.ShapeCorners(pieces);
            var rects = TetherGeometry.ShapeRects(pieces);
            var wrap = new TetherWrap();

            wrap.Update(100f, 0f, corners, rects, -400f, -40f);   // pulled back through the bar

            Assert.That(wrap.Corners, Is.EqualTo(new[] { 1 }));
            Assert.That(wrap.Contact(100f, 0f, corners), Is.EqualTo((100f, -20f)), "the bar's bottom-right corner");
        }

        private static void AssertNoSegmentCutsTheBody(TetherWrap wrap, float cursorX, float cursorY)
        {
            var chain = new List<(float X, float Y)>();
            wrap.Chain(AnchorX, AnchorY, Corners, chain);
            chain.Add((cursorX, cursorY));
            for (var i = 0; i + 1 < chain.Count; i++)
            {
                Assert.That(TetherGeometry.SegmentRectEntry(chain[i].X, chain[i].Y, chain[i + 1].X, chain[i + 1].Y,
                    50f - TetherWrap.CatchInset, 50f - TetherWrap.CatchInset, out _), Is.False, "segment " + i + " cuts the block");
            }
        }
    }

    /// <summary>UC-T8: optional cursor collision pushes the cursor disc out of walls and shapes (Cursory's ResolveOutOf*).</summary>
    public class CursorCollisionTests
    {
        private static readonly Wall Pillar = new Wall { Id = "w", X = 0f, Y = 0f, W = 50f, H = 400f };

        [Test]
        public void InsideAWall_IsPushedToTheNearestEdgePlusTheRadius()
        {
            var (x, y) = CursorCollision.OutOfWalls(new[] { Pillar }, 5f, 30f);
            Assert.That(x, Is.EqualTo(25f + Tuning.CursorRadius).Within(1e-4f), "the shallow axis is x");
            Assert.That(y, Is.EqualTo(30f));
            Assert.That(Tuning.CursorRadius, Is.EqualTo(10f));
        }

        [Test]
        public void NearAWallsEnd_IsPushedAlongY()
        {
            var (x, y) = CursorCollision.OutOfWalls(new[] { Pillar }, -2f, -205f);
            Assert.That(x, Is.EqualTo(-2f));
            Assert.That(y, Is.EqualTo(-200f - Tuning.CursorRadius).Within(1e-4f));
        }

        [Test]
        public void TheDiscItself_IsKeptClear_NotJustItsCentre()
        {
            var (x, _) = CursorCollision.OutOfWalls(new[] { Pillar }, 30f, 0f);   // centre outside, disc overlapping
            Assert.That(x, Is.EqualTo(35f).Within(1e-4f));
        }

        [Test]
        public void ClearOfEveryWall_IsUnchanged()
        {
            Assert.That(CursorCollision.OutOfWalls(new[] { Pillar }, 40f, 0f), Is.EqualTo((40f, 0f)));
            float x = 40f, y = 0f;
            Assert.That(CursorCollision.OutOfBox(ref x, ref y, 0f, 0f, 25f, 200f), Is.False);
        }

        [Test]
        public void InsideATurnedShape_IsPushedOutInTheShapesFrame()
        {
            var pieces = new List<ShapePiece> { new ShapePiece { LocalX = 0f, LocalY = 0f, HalfW = 100f, HalfH = 20f } };
            var angle = (float)(System.Math.PI / 2);   // the bar now stands upright
            var (x, y) = CursorCollision.OutOfShape(pieces, 500f, 500f, angle, 505f, 560f);

            Assert.That(x, Is.EqualTo(500f + 20f + Tuning.CursorRadius).Within(1e-3f), "out of the upright bar's side");
            Assert.That(y, Is.EqualTo(560f).Within(1e-3f));
        }

        [Test]
        public void ClearOfAShape_IsUnchanged()
        {
            var pieces = new List<ShapePiece> { new ShapePiece { LocalX = 0f, LocalY = 0f, HalfW = 100f, HalfH = 20f } };
            Assert.That(CursorCollision.OutOfShape(pieces, 0f, 0f, 0f, 0f, 100f), Is.EqualTo((0f, 100f)));
        }
    }
}
