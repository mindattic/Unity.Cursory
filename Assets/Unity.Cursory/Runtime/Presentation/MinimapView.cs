using System.Collections.Generic;
using MindAttic.Components.Unity;
using MindAttic.Components.Unity.Core;
using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// The minimap (UC-T11, Cursory's <c>drawMinimap</c>), on MindAttic Components' <see cref="MaMinimap"/> (UC-T23,
    /// M.COMP-T46) at the bottom-right of the HUD. It only feeds the map: goals (green once solved), walls and blocks
    /// in their colours and shapes as dots, all fitted (so the map shows the level's bounds with padding, not a small
    /// level in a big empty world), then the camera's view as an outline and every cursor as a dot in its player's
    /// colour (not fitted, so a cursor wandering off does not rescale the map). A click or drag on the map pans the
    /// camera there.
    /// </summary>
    public sealed class MinimapView
    {
        /// <summary>The map's size on the 1920x1080 HUD canvas (Cursory's 160 screen pixels, doubled like the package's metrics).</summary>
        public const float CanvasSize = 320f;

        /// <summary>Texels along each edge of the map (Cursory's 160-pixel minimap).</summary>
        public const int Resolution = 160;

        /// <summary>Texels kept clear around the level.</summary>
        public const float Padding = 8f;

        /// <summary>Redraws per second.</summary>
        public const float RedrawRate = 15f;

        private static readonly Color32 Backdrop = new Color32(0, 0, 0, 128);
        private static readonly Color32 WallColor = new Color32(170, 170, 170, 204);
        private static readonly Color32 GoalOpen = new Color32(127, 119, 221, 128);
        private static readonly Color32 ShapeGoalOpen = new Color32(216, 90, 48, 102);
        private static readonly Color32 GoalSolved = new Color32(29, 158, 117, 204);
        private static readonly Color32 ViewColor = new Color32(255, 255, 255, 128);

        private float _nextDraw;

        private MinimapView(Canvas canvas)
        {
            Map = MaMinimap.Create(canvas, CanvasSize, Resolution, Padding);
            Map.Backdrop = Backdrop;
            Map.ViewportColor = ViewColor;
            RectTransform rt = Map.RectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-24f, 24f);
        }

        /// <summary>The map widget.</summary>
        public MaMinimap Map { get; }

        /// <summary>Times the map has been drawn.</summary>
        public int Draws => Map != null ? Map.Draws : 0;

        /// <summary>The camera's visible world rectangle as the map last drew it (world pixels); null before a draw with a view.</summary>
        public MapRect? Viewport { get; private set; }

        /// <summary>Adds a minimap to <paramref name="canvas"/>.</summary>
        public static MinimapView Create(Canvas canvas) => new MinimapView(canvas);

        /// <summary>Redraws at most <see cref="RedrawRate"/> times a second (<paramref name="force"/> redraws now).</summary>
        public void Draw(LevelData level, LevelStateSync state, IReadOnlyDictionary<string, Transform> bodies, IReadOnlyList<CursorAvatar> cursors,
            CameraView view, Vector2 screen, bool force = false)
        {
            if (!force && Time.unscaledTime < _nextDraw) return;
            _nextDraw = Time.unscaledTime + 1f / RedrawRate;

            Map.Clear();
            if (level != null)
            {
                int goal = 0;
                foreach (BlockGoal g in level.BlockGoals) Map.AddRect(MapRect.FromCenter(g.X, g.Y, g.W, g.H), Solved(state, goal++) ? GoalSolved : GoalOpen);
                foreach (ShapeGoal g in level.ShapeGoals) Map.AddRect(MapRect.FromCenter(g.X, g.Y, g.W, g.H), Solved(state, goal++) ? GoalSolved : ShapeGoalOpen);
                foreach (Wall w in level.Walls) Map.AddRect(MapRect.FromCenter(w.X, w.Y, w.W, w.H), WallColor);
                foreach (Block b in level.Blocks)
                {
                    BodyPose p = PoseOf(bodies, b.Id, b.X, b.Y);
                    Map.AddRect(MapRect.FromCenter(p.X, p.Y, b.W, b.H), Hex(b.Color));
                }

                foreach (Shape s in level.Shapes)
                {
                    BodyPose p = PoseOf(bodies, s.Id, s.X, s.Y);
                    Map.AddPoint(p.X, p.Y, Hex(s.Color), 2, fit: true);
                }
            }

            if (view != null)
            {
                var v = view.Visible(screen.x, screen.y);
                Viewport = new MapRect(v.X, v.Y, v.W, v.H);
                Map.SetViewport(Viewport);
            }

            if (cursors != null)
            {
                foreach (CursorAvatar c in cursors)
                {
                    if (c == null || !c.IsSpawned) continue;
                    Map.AddPoint(c.Position.x, c.Position.y, c.PlayerColor, 1);
                }
            }

            Map.Apply();
        }

        /// <summary>Destroys the map.</summary>
        public void Destroy()
        {
            if (Map != null) Object.Destroy(Map.gameObject);
        }

        private static bool Solved(LevelStateSync state, int index) => state != null && state.IsSpawned && state.IsGoalSolved(index);

        private static BodyPose PoseOf(IReadOnlyDictionary<string, Transform> bodies, string id, float x, float y) =>
            bodies != null && bodies.TryGetValue(id, out Transform t) && t != null ? ClientBodies.PoseOf(t) : new BodyPose(x, y, 0f);

        private static Color32 Hex(string hex) =>
            HexColor.TryParse(hex, out byte r, out byte g, out byte b) ? new Color32(r, g, b, 255) : new Color32(136, 136, 136, 255);
    }
}
