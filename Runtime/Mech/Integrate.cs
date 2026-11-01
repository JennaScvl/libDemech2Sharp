namespace Demech2
{
    // The motion's integration (demech2 MW2/src/integrate.c).
    public sealed partial class Mech
    {
        // IntegrateMidpoint: v += a t; p += (v + a t / 2) t >> 16, with 32-bit sums and the 64-bit product's middle dword.
        public static void IntegrateMidpoint(ref int p_position, ref int p_velocity, int p_acceleration, int p_time)
        {
            uint delta = (uint)((long)p_acceleration * p_time);
            uint velocity = (uint)p_velocity;
            int midpoint = (int)((uint)((int)delta >> 1) + velocity);
            p_velocity = (int)(velocity + delta);
            p_position = (int)((uint)p_position + (uint)((ulong)((long)midpoint * p_time) >> 16));
        }
    }
}
