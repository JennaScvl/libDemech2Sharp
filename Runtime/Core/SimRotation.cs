using System;
using UnityEngine;

namespace Demech2
{
    // 3x3 rotations as rows in 2.30.
    public static class SimRotation
    {
        const long One = 1L << 30;

        public static long[] Identity => new[] { One, 0, 0, 0, One, 0, 0, 0, One };

        static long Q(long p_sin16) => p_sin16 << 14;

        // a * b, 2.30 (each product's 60 bits fit a long).
        static long M(long a, long b) => a * b >> 30;

        // Quaternion.Euler's rotation (Unity's order: z, then x, then y: Ry·Rx·Rz) of 16.16 degrees, from the sine table.
        public static long[] Euler(long p_x, long p_y, long p_z)
        {
            long sx = Q(Fixed.Sin(p_x)), cx = Q(Fixed.Cos(p_x));
            long sy = Q(Fixed.Sin(p_y)), cy = Q(Fixed.Cos(p_y));
            long sz = Q(Fixed.Sin(p_z)), cz = Q(Fixed.Cos(p_z));
            long[] ry = { cy, 0, sy, 0, One, 0, -sy, 0, cy };
            long[] rx = { One, 0, 0, 0, cx, -sx, 0, sx, cx };
            long[] rz = { cz, -sz, 0, sz, cz, 0, 0, 0, One };
            return Multiply(Multiply(ry, rx), rz);
        }

        public static long[] Multiply(long[] a, long[] b)
        {
            var r = new long[9];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) r[i * 3 + j] = M(a[i * 3], b[j]) + M(a[i * 3 + 1], b[3 + j]) + M(a[i * 3 + 2], b[6 + j]);
            }

            return r;
        }

        public static long[] Transpose(long[] a) => new[] { a[0], a[3], a[6], a[1], a[4], a[7], a[2], a[5], a[8] };

        // Load-time quantisation of a built Transform's rotation.
        public static long[] FromQuaternion(Quaternion p_q)
        {
            double x = p_q.x, y = p_q.y, z = p_q.z, w = p_q.w;
            double n = x * x + y * y + z * z + w * w;
            double s = n > 0 ? 2 / n : 0;
            double[] m =
            {
                1 - s * (y * y + z * z), s * (x * y - z * w), s * (x * z + y * w),
                s * (x * y + z * w), 1 - s * (x * x + z * z), s * (y * z - x * w),
                s * (x * z - y * w), s * (y * z + x * w), 1 - s * (x * x + y * y),
            };
            var r = new long[9];
            for (int i = 0; i < 9; i++) r[i] = (long)Math.Round(m[i] * One);
            return r;
        }

        // For the render side: the rotation as a quaternion.
        public static Quaternion ToQuaternion(long[] p_r)
        {
            var forward = new Vector3(p_r[2], p_r[5], p_r[8]) / One;
            var up = new Vector3(p_r[1], p_r[4], p_r[7]) / One;
            return forward == Vector3.zero ? Quaternion.identity : Quaternion.LookRotation(forward, up);
        }
    }
}
