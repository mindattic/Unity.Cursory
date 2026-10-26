using System;
using Unity.Shared.Core;
using UnityEngine;

namespace Unity.Cursory
{
    // Honours the player's shared Unity.Shared profile audio settings for the whole game, which is how the
    // CLI mute reaches it: launched with -muted or MINDATTIC_CLI_MUTED=1, Unity.Shared's bootstrap turns on
    // its session mute before the first scene loads, and the profile's CommonSettings then report sound
    // effects muted (every Cursory sound is a whistle, played through WhistleAudio; the game has no music).
    // That silences the listener for this run only: AudioListener.volume is never saved, and settings.json
    // is never written.
    public static class SessionAudio
    {
        // True once Apply found the profile's sound effects muted (the CLI mute, or the player's own setting).
        public static bool Muted { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnLoad() => Apply();

        // Reads the current profile's audio settings and mutes the listener if they say so. Never unmutes
        // what something else muted. Returns Muted.
        public static bool Apply()
        {
            Muted = ProfileMutesSound();
            if (Muted)
            {
                AudioListener.volume = 0f;
                Debug.Log("Unity.Cursory: audio muted for this session (Unity.Shared profile mute; -muted / MINDATTIC_CLI_MUTED=1).");
            }
            return Muted;
        }

        private static bool ProfileMutesSound()
        {
            if (!Profiles.IsConfigured)
                return false;
            try
            {
                var common = Profiles.Current.Settings.Common;
                return common.MuteSfx || common.SfxVolume <= 0f;
            }
            catch (Exception e)
            {
                // A profile folder the disk refuses must not stop the game; it just plays with sound.
                Debug.LogWarning($"Unity.Cursory: could not read the profile's audio settings ({e.Message}); sound stays on.");
                return false;
            }
        }
    }
}
