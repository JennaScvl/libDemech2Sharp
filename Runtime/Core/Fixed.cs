using System;

namespace Demech2
{
    // The sim step's fixed point (not the original's: the lockstep's), shared: 16.16 scalars, angles in degrees * 65536,
    // positions in whole units (SimObject.UnitsPerMetre a metre) or, where a test wants the fraction, 16.16 units in a
    // long. Everything here is integer math, its tables built by integer series or hard-coded, so each machine steps the
    // same bits.
    public static class Fixed
    {
        public const int One = 0x10000;
        public const int Half = 0x8000;
        public const int Deg180 = 180 << 16;
        public const int Deg360 = 360 << 16;

        // FixedMul16: the 64-bit product's middle dword (as the original's, wrapping to 32 bits).
        public static int Mul16(long p_a, long p_b) => (int)(p_a * p_b >> 16);

        // FixedDiv16: (a << 16) / b, 64-bit; a zero divisor gives 0.
        public static int Div16(long p_a, long p_b) => p_b == 0 ? 0 : (int)((p_a << 16) / p_b);

        // MulDiv64: a * b / c, 64-bit; a zero divisor gives 0.
        public static int MulDiv64(long p_a, long p_b, long p_c) => p_c == 0 ? 0 : (int)(p_a * p_b / p_c);

        // (a * b) >> 16 kept 64-bit, without the full product overflowing (a split into 16-bit halves): 16.16 units by
        // 16.16 scalars and squares of 16.16 units.
        public static long Mul16L(long p_a, long p_b)
        {
            long high = p_a >> 16, low = p_a & 0xffff;
            return high * p_b + (low * p_b >> 16);
        }

        // (a << 16) / b kept 64-bit; a zero divisor gives 0.
        public static long Div16L(long p_a, long p_b)
        {
            if (p_b == 0) return 0;
            if (p_a > long.MaxValue >> 17 || p_a < long.MinValue >> 17) return p_a / p_b << 16;
            return (p_a << 16) / p_b;
        }

        // The 16.16 value of whole p_value.
        public static long FromInt(long p_value) => p_value << 16;

        // A 16.16 value floored to whole.
        public static long Floor(long p_value) => p_value >> 16;

        // The 16.16 value of p_value, rounded (load-time and wire conversions only).
        public static int FromFloat(float p_value) => (int)Math.Round(p_value * 65536.0);
        public static long FromFloatL(double p_value) => (long)Math.Round(p_value * 65536.0);

        // For the render side: a 16.16 value as a float.
        public static float ToFloat(long p_value) => p_value / 65536f;

        // The integer square root (floored).
        public static long Sqrt(long p_value)
        {
            if (p_value <= 0) return 0;
            long x = (long)Math.Sqrt(p_value); // a first guess only: corrected to the exact floor below
            while (x * x > p_value) x--;
            while ((x + 1) * (x + 1) <= p_value) x++;
            return x;
        }

        // The 16.16 square root of a 16.16 value.
        public static long Sqrt16(long p_value) => Sqrt(p_value) << 8;

        // ApproximateVectorLength: (4 * the largest + the other two) / 4 of the absolute values.
        public static long ApproxLength(long p_x, long p_y, long p_z)
        {
            long x = Math.Abs(p_x), y = Math.Abs(p_y), z = Math.Abs(p_z);
            if (x < y) (x, y) = (y, x);
            if (x < z) (x, z) = (z, x);
            return (x * 4 + y + z) / 4;
        }

        // The exact length (the integer root of the squares, in the units given; 16.16 in, 16.16 out by Mul16L squares).
        public static long Length16(long p_x, long p_y, long p_z) =>
            Sqrt16(Mul16L(p_x, p_x) + Mul16L(p_y, p_y) + Mul16L(p_z, p_z));

        // ---- Trigonometry ---------------------------------------------------------------------------------------------

        // The sine over a turn, 4096 steps (and the first again), in 2.30; read interpolated.
        const int TableSteps = 4096;
        static readonly int[] s_sin = BuildSine();

        // pi in 2.30 (3.14159265358979 * 2^30).
        const long PiQ30 = 3373259426L;

        // A quarter wave by the Taylor series in 2.30 integers, the rest by symmetry.
        static int[] BuildSine()
        {
            var table = new int[TableSteps + 1];
            const int quarter = TableSteps / 4;
            for (int i = 0; i <= quarter; i++)
            {
                long x = PiQ30 / 2 * i / quarter;
                long x2 = x * x >> 30;
                long term = x, sum = x;
                for (int n = 1; n < 12; n++)
                {
                    term = -(term * x2 >> 30) / ((2 * n) * (2 * n + 1));
                    sum += term;
                }

                if (sum > 1 << 30) sum = 1 << 30;
                table[i] = (int)sum;
            }

            for (int i = 1; i < quarter; i++) table[quarter + i] = table[quarter - i];
            for (int i = 0; i < TableSteps / 2; i++) table[TableSteps / 2 + i] = -table[i];
            table[TableSteps] = table[0];
            return table;
        }

        // Sin, Cos: of degrees * 65536, in 16.16.
        public static int Sin(long p_angle)
        {
            long a = p_angle % Deg360;
            if (a < 0) a += Deg360;
            long t = a * TableSteps;
            int index = (int)(t / Deg360);
            long frac = t % Deg360;
            long s0 = s_sin[index], s1 = s_sin[index + 1];
            long value = s0 + (s1 - s0) * frac / Deg360;
            return (int)((value + (1 << 13)) >> 14);
        }

        public static int Cos(long p_angle) => Sin(p_angle + 90 * One);

        // atan(2^-i) in degrees * 2^24.
        static readonly int[] s_atan =
        {
            754974720, 445687602, 235489088, 119537938, 60000934, 30029717, 15018523, 7509720, 3754917, 1877466, 938734,
            469367, 234684, 117342, 58671, 29335, 14668, 7334, 3667, 1833, 917, 458, 229, 115, 57, 29,
        };

        // atan2(p_y, p_x) in degrees * 65536, -180..180 (CORDIC vectoring); 0 for 0, 0.
        public static int Atan2(long p_y, long p_x)
        {
            if (p_x == 0 && p_y == 0) return 0;
            long z = 0;
            if (p_x < 0)
            {
                p_x = -p_x;
                p_y = -p_y;
                z = 180L << 24;
            }

            // To 29-30 bits: down, so the gain (1.65) stays inside a long's room; up, so the shifts keep their precision.
            while (Math.Abs(p_x) >= 1L << 30 || Math.Abs(p_y) >= 1L << 30)
            {
                p_x >>= 1;
                p_y >>= 1;
            }

            while (Math.Abs(p_x) < 1L << 29 && Math.Abs(p_y) < 1L << 29)
            {
                p_x <<= 1;
                p_y <<= 1;
            }

            long rotation = 0;
            for (int i = 0; i < s_atan.Length; i++)
            {
                long x = p_x;
                if (p_y > 0)
                {
                    p_x += p_y >> i;
                    p_y -= x >> i;
                    rotation += s_atan[i];
                }
                else
                {
                    p_x -= p_y >> i;
                    p_y += x >> i;
                    rotation -= s_atan[i];
                }
            }

            z += rotation;
            if (z > 180L << 24) z -= 360L << 24;
            return (int)((z + 128) >> 8);
        }

        // TaskObject.Atan2's quirks in degrees * 65536: the heading of (x, z) (0 along +z, 90 along +x); along an axis
        // exactly that axis's, and 45 for no direction at all.
        public static int Heading(long p_x, long p_z)
        {
            if (p_x == 0)
            {
                if (p_z == 0) return 45 * One;
                return p_z < 0 ? Deg180 : 0;
            }

            if (p_z == 0) return p_x < 0 ? -90 * One : 90 * One;
            return Atan2(p_x, p_z);
        }

        // asin of a 16.16 ratio (clamped to -1..1), degrees * 65536.
        public static int Asin(long p_ratio)
        {
            if (p_ratio >= One) return 90 * One;
            if (p_ratio <= -One) return -90 * One;
            long c = Sqrt(((long)One << 16) - p_ratio * p_ratio);
            return Atan2(p_ratio, c);
        }

        // Quaternion.Euler of degrees * 65536 (Unity's order: z, then x, then y), from the sine table and Unity's managed
        // quaternion product rather than the engine's native trigonometry: the sim's poses (the mechs', the reels', the
        // object tasks') come out the same on every machine.
        public static UnityEngine.Quaternion Rotation(long p_x, long p_y, long p_z)
        {
            var x = new UnityEngine.Quaternion(Sin(p_x / 2) / 65536f, 0, 0, Cos(p_x / 2) / 65536f);
            var y = new UnityEngine.Quaternion(0, Sin(p_y / 2) / 65536f, 0, Cos(p_y / 2) / 65536f);
            var z = new UnityEngine.Quaternion(0, 0, Sin(p_z / 2) / 65536f, Cos(p_z / 2) / 65536f);
            return (y * x * z).normalized; // fixed-point sin/cos leave it a hair off unit length
        }

        // An angle (degrees * 65536) brought into -180..180.
        public static int Wrap180(long p_angle)
        {
            long a = p_angle % Deg360;
            if (a > Deg180) a -= Deg360;
            else if (a < -Deg180) a += Deg360;
            return (int)a;
        }
    }

    // A fixed 3-vector: 16.16 units (a position) or a 16.16 direction, in longs.
    public struct FixVec : IEquatable<FixVec>
    {
        public long X, Y, Z;

        public FixVec(long p_x, long p_y, long p_z)
        {
            X = p_x;
            Y = p_y;
            Z = p_z;
        }

        public static readonly FixVec Zero = default;

        // Whole units to 16.16.
        public static FixVec FromUnits(long p_x, long p_y, long p_z) => new FixVec(p_x << 16, p_y << 16, p_z << 16);
        public static FixVec FromUnits(UnityEngine.Vector3Int p_v) => FromUnits(p_v.x, p_v.y, p_v.z);

        // Floored to whole units.
        public UnityEngine.Vector3Int ToUnits() => new UnityEngine.Vector3Int((int)(X >> 16), (int)(Y >> 16), (int)(Z >> 16));

        // Load-time and render-boundary conversions (float units or a float direction).
        public static FixVec FromFloat(UnityEngine.Vector3 p_v) =>
            new FixVec(Fixed.FromFloatL(p_v.x), Fixed.FromFloatL(p_v.y), Fixed.FromFloatL(p_v.z));

        public UnityEngine.Vector3 ToFloat() => new UnityEngine.Vector3(X / 65536f, Y / 65536f, Z / 65536f);

        // Floored to whole units.
        public long UnitX => X >> 16;
        public long UnitY => Y >> 16;
        public long UnitZ => Z >> 16;

        public static FixVec operator +(FixVec p_a, FixVec p_b) => new FixVec(p_a.X + p_b.X, p_a.Y + p_b.Y, p_a.Z + p_b.Z);
        public static FixVec operator -(FixVec p_a, FixVec p_b) => new FixVec(p_a.X - p_b.X, p_a.Y - p_b.Y, p_a.Z - p_b.Z);
        public static FixVec operator -(FixVec p_a) => new FixVec(-p_a.X, -p_a.Y, -p_a.Z);

        // Scaled by a 16.16 scalar.
        public FixVec Mul(long p_scale) => new FixVec(Fixed.Mul16L(X, p_scale), Fixed.Mul16L(Y, p_scale), Fixed.Mul16L(Z, p_scale));

        // Over a 16.16 scalar.
        public FixVec Div(long p_scale) => new FixVec(Fixed.Div16L(X, p_scale), Fixed.Div16L(Y, p_scale), Fixed.Div16L(Z, p_scale));

        // The 16.16 dot product of two 16.16 vectors.
        public static long Dot(FixVec p_a, FixVec p_b) => Fixed.Mul16L(p_a.X, p_b.X) + Fixed.Mul16L(p_a.Y, p_b.Y) + Fixed.Mul16L(p_a.Z, p_b.Z);

        public long ApproxLength => Fixed.ApproxLength(X, Y, Z);
        public long Length => Fixed.Length16(X, Y, Z);

        // NormalizeVectorGuarded: over the approximate length; a zero vector stays.
        public FixVec NormalizeGuarded()
        {
            long length = ApproxLength;
            return length > 0 ? Div(length) : this;
        }

        public long this[int p_axis] => p_axis == 0 ? X : p_axis == 1 ? Y : Z;

        public bool Equals(FixVec p_other) => X == p_other.X && Y == p_other.Y && Z == p_other.Z;
        public override bool Equals(object p_obj) => p_obj is FixVec other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public static bool operator ==(FixVec p_a, FixVec p_b) => p_a.Equals(p_b);
        public static bool operator !=(FixVec p_a, FixVec p_b) => !p_a.Equals(p_b);
    }
}
