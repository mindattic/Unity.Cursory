using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// The HUD's words, engine-free (Cursory's <c>syncLevelDropdown</c> and <c>showLevelBanner</c>; the vote overlay's are Unity.Multiplayer's <c>VoteText</c>),
    /// so the Unity HUD only places them.
    /// </summary>
    public static class HudText
    {
        /// <summary>How long the level banner stays up, in seconds (Cursory's 3.5 s).</summary>
        public const float BannerSeconds = 3.5f;

        /// <summary>The level dropdown's entries, "Level 1" to "Level <paramref name="levelCount"/>"; index i is level i + 1.</summary>
        public static List<string> LevelOptions(int levelCount)
        {
            var options = new List<string>(Math.Max(0, levelCount));
            for (var n = 1; n <= levelCount; n++) options.Add("Level " + n);
            return options;
        }

        /// <summary>
        /// The level banner for <paramref name="level"/>: its signpost label's title and subtitle (the label whose id
        /// starts <c>L{n}-label</c>), else its title, else "Level N".
        /// </summary>
        public static (string Title, string Subtitle) Banner(LevelData level, int number)
        {
            if (level != null)
            {
                string prefix = "L" + number + "-label";
                foreach (var label in level.Labels)
                {
                    if (label.Id != null && label.Id.StartsWith(prefix, StringComparison.Ordinal))
                        return (label.Title, label.Subtitle ?? "");
                }
                if (!string.IsNullOrEmpty(level.Title)) return ("Level " + number + " — " + level.Title, "");
            }
            return ("Level " + number, "");
        }
    }
}
