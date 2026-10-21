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

        /// <summary>Command-line argument that picks the first level, overriding the Inspector (<c>-level 2</c>; UC-T14's nightly bots play level 2).</summary>
        public const string LevelArg = "-level";

        private NetSession _session;
        private IBodySync _bodySync;
        private GeometryBroadcast _geometry;
        private LevelStateServer _levelState;
        private LevelStateSync _levelStateSync;

        /// <summary>The levels being played, or null until a server session starts.</summary>
        public LevelLoader Loader { get; private set; }

        /// <summary>The tick-stepped simulation, or null until a server session starts.</summary>
        public ServerSimulation Simulation { get; private set; }

        /// <summary>
        /// The room's vote board (Unity.Multiplayer's, UM-T54), served with Cursory's reset and level kinds
        /// (<see cref="LevelVotes"/>) against <see cref="Loader"/>; null until a server session starts.
        /// </summary>
        public VoteBoard Votes { get; private set; }

        /// <summary>The spawned whistle board (UC-T10); null until a server session starts.</summary>
        public WhistleBoard Whistles { get; private set; }

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
            int first = LevelFromCommandLine(System.Environment.GetCommandLineArgs(), startLevel);
            if (first != 1) Loader.Load(first);
            Simulation = new ServerSimulation { Loader = Loader };
            Simulation.World = new PhysicsWorld(Loader.World, "Level " + Loader.CurrentLevel, _bodySync);
            Simulation.Bind(_session);
            _geometry = new GeometryBroadcast();
            _geometry.Bind(Simulation);
            _levelState = new LevelStateServer(Simulation, SpawnLevelState());
            _levelState.Track(Loader.World);
            SpawnVoteBoard();
            SpawnWhistleBoard();
            Debug.Log("[Unity.Cursory] Serving level " + Loader.CurrentLevel + " of " + Loader.LevelCount + ".");
        }

        /// <summary>
        /// Spawns the room's <see cref="VoteBoard"/> from the library's prefab and registers Cursory's two kinds: reset
        /// (re-seed the level) and level (load the level in the number payload; refused for the current level or one that
        /// doesn't exist). The library runs the rules: 2/3 of the players in the room, 15 s, one vote at a time.
        /// </summary>
        private void SpawnVoteBoard()
        {
            Votes = AddVoteKinds(VoteBoard.Spawn(_session), Loader);
        }

        /// <summary>Registers Cursory's reset and level vote kinds on <paramref name="board"/>, acting on <paramref name="loader"/>.</summary>
        public static VoteBoard AddVoteKinds(VoteBoard board, LevelLoader loader)
        {
            if (board == null) throw new System.ArgumentNullException(nameof(board));
            if (loader == null) throw new System.ArgumentNullException(nameof(loader));
            return board
                .AddKind(new VoteKind(LevelVotes.ResetKind, LevelVotes.ResetTitle) { Passed = v => LevelVotes.ApplyReset(loader) })
                .AddKind(new VoteKind(LevelVotes.LevelKind, r => LevelVotes.LevelTitle(r.Number))
                {
                    Check = r => LevelVotes.CheckLevel(loader, r.Number),
                    Passed = v => LevelVotes.ApplyLevel(loader, v.Number),
                });
        }

        /// <summary>Spawns the scene's one <see cref="WhistleBoard"/> (owned by the server).</summary>
        private void SpawnWhistleBoard()
        {
            GameObject prefab = NetPrefabs.LoadWhistleBoard();
            if (prefab == null)
            {
                Debug.LogError("[Unity.Cursory] No " + NetPrefabs.WhistleBoardResourcePath + " prefab; the server runs no whistles.", this);
                return;
            }

            GameObject board = Instantiate(prefab);
            board.name = "WhistleBoard";
            board.GetComponent<NetworkObject>().Spawn();
            Whistles = board.GetComponent<WhistleBoard>();
        }

        /// <summary>The level <see cref="LevelArg"/> names in <paramref name="args"/> (<c>-level 2</c> or <c>-level=2</c>), else <paramref name="fallback"/>.</summary>
        public static int LevelFromCommandLine(string[] args, int fallback)
        {
            if (args == null) return fallback;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = null;
                if (string.Equals(arg, LevelArg, System.StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) value = args[i + 1];
                else if (arg != null && arg.StartsWith(LevelArg + "=", System.StringComparison.OrdinalIgnoreCase)) value = arg.Substring(LevelArg.Length + 1);
                int level;
                if (value != null && int.TryParse(value, out level) && level >= 1) return level;
            }

            return fallback;
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
            if (Votes != null)
            {
                Votes.StopServing();
                if (Votes.IsSpawned) Votes.NetworkObject.Despawn(true);
                else Destroy(Votes.gameObject);
                Votes = null;
            }

            if (Whistles != null)
            {
                if (Whistles.IsSpawned) Whistles.NetworkObject.Despawn(true);
                else Destroy(Whistles.gameObject);
                Whistles = null;
            }

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
