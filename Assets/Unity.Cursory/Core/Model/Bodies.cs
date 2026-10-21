using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    // Level data types use public fields and [Serializable] so a level ScriptableObject (UC-T5) can hold
    // them directly. Positions are the centre of the thing, in world pixels (BIBLE UC-LAW-3); angles are
    // radians, anti-clockwise from the world x-axis.

    /// <summary>
    /// A draggable rectangular rigid body (Cursory's <c>BlockState</c>). It moves when the pulls of the
    /// cursors grabbing it sum past its <see cref="Mass"/> (BIBLE UC-LAW-2).
    /// </summary>
    [Serializable]
    public sealed class Block
    {
        public string Id = "";
        public float X;
        public float Y;
        public float W;
        public float H;
        /// <summary>Starting rotation in radians.</summary>
        public float Angle;
        /// <summary>The printed weight and move threshold.</summary>
        public float Mass = 6f;
        public string Color = "#3a3a3a";

        public Block Clone() => (Block)MemberwiseClone();
    }

    /// <summary>
    /// One rectangle of a compound <see cref="Shape"/>, centred at (<see cref="LocalX"/>, <see cref="LocalY"/>)
    /// in the shape's body frame with half-extents <see cref="HalfW"/> × <see cref="HalfH"/>.
    /// </summary>
    [Serializable]
    public sealed class ShapePiece
    {
        public float LocalX;
        public float LocalY;
        public float HalfW;
        public float HalfH;

        /// <summary>This piece's centre in world pixels when its shape sits at (<paramref name="x"/>, <paramref name="y"/>) turned by <paramref name="angle"/>.</summary>
        public (float X, float Y) WorldCentre(float x, float y, float angle)
        {
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            return (x + LocalX * cos - LocalY * sin, y + LocalX * sin + LocalY * cos);
        }

        public ShapePiece Clone() => (ShapePiece)MemberwiseClone();
    }

    /// <summary>
    /// A compound rigid body built from rectangles (Cursory's <c>ShapeActor</c>), such as an L made of two
    /// pieces. One body with a collider per piece, so offset pulls turn it.
    /// </summary>
    [Serializable]
    public sealed class Shape
    {
        public string Id = "";
        public float X;
        public float Y;
        /// <summary>Starting rotation in radians.</summary>
        public float Angle;
        /// <summary>The printed weight and move threshold, as for a <see cref="Block"/>.</summary>
        public float Mass = 6f;
        public string Color = "#7F77DD";
        public List<ShapePiece> Pieces = new List<ShapePiece>();

        public Shape Clone()
        {
            var copy = (Shape)MemberwiseClone();
            copy.Pieces = new List<ShapePiece>(Pieces.Count);
            foreach (var p in Pieces) copy.Pieces.Add(p.Clone());
            return copy;
        }
    }

    /// <summary>A static rectangular obstacle. Bodies collide with it; it is static geometry (BIBLE UC-LAW-4).</summary>
    [Serializable]
    public sealed class Wall
    {
        public string Id = "";
        public float X;
        public float Y;
        public float W;
        public float H;

        public Wall Clone() => (Wall)MemberwiseClone();
    }

    /// <summary>A static titled signpost in the world; static geometry (BIBLE UC-LAW-4).</summary>
    [Serializable]
    public sealed class WorldLabel
    {
        public string Id = "";
        public float X;
        public float Y;
        public string Title = "";
        public string Subtitle = "";

        public WorldLabel Clone() => (WorldLabel)MemberwiseClone();
    }
}
