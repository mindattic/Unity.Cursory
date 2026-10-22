using System.Collections.Generic;
using TMPro;
using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Draws the shared world on a peer that shows the game (UC-T11, Cursory's world renderer plus its overlay): the
    /// current level's goal pads (green once solved), walls, circuit parts and signposts as flat shapes, a coloured
    /// box with its Mass on every replicated block and shape piece, the wires, every cursor as an arrow in its
    /// player's colour with a name tag, and whistle ripples. It only reads replicated state (BIBLE UC-LAW-1).
    /// </summary>
    /// <remarks>
    /// Positions are world pixels mapped to metres through <see cref="WorldUnits"/> (UC-LAW-3). Static parts are rebuilt
    /// when the level number changes; body visuals are children of each replicated body, so they move with its
    /// interpolated pose. Sorting orders: goals 0, walls 1, circuit 2, bodies 3, wires 4, labels 5, ripples 6, cursors 7.
    /// </remarks>
    public sealed class WorldView : MonoBehaviour
    {
        /// <summary>How long a whistle ripple lives, in seconds (Cursory's 700 ms).</summary>
        public const float RippleSeconds = 0.7f;

        /// <summary>A free cursor's resting tip direction: up and to the left, like a classic pointer.</summary>
        public const float RestingCursorDegrees = 135f;

        /// <summary>How opaque a player's pointer ghost is (UC-T33): the cursor is the real thing, the pointer a hint.</summary>
        public const float GhostAlpha = 0.3f;

        /// <summary>The ghost shows only once the pointer is this far (world pixels) from the cursor.</summary>
        public const float GhostShowsPastPx = 6f;

        private static Sprite s_Square;
        private static Material s_LineMaterial;

        private readonly Dictionary<CursorAvatar, CursorGlyph> _cursors = new Dictionary<CursorAvatar, CursorGlyph>();
        private readonly List<SpriteRenderer> _goals = new List<SpriteRenderer>();
        private readonly List<LineRenderer> _wires = new List<LineRenderer>();
        private readonly List<Ripple> _ripples = new List<Ripple>();
        private readonly List<CursorAvatar> _gone = new List<CursorAvatar>();
        private Transform _static;
        private SpriteRenderer _bulb;
        private int _builtLevel;

        /// <summary>The level whose static parts are drawn (0 for none).</summary>
        public int BuiltLevel => _builtLevel;

        /// <summary>Ripples currently showing.</summary>
        public int RippleCount => _ripples.Count;

        /// <summary>Cursors currently drawn.</summary>
        public int CursorCount => _cursors.Count;

        /// <summary>
        /// Brings the picture up to date: <paramref name="level"/> (number <paramref name="levelNumber"/>) with
        /// <paramref name="state"/>'s goals, bulb and wires, the replicated <paramref name="bodies"/>, and
        /// <paramref name="cursors"/>, each at its physical body with a faint ghost at its pointer (UC-T33); the local
        /// one's (<paramref name="local"/>) ghost sits at <paramref name="localPointer"/> when given, your hand at once.
        /// </summary>
        public void Render(LevelData level, int levelNumber, LevelStateSync state, IReadOnlyDictionary<string, Transform> bodies,
            IReadOnlyList<CursorAvatar> cursors, CursorAvatar local, Vector2? localPointer)
        {
            if (levelNumber != _builtLevel) BuildStatic(level, levelNumber);
            UpdateGoalsAndCircuit(level, state);
            UpdateBodies(level, bodies);
            UpdateCursors(cursors, local, localPointer);
            UpdateRipples();
        }

        /// <summary>Starts a ripple at (<paramref name="worldPx"/>) in <paramref name="color"/>.</summary>
        public void AddRipple(Vector2 worldPx, Color color)
        {
            var go = new GameObject("Whistle Ripple");
            go.transform.SetParent(transform, false);
            LineRenderer line = NewLine(go, 3f, 6);
            line.loop = true;
            line.positionCount = 32;
            _ripples.Add(new Ripple { Line = line, Center = worldPx, Color = color, Born = Time.unscaledTime });
            UpdateRipples();
        }

        // --- static geometry --------------------------------------------------------------------------------------

        private void BuildStatic(LevelData level, int number)
        {
            if (_static != null) Destroy(_static.gameObject);
            _goals.Clear();
            _wires.Clear();
            _bulb = null;
            _builtLevel = number;
            _static = new GameObject("Level " + number).transform;
            _static.SetParent(transform, false);
            if (level == null) return;

            foreach (BlockGoal g in level.BlockGoals) _goals.Add(Box(_static, "Goal " + g.Id, g.X, g.Y, g.W, g.H, 0f, new Color(0.5f, 0.47f, 0.87f, 0.25f), 0));
            foreach (ShapeGoal g in level.ShapeGoals) _goals.Add(Box(_static, "Goal " + g.Id, g.X, g.Y, g.W, g.H, 0f, new Color(0.85f, 0.35f, 0.19f, 0.22f), 0));
            foreach (Wall w in level.Walls) Box(_static, "Wall " + w.Id, w.X, w.Y, w.W, w.H, 0f, new Color(0.42f, 0.44f, 0.48f), 1);
            foreach (CircuitComponent c in level.Components)
            {
                SpriteRenderer part = Box(_static, c.Kind + " " + c.Id, c.X, c.Y, c.W, c.H, 0f, new Color(0.22f, 0.24f, 0.28f), 2);
                if (c.Kind == ComponentKind.Bulb) _bulb = part;
                Text(_static, c.Label, c.X, c.Y, 20f, new Color(1f, 1f, 1f, 0.8f), 5);
            }

            foreach (Terminal t in level.Terminals) Box(_static, "Terminal " + t.Id, t.X, t.Y, 18f, 18f, 0f, new Color(0.85f, 0.85f, 0.85f), 2);
            foreach (Wire wire in level.Wires)
            {
                var go = new GameObject("Wire " + wire.Id);
                go.transform.SetParent(_static, false);
                LineRenderer line = NewLine(go, 6f, 4);
                line.positionCount = 2;
                Color color = Hex(wire.Color, new Color(0.79f, 0.64f, 0.45f));
                line.startColor = line.endColor = color;
                _wires.Add(line);
            }

            foreach (WorldLabel l in level.Labels)
            {
                Text(_static, l.Title, l.X, l.Y, 36f, new Color(1f, 1f, 1f, 0.85f), 5);
                Text(_static, l.Subtitle, l.X, l.Y - 46f, 18f, new Color(1f, 1f, 1f, 0.45f), 5);
            }
        }

        private void UpdateGoalsAndCircuit(LevelData level, LevelStateSync state)
        {
            bool known = state != null && state.IsSpawned && state.LevelNumber == _builtLevel;
            for (int i = 0; i < _goals.Count; i++)
            {
                if (_goals[i] == null) continue;
                bool solved = known && state.IsGoalSolved(i);
                Color open = i < (level != null ? level.BlockGoals.Count : 0) ? new Color(0.5f, 0.47f, 0.87f, 0.25f) : new Color(0.85f, 0.35f, 0.19f, 0.22f);
                _goals[i].color = solved ? new Color(0.11f, 0.62f, 0.46f, 0.55f) : open;
            }

            if (_bulb != null) _bulb.color = known && state.BulbLit ? new Color(1f, 0.85f, 0.3f) : new Color(0.22f, 0.24f, 0.28f);

            if (level == null) return;
            Vector4[] ends = known ? state.WireEnds : null;
            for (int i = 0; i < _wires.Count && i < level.Wires.Count; i++)
            {
                Wire w = level.Wires[i];
                Vector4 e = ends != null && i < ends.Length ? ends[i] : new Vector4(w.Ax, w.Ay, w.Bx, w.By);
                _wires[i].SetPosition(0, Meters(e.x, e.y, 0f));
                _wires[i].SetPosition(1, Meters(e.z, e.w, 0f));
            }
        }

        // --- bodies -----------------------------------------------------------------------------------------------

        private void UpdateBodies(LevelData level, IReadOnlyDictionary<string, Transform> bodies)
        {
            if (level == null || bodies == null) return;
            foreach (Block b in level.Blocks)
            {
                if (!bodies.TryGetValue(b.Id, out Transform t) || t == null || t.Find(VisualName) != null) continue;
                Transform visual = NewChild(t, VisualName);
                LocalBox(visual, 0f, 0f, b.W, b.H, Hex(b.Color, Color.gray), 3);
                LocalText(visual, Mass(b.Mass), Mathf.Max(22f, Mathf.Min(b.W, b.H) * 0.3f));
            }

            foreach (Shape s in level.Shapes)
            {
                if (!bodies.TryGetValue(s.Id, out Transform t) || t == null || t.Find(VisualName) != null) continue;
                Transform visual = NewChild(t, VisualName);
                Color color = Hex(s.Color, new Color(0.5f, 0.47f, 0.87f));
                foreach (ShapePiece p in s.Pieces) LocalBox(visual, p.LocalX, p.LocalY, p.HalfW * 2f, p.HalfH * 2f, color, 3);
                LocalText(visual, Mass(s.Mass), 34f);
            }
        }

        private const string VisualName = "Cursory Visual";

        private static string Mass(float mass) => mass.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        // --- cursors ----------------------------------------------------------------------------------------------

        private void UpdateCursors(IReadOnlyList<CursorAvatar> cursors, CursorAvatar local, Vector2? localPointer)
        {
            _gone.Clear();
            foreach (CursorAvatar known in _cursors.Keys)
            {
                bool alive = known != null && known.IsSpawned;
                if (alive && cursors != null)
                {
                    alive = false;
                    for (int i = 0; i < cursors.Count; i++) alive |= cursors[i] == known;
                }

                if (!alive) _gone.Add(known);
            }

            foreach (CursorAvatar dead in _gone)
            {
                if (_cursors[dead].Root != null) Destroy(_cursors[dead].Root.gameObject);
                if (_cursors[dead].Ghost != null) Destroy(_cursors[dead].Ghost.gameObject);
                _cursors.Remove(dead);
            }

            if (cursors == null) return;
            foreach (CursorAvatar avatar in cursors)
            {
                if (avatar == null || !avatar.IsSpawned) continue;
                if (!_cursors.TryGetValue(avatar, out CursorGlyph glyph))
                {
                    glyph = NewCursor(avatar);
                    _cursors[avatar] = glyph;
                }

                // UC-T33: the arrow is the real cursor, the server's body; the pointer (your own hand, at once, or another
                // player's synced one) is a faint ghost of it, apart from the arrow only while a rope holds it back.
                Vector2 at = avatar.Position;
                Vector2 pointer = avatar == local && localPointer.HasValue ? localPointer.Value : avatar.Pointer;
                float degrees = RestingCursorDegrees;
                TetherSync tether = avatar.Tether;
                if (tether.Count > 0)
                {
                    Vector2 toward = tether.Contact - at;
                    if (toward.sqrMagnitude > 1f) degrees = Mathf.Atan2(toward.y, toward.x) * Mathf.Rad2Deg;
                }

                glyph.Root.position = Meters(at.x, at.y, 0f);
                glyph.Arrow.localRotation = Quaternion.Euler(0f, 0f, degrees);
                glyph.Ghost.position = Meters(pointer.x, pointer.y, 0f);
                glyph.Ghost.gameObject.SetActive((pointer - at).sqrMagnitude > GhostShowsPastPx * GhostShowsPastPx);
                Color color = avatar.PlayerColor;
                if (glyph.Color != color)
                {
                    glyph.Color = color;
                    glyph.Mesh.colors = new[] { color, color, color, color };
                    Color faint = color;
                    faint.a = GhostAlpha;
                    glyph.GhostMesh.colors = new[] { faint, faint, faint, faint };
                    glyph.Name.color = color;
                }

                string name = avatar.PlayerName;
                if (glyph.Name.text != name) glyph.Name.text = name;
            }
        }

        private CursorGlyph NewCursor(CursorAvatar avatar)
        {
            Transform root = NewChild(transform, "Cursor " + avatar.OwnerClientId);
            Transform arrow = NewChild(root, "Arrow");
            Mesh mesh = ArrowMesh("Cursor Arrow");
            arrow.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = arrow.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = LineMaterial;
            renderer.sortingOrder = 7;
            TextMeshPro name = LocalTextAt(root, "", 12f, new Vector2(19f, -40f), 7, TextAlignmentOptions.TopLeft);

            Transform ghost = NewChild(transform, "Pointer " + avatar.OwnerClientId);
            Transform ghostArrow = NewChild(ghost, "Ghost");
            ghostArrow.localRotation = Quaternion.Euler(0f, 0f, RestingCursorDegrees);
            Mesh ghostMesh = ArrowMesh("Pointer Ghost");
            ghostArrow.gameObject.AddComponent<MeshFilter>().sharedMesh = ghostMesh;
            var ghostRenderer = ghostArrow.gameObject.AddComponent<MeshRenderer>();
            ghostRenderer.sharedMaterial = LineMaterial;
            ghostRenderer.sortingOrder = 7;
            ghost.gameObject.SetActive(false);
            return new CursorGlyph { Root = root, Arrow = arrow, Mesh = mesh, Name = name, Ghost = ghost, GhostMesh = ghostMesh, Color = Color.clear };
        }

        /// <summary>Cursory's arrow: tip at the origin pointing +x, body trailing behind (its drawArrow), in metres.</summary>
        private static Mesh ArrowMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Meters(-24f, -8f, 0f), Meters(-16f, 0f, 0f), Meters(-24f, 8f, 0f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            return mesh;
        }

        // --- ripples ----------------------------------------------------------------------------------------------

        private void UpdateRipples()
        {
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                Ripple r = _ripples[i];
                float t = (Time.unscaledTime - r.Born) / RippleSeconds;
                if (t >= 1f || r.Line == null)
                {
                    if (r.Line != null) Destroy(r.Line.gameObject);
                    _ripples.RemoveAt(i);
                    continue;
                }

                float radius = 20f + t * 120f;
                Color c = r.Color;
                c.a = 0.7f * (1f - t);
                r.Line.startColor = r.Line.endColor = c;
                for (int k = 0; k < r.Line.positionCount; k++)
                {
                    float a = k * Mathf.PI * 2f / r.Line.positionCount;
                    r.Line.SetPosition(k, Meters(r.Center.x + Mathf.Cos(a) * radius, r.Center.y + Mathf.Sin(a) * radius, 0f));
                }
            }
        }

        private void OnDestroy()
        {
            foreach (CursorGlyph glyph in _cursors.Values)
            {
                if (glyph.Mesh != null) Destroy(glyph.Mesh);
                if (glyph.GhostMesh != null) Destroy(glyph.GhostMesh);
            }
        }

        // --- helpers ----------------------------------------------------------------------------------------------

        private static Sprite Square
        {
            get
            {
                if (s_Square != null) return s_Square;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Cursory Square", filterMode = FilterMode.Point };
                var white = new Color32[16];
                for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(white);
                tex.Apply(false, true);
                s_Square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                s_Square.name = "Cursory Square";
                return s_Square;
            }
        }

        private static Material LineMaterial
        {
            get
            {
                if (s_LineMaterial != null) return s_LineMaterial;
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                s_LineMaterial = new Material(shader) { name = "Cursory Unlit" };
                return s_LineMaterial;
            }
        }

        private static Vector3 Meters(float x, float y, float z) => new Vector3(WorldUnits.ToMeters(x), WorldUnits.ToMeters(y), z);

        private static Transform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static SpriteRenderer Box(Transform parent, string name, float x, float y, float w, float h, float angle, Color color, int order)
        {
            Transform t = NewChild(parent, name);
            t.position = Meters(x, y, 0f);
            t.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
            t.localScale = new Vector3(WorldUnits.ToMeters(w), WorldUnits.ToMeters(h), 1f);
            var sr = t.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Square;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        private static void LocalBox(Transform parent, float x, float y, float w, float h, Color color, int order)
        {
            Transform t = NewChild(parent, "Piece");
            t.localPosition = Meters(x, y, 0f);
            t.localScale = new Vector3(WorldUnits.ToMeters(w), WorldUnits.ToMeters(h), 1f);
            var sr = t.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Square;
            sr.color = color;
            sr.sortingOrder = order;
        }

        /// <summary>Text centred on world point (<paramref name="x"/>, <paramref name="y"/>), <paramref name="sizePx"/> world pixels tall.</summary>
        private static void Text(Transform parent, string text, float x, float y, float sizePx, Color color, int order)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextMeshPro tmp = LocalTextAt(parent, text, sizePx, Vector2.zero, order, TextAlignmentOptions.Center);
            tmp.transform.position = Meters(x, y, 0f);
            tmp.color = color;
        }

        private static void LocalText(Transform parent, string text, float sizePx)
        {
            TextMeshPro tmp = LocalTextAt(parent, text, sizePx, Vector2.zero, 4, TextAlignmentOptions.Center);
            tmp.color = new Color(1f, 1f, 1f, 0.92f);
            tmp.fontStyle = FontStyles.Bold;
        }

        private static TextMeshPro LocalTextAt(Transform parent, string text, float sizePx, Vector2 offsetPx, int order, TextAlignmentOptions alignment)
        {
            Transform t = NewChild(parent, "Text");
            t.localPosition = Meters(offsetPx.x, offsetPx.y, 0f);
            var tmp = t.gameObject.AddComponent<TextMeshPro>();
            tmp.text = text;
            // TMP's world text is 10 units of fontSize per metre of line height; size the line in world pixels.
            tmp.fontSize = WorldUnits.ToMeters(sizePx) * 10f;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(WorldUnits.ToMeters(2000f), WorldUnits.ToMeters(sizePx * 2f));
            tmp.rectTransform.pivot = alignment == TextAlignmentOptions.TopLeft ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f);
            tmp.sortingOrder = order;
            return tmp;
        }

        private static LineRenderer NewLine(GameObject go, float widthPx, int order)
        {
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = WorldUnits.ToMeters(widthPx);
            line.numCapVertices = 2;
            line.sharedMaterial = LineMaterial;
            line.sortingOrder = order;
            return line;
        }

        private static Color Hex(string hex, Color fallback) =>
            HexColor.TryParse(hex, out byte r, out byte g, out byte b) ? new Color32(r, g, b, 255) : fallback;

        private sealed class CursorGlyph
        {
            public Transform Root;
            public Transform Arrow;
            public Mesh Mesh;
            public TextMeshPro Name;
            public Transform Ghost;
            public Mesh GhostMesh;
            public Color Color;
        }

        private sealed class Ripple
        {
            public LineRenderer Line;
            public Vector2 Center;
            public Color Color;
            public float Born;
        }
    }
}
