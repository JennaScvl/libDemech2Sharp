using System;
using UnityEngine;

namespace Demech2
{
    // Wreckage (debris.c), in the lockstep's fixed point (Fixed): up to 0x80 pieces thrown off under
    // gravity, spinning and bouncing on the terrain until they come to rest (UpdateDebrisPiece, over the pieces in slot
    // order, drawing from the sim's random table as the original does), and up to 0x80 chunks: objects blown off a model,
    // flying as pieces until shot apart (DamageChunk; their blast a sim effect) or timed out. Pieces and chunks share the
    // piece slots, chunks collide with shots and mechs, and their blasts damage: all of it is the sim's, the same on every
    // lockstep machine (SimState walks it). Each piece keeps its own place (16.16 units, its object's origin) and turn
    // (2.30 rows) and shows them on its object (a node's GameObject part, or an effect's entity). Units are world units
    // (centimetres) and ticks; angles degrees (16.16).
    public static class Debris
    {
        const int Count = 0x80;
        const int ChunkLifetime = 0xe24; // ticks

        // DebrisPiece (SIZE 0x24): the original's floats as 16.16 (velocity units a tick, spin degrees a tick, its
        // acceleration units a tick²); its place, turn and shape's center (object space) are not the original's fields.
        public struct DebrisPiece
        {
            public int m_unk0x00;
            public ISceneObject m_obj;
            public FixVec m_velocity;
            public FixVec m_spin;
            public long m_acceleration;
            public FixVec m_position;
            public long[] m_rotation;
            public FixVec m_center;
            public long m_radius;
        }

        // DebrisChunk (SIZE 0x14); its health 16.16.
        public struct DebrisChunk
        {
            public bool m_active;
            public ISceneObject m_obj;
            public int m_startTime;
            public long m_health;
            public Action<ISceneObject> m_callback;
        }

        public static int g_debrisCount;
        static readonly DebrisPiece[] g_debrisPieces = new DebrisPiece[Count];
        static readonly DebrisChunk[] g_debrisChunks = new DebrisChunk[Count];

        // g_gravity (units per tick², 0.0296 at 1 g: 0x794 16.16) and g_gravityScale (16.16), from the world's planet
        // record. The shots' g_gravity is the same.
        public static int g_gravity = 0x794;
        public static long g_gravityScale = 0x10000;

        // g_localMechLost: the local mech is gone (chunks then stay, outside a networked match).
        public static bool g_localMechLost;

        // SetGravity: the planet's gravity in g (16.16).
        public static void SetGravity(int p_g)
        {
            g_gravityScale = p_g;
            g_gravity = (int)((long)p_g * 0x794 >> 16);
        }

        public static bool IsDebrisFull()
        {
            int i;
            for (i = 0; i < Count && g_debrisPieces[i].m_obj != null; i++)
            {
            }

            return i == Count;
        }

        // The object of the piece or chunk p_transform flies as (a shape's SceneObject), or null.
        public static ISceneObject FindPieceObject(Transform p_transform)
        {
            if (p_transform == null) return null;
            for (int i = 0; i < Count; i++)
            {
                if (g_debrisPieces[i].m_obj != null && g_debrisPieces[i].m_obj.Transform == p_transform) return g_debrisPieces[i].m_obj;
                if (g_debrisChunks[i].m_active && g_debrisChunks[i].m_obj != null && g_debrisChunks[i].m_obj.Transform == p_transform) return g_debrisChunks[i].m_obj;
            }

            return null;
        }

        // AddDebrisPiece: the object takes a piece slot (its own, or the first free), off its parent. Not the original's:
        // its place and turn are taken from its node (an entity's from p_at, unturned).
        public static int AddDebrisPiece(ISceneObject p_obj, int p_unk0x00, FixVec? p_at = null)
        {
            if (p_obj == null) return -1;

            int i;
            for (i = 0; i < Count && g_debrisPieces[i].m_obj != p_obj; i++)
            {
            }

            bool own = i < Count;
            if (i == Count)
            {
                for (i = 0; i < Count && g_debrisPieces[i].m_obj != null; i++)
                {
                }
            }

            if (i == Count) return -1;

            FixVec position = own ? g_debrisPieces[i].m_position : default;
            long[] rotation = own ? g_debrisPieces[i].m_rotation : null;
            ResetDebrisPiece(i);
            g_debrisCount++;
            p_obj.Detach();

            ref DebrisPiece piece = ref g_debrisPieces[i];
            piece.m_unk0x00 = p_unk0x00;
            piece.m_obj = p_obj;
            piece.m_acceleration = -g_gravity;
            piece.m_center = FixVec.FromFloat(p_obj.Center);
            piece.m_radius = Fixed.FromFloatL(p_obj.Radius);
            if (own && rotation != null)
            {
                piece.m_position = position;
                piece.m_rotation = rotation;
            }
            else if (p_obj.GetNodePose(out FixVec nodePosition, out long[] nodeRotation))
            {
                piece.m_position = nodePosition;
                piece.m_rotation = nodeRotation;
            }
            else
            {
                if (p_at == null && p_obj.Transform != null) Debug.LogWarning($"[Debris] {p_obj.Transform.name}: no node; its place read from its Transform");
                piece.m_position = p_at ?? FixVec.FromFloat(p_obj.Position);
                piece.m_rotation = SimRotation.Identity;
            }

            Show(ref piece);
            return i;
        }

        // Throws the piece off in a random direction, spinning: velocity normal / (g_gravityScale * 1024) * 5.5, the
        // vertical a deviation up; spin normal * 0.5 / 1024.
        public static void ThrowDebrisPiece(int p_index)
        {
            if (p_index < 0) return;
            ref DebrisPiece piece = ref g_debrisPieces[p_index];
            if (piece.m_obj == null) return;

            long scale = g_gravityScale != 0 ? g_gravityScale : 0x10000;
            piece.m_velocity.X = ThrowSpeed(SimRandom.RandomNormal(), scale);
            piece.m_velocity.Z = ThrowSpeed(SimRandom.RandomNormal(), scale);
            piece.m_velocity.Y = ThrowSpeed(SimRandom.RandomNormal() + 0x400, scale);
            piece.m_spin.X = SimRandom.RandomNormal() * 32L;
            piece.m_spin.Y = SimRandom.RandomNormal() * 32L;
            piece.m_spin.Z = SimRandom.RandomNormal() * 32L;
        }

        // p_normal / (scale * 1024) * 5.5 units a tick, 16.16: p_normal * 352 / scale (5.5 * 65536 / 1024 = 352).
        static long ThrowSpeed(long p_normal, long p_scale) => p_normal * 352 * 0x10000 / p_scale;

        // Blows p_obj off its model as a chunk of debris; p_callback gets it when it's gone. p_destroyKind: the shape's
        // kind is 0x70, or p_unk0x16 is set and it's the model's part 0 (SetShapeKind(0x50) and straight to disposal).
        public static void BlowOffChunk(ISceneObject p_obj, Action<ISceneObject> p_callback, bool p_destroyKind)
        {
            if (p_obj == null) return;

            int i;
            for (i = 0; i < Count && g_debrisChunks[i].m_active; i++)
            {
            }

            if (i == Count || p_destroyKind)
            {
                p_obj.Detach();
                DisposeDebris(p_obj, p_callback);
                return;
            }

            int piece = AddDebrisPiece(p_obj, 2);
            if (piece >= 0)
            {
                ref DebrisChunk chunk = ref g_debrisChunks[i];
                chunk.m_active = true;
                chunk.m_obj = p_obj;
                chunk.m_callback = p_callback;
                chunk.m_startTime = Clock.g_currentClock;
                chunk.m_health = 16L << 16;
                // ClearObjTreeKind(0x300), SetObjTreeKind(0x50), SetObjTreeOwner(i): shots hit it as a chunk.
                Shape shape = p_obj.Shape;
                if (shape != null)
                {
                    shape.Kind = 0x50;
                    shape.Owner = i;
                }

                ThrowDebrisPiece(piece);
            }
            else
            {
                DisposeDebris(p_obj, p_callback);
            }
        }

        public static void DisposeDebris(ISceneObject p_obj, Action<ISceneObject> p_callback)
        {
            if (p_obj == null) return;
            HideDebrisObj(p_obj);
            p_callback?.Invoke(p_obj);
            int index = FindDebrisPiece(p_obj);
            if (index != -1) ResetDebrisPiece(index);
        }

        // The shape's center (16.16 units, the world's): its piece's place and turn, else its object's.
        static FixVec CenterOf(ISceneObject p_obj, out long p_radius)
        {
            int index = FindDebrisPiece(p_obj);
            if (index >= 0)
            {
                ref DebrisPiece piece = ref g_debrisPieces[index];
                p_radius = piece.m_radius;
                return piece.m_position + Rotate(piece.m_rotation, piece.m_center);
            }

            p_radius = Fixed.FromFloatL(p_obj.GetBounds(out Vector3 center));
            return FixVec.FromFloat(center);
        }

        // Blows the chunk up: its blast a sim effect (7) at its center.
        public static void ExplodeChunk(int p_index)
        {
            ref DebrisChunk chunk = ref g_debrisChunks[p_index];
            ISceneObject obj = chunk.m_obj;
            if (obj == null) return;

            FixVec center = CenterOf(obj, out _);
            Effects.SpawnExplosion(-2, 7, center, center);
            HideDebrisObj(obj);
            chunk.m_callback?.Invoke(obj);
            int piece = FindDebrisPiece(obj);
            if (piece != -1) ResetDebrisPiece(piece);
            chunk = default;
        }

        // p_damage: 16.16 points.
        public static void DamageChunk(int p_index, long p_damage)
        {
            if (p_index < 0 || p_index >= Count || !g_debrisChunks[p_index].m_active) return;
            g_debrisChunks[p_index].m_health -= p_damage;
            if (g_debrisChunks[p_index].m_health < 0) ExplodeChunk(p_index);
        }

        // Damages the chunks within p_radius (16.16 units) of p_center by p_damage (16.16 points) per tick.
        public static void DamageChunksInRadius(FixVec p_center, long p_radius, long p_damage)
        {
            int i = Count;
            while (i-- > 0)
            {
                DebrisChunk chunk = g_debrisChunks[i];
                if (!chunk.m_active || chunk.m_obj == null) continue;
                FixVec center = CenterOf(chunk.m_obj, out long radius);
                if (InRadius.IsWithinRadius(center - p_center, radius + p_radius)) DamageChunk(i, p_damage * Clock.g_deltaTime);
            }
        }

        public static void RemoveChunk(ISceneObject p_obj, Action<ISceneObject> p_callback)
        {
            if (p_obj == null) return;
            for (int i = 0; i < Count; i++)
            {
                if (g_debrisChunks[i].m_obj == p_obj)
                {
                    g_debrisChunks[i] = default;
                    break;
                }
            }

            DisposeDebris(p_obj, p_callback);
        }

        // HideObjTree, DisableObjTreeCollision, UpdateObj.
        public static void HideDebrisObj(ISceneObject p_obj)
        {
            if (p_obj == null) return;
            p_obj.Show(false);
            Shape shape = p_obj.Shape;
            if (shape != null) ShapeCollision.DisableShapeCollision(shape);
        }

        public static void ZeroChunx()
        {
            for (int i = 0; i < Count; i++)
            {
                ISceneObject obj = g_debrisChunks[i].m_obj;
                if (obj != null) g_debrisChunks[i].m_callback?.Invoke(obj);
                g_debrisChunks[i] = default;
            }

            for (int i = 0; i < Count; i++) ResetDebrisPiece(i);
            g_debrisCount = 0;
        }

        public static void ResetDebrisPiece(int p_index) => g_debrisPieces[p_index] = default;

        public static int FindDebrisPiece(ISceneObject p_obj)
        {
            for (int i = 0; i < Count; i++)
            {
                if (g_debrisPieces[i].m_obj == p_obj) return i;
            }

            return -1;
        }

        // Pushes the piece by p_push (16.16 units a tick) on top of a random throw: the throw cut to twice the push's
        // share of it when the push is the slower.
        public static void PushDebrisPiece(int p_index, FixVec p_push)
        {
            ref DebrisPiece piece = ref g_debrisPieces[p_index];
            if (piece.m_obj == null) return;
            long length = p_push.ApproxLength;
            if (length == 0) return;

            ThrowDebrisPiece(p_index);
            long speed = piece.m_velocity.ApproxLength;
            if (length < speed)
            {
                long scale = Fixed.Div16L(length * 2, speed);
                piece.m_velocity = piece.m_velocity.Mul(scale);
            }

            piece.m_velocity += p_push;
        }

        // ---- UpdateDebris ------------------------------------------------------------------------------------------------

        public static void UpdateDebris()
        {
            for (int i = 0; i < Count; i++)
                if (g_debrisPieces[i].m_obj != null) UpdateDebrisPiece(i);

            for (int i = 0; i < Count; i++)
            {
                // g_localMechLost keeps the chunks; in a networked match whose it is differs by machine, so not there.
                if (g_debrisChunks[i].m_active && Clock.g_currentClock - g_debrisChunks[i].m_startTime > ChunkLifetime && !(g_localMechLost && !Host.Net.NetRules))
                {
                    DisposeDebris(g_debrisChunks[i].m_obj, g_debrisChunks[i].m_callback);
                    g_debrisChunks[i] = default;
                }
            }
        }

        // UpdateDebrisPiece: the midpoint integration of its fall (IntegrateMidpoint) from its shape's center less half its
        // radius, against the ground under the center (GetTerrainHeight); the bounce (a quarter back up, or an eighth and
        // half the sideways speed reversed on a coin toss), the spin halved and reversed, and rest once it lands slower than
        // 0.55 units a tick (hidden if it landed on its way up).
        static void UpdateDebrisPiece(int p_index)
        {
            ref DebrisPiece piece = ref g_debrisPieces[p_index];
            long dt = Clock.g_deltaTime;
            FixVec center = piece.m_position + Rotate(piece.m_rotation, piece.m_center);
            long y = center.Y - piece.m_radius / 2;
            long ground = SimMain.g_terrain != null ? SimMain.g_terrain.GetTerrainHeight(new FixVec(center.X, y, center.Z)) : 0;

            bool landed = false, hide = false;
            long newY = y, velocityY = piece.m_velocity.Y;
            bool rising = velocityY > 0;
            long midpointStep = dt * piece.m_acceleration;
            newY += (midpointStep / 2 + velocityY) * dt;
            velocityY += midpointStep;

            if (velocityY <= 0 && newY <= ground)
            {
                if (rising)
                {
                    hide = true;
                    landed = true;
                }

                // 0.5524862 units a tick.
                if (velocityY > -36207) landed = true;

                newY = ground;
                velocityY = -(velocityY / 4);
                if (SimRandom.RandomIntBelow(2) != 0)
                {
                    velocityY /= 2;
                    piece.m_velocity.X = -(piece.m_velocity.X / 2);
                    piece.m_velocity.Z = -(piece.m_velocity.Z / 2);
                }

                piece.m_spin = new FixVec(-(piece.m_spin.X / 2), -(piece.m_spin.Y / 2), -(piece.m_spin.Z / 2));
            }

            piece.m_velocity.Y = velocityY;
            piece.m_position += new FixVec(piece.m_velocity.X * dt, newY - y, piece.m_velocity.Z * dt);
            piece.m_rotation = SimRotation.Multiply(piece.m_rotation, SimRotation.Euler(piece.m_spin.X * dt, piece.m_spin.Y * dt, piece.m_spin.Z * dt));
            if (hide) HideDebrisObj(piece.m_obj);
            Show(ref piece);
            if (landed)
            {
                g_debrisCount--;
                ResetDebrisPiece(p_index);
            }
        }

        // A 2.30 rotation's rows (taken as 2.16) onto a 16.16 vector (a shape's center: small).
        static FixVec Rotate(long[] p_rows, FixVec p_v)
        {
            if (p_rows == null) return p_v;
            long R(int i) => p_rows[i] >> 14;
            return new FixVec(
                (R(0) * p_v.X + R(1) * p_v.Y + R(2) * p_v.Z) >> 16,
                (R(3) * p_v.X + R(4) * p_v.Y + R(5) * p_v.Z) >> 16,
                (R(6) * p_v.X + R(7) * p_v.Y + R(8) * p_v.Z) >> 16);
        }

        // The piece's place and turn onto its object (its node: the sim's collision follows; an entity: only shown).
        static void Show(ref DebrisPiece p_piece)
        {
            ISceneObject obj = p_piece.m_obj;
            if (obj == null || p_piece.m_rotation == null) return;
            Quaternion turn = SimRotation.ToQuaternion(p_piece.m_rotation);
            if (obj.SetNodePose(p_piece.m_position, p_piece.m_rotation, turn)) return;

            obj.SetRotation(turn);
            obj.SetPosition(p_piece.m_position.ToFloat());
        }
    }
}
