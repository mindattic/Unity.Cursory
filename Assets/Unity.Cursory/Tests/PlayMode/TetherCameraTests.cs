using System.Collections;
using MindAttic.Components.Unity;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T34: the tether camera on a real Host playing level 1. While Ann drags the block around the screen, every
    /// frame keeps her cursor and the rope's anchor on the block on screen, the camera eases (never snaps) and zooms
    /// out when the rope needs it, the minimap's viewport follows the camera, and letting go hands the camera back to
    /// free pan and wheel zoom.
    /// </summary>
    public class TetherCameraTests
    {
        private static readonly Vector2 ScreenSize = new Vector2(1920f, 1080f);

        private GameObject _netGo;
        private GameObject _cameraGo;
        private NetSession _session;
        private LevelServer _server;
        private CursoryClient _client;

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
        }

        private IEnumerator StartHostAndClient()
        {
            _netGo = new GameObject("UC-T34 host");
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
            _session.Identity = new PlayerIdentity("ann-token", "Ann", new PlayerColor(0x12, 0x9E, 0xD4));
            yield return null;

            Assert.That(_session.Start(new NetConfig { Topology = Topology.Host, Port = FreePort.Udp(), GameVersion = "uc-t34" }), Is.True, "StartHost");
            yield return WaitFor(() => _server.IsServing && CursorAvatar.Local(_session) != null, "the host never served level 1 with Ann's cursor");

            _cameraGo = new GameObject("UC-T34 camera");
            var camera = _cameraGo.AddComponent<Camera>();
            _cameraGo.AddComponent<TopDownCamera>();
            _client = CursoryClient.Create(_session, camera);
            _client.ReadDevices = false;
            _client.ScreenOverride = ScreenSize;
            _client.ReduceMotionOverride = false;
            yield return WaitFor(() => _client.LevelState != null && _client.LevelNumber == 1 && _client.Bodies.Count == 1,
                "the presentation never found level 1's block");
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, string failure, float seconds = 5f)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, failure);
        }

        private Vector2 ScreenOf(Vector2 world)
        {
            var (x, y) = _client.View.WorldToScreen(world.x, world.y, ScreenSize.x, ScreenSize.y);
            return new Vector2(x, y);
        }

        private void AssertOnScreen(Vector2 world, string what, int frame)
        {
            const float slack = 4f;   // a server tick may land between the camera's step and this check
            Vector2 s = ScreenOf(world);
            Assert.That(s.x >= -slack && s.x <= ScreenSize.x + slack && s.y >= -slack && s.y <= ScreenSize.y + slack, Is.True,
                $"{what} at {world} is off screen ({s}) on frame {frame}: {_client.View}");
        }

        [UnityTest]
        public IEnumerator While_dragging_the_camera_keeps_the_cursor_and_the_anchor_on_screen()
        {
            yield return StartHostAndClient();
            CursorAvatar me = CursorAvatar.Local(_session);
            Vector2 press = ScreenOf(new Vector2(3560f, 5000f));   // on level 1's block (3500, 5000, 160 square)
            Assert.That(_client.Pointer.PointerDown(press, 1000), Is.EqualTo(PressOutcome.Grabbed));
            yield return WaitFor(() => me.Tether.Count > 0 && _client.TetherFollow.Active, "the grab never tethered the camera");

            // Drag to the four corners and back through the middle, a little each frame, checking every frame.
            Vector2[] path =
            {
                new Vector2(1880f, 1040f), new Vector2(40f, 1040f), new Vector2(40f, 40f), new Vector2(1880f, 40f), ScreenSize / 2f,
            };
            Vector2 from = press;
            float startX = _client.View.X, startY = _client.View.Y, minZoom = _client.View.Zoom;
            float lastX = startX, lastY = startY, biggestJump = 0f;
            int frame = 0;
            foreach (Vector2 to in path)
            {
                for (int i = 1; i <= 45; i++)
                {
                    _client.Pointer.PointerMove(Vector2.Lerp(from, to, i / 45f));
                    yield return null;
                    frame++;
                    Assert.That(me.Tether.Count, Is.GreaterThan(0), "still tethered");
                    AssertOnScreen(me.Position, "the cursor", frame);
                    AssertOnScreen(me.Tether[0], "the anchor", frame);
                    minZoom = Mathf.Min(minZoom, _client.View.Zoom);
                    biggestJump = Mathf.Max(biggestJump, Vector2.Distance(new Vector2(lastX, lastY), new Vector2(_client.View.X, _client.View.Y)) * _client.View.Zoom);
                    lastX = _client.View.X;
                    lastY = _client.View.Y;
                }

                from = to;
            }

            Assert.That(new Vector2(lastX - startX, lastY - startY).magnitude, Is.GreaterThan(1f), "the camera followed the drag");
            Assert.That(biggestJump, Is.LessThan(ScreenSize.y / 4f), "the camera eases; it never jumps a quarter screen in a frame");

            // The minimap's viewport is the camera's view.
            _client.Minimap.Draw(CursoryClient.LevelDefinition(1), null, null, null, _client.View, ScreenSize, force: true);
            var visible = _client.View.Visible(ScreenSize.x, ScreenSize.y);
            Assert.That(_client.Minimap.Viewport.HasValue, Is.True);
            Assert.That(_client.Minimap.Viewport.Value.X, Is.EqualTo(visible.X).Within(0.01f), "the minimap viewport follows the camera");
            Assert.That(_client.Minimap.Viewport.Value.W, Is.EqualTo(visible.W).Within(0.01f));

            // Letting go hands the camera back: the wheel zooms the view itself again.
            _client.Pointer.PointerUp(9000);
            yield return WaitFor(() => !_client.TetherFollow.Active, "letting go never ended the tether camera");
            float before = _client.View.Zoom;
            _client.Pointer.Wheel(ScreenSize / 2f, 1f);
            Assert.That(_client.View.Zoom, Is.EqualTo(CameraView.ClampZoom(before * CameraView.WheelStep)).Within(1e-4f), "free wheel zoom again");
        }

        [UnityTest]
        public IEnumerator With_reduce_motion_the_camera_goes_straight_to_the_framing()
        {
            yield return StartHostAndClient();
            _client.ReduceMotionOverride = true;
            CursorAvatar me = CursorAvatar.Local(_session);
            Vector2 press = ScreenOf(new Vector2(3560f, 5000f));
            Assert.That(_client.Pointer.PointerDown(press, 1000), Is.EqualTo(PressOutcome.Grabbed));
            yield return WaitFor(() => me.Tether.Count > 0 && _client.TetherFollow.Active, "the grab never tethered the camera");
            yield return null;
            Vector2 cursor = me.Position, anchor = me.Tether[0];
            Assert.That(_client.View.X, Is.EqualTo((cursor.x + anchor.x) / 2f).Within(40f), "centred on the midpoint at once");
            Assert.That(_client.View.Y, Is.EqualTo((cursor.y + anchor.y) / 2f).Within(40f));
            _client.Pointer.PointerUp(2000);
        }
    }
}
