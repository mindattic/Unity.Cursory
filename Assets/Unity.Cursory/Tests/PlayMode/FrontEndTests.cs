using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MindAttic.Components.Unity;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using Unity.Multiplayer.UI;
using Unity.Multiplayer.UI.Testing;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T12: Cursory on the library's drop-in front end, driven through its menus the way players would. Two peers are
    /// wired as Main.unity wires its NetworkManager (NetBootstrap + LevelServer + NetFrontEnd, plus the CursoryClient a
    /// player build boots), each with its own name and colour, over real Unity Transport on a free port: the host hosts
    /// from the main menu and its lobby shows a join code (an in-process rendezvous service); the client joins by that
    /// code into level 1; the HUD shows only in the game; the in-game Esc menu carries Cursory's per-player switches and
    /// they reach the server; a refused join and the host ending the game are explained in plain words.
    /// </summary>
    public class FrontEndTests : NetcodeIntegrationTest
    {
        private static readonly Color Red = new Color32(0xE6, 0x4B, 0x3C, 0xFF);
        private static readonly Color Blue = new Color32(0x34, 0x98, 0xDB, 0xFF);
        private static readonly Vector2 ScreenSize = new Vector2(1920f, 1080f);

        private readonly List<GameObject> _objects = new List<GameObject>();
        private InProcessRendezvousServer _rendezvous;
        private ushort _port;
        private string _hostVersion;
        private string _clientVersion;
        private Peer _host;
        private Peer _client;

        /// <summary>One player's process: the scene's networked object and the presentation a player build boots.</summary>
        private sealed class Peer
        {
            public NetBootstrap Bootstrap;
            public LevelServer Server;
            public NetFrontEnd FrontEnd;
            public CursoryClient Client;
            public NetSession Session => Bootstrap.Session;
        }

        public FrontEndTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 1;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            foreach (NetworkManager manager in m_NetworkManagers) manager.NetworkConfig.EnableSceneManagement = false;
            InProcessRendezvousServer rendezvous = _rendezvous = new InProcessRendezvousServer();   // the closures outlive the field
            _port = FreePort.Udp();
            _hostVersion = _clientVersion = "uc-t12";
            FrontEndTesting.ForceMenus = true;   // a batch-mode run is headless: build the menus anyway
            // A host's Auto means Relay (not installed here), so the host names the rendezvous service outright.
            _host = CreatePeer(m_ServerNetworkManager, "Ann", Red, () =>
            {
                NetConfig config = rendezvous.ForServer(Config(_hostVersion));
                config.JoinCodes = JoinCodeProvider.Rendezvous;
                return config;
            });
            _client = CreatePeer(m_ClientNetworkManagers[0], "Bob", Blue, () => rendezvous.ForClient(Config(_clientVersion)));
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            foreach (Peer peer in new[] { _client, _host })
            {
                if (peer == null) continue;
                if (peer.Client != null) Object.Destroy(peer.Client.gameObject);
                if (peer.Session != null && peer.Session.IsActive) peer.Session.Leave();
            }

            yield return null;
            foreach (GameObject go in _objects)
            {
                if (go != null) Object.Destroy(go);
            }

            _objects.Clear();
            _rendezvous?.Dispose();
            _rendezvous = null;
            _host = _client = null;
            FrontEndTesting.ForceMenus = null;
            FrontEndTesting.CleanUpUi();
            yield return null;
        }

        private NetConfig Config(string version) => new NetConfig { Port = _port, GameVersion = version, JoinCodes = JoinCodeProvider.Auto };

        /// <summary>
        /// Main.unity's wiring on a harness NetworkManager: NetBootstrap (switched off before its Start, so a headless
        /// editor does not serve by itself; the front end still drives its session) + LevelServer + NetFrontEnd, and the
        /// CursoryClient a player build boots beside them.
        /// </summary>
        private Peer CreatePeer(NetworkManager manager, string name, Color color, System.Func<NetConfig> config)
        {
            var peer = new Peer();
            GameObject go = manager.gameObject;
            peer.Bootstrap = go.AddComponent<NetBootstrap>();
            peer.Bootstrap.enabled = false;
            peer.Bootstrap.Session.Identity = new PlayerIdentity(PlayerIdentity.NewToken(), name, default);   // one process, two players
            peer.Server = go.AddComponent<LevelServer>();
            peer.FrontEnd = go.AddComponent<NetFrontEnd>();
#if UNITY_EDITOR
            // Main.unity's settings asset (title "Cursory"), as the scene wires it.
            var wiring = new UnityEditor.SerializedObject(peer.FrontEnd);
            wiring.FindProperty("settings").objectReferenceValue =
                UnityEditor.AssetDatabase.LoadAssetAtPath<NetFrontEndSettings>("Assets/Unity.Cursory/Settings/FrontEndSettings.asset");
            wiring.ApplyModifiedPropertiesWithoutUndo();
#endif
            FrontEndTesting.Bind(peer.FrontEnd, peer.Bootstrap.Session, config);   // this test's port and rendezvous
            peer.FrontEnd.Identity = new FixedIdentitySource(name, color);
            peer.FrontEnd.Reconnect = new ReconnectPolicy { MaxAttempts = 0 };

            var cameraGo = new GameObject(name + "'s camera");
            _objects.Add(cameraGo);
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.10f, 1f);
            cameraGo.AddComponent<TopDownCamera>();
            peer.Client = CursoryClient.Create(peer.Bootstrap.Session, camera, peer.FrontEnd);
            peer.Client.ReadDevices = false;
            peer.Client.ScreenOverride = ScreenSize;
            return peer;
        }

        private static IEnumerator Wait(System.Func<bool> condition, string failure, float seconds = 10f)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("Timed out after " + seconds + " s: " + failure);
                yield return null;
            }
        }

        private static bool HudShown(Peer peer) => peer.Client.Hud.Canvas.gameObject.activeSelf;

        /// <summary>Both front ends up on their main menus.</summary>
        private IEnumerator MenusUp()
        {
            yield return null;   // Start: LevelServer registers Cursory's prefabs, the front ends build their menus
            Assert.That(_host.FrontEnd.HasMenus && _client.FrontEnd.HasMenus, Is.True, "ForceMenus builds the menus in batch mode");
            Assert.That(_host.FrontEnd.State, Is.EqualTo(FrontEndState.Menu));
            yield return null;
        }

        private IEnumerator HostFromTheMenu()
        {
            FrontEndTesting.Click(FrontEndTesting.MainScreen(_host.FrontEnd), "Host");
            yield return Wait(() => _host.FrontEnd.State == FrontEndState.Lobby, "the host never reached the lobby (" + _host.Session.State + ")");
            yield return Wait(() => _host.Server.IsServing, "the host never served a level");
        }

        private IEnumerator JoinByCodeFromTheMenu()
        {
            yield return Wait(() => FrontEndTesting.LobbyCode(_host.FrontEnd) != null, "the lobby never showed a join code");
            string code = FrontEndTesting.LobbyCode(_host.FrontEnd).Code;
            FrontEndTesting.Click(FrontEndTesting.MainScreen(_client.FrontEnd), "Join by code");
            MenuScreen join = _client.FrontEnd.MainMenu.Current;
            Assert.That(join.Title, Is.EqualTo("Join by code"));
            join.GetComponentInChildren<MaTextbox>().Text = code.Replace("-", string.Empty).ToLowerInvariant();
            FrontEndTesting.Click(join, "Join");
            yield return Wait(() => _client.FrontEnd.State == FrontEndState.InGame,
                "the client never got in (" + _client.FrontEnd.State + ", " + _client.Session.LastDisconnect + ")");
        }

        [UnityTest]
        public IEnumerator Host_from_the_menu_then_a_client_joins_by_code_into_level_1_and_the_hud_shows_only_in_game()
        {
            yield return MenusUp();
            Assert.That(FrontEndTesting.Buttons(FrontEndTesting.MainScreen(_host.FrontEnd)),
                Is.EqualTo(new[] { "Host", "Join by IP", "Join by code", "Settings", "Quit" }));
            Assert.That(FrontEndTesting.MainScreen(_host.FrontEnd).Subtitle, Is.EqualTo("Playing as Ann"), "the name from the identity source");
            Assert.That(HudShown(_host) || HudShown(_client), Is.False, "no HUD on the main menu");

            yield return HostFromTheMenu();
            Assert.That(_host.Session.IsHost, Is.True);
            Assert.That(_host.Server.Loader.CurrentLevel, Is.EqualTo(1));
            Assert.That(HudShown(_host), Is.False, "no HUD in the lobby");
            Assert.That(_host.Client.IsMenuOpen, Is.True, "the world ignores the pointer under the lobby");
            Assert.That(_host.Client.Pointer.PointerDown(new Vector2(1700f, 900f), 0), Is.EqualTo(PressOutcome.Ignored));

            yield return JoinByCodeFromTheMenu();
            yield return null;
            Assert.That(HudShown(_client), Is.True, "the client is in the game: HUD up");
            Assert.That(_client.FrontEnd.MainMenu.IsOpen, Is.False);
            Assert.That(HudShown(_host), Is.False, "the host is still in its lobby");
            yield return Wait(() => FrontEndTesting.LobbyRoster(_host.FrontEnd).Count == 2, "the lobby roster never showed Bob");
            Assert.That(FrontEndTesting.LobbyRoster(_host.FrontEnd).Rows.Select(r => r.Text), Is.EquivalentTo(new[] { "Ann", "Bob" }));

            // The names and colours the front end put in the approval payload are the cursors' (UC-T11 reads the seat).
            yield return Wait(() => CursorAvatar.All(_host.Session).Count == 2, "the host never saw both cursors");
            CursorAvatar bob = CursorAvatar.ForClient(_host.Session, _client.Session.LocalClientId);
            yield return Wait(() => bob.PlayerName == "Bob", "Bob's cursor never carried his name");
            Assert.That((Color32)bob.PlayerColor, Is.EqualTo((Color32)Blue), "Bob's colour from his identity");
            Assert.That((Color32)CursorAvatar.Local(_host.Session).PlayerColor, Is.EqualTo((Color32)Red), "Ann's colour from hers");

            // UM-T55: the cursors read the library's roster, so Bob's mid-game rename shows on the host at once.
            var rename = _client.Session.Roster.RequestChange("Bobby", PlayerColors.Palette[2]);
            yield return Wait(() => rename.IsCompleted, "Bob's rename was never answered");
            Assert.That(rename.Result.Accepted, Is.True, rename.Result.Reason);
            yield return Wait(() => bob.PlayerName == "Bobby", "the host never saw Bob's new name on his cursor");
            Assert.That((Color32)bob.PlayerColor, Is.EqualTo(PlayerColors.Palette[2].ToColor32()), "and his new colour");
            yield return Wait(() => FrontEndTesting.LobbyRoster(_host.FrontEnd).Rows.Any(r => r.Text == "Bobby"), "the lobby roster never followed");
            yield return Wait(() => _client.Client.LevelNumber == 1 && _client.Client.Hud.ShownLevel == 1, "the client never saw level 1");

            // Start hands the host over to the game: menus gone, HUD up, level banner.
            FrontEndTesting.Click(FrontEndTesting.LobbyScreen(_host.FrontEnd), "Start");
            Assert.That(_host.FrontEnd.State, Is.EqualTo(FrontEndState.InGame));
            yield return null;
            Assert.That(HudShown(_host), Is.True, "the host is in the game: HUD up");
            Assert.That(_host.Client.IsMenuOpen, Is.False);
            Assert.That(_host.Client.Hud.Banner.IsShowing, Is.True, "the level's signpost on entering the game");

            // The host ends the game: the client is told why, back on its menu, HUD gone.
            _host.FrontEnd.PauseMenu.Open();
            FrontEndTesting.Click(_host.FrontEnd.PauseMenu.Current, "End game");
            FrontEndTesting.Click(MaModal.Top, "End game");
            yield return Wait(() => _client.FrontEnd.State == FrontEndState.Menu && _client.FrontEnd.LastMessage.HasValue,
                "the client was never told the game ended (" + _client.FrontEnd.State + ")");
            Assert.That(_client.FrontEnd.LastMessage.Value.Title, Is.EqualTo("Game ended"));
            Assert.That(FrontEndTesting.MessageDialog(_client.FrontEnd).IsOpen, Is.True, "in a dialog");
            // UM-T65: a stopping host first tells its clients and finishes once they have gone (at most 2 s), so its own menu follows.
            yield return Wait(() => _host.FrontEnd.State == FrontEndState.Menu, "the host never got back to its menu (" + _host.FrontEnd.State + ")");
            yield return null;
            Assert.That(HudShown(_client) || HudShown(_host), Is.False, "back on the menus: no HUD");
            Assert.That(_host.Server.IsServing, Is.False, "the host stopped serving");
        }

        [UnityTest]
        public IEnumerator The_in_game_menu_carries_Cursorys_switches_and_they_call_the_owner_rpcs()
        {
            yield return MenusUp();
            yield return HostFromTheMenu();
            yield return JoinByCodeFromTheMenu();
            FrontEndTesting.Click(FrontEndTesting.LobbyScreen(_host.FrontEnd), "Start");
            CursorAvatar mine = null;
            yield return Wait(() => (mine = CursorAvatar.Local(_client.Session)) != null && mine.IsSpawned, "Bob's cursor never spawned");
            CursorAvatar serverCopy = CursorAvatar.ForClient(_host.Session, _client.Session.LocalClientId);
            yield return null;

            // The HUD's pause button opens the library's in-game menu (Esc does the same); the world stops taking the pointer.
            Assert.That(_client.FrontEnd.PauseMenu.IsOpen, Is.False);
            _client.Client.Hud.PauseButton.Click();
            yield return null;
            Assert.That(_client.FrontEnd.PauseMenu.IsOpen, Is.True, "the pause button opens the front end's menu");
            Assert.That(_client.Client.IsMenuOpen, Is.True);
            Assert.That(_client.Client.Pointer.PointerDown(new Vector2(1700f, 900f), 0), Is.EqualTo(PressOutcome.Ignored), "the world ignores the pointer");
            MenuScreen pause = _client.FrontEnd.PauseMenu.Current;
            Assert.That(FrontEndTesting.Buttons(pause), Is.EqualTo(new[] { "Resume", "Settings", "Leave game" }), "the library's items");
            PlayerSettingsItems items = _client.Client.Settings;
            Assert.That(items, Is.Not.Null, "Cursory's switches were added");
            Assert.That(pause.GetComponentsInChildren<MaToggle>(), Is.SupersetOf(new[] { items.CursorCollision, items.SegmentedTether }),
                "Cursory's switches sit on the library's in-game menu");
            Assert.That(items.CursorCollision.Value || items.SegmentedTether.Value, Is.False, "both off by default");

            items.CursorCollision.Value = true;
            yield return Wait(() => serverCopy.CursorCollision && mine.CursorCollision, "Cursor collision never reached the server and back");
            Assert.That(serverCopy.SegmentedTether, Is.False);
            items.SegmentedTether.Value = true;
            yield return Wait(() => serverCopy.SegmentedTether && mine.SegmentedTether, "Segmented tether never reached the server and back");
            items.CursorCollision.Value = false;
            yield return Wait(() => !serverCopy.CursorCollision, "turning collision off never reached the server");

            FrontEndTesting.Click(pause, "Resume");
            yield return null;
            Assert.That(_client.FrontEnd.PauseMenu.IsOpen, Is.False, "Resume closes it");
            Assert.That(_client.Client.IsMenuOpen, Is.False, "the world takes the pointer again");
            items.SegmentedTether.SetValueWithoutNotify(false);
            _client.Client.OpenMenu();
            yield return null;
            Assert.That(items.SegmentedTether.Value, Is.True, "reopening shows the server's values");
            Assert.That(items.CursorCollision.Value, Is.False);
            Assert.That(_host.FrontEnd.PauseMenu.IsOpen, Is.False, "one player's menu is theirs alone");
        }

        [UnityTest]
        public IEnumerator A_refused_join_is_explained_and_leaves_the_player_on_the_menu()
        {
            _hostVersion = "uc-t12-new";
            _clientVersion = "uc-t12-old";
            yield return MenusUp();
            yield return HostFromTheMenu();
            yield return Wait(() => FrontEndTesting.LobbyCode(_host.FrontEnd) != null, "the lobby never showed a join code");

            FrontEndTesting.Click(FrontEndTesting.MainScreen(_client.FrontEnd), "Join by IP");
            MenuScreen join = _client.FrontEnd.MainMenu.Current;
            join.GetComponentInChildren<MaTextbox>().Text = "127.0.0.1:" + _port;
            FrontEndTesting.Click(join, "Join");
            yield return Wait(() => _client.FrontEnd.State == FrontEndState.Menu && _client.FrontEnd.LastMessage.HasValue,
                "the client was never told (" + _client.FrontEnd.State + ", " + _client.Session.LastDisconnect + ")");

            DisconnectMessage message = _client.FrontEnd.LastMessage.Value;
            Assert.That(message.Title, Is.EqualTo("Different version"));
            Assert.That(message.Detail, Does.Contain("uc-t12-new").And.Contain("uc-t12-old"), "the server's own sentence names both versions");
            Assert.That(FrontEndTesting.MessageDialog(_client.FrontEnd).IsOpen, Is.True, "shown in a dialog");
            Assert.That(_client.FrontEnd.MainMenu.Current, Is.SameAs(FrontEndTesting.MainScreen(_client.FrontEnd)), "back on the main menu");
            yield return null;
            Assert.That(HudShown(_client), Is.False, "no HUD for a game never joined");
            Assert.That(CursorAvatar.All(_host.Session).Count, Is.EqualTo(1), "the host has only its own cursor");
        }

        [Test]
        public void Main_scene_has_the_front_end_beside_its_NetBootstrap_with_Cursorys_settings()
        {
#if UNITY_EDITOR
            const string scenePath = "Assets/Unity.Cursory/Scenes/Main.unity";
            const string settingsPath = "Assets/Unity.Cursory/Settings/FrontEndSettings.asset";
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<NetFrontEndSettings>(settingsPath);
            Assert.That(settings, Is.Not.Null, settingsPath);
            Assert.That(settings.title, Is.EqualTo("Cursory"));
            Assert.That(settings.showLobby && settings.pauseMenu && settings.autoReconnect, Is.True, "the library's defaults otherwise");

            string frontEndScript = UnityEditor.AssetDatabase.AssetPathToGUID("Packages/com.mindattic.unity.multiplayer/Runtime/UI/NetFrontEnd.cs");
            string bootstrapScript = UnityEditor.AssetDatabase.AssetPathToGUID("Packages/com.mindattic.unity.multiplayer/Runtime/NetBootstrap.cs");
            Assert.That(frontEndScript, Is.Not.Empty);
            string scene = File.ReadAllText(scenePath);
            string[] documents = scene.Split(new[] { "--- !u!" }, System.StringSplitOptions.None);
            string frontEnd = documents.Single(d => d.Contains("guid: " + frontEndScript));
            string bootstrap = documents.Single(d => d.Contains("guid: " + bootstrapScript));
            Assert.That(frontEnd, Does.Contain("settings: {fileID: 11400000, guid: " + UnityEditor.AssetDatabase.AssetPathToGUID(settingsPath)));
            Assert.That(GameObjectOf(frontEnd), Is.EqualTo(GameObjectOf(bootstrap)), "on the NetBootstrap's GameObject");
#else
            Assert.Ignore("Reads the scene asset in the editor.");
#endif
        }

        private static string GameObjectOf(string document)
        {
            string line = document.Split('\n').First(l => l.Trim().StartsWith("m_GameObject:"));
            return line.Trim();
        }

        /// <summary>
        /// Renders the main menu and the host's lobby (join code and both players, level 1 behind) to
        /// <c>Screenshots/uc-t12-main-menu.png</c> and <c>Screenshots/uc-t12-lobby.png</c> when the run has a graphics
        /// device (batch mode without -nographics); without one it only checks the screens built.
        /// </summary>
        [UnityTest]
        public IEnumerator Main_menu_and_lobby_screenshots()
        {
            yield return MenusUp();
            yield return Shoot(_host, "uc-t12-main-menu.png");

            yield return HostFromTheMenu();
            yield return JoinByCodeFromTheMenu();
            yield return Wait(() => FrontEndTesting.LobbyRoster(_host.FrontEnd).Count == 2, "the lobby roster never showed Bob");
            CursorAvatar.Local(_host.Session).Move(new Vector2(4600f, 5150f));
            _host.Client.View.Zoom = 0.5f;
            yield return null;
            yield return Shoot(_host, "uc-t12-lobby.png");
        }

        private IEnumerator Shoot(Peer peer, string file)
        {
            for (int i = 0; i < 3; i++) yield return null;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[UC-T12] No graphics device (-nographics): " + file + " skipped.");
                yield break;
            }

            Camera camera = peer.Client.Camera.GetComponent<Camera>();
            var rt = new RenderTexture((int)ScreenSize.x, (int)ScreenSize.y, 24);
            var canvases = peer.FrontEnd.GetComponentsInChildren<Canvas>(false).Where(c => c.isRootCanvas).ToList();
            foreach (Canvas overlay in Object.FindObjectsByType<Canvas>()) if (overlay.isRootCanvas && overlay.renderMode == RenderMode.ScreenSpaceOverlay && overlay.name.Contains("Overlay")) canvases.Add(overlay);
            var modes = canvases.Select(c => c.renderMode).ToList();
            for (int i = 0; i < canvases.Count; i++)
            {
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = camera;
                canvases[i].planeDistance = 1f + i * 0.01f;
            }

            camera.targetTexture = rt;
            for (int i = 0; i < 2; i++) yield return null;
            camera.Render();
            RenderTexture.active = rt;
            var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            for (int i = 0; i < canvases.Count; i++)
            {
                canvases[i].renderMode = modes[i];
                canvases[i].worldCamera = null;
            }

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", file));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, shot.EncodeToPNG());
            Object.Destroy(shot);
            rt.Release();
            Debug.Log("[UC-T12] Screenshot: " + path);
            Assert.That(File.Exists(path), Is.True);
        }
    }
}
