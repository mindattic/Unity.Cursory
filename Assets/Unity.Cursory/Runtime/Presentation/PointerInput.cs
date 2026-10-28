using System;
using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>What a press did (<see cref="PointerInput.PointerDown"/>).</summary>
    public enum PressOutcome
    {
        /// <summary>Ignored: the menu is open, the pointer is over the HUD, or it isn't the left button's job.</summary>
        Ignored = 0,

        /// <summary>It landed on something and sent a grab intent.</summary>
        Grabbed = 1,

        /// <summary>It landed on empty space: a whistle on a quick release, a pan on a drag.</summary>
        Armed = 2,
    }

    /// <summary>
    /// Turns the pointer into Cursory's intents (UC-T11, room-core.js's pointer handlers): a press on a wire end, shape,
    /// block or wall (<see cref="BodyPicker"/>) grabs it and a release lets go; a press on empty space arms a whistle and
    /// a pan (<see cref="PointerGesture"/>: 5 px of travel pans the camera, a release within 250 ms whistles); the wheel
    /// zooms about the pointer (<see cref="CameraView"/>, 0.1 to 4). Screen points are pixels from the bottom-left.
    /// </summary>
    /// <remarks>
    /// <see cref="CursoryClient"/> feeds it from the Input System's mouse every frame; tests call
    /// <see cref="PointerDown"/>, <see cref="PointerMove"/>, <see cref="PointerUp"/> and <see cref="Wheel"/> directly
    /// with their own clock, which is why every call takes its time and screen point.
    /// </remarks>
    public sealed class PointerInput
    {
        private readonly CursoryClient _client;
        private readonly PointerGesture _gesture = new PointerGesture();
        private Vector2 _panOrigin;

        internal PointerInput(CursoryClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>The click-vs-drag state of the current press.</summary>
        public PointerGesture Gesture => _gesture;

        /// <summary>Where the pointer is, in screen pixels (null before it has been seen, or after it left the window).</summary>
        public Vector2? Screen { get; private set; }

        /// <summary>What the current press holds; <see cref="PickResult.Nothing"/> when it holds nothing.</summary>
        public PickResult Holding { get; private set; }

        /// <summary>Raised on every whistle this player makes, with its world point (world pixels).</summary>
        public event Action<Vector2> Whistled;

        /// <summary>The world point under the pointer now, in world pixels; null when the pointer is unknown.</summary>
        public Vector2? World
        {
            get
            {
                if (!Screen.HasValue) return null;
                Vector2 size = _client.ScreenSize;
                var (x, y) = _client.View.ScreenToWorld(Screen.Value.x, Screen.Value.y, size.x, size.y);
                return new Vector2(x, y);
            }
        }

        /// <summary>The left button went down at <paramref name="screen"/> at <paramref name="nowMs"/>.</summary>
        public PressOutcome PointerDown(Vector2 screen, double nowMs)
        {
            Screen = screen;
            if (_client.IsMenuOpen) return PressOutcome.Ignored;

            Vector2 world = World.Value;
            CursorAvatar me = _client.LocalAvatar;
            PickResult hit = _client.Pick(world);
            if (hit.IsHit && me != null)
            {
                me.Move(world);
                switch (hit.Kind)
                {
                    case PickKind.WireEnd: me.GrabWireEnd(hit.Id, hit.EndA); break;
                    case PickKind.Shape: me.GrabShape(hit.Id, world.x, world.y); break;
                    case PickKind.Block: me.Grab(hit.Id, world.x, world.y); break;
                    case PickKind.Wall: me.GrabWall(hit.Id, world.x, world.y); break;
                }

                Holding = hit;
                return PressOutcome.Grabbed;
            }

            _gesture.Arm(screen.x, screen.y, nowMs, world.x, world.y);
            _panOrigin = new Vector2(_client.View.X, _client.View.Y);
            return PressOutcome.Armed;
        }

        /// <summary>The pointer moved to <paramref name="screen"/> (button up or down).</summary>
        public void PointerMove(Vector2 screen)
        {
            Screen = screen;
            if (_client.IsMenuOpen) return;
            _gesture.Move(screen.x, screen.y);
            if (_gesture.IsPanning)
            {
                _client.View.PanFrom(_panOrigin.x, _panOrigin.y, screen.x - _gesture.StartX, screen.y - _gesture.StartY);
            }
        }

        /// <summary>The left button came up at <paramref name="nowMs"/>: let go, finish a pan, or whistle.</summary>
        public GestureResult PointerUp(double nowMs)
        {
            ReleaseHold();
            GestureResult result = _gesture.Release(nowMs);
            if (result == GestureResult.Whistle)
            {
                var at = new Vector2(_gesture.WorldX, _gesture.WorldY);
                _client.Whistle(at);
                Whistled?.Invoke(at);
            }

            return result;
        }

        /// <summary>One wheel movement at <paramref name="screen"/>: positive zooms in, negative out.</summary>
        public void Wheel(Vector2 screen, float scrollY)
        {
            Screen = screen;
            if (_client.IsMenuOpen) return;
            if (_client.TetherFollow.Active)
            {
                // UC-T34: tethered, the camera frames the rope; the wheel sets how close it may come, not the view itself.
                if (scrollY != 0f && !float.IsNaN(scrollY)) _client.TetherFollow.Wheel(scrollY > 0f ? CameraView.WheelStep : 1f / CameraView.WheelStep);
                return;
            }

            Vector2 size = _client.ScreenSize;
            _client.View.Wheel(screen.x, screen.y, scrollY, size.x, size.y);
        }

        /// <summary>The pointer left the window (it is no longer drawn).</summary>
        public void PointerLeft()
        {
            if (Holding.IsHit) return;   // keep drawing a drag that runs off the edge
            Screen = null;
        }

        /// <summary>Drops the press and lets go of anything held (focus lost, menu opened), as Cursory's blur handler does.</summary>
        public void CancelAll()
        {
            _gesture.Cancel();
            ReleaseHold();
        }

        private void ReleaseHold()
        {
            if (!Holding.IsHit) return;
            Holding = PickResult.Nothing;
            CursorAvatar me = _client.LocalAvatar;
            if (me != null && me.IsSpawned) me.Release();
        }
    }
}
