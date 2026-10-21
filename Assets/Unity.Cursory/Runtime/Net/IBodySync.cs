using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// How a server-authoritative <see cref="Rigidbody2D"/>'s pose reaches every client (BIBLE UC-LAW-7, the one
    /// recorded exception to UC-LAW-7's "talk to the library, not NGO" rule). <see cref="PhysicsWorld"/> talks to
    /// this, never to NGO's <c>NetworkTransform</c>/<c>NetworkRigidbody2D</c> directly, so a compact snapshot can
    /// replace today's implementation (<see cref="NgoBodySync"/>) without touching gameplay code.
    /// </summary>
    public interface IBodySync
    {
        /// <summary>
        /// A new server-owned body at <paramref name="position"/> (metres) turned by <paramref name="rotation"/>,
        /// parented under <paramref name="parent"/>, already replicated to every client under <paramref name="key"/> (its
        /// level block or shape id), so a client finds its copy by that key (<see cref="ClientBodies"/>), not by spawn
        /// order: the copy shows the same x, y and rotation z, interpolated. <see cref="PhysicsWorld"/> adds the
        /// <see cref="Rigidbody2D"/>, colliders and joints to the object this returns exactly as it would to a plain one.
        /// </summary>
        GameObject SpawnBody(string key, string name, Vector2 position, Quaternion rotation, Transform parent);

        /// <summary>
        /// Takes a body <see cref="SpawnBody"/> made off every client now and destroys it, freeing its key at once so a
        /// world rebuilt in the same frame (a level reset) can spawn the same keys again.
        /// </summary>
        void DespawnBody(GameObject body);
    }
}
