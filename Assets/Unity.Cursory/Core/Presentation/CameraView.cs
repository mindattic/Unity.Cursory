using System;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// The client camera's maths, engine-free (Cursory's <c>state.cam</c> plus <c>clientToWorld</c>, the wheel zoom and
    /// the empty-space pan in room-core.js): a centre in world pixels and a zoom, where zoom 1 shows one world pixel per
    /// screen pixel. Screen points are pixels from the bottom-left with y up, as Unity reports them; world y is up too
    /// (BIBLE UC-LAW-3 maps world pixels straight onto metres), so no axis flips.
    /// </summary>
    /// <remarks>
    /// The centre stays inside the <see cref="WorldUnits.WorldSizePx"/> square and the zoom inside
    /// <see cref="ZoomMin"/>..<see cref="ZoomMax"/>, whatever the input.
    /// </remarks>
    public sealed class CameraView
    {
        /// <summary>Farthest out the wheel zooms: a tenth of a screen pixel per world pixel.</summary>
        public const float ZoomMin = 0.1f;

        /// <summary>Farthest in the wheel zooms: four screen pixels per world pixel.</summary>
        public const float ZoomMax = 4f;

        /// <summary>The zoom multiple for one wheel notch.</summary>
        public const float WheelStep = 1.1f;

        /// <summary>How far the camera eases toward the grab midpoint each frame while dragging a body (Cursory's 0.12).</summary>
        public const float FollowRate = 0.12f;

        private float _x = WorldUnits.WorldSizePx / 2f;
        private float _y = WorldUnits.WorldSizePx / 2f;
        private float _zoom = 1f;

        /// <summary>Width of the world the centre is kept inside, in world pixels.</summary>
        public float WorldWidth { get; set; } = WorldUnits.WorldSizePx;

        /// <summary>Height of the world the centre is kept inside, in world pixels.</summary>
        public float WorldHeight { get; set; } = WorldUnits.WorldSizePx;

        /// <summary>The world point at the centre of the screen, x in world pixels (clamped to the world).</summary>
        public float X
        {
            get => _x;
            set => _x = Clamp(Finite(value, _x), 0f, WorldWidth);
        }

        /// <summary>The world point at the centre of the screen, y in world pixels (clamped to the world).</summary>
        public float Y
        {
            get => _y;
            set => _y = Clamp(Finite(value, _y), 0f, WorldHeight);
        }

        /// <summary>Screen pixels per world pixel, clamped to <see cref="ZoomMin"/>..<see cref="ZoomMax"/>.</summary>
        public float Zoom
        {
            get => _zoom;
            set => _zoom = ClampZoom(value);
        }

        /// <summary><paramref name="zoom"/> held inside <see cref="ZoomMin"/>..<see cref="ZoomMax"/> (NaN keeps 1).</summary>
        public static float ClampZoom(float zoom) => float.IsNaN(zoom) ? 1f : Clamp(zoom, ZoomMin, ZoomMax);

        /// <summary>The world point under screen pixel (<paramref name="sx"/>, <paramref name="sy"/>) on a <paramref name="screenW"/> × <paramref name="screenH"/> screen.</summary>
        public (float X, float Y) ScreenToWorld(float sx, float sy, float screenW, float screenH) =>
            (_x + (sx - screenW / 2f) / _zoom, _y + (sy - screenH / 2f) / _zoom);

        /// <summary>The screen pixel over world point (<paramref name="wx"/>, <paramref name="wy"/>).</summary>
        public (float X, float Y) WorldToScreen(float wx, float wy, float screenW, float screenH) =>
            (screenW / 2f + (wx - _x) * _zoom, screenH / 2f + (wy - _y) * _zoom);

        /// <summary>
        /// Multiplies the zoom by <paramref name="factor"/> (then clamps it), keeping the world point under screen pixel
        /// (<paramref name="sx"/>, <paramref name="sy"/>) where it is, as Cursory's wheel does.
        /// </summary>
        public void ZoomAt(float sx, float sy, float factor, float screenW, float screenH)
        {
            if (!(factor > 0f) || float.IsInfinity(factor)) return;
            var before = ScreenToWorld(sx, sy, screenW, screenH);
            Zoom = _zoom * factor;
            var after = ScreenToWorld(sx, sy, screenW, screenH);
            X = _x + (before.X - after.X);
            Y = _y + (before.Y - after.Y);
        }

        /// <summary>One wheel movement at a screen pixel: a positive <paramref name="scrollY"/> (wheel away) zooms in by <see cref="WheelStep"/>, a negative one out.</summary>
        public void Wheel(float sx, float sy, float scrollY, float screenW, float screenH)
        {
            if (scrollY == 0f || float.IsNaN(scrollY)) return;
            ZoomAt(sx, sy, scrollY > 0f ? WheelStep : 1f / WheelStep, screenW, screenH);
        }

        /// <summary>
        /// A pan drag: the centre was (<paramref name="originX"/>, <paramref name="originY"/>) when the drag started and the
        /// pointer has since moved (<paramref name="dxScreen"/>, <paramref name="dyScreen"/>) screen pixels; the world
        /// follows the pointer, so the centre moves the other way.
        /// </summary>
        public void PanFrom(float originX, float originY, float dxScreen, float dyScreen)
        {
            X = originX - dxScreen / _zoom;
            Y = originY - dyScreen / _zoom;
        }

        /// <summary>Eases the centre <paramref name="rate"/> of the way toward (<paramref name="tx"/>, <paramref name="ty"/>).</summary>
        public void Follow(float tx, float ty, float rate = FollowRate)
        {
            X = _x + (tx - _x) * rate;
            Y = _y + (ty - _y) * rate;
        }

        /// <summary>The visible world rectangle as (min x, min y, width, height) in world pixels.</summary>
        public (float X, float Y, float W, float H) Visible(float screenW, float screenH)
        {
            float w = screenW / _zoom, h = screenH / _zoom;
            return (_x - w / 2f, _y - h / 2f, w, h);
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        private static float Finite(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;

        /// <inheritdoc />
        public override string ToString() => FormattableString.Invariant($"Camera ({_x:0.#}, {_y:0.#}) x{_zoom:0.###}");
    }
}
