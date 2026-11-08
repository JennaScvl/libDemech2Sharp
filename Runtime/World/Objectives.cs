using UnityEngine;

namespace Demech2
{
    // The stars' missions as they play out (objective.c: UpdateObjectives, UpdateObjective, ObjectiveConditionsHold,
    // TestObjectiveCondition, GetObjectiveTargetState, HasTeamReachedTarget, IsTeamNearTarget, AnnounceObjective,
    // AnnounceMissionResult, GetTeamHomeTarget, RestartStarMission, ChooseNetworkWinner): each frame every star's
    // objectives whose conditions hold are checked (their targets destroyed, defended, inspected, reached; their time
    // up), finished ones are announced to the local star, the types 0x10000 and up end, toggle, fail, succeed or restart
    // other missions' objectives, and the mission succeeds once every mandatory objective did, fails once one failed, or
    // runs out of time. A star's current objective is the first whose conditions hold; when it changes the star's orders
    // are reset (ResetStarOrders). Times are mission seconds (g_missionTime, the clock / 181). The original's network
    // branches aren't ported; a networked match runs every star's objectives on every machine, every pilot reaching
    // navs as the local one does. The host may change the slots, the announcements and the targets (Host.Objectives).
    public static class Objectives
    {
        // g_missionTime, g_forceMissionSuccess (the "icanthackit" cheat).
        public static int g_missionTime;
        public static bool g_forceMissionSuccess;

        // g_objectiveTable[p_star], or null.
        public static MissionTable MissionOf(World p_world, int p_star) =>
            p_star >= 0 && p_star < World.c_maxTeams ? p_world.m_missions[p_star] : null;

        // g_currentObjective[p_star].
        public static int CurrentObjective(World p_world, int p_star) =>
            p_star >= 0 && p_star < World.c_maxTeams ? p_world.m_currentObjective[p_star] : -1;

        // The star's current objective (g_objectiveTable[star].m_objectives[g_currentObjective[star]]), or null.
        public static MissionObjective CurrentObjectiveOf(World p_world, int p_star)
        {
            MissionTable mission = MissionOf(p_world, p_star);
            int index = CurrentObjective(p_world, p_star);
            return mission != null && index >= 0 && index < mission.m_objectives.Count ? mission.m_objectives[index] : null;
        }

        // GetTeamHomeTarget: the target of the star's return objective (type 0x20), else its first objective's first.
        public static int GetTeamHomeTarget(World p_world, int p_team)
        {
            MissionTable mission = MissionOf(p_world, p_team);
            if (mission == null || mission.m_objectives.Count == 0) return 0;
            foreach (MissionObjective objective in mission.m_objectives)
            {
                if (objective.m_type == 0x20) return objective.m_targets[0];
            }

            return mission.m_objectives[0].m_targets[0];
        }

        // GetObjectiveTargetState: a player's or game thing's state bits (0xe); a nav's are 0.
        static int GetObjectiveTargetState(World p_world, int p_target)
        {
            int index = p_target & 0xff;
            switch (p_target & 0xff00)
            {
                case 0x200:
                    return index < p_world.m_players.Count ? p_world.m_players[index].m_flags & 0xe : 0;
                case 0x400:
                    return index < p_world.m_gameThings.Count ? p_world.m_gameThings[index].m_flags & 0xe : 0;
            }

            return 0;
        }

        // HasTeamReachedTarget: whether team p_team inspected the player or game thing, or reached the nav.
        static bool HasTeamReachedTarget(World p_world, int p_target, int p_team)
        {
            int index = p_target & 0xff;
            int bit = 1 << p_team;
            switch (p_target & 0xff00)
            {
                case 0x200:
                    if (index >= p_world.m_players.Count) return false;
                    Mech mech = p_world.m_players[index].m_mech;
                    return (p_world.m_players[index].m_flags & 0x20) != 0 && mech != null && (bit & mech.m_inspectedBy) != 0;
                case 0x400:
                    return index < p_world.m_gameThings.Count && (p_world.m_gameThings[index].m_flags & 0x20) != 0 &&
                           (bit & p_world.m_gameThings[index].m_teamsReached) != 0;
                case 0x100:
                    return index < p_world.m_navs.Count && (p_world.m_navs[index].m_flags & 0x20) != 0 &&
                           (bit & p_world.m_navs[index].m_teamsReached) != 0;
            }

            return false;
        }

        // IsTeamNearTarget: whether a member of team p_team (a live one, for a player target) is within 20000 of the
        // target (a nav's radius, when it has one). The local player reaching a nav while running marks it reached.
        static bool IsTeamNearTarget(World p_world, int p_target, int p_team)
        {
            if (Host.Objectives.TeamNearTarget(p_target, p_team, out bool near)) return near;
            if (Teams.GetTeamLeader(p_world, p_team) < 0) return false;
            int index = p_target & 0xff;
            switch (p_target & 0xff00)
            {
                case 0x200:
                {
                    if (index >= p_world.m_players.Count) return false;
                    Vector3Int at = PositionOf(p_world, index);
                    foreach (Player p in p_world.m_players)
                    {
                        if (p.m_team == p_team && (p.m_flags & 6) == 0 && IsWithin(PositionOf(p_world, p.m_index) - at, 20000)) return true;
                    }

                    return false;
                }
                case 0x400:
                {
                    if (index >= p_world.m_gameThings.Count) return false;
                    Vector3Int at = Host.World.ThingPosition(index);
                    foreach (Player p in p_world.m_players)
                    {
                        if (p.m_team == p_team && IsWithin(PositionOf(p_world, p.m_index) - at, 20000)) return true;
                    }

                    return false;
                }
                case 0x100:
                {
                    if (index >= p_world.m_navs.Count) return false;
                    NavPoint nav = p_world.m_navs[index];
                    int radius = nav.m_radius;
                    if (radius <= 0) radius = 20000;
                    foreach (Player p in p_world.m_players)
                    {
                        if (p.m_team != p_team || !IsWithin(PositionOf(p_world, p.m_index) - nav.m_position, radius)) continue;
                        // A networked host: a remote player reaches it too (the sound is the local player's).
                        if (SimMain.IsPilot(p.m_index) && (p.m_flags & 0x2000) != 0 && (nav.m_flags & 0x20) == 0 && nav.m_used != 0)
                        {
                            nav.m_flags |= 0x20;
                            nav.m_teamsReached |= 1 << p_team;
                            if (p.m_index == SimMain.g_localPlayerId) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xe7), 100, 0x40, 5, 0x50);
                        }

                        return true;
                    }

                    return false;
                }
            }

            return false;
        }

        // Player::m_position: the mech's object, or the gpspec's place without one.
        public static Vector3Int PositionOf(World p_world, int p_index)
        {
            Mech mech = p_world.m_players[p_index].m_mech;
            return mech != null ? mech.ObjectPosition : p_world.m_players[p_index].m_position;
        }

        // IsWithinRadius (inline): the squares, exactly (whole units).
        public static bool IsWithin(Vector3Int p_d, long p_radius) =>
            (long)p_d.x * p_d.x + (long)p_d.y * p_d.y + (long)p_d.z * p_d.z <= p_radius * p_radius;

        // AnnounceObjective: the local star's objective p_objective, once: "<name> successful" (p_state 5) or "failed"
        // (6) with its sound (or the host's, Host.Objectives.Announce; p_entry a slot past the table's objectives).
        static void AnnounceObjective(World p_world, int p_star, int p_objective, int p_state, MissionObjective p_entry = null)
        {
            if (p_star != p_world.m_localStar || p_objective < 0 || p_objective >= p_world.m_objectiveAnnounced.Length ||
                p_world.m_objectiveAnnounced[p_objective]) return;
            MissionObjective objective = p_entry ?? Host.Objectives.Slot(p_world, p_star, p_objective);
            if (Host.Objectives.Announce(objective, p_state)) { }
            else if (p_state == 5) MissionSpeech.SayLine(objective.m_successSound);
            else if (p_state == 6) MissionSpeech.SayLine(objective.m_failSound);
            p_world.m_objectiveAnnounced[p_objective] = true;
        }

        // ChooseNetworkWinner (objective.c 0x1001b21a): in a network game that isn't a team game (a networked match
        // without teams), a mission whose listed objectives come to exactly one and include a listed secondary
        // (priority 2) is a deathmatch: every player 0-7 is scored (kills of others minus kills of itself, empty
        // players' zeros counted as written), the best is the winner, a tie with the best -2. KillMech's, after each
        // kill counted.
        public static void ChooseNetworkWinner(World p_world)
        {
            int best = -0x7fff;
            bool deathmatch = false;
            MissionTable mission = MissionOf(p_world, p_world.m_localStar);
            if (!Host.Net.Active || !Host.Net.FreeForAll || mission == null) return;
            int listed = 0;
            bool secondary = false;
            for (int i = 0; i < mission.m_objectives.Count; i++)
            {
                MissionObjective objective = mission.m_objectives[i];
                listed += objective.m_listed ? 1 : 0;
                if (objective.m_listed && objective.m_priority == 2) secondary = true;
            }

            if (secondary && listed == 1) deathmatch = true;
            if (!deathmatch) return;
            int[,] kills = Host.World.CareerKills;
            int winner = 0;
            for (int player = 0; player < 8; player++)
            {
                int score = 0;
                for (int victim = 0; victim < 8; victim++)
                {
                    if (player == victim) score -= kills[player, victim];
                    else score += kills[player, victim];
                }

                if (score > best)
                {
                    best = score;
                    winner = player;
                }
                else if (score == best) winner = -2;
            }

            Host.World.CareerWinner = winner;
        }

        // AnnounceMissionResult: the local star's result: successful (2), failed (3) or out of time (4).
        static void AnnounceMissionResult(World p_world, int p_star, int p_status)
        {
            if (p_star != p_world.m_localStar) return;
            if (Host.Objectives.AnnounceMissionResult(p_world, p_star, p_status)) return;

            // g_missionResultAnnounced: in a network game (a networked match), only the first result.
            if (p_world.m_missionResultAnnounced && Host.Net.Active) return;
            p_world.m_missionResultAnnounced = true;

            if (p_status == 2)
            {
                MissionSpeech.SayLine(p_world.m_missions[p_star].m_successSound);
                // A network game's success: the local player is the winner, and the others are told (SendSuccessMsg).
                if (Host.Net.Active)
                {
                    Host.World.CareerWinner = SimMain.g_localPlayerId;
                    Host.Net.SendSuccessMsg();
                }
            }
            else if (p_status == 3) MissionSpeech.SayLine(p_world.m_missions[p_star].m_failSound);
            else if (p_status == 4) MissionSpeech.SayLine("BET68");
        }

        // TestObjectiveCondition: condition p_condition (1 the other objective is done, 2 successful, 3 failed).
        static bool TestObjectiveCondition(World p_world, int p_star, int p_objective, int p_condition)
        {
            MissionObjective objective = p_world.m_missions[p_star].m_objectives[p_objective];
            int kind = objective.m_conditionKinds[p_condition];
            int state = ObjectiveState(p_world, objective.m_conditionStars[p_condition], objective.m_conditionObjectives[p_condition]);
            return (kind == 1 && (state == 5 || state == 6)) || (kind == 2 && state == 5) || (kind == 3 && state == 6);
        }

        // The state of star p_star's objective p_objective (the original reads past the objectives as zeros).
        static int ObjectiveState(World p_world, int p_star, int p_objective)
        {
            MissionTable mission = MissionOf(p_world, p_star);
            // A slot past the objectives the host may have written.
            if (Host.Objectives.SpareState(mission, p_objective, out int spare)) return spare;
            return mission != null && p_objective >= 0 && p_objective < mission.m_objectives.Count ? mission.m_objectives[p_objective].m_state : 0;
        }

        // ObjectiveConditionsHold: one of the conditions, or every one; never a finished objective's, always a Begin's
        // (type 0x10). p_frame: called by UpdateObjectives, its loop counter left with the host (LoopEnded).
        public static bool ObjectiveConditionsHold(World p_world, int p_star, int p_objective, bool p_frame = false)
        {
            MissionObjective objective = p_world.m_missions[p_star].m_objectives[p_objective];
            bool result = true;
            if (objective.m_state == 5 || objective.m_state == 6) return false;
            if (objective.m_type == 0x10) return true;
            int i = 0;
            bool decided = false;
            for (; i < 8; i++)
            {
                if (objective.m_conditionKinds[i] == 0) break;
                bool holds = TestObjectiveCondition(p_world, p_star, p_objective, i);
                if (!objective.m_allConditions)
                {
                    if (holds)
                    {
                        decided = true;
                        break;
                    }
                }
                else
                {
                    result &= holds;
                    if (!result)
                    {
                        decided = true;
                        break;
                    }
                }
            }

            if (p_frame) Host.Objectives.LoopEnded(p_world, i);
            if (decided) return !objective.m_allConditions;
            return objective.m_allConditions;
        }

        // UpdateObjective: star p_star's objective p_objective while its conditions hold: whether its targets are done
        // (by its type), whether its time is up, and what it does when done. Its state: 5 successful, 6 failed, 8
        // failed for another star, 3 still going.
        static void UpdateObjective(World p_world, int p_star, int p_objective)
        {
            MissionTable mission = p_world.m_missions[p_star];
            MissionObjective objective = mission.m_objectives[p_objective];
            bool done = true, untouched = true, expired;
            int state = 3;
            // The loop counter (0x400000 reads it): 0, or the host's (LoopStart).
            int i = Host.Objectives.LoopStart(p_world);

            // A network game: a successful destroy objective (type 2) whose targets aren't all destroyed again (a
            // player regenerated) is under way again.
            if (Host.Net.Active && objective.m_state == 5 && objective.m_type == 2)
            {
                bool still = true;
                for (int t = 0; t < objective.m_targetCount; t++) still = still && GetObjectiveTargetState(p_world, objective.m_targets[t]) != 0;
                if (!still)
                {
                    objective.m_startTime = -1;
                    objective.m_endTime = -1;
                    objective.m_state = 3;
                }
            }

            if (objective.m_state == 5 || objective.m_state == 6 || !ObjectiveConditionsHold(p_world, p_star, p_objective))
            {
                objective.m_active = false;
                return;
            }

            if (objective.m_startTime == -1)
            {
                objective.m_startTime = g_missionTime;
                objective.m_active = true;
            }

            switch (objective.m_type)
            {
                case 1:
                case 2:
                    for (i = 0; i < objective.m_targetCount; i++) done = done && GetObjectiveTargetState(p_world, objective.m_targets[i]) != 0;
                    break;
                case 4:
                    for (i = 0; i < objective.m_targetCount; i++)
                    {
                        bool alive = GetObjectiveTargetState(p_world, objective.m_targets[i]) != 0;
                        done = done && !alive;
                        untouched = untouched && alive;
                    }

                    break;
                case 8:
                    for (i = 0; i < objective.m_targetCount; i++) done = done && HasTeamReachedTarget(p_world, objective.m_targets[i], p_star);
                    break;
                case 0x20:
                    for (i = 0; i < objective.m_targetCount; i++) done = done && HasTeamReachedTarget(p_world, objective.m_targets[i], p_star);
                    for (i = 0; i < mission.m_objectives.Count; i++)
                    {
                        if (mission.m_objectives[i].m_mandatory && i != p_objective) done = done && mission.m_objectives[i].m_state == 5;
                    }

                    break;
                case 0x100:
                    // A networked host: a star with a player in it goes as the local star.
                    if (!SimMain.IsPilotStar(p_star)) done = done && p_world.m_currentObjective[p_star] == p_objective;
                    for (i = 0; i < objective.m_targetCount; i++) done = done && IsTeamNearTarget(p_world, objective.m_targets[i], p_star);
                    break;
                case 0x2000:
                    for (i = 0; i < objective.m_targetCount; i++) done = done && !IsTeamNearTarget(p_world, objective.m_targets[i], p_star);
                    break;
            }

            Host.Objectives.LoopEnded(p_world, i);
            expired = objective.m_timeLimit >= 0 && objective.m_startTime >= 0 && g_missionTime - objective.m_startTime > objective.m_timeLimit;

            switch (objective.m_type)
            {
                case 0x10000:
                {
                    if (objective.m_targetStar > p_world.m_objectiveCount) break;
                    MissionTable won = Host.Objectives.TargetMissionOf(p_world, objective);
                    if (won == null) break;
                    won.m_status = 2;
                    AnnounceMissionResult(p_world, objective.m_targetStar, won.m_status);
                    won.m_endTime = g_missionTime;
                    state = 5;
                    AnnounceObjective(p_world, p_star, p_objective, state);
                    break;
                }
                case 0x20000:
                {
                    if (objective.m_targetStar > p_world.m_objectiveCount) break;
                    MissionTable lost = Host.Objectives.TargetMissionOf(p_world, objective);
                    if (lost == null) break;
                    lost.m_status = 3;
                    AnnounceMissionResult(p_world, objective.m_targetStar, lost.m_status);
                    lost.m_endTime = g_missionTime;
                    state = 5;
                    AnnounceObjective(p_world, p_star, p_objective, state);
                    break;
                }
                case 0x40000:
                {
                    MissionObjective other = Host.Objectives.TargetObjectiveOf(p_world, objective);
                    if (other == null) break;
                    other.m_listed = !other.m_listed;
                    state = 5;
                    AnnounceObjective(p_world, p_star, p_objective, state);
                    break;
                }
                case 0x80000:
                {
                    MissionObjective other = Host.Objectives.TargetObjectiveOf(p_world, objective);
                    if (other == null || other.m_state == 6 || other.m_state == 5) break;
                    other.m_state = 6;
                    other.m_endTime = g_missionTime;
                    AnnounceObjective(p_world, objective.m_targetStar, objective.m_targetObjective, other.m_state, other);
                    state = 5;
                    AnnounceObjective(p_world, p_star, p_objective, state);
                    break;
                }
                case 0x200000:
                {
                    if (objective.m_targetStar > p_world.m_objectiveCount) break;
                    MissionTable restarted = Host.Objectives.TargetMissionOf(p_world, objective);
                    if (restarted == null) break;
                    for (int j = 0; j < restarted.m_objectives.Count; j++)
                    {
                        MissionObjective reset = restarted.m_objectives[j];
                        reset.m_state = 3;
                        reset.m_startTime = -1;
                        reset.m_endTime = -1;
                        if (reset.m_type != 0x10 && j < p_world.m_objectiveAnnounced.Length) p_world.m_objectiveAnnounced[j] = false;
                    }

                    restarted.m_status = 0;
                    restarted.m_endTime = -1;
                    state = 5;
                    AnnounceObjective(p_world, p_star, p_objective, state);
                    break;
                }
                case 0x400000:
                {
                    MissionObjective other = Host.Objectives.TargetObjectiveOf(p_world, objective);
                    if (other == null) break;
                    other.m_state = 3;
                    other.m_startTime = -1;
                    other.m_endTime = -1;
                    // The original clears the announcement of objective i, the last loop's counter (which no loop of
                    // this type sets: here 0, or the host's LoopStart).
                    if (other.m_type != 0x10)
                    {
                        if (Host.Objectives.ClearAnnounced(p_world, i)) { }
                        else if (i >= 0 && i < p_world.m_objectiveAnnounced.Length) p_world.m_objectiveAnnounced[i] = false;
                    }
                    state = 5;
                    AnnounceObjective(p_world, p_star, p_objective, state);
                    break;
                }
                case 0x100000:
                {
                    MissionObjective other = Host.Objectives.TargetObjectiveOf(p_world, objective);
                    if (other == null || other.m_state == 6 || other.m_state == 5) break;
                    other.m_state = 5;
                    other.m_endTime = g_missionTime;
                    AnnounceObjective(p_world, objective.m_targetStar, objective.m_targetObjective, other.m_state, other);
                    state = 5;
                    AnnounceObjective(p_world, p_star, p_objective, state);
                    break;
                }
                case 0:
                case 0x200:
                case 0x400:
                case 0x800:
                    if (expired)
                    {
                        // A network game: not while the star's leader is destroyed (flag 2).
                        if (Host.Net.Active)
                        {
                            int leader = Teams.GetTeamLeader(p_world, p_star);
                            if (leader >= 0 && leader < p_world.m_players.Count && (p_world.m_players[leader].m_flags & 2) != 0) return;
                        }

                        state = 5;
                        AnnounceObjective(p_world, p_star, p_objective, state);
                    }

                    break;
                case 4:
                    if (expired)
                    {
                        if (done)
                        {
                            state = 5;
                            AnnounceObjective(p_world, p_star, p_objective, state);
                        }
                        else state = 6;
                    }
                    else
                    {
                        if (!done)
                        {
                            state = p_world.m_localStar == p_star ? 6 : 8;
                            AnnounceObjective(p_world, p_star, p_objective, 6);
                        }

                        if (untouched) state = 6;
                    }

                    break;
                default:
                    if (done)
                    {
                        state = 5;
                        AnnounceObjective(p_world, p_star, p_objective, state);
                    }
                    else if (expired)
                    {
                        state = 6;
                        AnnounceObjective(p_world, p_star, p_objective, state);
                    }

                    break;
            }

            objective.m_state = state;
            if (state != 3) objective.m_endTime = g_missionTime;
        }

        // RestartStarMission (objective.c, 0x1001cc5c): star p_star's mission over (a network game's regenerated
        // player): every objective back to state 3 without times; a completed type 0x40000 toggles its target's
        // listing again.
        public static void RestartStarMission(World p_world, int p_star)
        {
            g_missionTime = Clock.g_currentClock / 181;
            MissionTable mission = MissionOf(p_world, p_star);
            if (mission == null) return;
            foreach (MissionObjective objective in mission.m_objectives)
            {
                if (objective.m_type == 0x40000 && objective.m_state == 5)
                {
                    MissionObjective other = TargetObjectiveOf(p_world, objective);
                    if (other != null) other.m_listed = !other.m_listed;
                }

                objective.m_state = 3;
                objective.m_startTime = -1;
                objective.m_endTime = -1;
            }

            mission.m_status = 0;
            mission.m_startTime = g_missionTime;
            mission.m_endTime = -1;
        }

        // The objective a type 0x40000 and up objective acts on, or null past the table (the original's bounds test
        // lets the count itself through: that objective is an empty one).
        public static MissionObjective TargetObjectiveOf(World p_world, MissionObjective p_objective)
        {
            if (p_objective.m_targetStar > p_world.m_objectiveCount) return null;
            MissionTable mission = MissionOf(p_world, p_objective.m_targetStar);
            if (mission == null || mission.m_objectives.Count < p_objective.m_targetObjective) return null;
            return p_objective.m_targetObjective >= 0 && p_objective.m_targetObjective < mission.m_objectives.Count
                ? mission.m_objectives[p_objective.m_targetObjective]
                : null;
        }

        // UpdateObjectives: every star's objectives, then while its mission is in progress whether it succeeded (every
        // mandatory objective done), failed or ran out of time; the local star's finished mission resolves the mission
        // once the speech is done. Then its current objective, the first whose conditions hold (not of a type 0x10000
        // and up); a change resets the star's orders.
        public static void UpdateObjectives(World p_world)
        {
            g_missionTime = Clock.g_currentClock / 181;
            for (int star = 0; star < p_world.m_objectiveCount; star++)
            {
                // A star without a mission table has an empty one.
                MissionTable mission = p_world.m_missions[star] ??= new MissionTable();
                for (int i = 0; i < mission.m_objectives.Count; i++) UpdateObjective(p_world, star, i);

                if (mission.m_status == 0)
                {
                    bool success = true, failed = false;
                    foreach (MissionObjective objective in mission.m_objectives)
                    {
                        if (!objective.m_mandatory) continue;
                        switch (objective.m_state)
                        {
                            case 5:
                                break;
                            case 6:
                            case 8:
                                success = false;
                                failed = true;
                                break;
                            default:
                                success = false;
                                break;
                        }
                    }

                    if (success || g_forceMissionSuccess)
                    {
                        mission.m_status = 2;
                        AnnounceMissionResult(p_world, star, mission.m_status);
                        mission.m_endTime = g_missionTime;
                    }
                    else if (failed)
                    {
                        mission.m_status = 3;
                        AnnounceMissionResult(p_world, star, mission.m_status);
                        mission.m_endTime = g_missionTime;
                    }
                    else if (mission.m_timeLimit > 0 && g_missionTime - mission.m_startTime >= mission.m_timeLimit)
                    {
                        mission.m_status = 4;
                        AnnounceMissionResult(p_world, star, mission.m_status);
                        mission.m_endTime = g_missionTime;
                    }
                }
                else if (star == p_world.m_localStar && Host.World.MissionResolvable)
                {
                    Host.World.ResolveMission();
                }

                int current = -1;
                for (int i = 0; i < mission.m_objectives.Count; i++)
                {
                    if (ObjectiveConditionsHold(p_world, star, i, true) && (mission.m_objectives[i].m_type & unchecked((int)0xffff0000)) == 0)
                    {
                        current = i;
                        break;
                    }
                }

                if (p_world.m_currentObjective[star] != current)
                {
                    p_world.m_currentObjective[star] = current;
                    Host.World.DebugDumpMechs($"ResetStarOrders star {star} -> objective {current}");
                    StarOrders.ResetStarOrders(star);
                    // ResetStarOrders' push of the star (the host's loop word).
                    Host.Objectives.LoopEnded(p_world, star);
                }
            }
        }
    }
}
