namespace Demech2
{
    // A ray (demech2 MW2/include/ray.h, MW2/src/ray.c), the original's floats as 16.16 fixed point (Fixed): a
    // segment from (x0, y0, z0) to (x1, y1, z1) in 16.16 units, its delta, and once built its 16.16 direction and
    // length. Built "fixed", the length is the approximate one (ApproximateVectorLength) and the direction the delta
    // over it, so not quite a unit vector; built "float", the length is the exact root and the direction a unit
    // vector. Named CollisionRay, not Ray, so it doesn't clash with UnityEngine.Ray.
    public struct CollisionRay
    {
        public const int c_rayNone = 0, c_rayFixed = 1, c_rayFloat = 2;

        public long m_x0, m_y0, m_z0, m_x1, m_y1, m_z1;
        public long m_dx, m_dy, m_dz;
        public long m_dirX, m_dirY, m_dirZ;
        public long m_length;
        public int m_state;

        public FixVec Start => new FixVec(m_x0, m_y0, m_z0);
        public FixVec End => new FixVec(m_x1, m_y1, m_z1);
        public FixVec Direction => new FixVec(m_dirX, m_dirY, m_dirZ);

        // BuildRayFromSegment.
        public static CollisionRay BuildRayFromSegment(FixVec p_start, FixVec p_end)
        {
            return new CollisionRay
            {
                m_x0 = p_start.X, m_y0 = p_start.Y, m_z0 = p_start.Z,
                m_x1 = p_end.X, m_y1 = p_end.Y, m_z1 = p_end.Z,
                m_dx = p_end.X - p_start.X, m_dy = p_end.Y - p_start.Y, m_dz = p_end.Z - p_start.Z,
                m_state = c_rayNone,
            };
        }

        // BuildRayFromDirection: the direction as given (not normalized); p_length 16.16 units.
        public static CollisionRay BuildRayFromDirection(FixVec p_start, FixVec p_direction, long p_length)
        {
            var ray = new CollisionRay
            {
                m_x0 = p_start.X, m_y0 = p_start.Y, m_z0 = p_start.Z,
                m_dirX = p_direction.X, m_dirY = p_direction.Y, m_dirZ = p_direction.Z,
                m_dx = Fixed.Mul16L(p_length, p_direction.X), m_dy = Fixed.Mul16L(p_length, p_direction.Y),
                m_dz = Fixed.Mul16L(p_length, p_direction.Z),
                m_length = p_length,
                m_state = c_rayFixed,
            };
            ray.m_x1 = ray.m_dx + ray.m_x0;
            ray.m_y1 = ray.m_dy + ray.m_y0;
            ray.m_z1 = ray.m_dz + ray.m_z0;
            return ray;
        }

        // GetRayLength.
        public long GetRayLength()
        {
            if (m_state == c_rayNone) BuildRayFixed();
            return m_length;
        }

        // BuildRayFixed.
        public void BuildRayFixed()
        {
            if (m_state != c_rayNone) return;
            m_length = Fixed.ApproxLength(m_dx, m_dy, m_dz);
            if (m_length > 0)
            {
                m_dirX = Fixed.Div16L(m_dx, m_length);
                m_dirY = Fixed.Div16L(m_dy, m_length);
                m_dirZ = Fixed.Div16L(m_dz, m_length);
                m_state = c_rayFixed;
            }
        }

        // BuildRayFloat: the exact length (an integer root here).
        public void BuildRayFloat()
        {
            if (m_state == c_rayFloat) return;
            long dx = m_x1 - m_x0, dy = m_y1 - m_y0, dz = m_z1 - m_z0;
            long length = Fixed.Length16(dx, dy, dz);
            if (length > 0)
            {
                m_dirX = Fixed.Div16L(dx, length);
                m_dirY = Fixed.Div16L(dy, length);
                m_dirZ = Fixed.Div16L(dz, length);
            }

            m_length = length;
            m_state = c_rayFloat;
        }

        // SetRayLength.
        public void SetRayLength(long p_length)
        {
            if (m_state == c_rayNone) BuildRayFixed();
            if (m_state == c_rayNone) return;
            m_x1 = Fixed.Mul16L(m_dirX, p_length) + m_x0;
            m_y1 = Fixed.Mul16L(m_dirY, p_length) + m_y0;
            m_z1 = Fixed.Mul16L(m_dirZ, p_length) + m_z0;
            m_dx = m_x1 - m_x0;
            m_dy = m_y1 - m_y0;
            m_dz = m_z1 - m_z0;
            m_length = p_length;
        }

        // SetRayEnd: rebuilt the way it was built.
        public void SetRayEnd(long p_x1, long p_y1, long p_z1)
        {
            m_x1 = p_x1;
            m_y1 = p_y1;
            m_z1 = p_z1;
            m_dx = p_x1 - m_x0;
            m_dy = p_y1 - m_y0;
            m_dz = p_z1 - m_z0;
            int state = m_state;
            m_state = c_rayNone;
            switch (state)
            {
                case c_rayFixed:
                    BuildRayFixed();
                    break;
                case c_rayFloat:
                    BuildRayFloat();
                    break;
                default:
                    m_dirX = m_dirY = m_dirZ = m_length = 0;
                    break;
            }
        }

        // ClipRayToGround: cut where the ray reaches height p_y (16.16 units).
        public void ClipRayToGround(long p_y)
        {
            if (m_dy != 0) SetRayLength(Fixed.Div16L(Fixed.Mul16L(p_y - m_y0, m_length), m_dy));
        }

        // ClipRaySlab: the ray's span (as multiples of p_delta, 16.16) between p_min and p_max on one axis. True for a
        // miss (parallel to the slab and outside it).
        public static bool ClipRaySlab(long p_origin, long p_delta, long p_min, long p_max, out long p_tMin, out long p_tMax)
        {
            long t0, t1;
            if (p_delta == 0)
            {
                if (p_origin < p_min || p_origin > p_max)
                {
                    p_tMin = p_tMax = 0;
                    return true;
                }

                t0 = -ShapeCollision.Max;
                t1 = ShapeCollision.Max;
            }
            else
            {
                t0 = Fixed.Div16L(p_min - p_origin, p_delta);
                t1 = Fixed.Div16L(p_max - p_origin, p_delta);
            }

            if (t1 < t0)
            {
                p_tMin = t1;
                p_tMax = t0;
            }
            else
            {
                p_tMin = t0;
                p_tMax = t1;
            }

            return false;
        }
    }
}
