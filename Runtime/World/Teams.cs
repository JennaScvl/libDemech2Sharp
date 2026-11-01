using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // The teams (stars) as the AI works them (team.c: SetTeamLeader, GetTeamSlotPosition, AssignTeamSlots,
    // SetTeamFormation, GetTeamFormation, OnSameSide, GetPlayerSide, GetThingSide; targeting.c: AddNavPoint,
    // RemoveNavPoint): a team's leader and members, its formation (each slot's offset from the leader and heading), the
    // places its slots put its members, and the nav points AI players place for themselves.
    public static class Teams
    {
        // The members of team p_team, by player index (Team::m_members).
        public static IReadOnlyList<int> TeamMembers(World p_world, int p_team) =>
            p_team >= 0 && p_team < World.c_maxTeams ? p_world.m_teams[p_team].m_members : (IReadOnlyList<int>)Array.Empty<int>();

        // Team::m_side.
        public static int TeamSide(World p_world, int p_team) =>
            p_team >= 0 && p_team < World.c_maxTeams ? p_world.m_teams[p_team].m_side : 2;

        // GetPlayerSide: the side of player p_index's team (0 friendly, 1 enemy, 2 neutral).
        public static int GetPlayerSide(World p_world, int p_index) =>
            p_index >= 0 && p_index < p_world.m_players.Count && p_world.m_teams[p_world.m_players[p_index].m_team] != null
                ? p_world.m_teams[p_world.m_players[p_index].m_team].m_side
                : 2;

        // OnSameSide.
        public static bool OnSameSide(World p_world, int p_a, int p_b) => GetPlayerSide(p_world, p_b) == GetPlayerSide(p_world, p_a);

        // GetTeamLeader.
        public static int GetTeamLeader(World p_world, int p_team) =>
            p_team >= 0 && p_team < World.c_maxTeams ? p_world.m_teams[p_team].m_leader : -1;

        // Not the original's: the star of the thing's affiliation, -1 without one (SimMain.ThingSideOf).
        public static int ThingTeam(World p_world, int p_index)
        {
            if (p_index < 0 || p_index >= p_world.m_gameThings.Count) return -1;
            int affiliation = p_world.m_gameThings[p_index].m_affiliation;
            if (affiliation < 0) return -1;
            for (int i = 0; i < World.c_maxTeams; i++)
                if (p_world.m_teams[i] != null && p_world.m_teams[i].m_affiliation == affiliation) return i;
            return -1;
        }

        // GetThingSide: the side of the thing's affiliation (2 without one).
        public static int GetThingSide(World p_world, int p_index)
        {
            if (p_index < 0 || p_index >= p_world.m_gameThings.Count) return 2;
            int affiliation = p_world.m_gameThings[p_index].m_affiliation;
            return affiliation < 0 ? 2 : affiliation < p_world.m_starSides.Length ? p_world.m_starSides[affiliation] : 0;
        }

        // SetTeamLeader: p_player leads p_team (-1: none), the formation's origin moved to its slot.
        public static bool SetTeamLeader(World p_world, int p_team, int p_player)
        {
            if (p_player == -1)
            {
                p_world.m_teams[p_team].m_leader = -1;
                return false;
            }

            if (p_player >= p_world.m_players.Count || p_team >= World.c_maxTeams) return false;
            Team team = p_world.m_teams[p_team];
            team.m_leader = p_player;
            int slot = p_world.m_players[p_player].m_slot;
            int dx = slot >= 0 && slot < 8 ? team.m_x[slot] : 0, dz = slot >= 0 && slot < 8 ? team.m_z[slot] : 0;
            for (int i = 0; i < 8; i++)
            {
                team.m_x[i] -= dx;
                team.m_z[i] -= dz;
            }

            return true;
        }

        // GetTeamSlotPosition: where p_player's formation slot puts it: the leader's own place and heading, or the
        // slot's offset turned by the leader's heading from the leader's place (units; the heading 16.16 degrees).
        public static bool GetTeamSlotPosition(World p_world, int p_player, out int p_x, out int p_z, out int p_heading)
        {
            List<Player> players = p_world.m_players;
            p_x = p_z = p_heading = 0;
            if (p_player < 0 || p_player >= players.Count) return false;
            int teamIndex = players[p_player].m_team;
            Team team = p_world.m_teams[teamIndex];
            int leader = team.m_leader;
            if (leader < 0 || leader >= players.Count) return false;
            Mech leaderMech = players[leader].m_mech;
            Vector3Int at = leaderMech != null ? leaderMech.ObjectPosition : players[leader].m_position;
            int leaderHeading = leaderMech != null ? leaderMech.HeadingFixed : players[leader].m_heading;
            if (leader != p_player)
            {
                int slot = players[p_player].m_slot;
                if (slot < 0 || slot >= 8) slot = 0;
                long sin = Fixed.Sin(leaderHeading), cos = Fixed.Cos(leaderHeading);
                long x = team.m_x[slot], z = team.m_z[slot];
                p_x = (int)((((long)at.x << 16) + x * cos + z * sin) / 65536);
                p_z = (int)((((long)at.z << 16) - x * sin + z * cos) / 65536);
                p_heading = team.m_heading[slot];
            }
            else
            {
                p_x = at.x;
                p_z = at.z;
                p_heading = leaderHeading;
            }

            return true;
        }

        // AssignTeamSlots: the team's mechs still in play numbered from slot 1, the leader (chosen when it has none)
        // taking slot 0.
        public static bool AssignTeamSlots(World p_world, int p_team)
        {
            Team team = p_world.m_teams[p_team];
            int leader = GetTeamLeader(p_world, p_team);
            if (leader == -1)
            {
                leader = StarOrders.ChooseTeamLeader(p_team);
                if (leader == -1) return false;
            }

            int slot = 1;
            foreach (int index in team.m_members)
            {
                Player player = p_world.m_players[index];
                Mech mech = player.m_mech;
                int type = mech != null ? mech.m_type : 0;
                if (type == 1 && (player.m_flags & 6) == 0)
                {
                    if (player.m_index == leader) player.m_slot = 0;
                    else player.m_slot = slot++;
                }
            }

            return true;
        }

        // GetTeamFormation.
        public static int GetTeamFormation(World p_world, int p_team) =>
            p_team >= 0 && p_team < World.c_maxTeams ? p_world.m_teams[p_team].m_formation : 0;

        // SetTeamFormation: template p_formation's slots for the team (the local star hears it named).
        public static void SetTeamFormation(World p_world, int p_team, int p_formation)
        {
            if (p_team >= World.c_maxTeams || p_formation >= 32) return;
            if (p_team == p_world.m_localStar) StarOrders.SayFormation(p_formation);
            p_world.m_teams[p_team].m_formation = p_formation;
            if (p_formation >= 0 && p_formation < p_world.m_formations.Count)
            {
                Formation formation = p_world.m_formations[p_formation];
                Array.Copy(formation.m_x, p_world.m_teams[p_team].m_x, 8);
                Array.Copy(formation.m_z, p_world.m_teams[p_team].m_z, 8);
                Array.Copy(formation.m_heading, p_world.m_teams[p_team].m_heading, 8);
            }
            else
            {
                // g_formationTemplates past the count are zeroed.
                Array.Clear(p_world.m_teams[p_team].m_x, 0, 8);
                Array.Clear(p_world.m_teams[p_team].m_z, 0, 8);
                Array.Clear(p_world.m_teams[p_team].m_heading, 0, 8);
            }
        }

        // AddNavPoint: a nav "!" for player p_owner at (p_x, p_y, p_z) (flags 0x401, radius 3000, its team's). Its
        // index, or -1 with the table full.
        public static int AddNavPoint(World p_world, int p_owner, int p_x, int p_y, int p_z)
        {
            List<NavPoint> navs = p_world.m_navs;
            if (navs.Count >= World.c_maxNavs) return -1;
            NavPoint nav = new NavPoint
            {
                m_name = "!",
                m_position = new Vector3Int(p_x, p_y, p_z),
                m_used = 1,
                m_flags = 0x401,
                m_owner = p_owner | 0x200,
                m_team = p_owner >= 0 && p_owner < p_world.m_players.Count ? p_world.m_players[p_owner].m_team : 0,
                m_radius = 3000,
            };
            navs.Add(nav);
            return navs.Count - 1;
        }

        // RemoveNavPoint: nav p_nav (an AI target id, 0x100 | index) when player p_owner placed it; the later navs move
        // down, and so do the players' AI targets, goals and navs pointing past it, and their targets but the local
        // player's. The new nav count, or -1.
        public static int RemoveNavPoint(World p_world, int p_owner, int p_nav)
        {
            List<NavPoint> navs = p_world.m_navs;
            if ((p_nav & 0x100) == 0) return -1;
            int index = p_nav & 0xff;
            if (index >= navs.Count) return -1;
            if ((navs[index].m_flags & 1) == 0 || navs[index].m_owner != (p_owner | 0x200)) return -1;
            navs.RemoveAt(index);
            foreach (Player player in p_world.m_players)
            {
                Mech mech = player.m_mech;
                if (mech == null) continue;
                mech.ShiftNavIds(index, player.m_index != SimMain.g_localPlayerId);
            }

            return navs.Count;
        }
    }
}
