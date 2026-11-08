using System;
using UnityEngine;

namespace Demech2
{
    // Targets (targeting.c: CycleTarget, TargetNavPoint, TargetGamePiece, TargetGameThing, UpdateTarget,
    // TargetAtReticle, TargetNearestEnemy, GetBearingAndRange and the cycles; lineofsight.c), the original's floats
    // as the lockstep's fixed point (Fixed): a player's target (PlayerTargetInfo) is a nav point, a player or a game thing,
    // which it cycles through in that ring, and where it is from the player. The local player may only pick what its
    // radar shows (flags 0x1400) within 0x2ab98 units, unless it picked it at the reticle. Reaching a nav marks it
    // reached; the inspect key inspects a target close enough.
    public static class Targeting
    {
        const int MaxRange = 0x2ab98;

        // g_reticleTargeting: the target was picked at the reticle (the radar's limits don't hold). Not the original's:
        // each pilot's own (Mech.m_reticleTargeting).

        // g_inspectResult: 0 none, 1 inspected, 2 too far, 3 already inspected by the team.
        public static int g_inspectResult;

        // Not in the original: the last nonzero g_inspectResult, held for the cockpit (render rate) to take once, so the
        // result's sound plays once rather than every rendered frame until the next step, nor is lost to a catch-up step.
        public static int s_inspectShown;



        // CycleTarget: p_step places on through the navs, the players and the game things; with p_step 0 (or a lost
        // target) from the first player. The target is lost (0x1000) when none qualifies.
        public static void CycleTarget(Mech p_mech, int p_step, int p_flags)
        {
            World world = SimMain.g_world;
            if (world == null || p_mech == null) return;
            bool found = false;
            if (p_mech.IsPilot) p_mech.m_reticleTargeting = false;
            int target = p_mech.m_targetInfo.m_target;
            int kind = target & 0xf00, index = target & 0xff;
            if (p_step == 0 || (target & 0x1000) != 0)
            {
                kind = 0x200;
                index = 0;
                p_step = 1;
            }
            else index += p_step;

            int limit = world.m_players.Count + world.m_navs.Count + world.m_gameThings.Count + 3;
            int tries = 0;
            while (tries++ < limit && !found)
            {
                int result;
                switch (kind)
                {
                    case 0x100:
                        result = TargetNavPoint(p_mech, index, p_flags);
                        if (result == -1)
                        {
                            index = world.m_gameThings.Count - 1;
                            kind = 0x400;
                        }
                        else if (result == -2)
                        {
                            index = 0;
                            kind = 0x200;
                        }
                        else if (result == 1) found = true;
                        else index += p_step;
                        break;
                    case 0x200:
                        result = TargetGamePiece(p_mech, index, p_flags);
                        if (result == -1)
                        {
                            index = world.m_navs.Count - 1;
                            kind = 0x100;
                        }
                        else if (result == -2)
                        {
                            index = 0;
                            kind = 0x400;
                        }
                        else if (result == 1) found = true;
                        else index += p_step;
                        break;
                    case 0x400:
                        result = TargetGameThing(p_mech, index, p_flags);
                        if (result == -1)
                        {
                            index = world.m_players.Count - 1;
                            kind = 0x200;
                        }
                        else if (result == -2)
                        {
                            index = 0;
                            kind = 0x100;
                        }
                        else if (result == 1) found = true;
                        else index += p_step;
                        break;
                    default:
                        index = 0;
                        kind = 0x200;
                        break;
                }
            }

            if (found)
            {
                p_mech.m_targetInfo.m_target = kind | index;
                if (kind == 0x100) Host.Mechs.RouteNavTargeted(p_mech, index);
            }
            else p_mech.m_targetInfo.m_target |= 0x1000;

            // The autopilot stops on a target that isn't a nav (or as the host's route says).
            if (Host.Mechs.AutopilotDrops(p_mech, kind) && p_mech.m_autopilot != 0) p_mech.m_steering["mech.autopilot"] = 1;
        }

        // Writes the target's place, bearing, distances and pitch.
        static void SetInfo(Mech p_mech, Vector3Int p_at)
        {
            Vector3Int d = p_at - p_mech.ObjectPosition;
            GetBearingAndRange(d, out int heading, out int range, out int distance, out int pitch);
            PlayerTargetInfo info = p_mech.m_targetInfo;
            info.m_position = p_at;
            info.m_heading = heading;
            info.m_range = range;
            info.m_distance = distance;
            info.m_pitch = pitch;
        }

        // TargetNavPoint: 1, or -1 and -2 out of range, -9 for flags 0xe, -3 a free nav, -4 not an objective's when flag
        // 0x10000 asks for one, -6 another owner's or team's.
        public static int TargetNavPoint(Mech p_mech, int p_nav, int p_flags)
        {
            World world = SimMain.g_world;
            if (p_nav < 0) return -1;
            if (p_nav >= world.m_navs.Count) return -2;
            if ((p_flags & 0xe) != 0) return -9;
            NavPoint nav = world.m_navs[p_nav];
            if (nav.m_used == 0) return -3;
            if ((p_flags & 0x10000) != 0 && (nav.m_flags & 0x40) == 0) return -4;
            if ((nav.m_flags & 1) != 0)
            {
                if (nav.m_owner != (p_mech.m_player.m_index | 0x200)) return -6;
            }
            else if ((p_flags & 0x100) != 0) return -6;

            if (p_mech.m_player.m_team != nav.m_team) return -6;
            if (nav.m_obj != null) nav.m_position = NodeUnits(nav.m_obj);
            SetInfo(p_mech, nav.m_position);
            Host.Mechs.RouteNavPlaced(p_mech);
            return 1;
        }

        // TargetGamePiece: 1, or -1 and -2 out of range, -9 for flags 0x15 or the wrong side, -3 a shut-down player (for
        // the local player, but at the reticle), a destroyed one or itself, -4 not an objective's, -5 and -7 one the
        // local player may not pick (off the radar, or too far), -8 one without a shape.
        public static int TargetGamePiece(Mech p_mech, int p_index, int p_flags)
        {
            World world = SimMain.g_world;
            if (p_index < 0) return -1;
            if (p_index >= world.m_players.Count) return -2;
            if ((p_flags & 0x15) != 0) return -9;
            bool local = p_mech.IsPilot;
            Player target = world.m_players[p_index];
            if (!p_mech.m_reticleTargeting && Host.World.HiddenShutDown(target) && local) return -3;
            if ((target.m_flags & 6) != 0) return -3;
            if (target.m_index == p_mech.m_player.m_index) return -3;
            if ((p_flags & 0x10000) != 0 && (target.m_flags & 0x40) == 0) return -4;
            if ((p_flags & 0x20000) != 0 && SimMain.SideOf(p_mech.m_player.m_index, p_index) != 0) return -9;
            if ((p_flags & 0x40000) != 0 && SimMain.SideOf(p_mech.m_player.m_index, p_index) != 1) return -9;
            if (!p_mech.m_reticleTargeting && (target.m_flags & 0x1400) == 0 && local) return -5;
            if ((target.m_flags & 0x800) != 0 && local) return -5;
            Mech mech = target.m_mech;
            if (mech == null || mech.m_host.RootShape == null) return -8;

            Vector3Int at = mech.ObjectPosition;
            GetBearingAndRange(at - p_mech.ObjectPosition, out _, out int range, out _, out _);
            if ((target.m_flags & 0x1000) == 0 && range > MaxRange && local) return -7;
            SetInfo(p_mech, at);
            return 1;
        }

        // TargetGameThing: as TargetGamePiece for a game thing (-3 a destroyed one); its test of flag 0x10000 never
        // holds.
        public static int TargetGameThing(Mech p_mech, int p_index, int p_flags)
        {
            World world = SimMain.g_world;
            if (p_index < 0) return -1;
            if (p_index >= world.m_gameThings.Count) return -2;
            if ((p_flags & 0x23) != 0) return -9;
            bool local = p_mech.IsPilot;
            GameThing thing = world.m_gameThings[p_index];
            if ((thing.m_flags & 4) != 0) return -3;
            if ((p_flags & 0x20000) != 0 && SimMain.ThingSideOf(p_mech.m_player.m_index, p_index) != 0) return -9;
            if ((p_flags & 0x40000) != 0 && SimMain.ThingSideOf(p_mech.m_player.m_index, p_index) != 1) return -9;
            if (!p_mech.m_reticleTargeting && (thing.m_flags & 0x1400) == 0 && local) return -5;
            if ((thing.m_flags & 0x800) != 0 && local) return -5;
            if (Host.World.ThingShape(p_index) == null) return -8;
            if (!Host.World.ThingStanding(p_index, out Vector3Int at)) return -8;

            GetBearingAndRange(at - p_mech.ObjectPosition, out _, out int range, out _, out _);
            if ((thing.m_flags & 0x1000) == 0 && range > MaxRange && local) return -7;
            SetInfo(p_mech, at);
            return 1;
        }

        // UpdateTarget: the target checked again and its place updated, or lost (0x1000). A nav within its radius is
        // reached (for the local player not on the autopilot: its sound) and the next nav targeted. The inspect key
        // inspects a player or game thing within its radius and 20000 for the team.
        public static bool UpdateTarget(Mech p_mech)
        {
            World world = SimMain.g_world;
            if (world == null) return false;
            bool lost = true, isLocal = p_mech.IsLocal;
            int index = p_mech.m_targetInfo.m_target & 0xff, kind = p_mech.m_targetInfo.m_target & 0xf00;
            switch (kind)
            {
                case 0x200:
                    if (TargetGamePiece(p_mech, index, 0) >= 0) lost = false;
                    break;
                case 0x400:
                    if (TargetGameThing(p_mech, index, 0) >= 0) lost = false;
                    break;
                case 0x100:
                    if (TargetNavPoint(p_mech, index, 0) >= 0)
                    {
                        lost = false;
                        NavPoint nav = world.m_navs[index];
                        if (nav.m_radius > p_mech.m_targetInfo.m_distance && p_mech.m_autopilot != 1 && isLocal)
                        {
                            if ((nav.m_flags & 0x20) == 0)
                            {
                                nav.m_flags |= 0x20;
                                nav.m_teamsReached |= 1 << p_mech.m_player.m_team;
                                CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xe7), 100, 0x40, 5, 0x50);
                            }

                            CycleNavTarget(p_mech, 1, false);
                        }
                    }

                    break;
            }

            if (lost)
            {
                p_mech.m_targetInfo.m_target |= 0x1000;
                return false;
            }

            if (isLocal) g_inspectResult = 0;
            if (p_mech.m_steering.Pressed("lock.inspect"))
            {
                p_mech.m_steering["lock.inspect"] = 0;
                int team = 1 << p_mech.m_player.m_team;
                if (kind == 0x200)
                {
                    Mech target = SimMain.MechOf(index);
                    if (target == null) return true;
                    if ((target.m_inspectedBy & team) == 0)
                    {
                        if (target.m_radius + 20000 > p_mech.m_targetInfo.m_distance)
                        {
                            if (isLocal) g_inspectResult = 1;
                            target.m_player.m_flags |= 0x20;
                            target.m_inspectedBy |= team;
                        }
                        else if (isLocal) g_inspectResult = 2;
                    }
                    else if (isLocal) g_inspectResult = 3;
                }
                else if (kind == 0x400)
                {
                    GameThing thing = world.m_gameThings[index];
                    if ((thing.m_teamsReached & team) == 0)
                    {
                        if (thing.m_radius + (20000L << 16) > (long)p_mech.m_targetInfo.m_distance << 16)
                        {
                            if (isLocal) g_inspectResult = 1;
                            thing.m_flags |= 0x20;
                            thing.m_teamsReached |= team;
                        }
                        else if (isLocal) g_inspectResult = 2;
                    }
                    else if (isLocal) g_inspectResult = 3;
                }

                if (isLocal && g_inspectResult != 0) s_inspectShown = g_inspectResult;
            }

            return true;
        }

        // TargetAtReticle: the local player's aimed shape (g_aimedShape) targeted, a player's mech or a game thing, when
        // it may be; the old target back if UpdateTarget refuses it.
        public static void TargetAtReticle(Mech mech)
        {
            if (mech == null) return;
            int target = -1;
            bool autopilot = mech.m_autopilot == 1;
            int previous = mech.m_targetInfo.m_target;
            Shape shape = mech.m_aimedShape;
            if (shape != null)
            {
                if ((shape.Kind & 0x100) != 0)
                {
                    if (TargetGamePiece(mech, shape.Owner, 0) >= 0) target = shape.Owner | 0x200;
                }
                else if ((shape.Kind & 0x200) != 0)
                {
                    if (TargetGameThing(mech, shape.Owner, 0) >= 0) target = shape.Owner | 0x400;
                }

                if (target != -1 && mech.m_targetInfo.m_target != target)
                {
                    mech.m_targetInfo.m_target = target;
                    if (mech.m_autopilot != 0 && Host.Mechs.AutopilotDrops(mech, -1)) mech.m_steering["mech.autopilot"] = 1;
                    if (!UpdateTarget(mech))
                    {
                        mech.m_targetInfo.m_target = previous;
                        if (autopilot)
                        {
                            mech.m_steering["mech.autopilot"] = 0;
                            mech.m_autopilot = 1;
                        }
                    }
                }
            }

            mech.m_reticleTargeting = true;
        }

        // A static object's position (its node's), rounded to whole units.
        static Vector3Int NodeUnits(Transform p_node)
        {
            FixVec at = Host.Anim.GetObjPosition(p_node);
            return new Vector3Int((int)((at.X + Fixed.Half) >> 16), (int)((at.Y + Fixed.Half) >> 16),
                (int)((at.Z + Fixed.Half) >> 16));
        }

        // GetBearingAndRange: the vector's heading, its ground distance (along the heading), its full length (along the
        // pitch) and its pitch: 16.16 degrees and whole units (truncated).
        public static void GetBearingAndRange(Vector3Int p_d, out int p_heading, out int p_length, out int p_distance, out int p_pitch)
        {
            p_heading = Fixed.Heading(p_d.x, p_d.z);
            long ground; // 16.16 units
            if (Math.Abs((long)p_d.x) > Math.Abs((long)p_d.z))
            {
                int sine = Fixed.Sin(p_heading);
                ground = sine != 0 ? ((long)p_d.x << 32) / sine : 0;
            }
            else
            {
                int cosine = Fixed.Cos(p_heading);
                ground = cosine != 0 ? ((long)p_d.z << 32) / cosine : 0;
            }

            p_pitch = Fixed.Heading((long)p_d.y << 16, ground);
            int pitchCosine = Fixed.Cos(p_pitch);
            long length = pitchCosine != 0 ? Fixed.Div16L(ground, pitchCosine) : 0;
            p_length = (int)(length / 65536);
            p_distance = (int)(ground / 65536);
        }

        // CycleNavTarget: p_step navs on (with p_ownOnly, the player's own).
        public static void CycleNavTarget(Mech p_mech, int p_step, bool p_ownOnly) =>
            CycleTarget(p_mech, p_step, 1 | (p_ownOnly ? 0x100 : 0));

        public static void CycleGameThingTarget(Mech p_mech, int p_step) => CycleTarget(p_mech, p_step, 4);
        public static void CycleGamePieceTarget(Mech p_mech, int p_step) => CycleTarget(p_mech, p_step, 2);
        public static void CycleFriendlyTarget(Mech p_mech, int p_step) => CycleTarget(p_mech, p_step, 0x20008);
        public static void CycleEnemyTarget(Mech p_mech, int p_step) => CycleTarget(p_mech, p_step, 0x40008);

        // TargetNearestEnemy: the nearest enemy within 0x2ab98, from a cycle through them all; the current target stays
        // without one, or when UpdateTarget refuses it.
        public static void TargetNearestEnemy(Mech mech)
        {
            if (mech == null) return;
            int target = 0, best = -1;
            int bestDistance = int.MaxValue;
            bool autopilot = mech.m_autopilot == 1;
            int saved = mech.m_targetInfo.m_target;
            CycleEnemyTarget(mech, 0);
            while (best != target)
            {
                target = mech.m_targetInfo.m_target;
                if ((target & 0x1000) != 0) break;
                int distance = mech.m_targetInfo.m_range;
                if (distance < bestDistance)
                {
                    best = target;
                    bestDistance = distance;
                    target = 0;
                }

                CycleEnemyTarget(mech, 1);
            }

            if (best == -1 || bestDistance > MaxRange)
            {
                mech.m_targetInfo.m_target = saved;
                if (autopilot) mech.m_steering["mech.autopilot"] = 0;
            }
            else
            {
                mech.m_targetInfo.m_target = best;
                if (!UpdateTarget(mech))
                {
                    mech.m_targetInfo.m_target = saved;
                    if (autopilot) mech.m_steering["mech.autopilot"] = 0;
                }
            }
        }
    }
}
