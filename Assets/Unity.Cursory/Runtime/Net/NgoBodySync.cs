using System;
using Unity.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Today's <see cref="IBodySync"/> (BIBLE UC-LAW-7): NGO's <c>NetworkTransform</c> plus
    /// <c>NetworkRigidbody2D</c>, syncing x, y and rotation z only, half precision, interpolated. A body is spawned
    /// from <see cref="prefab"/> (see <see cref="NetPrefabs"/>) rather than built by hand, because only a real
    /// prefab asset gets a stable <c>GlobalObjectIdHash</c> every peer agrees on. Each body carries Unity.Multiplayer's
    /// <see cref="NetKey"/> with its level block or shape id (UM-T60), so clients find it by id in
    /// <see cref="NetSession.Objects"/> (<see cref="ClientBodies"/>).
    /// </summary>
    public sealed class NgoBodySync : IBodySync
    {
        private readonly NetSession _session;
        private readonly GameObject _prefab;

        /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="prefab"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="prefab"/> has no <see cref="NetworkObject"/> or no <see cref="NetKey"/>.</exception>
        public NgoBodySync(NetSession session, GameObject prefab)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (prefab.GetComponent<NetworkObject>() == null || prefab.GetComponent<NetKey>() == null)
            {
                throw new ArgumentException("The networked-body prefab needs a NetworkObject and a NetKey; run Unity.Cursory/Write Networked Prefabs.", nameof(prefab));
            }

            _session = session;
            _prefab = prefab;
        }

        /// <summary>Registers <paramref name="prefab"/> with <paramref name="session"/> if it is not already known. Call before the session starts.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="prefab"/> is null.</exception>
        public static void Register(NetSession session, GameObject prefab)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (!session.Prefabs.Contains(prefab))
            {
                session.Prefabs.Register(prefab);
            }
        }

        /// <inheritdoc />
        public GameObject SpawnBody(string key, string name, Vector2 position, Quaternion rotation, Transform parent)
        {
            // Keyed through Unity.Multiplayer's registry (UM-T60): every client finds this body by its block/shape id.
            GameObject go = _session.Objects.Spawn(_prefab, key, position, rotation, parent);
            go.name = name;
            return go;
        }

        /// <inheritdoc />
        public void DespawnBody(GameObject body)
        {
            if (body == null) return;
            NetworkObject networkObject = body.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn(true);
            }
            else
            {
                UnityEngine.Object.Destroy(body);
            }
        }

        /// <summary>
        /// Builds (but does not save) a template body: <see cref="NetworkObject"/>, a <see cref="Rigidbody2D"/>
        /// (dynamic, no gravity: <see cref="PhysicsWorld"/> configures it per body), <see cref="NetworkTransform"/>
        /// syncing position x/y and rotation z only (no scale), half precision, interpolated, and
        /// <see cref="NetworkRigidbody2D"/>, and a <see cref="NetKey"/> the server fills per body. <see cref="NetPrefabWriter"/> saves this as the real prefab asset
        /// every peer loads (<see cref="NetPrefabs.LoadNetworkedBody"/>); a test may use it directly instead.
        /// </summary>
        public static GameObject BuildTemplate(string name = "Unity.Cursory Networked Body")
        {
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();

            var transform = go.AddComponent<NetworkTransform>();
            transform.SyncPositionX = true;
            transform.SyncPositionY = true;
            transform.SyncPositionZ = false;
            transform.SyncRotAngleX = false;
            transform.SyncRotAngleY = false;
            transform.SyncRotAngleZ = true;
            transform.SyncScaleX = false;
            transform.SyncScaleY = false;
            transform.SyncScaleZ = false;
            transform.UseHalfFloatPrecision = true;
            transform.Interpolate = true;

            var rigidbody = go.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Dynamic;
            rigidbody.gravityScale = 0f;

            go.AddComponent<NetworkRigidbody2D>();
            go.AddComponent<NetKey>();
            return go;
        }
    }
}
