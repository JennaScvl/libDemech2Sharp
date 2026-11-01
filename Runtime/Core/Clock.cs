namespace Demech2
{
    // The game clock (demech2 MW2/src/clock.c): ticks of the 181 Hz Miles timer. The game's real-time pacing
    // (SimClock) steps it; the sim reads it.
    public static class Clock
    {
        public const int TicksPerSecond = 181;

        // Not in the original: the ticks of one sim step, 181 / 10 (~10 Hz).
        public const int StepTicks = TicksPerSecond / 10;

        // g_currentClock: the stepped clock the sim sees.
        public static int g_currentClock;

        // g_deltaTime: the ticks of the step (always StepTicks once stepping).
        public static int g_deltaTime;
    }
}
