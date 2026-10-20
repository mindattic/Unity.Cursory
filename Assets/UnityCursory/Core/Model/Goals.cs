using System;

namespace UnityCursory.Core
{
    /// <summary>
    /// A goal rectangle solved while its target <see cref="Block"/>'s centre is strictly inside it
    /// (Cursory's <c>GoalZone</c>).
    /// </summary>
    [Serializable]
    public sealed class BlockGoal
    {
        public string Id = "";
        public float X;
        public float Y;
        public float W;
        public float H;
        public string TargetBlockId = "";

        /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) is strictly inside the rectangle.</summary>
        public bool Contains(float x, float y) => GoalRect.Contains(X, Y, W, H, x, y);

        /// <summary>True when a target block centred at (<paramref name="blockX"/>, <paramref name="blockY"/>) solves this goal.</summary>
        public bool IsSatisfiedBy(float blockX, float blockY) => Contains(blockX, blockY);

        public BlockGoal Clone() => (BlockGoal)MemberwiseClone();
    }

    /// <summary>
    /// A goal rectangle solved only while every piece centre of its target <see cref="Shape"/> is strictly
    /// inside it: the whole shape has to be threaded in, not just its origin.
    /// </summary>
    [Serializable]
    public sealed class ShapeGoal
    {
        public string Id = "";
        public float X;
        public float Y;
        public float W;
        public float H;
        public string TargetShapeId = "";

        /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) is strictly inside the rectangle.</summary>
        public bool Contains(float x, float y) => GoalRect.Contains(X, Y, W, H, x, y);

        /// <summary>
        /// True when <paramref name="shape"/>, posed at (<paramref name="x"/>, <paramref name="y"/>) turned by
        /// <paramref name="angle"/>, has every piece centre inside. A shape with no pieces never solves it.
        /// </summary>
        public bool IsSatisfiedBy(Shape shape, float x, float y, float angle)
        {
            if (shape == null || shape.Pieces.Count == 0) return false;
            foreach (var piece in shape.Pieces)
            {
                var (wx, wy) = piece.WorldCentre(x, y, angle);
                if (!Contains(wx, wy)) return false;
            }
            return true;
        }

        public ShapeGoal Clone() => (ShapeGoal)MemberwiseClone();
    }

    internal static class GoalRect
    {
        public static bool Contains(float cx, float cy, float w, float h, float x, float y) =>
            x > cx - w / 2f && x < cx + w / 2f && y > cy - h / 2f && y < cy + h / 2f;
    }
}
