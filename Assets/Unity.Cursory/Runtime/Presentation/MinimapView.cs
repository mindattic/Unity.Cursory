using System.Collections.Generic;
using MindAttic.Components.Unity;
using Unity.Cursory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.Cursory
{
    /// <summary>
    /// The minimap (UC-T11, Cursory's <c>drawMinimap</c>): the whole world in a MindAttic Components
    /// <see cref="MaPanel"/> at the bottom-right of the HUD, drawn into a small texture through Core's
    /// <see cref="MinimapTransform"/>: goals (green once solved), walls, blocks in their colours, shapes as dots, the
    /// camera's view as an outline and every cursor as a dot in its player's colour.
    /// </summary>
    public sealed class MinimapView
    {
        /// <summary>The map's size on the 1920x1080 HUD canvas (Cursory's 160 screen pixels, doubled like the package's metrics).</summary>
        public const float CanvasSize = 320f;

        /// <summary>Redraws per second.</summary>
        public const float RedrawRate = 15f;

        private static readonly Color32 Backdrop = new Color32(0, 0, 0, 128);
        private static readonly Color32 WallColor = new Color32(170, 170, 170, 204);
        private static readonly Color32 GoalOpen = new Color32(127, 119, 221, 128);
        private static readonly Color32 ShapeGoalOpen = new Color32(216, 90, 48, 102);
        private static readonly Color32 GoalSolved = new Color32(29, 158, 117, 204);
        private static readonly Color32 ViewColor = new Color32(255, 255, 255, 128);

        private readonly Color32[] _pixels;
        private float _nextDraw;

        private MinimapView(Canvas canvas)
        {
            Map = new MinimapTransform();
            int size = (int)MinimapTransform.DefaultSizePx;
            _pixels = new Color32[size * size];
            Texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Minimap" };

            Panel = MaPanel.Create(canvas, PanelStyle.Inset, "Minimap");
            RectTransform rt = Panel.RectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-24f, 24f);
            rt.sizeDelta = new Vector2(CanvasSize + 8f, CanvasSize + 8f);

            var imageGo = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(rt, false);
            var imageRt = (RectTransform)imageGo.transform;
            imageRt.anchorMin = imageRt.anchorMax = imageRt.pivot = new Vector2(0.5f, 0.5f);
            imageRt.sizeDelta = new Vector2(CanvasSize, CanvasSize);
            Image = imageGo.GetComponent<RawImage>();
            Image.texture = Texture;
            Image.raycastTarget = false;
        }

        /// <summary>The panel holding the map.</summary>
        public MaPanel Panel { get; }

        /// <summary>The map image.</summary>
        public RawImage Image { get; }

        /// <summary>The texture the map is drawn into (y up, like the world).</summary>
        public Texture2D Texture { get; }

        /// <summary>World pixels to map pixels.</summary>
        public MinimapTransform Map { get; }

        /// <summary>Times the map has been drawn.</summary>
        public int Draws { get; private set; }

        /// <summary>Adds a minimap to <paramref name="canvas"/>.</summary>
        public static MinimapView Create(Canvas canvas) => new MinimapView(canvas);

        /// <summary>Redraws at most <see cref="RedrawRate"/> times a second (<paramref name="force"/> redraws now).</summary>
        public void Draw(LevelData level, LevelStateSync state, IReadOnlyDictionary<string, Transform> bodies, IReadOnlyList<CursorAvatar> cursors,
            CameraView view, Vector2 screen, bool force = false)
        {
            if (!force && Time.unscaledTime < _nextDraw) return;
            _nextDraw = Time.unscaledTime + 1f / RedrawRate;

            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = Backdrop;
            if (level != null)
            {
                int goal = 0;
                foreach (BlockGoal g in level.BlockGoals) Fill(Map.Feature(g.X, g.Y, g.W, g.H), Solved(state, goal++) ? GoalSolved : GoalOpen);
                foreach (ShapeGoal g in level.ShapeGoals) Fill(Map.Feature(g.X, g.Y, g.W, g.H), Solved(state, goal++) ? GoalSolved : ShapeGoalOpen);
                foreach (Wall w in level.Walls) Fill(Map.Feature(w.X, w.Y, w.W, w.H), WallColor);
                foreach (Block b in level.Blocks)
                {
                    BodyPose p = PoseOf(bodies, b.Id, b.X, b.Y);
                    Fill(Map.Feature(p.X, p.Y, b.W, b.H), Hex(b.Color));
                }

                foreach (Shape s in level.Shapes)
                {
                    BodyPose p = PoseOf(bodies, s.Id, s.X, s.Y);
                    Dot(p.X, p.Y, 2, Hex(s.Color));
                }
            }

            if (view != null) Outline(Map.Viewport(view, screen.x, screen.y), ViewColor);
            if (cursors != null)
            {
                foreach (CursorAvatar c in cursors)
                {
                    if (c == null || !c.IsSpawned) continue;
                    Dot(c.Position.x, c.Position.y, 1, c.PlayerColor);
                }
            }

            Texture.SetPixels32(_pixels);
            Texture.Apply(false);
            Draws++;
        }

        /// <summary>Destroys the panel and the texture.</summary>
        public void Destroy()
        {
            if (Panel != null) Object.Destroy(Panel.gameObject);
            if (Texture != null) Object.Destroy(Texture);
        }

        private static bool Solved(LevelStateSync state, int index) => state != null && state.IsSpawned && state.IsGoalSolved(index);

        private static BodyPose PoseOf(IReadOnlyDictionary<string, Transform> bodies, string id, float x, float y) =>
            bodies != null && bodies.TryGetValue(id, out Transform t) && t != null ? ClientBodies.PoseOf(t) : new BodyPose(x, y, 0f);

        private static Color32 Hex(string hex) =>
            HexColor.TryParse(hex, out byte r, out byte g, out byte b) ? new Color32(r, g, b, 255) : new Color32(136, 136, 136, 255);

        private void Dot(float wx, float wy, int radius, Color32 color)
        {
            var (mx, my) = Map.ToMap(wx, wy);
            Fill(new MiniRect(mx - radius, my - radius, radius * 2 + 1, radius * 2 + 1), color);
        }

        private void Fill(MiniRect r, Color32 color)
        {
            int size = (int)Map.Width;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(r.X), 0, size), x1 = Mathf.Clamp(Mathf.CeilToInt(r.X + r.W), 0, size);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(r.Y), 0, size), y1 = Mathf.Clamp(Mathf.CeilToInt(r.Y + r.H), 0, size);
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++) _pixels[y * size + x] = Blend(_pixels[y * size + x], color);
            }
        }

        private void Outline(MiniRect r, Color32 color)
        {
            if (r.IsEmpty) return;
            Fill(new MiniRect(r.X, r.Y, r.W, 1), color);
            Fill(new MiniRect(r.X, r.Y + r.H - 1, r.W, 1), color);
            Fill(new MiniRect(r.X, r.Y, 1, r.H), color);
            Fill(new MiniRect(r.X + r.W - 1, r.Y, 1, r.H), color);
        }

        private static Color32 Blend(Color32 under, Color32 over)
        {
            float a = over.a / 255f;
            return new Color32((byte)(over.r * a + under.r * (1 - a)), (byte)(over.g * a + under.g * (1 - a)), (byte)(over.b * a + under.b * (1 - a)),
                (byte)Mathf.Max(under.a, over.a));
        }
    }
}
