using System;
using System.Collections.Generic;
using Unity.Cursory.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// A cursor's rope as every peer draws it (UC-T8, <see cref="CursorAvatar.Tether"/>): the chain from the grab's
    /// rim anchor through every corner a segmented tether has caught, in world pixels. The last point is the contact
    /// the grab pulls on; the rope's final stretch runs from there to the cursor. Empty (<see cref="Count"/> 0) when
    /// the cursor holds nothing a rope is drawn to.
    /// </summary>
    /// <remarks>
    /// A fixed-size value (at most <see cref="MaxPoints"/>: the anchor plus <see cref="TetherWrap.MaxCorners"/>), so
    /// it can be a <c>[Synced]</c> field; only the points in use go on the wire, and unused slots stay zero so two
    /// equal ropes always compare equal.
    /// </remarks>
    public struct TetherSync : INetworkSerializable, IEquatable<TetherSync>
    {
        /// <summary>The longest chain: the anchor and every corner a rope may catch.</summary>
        public const int MaxPoints = TetherWrap.MaxCorners + 1;

        private byte _count;
        private Vector2 _p0, _p1, _p2, _p3, _p4, _p5, _p6, _p7, _p8;

        /// <summary>How many points the chain has; 0 for no rope.</summary>
        public int Count => _count;

        /// <summary>The chain's point <paramref name="index"/>, world pixels; 0 is the anchor, <see cref="Count"/> - 1 the contact.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not below <see cref="Count"/>.</exception>
        public Vector2 this[int index]
        {
            get
            {
                if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
                return Get(index);
            }
        }

        /// <summary>The contact the grab pulls on (the chain's last point); only meaningful when <see cref="Count"/> is above 0.</summary>
        public Vector2 Contact => _count > 0 ? Get(_count - 1) : Vector2.zero;

        /// <summary>A rope through <paramref name="points"/> (world pixels), keeping at most <see cref="MaxPoints"/>.</summary>
        public static TetherSync From(IReadOnlyList<(float X, float Y)> points)
        {
            var sync = new TetherSync();
            if (points == null) return sync;
            var n = Math.Min(points.Count, MaxPoints);
            for (var i = 0; i < n; i++) sync.Set(i, new Vector2(points[i].X, points[i].Y));
            sync._count = (byte)n;
            return sync;
        }

        /// <summary>A straight rope pulling on the one point (<paramref name="x"/>, <paramref name="y"/>), e.g. a wall's rim.</summary>
        public static TetherSync Single(float x, float y)
        {
            var sync = new TetherSync();
            sync.Set(0, new Vector2(x, y));
            sync._count = 1;
            return sync;
        }

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _count);
            if (serializer.IsReader)
            {
                if (_count > MaxPoints) _count = MaxPoints;
                for (var i = 0; i < MaxPoints; i++) Set(i, Vector2.zero);
            }

            for (var i = 0; i < _count; i++)
            {
                var p = Get(i);
                serializer.SerializeValue(ref p);
                if (serializer.IsReader) Set(i, p);
            }
        }

        /// <inheritdoc />
        public bool Equals(TetherSync other)
        {
            if (_count != other._count) return false;
            for (var i = 0; i < _count; i++)
            {
                Vector2 a = Get(i), b = other.Get(i);
                if (a.x != b.x || a.y != b.y) return false;
            }

            return true;
        }

        public override bool Equals(object obj) => obj is TetherSync other && Equals(other);

        public override int GetHashCode()
        {
            int hash = _count;
            for (var i = 0; i < _count; i++) hash = hash * 31 + Get(i).GetHashCode();
            return hash;
        }

        private Vector2 Get(int i)
        {
            switch (i)
            {
                case 0: return _p0;
                case 1: return _p1;
                case 2: return _p2;
                case 3: return _p3;
                case 4: return _p4;
                case 5: return _p5;
                case 6: return _p6;
                case 7: return _p7;
                default: return _p8;
            }
        }

        private void Set(int i, Vector2 v)
        {
            switch (i)
            {
                case 0: _p0 = v; break;
                case 1: _p1 = v; break;
                case 2: _p2 = v; break;
                case 3: _p3 = v; break;
                case 4: _p4 = v; break;
                case 5: _p5 = v; break;
                case 6: _p6 = v; break;
                case 7: _p7 = v; break;
                default: _p8 = v; break;
            }
        }
    }
}
