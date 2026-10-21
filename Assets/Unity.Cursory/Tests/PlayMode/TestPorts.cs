using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T21: the one place a PlayMode test gets its port. Every network test serves on a free ephemeral port picked
    /// here instead of a fixed one (7777, the NGO harness's 7789 or a hand-picked 479xx), so two runs on one machine
    /// (parallel worktrees, another CI job, a local server from <c>deploy.ps1 -Profile Local</c>) never collide.
    /// Sessions take it through <see cref="Unity.Multiplayer.Core.NetConfig.Port"/> (directly, or via
    /// <c>NetBootstrap.Configure</c>); harness-started NetworkManagers through <see cref="UseFreePort"/>.
    /// </summary>
    internal static class TestPorts
    {
        /// <summary>
        /// A UDP port free right now: binds a UDP socket to port 0, reads back the port the OS gave it and releases it.
        /// Unity Transport is UDP, so this is the protocol that has to be free.
        /// </summary>
        public static ushort FreeUdpPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                return (ushort)((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        /// <summary>
        /// Points every harness NetworkManager's <see cref="UnityTransport"/> at one fresh <see cref="FreeUdpPort"/>,
        /// for tests the NGO integration harness starts itself (no NetSession, so no NetConfig to carry it). Returns the
        /// port, for a client the test adds later (<see cref="UsePort"/>).
        /// </summary>
        public static ushort UseFreePort(IEnumerable<NetworkManager> managers)
        {
            ushort port = FreeUdpPort();
            foreach (NetworkManager manager in managers) UsePort(manager, port);
            return port;
        }

        /// <summary>Points one harness NetworkManager's <see cref="UnityTransport"/> at <paramref name="port"/>.</summary>
        public static void UsePort(NetworkManager manager, ushort port)
        {
            if (manager.NetworkConfig.NetworkTransport is UnityTransport transport) transport.ConnectionData.Port = port;
        }
    }
}
