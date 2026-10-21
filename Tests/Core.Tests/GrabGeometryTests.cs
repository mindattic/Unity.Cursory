using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    /// <summary>UC-T6: grabs land on the rim (BIBLE UC-LAW-5), as Cursory's ProjectToEdge did.</summary>
    public class GrabGeometryTests
    {
        [TestCase(10f, 0f, 50f, 0f)]      // inside, nearest the right edge
        [TestCase(-40f, 5f, -50f, 5f)]    // nearest the left edge
        [TestCase(3f, 45f, 3f, 50f)]      // nearest the top (+y)
        [TestCase(3f, -45f, 3f, -50f)]    // nearest the bottom (-y)
        [TestCase(200f, 20f, 50f, 20f)]   // outside: clamped, then onto the rim
        [TestCase(80f, 90f, 50f, 50f)]    // outside a corner lands on the corner
        public void ProjectToEdge_FindsTheNearestRimPoint(float lx, float ly, float ex, float ey)
        {
            var (x, y) = GrabGeometry.ProjectToEdge(lx, ly, 50f, 50f);
            Assert.That(x, Is.EqualTo(ex).Within(1e-4f));
            Assert.That(y, Is.EqualTo(ey).Within(1e-4f));
        }

        [Test]
        public void ProjectToEdge_TheCentreGoesRight_AsInCursory()
        {
            Assert.That(GrabGeometry.ProjectToEdge(0f, 0f, 50f, 50f), Is.EqualTo((50f, 0f)));
        }

        [Test]
        public void BlockAnchor_UsesTheBodysCurrentPose()
        {
            // A 200 x 40 bar centred at (100, 100) turned a quarter turn: its long axis now runs along y,
            // so a click above it lands on its +x end in body coordinates.
            var anchor = GrabGeometry.BlockAnchor(200f, 40f, 100f, 100f, (float)Math.PI / 2f, 100f, 300f);
            Assert.That(anchor.X, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(anchor.Y, Is.EqualTo(0f).Within(1e-3f));

            var world = GrabGeometry.ToWorld(100f, 100f, (float)Math.PI / 2f, anchor.X, anchor.Y);
            Assert.That(world.X, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(world.Y, Is.EqualTo(200f).Within(1e-3f));
        }

        [Test]
        public void ShapeAnchor_SnapsToTheNearestPiecesRim()
        {
            // An L: a long piece along x and a short one rising from its right end.
            var pieces = new List<ShapePiece>
            {
                new ShapePiece { LocalX = 0f, LocalY = 0f, HalfW = 100f, HalfH = 20f },
                new ShapePiece { LocalX = 80f, LocalY = 70f, HalfW = 20f, HalfH = 50f },
            };

            Assert.That(GrabGeometry.TryShapeAnchor(pieces, 0f, 0f, 0f, 75f, 110f, out var up), Is.True);
            Assert.That(up.X, Is.EqualTo(75f).Within(1e-3f));
            Assert.That(up.Y, Is.EqualTo(120f).Within(1e-3f), "the upright's top edge");

            Assert.That(GrabGeometry.TryShapeAnchor(pieces, 0f, 0f, 0f, -90f, -5f, out var left), Is.True);
            Assert.That(left.X, Is.EqualTo(-100f).Within(1e-3f), "the long piece's left end");
            Assert.That(left.Y, Is.EqualTo(-5f).Within(1e-3f));
        }

        [Test]
        public void ShapeAnchor_NoPieces_IsNoGrab()
        {
            Assert.That(GrabGeometry.TryShapeAnchor(new List<ShapePiece>(), 0f, 0f, 0f, 1f, 1f, out _), Is.False);
            Assert.That(GrabGeometry.TryShapeAnchor(null, 0f, 0f, 0f, 1f, 1f, out _), Is.False);
        }

        [Test]
        public void ToLocal_UndoesToWorld()
        {
            var w = GrabGeometry.ToWorld(12f, -7f, 0.7f, 33f, 44f);
            var l = GrabGeometry.ToLocal(12f, -7f, 0.7f, w.X, w.Y);
            Assert.That(l.X, Is.EqualTo(33f).Within(1e-3f));
            Assert.That(l.Y, Is.EqualTo(44f).Within(1e-3f));
        }
    }
}
