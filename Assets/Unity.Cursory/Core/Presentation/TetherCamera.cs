using System;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// The tether camera's maths, engine-free (UC-T34): while the local player is tethered, <see cref="Step"/> eases a
    /// <see cref="CameraView"/> toward the midpoint of their cursor and the point their rope is tied to on the object,
    /// zoomed out just enough to keep both (plus <see cref="MarginPx"/> of screen) in view. The ease is critically
    /// damped (no overshoot, no snap) and runs in log-zoom so zooming out and in feel the same; with reduce motion on
    /// the camera goes straight to the framing instead of sweeping there (the family's "every transition instant").
    /// </summary>
    /// <remarks>
    /// <para>It only ever zooms <i>out</i> past the player's own zoom: the target zoom is the smaller of
    /// <see cref="PreferredZoom"/> (the zoom when the tether began, which the wheel still changes while tethered) and the
    /// fitting zoom. When the tether ends (<see cref="End"/>) it stops driving the view, which is then the player's
    /// again: free pan and wheel zoom (UC-T11).</para>
    /// <para>Whatever the ease is doing, every step also keeps both points on screen (<see cref="Contain"/>): if the
    /// eased view would leave either one off the edge, the view is widened or shifted just enough to bring it back, so a
    /// fast drag can never lose the cursor or the anchor.</para>
    /// </remarks>
    public sealed class TetherCamera
    {
        /// <summary>Screen pixels kept clear between each framed point and the screen's edge.</summary>
        public const float MarginPx = 120f;

        /// <summary>The ease's smooth time, in seconds: roughly how long the camera takes to catch up with a still target.</summary>
        public const float SmoothSeconds = 0.3f;

        private float _vx, _vy, _vLogZoom;

        /// <summary>True between <see cref="Begin"/> and <see cref="End"/>: the camera is following a tether.</summary>
        public bool Active { get; private set; }

        /// <summary>The zoom the player wants when the tether allows it: the zoom at <see cref="Begin"/>, changed by <see cref="Wheel"/>.</summary>
        public float PreferredZoom { get; private set; } = 1f;

        /// <summary>Starts following from a view at <paramref name="currentZoom"/> (which becomes <see cref="PreferredZoom"/>); does nothing when already active.</summary>
        public void Begin(float currentZoom)
        {
            if (Active) return;
            Active = true;
            PreferredZoom = CameraView.ClampZoom(currentZoom);
            _vx = _vy = _vLogZoom = 0f;
        }

        /// <summary>Stops following; the view is the player's to pan and zoom again.</summary>
        public void End()
        {
            Active = false;
            _vx = _vy = _vLogZoom = 0f;
        }

        /// <summary>A wheel zoom while tethered: multiplies <see cref="PreferredZoom"/> by <paramref name="factor"/> (clamped) instead of fighting the follow.</summary>
        public void Wheel(float factor)
        {
            if (!(factor > 0f) || float.IsInfinity(factor)) return;
            PreferredZoom = CameraView.ClampZoom(PreferredZoom * factor);
        }

        /// <summary>
        /// The zoom that fits world points (<paramref name="ax"/>, <paramref name="ay"/>) and (<paramref name="bx"/>,
        /// <paramref name="by"/>) on a <paramref name="screenW"/> × <paramref name="screenH"/> screen when centred on their
        /// midpoint, with <paramref name="marginPx"/> clear at every edge (shrunk on a tiny screen so some room is always
        /// left), clamped to <see cref="CameraView.ZoomMin"/>..<see cref="CameraView.ZoomMax"/>.
        /// </summary>
        public static float FitZoom(float ax, float ay, float bx, float by, float screenW, float screenH, float marginPx = MarginPx)
        {
            float mx = Math.Min(Math.Max(0f, marginPx), screenW / 4f);
            float my = Math.Min(Math.Max(0f, marginPx), screenH / 4f);
            float spanX = Math.Abs(ax - bx), spanY = Math.Abs(ay - by);
            float zoom = CameraView.ZoomMax;
            if (spanX > 0f) zoom = Math.Min(zoom, (screenW - 2f * mx) / spanX);
            if (spanY > 0f) zoom = Math.Min(zoom, (screenH - 2f * my) / spanY);
            return CameraView.ClampZoom(zoom);
        }

        /// <summary>Where the camera wants to be for a cursor at (<paramref name="cx"/>, <paramref name="cy"/>) tied to (<paramref name="ax"/>, <paramref name="ay"/>): their midpoint, at the smaller of <see cref="PreferredZoom"/> and the fitting zoom.</summary>
        public (float X, float Y, float Zoom) Target(float cx, float cy, float ax, float ay, float screenW, float screenH) =>
            ((cx + ax) / 2f, (cy + ay) / 2f, Math.Min(PreferredZoom, FitZoom(cx, cy, ax, ay, screenW, screenH)));

        /// <summary>
        /// One frame of <paramref name="dt"/> seconds: begins following if not yet active, eases <paramref name="view"/>
        /// toward <see cref="Target"/> (or puts it there at once when <paramref name="reduceMotion"/> is on), then
        /// <see cref="Contain"/>s both points.
        /// </summary>
        public void Step(CameraView view, float cx, float cy, float ax, float ay, float screenW, float screenH, float dt, bool reduceMotion)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!Finite(cx) || !Finite(cy) || !Finite(ax) || !Finite(ay) || !(screenW > 0f) || !(screenH > 0f)) return;
            Begin(view.Zoom);
            var target = Target(cx, cy, ax, ay, screenW, screenH);
            if (reduceMotion || !(dt > 0f) || float.IsInfinity(dt))
            {
                if (reduceMotion)
                {
                    view.Zoom = target.Zoom;
                    view.X = target.X;
                    view.Y = target.Y;
                    _vx = _vy = _vLogZoom = 0f;
                }
            }
            else
            {
                float logZoom = Damp((float)Math.Log(view.Zoom), (float)Math.Log(target.Zoom), ref _vLogZoom, SmoothSeconds, dt);
                view.Zoom = (float)Math.Exp(logZoom);
                view.X = Damp(view.X, target.X, ref _vx, SmoothSeconds, dt);
                view.Y = Damp(view.Y, target.Y, ref _vy, SmoothSeconds, dt);
            }

            Contain(view, cx, cy, ax, ay, screenW, screenH);
        }

        /// <summary>
        /// Keeps world points (<paramref name="ax"/>, <paramref name="ay"/>) and (<paramref name="bx"/>,
        /// <paramref name="by"/>) on screen with the least change to <paramref name="view"/>: zooms out first if the two
        /// can't both fit at the current zoom (edge to edge, no margin), then shifts the centre just enough to bring
        /// any point that is off an edge back onto it. Points already on screen change nothing.
        /// </summary>
        public static void Contain(CameraView view, float ax, float ay, float bx, float by, float screenW, float screenH)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!(screenW > 0f) || !(screenH > 0f)) return;
            float fit = FitZoom(ax, ay, bx, by, screenW, screenH, 0f);
            if (view.Zoom > fit) view.Zoom = fit;

            float halfW = screenW / 2f / view.Zoom, halfH = screenH / 2f / view.Zoom;
            float minX = Math.Min(ax, bx), maxX = Math.Max(ax, bx);
            float minY = Math.Min(ay, by), maxY = Math.Max(ay, by);
            float x = view.X, y = view.Y;
            if (maxX > x + halfW) x = maxX - halfW;
            if (minX < x - halfW) x = minX + halfW;
            if (maxY > y + halfH) y = maxY - halfH;
            if (minY < y - halfH) y = minY + halfH;
            view.X = x;
            view.Y = y;
        }

        /// <summary>
        /// A critically damped spring from <paramref name="current"/> toward <paramref name="target"/> over
        /// <paramref name="dt"/> seconds (the Game Programming Gems 4 approximation Unity's SmoothDamp uses), carrying
        /// <paramref name="velocity"/> between calls; it never overshoots the target.
        /// </summary>
        public static float Damp(float current, float target, ref float velocity, float smoothSeconds, float dt)
        {
            if (!(dt > 0f)) return current;
            float omega = 2f / Math.Max(1e-4f, smoothSeconds);
            float x = omega * dt;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * exp;
            float output = target + (change + temp) * exp;
            if ((target - current > 0f) == (output > target))
            {
                output = target;   // passed it: stop there, never overshoot
                velocity = 0f;
            }

            return output;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
