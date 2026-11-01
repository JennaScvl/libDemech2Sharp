using System;

namespace Demech2
{
    // A world pose (16.16): its linear part (rotation and scale, rows) and its translation (units, the world root's
    // frame). The game's scene graph supplies it.
    public struct WorldPose
    {
        public long M00, M01, M02, M10, M11, M12, M20, M21, M22;
        public FixVec Translation;

        // A point (16.16 units, the pose's space) in the world.
        public FixVec TransformPoint(FixVec p_v) => new FixVec(
            Fixed.Mul16L(M00, p_v.X) + Fixed.Mul16L(M01, p_v.Y) + Fixed.Mul16L(M02, p_v.Z),
            Fixed.Mul16L(M10, p_v.X) + Fixed.Mul16L(M11, p_v.Y) + Fixed.Mul16L(M12, p_v.Z),
            Fixed.Mul16L(M20, p_v.X) + Fixed.Mul16L(M21, p_v.Y) + Fixed.Mul16L(M22, p_v.Z)) + Translation;

        public bool Equals(WorldPose p_other) =>
            M00 == p_other.M00 && M01 == p_other.M01 && M02 == p_other.M02 && M10 == p_other.M10 && M11 == p_other.M11 &&
            M12 == p_other.M12 && M20 == p_other.M20 && M21 == p_other.M21 && M22 == p_other.M22 &&
            Translation == p_other.Translation;
    }

    // A shape as the collision tests see it (demech2 MW2/include/shape.h's collision members): its kind (0xf00 the
    // owner's class: 0x100 a player's part, 0x200 a game thing, 0x400 a shot; 0xf0 its type: 0x10 fire, 0x50 debris,
    // 0x70 hidden...), its owner (player, game thing, chunk or static index), the mech section it belongs to
    // (m_partId), its collision type (g_shapeCollisionFns) and whether it is in the collider list (not flag 0x800). Its
    // geometry is its WTB record's first model (16.16, quantised at load by the game), placed by its world pose
    // (ReadWorldPose, the game's): the bounding sphere (ComputeShapeBounds), the world box (EnsureBoundBox), the faces
    // and their normals (ComputeFaceNormal), and for a terrain shape its quadtree (BuildShapeQuadtree).
    public abstract class Shape
    {
        public int Kind;
        public int Owner;
        public int PartId;
        public int CollisionType = 4;
        public bool Collides;
        public int Flags;             // m_flags: 0xf0 the damage level (SetShapeState)
        public int LoadFlags;         // m_flags & 0x10f (SetShapeLoadFlags): the depth sort's (QueueFace)
        public readonly bool Dynamic; // it moves: its world data follow its pose
        public bool BuildQuadtreeOnEnable;

        // Not the original's: a fire or the like (kinds 0x10, 0x60) that each machine turns to its own eye
        // (TurnBillboards): the sim places it unturned.
        public bool FacesEye;

        // Set by the game at load: whether it has a pose at all, the first model's faces, its fixed vertices (16.16 WTB
        // units), normals (in its own scale) and center, and its sphere's radius (16.16 units).
        protected bool hasPose;
        protected ushort[][] faces;
        protected FixVec[] fxModelVertices;
        protected FixVec[] fxModelNormals;
        protected FixVec fxModelCenter;
        public long FxRadius;

        // The fixed world data: the matrix (16.16 rotation and scale, 16.16 units' offset), the center, the vertices,
        // the normals and the box.
        readonly long[] fxMatrix = new long[12];
        bool fxPlaced;
        FixVec fxCenter;
        FixVec[] fxWorldVertices;
        FixVec[] fxWorldNormals;
        bool fxBoxValid, fxVerticesValid;
        FixVec fxBoxMin, fxBoxMax;
        public QuadtreeNode Quadtree;

        protected Shape(bool p_dynamic)
        {
            Dynamic = p_dynamic;
        }

        // The shape's world pose now (the game's scene graph).
        protected abstract WorldPose ReadWorldPose();

        // Its object is in the scene (shown, active): a fire heats only then (HeatMechsNearFires).
        public abstract bool InScene { get; }

        public ushort[][] Faces => faces;

        // m_kind's type (0xf0).
        public int KindType => Kind & 0xf0;

        // TransformShapeCenter: the world pose; the world data follow it once it changed.
        void PlaceFx()
        {
            if (!hasPose) return;
            if (fxPlaced && !Dynamic) return;
            WorldPose pose = ReadWorldPose();
            long[] now =
            {
                pose.M00, pose.M01, pose.M02, pose.Translation.X, pose.M10, pose.M11, pose.M12, pose.Translation.Y,
                pose.M20, pose.M21, pose.M22, pose.Translation.Z,
            };
            if (FacesEye)
            {
                // Only its scale: each column's length on the diagonal.
                for (int column = 0; column < 3; column++)
                {
                    long length = Fixed.Length16(now[column], now[4 + column], now[8 + column]);
                    for (int row = 0; row < 3; row++) now[row * 4 + column] = row == column ? length : 0;
                }
            }

            bool changed = !fxPlaced;
            for (int i = 0; i < 12; i++)
            {
                if (fxMatrix[i] != now[i]) changed = true;
                fxMatrix[i] = now[i];
            }

            if (!changed) return;
            fxPlaced = true;
            fxCenter = Place(fxModelCenter);
            fxBoxValid = fxVerticesValid = false;
        }

        FixVec Place(FixVec p_v) => new FixVec(
            Fixed.Mul16L(fxMatrix[0], p_v.X) + Fixed.Mul16L(fxMatrix[1], p_v.Y) + Fixed.Mul16L(fxMatrix[2], p_v.Z) + fxMatrix[3],
            Fixed.Mul16L(fxMatrix[4], p_v.X) + Fixed.Mul16L(fxMatrix[5], p_v.Y) + Fixed.Mul16L(fxMatrix[6], p_v.Z) + fxMatrix[7],
            Fixed.Mul16L(fxMatrix[8], p_v.X) + Fixed.Mul16L(fxMatrix[9], p_v.Y) + Fixed.Mul16L(fxMatrix[10], p_v.Z) + fxMatrix[11]);

        // The matrix's rotation (each column over its length) applied to a direction.
        FixVec Rotate(FixVec p_v)
        {
            long c0 = Fixed.Length16(fxMatrix[0], fxMatrix[4], fxMatrix[8]);
            long c1 = Fixed.Length16(fxMatrix[1], fxMatrix[5], fxMatrix[9]);
            long c2 = Fixed.Length16(fxMatrix[2], fxMatrix[6], fxMatrix[10]);
            long x = c0 > 0 ? Fixed.Div16L(p_v.X, c0) : 0, y = c1 > 0 ? Fixed.Div16L(p_v.Y, c1) : 0, z = c2 > 0 ? Fixed.Div16L(p_v.Z, c2) : 0;
            return new FixVec(
                Fixed.Mul16L(fxMatrix[0], x) + Fixed.Mul16L(fxMatrix[1], y) + Fixed.Mul16L(fxMatrix[2], z),
                Fixed.Mul16L(fxMatrix[4], x) + Fixed.Mul16L(fxMatrix[5], y) + Fixed.Mul16L(fxMatrix[6], z),
                Fixed.Mul16L(fxMatrix[8], x) + Fixed.Mul16L(fxMatrix[9], y) + Fixed.Mul16L(fxMatrix[10], z));
        }

        // m_center, 16.16 units.
        public FixVec FxCenter
        {
            get
            {
                PlaceFx();
                return fxCenter;
            }
        }

        // The world vertices (each Vertex's m_world), 16.16 units.
        public FixVec[] FxWorldVertices
        {
            get
            {
                PlaceFx();
                if (fxVerticesValid) return fxWorldVertices;
                if (fxWorldVertices == null || fxWorldVertices.Length != fxModelVertices.Length) fxWorldVertices = new FixVec[fxModelVertices.Length];
                for (int i = 0; i < fxModelVertices.Length; i++) fxWorldVertices[i] = Place(fxModelVertices[i]);
                if (fxWorldNormals == null || fxWorldNormals.Length != faces.Length) fxWorldNormals = new FixVec[faces.Length];
                for (int i = 0; i < faces.Length; i++) fxWorldNormals[i] = Rotate(fxModelNormals[i]);
                fxVerticesValid = true;
                return fxWorldVertices;
            }
        }

        // Each face's normal (Face::m_normal) in the world, 16.16.
        public FixVec[] FxWorldNormals
        {
            get
            {
                _ = FxWorldVertices;
                return fxWorldNormals;
            }
        }

        // EnsureBoundBox: the world box of the vertices, 16.16 units.
        public bool EnsureBoundBox(out FixVec p_min, out FixVec p_max)
        {
            PlaceFx();
            if (!fxBoxValid)
            {
                FixVec[] vertices = FxWorldVertices;
                if (vertices.Length == 0)
                {
                    p_min = p_max = FixVec.Zero;
                    return false;
                }

                fxBoxMin = fxBoxMax = vertices[0];
                foreach (FixVec v in vertices)
                {
                    fxBoxMin = new FixVec(Math.Min(fxBoxMin.X, v.X), Math.Min(fxBoxMin.Y, v.Y), Math.Min(fxBoxMin.Z, v.Z));
                    fxBoxMax = new FixVec(Math.Max(fxBoxMax.X, v.X), Math.Max(fxBoxMax.Y, v.Y), Math.Max(fxBoxMax.Z, v.Z));
                }

                fxBoxValid = true;
            }

            p_min = fxBoxMin;
            p_max = fxBoxMax;
            return fxModelVertices.Length > 0;
        }

        // ComputeFaceNormal, fixed: 16.16 vertices in, a 16.16 normal out. A triangle's normal, or for a longer face the
        // normal of its longest edge with the vertex making the largest triangle (the first over 16 wins).
        public static FixVec ComputeFaceNormal(FixVec[] p_vertices, ushort[] p_indices)
        {
            FixVec normal = new FixVec(Fixed.One, 0, 0);
            if (p_indices.Length == 3)
            {
                ComputeTriangleNormal(p_vertices[p_indices[0]], p_vertices[p_indices[1]], p_vertices[p_indices[2]], out normal);
            }
            else if (p_indices.Length > 3)
            {
                long best = 0;
                int a = -1, b = -1;
                int prev = p_indices[0];
                for (int i = p_indices.Length; i-- > 0;)
                {
                    int vertex = p_indices[i];
                    long length = (p_vertices[prev] - p_vertices[vertex]).ApproxLength;
                    if (length > best)
                    {
                        best = length;
                        a = vertex;
                        b = prev;
                    }

                    prev = vertex;
                }

                if (best > 0)
                {
                    long bestArea = -1;
                    for (int i = p_indices.Length; i-- > 0;)
                    {
                        int vertex = p_indices[i];
                        if (vertex == a || vertex == b) continue;
                        long area = ComputeTriangleNormal(p_vertices[a], p_vertices[b], p_vertices[vertex], out FixVec n);
                        if (area > bestArea)
                        {
                            normal = n;
                            bestArea = area;
                            if (area > 16) break;
                        }
                    }
                }
            }

            return normal;
        }

        // ComputeTriangleNormal, fixed: the 16.16 unit normal, and the cross product's length in whole units (squared).
        static long ComputeTriangleNormal(FixVec p_0, FixVec p_1, FixVec p_2, out FixVec p_normal)
        {
            long nx = -(Fixed.Mul16L(p_2.Z - p_1.Z, p_1.Y - p_0.Y) - Fixed.Mul16L(p_1.Z - p_0.Z, p_2.Y - p_1.Y));
            long ny = -(Fixed.Mul16L(p_1.Z - p_0.Z, p_2.X - p_1.X) - Fixed.Mul16L(p_2.Z - p_1.Z, p_1.X - p_0.X));
            long nz = -(Fixed.Mul16L(p_1.X - p_0.X, p_2.Y - p_1.Y) - Fixed.Mul16L(p_1.Y - p_0.Y, p_2.X - p_1.X));
            int shift = 0;
            while (Math.Abs(nx) >= 1L << 30 || Math.Abs(ny) >= 1L << 30 || Math.Abs(nz) >= 1L << 30)
            {
                nx >>= 1;
                ny >>= 1;
                nz >>= 1;
                shift++;
            }

            long length = Fixed.Sqrt(nx * nx + ny * ny + nz * nz);
            p_normal = length > 0 ? new FixVec((nx << 16) / length, (ny << 16) / length, (nz << 16) / length) : FixVec.Zero;
            return length << shift >> 16;
        }
    }
}
