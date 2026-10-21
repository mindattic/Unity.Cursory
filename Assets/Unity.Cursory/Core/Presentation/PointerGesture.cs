using System;

namespace Unity.Cursory.Core
{
    /// <summary>What a pointer release on empty space meant (<see cref="PointerGesture.Release"/>).</summary>
    public enum GestureResult
    {
        /// <summary>Nothing: no press was armed, or it was held too long to count as a click.</summary>
        None = 0,

        /// <summary>A quick click on empty space: whistle there (Cursory's click-vs-drag rule).</summary>
        Whistle = 1,

        /// <summary>The press had become a camera pan, which this release ends.</summary>
        PanEnded = 2,
    }

    /// <summary>
    /// Cursory's click-vs-drag rule for a press on empty space (room-core.js <c>onMouseDown</c>/<c>onMouseMove</c>/
    /// <c>onMouseUp</c>): the press arms both a whistle and a pan; moving <see cref="DragThresholdPx"/> screen pixels or
    /// more commits to a pan and cancels the whistle; a release within <see cref="ClickMaxMs"/> that never became a pan
    /// whistles at the world point that was pressed. A press on a body is a grab, never armed here.
    /// </summary>
    /// <example>
    /// <code>
    /// var g = new PointerGesture();
    /// g.Arm(100, 100, 0, worldX: 5000, worldY: 5000);
    /// g.Move(102, 101);                 // false: 2.2 px is still a click
    /// var result = g.Release(120);      // GestureResult.Whistle at (5000, 5000)
    /// </code>
    /// </example>
    public sealed class PointerGesture
    {
        /// <summary>Screen pixels a press may travel and still be a click (inclusive: exactly this far is a drag).</summary>
        public const float DragThresholdPx = 5f;

        /// <summary>Longest press, in milliseconds, that still whistles on release (inclusive).</summary>
        public const double ClickMaxMs = 250;

        /// <summary>True between <see cref="Arm"/> and the release while the press is still a candidate click.</summary>
        public bool IsArmed { get; private set; }

        /// <summary>True once the armed press has moved far enough to be a pan, until the release.</summary>
        public bool IsPanning { get; private set; }

        /// <summary>Screen point the press started at.</summary>
        public float StartX { get; private set; }

        /// <summary>Screen point the press started at.</summary>
        public float StartY { get; private set; }

        /// <summary>When the press started, in milliseconds on the caller's clock.</summary>
        public double StartMs { get; private set; }

        /// <summary>The world point (world pixels) under the press: where a click whistles.</summary>
        public float WorldX { get; private set; }

        /// <summary>The world point (world pixels) under the press: where a click whistles.</summary>
        public float WorldY { get; private set; }

        /// <summary>True when an offset of (<paramref name="dx"/>, <paramref name="dy"/>) screen pixels is a drag, not a click.</summary>
        public static bool IsDrag(float dx, float dy) => dx * dx + dy * dy >= DragThresholdPx * DragThresholdPx;

        /// <summary>True when a press held <paramref name="elapsedMs"/> milliseconds still counts as a click.</summary>
        public static bool IsClick(double elapsedMs) => elapsedMs >= 0 && elapsedMs <= ClickMaxMs;

        /// <summary>A press on empty space at screen (<paramref name="screenX"/>, <paramref name="screenY"/>) at <paramref name="nowMs"/>, over world point (<paramref name="worldX"/>, <paramref name="worldY"/>).</summary>
        public void Arm(float screenX, float screenY, double nowMs, float worldX, float worldY)
        {
            IsArmed = true;
            IsPanning = false;
            StartX = screenX;
            StartY = screenY;
            StartMs = nowMs;
            WorldX = worldX;
            WorldY = worldY;
        }

        /// <summary>
        /// The pointer moved to screen (<paramref name="screenX"/>, <paramref name="screenY"/>). Returns true exactly once:
        /// on the move that turns the armed press into a pan (the whistle is then cancelled).
        /// </summary>
        public bool Move(float screenX, float screenY)
        {
            if (!IsArmed || IsPanning) return false;
            if (!IsDrag(screenX - StartX, screenY - StartY)) return false;
            IsPanning = true;
            return true;
        }

        /// <summary>The press ended at <paramref name="nowMs"/>: what it meant. Always leaves the gesture idle.</summary>
        public GestureResult Release(double nowMs)
        {
            var result = GestureResult.None;
            if (IsPanning) result = GestureResult.PanEnded;
            else if (IsArmed && IsClick(nowMs - StartMs)) result = GestureResult.Whistle;
            Cancel();
            return result;
        }

        /// <summary>Drops the press without a result (focus lost, pause menu opened).</summary>
        public void Cancel()
        {
            IsArmed = false;
            IsPanning = false;
        }

        /// <inheritdoc />
        public override string ToString() =>
            IsPanning ? "Panning" : IsArmed ? FormattableString.Invariant($"Armed at ({StartX}, {StartY})") : "Idle";
    }
}
