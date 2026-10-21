using System;
using MindAttic.Unity.Multiplayer;
using MindAttic.Unity.Multiplayer.Core;
using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Steps the server's <see cref="PhysicsWorld"/> once per network tick (BIBLE UC-LAW-1): Physics2D runs in
    /// <see cref="SimulationMode2D.Script"/> mode, so nothing moves unless this calls
    /// <see cref="PhysicsWorld.Step"/>, and it only listens to a tick clock on the server. A client never
    /// simulates; it shows what the server replicates.
    /// </summary>
    /// <remarks>
    /// Bind it to a <see cref="NetSession"/> with <see cref="Bind"/>: when the session starts as a Host or
    /// Server it attaches to <see cref="NetSession.Clock"/> (NGO's <c>NetworkTickSystem.Tick</c>), and when the
    /// session ends it detaches. Tests attach any <see cref="ITickClock"/> directly with <see cref="Attach"/>.
    /// Each tick steps exactly <see cref="SimulationRate.TickSeconds"/>, whatever the frame rate.
    /// </remarks>
    public sealed class ServerSimulation : IDisposable
    {
        private ITickClock _clock;
        private NetSession _session;

        /// <summary>Puts Physics2D in Script mode, so the world only moves when a tick steps it.</summary>
        public ServerSimulation()
        {
            UseScriptMode();
        }

        /// <summary>The world each tick steps; null steps nothing. Swap it on a level load or reset.</summary>
        public PhysicsWorld World { get; set; }

        /// <summary>True while a tick clock is attached, which happens only on the server.</summary>
        public bool IsAttached => _clock != null;

        /// <summary>Ticks stepped since construction.</summary>
        public long StepCount { get; private set; }

        /// <summary>Raised after each step, with the clock's tick, for the server's per-tick game logic.</summary>
        public event Action<long> Stepped;

        /// <summary>Stops every Physics2D auto-step (FixedUpdate or Update); the simulation advances only by hand.</summary>
        public static void UseScriptMode()
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
        }

        /// <summary>
        /// Follows <paramref name="session"/>: attaches to its clock whenever it starts as a server (Host or
        /// Server) and detaches when it ends. A client session never attaches. Replaces any earlier binding.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
        public void Bind(NetSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            Unbind();
            _session = session;
            _session.Started += OnSessionStarted;
            _session.Ended += OnSessionEnded;
            if (_session.Clock != null) OnSessionStarted();
        }

        /// <summary>Stops following the bound session and detaches from its clock.</summary>
        public void Unbind()
        {
            if (_session == null) return;
            _session.Started -= OnSessionStarted;
            _session.Ended -= OnSessionEnded;
            _session = null;
            Detach();
        }

        /// <summary>Steps on <paramref name="clock"/>'s ticks from now on, replacing any clock attached before.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
        public void Attach(ITickClock clock)
        {
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            Detach();
            UseScriptMode();
            _clock = clock;
            _clock.Ticked += OnTick;
        }

        /// <summary>Stops stepping. Safe to call when nothing is attached.</summary>
        public void Detach()
        {
            if (_clock == null) return;
            _clock.Ticked -= OnTick;
            _clock = null;
        }

        /// <summary>Unbinds, detaches and disposes the <see cref="World"/>.</summary>
        public void Dispose()
        {
            Unbind();
            Detach();
            World?.Dispose();
            World = null;
        }

        private void OnSessionStarted()
        {
            if (_session == null || !_session.IsServer || _session.Clock == null) return;
            Attach(_session.Clock);
        }

        private void OnSessionEnded(Disconnection reason)
        {
            Detach();
        }

        private void OnTick(long tick)
        {
            var world = World;
            if (world == null) return;
            world.Step(SimulationRate.TickSeconds);
            StepCount++;
            Stepped?.Invoke(tick);
        }
    }
}
