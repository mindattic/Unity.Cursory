using System;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// Cursory's feel constants (Cursory.Core/Services/RoomState.cs) and the Mass rules built on them (BIBLE
    /// UC-LAW-2), plus the physical cursor and rope of UC-T33. Lengths are world pixels.
    /// </summary>
    /// <remarks>
    /// Since UC-T33 a player's pull is not a joint on the body: each cursor is a light dynamic body pulled toward the
    /// player's pointer by a spring capped at <see cref="GrabMaxForce"/>, and a rope of at most
    /// <see cref="RopeLengthPx"/> ties it to what it holds. A taut rope carries the cursor's pull to the body, so
    /// one cursor still pulls with at most <see cref="GrabMaxForce"/> and the Mass thresholds are unchanged.
    /// </remarks>
    public static class Tuning
    {
        /// <summary>One player's pull strength, in newtons: the cap on the spring (<c>TargetJoint2D</c> maxForce) that pulls
        /// their cursor toward their pointer, and so the most tension their rope can carry.</summary>
        public const float GrabMaxForce = 60f;

        /// <summary>The cursor's physical mass, in kilograms: light, so it moves like a cursor when free and is dragged
        /// along by anything heavier than its pull.</summary>
        public const float CursorMassKg = 0.1f;

        /// <summary>The cursor spring's stiffness in Hz: the <c>TargetJoint2D</c> frequency on the cursor's body.</summary>
        public const float CursorFrequency = 6f;

        /// <summary>The cursor spring's damping ratio (critically damped: it settles on the pointer without overshoot).</summary>
        public const float CursorDamping = 1f;

        /// <summary>
        /// Linear damping on the cursor's body: heavy, so a free cursor tops out near 20 m/s (2000 px/s) and a rope it
        /// snaps taut gives a heavy body no more than a nudge its friction soaks up.
        /// </summary>
        public const float CursorLinearDamping = 30f;

        /// <summary>Newtons per mass unit. A body's ground friction is Mass times this, and a pull's
        /// strength in mass units is its force divided by this, so a body moves exactly when the pulls
        /// on it sum past its Mass.</summary>
        public const float ForcePerMass = 40f;

        /// <summary>Kilograms of inertia per mass unit: <c>rb.mass = Mass × 5</c>.</summary>
        public const float InertiaKgPerMass = 5f;

        /// <summary>Rotational friction as a multiple of a body's linear friction.</summary>
        public const float FrictionTorqueScale = 1.2f;

        /// <summary>Linear damping on every body, so a released body coasts to a stop.</summary>
        public const float LinearDamping = 1.5f;

        /// <summary>Angular damping on every body.</summary>
        public const float AngularDamping = 2.0f;

        /// <summary>One grab's pull ceiling in mass units (60 ÷ 40 = 1.5). One cursor moves anything
        /// lighter than this; heavier bodies need cooperating cursors.</summary>
        public const float SingleGrabMaxMass = GrabMaxForce / ForcePerMass;

        /// <summary>A rope's maximum length (anchor, round any corners it has caught, to the cursor).</summary>
        public const float RopeLengthPx = 300f;

        /// <summary>
        /// How far past a taut rope's end the pointer has to be for the cursor spring to pull at its cap: the spring's
        /// stiffness is <see cref="CursorMassKg"/> × (2π <see cref="CursorFrequency"/>)², so it saturates at
        /// <see cref="GrabMaxForce"/> over that (about 42 px).
        /// </summary>
        public static float CursorSaturationPx => WorldUnits.ToPixels(GrabMaxForce / CursorStiffness);

        /// <summary>The cursor spring's stiffness in newtons per metre.</summary>
        public static float CursorStiffness
        {
            get
            {
                var omega = 2f * (float)Math.PI * CursorFrequency;
                return CursorMassKg * omega * omega;
            }
        }

        /// <summary>
        /// The pull a cursor spring gives when the pointer is <paramref name="stretchPx"/> past where the cursor is held:
        /// stiffness times the stretch, capped at <see cref="GrabMaxForce"/>. Never negative.
        /// </summary>
        public static float CursorPull(float stretchPx)
        {
            if (!(stretchPx > 0f)) return 0f;
            return Math.Min(GrabMaxForce, CursorStiffness * WorldUnits.ToMeters(stretchPx));
        }

        /// <summary>The cursor's collision radius when cursor collision is on.</summary>
        public const float CursorRadius = 10f;

        /// <summary>Distance within which a released wire end snaps onto a terminal.</summary>
        public const float WireSnapRadius = 90f;

        /// <summary>How far a level's Mass must sit from a cursor-count threshold (1.5, 3.0, …) so that
        /// solver softness never decides whether a body moves.</summary>
        public const float MassThresholdClearance = 0.2f;

        private const float Epsilon = 1e-4f;

        /// <summary>Ground friction force for a body of <paramref name="mass"/>: the <c>FrictionJoint2D</c> maxForce.</summary>
        public static float FrictionForce(float mass) => mass * ForcePerMass;

        /// <summary>Ground friction torque for a body of <paramref name="mass"/>: the <c>FrictionJoint2D</c> maxTorque.</summary>
        public static float FrictionTorque(float mass) => FrictionForce(mass) * FrictionTorqueScale;

        /// <summary>The Physics2D rigid body mass for a body of printed <paramref name="mass"/>.</summary>
        public static float RigidbodyMass(float mass) => mass * InertiaKgPerMass;

        /// <summary>
        /// How many cursors pulling flat out it takes to move a body: the fewest n with
        /// n × <see cref="SingleGrabMaxMass"/> past <paramref name="mass"/>. 1.0 needs one, 2.0 needs two.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="mass"/> is not positive.</exception>
        public static int CursorsNeeded(float mass)
        {
            if (!(mass > 0f)) throw new ArgumentOutOfRangeException(nameof(mass), mass, "must be positive");
            return (int)Math.Floor(mass / SingleGrabMaxMass + Epsilon) + 1;
        }

        /// <summary>
        /// True when <paramref name="mass"/> is at least <see cref="MassThresholdClearance"/> away from every
        /// multiple of <see cref="SingleGrabMaxMass"/>, the rule every level mass keeps (1.3 and 2.0 pass; 1.4 fails).
        /// </summary>
        public static bool IsClearOfThresholds(float mass)
        {
            if (!(mass > 0f)) return false;
            var nearest = (float)Math.Round(mass / SingleGrabMaxMass) * SingleGrabMaxMass;
            if (nearest <= 0f) return true;
            return Math.Abs(mass - nearest) + Epsilon >= MassThresholdClearance;
        }
    }
}
