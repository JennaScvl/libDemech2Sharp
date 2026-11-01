using System.Collections.Generic;

namespace Demech2
{
    // QuadtreeNode: a leaf's faces (indices into the shape's faces), or four children (16.16 units).
    public sealed class QuadtreeNode
    {
        public long MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
        public readonly QuadtreeNode[] Children = new QuadtreeNode[4];
        public int[] Faces = new int[0];
    }

    // A terrain shape's quadtree (demech2 MW2/src/quadtree.c), over the shape's world faces, 16.16 units.
    public static class Quadtree
    {
        const long Max = ShapeCollision.Max;

        // BuildShapeQuadtree: over the world box of the model's vertices, four children from it.
        public static void BuildShapeQuadtree(Shape p_shape)
        {
            FixVec[] vertices = p_shape.FxWorldVertices;
            if (vertices.Length == 0) return;
            FixVec v0 = vertices[0];
            var root = new QuadtreeNode { MinX = v0.X, MaxX = v0.X, MinY = v0.Y, MaxY = v0.Y, MinZ = v0.Z, MaxZ = v0.Z };
            for (int i = 1; i < vertices.Length; i++)
            {
                FixVec v = vertices[i];
                if (root.MaxX < v.X) root.MaxX = v.X;
                if (v.X < root.MinX) root.MinX = v.X;
                if (root.MaxZ < v.Z) root.MaxZ = v.Z;
                if (v.Z < root.MinZ) root.MinZ = v.Z;
                if (root.MaxY < v.Y) root.MaxY = v.Y;
                if (root.MinY > v.Y) root.MinY = v.Y;
            }

            p_shape.Quadtree = root;
            for (int j = 0; j < 4; j++) root.Children[j] = BuildQuadtreeChild(root, j, p_shape, 0);
        }

        // BuildQuadtreeChild: up to 25 faces in a leaf; more split it again.
        static QuadtreeNode BuildQuadtreeChild(QuadtreeNode p_node, int p_quadrant, Shape p_shape, int p_depth)
        {
            long minX, maxX, minZ, maxZ;
            if ((p_quadrant & 1) != 0)
            {
                minX = (p_node.MaxX + p_node.MinX) / 2 + Fixed.One;
                maxX = p_node.MaxX;
            }
            else
            {
                minX = p_node.MinX;
                maxX = (p_node.MaxX + p_node.MinX) / 2;
            }

            if ((p_quadrant & 2) != 0)
            {
                minZ = (p_node.MaxZ + p_node.MinZ) / 2 + Fixed.One;
                maxZ = p_node.MaxZ;
            }
            else
            {
                minZ = p_node.MinZ;
                maxZ = (p_node.MaxZ + p_node.MinZ) / 2;
            }

            if (minX >= maxX || minZ >= maxZ) return null;

            FixVec[] vertices = p_shape.FxWorldVertices;
            ushort[][] faces = p_shape.Faces;
            var entries = new List<int>(25);
            long lowY = Max, highY = -Max;
            for (int i = 0; i < faces.Length; i++)
            {
                if (!FaceOverlapsBox(faces[i], vertices, minX, maxX, minZ, maxZ, out long faceLow, out long faceHigh)) continue;
                if (entries.Count + 1 > 25 && p_depth < 24)
                {
                    var node = new QuadtreeNode { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
                    lowY = Max;
                    highY = -Max;
                    for (int j = 0; j < 4; j++)
                    {
                        QuadtreeNode child = node.Children[j] = BuildQuadtreeChild(node, j, p_shape, p_depth + 1);
                        if (child == null) continue;
                        if (child.MaxY > highY) highY = child.MaxY;
                        if (child.MinY < lowY) lowY = child.MinY;
                    }

                    node.MaxY = highY;
                    node.MinY = lowY;
                    // The Matrox edition compares its float with the integer 0x7fffffff here: never true, so no check.
                    return node;
                }

                if (faceHigh > highY) highY = faceHigh;
                if (lowY > faceLow) lowY = faceLow;
                entries.Add(i);
            }

            if (entries.Count == 0) lowY = highY = 0;
            return new QuadtreeNode
            {
                MinX = minX, MaxX = maxX, MinY = lowY, MaxY = highY, MinZ = minZ, MaxZ = maxZ, Faces = entries.ToArray(),
            };
        }

        // FaceOverlapsBox: the face's box against (x, z); its Y span out.
        static bool FaceOverlapsBox(ushort[] p_face, FixVec[] p_vertices, long p_minX, long p_maxX, long p_minZ,
            long p_maxZ, out long p_minY, out long p_maxY)
        {
            long minX = Max, minY = Max, minZ = Max, maxX = -Max, maxY = -Max, maxZ = -Max;
            foreach (ushort index in p_face)
            {
                FixVec v = p_vertices[index];
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Z < minZ) minZ = v.Z;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
                if (v.Z > maxZ) maxZ = v.Z;
            }

            p_minY = minY;
            p_maxY = maxY;
            return minX <= p_maxX && maxX >= p_minX && minZ <= p_maxZ && maxZ >= p_minZ;
        }

        // TestQuadtreeRay: the node's box clipped; a leaf's first face hit, or the nearest hit among the children.
        public static bool TestQuadtreeRay(QuadtreeNode p_node, Shape p_shape, ref CollisionRay p_ray)
        {
            if (p_node == null) return false;
            if (CollisionRay.ClipRaySlab(p_ray.m_x0, p_ray.m_dirX, p_node.MinX, p_node.MaxX, out long tMin, out long tMax)) return false;
            if (CollisionRay.ClipRaySlab(p_ray.m_y0, p_ray.m_dirY, p_node.MinY, p_node.MaxY, out long t0, out long t1)) return false;
            if (t0 > tMin) tMin = t0;
            if (t1 < tMax) tMax = t1;
            if (CollisionRay.ClipRaySlab(p_ray.m_z0, p_ray.m_dirZ, p_node.MinZ, p_node.MaxZ, out t0, out t1)) return false;
            if (t0 > tMin) tMin = t0;
            if (t1 < tMax) tMax = t1;
            if (tMax < tMin) return false;
            if (tMin < 0) tMin = 0;
            t0 = p_ray.GetRayLength();
            if (t0 < tMax) tMax = t0;
            if (tMax < tMin) return false;

            if (p_node.Faces.Length == 0) return TestQuadtreeChildrenRay(p_node, p_shape, ref p_ray);

            FixVec[] vertices = p_shape.FxWorldVertices;
            FixVec[] normals = p_shape.FxWorldNormals;
            ushort[][] faces = p_shape.Faces;
            foreach (int face in p_node.Faces)
            {
                if (ShapeCollision.IntersectRayFace(faces[face], normals[face], vertices, ref p_ray)) return true;
            }

            return false;
        }

        static bool TestQuadtreeChildrenRay(QuadtreeNode p_node, Shape p_shape, ref CollisionRay p_ray)
        {
            long nearest = Max;
            CollisionRay ray = p_ray, best = default;
            for (int i = 0; i < 4; i++)
            {
                if (TestQuadtreeRay(p_node.Children[i], p_shape, ref ray))
                {
                    long length = ray.GetRayLength();
                    if (length < nearest)
                    {
                        nearest = length;
                        best = ray;
                    }

                    ray = p_ray;
                }
            }

            // The Matrox edition compares with the integer 0x7fffffff (units).
            if (nearest < (long)int.MaxValue << 16)
            {
                p_ray = best;
                return true;
            }

            return false;
        }

        // ClassifyQuadtreePoint: 0 outside the tree's ground, 2 above the faces under the point, 3 below one.
        public static int ClassifyQuadtreePoint(QuadtreeNode p_node, Shape p_shape, FixVec p_at)
        {
            if (p_node == null) return 0;
            if (p_node.MinX > p_at.X || p_node.MaxX < p_at.X || p_node.MinZ > p_at.Z || p_node.MaxZ < p_at.Z) return 0;
            if (p_node.MaxY < p_at.Y) return 2;
            if (p_node.MinY > p_at.Y) return 3;
            if (p_node.Faces.Length == 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    int result = ClassifyQuadtreePoint(p_node.Children[i], p_shape, p_at);
                    if (result != 0) return result;
                }

                return 2;
            }

            FixVec[] vertices = p_shape.FxWorldVertices;
            FixVec[] normals = p_shape.FxWorldNormals;
            ushort[][] faces = p_shape.Faces;
            foreach (int face in p_node.Faces)
            {
                if (normals[face].Y > 0 && ShapeCollision.IsPointInFace(faces[face], vertices, p_at.X, p_at.Z, 0, 2))
                {
                    return ShapeCollision.IsBelowFacePlane(faces[face], normals[face], vertices, p_at, out _) ? 3 : 2;
                }
            }

            return 2;
        }
    }
}
