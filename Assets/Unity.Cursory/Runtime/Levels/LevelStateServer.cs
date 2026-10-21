using System;
using Unity.Cursory.Core;

namespace Unity.Cursory
{
    /// <summary>
    /// The server's per-tick win detection (Cursory's goal pass after each step): after every
    /// <see cref="ServerSimulation"/> step it evaluates the tracked level's <see cref="LevelProgress"/> against the
    /// bodies' Physics2D poses and publishes the result through a <see cref="LevelStateSync"/>.
    /// </summary>
    /// <remarks>
    /// <para>It tracks the simulation's <see cref="LevelLoader.World"/> (the same <see cref="LevelData"/> the
    /// <see cref="PhysicsWorld"/> is built from) and re-tracks it by itself after a load or reset; without a loader,
    /// call <see cref="Track"/>. The tracked <see cref="LevelProgress"/> is also <see cref="ServerSimulation.Progress"/>.</para>
    /// <para>Wire-end grabbing goes through it: <see cref="CursorAvatar"/>'s server handlers call
    /// <see cref="LevelProgress.GrabWireEnd"/> (keyed by the cursor's owner client id) on <c>GrabWireEnd</c>,
    /// <see cref="LevelProgress.MoveCursor"/> on every <c>Move</c> so the held end follows, and
    /// <see cref="LevelProgress.ReleaseWireEnd"/> on <c>Release</c>, which snaps it within
    /// <see cref="Tuning.WireSnapRadius"/>. The next tick's evaluation lights the bulb and publishes the wires.</para>
    /// </remarks>
    public sealed class LevelStateServer : IDisposable
    {
        private ServerSimulation _sim;

        /// <summary>Evaluates after every step of <paramref name="sim"/>; publishes through <paramref name="sync"/> when it is not null.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="sim"/> is null.</exception>
        public LevelStateServer(ServerSimulation sim, LevelStateSync sync = null)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            Sync = sync;
            _sim.Stepped += OnStepped;
        }

        /// <summary>The replicated state this publishes into; null publishes nothing.</summary>
        public LevelStateSync Sync { get; set; }

        /// <summary>The tracked level's goals, bulb and wires; null until <see cref="Track"/>.</summary>
        public LevelProgress Progress { get; private set; }

        /// <summary>Raised on the server the tick the tracked level becomes won.</summary>
        public event Action<int> LevelWon;

        /// <summary>Starts tracking <paramref name="world"/>, the live copy of the level the physics world was built from, and publishes it at once.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
        public LevelProgress Track(LevelData world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            Progress = new LevelProgress(world);
            if (_sim != null) _sim.Progress = Progress;
            Evaluate();
            return Progress;
        }

        /// <summary>
        /// One goal pass now: evaluate against the current physics world and publish. Also run after every step.
        /// When the simulation's <see cref="ServerSimulation.Loader"/> has moved to a new <see cref="LevelLoader.World"/>
        /// (a load or reset), that world is tracked first.
        /// </summary>
        public void Evaluate()
        {
            var loader = _sim?.Loader;
            if (loader != null && loader.World != null && (Progress == null || Progress.World != loader.World))
            {
                Track(loader.World);
                return;
            }

            var progress = Progress;
            if (progress == null) return;
            var wasWon = progress.IsLevelSolved;
            progress.Evaluate(Poses(_sim?.World));
            Sync?.Publish(progress);
            if (!wasWon && progress.IsLevelSolved) LevelWon?.Invoke(progress.World.Number);
        }

        /// <summary>A <see cref="PoseLookup"/> over <paramref name="world"/>'s bodies; knows no body when it is null.</summary>
        public static PoseLookup Poses(PhysicsWorld world)
        {
            return (string id, out float x, out float y, out float angle) =>
            {
                if (world == null || !world.HasBody(id))
                {
                    x = y = angle = 0f;
                    return false;
                }
                (x, y, angle) = world.Pose(id);
                return true;
            };
        }

        /// <summary>Stops evaluating.</summary>
        public void Dispose()
        {
            if (_sim == null) return;
            _sim.Stepped -= OnStepped;
            if (_sim.Progress == Progress) _sim.Progress = null;
            _sim = null;
        }

        private void OnStepped(long tick) => Evaluate();
    }
}
