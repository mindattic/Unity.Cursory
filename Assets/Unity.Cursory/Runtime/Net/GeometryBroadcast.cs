using System;
using Unity.Cursory.Core;

namespace Unity.Cursory
{
    /// <summary>
    /// Wires a <see cref="LevelLoader"/>'s geometry-resend flag to every spawned <see cref="CursorAvatar"/> (BIBLE
    /// UC-LAW-4): once per <see cref="ServerSimulation"/> tick, a pending resend after a level load or reset goes
    /// out to every connected cursor. A newly spawned cursor gets the current geometry right away, from
    /// <see cref="CursorAvatar.OnSpawned"/> itself, so nothing here is needed for a late joiner.
    /// </summary>
    public sealed class GeometryBroadcast : IDisposable
    {
        private ServerSimulation _sim;

        /// <summary>Follows <paramref name="sim"/>'s ticks from now on, replacing any simulation bound before.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="sim"/> is null.</exception>
        public void Bind(ServerSimulation sim)
        {
            if (sim == null)
            {
                throw new ArgumentNullException(nameof(sim));
            }

            Unbind();
            _sim = sim;
            _sim.GeometryChanged += OnGeometryChanged;
        }

        /// <summary>Stops following the bound simulation. Safe to call when nothing is bound.</summary>
        public void Unbind()
        {
            if (_sim == null)
            {
                return;
            }

            _sim.GeometryChanged -= OnGeometryChanged;
            _sim = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Unbind();
        }

        private void OnGeometryChanged(LevelGeometry geometry)
        {
            if (_sim != null)
            {
                CursorAvatar.BroadcastGeometry(_sim.BoundSession, geometry);
            }
        }
    }
}
