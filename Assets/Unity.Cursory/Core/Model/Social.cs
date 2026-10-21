namespace Unity.Cursory.Core
{
    /// <summary>
    /// A whistle: a player clicked empty space at (<see cref="X"/>, <see cref="Y"/>). Clients draw a ripple
    /// in <see cref="Color"/> and play the player's tone. The only communication in the game. (The room's votes are
    /// Unity.Multiplayer's votes kit, UM-T54; <see cref="LevelVotes"/> holds Cursory's two kinds.)
    /// </summary>
    public sealed class Whistle
    {
        /// <summary>Ticks a whistle stays live on the server (about a second at 30 Hz).</summary>
        public const int BroadcastTicks = 30;

        public ulong PlayerId;
        public string Color = "#7F77DD";
        /// <summary>The player's tone, 0 to <see cref="WhistleTone.Count"/> - 1 (<see cref="WhistleTone.For"/>).</summary>
        public int Tone;
        public float X;
        public float Y;
        public long Tick;

        /// <summary>True while the whistle is younger than <see cref="BroadcastTicks"/>.</summary>
        public bool IsLive(long nowTick) => nowTick - Tick < BroadcastTicks;
    }
}
