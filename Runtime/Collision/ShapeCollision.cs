using System;
using System.Collections.Generic;

namespace Demech2
{
    // The game's alternative segment test (not the original's: e.g. engine physics), asked first when UseOverride.
    public interface ISegmentTestHost
    {
        bool UseOverride { get; }
        bool TestSegment(ref CollisionRay p_ray, out Shape p_hit, int p_exclude, bool p_scenery);
    }

    // The world's collider list and its tests (demech2 MW2/src/collision.c, shapecollision.c, shapelists.c' collider
    // half, shapegeom.c's RayShapeDistance and ApproximateShapeDistance), the original's floats as 16.16 units
    // (Fixed). Each hit leaves the surface's 16.16 normal in g_hitNormal; TestSegmentCollision's in g_segmentNormal.
    // The terrain quadtrees are Quadtree's.
    public static class ShapeCollision
    {
        public const long Max = long.MaxValue >> 2;

        // g_sceneShapes' collider list: the most recently enabled first (EnableShapeCollision pushes to the front).
        static readonly List<Shape> g_colliders = new List<Shape>();

        // g_hitNormal, g_segmentNormal (16.16).
        public static FixVec g_hitNormal;
        public static FixVec g_segmentNormal;

        // Every shape, in the list or not (g_sceneShapes' draw list): the game's registry.
        public static IEnumerable<Shape> g_sceneShapes = Array.Empty<Shape>();

        public static IReadOnlyList<Shape> Colliders => g_colliders;
        public static IEnumerable<Shape> AllShapes => g_sceneShapes;

        // Not the original's: a snapshot's collider list restored: its shapes in its order, then any this machine has
        // that the snapshot doesn't name.
        public static void RestoreColliders<T>(List<T> p_shapes) where T : Shape
        {
            var others = new List<Shape>();
            foreach (Shape shape in g_colliders)
                if (!(shape is T known) || !p_shapes.Contains(known)) others.Add(shape);
            g_colliders.Clear();
            foreach (T shape in p_shapes)
            {
                shape.Collides = true;
                g_colliders.Add(shape);
            }

            g_colliders.AddRange(others);
        }

        public static void ClearColliders() => g_colliders.Clear();

        // AddSceneShape: it joins the collider list unless its type is 4 or it's disabled.
        public static void AddSceneShape(Shape p_shape, bool p_collides)
        {
            p_shape.Collides = false;
            if (p_collides) EnableShapeCollision(p_shape);
        }

        // SetShapeCollisionType: type 4 disables, any other enables.
        public static void SetShapeCollisionType(Shape p_shape, int p_type)
        {
            p_shape.CollisionType = p_type;
            if (p_type == 4) DisableShapeCollision(p_shape);
            else EnableShapeCollision(p_shape);
        }

        // EnableShapeCollision: back at the front of the list (not type 4).
        public static void EnableShapeCollision(Shape p_shape)
        {
            if (p_shape == null || p_shape.Collides || p_shape.CollisionType == 4) return;
            p_shape.Collides = true;
            g_colliders.Insert(0, p_shape);
            if (p_shape.BuildQuadtreeOnEnable && p_shape.Quadtree == null && p_shape.CollisionType == 5) Quadtree.BuildShapeQuadtree(p_shape);
        }

        // DisableShapeCollision.
        public static void DisableShapeCollision(Shape p_shape)
        {
            if (p_shape == null || !p_shape.Collides) return;
            p_shape.Collides = false;
            g_colliders.Remove(p_shape);
        }

        // ---- Segments and points ----------------------------------------------------------------------------------

        // TestSegmentCollision: the nearest hit of the segment among the colliders, but the parts (kind 0x100) of
        // player p_exclude (all of them when p_exclude < 0). On a hit, the ray ends at it.
        public static bool TestSegmentCollision(ref CollisionRay p_ray, out Shape p_hit, int p_exclude)
        {
            ISegmentTestHost host = Host.SegmentTest;
            if (host != null && host.UseOverride) return host.TestSegment(ref p_ray, out p_hit, p_exclude, false);

            Shape nearest = null;
            long best = Max;
            CollisionRay hitRay = default;
            if (p_ray.GetRayLength() <= 0) return TestPointCollision(p_ray.Start, out p_hit);

            for (int i = 0; i < g_colliders.Count; i++)
            {
                Shape shape = g_colliders[i];
                if ((shape.Kind & 0x100) != 0 && (shape.Owner == p_exclude || p_exclude < 0)) continue;
                long distance = RayShapeDistance(shape, ref p_ray);
                if (distance < best)
                {
                    CollisionRay ray = p_ray;
                    if (TestShapeRay(shape, ref ray, distance))
                    {
                        long length = ray.GetRayLength();
                        if (length < best)
                        {
                            best = length;
                            hitRay = ray;
                            nearest = shape;
                            g_segmentNormal = g_hitNormal;
                        }
                    }
                }
            }

            p_hit = nearest;
            if (nearest == null) return false;
            p_ray = hitRay;
            return true;
        }

        // TestSceneryCollision: TestSegmentCollision against the scenery: no players' parts, nor shapes of type 6.
        public static bool TestSceneryCollision(ref CollisionRay p_ray, out Shape p_hit)
        {
            ISegmentTestHost host = Host.SegmentTest;
            if (host != null && host.UseOverride) return host.TestSegment(ref p_ray, out p_hit, -1, true);

            Shape nearest = null;
            long best = Max;
            CollisionRay hitRay = default;
            if (p_ray.GetRayLength() <= 0) return TestPointCollision(p_ray.Start, out p_hit);

            for (int i = 0; i < g_colliders.Count; i++)
            {
                Shape shape = g_colliders[i];
                if ((shape.Kind & 0x100) != 0 || shape.CollisionType == 6) continue;
                long distance = RayShapeDistance(shape, ref p_ray);
                if (distance < best)
                {
                    CollisionRay ray = p_ray;
                    if (TestShapeRay(shape, ref ray, distance))
                    {
                        best = ray.GetRayLength();
                        hitRay = ray;
                        nearest = shape;
                        g_segmentNormal = g_hitNormal;
                    }
                }
            }

            p_hit = nearest;
            if (nearest == null) return false;
            p_ray = hitRay;
            return true;
        }

        // TestPointCollision: the point against the shape nearest to it.
        public static bool TestPointCollision(FixVec p_at, out Shape p_hit)
        {
            p_hit = FindNearestShape(p_at);
            return p_hit != null && TestShapePoint(p_hit, p_at);
        }

        // FindNearestShape (ApproximateShapeDistance: only shapes whose sphere holds the point).
        static Shape FindNearestShape(FixVec p_at)
        {
            Shape nearest = null;
            long best = Max;
            foreach (Shape shape in g_colliders)
            {
                long distance = ApproximateShapeDistance(shape, p_at);
                if (distance < best)
                {
                    best = distance;
                    nearest = shape;
                }
            }

            return nearest;
        }

        // ApproximateShapeDistance: the approximate distance to the shape's center, or Max outside its sphere.
        static long ApproximateShapeDistance(Shape p_shape, FixVec p_at)
        {
            FixVec d = p_shape.FxCenter - p_at;
            long radius = p_shape.FxRadius;
            if (radius >= d.X && -radius <= d.X && radius >= d.Y && -radius <= d.Y && radius >= d.Z && -radius <= d.Z &&
                Fixed.Mul16L(radius, radius) - FixVec.Dot(d, d) > 0)
            {
                return d.ApproxLength;
            }

            return Max;
        }

        // RayShapeDistance: how far along the ray it comes to the shape's sphere (the closest approach less the
        // radius), Max when it misses or ends first, 10 when it starts inside.
        static long RayShapeDistance(Shape p_shape, ref CollisionRay p_ray)
        {
            long radius = p_shape.FxRadius;
            if (radius <= 0) return Max;
            FixVec c = p_shape.FxCenter;
            FixVec d = new FixVec(c.X - p_ray.m_x0, c.Y - p_ray.m_y0, c.Z - p_ray.m_z0);
            long excess = FixVec.Dot(d, d) - Fixed.Mul16L(radius, radius);
            if (excess < 0) return 10L << 16;
            long t = FixVec.Dot(d, new FixVec(p_ray.m_dx, p_ray.m_dy, p_ray.m_dz));
            if (t < 0) return Max;
            t = Fixed.Div16L(t, p_ray.m_length);
            long check = Fixed.Mul16L(t, t) - excess;
            if (check < 0) return Max;
            t -= radius;
            if (t < 0) return 0;
            return t >= p_ray.GetRayLength() ? Max : t;
        }

        // TestShapeRay: the type's ray test, else its point test at four steps along the ray, else the sphere.
        static bool TestShapeRay(Shape p_shape, ref CollisionRay p_ray, long p_distance)
        {
            if (p_ray.GetRayLength() <= 0) return true;
            switch (p_shape.CollisionType)
            {
                case 0:
                case 1:
                    return TestRayBox(p_shape, ref p_ray);
                case 2:
                case 7:
                    return TestRayFaces(p_shape, ref p_ray);
                case 4:
                    return false;
                case 5:
                    return Quadtree.TestQuadtreeRay(p_shape.Quadtree, p_shape, ref p_ray);
            }

            EndRayAtShape(p_shape, ref p_ray, p_distance);
            return true;
        }

        // EndRayAtShape: cut at p_distance; the normal from the center to the end (to the start at distance 10).
        static void EndRayAtShape(Shape p_shape, ref CollisionRay p_ray, long p_distance)
        {
            if (p_distance > 0) p_ray.SetRayLength(p_distance);
            FixVec from = p_distance == 10L << 16 ? p_ray.Start : p_ray.End;
            g_hitNormal = (from - p_shape.FxCenter).NormalizeGuarded();
        }

        // TestShapePoint: the type's point test; a type without one always hits.
        static bool TestShapePoint(Shape p_shape, FixVec p_at)
        {
            switch (p_shape.CollisionType)
            {
                case 0:
                    ClassifyPointInBox(p_shape, p_at, out bool inside, out _, out _);
                    return inside;
                case 1:
                    return TestPointInBoxColumn(p_shape, p_at);
                case 2:
                    return TestPointAboveFaces(p_shape, p_at);
                case 4:
                    return false;
                case 5:
                    return TestPointTerrain(p_shape, p_at);
            }

            return true;
        }

        // ---- Box ---------------------------------------------------------------------------------------------------

        static void ClassifyPointInBox(Shape p_shape, FixVec p_at, out bool p_inside, out bool p_inColumn, out long p_top)
        {
            p_inside = p_inColumn = false;
            p_top = 0;
            if (!p_shape.EnsureBoundBox(out FixVec min, out FixVec max)) return;
            if (p_at.X >= min.X && p_at.X <= max.X && p_at.Z >= min.Z && p_at.Z <= max.Z)
            {
                p_inColumn = true;
                p_top = max.Y;
                p_inside = p_at.Y >= min.Y && p_at.Y <= max.Y;
            }
        }

        static bool TestPointInBoxColumn(Shape p_shape, FixVec p_at)
        {
            if (!p_shape.EnsureBoundBox(out FixVec min, out FixVec max)) return false;
            return p_at.X >= min.X && p_at.X <= max.X && p_at.Z >= min.Z && p_at.Z <= max.Z;
        }

        // TestRayBox: the slabs in X, Y and Z; a ray starting inside the box hits at its start. The normal is the
        // entered face's.
        static bool TestRayBox(Shape p_shape, ref CollisionRay p_ray)
        {
            if (!p_shape.EnsureBoundBox(out FixVec min, out FixVec max)) return false;
            if (CollisionRay.ClipRaySlab(p_ray.m_x0, p_ray.m_dirX, min.X, max.X, out long tNear, out long tFar)) return false;
            int axis = 0;
            if (CollisionRay.ClipRaySlab(p_ray.m_y0, p_ray.m_dirY, min.Y, max.Y, out long tEnter, out long tExit)) return false;
            if (tNear < tEnter)
            {
                axis = 1;
                tNear = tEnter;
            }

            if (tFar > tExit) tFar = tExit;
            if (CollisionRay.ClipRaySlab(p_ray.m_z0, p_ray.m_dirZ, min.Z, max.Z, out tEnter, out tExit)) return false;
            if (tNear < tEnter)
            {
                tNear = tEnter;
                axis = 2;
            }

            if (tFar > tExit) tFar = tExit;
            if (tFar < tNear) return false;
            if (tNear < 0) tNear = 0;
            tEnter = p_ray.GetRayLength();
            if (tFar > tEnter) tFar = tEnter;
            if (tFar < tNear) return false;

            p_ray.SetRayLength(tNear);
            switch (axis)
            {
                case 0:
                    g_hitNormal = new FixVec(p_ray.m_dx > 0 ? -Fixed.One : Fixed.One, 0, 0);
                    break;
                case 1:
                    g_hitNormal = new FixVec(0, p_ray.m_dy > 0 ? -Fixed.One : Fixed.One, 0);
                    break;
                default:
                    g_hitNormal = new FixVec(0, 0, p_ray.m_dz > 0 ? -Fixed.One : Fixed.One);
                    break;
            }

            return true;
        }

        // ---- Faces -------------------------------------------------------------------------------------------------

        // TestRayFaces: the first face, in the model's order, the ray hits.
        static bool TestRayFaces(Shape p_shape, ref CollisionRay p_ray)
        {
            FixVec[] vertices = p_shape.FxWorldVertices;
            FixVec[] normals = p_shape.FxWorldNormals;
            ushort[][] faces = p_shape.Faces;
            for (int i = 0; i < faces.Length; i++)
            {
                if (IntersectRayFace(faces[i], normals[i], vertices, ref p_ray)) return true;
            }

            return false;
        }

        // TestPointAboveFaces: the first downward face over (x, z): whether the point is above its plane.
        static bool TestPointAboveFaces(Shape p_shape, FixVec p_at)
        {
            FixVec[] vertices = p_shape.FxWorldVertices;
            FixVec[] normals = p_shape.FxWorldNormals;
            ushort[][] faces = p_shape.Faces;
            for (int i = 0; i < faces.Length; i++)
            {
                FixVec n = normals[i];
                if (n.Y < 0 && IsPointInFace(faces[i], vertices, p_at.X, p_at.Z, 0, 2))
                {
                    FixVec v = vertices[faces[i][0]];
                    long height = v.Y - Fixed.Div16L(Fixed.Mul16L(p_at.Z - v.Z, n.Z) + Fixed.Mul16L(p_at.X - v.X, n.X), n.Y);
                    g_hitNormal = n;
                    return height < p_at.Y;
                }
            }

            return false;
        }

        // TestPointUnderFloor: the first upward face under (x, z): whether the point is below its plane.
        static bool TestPointUnderFloor(Shape p_shape, FixVec p_at)
        {
            FixVec[] vertices = p_shape.FxWorldVertices;
            FixVec[] normals = p_shape.FxWorldNormals;
            ushort[][] faces = p_shape.Faces;
            for (int i = 0; i < faces.Length; i++)
            {
                if (normals[i].Y > 0 && IsPointInFace(faces[i], vertices, p_at.X, p_at.Z, 0, 2))
                {
                    return IsBelowFacePlane(faces[i], normals[i], vertices, p_at, out _);
                }
            }

            return false;
        }

        // TestPointTerrain: below one of the quadtree's faces, or without one under the first upward face.
        static bool TestPointTerrain(Shape p_shape, FixVec p_at)
        {
            if (p_shape.Quadtree != null) return (Quadtree.ClassifyQuadtreePoint(p_shape.Quadtree, p_shape, p_at) & 1) != 0;
            return TestPointUnderFloor(p_shape, p_at);
        }

        // IsBelowFacePlane: the plane's height at (x, z); the hit normal the face's.
        internal static bool IsBelowFacePlane(ushort[] p_face, FixVec p_normal, FixVec[] p_vertices, FixVec p_at, out long p_height)
        {
            FixVec v = p_vertices[p_face[0]];
            p_height = v.Y - Fixed.Div16L(Fixed.Mul16L(p_at.X - v.X, p_normal.X) + Fixed.Mul16L(p_at.Z - v.Z, p_normal.Z), p_normal.Y);
            g_hitNormal = p_normal;
            return p_at.Y < p_height;
        }

        // IntersectRayFace: the ray against the face's plane from its front, the point within the face.
        internal static bool IntersectRayFace(ushort[] p_face, FixVec p_normal, FixVec[] p_vertices, ref CollisionRay p_ray)
        {
            if (p_face.Length < 1) return false;
            long denom = FixVec.Dot(p_ray.Direction, p_normal);
            if (denom >= 0) return false;
            FixVec v = p_vertices[p_face[0]];
            long d = -FixVec.Dot(v, p_normal);
            long num = FixVec.Dot(p_ray.Start, p_normal) + d;
            if (num <= 0) return false;
            long t = -Fixed.Div16L(num, denom);
            if (t <= 0 || p_ray.m_length < t) return false;
            long x = Fixed.Mul16L(p_ray.m_dirX, t) + p_ray.m_x0;
            long y = Fixed.Mul16L(p_ray.m_dirY, t) + p_ray.m_y0;
            long z = Fixed.Mul16L(p_ray.m_dirZ, t) + p_ray.m_z0;
            if (!IsPointInFace(p_face, p_normal, p_vertices, x, y, z)) return false;
            p_ray.SetRayEnd(x, y, z);
            g_hitNormal = p_normal;
            return true;
        }

        // IsPointInFace: projected on the plane the normal is closest to.
        static bool IsPointInFace(ushort[] p_face, FixVec p_normal, FixVec[] p_vertices, long p_x, long p_y, long p_z)
        {
            if (p_face.Length < 3) return false;
            long nx = Math.Abs(p_normal.X), ny = Math.Abs(p_normal.Y), nz = Math.Abs(p_normal.Z);
            if (ny > nx && ny > nz) return IsPointInFace(p_face, p_vertices, p_x, p_z, 0, 2);
            if (nz < nx) return IsPointInFace(p_face, p_vertices, p_y, p_z, 1, 2);
            return IsPointInFace(p_face, p_vertices, p_x, p_y, 0, 1);
        }

        // IsPointInFaceXZ, IsPointInFaceXY, IsPointInFaceYZ: (p_a, p_b) on axes p_axisA and p_axisB. The point needs
        // vertices on each side on both axes; then the edges crossing its b must pass it on both sides of a.
        public static bool IsPointInFace(ushort[] p_face, FixVec[] p_vertices, long p_a, long p_b, int p_axisA, int p_axisB)
        {
            int count = p_face.Length;
            if (count < 3) return false;
            int sides = 0;
            for (int i = count; i-- > 0;)
            {
                FixVec v = p_vertices[p_face[i]];
                sides |= v[p_axisA] <= p_a ? 1 : 2;
                sides |= v[p_axisB] <= p_b ? 4 : 8;
                if (sides == 15) break;
            }

            if (sides != 15) return false;

            bool left = false, right = false;
            FixVec prev = p_vertices[p_face[0]];
            for (int i = count; i-- > 0;)
            {
                FixVec vertex = p_vertices[p_face[i]];
                long va = vertex[p_axisA], vb = vertex[p_axisB], pa = prev[p_axisA], pb = prev[p_axisB];
                prev = vertex;
                if (vb < p_b && pb < p_b) continue;
                if (vb > p_b && pb > p_b) continue;
                if (vb == p_b && pb == p_b)
                {
                    if (va < p_a && pa < p_a) return false;
                    if (va > p_a && pa > p_a) return false;
                    return true;
                }

                if (vb == p_b) continue;
                if (va < p_a && pa < p_a)
                {
                    if (left) return false;
                    if (right) return true;
                    left = true;
                    continue;
                }

                if (va > p_a && pa > p_a)
                {
                    if (right) return false;
                    if (left) return true;
                    right = true;
                    continue;
                }

                long offset = va - p_a + Fixed.Div16L(Fixed.Mul16L(pa - va, p_b - vb), pb - vb);
                if (offset < 0)
                {
                    if (left) return false;
                    if (right) return true;
                    left = true;
                }
                else if (offset > 0)
                {
                    if (right) return false;
                    if (left) return true;
                    right = true;
                }
                else return true;
            }

            return false;
        }
    }
}
