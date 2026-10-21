using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Cursory.Core.Tests.Solvability
{
    /// <summary>
    /// A headless stand-in for the server's Physics2D world, just faithful enough to answer "can two cursors
    /// get this level's bodies to their goals?" without the editor. It models the Mass rule of BIBLE
    /// UC-LAW-2 with the same constants the real sim uses (<see cref="Tuning"/>): each grab pulls with up to
    /// <see cref="Tuning.GrabMaxForce"/> newtons, a spring of the grab joint's own stiffness (rigid body mass
    /// times (2 pi <see cref="Tuning.GrabFrequency"/>) squared, so it saturates within about a pixel, as the real
    /// joint does; the 300-pixel ramp of <see cref="Tuning.MaxPullPx"/> is only the HUD's pull readout); ground
    /// friction is Coulomb friction of
    /// <see cref="Tuning.FrictionForce"/>; inertia is <see cref="Tuning.RigidbodyMass"/>; linear damping is
    /// <see cref="Tuning.LinearDamping"/>. Bodies translate only (no torque) and collide as axis-aligned boxes
    /// with walls, the world edge and each other, sliding along what they hit.
    ///
    /// That catches what Cursory's SolvabilityTests catch: a body too heavy for two cursors, a gap too tight to
    /// pass, a goal that can't be reached. It is not the game's physics (UC-T6 is, with its own PlayMode
    /// test); rotation puzzles are left to the shape movability check and to human play, as in Cursory.
    /// </summary>
    internal sealed class PullRoom
    {
        private const float Dt = SimulationRate.TickSeconds;
        private const float RestSpeed = 1e-4f;   // m/s: below this a body is at rest and static friction applies
        private const float MaxSubstepPx = 10f;  // no tunnelling through 120-pixel walls

        private readonly List<Body> bodies = new List<Body>();
        private readonly List<Box> walls = new List<Box>();
        private readonly Dictionary<string, Tether> grabs = new Dictionary<string, Tether>(StringComparer.Ordinal);

        public PullRoom(LevelData level)
        {
            Level = level.Clone();
            foreach (var b in Level.Blocks)
            {
                RequireUnrotated(b.Id, b.Angle);
                bodies.Add(new Body(b.Id, b.Mass, b.X, b.Y, new[] { new Box(0, 0, b.W / 2f, b.H / 2f) }));
            }
            foreach (var s in Level.Shapes)
            {
                RequireUnrotated(s.Id, s.Angle);
                bodies.Add(new Body(s.Id, s.Mass, s.X, s.Y, s.Pieces.Select(p => new Box(p.LocalX, p.LocalY, p.HalfW, p.HalfH)).ToArray()));
            }
            foreach (var w in Level.Walls) walls.Add(new Box(w.X, w.Y, w.W / 2f, w.H / 2f));
        }

        public LevelData Level { get; }

        public int Ticks { get; private set; }

        /// <summary>Where body <paramref name="id"/> is now.</summary>
        public (float X, float Y) Position(string id)
        {
            var b = Find(id);
            return (b.X, b.Y);
        }

        /// <summary>
        /// <paramref name="user"/> grabs body <paramref name="id"/> at the point on its rim nearest
        /// (<paramref name="clickX"/>, <paramref name="clickY"/>) (BIBLE UC-LAW-5). False if the body doesn't exist.
        /// </summary>
        public bool Grab(string user, string id, float clickX, float clickY)
        {
            var b = bodies.FirstOrDefault(x => x.Id == id);
            if (b == null) return false;
            var bestDist = float.MaxValue;
            (float X, float Y) bestPoint = default;
            foreach (var piece in b.Pieces)
            {
                var p = piece.NearestRimPoint(clickX - b.X, clickY - b.Y);
                var d = Sq(p.X - (clickX - b.X)) + Sq(p.Y - (clickY - b.Y));
                if (d < bestDist) { bestDist = d; bestPoint = p; }
            }
            grabs[user] = new Tether(b, bestPoint.X, bestPoint.Y) { TargetX = b.X + bestPoint.X, TargetY = b.Y + bestPoint.Y };
            return true;
        }

        /// <summary>Moves <paramref name="user"/>'s cursor; a cursor more than the leash away from its grab only pulls harder up to the cap.</summary>
        public void SetCursor(string user, float x, float y)
        {
            if (grabs.TryGetValue(user, out var g)) { g.TargetX = x; g.TargetY = y; }
        }

        /// <summary>One 1/30 s tick.</summary>
        public void Step()
        {
            Ticks++;
            foreach (var b in bodies)
            {
                var kg = Tuning.RigidbodyMass(b.Mass);
                var omega = 2f * (float)Math.PI * Tuning.GrabFrequency;
                var saturationPx = WorldUnits.ToPixels(Tuning.GrabMaxForce / (kg * omega * omega));
                float fx = 0, fy = 0;
                foreach (var g in grabs.Values)
                {
                    if (g.Body != b) continue;
                    var dx = g.TargetX - (b.X + g.LocalX);
                    var dy = g.TargetY - (b.Y + g.LocalY);
                    var len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-3f) continue;
                    var force = Tuning.GrabMaxForce * Math.Min(1f, len / saturationPx);
                    fx += force * dx / len;
                    fy += force * dy / len;
                }

                var friction = Tuning.FrictionForce(b.Mass);
                var speed = (float)Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
                if (speed < RestSpeed)
                {
                    var pull = (float)Math.Sqrt(fx * fx + fy * fy);
                    if (pull <= friction) { b.Vx = b.Vy = 0; continue; }
                    fx -= friction * fx / pull;
                    fy -= friction * fy / pull;
                    b.Vx += fx / kg * Dt;
                    b.Vy += fy / kg * Dt;
                }
                else
                {
                    var vx = b.Vx + (fx - friction * b.Vx / speed) / kg * Dt;
                    var vy = b.Vy + (fy - friction * b.Vy / speed) / kg * Dt;
                    // Friction stops a body; it never pushes it backwards.
                    if (vx * b.Vx + vy * b.Vy < 0f && Math.Sqrt(fx * fx + fy * fy) <= friction) { vx = 0; vy = 0; }
                    b.Vx = vx;
                    b.Vy = vy;
                }

                var damping = 1f / (1f + Dt * Tuning.LinearDamping);
                b.Vx *= damping;
                b.Vy *= damping;
                Move(b, WorldUnits.ToPixels(b.Vx * Dt), WorldUnits.ToPixels(b.Vy * Dt));
            }
        }

        /// <summary>True when every block goal and shape goal is satisfied.</summary>
        public bool AllGoalsSolved()
        {
            foreach (var g in Level.BlockGoals)
            {
                var (x, y) = Position(g.TargetBlockId);
                if (!g.IsSatisfiedBy(x, y)) return false;
            }
            foreach (var g in Level.ShapeGoals)
            {
                var (x, y) = Position(g.TargetShapeId);
                var shape = Level.Shapes.Single(s => s.Id == g.TargetShapeId);
                if (!g.IsSatisfiedBy(shape, x, y, shape.Angle)) return false;
            }
            return true;
        }

        private void Move(Body b, float dxPx, float dyPx)
        {
            var steps = (int)Math.Ceiling(Math.Max(Math.Abs(dxPx), Math.Abs(dyPx)) / MaxSubstepPx);
            if (steps == 0) return;
            var sx = dxPx / steps;
            var sy = dyPx / steps;
            for (var i = 0; i < steps; i++)
            {
                if (sx != 0f)
                {
                    b.X += sx;
                    if (Blocked(b)) { b.X -= sx; sx = 0f; b.Vx = 0f; }
                }
                if (sy != 0f)
                {
                    b.Y += sy;
                    if (Blocked(b)) { b.Y -= sy; sy = 0f; b.Vy = 0f; }
                }
            }
        }

        private bool Blocked(Body b)
        {
            foreach (var piece in b.Pieces)
            {
                var mine = piece.At(b.X, b.Y);
                if (mine.MinX < 0 || mine.MinY < 0 || mine.MaxX > WorldUnits.WorldSizePx || mine.MaxY > WorldUnits.WorldSizePx) return true;
                foreach (var w in walls)
                    if (mine.Overlaps(w)) return true;
                foreach (var other in bodies)
                {
                    if (other == b) continue;
                    foreach (var op in other.Pieces)
                        if (mine.Overlaps(op.At(other.X, other.Y))) return true;
                }
            }
            return false;
        }

        private Body Find(string id) =>
            bodies.FirstOrDefault(b => b.Id == id) ?? throw new ArgumentException($"no body '{id}' in level {Level.Number}");

        private static void RequireUnrotated(string id, float angle)
        {
            if (angle != 0f) throw new NotSupportedException($"'{id}' starts rotated; the pull harness only translates bodies");
        }

        private static float Sq(float v) => v * v;

        private sealed class Body
        {
            public Body(string id, float mass, float x, float y, Box[] pieces)
            {
                Id = id;
                Mass = mass;
                X = x;
                Y = y;
                Pieces = pieces;
            }

            public string Id { get; }
            public float Mass { get; }
            public Box[] Pieces { get; }
            public float X;
            public float Y;
            public float Vx; // m/s
            public float Vy;
        }

        private sealed class Tether
        {
            public Tether(Body body, float localX, float localY)
            {
                Body = body;
                LocalX = localX;
                LocalY = localY;
            }

            public Body Body { get; }
            public float LocalX { get; }
            public float LocalY { get; }
            public float TargetX;
            public float TargetY;
        }

        /// <summary>An axis-aligned box: centre and half-extents, in pixels.</summary>
        private readonly struct Box
        {
            public Box(float cx, float cy, float hw, float hh)
            {
                Cx = cx;
                Cy = cy;
                Hw = hw;
                Hh = hh;
            }

            public float Cx { get; }
            public float Cy { get; }
            public float Hw { get; }
            public float Hh { get; }
            public float MinX => Cx - Hw;
            public float MaxX => Cx + Hw;
            public float MinY => Cy - Hh;
            public float MaxY => Cy + Hh;

            public Box At(float x, float y) => new Box(Cx + x, Cy + y, Hw, Hh);

            public bool Overlaps(Box o) => MinX < o.MaxX && MaxX > o.MinX && MinY < o.MaxY && MaxY > o.MinY;

            /// <summary>The nearest point on this box's perimeter to (<paramref name="x"/>, <paramref name="y"/>), in the same frame.</summary>
            public (float X, float Y) NearestRimPoint(float x, float y)
            {
                var px = Math.Max(MinX, Math.Min(MaxX, x));
                var py = Math.Max(MinY, Math.Min(MaxY, y));
                var toLeft = px - MinX;
                var toRight = MaxX - px;
                var toTop = py - MinY;
                var toBottom = MaxY - py;
                var min = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));
                if (min == toLeft) return (MinX, py);
                if (min == toRight) return (MaxX, py);
                if (min == toTop) return (px, MinY);
                return (px, MaxY);
            }
        }
    }
}
