using System;
using System.Collections.Generic;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Unity.Cursory
{
    /// <summary>
    /// The client presentation (UC-T11): everything a player sees and does on a peer that shows the game, bound to one
    /// <see cref="NetSession"/>. It owns the camera (<see cref="CameraView"/>: drag empty space to pan, wheel to zoom
    /// 0.1 to 4), the pointer (<see cref="PointerInput"/>: grab, release, and click-vs-drag whistles), the world
    /// (<see cref="WorldView"/>), the HUD (<see cref="CursoryHud"/>, from MindAttic Components), the Esc menu
    /// (<see cref="CursoryPauseMenu"/>), the minimap (<see cref="MinimapView"/>) and whistle sound
    /// (<see cref="WhistleAudio"/>). It sends only intents (BIBLE UC-LAW-1): <see cref="CursorAvatar"/>'s Move and
    /// grabs, and the <see cref="VoteBoard"/>'s votes and whistles.
    /// </summary>
    /// <remarks>
    /// <para>A player build creates one at scene load next to the scene's <see cref="NetBootstrap"/>
    /// (<see cref="Boot"/>). A headless process (<c>-batchmode</c>, a dedicated server build, or a <c>-bot</c>) creates
    /// none: <see cref="ShouldPresent"/>. Tests create one with <see cref="Create"/> and drive its
    /// <see cref="Pointer"/> directly (<see cref="ReadDevices"/> off).</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CursoryClient : MonoBehaviour
    {
        /// <summary>Cursor positions sent a second (the server accepts up to 30; a grab's own Move needs headroom).</summary>
        public const float MovesPerSecond = 25f;

        private static LevelLibrary s_Library;
        private static LevelLoader s_Definitions;

        private readonly Dictionary<string, Transform> _bodies = new Dictionary<string, Transform>();
        private readonly Dictionary<string, BodyPose> _poses = new Dictionary<string, BodyPose>();
        private readonly List<(float, float, float, float)> _wireEnds = new List<(float, float, float, float)>();
        private NetSession _session;
        private TopDownCamera _camera;
        private VoteBoard _board;
        private LevelStateSync _levelState;
        private float _nextMove;
        private float _nextLookup;
        private Vector2? _lastSent;

        /// <summary>The session this presents.</summary>
        public NetSession Session => _session;

        /// <summary>The camera's centre and zoom, in world pixels.</summary>
        public CameraView View { get; } = new CameraView();

        /// <summary>The top-down camera this drives.</summary>
        public TopDownCamera Camera => _camera;

        /// <summary>The pointer: grabs, pan, whistles, zoom.</summary>
        public PointerInput Pointer { get; private set; }

        /// <summary>The world picture.</summary>
        public WorldView World { get; private set; }

        /// <summary>The HUD.</summary>
        public CursoryHud Hud { get; private set; }

        /// <summary>The Esc menu.</summary>
        public CursoryPauseMenu Pause { get; private set; }

        /// <summary>The minimap.</summary>
        public MinimapView Minimap { get; private set; }

        /// <summary>Whistle sound.</summary>
        public WhistleAudio Audio { get; private set; }

        /// <summary>When true (the default), the mouse drives <see cref="Pointer"/> every frame; tests turn it off and drive it themselves.</summary>
        public bool ReadDevices { get; set; } = true;

        /// <summary>A fixed screen size to use instead of the real one (tests; null uses <see cref="UnityEngine.Screen"/>).</summary>
        public Vector2? ScreenOverride { get; set; }

        /// <summary>The screen size in pixels the camera maths uses.</summary>
        public Vector2 ScreenSize
        {
            get
            {
                if (ScreenOverride.HasValue) return ScreenOverride.Value;
                UnityEngine.Camera cam = _camera != null ? _camera.GetComponent<UnityEngine.Camera>() : null;
                if (cam != null && cam.pixelWidth > 0 && cam.pixelHeight > 0) return new Vector2(cam.pixelWidth, cam.pixelHeight);
                return UnityEngine.Screen.width > 0 && UnityEngine.Screen.height > 0
                    ? new Vector2(UnityEngine.Screen.width, UnityEngine.Screen.height)
                    : new Vector2(1920f, 1080f);
            }
        }

        /// <summary>This peer's own cursor, or null before it has spawned (and always on a dedicated server).</summary>
        public CursorAvatar LocalAvatar => CursorAvatar.Local(_session);

        /// <summary>The room's vote board as this peer sees it; null until it has spawned here.</summary>
        public VoteBoard Board => _board;

        /// <summary>The room's level state as this peer sees it; null until it has spawned here.</summary>
        public LevelStateSync LevelState => _levelState;

        /// <summary>The level being played, as this peer knows it (0 before it knows).</summary>
        public int LevelNumber
        {
            get
            {
                if (_levelState != null && _levelState.IsSpawned && _levelState.LevelNumber > 0) return _levelState.LevelNumber;
                CursorAvatar me = LocalAvatar;
                return me != null && me.Geometry != null ? me.Geometry.LevelNumber : 0;
            }
        }

        /// <summary>The replicated bodies of the current level, by id (empty while a level is being swapped).</summary>
        public IReadOnlyDictionary<string, Transform> Bodies => _bodies;

        /// <summary>True while the Esc menu is open: the world ignores the pointer.</summary>
        public bool IsMenuOpen => Pause != null && Pause.IsOpen;

        /// <summary>
        /// True when this process should show the game: not batch mode (a dedicated server, a CI run, a headless bot),
        /// not a dedicated server build, and not started with <see cref="BotDriver.BotArg"/>.
        /// </summary>
        public static bool ShouldPresent(string[] args, bool batchMode)
        {
#if UNITY_SERVER
            return false;
#else
            if (batchMode) return false;
            return BotDriver.Arg(args, BotDriver.BotArg) == null && !HasFlag(args, BotDriver.BotArg);
#endif
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!ShouldPresent(Environment.GetCommandLineArgs(), Application.isBatchMode)) return;
            NetBootstrap bootstrap = NetBootstrap.Instance;
            if (bootstrap == null || bootstrap.Session == null || FindAnyObjectByType<CursoryClient>() != null) return;
            Create(bootstrap.Session, UnityEngine.Camera.main);
        }

        /// <summary>Builds the presentation for <paramref name="session"/>, driving <paramref name="camera"/> (the main camera when null).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
        public static CursoryClient Create(NetSession session, UnityEngine.Camera camera = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (camera == null) camera = UnityEngine.Camera.main;
            if (camera == null)
            {
                var cameraGo = new GameObject("Cursory Camera") { tag = "MainCamera" };
                camera = cameraGo.AddComponent<UnityEngine.Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.07f, 0.08f, 0.10f, 1f);
            }

            var go = new GameObject("Cursory Client");
            CursoryClient client = go.AddComponent<CursoryClient>();
            client._session = session;
            client._camera = camera.GetComponent<TopDownCamera>();
            if (client._camera == null) client._camera = camera.gameObject.AddComponent<TopDownCamera>();
            client.Pointer = new PointerInput(client);
            client.World = new GameObject("Cursory World").AddComponent<WorldView>();
            client.World.transform.SetParent(go.transform, false);
            client.Audio = go.AddComponent<WhistleAudio>();
            client.Hud = CursoryHud.Create(session, LevelCount());
            client.Hud.PauseRequested += () => client.Pause.Open();
            client.Minimap = MinimapView.Create(client.Hud.Canvas);
            client.Pause = CursoryPauseMenu.Create(() => client.LocalAvatar);
            client.Pause.Stack.Opened += client.Pointer.CancelAll;
            client.ApplyCamera();
            return client;
        }

        /// <summary>The level definition every client holds for level <paramref name="number"/> (the Resources library, else Core's seeds); null when none.</summary>
        public static LevelData LevelDefinition(int number)
        {
            if (s_Definitions == null)
            {
                if (s_Library == null) s_Library = LevelLibrary.Load();
                s_Definitions = s_Library != null ? s_Library.CreateLoader() : new LevelLoader(LevelSeeds.All());
            }

            return number >= 1 && s_Definitions.Exists(number) ? s_Definitions.Definition(number) : null;
        }

        private static int LevelCount()
        {
            LevelDefinition(1);
            return s_Definitions != null ? s_Definitions.LevelCount : LevelSeeds.Count;
        }

        /// <summary>What a press at <paramref name="worldPx"/> lands on, against the current level and its live bodies and wires.</summary>
        public PickResult Pick(Vector2 worldPx)
        {
            LevelData level = LevelDefinition(LevelNumber);
            if (level == null) return PickResult.Nothing;
            CollectBodies(level);
            _poses.Clear();
            foreach (KeyValuePair<string, Transform> body in _bodies)
            {
                if (body.Value != null) _poses[body.Key] = ClientBodies.PoseOf(body.Value);
            }

            _wireEnds.Clear();
            if (_levelState != null && _levelState.IsSpawned && _levelState.LevelNumber == level.Number)
            {
                foreach (Vector4 e in _levelState.WireEnds) _wireEnds.Add((e.x, e.y, e.z, e.w));
            }

            return BodyPicker.Pick(level, _poses, _wireEnds.Count == level.Wires.Count ? _wireEnds : null, worldPx.x, worldPx.y);
        }

        /// <summary>Whistles at <paramref name="worldPx"/>: the ripple and tone play here at once, and the board tells everyone else.</summary>
        public void Whistle(Vector2 worldPx)
        {
            CursorAvatar me = LocalAvatar;
            World.AddRipple(worldPx, me != null ? me.PlayerColor : Color.white);
            Audio.Play(WhistleToneOf(me));
            if (_board != null && _board.IsSpawned) _board.Whistle(worldPx);
        }

        private int WhistleToneOf(CursorAvatar me) =>
            WhistleTone.For(me != null ? (ulong)Mathf.Max(0, me.SeatIndex) : _session.LocalClientId);

        private void Update()
        {
            FindRoomEntities();
            if (ReadDevices) ReadMouse();
            SendMove();
            FollowGrab();
        }

        private void LateUpdate()
        {
            ApplyCamera();
            LevelData level = LevelDefinition(LevelNumber);
            CollectBodies(level);
            IReadOnlyList<CursorAvatar> cursors = CursorAvatar.All(_session);
            World.Render(level, LevelNumber, _levelState, _bodies, cursors, LocalAvatar, Pointer.World);
            Hud.Refresh();
            Minimap.Draw(level, _levelState, _bodies, cursors, View, ScreenSize);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) Pointer?.CancelAll();
        }

        private void OnDestroy()
        {
            if (_board != null) _board.Whistled -= OnWhistled;
            Hud?.Destroy();
            Pause?.Destroy();
            Minimap?.Destroy();
        }

        /// <summary>Finds this session's board and level state as they spawn (and again after a new session).</summary>
        private void FindRoomEntities()
        {
            bool boardOk = _board != null && _board.IsSpawned;
            bool stateOk = _levelState != null && _levelState.IsSpawned;
            if ((boardOk && stateOk) || Time.unscaledTime < _nextLookup) return;
            _nextLookup = Time.unscaledTime + 0.25f;

            if (!boardOk)
            {
                VoteBoard board = null;
                foreach (VoteBoard candidate in FindObjectsByType<VoteBoard>())
                {
                    if (candidate.IsSpawned && candidate.Session == _session) board = candidate;
                }

                if (_board != board)
                {
                    if (_board != null) _board.Whistled -= OnWhistled;
                    _board = board;
                    if (_board != null) _board.Whistled += OnWhistled;
                    Hud.Bind(_board);
                }
            }

            if (!stateOk)
            {
                LevelStateSync state = null;
                foreach (LevelStateSync candidate in FindObjectsByType<LevelStateSync>())
                {
                    if (candidate.IsSpawned && candidate.Session == _session) state = candidate;
                }

                _levelState = state;
                Hud.Bind(_levelState);
            }
        }

        /// <summary>Somebody else whistled: their ripple and tone (our own already played when we clicked).</summary>
        private void OnWhistled(Core.Whistle whistle)
        {
            if (whistle.PlayerId == _session.LocalClientId) return;
            CursorAvatar who = CursorAvatar.ForClient(_session, whistle.PlayerId);
            Color color = who != null ? who.PlayerColor
                : HexColor.TryParse(whistle.Color, out byte r, out byte g, out byte b) ? (Color)new Color32(r, g, b, 255) : Color.white;
            World.AddRipple(new Vector2(whistle.X, whistle.Y), color);
            Audio.Play(whistle.Tone);
        }

        private void ReadMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 screen = mouse.position.ReadValue();
            Vector2 size = ScreenSize;
            bool inside = screen.x >= 0f && screen.y >= 0f && screen.x <= size.x && screen.y <= size.y && Application.isFocused;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            double now = Time.unscaledTimeAsDouble * 1000.0;

            if (!inside) Pointer.PointerLeft();
            else if (Pointer.Screen != screen) Pointer.PointerMove(screen);

            if (mouse.leftButton.wasPressedThisFrame && inside && !overUi) Pointer.PointerDown(screen, now);
            if (mouse.leftButton.wasReleasedThisFrame) Pointer.PointerUp(now);

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f && inside && !overUi) Pointer.Wheel(screen, scroll);
        }

        /// <summary>Sends the cursor's position (the world point under the pointer) at <see cref="MovesPerSecond"/>, only when it moved.</summary>
        private void SendMove()
        {
            if (IsMenuOpen || Time.unscaledTime < _nextMove) return;
            CursorAvatar me = LocalAvatar;
            Vector2? world = Pointer.World;
            if (me == null || !me.IsSpawned || !world.HasValue) return;
            if (_lastSent.HasValue && (_lastSent.Value - world.Value).sqrMagnitude < 0.01f) return;
            _nextMove = Time.unscaledTime + 1f / MovesPerSecond;
            _lastSent = world;
            me.Move(world.Value);
        }

        /// <summary>While dragging a body, ease the camera to the midpoint of the cursor and the body (Cursory's followCamera).</summary>
        private void FollowGrab()
        {
            PickResult held = Pointer.Holding;
            if (IsMenuOpen || (held.Kind != PickKind.Block && held.Kind != PickKind.Shape)) return;
            Vector2? world = Pointer.World;
            if (!world.HasValue || !_bodies.TryGetValue(held.Id, out Transform body) || body == null) return;
            BodyPose pose = ClientBodies.PoseOf(body);
            View.Follow((world.Value.x + pose.X) / 2f, (world.Value.y + pose.Y) / 2f);
        }

        private void CollectBodies(LevelData level)
        {
            CursorAvatar any = LocalAvatar;
            NetworkManager manager = any != null ? any.NetworkManager : _levelState != null ? _levelState.NetworkManager : null;
            ClientBodies.Collect(manager, level, _bodies);
        }

        /// <summary>Puts the Unity camera where <see cref="View"/> says.</summary>
        private void ApplyCamera()
        {
            if (_camera == null) return;
            _camera.Zoom = View.Zoom;
            _camera.transform.position = new Vector3(WorldUnits.ToMeters(View.X), WorldUnits.ToMeters(View.Y), -TopDownCamera.Height);
            _camera.Apply();
        }
    }
}
