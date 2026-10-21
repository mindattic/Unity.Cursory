using System;
using System.Collections.Generic;
using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// The server's Physics2D world for one level (BIBLE UC-LAW-1, UC-LAW-2): every block and shape of a
    /// <see cref="LevelData"/> as a dynamic <see cref="Rigidbody2D"/> held to the ground by a
    /// <see cref="FrictionJoint2D"/>, every wall as a static collider, and every cursor's grab as a force-capped
    /// <see cref="TargetJoint2D"/> on the body's rim (UC-LAW-5). It speaks world pixels and converts to metres
    /// only here, through <see cref="WorldUnits"/> (UC-LAW-3).
    /// </summary>
    /// <remarks>
    /// The mapping from Cursory's Aether world (MindAttic.Unity.Multiplayer RFC 0001): grab maxForce
    /// <see cref="Tuning.GrabMaxForce"/>, frequency <see cref="Tuning.GrabFrequency"/>, damping
    /// <see cref="Tuning.GrabDamping"/>, target set by hand; ground friction to the world with maxForce
    /// <c>Mass × 40</c> and maxTorque <c>× 1.2</c>; <c>rb.mass = Mass × 5</c>, gravity 0, linear damping 1.5,
    /// angular 2.0. A body moves exactly when the grabs on it sum past its Mass.
    /// <para>The world never steps itself: <see cref="Step"/> is called once per server tick by
    /// <see cref="ServerSimulation"/>, with <see cref="Physics2D.simulationMode"/> set to Script.</para>
    /// </remarks>
    public sealed class PhysicsWorld : IDisposable
    {
        private readonly Dictionary<string, Body> _bodies = new Dictionary<string, Body>();
        private readonly Dictionary<ulong, Held> _grabs = new Dictionary<ulong, Held>();
        private GameObject _root;

        /// <summary>Builds the bodies and walls of <paramref name="level"/> under a new root object.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="level"/> is null.</exception>
        public PhysicsWorld(LevelData level, string name = "PhysicsWorld")
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            _root = new GameObject(name);
            foreach (var w in level.Walls) AddWall(w);
            foreach (var b in level.Blocks) AddBlock(b);
            foreach (var s in level.Shapes) AddShape(s);
            Physics2D.SyncTransforms();
        }

        /// <summary>The ids of every movable body (blocks and shapes).</summary>
        public IEnumerable<string> BodyIds => _bodies.Keys;

        /// <summary>The cursors holding a body right now.</summary>
        public IEnumerable<ulong> Grabbers => _grabs.Keys;

        /// <summary>Advances the world by <paramref name="seconds"/> (one server tick: <see cref="SimulationRate.TickSeconds"/>).</summary>
        public void Step(float seconds)
        {
            ThrowIfDisposed();
            Physics2D.Simulate(seconds);
        }

        /// <summary>True when <paramref name="id"/> is a movable body of this world.</summary>
        public bool HasBody(string id) => id != null && _bodies.ContainsKey(id);

        /// <summary>Where body <paramref name="id"/> is: its origin in world pixels and its angle in radians.</summary>
        /// <exception cref="KeyNotFoundException">No body has that id.</exception>
        public (float X, float Y, float Angle) Pose(string id)
        {
            var rb = Find(id).Rigidbody;
            var p = rb.position;
            return (WorldUnits.ToPixels(p.x), WorldUnits.ToPixels(p.y), rb.rotation * Mathf.Deg2Rad);
        }

        /// <summary>The Physics2D body behind <paramref name="id"/>, for body sync (UC-T7) and tests.</summary>
        /// <exception cref="KeyNotFoundException">No body has that id.</exception>
        public Rigidbody2D Rigidbody(string id) => Find(id).Rigidbody;

        /// <summary>
        /// <paramref name="cursor"/> grabs body <paramref name="id"/> at the point on its rim nearest the click
        /// (<paramref name="clickX"/>, <paramref name="clickY"/>, world pixels), dropping any grab it already had. The
        /// grab pulls that point toward the cursor, which starts at the click; move it with <see cref="MoveCursor"/>.
        /// False (and no grab) when the body doesn't exist or the click isn't a finite point.
        /// </summary>
        public bool Grab(ulong cursor, string id, float clickX, float clickY)
        {
            ThrowIfDisposed();
            if (!IsFinite(clickX) || !IsFinite(clickY)) return false;
            if (id == null || !_bodies.TryGetValue(id, out var body)) return false;

            var (x, y, angle) = Pose(id);
            (float X, float Y) anchor;
            if (body.Pieces != null)
            {
                if (!GrabGeometry.TryShapeAnchor(body.Pieces, x, y, angle, clickX, clickY, out anchor)) return false;
            }
            else
            {
                anchor = GrabGeometry.BlockAnchor(body.W, body.H, x, y, angle, clickX, clickY);
            }

            Release(cursor);
            var joint = body.Rigidbody.gameObject.AddComponent<TargetJoint2D>();
            joint.autoConfigureTarget = false;
            joint.anchor = ToMeters(anchor.X, anchor.Y);
            joint.target = ToMeters(clickX, clickY);
            joint.maxForce = Tuning.GrabMaxForce;
            joint.frequency = Tuning.GrabFrequency;
            joint.dampingRatio = Tuning.GrabDamping;
            _grabs[cursor] = new Held(body, joint, anchor.X, anchor.Y);
            return true;
        }

        /// <summary>Moves <paramref name="cursor"/>'s grab target to (<paramref name="x"/>, <paramref name="y"/>) world pixels. False when it holds nothing.</summary>
        public bool MoveCursor(ulong cursor, float x, float y)
        {
            ThrowIfDisposed();
            if (!IsFinite(x) || !IsFinite(y)) return false;
            if (!_grabs.TryGetValue(cursor, out var grab)) return false;
            grab.Joint.target = ToMeters(x, y);
            return true;
        }

        /// <summary>Lets go of whatever <paramref name="cursor"/> holds. False when it held nothing.</summary>
        public bool Release(ulong cursor)
        {
            if (!_grabs.TryGetValue(cursor, out var grab)) return false;
            _grabs.Remove(cursor);
            if (grab.Joint != null)
            {
                // Destroy is deferred to the end of the frame; disabling takes the joint out of the very next step.
                grab.Joint.enabled = false;
                if (Application.isPlaying) UnityEngine.Object.Destroy(grab.Joint);
                else UnityEngine.Object.DestroyImmediate(grab.Joint);
            }
            return true;
        }

        /// <summary>
        /// What <paramref name="cursor"/> holds: the body id and the grab's rim anchor in body-local world pixels.
        /// False when it holds nothing.
        /// </summary>
        public bool TryGetGrab(ulong cursor, out string bodyId, out (float X, float Y) anchor)
        {
            if (_grabs.TryGetValue(cursor, out var grab))
            {
                bodyId = grab.Body.Id;
                anchor = (grab.AnchorX, grab.AnchorY);
                return true;
            }
            bodyId = null;
            anchor = default;
            return false;
        }

        /// <summary>Destroys every body, wall and grab.</summary>
        public void Dispose()
        {
            if (_root == null) return;
            _grabs.Clear();
            _bodies.Clear();
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root);
            _root = null;
        }

        private void AddWall(Wall w)
        {
            var go = new GameObject("Wall " + w.Id);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = ToMeters(w.X, w.Y);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(WorldUnits.ToMeters(w.W), WorldUnits.ToMeters(w.H));
        }

        private void AddBlock(Block b)
        {
            var rb = NewBody("Block " + b.Id, b.X, b.Y, b.Angle);
            var box = rb.gameObject.AddComponent<BoxCollider2D>();
            box.size = new Vector2(WorldUnits.ToMeters(b.W), WorldUnits.ToMeters(b.H));
            Finish(rb, b.Mass);
            _bodies.Add(b.Id, new Body(b.Id, rb, b.W, b.H, null));
        }

        private void AddShape(Shape s)
        {
            var rb = NewBody("Shape " + s.Id, s.X, s.Y, s.Angle);
            var pieces = new List<ShapePiece>(s.Pieces.Count);
            foreach (var p in s.Pieces)
            {
                var box = rb.gameObject.AddComponent<BoxCollider2D>();
                box.offset = ToMeters(p.LocalX, p.LocalY);
                box.size = new Vector2(WorldUnits.ToMeters(p.HalfW * 2f), WorldUnits.ToMeters(p.HalfH * 2f));
                pieces.Add(p.Clone());
            }
            Finish(rb, s.Mass);
            _bodies.Add(s.Id, new Body(s.Id, rb, 0f, 0f, pieces));
        }

        private Rigidbody2D NewBody(string name, float x, float y, float angle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.SetPositionAndRotation(ToMeters(x, y), Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg));
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.linearDamping = Tuning.LinearDamping;
            rb.angularDamping = Tuning.AngularDamping;
            rb.interpolation = RigidbodyInterpolation2D.None; // the server steps by hand; clients interpolate the sync
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            return rb;
        }

        /// <summary>Mass, then ground friction anchored at the centre of mass so it adds no torque of its own.</summary>
        private static void Finish(Rigidbody2D rb, float mass)
        {
            rb.useAutoMass = false;
            rb.mass = Tuning.RigidbodyMass(mass);
            var friction = rb.gameObject.AddComponent<FrictionJoint2D>();
            friction.connectedBody = null; // the world: top-down dry friction against the ground
            friction.autoConfigureConnectedAnchor = true;
            friction.anchor = rb.centerOfMass;
            friction.maxForce = Tuning.FrictionForce(mass);
            friction.maxTorque = Tuning.FrictionTorque(mass);
            friction.enableCollision = false;
        }

        private Body Find(string id)
        {
            ThrowIfDisposed();
            if (id != null && _bodies.TryGetValue(id, out var body)) return body;
            throw new KeyNotFoundException("No body '" + id + "' in this world.");
        }

        private void ThrowIfDisposed()
        {
            if (_root == null) throw new ObjectDisposedException(nameof(PhysicsWorld));
        }

        private static Vector2 ToMeters(float x, float y) => new Vector2(WorldUnits.ToMeters(x), WorldUnits.ToMeters(y));

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private sealed class Body
        {
            public readonly string Id;
            public readonly Rigidbody2D Rigidbody;
            public readonly float W;
            public readonly float H;
            /// <summary>A shape's pieces; null for a block.</summary>
            public readonly List<ShapePiece> Pieces;

            public Body(string id, Rigidbody2D rigidbody, float w, float h, List<ShapePiece> pieces)
            {
                Id = id;
                Rigidbody = rigidbody;
                W = w;
                H = h;
                Pieces = pieces;
            }
        }

        private sealed class Held
        {
            public readonly Body Body;
            public readonly TargetJoint2D Joint;
            public readonly float AnchorX;
            public readonly float AnchorY;

            public Held(Body body, TargetJoint2D joint, float anchorX, float anchorY)
            {
                Body = body;
                Joint = joint;
                AnchorX = anchorX;
                AnchorY = anchorY;
            }
        }
    }
}
