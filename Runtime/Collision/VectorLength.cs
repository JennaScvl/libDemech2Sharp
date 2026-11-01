using System;
using UnityEngine;

namespace Demech2
{
    // ApproximateVectorLength (demech2 MW2/src/approxlen.c): (4 * the largest + the other two) / 4 of the absolute
    // values; the original's integers, and the render side's floats.
    public static class ApproxLen
    {
        public static int ApproximateVectorLength(int p_x, int p_y, int p_z)
        {
            int x = Math.Abs(p_x), y = Math.Abs(p_y), z = Math.Abs(p_z);
            if (x < y) (x, y) = (y, x);
            if (x < z) (x, z) = (z, x);
            return (int)(((uint)x << 2) + (uint)y + (uint)z) >> 2;
        }

        public static float ApproximateVectorLength(float p_x, float p_y, float p_z)
        {
            float x = Mathf.Abs(p_x), y = Mathf.Abs(p_y), z = Mathf.Abs(p_z), swap;
            if (x < y)
            {
                swap = x;
                x = y;
                y = swap;
            }

            if (x < z)
            {
                swap = x;
                x = z;
                z = swap;
            }

            return (x * 4 + y + z) / 4;
        }

        public static float ApproximateVectorLength(Vector3 p_v) => ApproximateVectorLength(p_v.x, p_v.y, p_v.z);

        // NormalizeVectorGuarded: over the approximate length; a zero vector stays (render side's floats).
        public static Vector3 NormalizeVectorGuarded(Vector3 p_v)
        {
            float length = ApproximateVectorLength(p_v);
            return length > 1e-07f ? p_v / length : p_v;
        }

        // FIXED_IS_NEGATIVE: the float's sign bit (-0 counts; render side's floats).
        public static bool IsNegative(float p_value) => BitConverter.SingleToInt32Bits(p_value) < 0;
    }

    // IsWithinRadius (demech2 MW2/src/inradius.c, and the Matrox edition's inline copies).
    public static class InRadius
    {
        // inradius.c: the squares compared unsigned; a sum at or above the radius's square counts as inside when their
        // difference's low dword is 0.
        public static bool IsWithinRadius(int p_x, int p_y, int p_z, int p_radius)
        {
            ulong sum = (ulong)((long)p_x * p_x) + (ulong)((long)p_y * p_y) + (ulong)((long)p_z * p_z);
            ulong square = (ulong)((long)p_radius * p_radius);
            return sum < square || (uint)(sum - square) == 0;
        }

        // Matrox, inline: render side's floats.
        public static bool IsWithinRadius(float p_x, float p_y, float p_z, float p_radius) =>
            p_x * p_x + p_y * p_y + p_z * p_z <= p_radius * p_radius;

        // Matrox, inline: 16.16 units.
        public static bool IsWithinRadius(FixVec p_d, long p_radius) => FixVec.Dot(p_d, p_d) <= Fixed.Mul16L(p_radius, p_radius);
    }
}
