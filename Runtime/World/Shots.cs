using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // A shot in flight (shot.h), its flight in the lockstep's fixed point (Fixed).
    public sealed class Shot
    {
        public int m_type = -1;          // 7 knocks mechs' parts loose (the gauss rifle, the flamer)
        public int m_impact = -1;        // the effect it sets off, with what it hit (c_impact...)
        public FixVec m_position;        // 16.16 units: where it is (its object shows it)
        public FixVec m_velocity;        // 16.16 units a tick
        public FixVec m_steering;        // 16.16 units a tick, a tick: gravity, and a missile's turn toward its target
        public bool m_runaway;           // its steering ran away: the floats' NaN
        public int m_age, m_lifetime;    // ticks
        public int m_swayPhase;
        public ISceneObject m_object;    // its shape (a booyowthing record's), hidden until fired
        public object m_look;            // not the sim's: what the game draws it with
        public int m_target, m_targetKind; // a guided missile's: an index, c_shotTargetPlayer or c_shotTargetGameThing
        public int m_flags;              // 1 in flight, 0x8000 the proximity fuse
        public int m_unk0x3c;
        public int m_shooter;            // a player index
        public int m_weapon = -1;        // its weapon type
        public int m_damage, m_heat;     // 16.16
        public bool m_tracked;           // the tracked shot's view follows it
        public object m_ext;             // the host's data for this slot, or null (Host.Weapons.ResetShot clears it)
    }

    // The shots (shots.c: FirstShots, ResetShotSlot, UpdateAllShots, UpdateShot, GuideMissileToTarget, DetonateShot,
    // TrackLastShot, GetTrackedShotView, DamageGameThing, KillGameThing, DamageThingsInRadius; world.c's booyowthing
    // records), the floats as the lockstep's fixed point (Fixed): 0xaf slots, each the object of a
    // booyowthing record and its shot type, launched by SpawnShot (Mech). Each tick a shot moves on (gravity, a
    // missile's steering) and the segment it covered is tested against the world (TestSegmentCollision, its shooter's own
    // parts left out): a hit damages the mech part, the game thing or the chunk it hit and sets off the shot's effect,
    // just short of the point of impact. A shot whose time runs out vanishes. The missiles, the ground's stop, a hit's
    // additions and a missile's steering are the host's to change (Host.Weapons). The slots' objects are the game's
    // (Host.Fx).
    public static class Shots
    {
        // The original's fixed pool (0xaf) was the 486's limit, not a design: here the pool starts at 0xff and grows a
        // slot of a type's shape whenever every slot of it is in flight (the game's Host.Fx.GrowShot).
        public const int c_initialSlots = 0xff;
        public static int SlotCount => g_shots.Length;
        public const int c_proximityFuse = 0x8000;
        public const int c_shotTargetPlayer = 0x200, c_shotTargetGameThing = 0x400;

        public static Shot[] g_shots = new Shot[c_initialSlots];
        public static int s_loadedSlots = c_initialSlots; // the slots before any grew (the rest are hashed only in flight)

        // The missile types (3 and 4, and the host's): the launch's spread, the missile view.
        public static bool IsMissile(int p_type) => p_type == 3 || p_type == 4 || Host.Weapons.IsMissile(p_type);
        public static int g_nextShotRecord;

        // g_lastLocalMissile: the local player's last missile's slot, or -1; g_trackedShot and its view.
        public static int g_lastLocalMissile = -1;
        public static int g_trackedShot = -1;
        public static bool g_hasTrackedView;
        public static Vector3 g_trackedPosition; // the render side's (units)
        public static float g_trackedHeading;

        // FirstShots' shot half: every slot empty.
        public static void FirstShots()
        {
            if (g_shots.Length != c_initialSlots) g_shots = new Shot[c_initialSlots];
            s_loadedSlots = c_initialSlots;
            Host.Fx.ResetShotLooks();
            for (int i = 0; i < c_initialSlots; i++)
            {
                g_shots[i] = new Shot();
                ResetShotSlot(i);
            }

            g_nextShotRecord = 0;
            g_lastLocalMissile = -1;
            g_trackedShot = -1;
            g_hasTrackedView = false;
        }

        // ResetShotSlot.
        public static void ResetShotSlot(int p_index)
        {
            Shot shot = g_shots[p_index];
            shot.m_velocity = FixVec.Zero;
            shot.m_steering = FixVec.Zero;
            shot.m_runaway = false;
            shot.m_age = shot.m_lifetime = 0;
            shot.m_swayPhase = 0;
            shot.m_impact = -1;
            shot.m_target = 0;
            shot.m_targetKind = 0;
            shot.m_flags = 0;
            shot.m_unk0x3c = 0;
            shot.m_damage = shot.m_heat = 0;
            shot.m_tracked = false;
            Host.Weapons.ResetShot(shot);
        }

        // A new empty slot at the end of the pool.
        public static int AppendSlot()
        {
            int index = g_shots.Length;
            Shot[] grown = new Shot[index + 1];
            Array.Copy(g_shots, grown, index);
            grown[index] = new Shot();
            g_shots = grown;
            ResetShotSlot(index);
            return index;
        }

        // A slot the walk hashes: every loaded one, a grown one only in flight (a resynced machine may have grown more).
        public static bool Hashed(int p_index) => p_index < s_loadedSlots || g_shots[p_index].m_flags != 0;

        // SpawnShot's search: the first slot of shot type p_type that isn't flying and has an object, else a new one
        // (p_visualType: the shape it borrows when the world has none of p_type).
        public static int FindFree(int p_type, int p_visualType = -1)
        {
            int index = FindFreeSlot(p_type, p_visualType);
            if (s_capturing && index >= 0) s_captured.Add(index);
            return index;
        }

        static int FindFreeSlot(int p_type, int p_visualType)
        {
            if (g_shots[0] == null) return -1;
            for (int i = 0; i < SlotCount; i++)
            {
                Shot shot = g_shots[i];
                if (shot.m_type == p_type && shot.m_flags == 0 && shot.m_object != null) return i;
            }

            return Host.Fx.GrowShot(p_type, p_visualType);
        }

        // A networked match's reported volley's catch-up (UpdateWeaponFireState's): the shots launched
        // between BeginCapture and CatchUpCaptured flown on p_ticks at once, a step at most at a time, hitting as they
        // would have (the damage the host's alone).
        static bool s_capturing;
        static readonly List<int> s_captured = new List<int>();

        public static void BeginCapture()
        {
            s_captured.Clear();
            s_capturing = true;
        }

        public static void CatchUpCaptured(int p_ticks)
        {
            s_capturing = false;
            foreach (int index in s_captured)
            {
                Shot shot = g_shots[index];
                int left = p_ticks;
                while (left > 0 && shot.m_flags != 0 && shot.m_object != null)
                {
                    int dt = Math.Min(left, Clock.StepTicks);
                    left -= dt;
                    shot.m_age += dt;
                    shot.m_lifetime -= dt;
                    if (shot.m_unk0x3c != 0) continue;
                    // The host: the mechs as they stood as this stretch ended (rewound hits).
                    bool rewound = Host.Net.RewindMechs(left, shot.m_shooter);
                    try { UpdateShot(index, dt); }
                    finally { if (rewound) Host.Net.RestoreMechs(); }
                }
            }

            s_captured.Clear();
        }

        // UpdateAllShots: every shot in flight ages and moves on.
        public static void UpdateAllShots()
        {
            Host.Fx.UpdateTracers();
            if (g_shots[0] == null) return;
            for (int i = 0; i < SlotCount; i++)
            {
                Shot shot = g_shots[i];
                if (shot.m_flags == 0 || shot.m_object == null) continue;
                shot.m_age += Clock.g_deltaTime;
                shot.m_lifetime -= Clock.g_deltaTime;
                if (shot.m_unk0x3c == 0) UpdateShot(i, Clock.g_deltaTime);
            }
        }

        // UpdateShot: the move (IntegrateMidpoint), the segment tested for a hit (or the proximity fuse's target, or the
        // ground below height 0), and on a hit the damage and the shot detonated 100 units short of it; out of time, it
        // vanishes; else a guided missile steers on.
        // p_dt: the ticks flown (the step's; a catch-up's own).
        static void UpdateShot(int p_index, int p_dt)
        {
            Shot shot = g_shots[p_index];
            int dt = p_dt;
            // A shot whose steering ran away flies on unseen, its floats no longer numbers, until its time runs out.
            if (shot.m_runaway)
            {
                shot.m_object.Show(false);
                if (shot.m_lifetime <= 0) DetonateShot(p_index, false, shot.m_position, shot.m_position);
                return;
            }

            int damageFlags = 0;
            FixVec start = shot.m_position;
            FixVec velocity = shot.m_velocity;
            var step = new FixVec(shot.m_steering.X * dt, shot.m_steering.Y * dt, shot.m_steering.Z * dt);
            var at = new FixVec(start.X + (step.X / 2 + velocity.X) * dt, start.Y + (step.Y / 2 + velocity.Y) * dt,
                start.Z + (step.Z / 2 + velocity.Z) * dt);
            velocity += step;

            CollisionRay ray = CollisionRay.BuildRayFromSegment(start, at);
            if ((at - start).ApproxLength > 3000L << 16) ray.BuildRayFloat();
            else ray.BuildRayFixed();

            bool hitResult;
            Shape hit = null;
            FixVec hitAt = default, back = default;
            if ((shot.m_flags & c_proximityFuse) != 0)
            {
                hitResult = true;
                if (shot.m_targetKind == c_shotTargetPlayer)
                {
                    Mech mech = SimMain.MechOf(shot.m_target);
                    hit = mech != null ? mech.m_host.RootShape : null;
                }
                else if (shot.m_targetKind == c_shotTargetGameThing) hit = SimMain.g_world != null ? Host.World.ThingShape(shot.m_target) : null;

                hitAt = back = start;
            }
            else
            {
                hitResult = ShapeCollision.TestSegmentCollision(ref ray, out Shape found, shot.m_shooter);
                hit = found;
                if (hitResult)
                {
                    hitAt = ray.End;
                    FixVec dir = ray.Direction;
                    back = hitAt - new FixVec(dir.X * 100, dir.Y * 100, dir.Z * 100);
                }
            }

            // Mech Soccer's ball (not in the original): before anything else the segment hit, it takes the shot (pushed
            // at the host) and the shot goes off on it.
            if (Host.World.SoccerActive && (shot.m_flags & c_proximityFuse) == 0 &&
                Host.World.ShotHitsBall(shot, start, hitResult ? hitAt : at, out FixVec ballAt, out FixVec ballBack))
            {
                shot.m_impact |= Effects.c_impactThing;
                DetonateShot(p_index, true, ballAt, ballBack);
                return;
            }

            if (hitResult && hit != null)
            {
                int surface = hit.Kind;
                SimMain.g_lastHitShooter = shot.m_shooter;
                if ((surface & 0x100) != 0) HitMech(shot, hit, velocity, ref damageFlags);
                else if ((surface & 0x200) != 0)
                {
                    shot.m_impact |= Effects.c_impactThing;
                    if (SimMain.g_world != null) DamageGameThing(SimMain.g_world, shot.m_shooter, hit, shot.m_damage, hitAt);
                }
                else if ((surface & 0xf0) == 0x50) Debris.DamageChunk(hit.Owner, shot.m_damage);
                else if ((surface & 0x400) != 0) shot.m_impact |= Effects.c_impactThing;
                else shot.m_impact |= Effects.c_impactGround;
            }
            else if (at.Y < 0)
            {
                long ground = SimMain.g_terrain != null ? SimMain.g_terrain.GetTerrainHeight(at) : 0;
                if (at.Y < Host.Weapons.ShotGroundStop(ground))
                {
                    hitResult = true;
                    shot.m_impact |= Effects.c_impactGround;
                    ray.ClipRayToGround(ground);
                    hitAt = back = ray.End;
                }
            }

            if (hitResult) DetonateShot(p_index, true, hitAt, back, hit);
            else if (shot.m_lifetime <= 0) DetonateShot(p_index, false, at, at);
            else
            {
                if (shot.m_targetKind != 0 && shot.m_age > 0) GuideMissileToTarget(shot, at, dt);
                shot.m_position = at;
                shot.m_object.SetPosition(at.ToFloat());
                shot.m_velocity = velocity;
                if (shot.m_tracked)
                {
                    g_trackedShot = p_index;
                    g_trackedPosition = at.ToFloat();
                    g_trackedHeading = Fixed.Heading(velocity.X, velocity.Z) / 65536f;
                    g_hasTrackedView = true;
                }
            }
        }

        // A shot's hit on a mech's part: hit from behind (its bearing within 90 degrees of the mech's heading) it
        // strikes the rear armor; the hits counted; the victim's heat and damage; the local player's mech shakes.
        static void HitMech(Shot p_shot, Shape p_hit, FixVec p_velocity, ref int p_damageFlags)
        {
            int victim = p_hit.Owner;
            Mech mech = SimMain.MechOf(victim);
            p_shot.m_impact |= Effects.c_impactMech;
            if (p_shot.m_type == 7) p_shot.m_impact |= 0x1000;
            Host.Weapons.ShotHitMech(p_shot, mech, victim);

            if (mech != null)
            {
                int bearing = (mech.HeadingFixed - Fixed.Heading(p_shot.m_velocity.X, p_shot.m_velocity.Z)) % Fixed.Deg360;
                if (bearing > Fixed.Deg180) bearing -= Fixed.Deg360;
                else if (bearing < -Fixed.Deg180) bearing += Fixed.Deg360;
                if (bearing < 90 << 16 && bearing > -90 << 16) p_damageFlags |= 0x8000;
            }

            // g_careerRecord's hits (by the local player, its team; on them).
            Host.World.CountHit(victim);

            // The local player's hit (its impact's variant and quiet sound): any pilot's, the same on every machine.
            if (SimMain.IsPilot(victim))
            {
                p_shot.m_impact |= 0x4000;
                if (p_hit.PartId == 1 || p_hit.PartId == 3 || p_hit.PartId == 2 || p_hit.PartId == 4) p_shot.m_impact |= 0x2000;
            }

            if (victim == SimMain.g_localPlayerId) CameraShake.PlayPlayerHitFeedback(p_velocity.ToFloat(), Host.Fx.RenderTicks);

            // RecordAttack: the shooter's AI goal (the user's); the damage after it.
            Mech.RecordAttack(p_shot.m_shooter, victim);
            if (mech != null)
            {
                // A networked match: the host's alone, reported.
                if (Host.Net.DamagesHere)
                {
                    mech.m_deltaHeat += p_shot.m_heat;
                    Host.Net.Heated(mech, p_shot.m_heat);
                    Host.Weapons.ShotHeatedMech(p_shot, mech);
                }

                mech.ApplyDamage(p_shot.m_shooter, p_shot.m_damage, p_hit.PartId | p_damageFlags);
            }
        }



        // Past this (16.16 units a tick, a tick) the floats' steering soon runs to infinity: m_runaway.
        public const long c_runawaySteering = 1L << 50;

        // GuideMissileToTarget: the steering toward the target (a player not destroyed, a game thing standing; else the
        // missile flies on unguided), the proximity fuse armed within 100 units, the missile turned along its flight.
        static void GuideMissileToTarget(Shot p_shot, FixVec p_at, int p_dt)
        {
            if (p_shot.m_target < 0) return;
            FixVec target;
            switch (p_shot.m_targetKind)
            {
                case c_shotTargetPlayer:
                {
                    Mech mech = SimMain.MechOf(p_shot.m_target);
                    if (mech == null || (mech.PlayerFlags & 2) != 0 || (mech.PlayerFlags & 4) != 0)
                    {
                        p_shot.m_steering = FixVec.Zero;
                        p_shot.m_targetKind = 0;
                        return;
                    }

                    target = FixVec.FromUnits(mech.ObjectPosition);
                    break;
                }
                case c_shotTargetGameThing:
                {
                    if (SimMain.g_world == null || !Host.World.ThingStanding(p_shot.m_target, out Vector3Int thing))
                    {
                        p_shot.m_steering = FixVec.Zero;
                        p_shot.m_targetKind = 0;
                        return;
                    }

                    target = FixVec.FromUnits(thing);
                    break;
                }
                default:
                    return;
            }

            FixVec d = target - p_at;
            long distance = Host.Weapons.MissileDistance(p_shot, d);
            if (distance <= 100L << 16) p_shot.m_flags |= c_proximityFuse;
            // On the target itself the floats' direction is 0 / 0: no longer numbers.
            if (distance == 0)
            {
                p_shot.m_runaway = true;
                return;
            }

            d = d.Div(distance);
            d = Host.Weapons.MissileAim(p_shot, d, distance);
            FixVec v = p_shot.m_velocity.NormalizeGuarded();
            int steered = Host.Weapons.SteerMissile(p_shot, d, v, p_dt);
            if (steered == 2) return;
            if (steered == 0) p_shot.m_steering = d - v;
            int heading = Fixed.Heading(v.X, v.Z);
            int pitch = -Fixed.Asin(v.Y);
            p_shot.m_object.SetRotation(Quaternion.Euler(pitch / 65536f, heading / 65536f, 0));
        }

        // DetonateShot: the shot hidden and its slot freed; p_explode sets off its effect at p_at, seen from p_camera.
        static void DetonateShot(int p_index, bool p_explode, FixVec p_at, FixVec p_camera, Shape p_hit = null)
        {
            Shot shot = g_shots[p_index];
            if (shot.m_tracked)
            {
                g_trackedShot = -1;
                g_hasTrackedView = false;
            }

            shot.m_object.Show(false);
            int impact = shot.m_impact;
            int shooter = shot.m_shooter;
            object impactFx = Host.Fx.ImpactFxFor(shot.m_weapon);
            // A missile's (guided, or a torpedo) impact is an explosion's sound (Effects.g_explosionSound).
            bool missile = (shot.m_weapon >= 0 && WeaponDef.Of(shot.m_weapon).m_guided != 0) || Host.Weapons.ExplodesAsMissile(shot);
            ResetShotSlot(p_index);
            if (!p_explode) return;
            Effects.g_impactFx = impactFx;
            Effects.g_explosionSound = missile;
            try { Effects.SpawnEffect(shooter, impact, p_at.ToFloat(), p_camera.ToFloat(), p_hit); }
            finally
            {
                Effects.g_impactFx = null;
                Effects.g_explosionSound = false;
            }
        }

        // GetTrackedShotView: whether the tracked shot still flies (its view in g_trackedPosition, g_trackedHeading).
        public static bool GetTrackedShotView()
        {
            if (g_trackedShot != -1 && g_shots[g_trackedShot] != null && g_shots[g_trackedShot].m_tracked) return true;
            g_trackedShot = -1;
            g_hasTrackedView = false;
            return false;
        }

        // TrackLastShot: the local player's last missile followed, while it flies (slot 0 never is).
        public static bool TrackLastShot()
        {
            Shot shot = g_lastLocalMissile > 0 && g_shots[0] != null ? g_shots[g_lastLocalMissile] : null;
            if (shot == null || shot.m_flags == 0 || shot.m_shooter != SimMain.g_localPlayerId)
            {
                g_trackedShot = -1;
                return false;
            }

            g_trackedShot = g_lastLocalMissile;
            shot.m_tracked = true;
            // g_trackedShotView keeps its last values until the shot next moves: here, the shot's own.
            g_trackedPosition = shot.m_position.ToFloat();
            g_trackedHeading = Fixed.Heading(shot.m_velocity.X, shot.m_velocity.Z) / 65536f;
            g_hasTrackedView = true;
            return true;
        }

        // ---- Game things ------------------------------------------------------------------------------------------

        // DamageGameThing: p_damage (16.16; its whole points) off the hit points of the thing p_shape belongs to; when
        // they run out, it blows up (a mine-like 0xb0 shape with effect 0xd, else 3 and debris) and is destroyed. p_at
        // 16.16 units.
        // A networked match's is the host's alone, each reported.
        public static void DamageGameThing(World p_world, int p_owner, Shape p_shape, int p_damage, FixVec p_at)
        {
            if (p_shape == null || !Host.Net.DamagesHere) return;
            if (Host.Net.BeginDamage()) Host.Net.ThingDamaged(p_owner, p_shape.Owner, p_shape.Kind, p_damage, p_at);
            try { DamageGameThing(p_world, p_owner, p_shape.Owner, p_shape.Kind, p_damage, p_at); }
            finally { Host.Net.EndDamage(); }
        }

        // The thing p_index (its shape's kind p_kind) damaged.
        public static void DamageGameThing(World p_world, int p_owner, int p_index, int p_kind, int p_damage, FixVec p_at)
        {
            int index = p_index;
            if (index < 0 || index >= p_world.m_gameThings.Count || Host.World.IsTunnelDoor(index)) return;
            GameThing thing = p_world.m_gameThings[index];
            if ((thing.m_flags & 4) != 0 || thing.m_hitPoints == 0) return;

            thing.m_hitPoints -= p_damage / 65536;
            if (thing.m_hitPoints <= 0)
            {
                Vector3 at = p_at.ToFloat();
                thing.m_hitPoints = 0;
                if ((p_kind & 0xf0) == 0xb0) Effects.SpawnExplosion(p_owner, 0xd, at, at);
                else
                {
                    Effects.SpawnExplosion(p_owner, 3, at, at);
                    Effects.SpawnExplosion(p_owner, 0x20b, at, at);
                }

                KillGameThing(p_world, index);
            }
        }

        // KillGameThing: the kill counted by the last shooter's side, then its object replaced (the game's
        // DestroyThingObject).
        public static void KillGameThing(World p_world, int p_index)
        {
            GameThing thing = p_world.m_gameThings[p_index];
            Shape aimed = SimMain.g_aimedShape;
            if (aimed != null && (aimed.Kind & 0x200) != 0 && aimed.Owner == p_index) SimMain.g_aimedShape = null;
            SimMain.ForgetAimed(0x200, p_index);
            if ((thing.m_flags & 4) != 0) return;

            int shooter = SimMain.g_lastHitShooter;
            if (shooter >= 0) Host.World.CountThingKill(shooter, Teams.GetThingSide(p_world, p_index));

            Host.World.DestroyThingObject(p_index);
        }

        // DamageThingsInRadius: every standing thing whose shape comes within p_radius of p_at takes p_rate per tick
        // (16.16 units and points).
        public static void DamageThingsInRadius(World p_world, int p_owner, FixVec p_at, long p_radius, int p_rate)
        {
            // Mech Soccer's ball (not in the original): the splash pushes it (the host's).
            if (Host.World.SoccerActive) Host.World.SplashBall(p_at, p_radius, p_rate);
            for (int i = p_world.m_gameThings.Count; i-- > 0;)
            {
                GameThing thing = p_world.m_gameThings[i];
                if ((thing.m_flags & 4) != 0) continue;
                Shape shape = Host.World.ThingShape(i);
                if (shape == null) continue;
                FixVec center = shape.FxCenter;
                long reach = shape.FxRadius + p_radius;
                if ((center - p_at).ApproxLength < reach)
                {
                    DamageGameThing(p_world, p_owner, shape, p_rate * Clock.g_deltaTime, center);
                }
            }
        }
    }
}
