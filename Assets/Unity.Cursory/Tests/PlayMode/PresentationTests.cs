using System.Collections;
using System.Collections.Generic;
using System.IO;
using MindAttic.Components.Unity;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using Unity.Multiplayer.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T11: the client presentation on a real Host (NetBootstrap + LevelServer over loopback, level 1): click vs
    /// drag (a quick click whistles through the VoteBoard, a 5 px drag pans instead, a press on the block grabs it),
    /// the wheel zoom clamp, the HUD following the VoteBoard and LevelStateSync and voting through them, player colours, and that a headless
    /// run shows nothing (UC-T12's FrontEndTests cover the menus and the per-player switches).
    /// </summary>
    public class PresentationTests
    {
        private static readonly PlayerColor AnnColor = new PlayerColor(0x12, 0x9E, 0xD4);
        private static readonly Vector2 ScreenSize = new Vector2(1920f, 1080f);

        private GameObject _netGo;
        private GameObject _cameraGo;
        private NetSession _session;
        private LevelServer _server;
        private CursoryClient _client;
        private SimulationMode2D _mode;

        [SetUp]
        public void SetUp()
        {
            _mode = Physics2D.simulationMode;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_client != null) Object.Destroy(_client.gameObject);
            _client = null;
            if (_session != null && _session.IsActive) _session.Leave();
            yield return null;
            if (_netGo != null) Object.Destroy(_netGo);
            if (_cameraGo != null) Object.Destroy(_cameraGo);
            _netGo = _cameraGo = null;
            _session = null;
            MaToast.DismissAll();
            yield return null;
            Physics2D.simulationMode = _mode;
        }

        /// <summary>A Host on a free port playing level 1, Ann's cursor spawned, and the presentation bound and seeing it all.</summary>
        private IEnumerator StartHostAndClient()
        {
            Assert.That(Object.FindAnyObjectByType<CursoryClient>(), Is.Null, "a batch-mode run creates no presentation by itself");

            _netGo = new GameObject("UC-T11 host");
            _netGo.SetActive(false);
            var manager = _netGo.AddComponent<NetworkManager>();
            _netGo.AddComponent<UnityTransport>();
            var bootstrap = _netGo.AddComponent<NetBootstrap>();
            _server = _netGo.AddComponent<LevelServer>();
            if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.EnableSceneManagement = false;
            _netGo.SetActive(true);
            bootstrap.enabled = false;   // its Start would serve headless; this test hosts instead
            _session = bootstrap.Session;
            _session.Identity = new PlayerIdentity("ann-token", "Ann", AnnColor);
            yield return null;           // LevelServer.Start registers the prefabs and the player prefab

            Assert.That(_session.Start(new NetConfig { Topology = Topology.Host, Port = FreePort.Udp(), GameVersion = "uc-t11" }), Is.True, "StartHost");
            yield return WaitFor(() => _server.IsServing && CursorAvatar.Local(_session) != null, "the host never served level 1 with Ann's cursor");

            _cameraGo = new GameObject("UC-T11 camera");
            var camera = _cameraGo.AddComponent<Camera>();
            _cameraGo.AddComponent<TopDownCamera>();
            _client = CursoryClient.Create(_session, camera);
            _client.ReadDevices = false;
            _client.ScreenOverride = ScreenSize;
            yield return WaitFor(() => _client.Board != null && _client.Whistles != null && _client.LevelState != null && _client.LevelNumber == 1 && _client.Bodies.Count == 1,
                "the presentation never found the vote and whistle boards, the level state and level 1's block");
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, string failure, float seconds = 5f)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, failure);
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private Vector2 ScreenOf(float worldX, float worldY)
        {
            var (x, y) = _client.View.WorldToScreen(worldX, worldY, ScreenSize.x, ScreenSize.y);
            return new Vector2(x, y);
        }

        [Test]
        public void A_headless_or_bot_process_shows_nothing()
        {
            Assert.That(CursoryClient.ShouldPresent(new[] { "Unity.exe" }, batchMode: true), Is.False, "-batchmode (a dedicated server, CI)");
            Assert.That(CursoryClient.ShouldPresent(new[] { "Cursory.exe", "-bot", "A" }, batchMode: false), Is.False, "-bot");
            Assert.That(CursoryClient.ShouldPresent(new[] { "Cursory.exe", "-bot=B" }, batchMode: false), Is.False, "-bot=B");
            Assert.That(CursoryClient.ShouldPresent(new[] { "Cursory.exe" }, batchMode: false), Is.True, "a player");
        }

        [UnityTest]
        public IEnumerator A_quick_click_whistles_a_drag_pans_and_a_press_on_the_block_grabs_it()
        {
            yield return StartHostAndClient();
            var whistles = new List<Whistle>();
            _client.Whistles.Whistled += whistles.Add;
            CursorAvatar me = CursorAvatar.Local(_session);

            // A quick, still click on empty space: a whistle at the pressed world point, for everyone.
            Vector2 empty = new Vector2(1700f, 900f);
            Assert.That(_client.Pointer.PointerDown(empty, 1000), Is.EqualTo(PressOutcome.Armed));
            _client.Pointer.PointerMove(empty + new Vector2(3f, 3f));
            Assert.That(_client.Pointer.PointerUp(1200), Is.EqualTo(GestureResult.Whistle), "4 px and 200 ms is a click");
            yield return WaitFor(() => whistles.Count == 1, "the whistle never went round the board");
            Assert.That(whistles[0].PlayerId, Is.EqualTo(_session.LocalClientId));
            Assert.That(whistles[0].X, Is.EqualTo(5740f).Within(0.01f), "1700 px across a 1920 px screen centred on 5000");
            Assert.That(whistles[0].Y, Is.EqualTo(5360f).Within(0.01f));
            Assert.That(_client.Audio.Played, Is.EqualTo(1), "our own tone plays once, at the click (not again on the broadcast)");
            Assert.That(_client.World.RippleCount, Is.GreaterThanOrEqualTo(1));
            Assert.That((_client.View.X, _client.View.Y), Is.EqualTo((5000f, 5000f)), "a click does not move the camera");

            // Five pixels of travel turn the press into a pan: the world follows the pointer and nobody whistles.
            Assert.That(_client.Pointer.PointerDown(empty, 2000), Is.EqualTo(PressOutcome.Armed));
            _client.Pointer.PointerMove(empty + new Vector2(5f, 0f));
            Assert.That(_client.Pointer.Gesture.IsPanning, Is.True, "5 px is a drag");
            _client.Pointer.PointerMove(empty + new Vector2(40f, -20f));
            Assert.That(_client.View.X, Is.EqualTo(4960f).Within(0.01f));
            Assert.That(_client.View.Y, Is.EqualTo(5020f).Within(0.01f));
            Assert.That(_client.Pointer.PointerUp(2050), Is.EqualTo(GestureResult.PanEnded));

            // A press held past 250 ms is neither.
            _client.Pointer.PointerDown(empty, 3000);
            Assert.That(_client.Pointer.PointerUp(3251), Is.EqualTo(GestureResult.None));
            yield return Frames(10);
            Assert.That(whistles.Count, Is.EqualTo(1), "neither the pan nor the long press whistled");

            // A press on the block is a grab; the release lets go.
            Vector2 onBlock = ScreenOf(3560f, 5000f);
            Assert.That(_client.Pointer.PointerDown(onBlock, 4000), Is.EqualTo(PressOutcome.Grabbed));
            Assert.That(_client.Pointer.Holding, Is.EqualTo(new PickResult(PickKind.Block, "L1-block")));
            yield return WaitFor(() => me.Held == GrabKind.Block && me.HeldId == "L1-block", "the server never saw the grab");
            _client.Pointer.PointerUp(4100);
            yield return WaitFor(() => me.Held == GrabKind.None, "the release never let go");
            Assert.That(whistles.Count, Is.EqualTo(1), "a grab never whistles");
        }

        [UnityTest]
        public IEnumerator The_wheel_zooms_about_the_pointer_between_a_tenth_and_four()
        {
            yield return StartHostAndClient();
            Vector2 at = new Vector2(1500f, 300f);
            var (wx, wy) = _client.View.ScreenToWorld(at.x, at.y, ScreenSize.x, ScreenSize.y);

            _client.Pointer.Wheel(at, 120f);
            Assert.That(_client.View.Zoom, Is.EqualTo(1.1f).Within(1e-4f), "one notch in");
            var (ax, ay) = _client.View.ScreenToWorld(at.x, at.y, ScreenSize.x, ScreenSize.y);
            Assert.That(ax, Is.EqualTo(wx).Within(0.05f), "the world point under the pointer stays put");
            Assert.That(ay, Is.EqualTo(wy).Within(0.05f));

            for (int i = 0; i < 60; i++) _client.Pointer.Wheel(at, 1f);
            Assert.That(_client.View.Zoom, Is.EqualTo(CameraView.ZoomMax), "clamped at 4");
            yield return null;
            Assert.That(_client.Camera.Zoom, Is.EqualTo(4f), "the Unity camera follows");
            var cam = _client.Camera.GetComponent<Camera>();
            if (cam.pixelHeight > 0)
                Assert.That(cam.orthographicSize, Is.EqualTo(WorldUnits.OrthographicSize(cam.pixelHeight, 4f)).Within(1e-4f));

            for (int i = 0; i < 120; i++) _client.Pointer.Wheel(at, -1f);
            Assert.That(_client.View.Zoom, Is.EqualTo(CameraView.ZoomMin), "clamped at 0.1");
            yield return null;
            Assert.That(_client.Camera.Zoom, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(_client.View.X, Is.InRange(0f, WorldUnits.WorldSizePx));
            Assert.That(_client.View.Y, Is.InRange(0f, WorldUnits.WorldSizePx));
        }

        [UnityTest]
        public IEnumerator The_hud_follows_the_vote_board_and_level_state_and_votes_through_them()
        {
            yield return StartHostAndClient();
            CursoryHud hud = _client.Hud;
            yield return WaitFor(() => hud.ShownLevel == 1, "the HUD never showed level 1");
            Assert.That(hud.LevelSelect.Value, Is.EqualTo(0), "Level 1 in the dropdown");
            Assert.That(hud.LevelSelect.Count, Is.EqualTo(14));
            Assert.That(hud.Banner.IsShowing, Is.True, "the level banner shows on joining");
            Assert.That(hud.Banner.Title.Text, Is.EqualTo("Level 1 — Drop it on the pad"));
            Assert.That(hud.ConnectionPill.Text, Is.EqualTo("Hosting"));
            Assert.That(hud.ConnectionPill.Kind, Is.EqualTo(MindAttic.Components.Unity.Core.StatusKind.Ok));
            Assert.That(hud.VoteOverlay, Is.Not.Null, "the library's overlay on the room's board (UM-T54)");
            VoteOverlay overlay = hud.VoteOverlay;
            Assert.That(overlay.Panel.Visible, Is.False, "no vote, no overlay");
            yield return WaitFor(() => _client.Minimap.Draws > 0, "the minimap never drew");

            // Three voters (us and two stand-ins) so one Yes doesn't pass on its own: quorum 2 of 3.
            ulong me = _session.LocalClientId;
            var roster = new[] { me, 1001UL, 1002UL };
            _server.Votes.Serve(null, null, () => roster);

            hud.ResetButton.Click();
            yield return WaitFor(() => overlay.Panel.Visible, "Reset never opened a vote");
            Assert.That(overlay.TitleText.Text, Is.EqualTo("Reset the level?"));
            Assert.That(overlay.TallyText.Text, Does.StartWith("Yes 1 · No 0 · needs 2 of 3"), "the starter counts as Yes");
            Assert.That(overlay.YesButton.Interactable || overlay.NoButton.Interactable, Is.False, "we already voted (Cursory disables both)");
            yield return WaitFor(() => !hud.ResetButton.Interactable, "one vote at a time: Reset never disabled");   // the HUD refreshes in LateUpdate, the overlay in Update

            _server.Votes.Box.Cast(1001UL, false);
            _server.Votes.Box.Cast(1002UL, false);
            yield return WaitFor(() => !overlay.Panel.Visible, "the rejected vote never left the overlay");

            // A stand-in proposes level 3; our Yes is the second of two needed.
            _server.Votes.Box.Start(new VoteRequest(LevelVotes.LevelKind, 1001UL, 3), roster);
            yield return WaitFor(() => overlay.Panel.Visible && overlay.YesButton.Interactable, "we never got to vote on level 3");
            Assert.That(overlay.TitleText.Text, Is.EqualTo("Switch to Level 3?"));
            Assert.That(overlay.StandingText.Text, Is.EqualTo("Your vote"));
            overlay.YesButton.Click();
            yield return WaitFor(() => _server.Loader.CurrentLevel == 3, "our Yes never passed the vote");
            yield return WaitFor(() => hud.ShownLevel == 3, "the HUD never followed the level state to level 3");
            Assert.That(overlay.Panel.Visible, Is.False);
            Assert.That(hud.LevelSelect.Value, Is.EqualTo(2), "Level 3 in the dropdown");
            Assert.That(hud.Banner.IsShowing, Is.True, "the new level's banner");
            Assert.That(hud.Banner.Title.Text, Is.EqualTo("Level 3 — Pivot the couch"));

            // Picking a level in the dropdown proposes it; the dropdown keeps showing the room's level.
            hud.LevelSelect.Value = 4;
            yield return WaitFor(() => overlay.Panel.Visible, "picking Level 5 never started a vote");
            Assert.That(overlay.TitleText.Text, Is.EqualTo("Switch to Level 5?"));
            Assert.That(_server.Votes.Status.StartedBy, Is.EqualTo(me));
            Assert.That(hud.LevelSelect.Value, Is.EqualTo(2), "still Level 3 until the vote passes");
        }

        [UnityTest]
        public IEnumerator Players_are_drawn_in_the_colour_and_name_they_connected_with()
        {
            yield return StartHostAndClient();
            CursorAvatar me = CursorAvatar.Local(_session);
            Assert.That((Color32)me.PlayerColor, Is.EqualTo(new Color32(AnnColor.R, AnnColor.G, AnnColor.B, 255)), "the approval payload's colour");
            Assert.That(me.PlayerName, Is.EqualTo("Ann"));
            Assert.That(me.SeatIndex, Is.EqualTo(0));
            yield return Frames(2);
            Assert.That(me.GetComponent<TetherRenderer>().Color, Is.EqualTo(me.PlayerColor), "the rope too");
            Assert.That(_client.World.CursorCount, Is.EqualTo(1), "the cursor is drawn");

            // UM-T55: name and colour come from the library's roster, so a mid-game change reaches cursor and rope at once.
            var rename = _session.Roster.RequestChange("Annie", PlayerColors.Palette[2]);
            Assert.That(rename.IsCompleted && rename.Result.Accepted, Is.True, "a host's own change is applied at once");
            Assert.That(me.PlayerName, Is.EqualTo("Annie"));
            Assert.That((Color32)me.PlayerColor, Is.EqualTo(PlayerColors.Palette[2].ToColor32()));
            yield return Frames(2);
            Assert.That(me.GetComponent<TetherRenderer>().Color, Is.EqualTo(me.PlayerColor), "the rope follows");
        }

        /// <summary>UC-T23: the minimap fits the level (walls, goals, bodies) with padding instead of the whole world, and a click on it pans the camera.</summary>
        [UnityTest]
        public IEnumerator The_minimap_fits_the_level_with_padding_and_pans_the_camera()
        {
            yield return StartHostAndClient();
            yield return WaitFor(() => _client.Hud.ShownLevel == 1 && _client.Minimap.Draws > 0, "the minimap never drew level 1");
            _client.Minimap.Draw(CursoryClient.LevelDefinition(1), null, null, null, _client.View, ScreenSize, force: true);
            MaMinimap map = _client.Minimap.Map;
            Assert.That(map.AutoFit, Is.True);
            Assert.That(map.ContentBounds.HasValue, Is.True);
            var bounds = map.ContentBounds.Value;
            LevelData level = CursoryClient.LevelDefinition(1);
            foreach (Wall w in level.Walls)
            {
                Assert.That(bounds.Contains(w.X - w.W / 2f, w.Y - w.H / 2f) && bounds.Contains(w.X + w.W / 2f, w.Y + w.H / 2f), Is.True, "every wall is inside the fitted bounds");
            }

            var (left, bottom) = map.Fit.ToMap(bounds.X, bounds.Y);
            var (right, top) = map.Fit.ToMap(bounds.XMax, bounds.YMax);
            float size = MinimapView.Resolution, pad = MinimapView.Padding;
            Assert.That(Mathf.Max(right - left, top - bottom), Is.EqualTo(size - 2f * pad).Within(0.01f), "the level spans the map, less the padding");
            Assert.That(Mathf.Min(left, bottom), Is.GreaterThanOrEqualTo(pad - 0.01f));
            Assert.That(Mathf.Max(right, top), Is.LessThanOrEqualTo(size - pad + 0.01f));
            Assert.That(bounds.W, Is.LessThan(WorldUnits.WorldSizePx / 2f), "level 1 is a small part of the world, so a whole-world map would maroon it");

            // A press on the map's centre pans the camera to the level's centre.
            RectTransform image = map.Image.rectTransform;
            Vector2 centre = RectTransformUtility.WorldToScreenPoint(null, image.TransformPoint(image.rect.center));
            map.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
            {
                position = centre, button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
            });
            Assert.That(_client.View.X, Is.EqualTo(bounds.CenterX).Within(bounds.W / size * 2f + 1f));
            Assert.That(_client.View.Y, Is.EqualTo(bounds.CenterY).Within(bounds.H / size * 2f + 1f));
        }

        /// <summary>
        /// Renders the HUD over level 1 to <c>Screenshots/uc-t23-hud.png</c> when the run has a graphics device (a
        /// batch-mode run without -nographics); with none it only checks the HUD built.
        /// </summary>
        [UnityTest]
        public IEnumerator Hud_screenshot()
        {
            yield return StartHostAndClient();
            yield return WaitFor(() => _client.Hud.ShownLevel == 1, "the HUD never showed level 1");
            CursorAvatar.Local(_session).Move(new Vector2(4600f, 5150f));
            _server.Votes.Serve(null, null, () => new[] { _session.LocalClientId, 1001UL, 1002UL });
            _client.Hud.ResetButton.Click();
            yield return WaitFor(() => _client.Hud.VoteOverlay.Panel.Visible, "no vote to show");
            _client.View.Zoom = 0.5f;   // the whole of level 1: block, pad and signpost
            _client.Camera.GetComponent<Camera>().backgroundColor = new Color(0.07f, 0.08f, 0.10f, 1f);
            _client.Camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            _client.Pointer.PointerMove(new Vector2(700f, 560f));
            _client.Whistle(new Vector2(4300f, 4600f));
            yield return Frames(3);

            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[UC-T11] No graphics device (-nographics): HUD screenshot skipped.");
                yield break;
            }

            var camera = _client.Camera.GetComponent<Camera>();
            var rt = new RenderTexture((int)ScreenSize.x, (int)ScreenSize.y, 24);
            Canvas hud = _client.Hud.Canvas;
            hud.renderMode = RenderMode.ScreenSpaceCamera;
            hud.worldCamera = camera;
            hud.planeDistance = 1f;
            Canvas votes = _client.Hud.VoteOverlay.Canvas;   // the library's overlay draws on its own canvas
            votes.renderMode = RenderMode.ScreenSpaceCamera;
            votes.worldCamera = camera;
            votes.planeDistance = 0.9f;
            camera.targetTexture = rt;
            yield return Frames(2);
            camera.Render();
            RenderTexture.active = rt;
            var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "uc-t23-hud.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, shot.EncodeToPNG());
            Object.Destroy(shot);
            rt.Release();
            Debug.Log("[UC-T23] HUD screenshot: " + path);
            Assert.That(File.Exists(path), Is.True);
        }
    }
}
