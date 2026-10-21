using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using UnityEngine.TestTools;

namespace Unity.Cursory.Tests
{
    /// <summary>
    /// UC-T14 on Unity.Multiplayer's bot mode (UM-T57), in process: the nightly compose smoke without Docker, on the
    /// library's <see cref="NetBotTest"/>. A dedicated server (Cursory's <see cref="LevelServer"/> on level 2, serving the
    /// fixture's server session) registers with an in-process rendezvous service and writes its join code to a file; two
    /// bots, roles A and B, wait for it, join by it and hand their sessions to <see cref="CursoryBot"/>, which grabs the
    /// Mass 2.0 block's two ends and pulls it around the wall onto the pad. Passing needs the server's own verdict
    /// (UC-T9's <see cref="LevelStateSync.LevelSolved"/>), seen by both bots, and the block's server-side pose inside the goal.
    /// </summary>
    public class BotEndToEndTests : NetBotTest
    {
        private readonly Dictionary<string, CursoryBot> _drivers = new Dictionary<string, CursoryBot>();
        private LevelServer _levelServer;

        public BotEndToEndTests() : base(bots: 2)
        {
        }

        protected override float BotTimeoutSeconds => 150f;

        protected override string GameVersion => "uc-t14";

        protected override void OnSessionsCreated()
        {
            // The server is wired as Main.unity wires it, but on the fixture's own session: LevelServer on its manager's object.
            _levelServer = m_ServerNetworkManager.gameObject.AddComponent<LevelServer>();
            _levelServer.StartLevel = 2;
            _levelServer.Serve(Server, m_ServerNetworkManager);

            // Each bot client: Cursory's prefabs and the cursor as its player prefab, as LevelServer registers them on a client.
            for (int i = 0; i < Clients.Count; i++)
            {
                NetPrefabs.RegisterAll(Clients[i]);
                m_ClientNetworkManagers[i].NetworkConfig.PlayerPrefab = NetPrefabs.LoadCursorAvatar();
            }
        }

        protected override IEnumerator OnSessionsTearDown()
        {
            _drivers.Clear();
            _levelServer = null;
            yield return base.OnSessionsTearDown();
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator TwoBotsJoinByCode_AndSolveLevel2Together()
        {
            yield return StartServerAndBots(role => _drivers[role] = new CursoryBot(), "A", "B");
            Assert.That(_levelServer.IsServing, Is.True, "the server never started serving");
            Assert.That(_levelServer.Loader.CurrentLevel, Is.EqualTo(2));
            Assert.That(Rendezvous.Count, Is.EqualTo(1), "the server registered with the rendezvous service");

            yield return WaitForBots();

            Assert.That(Server.ConnectedClientIds.Count, Is.EqualTo(2));
            foreach (NetBotRunner bot in Bots)
            {
                Assert.That(bot.Session.State, Is.EqualTo(SessionState.Connected), "bot " + bot.Role + " joined by code");
                Assert.That(bot.Bot.JoinCode, Is.EqualTo(Server.PublishedCodeText), "bot " + bot.Role + " used the written code");
                Assert.That(bot.PassMessage, Does.Contain("level 2 solved"));
            }

            CursoryBot a = _drivers["A"];
            Assert.That(a.LevelNumber, Is.EqualTo(2));

            // The server's truth: both cursors held the block, and its pose sits on the pad.
            BlockGoal goal = a.Plan.Goal;
            var (x, y, _) = _levelServer.Simulation.World.Pose(goal.TargetBlockId);
            Assert.That(goal.IsSatisfiedBy(x, y), Is.True, "the server's block at (" + x + ", " + y + ") is not on " + goal.Id);
            Assert.That(_levelServer.Simulation.World.Grabbers.Count(), Is.EqualTo(2), "both bots hold the block");
        }
    }
}
