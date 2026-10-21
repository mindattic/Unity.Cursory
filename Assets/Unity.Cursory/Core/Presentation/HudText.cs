using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// The HUD's words, engine-free (Cursory's <c>renderVote</c>, <c>syncLevelDropdown</c> and <c>showLevelBanner</c>),
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

        /// <summary>The vote overlay's question: "Reset the level?" or "Switch to Level N?".</summary>
        public static string VoteTitle(VoteStatus status)
        {
            if (status == null) return "";
            return status.Kind == VoteKind.SelectLevel ? "Switch to Level " + status.TargetLevel + "?" : "Reset the level?";
        }

        /// <summary>The overlay's tally: "Yes 1 · No 0 · needs 2 of 3 · 14 s".</summary>
        public static string VoteTally(VoteStatus status)
        {
            if (status == null) return "";
            return "Yes " + status.Yes + " · No " + status.No + " · needs " + status.Quorum + " of " + status.Voters + " · " + status.SecondsLeft + " s";
        }

        /// <summary>A line for how the local player stands: whether they may still vote.</summary>
        public static string VoteStanding(VoteChoice choice)
        {
            switch (choice)
            {
                case VoteChoice.Yes: return "You voted Yes";
                case VoteChoice.No: return "You voted No";
                case VoteChoice.Undecided: return "Your vote";
                default: return "You joined after this vote started";
            }
        }

        /// <summary>True when the local player may still press Yes or No (Cursory disables both once you have voted).</summary>
        public static bool CanVote(VoteChoice choice) => choice == VoteChoice.Undecided;

        /// <summary>A toast for how a vote ended.</summary>
        public static string VoteEnded(VoteKind kind, int targetLevel, VoteEnding ending)
        {
            string what = kind == VoteKind.SelectLevel ? "Switch to Level " + targetLevel : "Reset";
            switch (ending)
            {
                case VoteEnding.Passed: return what + ": passed";
                case VoteEnding.TimedOut: return what + ": timed out";
                case VoteEnding.Abandoned: return what + ": abandoned";
                default: return what + ": rejected";
            }
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
