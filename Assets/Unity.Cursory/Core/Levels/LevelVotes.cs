namespace Unity.Cursory.Core
{
    /// <summary>
    /// Cursory's two room votes (Cursory's <c>StartResetVote</c> and <c>StartLevelVote</c>), engine-free: their ids, how
    /// their questions read, which level votes are valid, and what passing one does to the <see cref="LevelLoader"/>.
    /// <see cref="LevelServer"/> registers them as Unity.Multiplayer <c>VoteKind</c>s on the room's <c>VoteBoard</c>
    /// (UM-T54), which runs the rules: a 2/3 quorum of the players in the room, 15 s, one vote at a time.
    /// </summary>
    public static class LevelVotes
    {
        /// <summary>The reset vote's kind id: re-seed the current level.</summary>
        public const string ResetKind = "reset";

        /// <summary>The level vote's kind id; its number payload is the level to load (1-based).</summary>
        public const string LevelKind = "level";

        /// <summary>The reset vote's question.</summary>
        public const string ResetTitle = "Reset the level?";

        /// <summary>The level vote's question: "Switch to Level N?".</summary>
        public static string LevelTitle(long level) => "Switch to Level " + level + "?";

        /// <summary>
        /// Null when a vote to switch to <paramref name="level"/> may start, else why not: a level that does not exist, or
        /// the one already loaded (a no-op vote must not block a real reset).
        /// </summary>
        public static string CheckLevel(LevelLoader loader, long level)
        {
            if (loader == null || level < 1 || level > int.MaxValue || !loader.Exists((int)level)) return "There is no level " + level + ".";
            if (level == loader.CurrentLevel) return "Level " + level + " is already being played.";
            return null;
        }

        /// <summary>What a passing reset does: re-seed the level (and queue the geometry resend).</summary>
        public static void ApplyReset(LevelLoader loader) => loader.Reset();

        /// <summary>What a passing level vote does: load <paramref name="level"/>.</summary>
        public static void ApplyLevel(LevelLoader loader, long level) => loader.Load((int)level);
    }
}
