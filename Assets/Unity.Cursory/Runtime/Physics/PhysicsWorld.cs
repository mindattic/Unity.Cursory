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
    /// The mapping from Cursory's Aether world (Unity.Multiplayer RFC 0001): grab maxForce
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
        private readonly Dictionary<string, Wall> _walls = new Dictionary<string, Wall>();
        private readonly IBodySync _bodySync;
        private GameObject _root;

        /// <summary>
        /// Builds the bodies and walls of <paramref name="level"/> under a new root object. When
        /// <paramref name="bodySync"/> is given (UC-T7, a running server session), every block and shape is
        /// replicated to every client through it (BIBLE UC-LAW-7); null (the default, and every UC-T6 caller)
        /// builds plain, unnetworked bodies, as every test that constructs a <see cref="PhysicsWorld"/> directly
        /// still does.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="level"/> is null.</exception>
        public PhysicsWorld(LevelData level, string name = "PhysicsWorld", IBodySync bodySync = null)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            _bodySync = bodySync;
            _root = new GameObject(name);
            foreach (var w in level.Walls) AddWall(w);
            foreach (var b in level.Blocks) AddBlock(b);
            foreach (var s in level.Shapes) AddShape(s);
            Physics2D.SyncTransforms();
        }

        /// <summary>How this world's bodies replicate; null for plain, unnetworked bodies. A rebuilt world reuses it.</summary>
        public IBodySync BodySync => _bodySync;

        /// <summary>True once <see cref="Dispose"/> has run.</summary>
        public bool IsDisposed => _root == null;

        /// <summary>The ids of every movable body (blocks and shapes).</summary>
        public IEnumerable<string> BodyIds => _bodies.Keys;

        /// <summary>True when <paramref name="id"/> names a compound shape rather than a block.</summary>
        /// <exception cref="KeyNotFoundException">No body has that id.</exception>
        public bool IsShape(string id) => Find(id).Pieces != null;

        /// <summary>True when <paramref name="id"/> is a wall of this world.</summary>
        public bool HasWall(string id) => id != null && _walls.ContainsKey(id);

        /// <summary>
        /// Where a grab on wall <paramref name="id"/> lands: the point on its rim nearest the click (UC-LAW-5), in
        /// body-local world pixels (as <see cref="TryGetGrab"/> returns for a block or shape). A wall never moves,
        /// so this is bookkeeping only (<see cref="CursorAvatar.GrabWall"/>); nothing in <see cref="PhysicsWorld"/>
        /// changes.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No wall has that id.</exception>
        public (float X, float Y) WallAnchor(string id, float clickX, float clickY)
        {
            Wall wall;
            if (id == null || !_walls.TryGetValue(id, out wall))
            {
                throw new KeyNotFoundException("No wall '" + id + "' in this world.");
            }

            return GrabGeometry.BlockAnchor(wall.W, wall.H, wall.X, wall.Y, 0f, clickX, clickY);
        }

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
            _grabs[cursor] = new Held(body, joint, anchor.X, anchor.Y) { TargetX = clickX, TargetY = clickY };
            return true;
        }

        /// <summary>Moves <paramref name="cursor"/>'s grab target to (<paramref name="x"/>, <paramref name="y"/>) world pixels. False when it holds nothing.</summary>
        public bool MoveCursor(ulong cursor, float x, float y)
        {
            ThrowIfDisposed();
            if (!IsFinite(x) || !IsFinite(y)) return false;
            if (!_grabs.TryGetValue(cursor, out var grab)) return false;
            grab.Joint.target = ToMeters(x, y);
            grab.TargetX = x;
            grab.TargetY = y;
            return true;
        }

        /// <summary>
        /// Updates <paramref name="cursor"/>'s rope (UC-T8) from the body's current pose and the cursor's current
        /// target, and fills <paramref name="chain"/> (when given) with it in world pixels: the rim anchor, then every
        /// corner the rope has caught. With <paramref name="segmented"/> the rope wraps the body's corners
        /// (<see cref="TetherWrap"/>) and the grab pulls on the last one it caught, so pulling round a corner turns the
        /// body; without, the rope is straight and the grab pulls on its rim anchor, as before. The last point of
        /// <paramref name="chain"/> is where the grab pulls. False (and an empty chain) when the cursor holds nothing.
        /// </summary>
        public bool UpdateTether(ulong cursor, bool segmented, List<(float X, float Y)> chain = null)
        {
            ThrowIfDisposed();
            chain?.Clear();
            if (!_grabs.TryGetValue(cursor, out var grab)) return false;

            var body = grab.Body;
            var (bx, by, angle) = Pose(body.Id);
            if (segmented)
            {
                var (lx, ly) = GrabGeometry.ToLocal(bx, by, angle, grab.TargetX, grab.TargetY);
                grab.Wrap.Update(grab.AnchorX, grab.AnchorY, body.Corners, body.Rects, lx, ly);
            }
            else
            {
                grab.Wrap.Clear();
            }

            var contact = grab.Wrap.Contact(grab.AnchorX, grab.AnchorY, body.Corners);
            var anchor = ToMeters(contact.X, contact.Y);
            if (grab.Joint != null && grab.Joint.anchor != anchor) grab.Joint.anchor = anchor;

            if (chain != null)
            {
                grab.Wrap.Chain(grab.AnchorX, grab.AnchorY, body.Corners, chain);
                for (var i = 0; i < chain.Count; i++)
                {
                    chain[i] = GrabGeometry.ToWorld(bx, by, angle, chain[i].X, chain[i].Y);
                }
            }

            return true;
        }

        /// <summary>
        /// Optional cursor collision (UC-T8): (<paramref name="x"/>, <paramref name="y"/>) pushed out of every wall and
        /// every compound shape (at its current pose) by the cursor disc's <paramref name="radius"/>
        /// (<see cref="CursorCollision"/>). Blocks are not solid to the cursor, as in Cursory.
        /// </summary>
        public (float X, float Y) ResolveCursor(float x, float y, float radius = Tuning.CursorRadius)
        {
            ThrowIfDisposed();
            if (!IsFinite(x) || !IsFinite(y)) return (x, y);
            (x, y) = CursorCollision.OutOfWalls(_walls.Values, x, y, radius);
            foreach (var body in _bodies.Values)
            {
                if (body.Pieces == null) continue;
                var (bx, by, angle) = Pose(body.Id);
                (x, y) = CursorCollision.OutOfShape(body.Pieces, bx, by, angle, x, y, radius);
            }

            return (x, y);
        }

        /// <summary>The centre of wall <paramref name="id"/> in world pixels (a <see cref="WallAnchor"/> is measured from it).</summary>
        /// <exception cref="KeyNotFoundException">No wall has that id.</exception>
        public (float X, float Y) WallCentre(string id)
        {
            Wall wall;
            if (id == null || !_walls.TryGetValue(id, out wall))
            {
                throw new KeyNotFoundException("No wall '" + id + "' in this world.");
            }

            return (wall.X, wall.Y);
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

        /// <summary>
        /// Destroys every body, wall and grab. Their rigid bodies leave the simulation at once (a Unity object is
        /// only destroyed at the end of the frame), so a world built in their place the same tick never meets them.
        /// </summary>
        public void Dispose()
        {
            if (_root == null) return;
            foreach (var body in _bodies.Values)
            {
                if (body.Rigidbody == null) continue;
                Retire(body.Rigidbody.gameObject);
                // A networked body is despawned now, not at the end of the frame, so its key (its block/shape id) is free
                // for a world rebuilt in its place this tick; it may not sit under the root, so it goes by itself.
                if (_bodySync != null) _bodySync.DespawnBody(body.Rigidbody.gameObject);
                else if (!body.Rigidbody.transform.IsChildOf(_root.transform)) DestroyObject(body.Rigidbody.gameObject);
            }
            Retire(_root);
            _grabs.Clear();
            _bodies.Clear();
            _walls.Clear();
            DestroyObject(_root);
            _root = null;
        }

        /// <summary>Takes <paramref name="go"/>'s colliders, joints and rigid bodies out of the simulation now.</summary>
        private static void Retire(GameObject go)
        {
            foreach (var joint in go.GetComponentsInChildren<Joint2D>(true)) joint.enabled = false;
            foreach (var col in go.GetComponentsInChildren<Collider2D>(true)) col.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody2D>(true)) rb.simulated = false;
        }

        private static void DestroyObject(GameObject go)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
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
            _walls[w.Id] = w.Clone();
        }

        private void AddBlock(Block b)
        {
            var rb = NewBody(b.Id, "Block " + b.Id, b.X, b.Y, b.Angle);
            var box = rb.gameObject.AddComponent<BoxCollider2D>();
            box.size = new Vector2(WorldUnits.ToMeters(b.W), WorldUnits.ToMeters(b.H));
            Finish(rb, b.Mass);
            _bodies.Add(b.Id, new Body(b.Id, rb, b.W, b.H, null));
        }

        private void AddShape(Shape s)
        {
            var rb = NewBody(s.Id, "Shape " + s.Id, s.X, s.Y, s.Angle);
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

        private Rigidbody2D NewBody(string id, string name, float x, float y, float angle)
        {
            Vector2 position = ToMeters(x, y);
            Quaternion rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
            GameObject go;
            if (_bodySync != null)
            {
                // The returned object already carries a Rigidbody2D (and is spawned, replicated to every client);
                // everything below just configures it, the same as the unnetworked path.
                go = _bodySync.SpawnBody(id, name, position, rotation, _root.transform);
            }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(_root.transform, false);
                go.transform.SetPositionAndRotation(position, rotation);
            }

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null) rb = go.AddComponent<Rigidbody2D>();
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
            /// <summary>The corners and boxes a tether wraps, in the body's own frame (UC-T8).</summary>
            public readonly IReadOnlyList<(float X, float Y)> Corners;
            public readonly IReadOnlyList<BodyRect> Rects;

            public Body(string id, Rigidbody2D rigidbody, float w, float h, List<ShapePiece> pieces)
            {
                Id = id;
                Rigidbody = rigidbody;
                W = w;
                H = h;
                Pieces = pieces;
                Corners = pieces != null ? TetherGeometry.ShapeCorners(pieces) : TetherGeometry.BlockCorners(w, h);
                Rects = pieces != null ? TetherGeometry.ShapeRects(pieces) : TetherGeometry.BlockRects(w, h);
            }
        }

        private sealed class Held
        {
            public readonly Body Body;
            public readonly TargetJoint2D Joint;
            public readonly float AnchorX;
            public readonly float AnchorY;
            /// <summary>Where the cursor pulls, in world pixels (the joint's target, unconverted).</summary>
            public float TargetX;
            public float TargetY;
            /// <summary>The corners this grab's rope has caught (UC-T8).</summary>
            public readonly TetherWrap Wrap = new TetherWrap();

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
