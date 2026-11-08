using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // A mech's collisions as it moves (demech2 MW2/src/mechcollision.c: MoveMechWithCollisions, CollideWithMechs,
    // CollideWithBuildings, DamageMechsInCollision, ApplyCollisionDamage, DamageMechHittingShape), in 1.1's 16.16 fixed
    // point: positions in units, velocities 16.16 units a tick, the collision normal (g_segmentNormal) 16.16. A running
    // or destroyed mech stops at the first mech it overlaps (their radii), the first building (a shape of collision type 6:
    // its sphere) or the scenery its move's ray (lengthened by its radius or height) hits, bouncing off it; a debris chunk
    // in its way is only pushed aside. Collisions above 200000 damage the mech (by the side the normal faces) and what it
    // hit.
    public sealed partial class Mech
    {
        // g_segmentNormal, as the collisions leave it (16.16).
        public static Vector3Int g_segmentNormal;

        // MoveMechWithCollisions: the mech moved by (p_dx, p_dy, p_dz) from its position into (p_x, p_y, p_z), stopped
        // at what it hits: p_hit the shape (null for a mech), p_hitPlayer the mech. Returns how many ticks it has been
        // colliding, 0 moving freely.
        public int MoveMechWithCollisions(ref Shape p_hit, ref Mech p_hitPlayer, int p_dx, int p_dy, int p_dz,
            ref int p_x, ref int p_y, ref int p_z)
        {
            int x0 = (int)m_positionX, y0 = (int)m_positionY, z0 = (int)m_positionZ;
            int x1 = p_dx + x0, y1 = p_dy + y0, z1 = p_dz + z0;
            p_x = x1;
            p_y = y1;
            p_z = z1;
            bool hit = false;
            // ShutdownStopsJets: a mech shut down or powering up collides too.
            bool shutDown = PowerState == 0 || PowerState == 1 || PowerState == 3;
            if (PowerState != 2 && PowerState != 4 && !(ShutdownStopsJets && shutDown))
            {
                m_collisionTicks = 0;
                return 0;
            }

            if (CollideWithMechs(ref p_x, ref p_y, ref p_z, ref p_hitPlayer))
            {
                m_collidedWith = p_hitPlayer.m_player.m_index;
                hit = true;
                m_collisionTicks++;
                p_hit = null;
            }
            else if (CollideWithBuildings(ref p_x, ref p_y, ref p_z, out Shape building))
            {
                hit = true;
                m_collisionTicks++;
                p_hit = building;
            }
            else
            {
                if (p_dx == 0 && p_dy == 0 && p_dz == 0)
                {
                    if (m_collisionTicks != 0)
                    {
                        m_collisionTicks++;
                        return m_collisionTicks;
                    }

                    return 0;
                }

                // The move's ray (BuildRayFromSegment, built fixed), lengthened by the radius flat and the height
                // straight up or down.
                CollisionRay ray = CollisionRay.BuildRayFromSegment(FixVec.FromUnits(x0, y0, z0), FixVec.FromUnits(x1, y1, z1));
                long length = ray.GetRayLength();
                int lift = (int)ray.m_dirY;
                if (lift < 0) lift = -lift;
                lift = Fixed.Mul16(m_radius, 0x10000 - lift) + Fixed.Mul16(m_height, lift);
                ray.SetRayLength(length + ((long)lift << 16));
                if (ShapeCollision.TestSceneryCollision(ref ray, out Shape shape))
                {
                    hit = true;
                    m_collisionTicks++;
                    p_hit = shape;
                    g_segmentNormal = ToFixed(ShapeCollision.g_segmentNormal);
                    if (g_segmentNormal == Vector3Int.zero && shape != null)
                    {
                        CollisionRay normal = CollisionRay.BuildRayFromSegment(shape.FxCenter, ray.End);
                        normal.BuildRayFixed();
                        g_segmentNormal = ToFixed(normal.Direction);
                    }

                    int dirX = (int)ray.m_dirX, dirY = (int)ray.m_dirY, dirZ = (int)ray.m_dirZ;
                    int dot = Fixed.Mul16(g_segmentNormal.x * 2, dirX) + Fixed.Mul16(g_segmentNormal.y * 2, dirY) +
                              Fixed.Mul16(g_segmentNormal.z * 2, dirZ);
                    int nx = dirX - Fixed.Mul16(dot, g_segmentNormal.x);
                    int ny = dirY - Fixed.Mul16(dot, g_segmentNormal.y);
                    int nz = dirZ - Fixed.Mul16(dot, g_segmentNormal.z);
                    int bounce = -ApproxLen.ApproximateVectorLength(m_newVelocityX, m_newVelocityY, m_newVelocityZ) >> 2;
                    m_velocityX = Fixed.Mul16(nx, bounce);
                    m_velocityY = Fixed.Mul16(ny, bounce);
                    m_velocityZ = Fixed.Mul16(nz, bounce);
                    ray.SetRayLength(ray.GetRayLength() - ((long)lift << 16));
                    int over = (int)((length - ray.GetRayLength()) / 65536);
                    if (over > 0)
                    {
                        nx = Fixed.Mul16(nx, over) >> 2;
                        ny = Fixed.Mul16(ny, over) >> 2;
                        nz = Fixed.Mul16(nz, over) >> 2;
                        p_x = (int)(ray.m_x1 / 65536) + nx;
                        p_y = (int)(ray.m_y1 / 65536) + ny;
                        p_z = (int)(ray.m_z1 / 65536) + nz;
                    }
                    else
                    {
                        p_x = (int)(ray.m_x1 / 65536);
                        p_y = (int)(ray.m_y1 / 65536);
                        p_z = (int)(ray.m_z1 / 65536);
                    }
                }
            }

            if (!hit)
            {
                m_collisionTicks = 0;
                p_hit = null;
            }

            return m_collisionTicks;
        }

        public static Vector3Int ToFixed(FixVec p_v) => new Vector3Int((int)p_v.X, (int)p_v.Y, (int)p_v.Z);

        // CollideWithMechs: the point pushed out of the first other mech it overlaps (one that ran into this one first, of
        // a lower index, always counts), with a velocity away from it; a wreck there is knocked over instead.
        public bool CollideWithMechs(ref int p_x, ref int p_y, ref int p_z, ref Mech p_hit)
        {
            int id = m_player.m_index;
            bool result = false;
            for (int i = 0; i < SimMain.PlayerCount; i++)
            {
                Mech mech = SimMain.MechOf(i);
                if (mech == null || mech.m_player.m_index == id) continue;
                if ((mech.m_flags & 0x100) != 0) continue;
                if ((mech.PlayerFlags & 0x4000) != 0) continue;

                bool first = mech.m_collidedWith == id && i < id;
                int dx = p_x - (int)mech.m_positionX, dy = p_y - (int)mech.m_positionY, dz = p_z - (int)mech.m_positionZ;
                int reach = mech.m_radius + m_radius;
                int dist = ApproxLen.ApproximateVectorLength(dx, dy, dz);
                if (!first && dist >= reach) continue;
                // A respawn's overlap: SeparateFromMechs slides them apart instead.
                if (m_separating || mech.m_separating) continue;

                if (mech.PowerState == 4) mech.KnockMechOver();
                else
                {
                    result = true;
                    p_hit = mech;
                    if (dist == 0) g_segmentNormal = new Vector3Int(0, 0, 0x10000);
                    else g_segmentNormal = new Vector3Int(Fixed.Div16(dx, dist), Fixed.Div16(dy, dist), Fixed.Div16(dz, dist));
                    p_x = (int)mech.m_positionX + Fixed.Mul16(g_segmentNormal.x, reach);
                    p_y = (int)mech.m_positionY + Fixed.Mul16(g_segmentNormal.y, reach);
                    p_z = (int)mech.m_positionZ + Fixed.Mul16(g_segmentNormal.z, reach);
                    m_newVelocityZ = mech.m_newVelocityZ;
                    int speedOff = ApproxLen.ApproximateVectorLength(m_newVelocityX - mech.m_newVelocityX, m_newVelocityY - mech.m_newVelocityY, m_newVelocityZ);
                    if (speedOff > 0x20000) speedOff >>= 1;
                    else speedOff = 0x10000;
                    m_velocityX = Fixed.Mul16(g_segmentNormal.x, speedOff);
                    m_velocityY = Fixed.Mul16(g_segmentNormal.y, speedOff);
                    m_velocityZ = Fixed.Mul16(g_segmentNormal.z, speedOff);
                }

                break;
            }

            return result;
        }

        // CollideWithBuildings: the point pushed out of the first shape of collision type 6 (not a mech's part) whose
        // sphere it's in, bouncing away; a debris chunk (kind 0x50) is pushed by the mech's velocity and heard instead,
        // and ends the search.
        public bool CollideWithBuildings(ref int p_x, ref int p_y, ref int p_z, out Shape p_hit)
        {
            p_hit = null;
            IReadOnlyList<Shape> colliders = ShapeCollision.Colliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                Shape shape = colliders[i];
                if (shape.CollisionType != 6 || (shape.Kind & 0x100) != 0) continue;
                FixVec center = shape.FxCenter;
                int cx = (int)(center.X / 65536), cy = (int)(center.Y / 65536), cz = (int)(center.Z / 65536);
                int dx = p_x - cx, dy = p_y - cy, dz = p_z - cz;
                int reach = m_radius + (int)(shape.FxRadius / 65536);
                int dist = ApproxLen.ApproximateVectorLength(dx, dy, dz);
                if (dist >= reach) continue;

                if ((shape.Kind & 0xf0) == 0x50)
                {
                    // Its object pushed as debris at twice the mech's velocity, when it moves (AddDebrisPiece,
                    // PushDebrisPiece), and heard there.
                    ISceneObject obj = Host.Fx.ChunkObject(shape);
                    if (ApproxLen.ApproximateVectorLength(m_velocityX, m_velocityY, m_velocityZ) > 0)
                    {
                        int index = Debris.AddDebrisPiece(obj, 1);
                        if (index >= 0)
                        {
                            Debris.PushDebrisPiece(index, new FixVec(m_velocityX * 2L, m_velocityY * 2L, m_velocityZ * 2L));
                            EffectSound.PlaySoundAt(Host.Audio.EyeUnits - obj.Position, Host.Anim.MapSoundId(0xb5), Eyepoint.g_inCockpitView);
                        }
                    }

                    return false;
                }

                if (dist == 0) g_segmentNormal = new Vector3Int(0, 0, 0x10000);
                else g_segmentNormal = new Vector3Int(Fixed.Div16(dx, dist), Fixed.Div16(dy, dist), Fixed.Div16(dz, dist));
                p_x = cx + Fixed.Mul16(g_segmentNormal.x, reach);
                p_y = cy + Fixed.Mul16(g_segmentNormal.y, reach);
                p_z = cz + Fixed.Mul16(g_segmentNormal.z, reach);
                int bounce = -ApproxLen.ApproximateVectorLength(m_newVelocityX, m_newVelocityY, m_newVelocityZ) >> 2;
                if (bounce > 8) bounce = -bounce >> 1;
                else bounce = -4;
                m_velocityX = Fixed.Mul16(g_segmentNormal.x, bounce);
                m_velocityY = Fixed.Mul16(g_segmentNormal.y, bounce);
                m_velocityZ = Fixed.Mul16(g_segmentNormal.z, bounce);
                p_hit = shape;
                return true;
            }

            return false;
        }

        // Respawn separation (a networked match's respawn): a respawned mech overlapping
        // another isn't snapped out by CollideWithMechs; both slide apart flat, a mech's reach a second, until it's clear.
        // Collision damage between it and other mechs is off for RespawnGraceTicks.
        public const int RespawnGraceTicks = 2 * Clock.TicksPerSecond;
        public bool m_separating;
        public int m_collisionGraceUntil;

        public void StartRespawnSeparation()
        {
            m_separating = true;
            m_collisionGraceUntil = Clock.g_currentClock + RespawnGraceTicks;
        }

        // The point (p_x, p_z) moved away from every other mech it overlaps while either is separating; the mech's own
        // separation ends once it overlaps none.
        public void SeparateFromMechs(int p_dt, ref int p_x, int p_y, ref int p_z)
        {
            int id = m_player.m_index;
            bool overlapping = false;
            for (int i = 0; i < SimMain.PlayerCount; i++)
            {
                Mech mech = SimMain.MechOf(i);
                if (mech == null || mech.m_player.m_index == id) continue;
                if (!m_separating && !mech.m_separating) continue;
                if ((mech.m_flags & 0x100) != 0 || (mech.PlayerFlags & 0x4000) != 0 || mech.PowerState == 4) continue;

                int dx = p_x - (int)mech.m_positionX, dz = p_z - (int)mech.m_positionZ;
                int reach = mech.m_radius + m_radius;
                if (ApproxLen.ApproximateVectorLength(dx, p_y - (int)mech.m_positionY, dz) >= reach) continue;

                overlapping = true;
                int step = Fixed.MulDiv64(reach, p_dt, Clock.TicksPerSecond);
                if (step < 1) step = 1;
                int flat = ApproxLen.ApproximateVectorLength(dx, 0, dz);
                if (flat == 0) p_z += id > mech.m_player.m_index ? step : -step;
                else
                {
                    p_x += Fixed.MulDiv64(dx, step, flat);
                    p_z += Fixed.MulDiv64(dz, step, flat);
                }
            }

            if (m_separating && !overlapping) m_separating = false;
        }

        // DamageMechsInCollision: with collision damage on, a meeting faster than 200000 damages this mech (by the other).
        public void DamageMechsInCollision(Mech p_other)
        {
            if (!SimMain.g_difficulty.m_collisionDamage) return;
            if (Clock.g_currentClock < m_collisionGraceUntil || Clock.g_currentClock < p_other.m_collisionGraceUntil) return;
            int speedOff = ApproxLen.ApproximateVectorLength(m_newVelocityX - p_other.m_newVelocityX, m_newVelocityY - p_other.m_newVelocityY, m_newVelocityZ - p_other.m_newVelocityZ);
            if (speedOff > 200000) ApplyCollisionDamage(p_other, Fixed.Div16(speedOff - 200000, 1300000) * 3);
        }

        // ApplyCollisionDamage: from below (the normal points down) the legs, four times scaled by the other mech's mass
        // over this one's (at least p_damage); from above both side torsos at half; else the section facing the normal.
        // p_other is null for a building or the ground (attacker -2).
        public void ApplyCollisionDamage(Mech p_other, int p_damage)
        {
            int attacker = p_other != null ? p_other.m_player.m_index : -2;
            if (g_segmentNormal.y < -0xddb4)
            {
                int damage;
                if (p_other == null) damage = p_damage;
                else
                {
                    int tons = m_config != null ? m_config.m_tons : 0;
                    int otherTons = p_other.m_config != null ? p_other.m_config.m_tons : 0;
                    damage = Fixed.MulDiv64((long)p_damage << 2, otherTons, tons > 0 ? tons : 1);
                    if (damage < p_damage) damage = p_damage;
                }

                ApplyDamage(attacker, damage, 1);
            }
            else if (g_segmentNormal.y > 0xb505)
            {
                p_damage >>= 1;
                ApplyDamage(attacker, p_damage, 7);
                ApplyDamage(attacker, p_damage, 8);
            }
            else
            {
                int angle = Fixed.Heading(-g_segmentNormal.x, -g_segmentNormal.z) - m_heading - m_torsoTwist.m_value;
                if (angle < -0xb40000) angle += 0x1680000;
                else if (angle > 0xb40000) angle -= 0x1680000;
                if (angle > 0)
                {
                    if (angle < 0x2d0000 || angle > 0x870000) ApplyDamage(attacker, p_damage, 2);
                    else ApplyDamage(attacker, p_damage, 5);
                }
                else if (angle > -0x2d0000 || angle < -0x870000) ApplyDamage(attacker, p_damage, 4);
                else ApplyDamage(attacker, p_damage, 6);
            }
        }

        // DamageMechHittingShape: hitting p_shape (null: the ground) faster than 200000 damages the mech (with collision
        // damage on) and a game thing's shape. (A network game's kind 0xb0 shape that destroys legs and itself isn't
        // reached: there is no network game.)
        public void DamageMechHittingShape(Shape p_shape)
        {
            int speedNow = ApproxLen.ApproximateVectorLength(m_newVelocityX, m_newVelocityY, m_newVelocityZ);
            if (speedNow <= 200000) return;
            int damage = Fixed.Div16(speedNow - 200000, 1300000) * 3;
            if (SimMain.g_difficulty.m_collisionDamage) ApplyCollisionDamage(null, damage);
            if (p_shape != null && (p_shape.Kind & 0x200) != 0) Shots.DamageGameThing(SimMain.g_world, m_player.m_index, p_shape, damage >> 16 << 16, p_shape.FxCenter);
        }
    }
}
