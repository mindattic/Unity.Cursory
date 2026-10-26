using System.IO;
using NUnit.Framework;
using Unity.Shared.Core;
using UnityEngine;

namespace Unity.Cursory.Tests
{
    // The CLI mute (-muted / MINDATTIC_CLI_MUTED=1) reaches Unity.Cursory through Unity.Shared's profile
    // CommonSettings, which report sound effects muted for that session; SessionAudio turns that into a
    // silent listener without writing the player's settings.json.
    public class SessionAudioTests
    {
        private string root;
        private ProfileStore priorStore;
        private float priorVolume;

        [SetUp]
        public void SetUp()
        {
            priorStore = Profiles.IsConfigured ? Profiles.Store : null;
            priorVolume = AudioListener.volume;
            root = Path.Combine(Path.GetTempPath(), "CursorySessionAudio-" + System.Guid.NewGuid().ToString("N"));
            Profiles.UseRoot(root);
            AudioListener.volume = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            AudioListener.volume = priorVolume;
            if (priorStore != null)
                Profiles.Use(priorStore);
            else
                Profiles.Reset();
            SessionAudio.Apply();
            AudioListener.volume = priorVolume;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void AProfileReportingSoundMutedSilencesTheListenerWithoutSavingIt()
        {
            var profile = Profiles.Current;
            profile.SaveSettings();
            var saved = File.ReadAllText(profile.SettingsPath);
            profile.Settings.Common.MuteSfx = true;

            Assert.That(SessionAudio.Apply(), Is.True);
            Assert.That(SessionAudio.Muted, Is.True);
            Assert.That(AudioListener.volume, Is.EqualTo(0f));
            Assert.That(File.ReadAllText(profile.SettingsPath), Is.EqualTo(saved), "settings.json was written.");
        }

        [Test]
        public void AnUnmutedProfileLeavesTheListenerAlone()
        {
            Profiles.Current.Settings.Common.MuteSfx = false;

            Assert.That(SessionAudio.Apply(), Is.False);
            Assert.That(AudioListener.volume, Is.EqualTo(1f));
        }
    }
}
