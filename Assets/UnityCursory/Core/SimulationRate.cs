namespace UnityCursory.Core
{
    /// <summary>The server's fixed simulation rate, kept from Cursory's 30 Hz game loop (BIBLE UC-LAW-1).</summary>
    public static class SimulationRate
    {
        /// <summary>Simulation and network ticks per second.</summary>
        public const int TicksPerSecond = 30;

        /// <summary>Seconds per tick, the step handed to Physics2D.Simulate on the server.</summary>
        public const float TickSeconds = 1f / TicksPerSecond;
    }
}
