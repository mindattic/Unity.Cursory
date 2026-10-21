using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>What a press landed on (<see cref="BodyPicker.Pick"/>).</summary>
    public enum PickKind
    {
        /// <summary>Empty space: the press arms a whistle and a pan (<see cref="PointerGesture"/>).</summary>
        None = 0,
        WireEnd = 1,
        Shape = 2,
        Block = 3,
        Wall = 4,
    }

    /// <summary>A pick result: what was hit and its id (for a wire end, which end).</summary>
    public readonly struct PickResult : IEquatable<PickResult>
    {
        public PickResult(PickKind kind, string id, bool endA = false)
        {
            Kind = kind;
            Id = id;
            EndA = endA;
        }

        /// <summary>Nothing was hit.</summary>
        public static PickResult Nothing => default;

        public PickKind Kind { get; }
        public string Id { get; }

        /// <summary>For <see cref="PickKind.WireEnd"/>: true for end A, false for end B.</summary>
        public bool EndA { get; }

        public bool IsHit => Kind != PickKind.None;

        public bool Equals(PickResult other) => Kind == other.Kind && Id == other.Id && EndA == other.EndA;

        public override bool Equals(object obj) => obj is PickResult other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ (Id != null ? Id.GetHashCode() : 0) ^ (EndA ? 1 : 0);

        public override string ToString() => Kind == PickKind.None ? "Nothing" : Kind + " " + Id + (Kind == PickKind.WireEnd ? (EndA ? " (A)" : " (B)") : "");
    }

    /// <summary>A body's live pose as this client sees it: centre in world pixels, rotation in radians.</summary>
    public readonly struct BodyPose
    {
        public BodyPose(float x, float y, float angle)
        {
            X = x;
            Y = y;
            Angle = angle;
        }

        public float X { get; }
        public float Y { get; }
        public float Angle { get; }
    }

    /// <summary>
    /// What a press at a world point grabs, in Cursory's order (room-core.js <c>onMouseDown</c>): a wire end within
    /// <see cref="WireEndRadiusPx"/> first (they sit on top), then a shape piece, then a block, then a wall; otherwise
    /// nothing, and the press is a whistle or a pan. Blocks and shapes are tested at their live poses (rotated boxes);
    /// walls and the level's dimensions come from the level data every client holds.
    /// </summary>
    public static class BodyPicker
    {
        /// <summary>How close to a wire end a press must land to pick it up (Cursory's generous 40 px).</summary>
        public const float WireEndRadiusPx = 40f;

        /// <summary>
        /// The thing under world point (<paramref name="x"/>, <paramref name="y"/>) in <paramref name="level"/>. A body
        /// missing from <paramref name="poses"/> sits where the level puts it; <paramref name="wireEnds"/> holds each wire's
        /// live ends (A.x, A.y, B.x, B.y) in the level's wire order, or null to use the level's own.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="level"/> is null.</exception>
        public static PickResult Pick(LevelData level, IReadOnlyDictionary<string, BodyPose> poses, IReadOnlyList<(float Ax, float Ay, float Bx, float By)> wireEnds, float x, float y)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));

            var best = PickResult.Nothing;
            float bestD = WireEndRadiusPx * WireEndRadiusPx;
            for (var i = 0; i < level.Wires.Count; i++)
            {
                var wire = level.Wires[i];
                var ends = wireEnds != null && i < wireEnds.Count ? wireEnds[i] : (wire.Ax, wire.Ay, wire.Bx, wire.By);
                float da = Sq(ends.Ax - x) + Sq(ends.Ay - y);
                if (da <= bestD) { bestD = da; best = new PickResult(PickKind.WireEnd, wire.Id, true); }
                float db = Sq(ends.Bx - x) + Sq(ends.By - y);
                if (db <= bestD) { bestD = db; best = new PickResult(PickKind.WireEnd, wire.Id, false); }
            }
            if (best.IsHit) return best;

            foreach (var shape in level.Shapes)
            {
                var pose = PoseOf(poses, shape.Id, shape.X, shape.Y, shape.Angle);
                var (lx, ly) = ToLocal(pose, x, y);
                foreach (var p in shape.Pieces)
                {
                    if (lx > p.LocalX - p.HalfW && lx < p.LocalX + p.HalfW && ly > p.LocalY - p.HalfH && ly < p.LocalY + p.HalfH)
                        return new PickResult(PickKind.Shape, shape.Id);
                }
            }

            foreach (var block in level.Blocks)
            {
                var pose = PoseOf(poses, block.Id, block.X, block.Y, block.Angle);
                var (lx, ly) = ToLocal(pose, x, y);
                if (lx > -block.W / 2f && lx < block.W / 2f && ly > -block.H / 2f && ly < block.H / 2f)
                    return new PickResult(PickKind.Block, block.Id);
            }

            foreach (var wall in level.Walls)
            {
                if (x > wall.X - wall.W / 2f && x < wall.X + wall.W / 2f && y > wall.Y - wall.H / 2f && y < wall.Y + wall.H / 2f)
                    return new PickResult(PickKind.Wall, wall.Id);
            }

            return PickResult.Nothing;
        }

        /// <summary>World point (<paramref name="x"/>, <paramref name="y"/>) in <paramref name="pose"/>'s body frame.</summary>
        public static (float X, float Y) ToLocal(BodyPose pose, float x, float y)
        {
            double c = Math.Cos(-pose.Angle), s = Math.Sin(-pose.Angle);
            double dx = x - pose.X, dy = y - pose.Y;
            return ((float)(dx * c - dy * s), (float)(dx * s + dy * c));
        }

        private static BodyPose PoseOf(IReadOnlyDictionary<string, BodyPose> poses, string id, float x, float y, float angle) =>
            poses != null && id != null && poses.TryGetValue(id, out var pose) ? pose : new BodyPose(x, y, angle);

        private static float Sq(float v) => v * v;
    }
}
