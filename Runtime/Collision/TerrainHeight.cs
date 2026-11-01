using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // The ground a mech stands on (demech2 MW2/src/collision.c: GetTerrainHeight, IsPointInFaceXZ, IsBelowFacePlane;
    // shapecollision.c: GetBoxTop; quadtree.c: BuildShapeQuadtree, BuildQuadtreeChild, GetQuadtreeTop): the world's
    // shapes of collision type 0 (a box: the top of the shape's bounding box) and 5 (terrain: its upward faces, found
    // through a quadtree built once from where the shape stands, the height solved from the face's plane).
    // Coordinates are MW2 units (centimetres) in the world's frame, y up, in 16.16 (Fixed): the meshes' triangles
    // quantised at load by the game, the queries integer math. The game supplies the shapes, their nodes' poses and
    // which game's rules the map plays by.
    public abstract class TerrainHeight
    {
        // QuadtreeNode: a leaf holds up to 25 faces (the shape's polygons, as the original's, not their fanned
        // triangles); an inner node (no faces) four children (16.16 units).
        protected sealed class Node
        {
            public long MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
            public Node[] Children = new Node[4];
            public int[] Faces;
        }

        protected sealed class GroundShape
        {
            public int Kind;
            public object[] Nodes;     // the game's handle of each of its meshes' nodes (GetNodePose)
            public WorldPose Pose;     // its first mesh's node's world pose when its data was built
            public FixVec[][] Local;   // a box's meshes' vertices, quantised at load (16.16 units, each mesh's space)
            public FixVec BoxMin, BoxMax;
            public FixVec[] Vertices;  // a terrain's, in the world frame
            public int[] Triangles;
            public FixVec[] Normals;   // each triangle's (16.16)
            public int[] PolygonFirst; // each polygon's first triangle; its triangles run to the next polygon's first
            public Node Root;
            public int[][] LocalTriangles; // Mercenaries' box shapes: each mesh's triangles, its winding flipped (load time)
            public bool[] LocalMirrored;
            public WorldPose FacesPose;    // the pose their quadtree was built at
            public bool BoxBuilt;
        }

        // Which game's ground rules the map plays by (GroundRules).
        protected const int RulesMain = 0, RulesGhostBear = 1, RulesMercenaries = 2;

        const int LeafFaces = 25;
        const long RayLift = 10L << 16; // how far above the point the ground ray starts
        const long MercsRayLift = 1000L << 16; // Mercenaries' window above the point

        readonly List<GroundShape> shapes = new List<GroundShape>();

        static readonly FixVec Up = new FixVec(0, Fixed.One, 0);

        // g_groundNormal: the normal of the surface the last query returned (zero for none), 16.16.
        public FixVec GroundNormal { get; private set; }

        public int ShapeCount => shapes.Count;

        // The world pose of node p_node (one of a shape's Nodes).
        protected abstract WorldPose GetNodePose(object p_node);

        // The map's game's rules, asked on each query (RulesMain, RulesGhostBear, RulesMercenaries).
        protected abstract int GroundRules { get; }

        protected void AddShape(GroundShape p_shape) => shapes.Add(p_shape);

        static FixVec Min(FixVec p_a, FixVec p_b) => new FixVec(Math.Min(p_a.X, p_b.X), Math.Min(p_a.Y, p_b.Y), Math.Min(p_a.Z, p_b.Z));
        static FixVec Max(FixVec p_a, FixVec p_b) => new FixVec(Math.Max(p_a.X, p_b.X), Math.Max(p_a.Y, p_b.Y), Math.Max(p_a.Z, p_b.Z));

        // EnsureBoundBox, ComputeModelBounds: the box of the shape's vertices (Local, filled at load) where it stands
        // (its meshes' nodes), again once it moved.
        protected void UpdateBox(GroundShape p_shape)
        {
            WorldPose now = GetNodePose(p_shape.Nodes[0]);
            if (p_shape.BoxBuilt && now.Equals(p_shape.Pose)) return;
            p_shape.Pose = now;
            p_shape.BoxBuilt = true;
            var min = new FixVec(long.MaxValue, long.MaxValue, long.MaxValue);
            var max = new FixVec(long.MinValue, long.MinValue, long.MinValue);
            for (int f = 0; f < p_shape.Nodes.Length; f++)
            {
                WorldPose pose = GetNodePose(p_shape.Nodes[f]);
                foreach (FixVec vertex in p_shape.Local[f])
                {
                    FixVec v = pose.TransformPoint(vertex);
                    min = Min(min, v);
                    max = Max(max, v);
                }
            }

            p_shape.BoxMin = min;
            p_shape.BoxMax = max;
        }

        // BuildShapeQuadtree: the faces (world frame, 16.16 units) and their normals (ComputeFaceNormal); the root the
        // box of the vertices, its four children built from it.
        protected static void BuildQuadtreeFrom(GroundShape p_shape, List<FixVec> vertices, List<int> triangles)
        {
            if (vertices.Count == 0) return;
            p_shape.Vertices = vertices.ToArray();
            p_shape.Triangles = triangles.ToArray();
            p_shape.Normals = new FixVec[p_shape.Triangles.Length / 3];
            for (int f = 0; f < p_shape.Normals.Length; f++)
            {
                FixVec a = p_shape.Vertices[p_shape.Triangles[f * 3]];
                FixVec b = p_shape.Vertices[p_shape.Triangles[f * 3 + 1]];
                FixVec c = p_shape.Vertices[p_shape.Triangles[f * 3 + 2]];
                p_shape.Normals[f] = UnitCross(b - a, c - a);
            }

            // The polygons back from their fans (MWMeshFile: each polygon its own vertices, its triangles in a row,
            // all from its first vertex).
            var polygons = new List<int>();
            for (int f = 0; f < p_shape.Normals.Length; f++)
                if (f == 0 || p_shape.Triangles[f * 3] != p_shape.Triangles[(f - 1) * 3]) polygons.Add(f);
            polygons.Add(p_shape.Normals.Length);
            p_shape.PolygonFirst = polygons.ToArray();

            FixVec min = p_shape.Vertices[0], max = p_shape.Vertices[0];
            foreach (FixVec v in p_shape.Vertices)
            {
                min = Min(min, v);
                max = Max(max, v);
            }

            var node = new Node { MinX = min.X, MaxX = max.X, MinY = min.Y, MaxY = max.Y, MinZ = min.Z, MaxZ = max.Z };
            for (int j = 0; j < 4; j++) node.Children[j] = BuildQuadtreeChild(p_shape, node, j, 0);
            p_shape.Root = node;
        }

        // Vector3.Cross(p_u, p_v).normalized in integers: 16.16 in, a 16.16 unit vector out (zero for none).
        static FixVec UnitCross(FixVec p_u, FixVec p_v)
        {
            long x = Fixed.Mul16L(p_u.Y, p_v.Z) - Fixed.Mul16L(p_u.Z, p_v.Y);
            long y = Fixed.Mul16L(p_u.Z, p_v.X) - Fixed.Mul16L(p_u.X, p_v.Z);
            long z = Fixed.Mul16L(p_u.X, p_v.Y) - Fixed.Mul16L(p_u.Y, p_v.X);
            while (Math.Abs(x) >= 1L << 30 || Math.Abs(y) >= 1L << 30 || Math.Abs(z) >= 1L << 30)
            {
                x >>= 1;
                y >>= 1;
                z >>= 1;
            }

            long length = Fixed.Sqrt(x * x + y * y + z * z);
            return length > 0 ? new FixVec((x << 16) / length, (y << 16) / length, (z << 16) / length) : FixVec.Zero;
        }

        // Floored to a whole unit (Mathf.Floor of the float's).
        static long FloorUnit(long p_value) => p_value & ~0xffffL;

        // BuildQuadtreeChild: quadrant bit 1 the high X half, bit 2 the high Z half (from the middle + 1); null when
        // it's empty. More than 25 faces split it again.
        static Node BuildQuadtreeChild(GroundShape p_shape, Node p_node, int p_quadrant, int p_depth)
        {
            long midX = FloorUnit((p_node.MaxX + p_node.MinX) >> 1), midZ = FloorUnit((p_node.MaxZ + p_node.MinZ) >> 1);
            long minX = (p_quadrant & 1) != 0 ? midX + Fixed.One : p_node.MinX;
            long maxX = (p_quadrant & 1) != 0 ? p_node.MaxX : midX;
            long minZ = (p_quadrant & 2) != 0 ? midZ + Fixed.One : p_node.MinZ;
            long maxZ = (p_quadrant & 2) != 0 ? p_node.MaxZ : midZ;
            if (minX >= maxX || minZ >= maxZ) return null;

            var faces = new List<int>();
            long lowY = long.MaxValue, highY = long.MinValue;
            for (int f = 0; f + 1 < p_shape.PolygonFirst.Length; f++)
            {
                if (!FaceOverlapsBox(p_shape, f, minX, maxX, minZ, maxZ, out long faceLow, out long faceHigh)) continue;
                faces.Add(f);
                if (faces.Count > LeafFaces && p_depth < 24)
                {
                    var inner = new Node { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
                    lowY = long.MaxValue;
                    highY = long.MinValue;
                    for (int j = 0; j < 4; j++)
                    {
                        Node child = inner.Children[j] = BuildQuadtreeChild(p_shape, inner, j, p_depth + 1);
                        if (child == null) continue;
                        highY = Math.Max(highY, child.MaxY);
                        lowY = Math.Min(lowY, child.MinY);
                    }

                    inner.MaxY = highY;
                    inner.MinY = lowY;
                    if (lowY == long.MaxValue) inner.MinY = inner.MaxY = 0;
                    return inner;
                }

                highY = Math.Max(highY, faceHigh);
                lowY = Math.Min(lowY, faceLow);
            }

            if (faces.Count == 0) lowY = highY = 0;
            return new Node
            {
                MinX = minX, MaxX = maxX, MinY = lowY, MaxY = highY, MinZ = minZ, MaxZ = maxZ,
                Faces = faces.Count > 0 ? faces.ToArray() : null,
            };
        }

        static bool FaceOverlapsBox(GroundShape p_shape, int p_face, long p_minX, long p_maxX, long p_minZ, long p_maxZ,
            out long p_minY, out long p_maxY)
        {
            var min = new FixVec(long.MaxValue, long.MaxValue, long.MaxValue);
            var max = new FixVec(long.MinValue, long.MinValue, long.MinValue);
            for (int t = p_shape.PolygonFirst[p_face]; t < p_shape.PolygonFirst[p_face + 1]; t++)
            for (int k = 0; k < 3; k++)
            {
                FixVec v = p_shape.Vertices[p_shape.Triangles[t * 3 + k]];
                min = Min(min, v);
                max = Max(max, v);
            }

            p_minY = min.Y;
            p_maxY = max.Y;
            return min.X <= p_maxX && max.X >= p_minX && min.Z <= p_maxZ && max.Z >= p_minZ;
        }

        // The render side's query: float units in and out, GroundNormal and g_hitNormal left as they were.
        public float GetTerrainHeight(Vector3 p_at)
        {
            FixVec normal = GroundNormal, kept = hitNormal;
            long height = GetTerrainHeight(FixVec.FromFloat(p_at));
            GroundNormal = normal;
            hitNormal = kept;
            return height / 65536f;
        }

        // The ground under p_at (16.16 units): a ray cast straight down from just above it, its first hit on a box top
        // or an upward terrain face; never below the 31CC ground plane at 0. GroundNormal the hit surface's. Only world
        // shapes are ground, so the mech casting the ray never hits itself.
        public long GetTerrainHeight(FixVec p_at)
        {
            int rules = GroundRules;
            if (rules == RulesGhostBear) return GetTerrainHeightGbl(p_at);
            if (rules == RulesMercenaries) return GetTerrainHeightMercs(p_at);
            long start = p_at.Y + RayLift, best = 0;
            GroundNormal = Up;
            foreach (GroundShape shape in shapes)
            {
                if (shape.Kind == 0)
                {
                    UpdateBox(shape);
                    if (p_at.X < shape.BoxMin.X || p_at.X > shape.BoxMax.X || p_at.Z < shape.BoxMin.Z || p_at.Z > shape.BoxMax.Z)
                        continue;
                    long top = shape.BoxMax.Y;
                    if (top > start || top <= best) continue;
                    best = top;
                    GroundNormal = Up;
                }
                else RayQuadtree(shape, shape.Root, p_at.X, p_at.Z, start, ref best);
            }

            return best;
        }

        // Mercenaries (MERCSW.DLL 0x100142b4: the highest top within 1000 of p_y, else the highest below, else 0) as
        // the engine's ray from 1000 above, never below the ground plane at 0: the nearest upward face of any ground
        // shape under the point (a box shape's own triangles, not its box). Integer math, so every machine agrees.
        long GetTerrainHeightMercs(FixVec p_at)
        {
            // Floored at the ground plane (0): a sunken mesh never takes the mech below it.
            long start = p_at.Y + MercsRayLift, best = 0;
            // Mercenaries' positions are whole units; a fraction could fall between a node's halves (mid, mid + 1).
            long x = FloorUnit(p_at.X), z = FloorUnit(p_at.Z);
            GroundNormal = Up;
            foreach (GroundShape shape in shapes)
            {
                if (shape.Kind == 0) UpdateBoxFaces(shape);
                RayQuadtree(shape, shape.Root, x, z, start, ref best);
            }

            return best;
        }

        // Mercenaries: a box shape's triangles where it stands (its meshes' nodes), in a quadtree, again once it moved.
        void UpdateBoxFaces(GroundShape p_shape)
        {
            UpdateBox(p_shape);
            if (p_shape.Root != null && p_shape.Triangles != null && p_shape.Pose.Equals(p_shape.FacesPose)) return;
            p_shape.FacesPose = p_shape.Pose;
            var vertices = new List<FixVec>();
            var triangles = new List<int>();
            for (int f = 0; f < p_shape.Nodes.Length; f++)
            {
                WorldPose pose = GetNodePose(p_shape.Nodes[f]);
                int first = vertices.Count;
                foreach (FixVec vertex in p_shape.Local[f]) vertices.Add(pose.TransformPoint(vertex));
                int[] indices = p_shape.LocalTriangles[f];
                bool mirrored = p_shape.LocalMirrored[f];
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    triangles.Add(first + indices[i]);
                    triangles.Add(first + indices[mirrored ? i + 2 : i + 1]);
                    triangles.Add(first + indices[mirrored ? i + 1 : i + 2]);
                }
            }

            BuildQuadtreeFrom(p_shape, vertices, triangles);
        }

        // g_hitNormal: the last hit surface's normal, kept across queries (a quadtree miss doesn't set it).
        FixVec hitNormal;

        // Ghost Bear's Legacy (MW2.flat 0x1c370, as demech2 collision.c GetTerrainHeight): each shape's top under the
        // point; the first top below p_y (while nothing is within 1000 of it) and the highest top within 1000 of p_y.
        long GetTerrainHeightGbl(FixVec p_at)
        {
            const long none = long.MinValue, window = 1000L << 16;
            long y = p_at.Y, below = none, best = none;
            GroundNormal = FixVec.Zero;
            foreach (GroundShape shape in shapes)
            {
                long top;
                if (shape.Kind == 0)
                {
                    // GetBoxTop
                    UpdateBox(shape);
                    if (p_at.X < shape.BoxMin.X || p_at.X > shape.BoxMax.X || p_at.Z < shape.BoxMin.Z || p_at.Z > shape.BoxMax.Z)
                        continue;
                    top = shape.BoxMax.Y;
                    hitNormal = Up;
                }
                else if (!GetQuadtreeTop(shape, shape.Root, p_at.X, p_at.Z, out top)) continue;

                if (best == none && top < y && below < top)
                {
                    below = top;
                    GroundNormal = hitNormal;
                }

                if (top + window > y && top - window < y && best < top)
                {
                    best = top;
                    GroundNormal = hitNormal;
                }
            }

            if (best != none) return best;
            return below != none ? below : 0;
        }

        // IsBelowFacePlane's height: the plane's height at (x, z) over face p_face (its first vertex and normal).
        static long PlaneHeight(GroundShape p_shape, int p_face, FixVec p_normal, long p_x, long p_z)
        {
            FixVec v = p_shape.Vertices[p_shape.Triangles[p_face * 3]];
            return v.Y - Fixed.Div16L(Fixed.Mul16L(p_x - v.X, p_normal.X) + Fixed.Mul16L(p_z - v.Z, p_normal.Z), p_normal.Y);
        }

        // GetQuadtreeTop (0x1d900): the first child holding the point, or the leaf's first upward face over it; a node
        // holding the point with neither hits at 0 (hitNormal unchanged).
        bool GetQuadtreeTop(GroundShape p_shape, Node p_node, long p_x, long p_z, out long p_top)
        {
            p_top = 0;
            if (p_node == null) return false;
            if (p_node.MinX > p_x || p_node.MaxX < p_x || p_node.MinZ > p_z || p_node.MaxZ < p_z) return false;
            if (p_node.Faces == null)
            {
                for (int j = 0; j < 4; j++)
                    if (GetQuadtreeTop(p_shape, p_node.Children[j], p_x, p_z, out p_top)) return true;
                p_top = 0;
                return true;
            }

            foreach (int polygon in p_node.Faces)
            for (int f = p_shape.PolygonFirst[polygon]; f < p_shape.PolygonFirst[polygon + 1]; f++)
            {
                FixVec n = p_shape.Normals[f];
                if (n.Y <= 0 || !IsPointInFaceXZ(p_shape, f, p_x, p_z)) continue;
                p_top = PlaneHeight(p_shape, f, n, p_x, p_z);
                hitNormal = n;
                return true;
            }

            return true;
        }

        // The highest upward face under the point at or below p_start in the leaves holding it, above p_best.
        void RayQuadtree(GroundShape p_shape, Node p_node, long p_x, long p_z, long p_start, ref long p_best)
        {
            if (p_node == null) return;
            if (p_node.MinX > p_x || p_node.MaxX < p_x || p_node.MinZ > p_z || p_node.MaxZ < p_z) return;
            if (p_node.MaxY <= p_best || p_node.MinY > p_start) return;
            if (p_node.Faces == null)
            {
                for (int j = 0; j < 4; j++) RayQuadtree(p_shape, p_node.Children[j], p_x, p_z, p_start, ref p_best);
                return;
            }

            foreach (int polygon in p_node.Faces)
            for (int f = p_shape.PolygonFirst[polygon]; f < p_shape.PolygonFirst[polygon + 1]; f++)
            {
                FixVec n = p_shape.Normals[f];
                if (n.Y <= 0 || !IsPointInFaceXZ(p_shape, f, p_x, p_z)) continue;
                long top = PlaneHeight(p_shape, f, n, p_x, p_z);
                if (top > p_start || top <= p_best) continue;
                p_best = top;
                GroundNormal = n;
            }
        }

        // IsPointInFaceXZ, for a triangle: inside or on an edge, seen from above.
        static bool IsPointInFaceXZ(GroundShape p_shape, int p_face, long p_x, long p_z)
        {
            FixVec a = p_shape.Vertices[p_shape.Triangles[p_face * 3]];
            FixVec b = p_shape.Vertices[p_shape.Triangles[p_face * 3 + 1]];
            FixVec c = p_shape.Vertices[p_shape.Triangles[p_face * 3 + 2]];
            long d1 = Edge(a, b, p_x, p_z), d2 = Edge(b, c, p_x, p_z), d3 = Edge(c, a, p_x, p_z);
            bool negative = d1 < 0 || d2 < 0 || d3 < 0, positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive);
        }

        static long Edge(FixVec p_a, FixVec p_b, long p_x, long p_z) =>
            Fixed.Mul16L(p_b.X - p_a.X, p_z - p_a.Z) - Fixed.Mul16L(p_b.Z - p_a.Z, p_x - p_a.X);
    }
}
