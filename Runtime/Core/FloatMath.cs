using System;
using UnityEngine;

namespace Demech2
{
    // The render side's float maths (demech2 matrox/floatmath.c).
    public static class FloatMath
    {
        // FixedAtan2: the bearing of (x, z) in degrees, 45 at the origin (the sim's is Fixed.Heading).
        public static float FixedAtan2(float p_x, float p_z)
        {
            if (Math.Abs(p_x) < 1e-7f)
            {
                if (Math.Abs(p_z) < 1e-7f) return 45.0f;
                return p_z < 0 ? 180.0f : 0.0f;
            }

            if (Math.Abs(p_z) < 1e-7f) return p_x < 0 ? -90.0f : 90.0f;
            return Mathf.Atan2(p_x, p_z) * Mathf.Rad2Deg;
        }
    }
}
