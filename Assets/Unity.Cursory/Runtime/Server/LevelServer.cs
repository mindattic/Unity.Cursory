using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Cursory.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// The server's level host (UC-T13): sits beside the scene's <see cref="NetBootstrap"/> and, when its session
    /// starts as a Host or a dedicated Server, loads the level library, builds the <see cref="PhysicsWorld"/> for
    /// the current level and binds a <see cref="ServerSimulation"/> to the session's tick clock (BIBLE UC-LAW-1).
    /// A client session never builds a world. The session ending disposes the world.
    /// <para>On every peer, before the session starts, it registers the UC-T7 network prefabs
    /// (<see cref="NetPrefabs.RegisterAll"/>) and makes the <see cref="CursorAvatar"/> the player prefab. The
    /// server's bodies replicate through the returned <see cref="NgoBodySync"/>, and a <see cref="GeometryBroadcast"/>
    /// sends the static geometry after each level load (UC-LAW-4). A <see cref="LevelStateServer"/> runs the goal
    /// pass each tick and publishes it through a spawned <see cref="LevelStateSync"/> (UC-T9). Its Start runs before NetBootstrap's, which is
    /// what starts the session.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class LevelServer : MonoBehaviour
    {
        [Tooltip("The level the server plays first (clamped to the library's range).")]
        [SerializeField, Min(1)] private int startLevel = 1;

        private NetSession _session;
        private IBodySync _bodySync;
        private GeometryBroadcast _geometry;
        private LevelStateServer _levelState;
        private LevelStateSync _levelStateSync;

        /// <summary>The levels being played, or null until a server session starts.</summary>
        public LevelLoader Loader { get; private set; }

        /// <summary>The tick-stepped simulation, or null until a server session starts.</summary>
        public ServerSimulation Simulation { get; private set; }

        /// <summary>True while a server session is running a level.</summary>
        public bool IsServing => Simulation != null && Simulation.World != null;

        /// <summary>The server's goal pass and wire state (UC-T9), or null until a server session starts.</summary>
        public LevelStateServer LevelState => _levelState;

        private void Start()
        {
            NetBootstrap bootstrap = GetComponent<NetBootstrap>();
            if (bootstrap == null) bootstrap = NetBootstrap.Instance;
            if (bootstrap == null || bootstrap.Session == null)
            {
                Debug.LogWarning("[Unity.Cursory] LevelServer found no NetBootstrap session; the server will run no level.", this);
                return;
            }

            _session = bootstrap.Session;
            if (_session.Clock != null)
            {
                Debug.LogWarning("[Unity.Cursory] The session started before LevelServer registered the network prefabs.", this);
            }

            _bodySync = NetPrefabs.RegisterAll(_session);
            NetworkManager manager = bootstrap.GetComponent<NetworkManager>();
            if (manager != null && manager.NetworkConfig != null) manager.NetworkConfig.PlayerPrefab = NetPrefabs.LoadCursorAvatar();

            _session.Started += OnStarted;
            _session.Ended += OnEnded;
            if (_session.Clock != null) OnStarted();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Started -= OnStarted;
                _session.Ended -= OnEnded;
                _session = null;
            }

            StopServing();
        }

        private void OnStarted()
        {
            if (_session == null || !_session.IsServer || IsServing) return;

            LevelLibrary library = LevelLibrary.Load();
            if (library == null)
            {
                Debug.LogError("[Unity.Cursory] No level library at Resources/" + LevelLibrary.ResourcePath + "; the server runs no level.", this);
                return;
            }

            Loader = library.CreateLoader();
            if (startLevel != 1) Loader.Load(startLevel);
            Simulation = new ServerSimulation { Loader = Loader };
            Simulation.World = new PhysicsWorld(Loader.World, "Level " + Loader.CurrentLevel, _bodySync);
            Simulation.Bind(_session);
            _geometry = new GeometryBroadcast();
            _geometry.Bind(Simulation);
            _levelState = new LevelStateServer(Simulation, SpawnLevelState());
            _levelState.Track(Loader.World);
            Debug.Log("[Unity.Cursory] Serving level " + Loader.CurrentLevel + " of " + Loader.LevelCount + ".");
        }

        private void OnEnded(Disconnection reason)
        {
            StopServing();
        }

        /// <summary>Spawns the server-owned <see cref="LevelStateSync"/> every client reads goal, bulb and wire state from.</summary>
        private LevelStateSync SpawnLevelState()
        {
            GameObject prefab = NetPrefabs.LoadLevelState();
            if (prefab == null) return null;
            GameObject go = Instantiate(prefab);
            go.GetComponent<NetworkObject>().Spawn();
            _levelStateSync = go.GetComponent<LevelStateSync>();
            return _levelStateSync;
        }

        private void StopServing()
        {
            _levelState?.Dispose();
            _levelState = null;
            if (_levelStateSync != null)
            {
                NetworkObject networkObject = _levelStateSync.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsSpawned) networkObject.Despawn();
                else Destroy(_levelStateSync.gameObject);
                _levelStateSync = null;
            }

            _geometry?.Dispose();
            _geometry = null;
            Simulation?.Dispose();
            Simulation = null;
            Loader = null;
        }
    }
}
