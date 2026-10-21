using System;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// Cursory's feel constants (Cursory.Core/Services/RoomState.cs), kept unchanged so the port plays
    /// the same, and the Mass rules built on them (BIBLE UC-LAW-2). Lengths are world pixels.
    /// </summary>
    public static class Tuning
    {
        /// <summary>Force ceiling of one grab, in newtons: the <c>TargetJoint2D</c> maxForce.</summary>
        public const float GrabMaxForce = 60f;

        /// <summary>Grab joint stiffness in Hz: the <c>TargetJoint2D</c> frequency.</summary>
        public const float GrabFrequency = 5f;

        /// <summary>Grab joint damping ratio: the <c>TargetJoint2D</c> dampingRatio.</summary>
        public const float GrabDamping = 0.7f;

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

        /// <summary>Tether length at which a pull saturates, and the leash length.</summary>
        public const float MaxPullPx = 300f;

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
