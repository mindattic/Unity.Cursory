using System;
using System.Collections.Generic;
using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// The server's Physics2D world for one level (BIBLE UC-LAW-1, UC-LAW-2): every block and shape of a
    /// <see cref="LevelData"/> as a dynamic <see cref="Rigidbody2D"/> held to the ground by a
    /// <see cref="FrictionJoint2D"/>, every wall as a static collider, every player's cursor as a light dynamic body
    /// pulled toward their pointer by a force-capped <see cref="TargetJoint2D"/>, and every grab as a rope: a
    /// max-length <see cref="DistanceJoint2D"/> from the cursor to the body's rim (UC-LAW-5), re-anchored at the body's
    /// corners as it wraps (UC-T33). It speaks world pixels and converts to metres only here, through
    /// <see cref="WorldUnits"/> (UC-LAW-3).
    /// </summary>
    /// <remarks>
    /// <para>Bodies (the mapping from Cursory's Aether world, Unity.Multiplayer RFC 0001): ground friction to the world
    /// with maxForce <c>Mass Ã— 40</c> and maxTorque <c>Ã— 1.2</c>; <c>rb.mass = Mass Ã— 5</c>, gravity 0, linear damping
    /// 1.5, angular 2.0.</para>
    /// <para>Cursors (UC-T33): mass <see cref="Tuning.CursorMassKg"/>, linear damping <see cref="Tuning.CursorLinearDamping"/>,
    /// no collider, rotation frozen; the spring toward the pointer is a <see cref="TargetJoint2D"/> with maxForce
    /// <see cref="Tuning.GrabMaxForce"/> (the player's pull strength), frequency <see cref="Tuning.CursorFrequency"/> and
    /// damping <see cref="Tuning.CursorDamping"/>. A free cursor follows the pointer like a cursor. A held one is tied by
    /// its rope: a <see cref="DistanceJoint2D"/> with <c>maxDistanceOnly</c>, from the cursor to the rope's contact (the
    /// rim anchor, or the last body corner it has caught, <see cref="TetherWrap"/>), whose distance is
    /// <see cref="Tuning.RopeLengthPx"/> less what the rope has used up round the corners. Slack, it pulls nothing; taut, it
    /// carries the spring's pull to the body and the body's weight back to the cursor, so a body moves exactly when the
    /// ropes on it sum past its Mass (UC-LAW-2), a load heavier than its pull drags the cursor toward it, and a rope pulled
    /// round a corner turns the body. After each step a cursor the solver let drift past its rope's end is put back on
    /// it, so a rope never stretches past its length.</para>
    /// <para>The world never steps itself: <see cref="Step"/> is called once per server tick by
    /// <see cref="ServerSimulation"/>, with <see cref="Physics2D.simulationMode"/> set to Script.</para>
    /// </remarks>
    public sealed class PhysicsWorld : IDisposable
    {
        private readonly Dictionary<string, Body> _bodies = new Dictionary<string, Body>();
        private readonly Dictionary<ulong, PlayerCursor> _cursors = new Dictionary<ulong, PlayerCursor>();
        private readonly Dictionary<string, Wall> _walls = new Dictionary<string, Wall>();
        private readonly List<(float X, float Y)> _scratch = new List<(float X, float Y)>();
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
        /// wall-local world pixels, from its centre (as <see cref="TryGetGrab"/> returns a block or shape's). Nothing
        /// changes; <see cref="GrabWall"/> ties a rope there.
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

        /// <summary>The cursors holding a block or shape right now (a wall rope is not a grab of a body).</summary>
        public IEnumerable<ulong> Grabbers
        {
            get
            {
                foreach (var pair in _cursors)
                {
                    if (pair.Value.Rope != null && pair.Value.Rope.Body != null) yield return pair.Key;
                }
            }
        }

        /// <summary>Every cursor with a body in this world.</summary>
        public IEnumerable<ulong> Cursors => _cursors.Keys;

        /// <summary>
        /// Advances the world by <paramref name="seconds"/> (one server tick: <see cref="SimulationRate.TickSeconds"/>):
        /// brings every rope's wrap and joint up to date, steps Physics2D, then wraps again for the new poses and puts any
        /// cursor the solver let drift past its rope's end back on it.
        /// </summary>
        public void Step(float seconds)
        {
            ThrowIfDisposed();
            RefreshRopes();
            Physics2D.Simulate(seconds);
            RefreshRopes();
            foreach (var cursor in _cursors.Values) KeepOnRope(cursor);
        }

        /// <summary>
        /// Re-wraps every rope round its body for where the cursors and bodies are now, and re-anchors its joint on the
        /// rope's contact with the length the corners leave free. <see cref="Step"/> does this; call it after moving a
        /// body or a cursor by hand.
        /// </summary>
        public void RefreshRopes()
        {
            ThrowIfDisposed();
            foreach (var cursor in _cursors.Values) RefreshRope(cursor);
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
        /// Points <paramref name="cursor"/>'s spring at (<paramref name="x"/>, <paramref name="y"/>) world pixels, the
        /// player's pointer: the cursor's body is pulled toward it with up to <see cref="Tuning.GrabMaxForce"/>. A cursor
        /// with no body in this world yet gets one there. False (and nothing changes) for a point that isn't finite.
        /// </summary>
        public bool MoveCursor(ulong cursor, float x, float y)
        {
            ThrowIfDisposed();
            if (!IsFinite(x) || !IsFinite(y)) return false;
            var c = EnsureCursor(cursor, x, y);
            c.PointerX = x;
            c.PointerY = y;
            c.Spring.target = ToMeters(x, y);
            return true;
        }

        /// <summary>True when <paramref name="cursor"/> has a body in this world.</summary>
        public bool HasCursor(ulong cursor) => _cursors.ContainsKey(cursor);

        /// <summary>Where <paramref name="cursor"/>'s body is, in world pixels. False when it has none in this world.</summary>
        public bool TryGetCursor(ulong cursor, out (float X, float Y) position)
        {
            if (_root != null && _cursors.TryGetValue(cursor, out var c))
            {
                position = CursorPx(c);
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>Where <paramref name="cursor"/>'s spring pulls it (the player's pointer), in world pixels. False when it has no body.</summary>
        public bool TryGetPointer(ulong cursor, out (float X, float Y) pointer)
        {
            if (_cursors.TryGetValue(cursor, out var c))
            {
                pointer = (c.PointerX, c.PointerY);
                return true;
            }

            pointer = default;
            return false;
        }

        /// <summary>The Physics2D body of <paramref name="cursor"/>, for tests; null when it has none in this world.</summary>
        public Rigidbody2D CursorBody(ulong cursor) => _cursors.TryGetValue(cursor, out var c) ? c.Rigidbody : null;

        /// <summary>Takes <paramref name="cursor"/>'s body (and any rope) out of this world, as when its player leaves. False when it had none.</summary>
        public bool RemoveCursor(ulong cursor)
        {
            if (!_cursors.TryGetValue(cursor, out var c)) return false;
            Release(cursor);
            _cursors.Remove(cursor);
            if (c.Rigidbody != null)
            {
                Retire(c.Rigidbody.gameObject);
                DestroyObject(c.Rigidbody.gameObject);
            }

            return true;
        }

        /// <summary>
        /// <paramref name="cursor"/> ties its rope to body <paramref name="id"/> at the point on its rim nearest the click
        /// (<paramref name="clickX"/>, <paramref name="clickY"/>, world pixels; UC-LAW-5), dropping any rope it already had.
        /// A cursor with no body yet gets one at the click. False (and no grab) when the body doesn't exist or the click
        /// isn't a finite point.
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

            var c = EnsureCursor(cursor, clickX, clickY);
            Release(cursor);
            c.Rope = new Rope(body, null, anchor.X, anchor.Y, NewRopeJoint(c, body.Rigidbody));
            RefreshRope(c);
            return true;
        }

        /// <summary>
        /// <paramref name="cursor"/> ties its rope to wall <paramref name="wallId"/> at the point on its rim nearest the
        /// click (UC-LAW-5): a wall never moves, so the rope only holds the cursor within its length of that point. False
        /// when there is no such wall or the click isn't finite.
        /// </summary>
        public bool GrabWall(ulong cursor, string wallId, float clickX, float clickY)
        {
            ThrowIfDisposed();
            if (!IsFinite(clickX) || !IsFinite(clickY) || !HasWall(wallId)) return false;
            var (ax, ay) = WallAnchor(wallId, clickX, clickY);
            var c = EnsureCursor(cursor, clickX, clickY);
            Release(cursor);
            c.Rope = new Rope(null, wallId, ax, ay, NewRopeJoint(c, null));
            RefreshRope(c);
            return true;
        }

        /// <summary>
        /// <paramref name="cursor"/>'s rope in world pixels, into <paramref name="chain"/> (cleared first): the rim anchor,
        /// then every body corner it has caught; the last point is the contact its joint pulls on, and the rope runs on from
        /// there to the cursor. False (and an empty chain) when the cursor has no rope.
        /// </summary>
        public bool TryGetRope(ulong cursor, List<(float X, float Y)> chain)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            ThrowIfDisposed();
            chain.Clear();
            if (!_cursors.TryGetValue(cursor, out var c) || c.Rope == null) return false;
            WorldChain(c.Rope, chain);
            return chain.Count > 0;
        }

        /// <summary>
        /// How long <paramref name="cursor"/>'s rope is now, in world pixels: along its chain round the caught corners,
        /// then on to the cursor. Never more than <see cref="Tuning.RopeLengthPx"/> after a <see cref="Step"/>. -1 when it
        /// has no rope.
        /// </summary>
        public float RopeLength(ulong cursor)
        {
            ThrowIfDisposed();
            if (!_cursors.TryGetValue(cursor, out var c) || c.Rope == null) return -1f;
            WorldChain(c.Rope, _scratch);
            var length = TetherGeometry.PathLength(_scratch);
            var contact = _scratch[_scratch.Count - 1];
            var at = CursorPx(c);
            return length + Distance(contact.X, contact.Y, at.X, at.Y);
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

        /// <summary>Unties <paramref name="cursor"/>'s rope; the cursor itself stays, free again. False when it held nothing.</summary>
        public bool Release(ulong cursor)
        {
            if (!_cursors.TryGetValue(cursor, out var c) || c.Rope == null) return false;
            var joint = c.Rope.Joint;
            c.Rope = null;
            if (joint != null)
            {
                // Destroy is deferred to the end of the frame; disabling takes the joint out of the very next step.
                joint.enabled = false;
                if (Application.isPlaying) UnityEngine.Object.Destroy(joint);
                else UnityEngine.Object.DestroyImmediate(joint);
            }

            return true;
        }

        /// <summary>
        /// What <paramref name="cursor"/> holds: the body id and the rope's rim anchor in body-local world pixels.
        /// False when it holds no block or shape (nothing, or a wall).
        /// </summary>
        public bool TryGetGrab(ulong cursor, out string bodyId, out (float X, float Y) anchor)
        {
            if (_cursors.TryGetValue(cursor, out var c) && c.Rope != null && c.Rope.Body != null)
            {
                bodyId = c.Rope.Body.Id;
                anchor = (c.Rope.AnchorX, c.Rope.AnchorY);
                return true;
            }
            bodyId = null;
            anchor = default;
            return false;
        }

        private PlayerCursor EnsureCursor(ulong id, float x, float y)
        {
            if (_cursors.TryGetValue(id, out var existing)) return existing;
            var go = new GameObject("Cursor " + id);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = ToMeters(x, y);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.useAutoMass = false;
            rb.mass = Tuning.CursorMassKg;
            rb.linearDamping = Tuning.CursorLinearDamping;
            rb.angularDamping = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.None;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            rb.position = ToMeters(x, y);

            var spring = go.AddComponent<TargetJoint2D>();
            spring.autoConfigureTarget = false;
            spring.anchor = Vector2.zero;
            spring.target = ToMeters(x, y);
            spring.maxForce = Tuning.GrabMaxForce;
            spring.frequency = Tuning.CursorFrequency;
            spring.dampingRatio = Tuning.CursorDamping;

            var cursor = new PlayerCursor(rb, spring) { PointerX = x, PointerY = y };
            _cursors[id] = cursor;
            return cursor;
        }

        private static DistanceJoint2D NewRopeJoint(PlayerCursor c, Rigidbody2D to)
        {
            var joint = c.Rigidbody.gameObject.AddComponent<DistanceJoint2D>();
            joint.autoConfigureConnectedAnchor = false;
            joint.autoConfigureDistance = false;
            joint.enableCollision = false;
            joint.maxDistanceOnly = true;
            joint.anchor = Vector2.zero;
            joint.connectedBody = to;   // null: the world, for a wall
            joint.distance = WorldUnits.ToMeters(Tuning.RopeLengthPx);
            return joint;
        }

        /// <summary>Re-wraps <paramref name="c"/>'s rope and points its joint at the contact, with what the corners leave free.</summary>
        private void RefreshRope(PlayerCursor c)
        {
            var rope = c.Rope;
            if (rope == null || rope.Joint == null) return;
            Vector2 connected;
            float free;
            if (rope.Body != null)
            {
                var body = rope.Body;
                var (bx, by, angle) = Pose(body.Id);
                var at = CursorPx(c);
                var (lx, ly) = GrabGeometry.ToLocal(bx, by, angle, at.X, at.Y);
                rope.Wrap.Update(rope.AnchorX, rope.AnchorY, body.Corners, body.Rects, lx, ly, Tuning.RopeLengthPx - TetherGeometry.MinFreeLengthPx);
                rope.Wrap.Chain(rope.AnchorX, rope.AnchorY, body.Corners, _scratch);
                var contact = _scratch[_scratch.Count - 1];
                connected = ToMeters(contact.X, contact.Y);   // body-local
                free = TetherGeometry.FreeLength(_scratch);
            }
            else
            {
                var (wx, wy) = WallCentre(rope.WallId);
                connected = ToMeters(wx + rope.AnchorX, wy + rope.AnchorY);   // the world
                free = Tuning.RopeLengthPx;
            }

            if (rope.Joint.connectedAnchor != connected) rope.Joint.connectedAnchor = connected;
            var distance = WorldUnits.ToMeters(free);
            if (!Mathf.Approximately(rope.Joint.distance, distance)) rope.Joint.distance = distance;
        }

        /// <summary>
        /// Puts <paramref name="c"/> back on its rope's end if the solver let it drift past (a joint is solved, not
        /// enforced), and drops the part of its velocity still carrying it outward.
        /// </summary>
        private void KeepOnRope(PlayerCursor c)
        {
            if (c.Rope == null || c.Rigidbody == null) return;
            WorldChain(c.Rope, _scratch);
            var free = c.Rope.Body != null ? TetherGeometry.FreeLength(_scratch) : Tuning.RopeLengthPx;
            var contact = _scratch[_scratch.Count - 1];
            var at = CursorPx(c);
            var (x, y) = TetherGeometry.Leash(contact.X, contact.Y, at.X, at.Y, free);
            if (x == at.X && y == at.Y) return;

            c.Rigidbody.position = ToMeters(x, y);
            var outward = new Vector2(x - contact.X, y - contact.Y).normalized;
            var v = c.Rigidbody.linearVelocity;
            var along = Vector2.Dot(v, outward);
            if (along > 0f) c.Rigidbody.linearVelocity = v - along * outward;
        }

        /// <summary>The rope's chain (anchor, then caught corners) in world pixels, into <paramref name="into"/>.</summary>
        private void WorldChain(Rope rope, List<(float X, float Y)> into)
        {
            if (rope.Body != null)
            {
                var (bx, by, angle) = Pose(rope.Body.Id);
                rope.Wrap.Chain(rope.AnchorX, rope.AnchorY, rope.Body.Corners, into);
                for (var i = 0; i < into.Count; i++)
                {
                    into[i] = GrabGeometry.ToWorld(bx, by, angle, into[i].X, into[i].Y);
                }
            }
            else
            {
                into.Clear();
                var (wx, wy) = WallCentre(rope.WallId);
                into.Add((wx + rope.AnchorX, wy + rope.AnchorY));
            }
        }

        private static (float X, float Y) CursorPx(PlayerCursor c)
        {
            var p = c.Rigidbody.position;
            return (WorldUnits.ToPixels(p.x), WorldUnits.ToPixels(p.y));
        }

        private static float Distance(float ax, float ay, float bx, float by) =>
            Mathf.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        /// <summary>
        /// Destroys every body, wall, cursor and rope. Their rigid bodies leave the simulation at once (a Unity object is
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
            _cursors.Clear();
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

        /// <summary>A player's cursor: its light body, the capped spring toward their pointer, and the rope it holds, if any.</summary>
        private sealed class PlayerCursor
        {
            public readonly Rigidbody2D Rigidbody;
            public readonly TargetJoint2D Spring;
            /// <summary>The player's pointer, in world pixels (the spring's target, unconverted).</summary>
            public float PointerX;
            public float PointerY;
            /// <summary>What it holds; null when free.</summary>
            public Rope Rope;

            public PlayerCursor(Rigidbody2D rigidbody, TargetJoint2D spring)
            {
                Rigidbody = rigidbody;
                Spring = spring;
            }
        }

        /// <summary>A rope from a cursor to a body's rim (or a wall's), and the corners it has caught (UC-T8's wrap, UC-T33's joint).</summary>
        private sealed class Rope
        {
            /// <summary>The body it is tied to; null for a wall.</summary>
            public readonly Body Body;
            /// <summary>The wall it is tied to; null for a body.</summary>
            public readonly string WallId;
            /// <summary>The rim anchor: body-local world pixels, or for a wall, from the wall's centre.</summary>
            public readonly float AnchorX;
            public readonly float AnchorY;
            public readonly DistanceJoint2D Joint;
            public readonly TetherWrap Wrap = new TetherWrap();

            public Rope(Body body, string wallId, float anchorX, float anchorY, DistanceJoint2D joint)
            {
                Body = body;
                WallId = wallId;
                AnchorX = anchorX;
                AnchorY = anchorY;
                Joint = joint;
            }
        }
    }
}
