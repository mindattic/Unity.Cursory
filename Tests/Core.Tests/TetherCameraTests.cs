using System;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    /// <summary>UC-T34: the tether camera's engine-free follow and zoom (framing, critically damped ease, containment, reduce motion).</summary>
    public class TetherCameraTests
    {
        private const float W = 1920f, H = 1080f;

        private static bool OnScreen(CameraView view, float x, float y, float slack = 0.5f)
        {
            var (sx, sy) = view.WorldToScreen(x, y, W, H);
            return sx >= -slack && sx <= W + slack && sy >= -slack && sy <= H + slack;
        }

        [Test]
        public void Fit_zoom_keeps_both_points_and_the_margin_on_screen()
        {
            float zoom = TetherCamera.FitZoom(4000f, 5000f, 6000f, 5000f, W, H);
            Assert.That(zoom, Is.EqualTo((W - 2f * TetherCamera.MarginPx) / 2000f).Within(1e-5f), "2000 px across the screen less two margins");

            float tall = TetherCamera.FitZoom(5000f, 4500f, 5000f, 5500f, W, H);
            Assert.That(tall, Is.EqualTo((H - 2f * TetherCamera.MarginPx) / 1000f).Within(1e-5f), "the shorter screen side binds a vertical rope");
        }

        [Test]
        public void Fit_zoom_is_clamped_and_close_points_allow_the_closest_zoom()
        {
            Assert.That(TetherCamera.FitZoom(5000f, 5000f, 5000f, 5000f, W, H), Is.EqualTo(CameraView.ZoomMax), "one point fits at any zoom");
            Assert.That(TetherCamera.FitZoom(0f, 0f, 1e6f, 0f, W, H), Is.EqualTo(CameraView.ZoomMin), "never past the wheel's farthest");
        }

        [Test]
        public void The_target_is_the_midpoint_and_only_ever_zooms_out_past_the_players_own_zoom()
        {
            var cam = new TetherCamera();
            cam.Begin(1f);
            var near = cam.Target(5000f, 5000f, 5100f, 5000f, W, H);
            Assert.That((near.X, near.Y), Is.EqualTo((5050f, 5000f)));
            Assert.That(near.Zoom, Is.EqualTo(1f), "a short rope keeps the player's zoom");

            var far = cam.Target(4000f, 5000f, 6000f, 5000f, W, H);
            Assert.That(far.Zoom, Is.LessThan(1f), "a long rope zooms out just enough");
            Assert.That(far.Zoom, Is.EqualTo(TetherCamera.FitZoom(4000f, 5000f, 6000f, 5000f, W, H)));
        }

        [Test]
        public void Damp_is_critically_damped_never_overshoots_and_settles()
        {
            float v = 0f, x = 0f;
            float last = x;
            for (int i = 0; i < 600; i++)
            {
                x = TetherCamera.Damp(x, 100f, ref v, TetherCamera.SmoothSeconds, 1f / 60f);
                Assert.That(x, Is.LessThanOrEqualTo(100f), "never past the target");
                Assert.That(x, Is.GreaterThanOrEqualTo(last), "monotonic toward it");
                last = x;
            }

            Assert.That(x, Is.EqualTo(100f).Within(0.01f), "settled after ten seconds");

            float v2 = 0f;
            float first = TetherCamera.Damp(0f, 100f, ref v2, TetherCamera.SmoothSeconds, 1f / 60f);
            Assert.That(first, Is.LessThan(10f), "no snap: one frame moves only a little of the way");
        }

        [Test]
        public void Step_eases_toward_the_framing_without_snapping()
        {
            var view = new CameraView { X = 5000f, Y = 5000f, Zoom = 1f };
            var cam = new TetherCamera();
            cam.Step(view, 5200f, 5100f, 5400f, 5100f, W, H, 1f / 60f, reduceMotion: false);
            Assert.That(cam.Active, Is.True);
            Assert.That(view.X, Is.GreaterThan(5000f).And.LessThan(5300f), "on its way to the midpoint, not there yet");

            for (int i = 0; i < 300; i++) cam.Step(view, 5200f, 5100f, 5400f, 5100f, W, H, 1f / 60f, reduceMotion: false);
            Assert.That(view.X, Is.EqualTo(5300f).Within(0.5f));
            Assert.That(view.Y, Is.EqualTo(5100f).Within(0.5f));
            Assert.That(view.Zoom, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Reduce_motion_goes_straight_to_the_framing()
        {
            var view = new CameraView { X = 5000f, Y = 5000f, Zoom = 2f };
            var cam = new TetherCamera();
            cam.Step(view, 4000f, 5000f, 6000f, 5000f, W, H, 1f / 60f, reduceMotion: true);
            Assert.That(view.X, Is.EqualTo(5000f).Within(1e-3f));
            Assert.That(view.Zoom, Is.EqualTo(TetherCamera.FitZoom(4000f, 5000f, 6000f, 5000f, W, H)).Within(1e-5f));
        }

        [Test]
        public void Every_step_keeps_the_cursor_and_anchor_on_screen_even_on_a_fast_drag()
        {
            var view = new CameraView { X = 5000f, Y = 5000f, Zoom = 1f };
            var cam = new TetherCamera();
            float ax = 5000f, ay = 5000f;
            for (int i = 0; i < 240; i++)
            {
                // The cursor races away and round at 3000 px/s while the anchor drifts behind it.
                float t = i / 60f;
                float cx = 5000f + (float)Math.Cos(t * 3f) * 3000f * Math.Min(1f, t);
                float cy = 5000f + (float)Math.Sin(t * 3f) * 2000f * Math.Min(1f, t);
                ax += (cx - ax) * 0.02f;
                ay += (cy - ay) * 0.02f;
                cam.Step(view, cx, cy, ax, ay, W, H, 1f / 60f, reduceMotion: false);
                Assert.That(OnScreen(view, cx, cy), Is.True, $"cursor off screen at frame {i}: {view}");
                Assert.That(OnScreen(view, ax, ay), Is.True, $"anchor off screen at frame {i}: {view}");
            }
        }

        [Test]
        public void Contain_leaves_a_view_that_already_shows_both_alone()
        {
            var view = new CameraView { X = 5000f, Y = 5000f, Zoom = 1f };
            TetherCamera.Contain(view, 4900f, 5000f, 5100f, 5050f, W, H);
            Assert.That((view.X, view.Y, view.Zoom), Is.EqualTo((5000f, 5000f, 1f)));
        }

        [Test]
        public void The_wheel_while_tethered_changes_the_preferred_zoom_and_End_hands_the_camera_back()
        {
            var view = new CameraView { Zoom = 1f };
            var cam = new TetherCamera();
            cam.Begin(view.Zoom);
            cam.Wheel(CameraView.WheelStep);
            Assert.That(cam.PreferredZoom, Is.EqualTo(1.1f).Within(1e-5f));
            for (int i = 0; i < 100; i++) cam.Wheel(0.5f);
            Assert.That(cam.PreferredZoom, Is.EqualTo(CameraView.ZoomMin), "clamped like the wheel");
            cam.End();
            Assert.That(cam.Active, Is.False);

            cam.Begin(2f);
            Assert.That(cam.PreferredZoom, Is.EqualTo(2f), "a new tether starts from the zoom the player has then");
        }
    }
}
