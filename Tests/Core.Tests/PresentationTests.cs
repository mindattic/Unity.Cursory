using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.Cursory.Core.Tests
{
    /// <summary>UC-T11: the client presentation's engine-free rules (click vs drag, camera maths, minimap, picking, HUD words).</summary>
    public class PointerGestureTests
    {
        [Test]
        public void A_quick_still_press_is_a_whistle_at_the_pressed_world_point()
        {
            var g = new PointerGesture();
            g.Arm(100, 100, 1000, 5000, 4000);
            Assert.That(g.Move(103, 103), Is.False, "4.2 px is under the 5 px threshold");
            Assert.That(g.Release(1250), Is.EqualTo(GestureResult.Whistle), "250 ms is still a click (inclusive)");
            Assert.That((g.WorldX, g.WorldY), Is.EqualTo((5000f, 4000f)));
            Assert.That(g.IsArmed || g.IsPanning, Is.False, "a release leaves it idle");
        }

        [Test]
        public void Moving_five_pixels_commits_to_a_pan_once_and_cancels_the_whistle()
        {
            var g = new PointerGesture();
            g.Arm(0, 0, 0, 0, 0);
            Assert.That(g.Move(3, 4), Is.True, "exactly 5 px is a drag");
            Assert.That(g.IsPanning, Is.True);
            Assert.That(g.Move(50, 50), Is.False, "the pan starts once");
            Assert.That(g.Release(10), Is.EqualTo(GestureResult.PanEnded), "a quick pan never whistles");
        }

        [Test]
        public void A_long_still_press_does_nothing()
        {
            var g = new PointerGesture();
            g.Arm(0, 0, 0, 0, 0);
            Assert.That(g.Release(251), Is.EqualTo(GestureResult.None));
        }

        [Test]
        public void Release_without_a_press_and_after_cancel_does_nothing()
        {
            var g = new PointerGesture();
            Assert.That(g.Release(0), Is.EqualTo(GestureResult.None));
            g.Arm(0, 0, 0, 0, 0);
            g.Cancel();
            Assert.That(g.Move(100, 0), Is.False);
            Assert.That(g.Release(10), Is.EqualTo(GestureResult.None));
        }

        [TestCase(4.99f, 0f, false)]
        [TestCase(5f, 0f, true)]
        [TestCase(-3f, -4f, true)]
        public void IsDrag_uses_a_5px_radius(float dx, float dy, bool drag) =>
            Assert.That(PointerGesture.IsDrag(dx, dy), Is.EqualTo(drag));

        [TestCase(0, true)]
        [TestCase(250, true)]
        [TestCase(250.5, false)]
        [TestCase(-1, false)]
        public void IsClick_is_250ms_inclusive(double ms, bool click) =>
            Assert.That(PointerGesture.IsClick(ms), Is.EqualTo(click));
    }

    public class CameraViewTests
    {
        [Test]
        public void Zoom_is_clamped_to_a_tenth_and_four()
        {
            var cam = new CameraView { Zoom = 100f };
            Assert.That(cam.Zoom, Is.EqualTo(CameraView.ZoomMax));
            cam.Zoom = 0.001f;
            Assert.That(cam.Zoom, Is.EqualTo(CameraView.ZoomMin));
            cam.Zoom = float.NaN;
            Assert.That(cam.Zoom, Is.EqualTo(1f));
            Assert.That(CameraView.ClampZoom(float.PositiveInfinity), Is.EqualTo(4f));
        }

        [Test]
        public void The_wheel_steps_by_1_1_and_never_leaves_the_range()
        {
            var cam = new CameraView();
            cam.Wheel(960, 540, 1f, 1920, 1080);
            Assert.That(cam.Zoom, Is.EqualTo(1.1f).Within(1e-5));
            for (int i = 0; i < 100; i++) cam.Wheel(960, 540, 1f, 1920, 1080);
            Assert.That(cam.Zoom, Is.EqualTo(4f));
            for (int i = 0; i < 200; i++) cam.Wheel(960, 540, -1f, 1920, 1080);
            Assert.That(cam.Zoom, Is.EqualTo(0.1f));
        }

        [Test]
        public void Zooming_keeps_the_world_point_under_the_pointer()
        {
            var cam = new CameraView { X = 5000, Y = 5000 };
            var before = cam.ScreenToWorld(1500, 200, 1920, 1080);
            cam.ZoomAt(1500, 200, 2.5f, 1920, 1080);
            var after = cam.ScreenToWorld(1500, 200, 1920, 1080);
            Assert.That(after.X, Is.EqualTo(before.X).Within(1e-2));
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(1e-2));
        }

        [Test]
        public void Screen_and_world_round_trip_with_y_up()
        {
            var cam = new CameraView { X = 2000, Y = 3000, Zoom = 2f };
            Assert.That(cam.ScreenToWorld(960, 540, 1920, 1080), Is.EqualTo((2000f, 3000f)), "the screen centre is the camera centre");
            Assert.That(cam.ScreenToWorld(1160, 740, 1920, 1080), Is.EqualTo((2100f, 3100f)), "up and right on screen is up and right in the world");
            var s = cam.WorldToScreen(2100, 3100, 1920, 1080);
            Assert.That(s, Is.EqualTo((1160f, 740f)));
        }

        [Test]
        public void A_pan_moves_the_world_with_the_pointer_and_stays_inside_the_world()
        {
            var cam = new CameraView { X = 5000, Y = 5000, Zoom = 2f };
            cam.PanFrom(5000, 5000, 100, -40);
            Assert.That((cam.X, cam.Y), Is.EqualTo((4950f, 5020f)));
            cam.PanFrom(0, 0, 1000, 1000);
            Assert.That((cam.X, cam.Y), Is.EqualTo((0f, 0f)), "clamped to the world's corner");
            cam.X = 1e9f;
            Assert.That(cam.X, Is.EqualTo(WorldUnits.WorldSizePx));
        }

        [Test]
        public void Follow_eases_twelve_percent_of_the_way()
        {
            var cam = new CameraView { X = 1000, Y = 1000 };
            cam.Follow(2000, 1000);
            Assert.That(cam.X, Is.EqualTo(1120f).Within(1e-3));
        }

        [Test]
        public void Visible_is_the_screen_divided_by_zoom()
        {
            var cam = new CameraView { X = 5000, Y = 5000, Zoom = 0.5f };
            Assert.That(cam.Visible(1920, 1080), Is.EqualTo((3080f, 3920f, 3840f, 2160f)));
        }
    }

    public class MinimapTransformTests
    {
        [Test]
        public void The_world_maps_onto_160px_with_a_2px_floor()
        {
            var map = new MinimapTransform();
            Assert.That(map.ScaleX, Is.EqualTo(0.016f).Within(1e-6));
            Assert.That(map.ToMap(5000, 10000), Is.EqualTo((80f, 160f)));
            var wall = map.Feature(5000, 5000, 180, 1200);
            Assert.That(wall.X, Is.EqualTo(78.56f).Within(1e-3));
            Assert.That(wall.W, Is.EqualTo(2.88f).Within(1e-3));
            Assert.That(wall.H, Is.EqualTo(19.2f).Within(1e-3));
            var tiny = map.Feature(100, 100, 10, 10);
            Assert.That((tiny.W, tiny.H), Is.EqualTo((2f, 2f)), "small geometry keeps a 2 px floor");
            Assert.That(map.ToWorld(80, 160), Is.EqualTo((5000f, 10000f)));
        }

        [Test]
        public void The_viewport_rectangle_follows_the_camera_and_is_clipped_to_the_map()
        {
            var map = new MinimapTransform();
            var cam = new CameraView { X = 5000, Y = 5000, Zoom = 1f };
            var v = map.Viewport(cam, 1000, 500);
            Assert.That(v.X, Is.EqualTo(72f).Within(1e-3));
            Assert.That(v.Y, Is.EqualTo(76f).Within(1e-3));
            Assert.That(v.W, Is.EqualTo(16f).Within(1e-3));
            Assert.That(v.H, Is.EqualTo(8f).Within(1e-3));

            cam.X = 0;
            cam.Y = 0;
            cam.Zoom = 0.1f;
            var clipped = map.Viewport(cam, 1920, 1080);
            Assert.That(clipped.X, Is.EqualTo(0f));
            Assert.That(clipped.Y, Is.EqualTo(0f));
            Assert.That(clipped.W, Is.EqualTo(153.6f).Within(1e-3));
            Assert.That(clipped.H, Is.EqualTo(86.4f).Within(1e-3));
        }

        [Test]
        public void Sizes_must_be_positive() =>
            Assert.That(() => new MinimapTransform(0f), Throws.TypeOf<System.ArgumentOutOfRangeException>());
    }

    public class BodyPickerTests
    {
        [Test]
        public void A_press_on_a_block_grabs_it_and_empty_space_picks_nothing()
        {
            var level = LevelSeeds.Level(1);
            Assert.That(BodyPicker.Pick(level, null, null, 3500, 5000), Is.EqualTo(new PickResult(PickKind.Block, "L1-block")));
            Assert.That(BodyPicker.Pick(level, null, null, 3579, 5079).Kind, Is.EqualTo(PickKind.Block), "inside the 160 px block");
            Assert.That(BodyPicker.Pick(level, null, null, 3581, 5000).IsHit, Is.False, "just outside it");
            Assert.That(BodyPicker.Pick(level, null, null, 6500, 5000).IsHit, Is.False, "a goal pad is not a body");
        }

        [Test]
        public void Live_poses_move_and_turn_the_hit_box()
        {
            var level = LevelSeeds.Level(1);
            var poses = new Dictionary<string, BodyPose> { ["L1-block"] = new BodyPose(4000, 5000, (float)System.Math.PI / 4) };
            Assert.That(BodyPicker.Pick(level, poses, null, 3500, 5000).IsHit, Is.False, "the block has moved away");
            Assert.That(BodyPicker.Pick(level, poses, null, 4000, 5105).Kind, Is.EqualTo(PickKind.Block), "turned 45 degrees, its corner reaches 113 px up");
            Assert.That(BodyPicker.Pick(level, poses, null, 4075, 5075).IsHit, Is.False, "and its side is now diagonal");
        }

        [Test]
        public void Walls_are_picked_after_bodies()
        {
            var level = LevelSeeds.Level(2);
            Assert.That(BodyPicker.Pick(level, null, null, 5000, 5500), Is.EqualTo(new PickResult(PickKind.Wall, "L2-wall")));
        }

        [Test]
        public void Shapes_are_picked_by_piece()
        {
            var level = LevelSeeds.Level(3);
            var shape = level.Shapes[0];
            var piece = shape.Pieces[0];
            Assert.That(BodyPicker.Pick(level, null, null, shape.X + piece.LocalX, shape.Y + piece.LocalY),
                Is.EqualTo(new PickResult(PickKind.Shape, shape.Id)));
        }

        [Test]
        public void Wire_ends_win_within_40px_nearest_first()
        {
            var level = LevelSeeds.Level(14);
            var wire = level.Wires[0];
            Assert.That(BodyPicker.Pick(level, null, null, wire.Ax + 30, wire.Ay), Is.EqualTo(new PickResult(PickKind.WireEnd, wire.Id, true)));
            Assert.That(BodyPicker.Pick(level, null, null, wire.Bx, wire.By - 39), Is.EqualTo(new PickResult(PickKind.WireEnd, wire.Id, false)));

            var moved = new List<(float, float, float, float)>();
            foreach (var w in level.Wires) moved.Add((w.Ax, w.Ay, w.Bx, w.By));
            moved[0] = (100, 100, wire.Bx, wire.By);
            Assert.That(BodyPicker.Pick(level, null, moved, 110, 100), Is.EqualTo(new PickResult(PickKind.WireEnd, wire.Id, true)), "the live ends, not the level's");
        }
    }

    public class HudTextTests
    {
        [Test]
        public void Level_options_and_banner()
        {
            Assert.That(HudText.LevelOptions(3), Is.EqualTo(new[] { "Level 1", "Level 2", "Level 3" }));
            var (title, sub) = HudText.Banner(LevelSeeds.Level(2), 2);
            Assert.That(title, Is.EqualTo("Level 2 — Too heavy for one"));
            Assert.That(sub, Does.StartWith("Mass 2"));
            Assert.That(HudText.Banner(null, 9), Is.EqualTo(("Level 9", "")));
            Assert.That(HudText.Banner(new LevelData { Number = 4, Title = "Bare" }, 4), Is.EqualTo(("Level 4 — Bare", "")));
        }
    }
}
