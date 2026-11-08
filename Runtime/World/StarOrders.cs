using System.Collections.Generic;

namespace Demech2
{
    // The stars' orders (ai.c: FirstAI, LeadStar, ResetStarOrders, AssignStarObjective,
    // HandleStarOrder, AssignStarTarget, GetEngagementAIFlags, GetTeamMembers, ChooseTeamLeader, RetargetGoals,
    // FindStarSlotPlayer, OrderStarSlot, GetLocalStarSize; speech.c: SayLancemateReport, SayFormation):
    // each star's leader gives its AI members the script and state of the star's current objective, sends an idle member
    // after the first target of the objective that isn't covered yet, and passes on what its members report (an enemy in
    // range: one member goes after it). The local player orders its own star's members.
    public static class StarOrders
    {
        // g_localStarAssigned: the local player's star got its first objective's orders (each pilot-led star's).
        static readonly HashSet<int> s_pilotStarsAssigned = new HashSet<int>();

        // Not the original's: the pilot giving the order RunOrderFor runs (a lockstep command), else the local player.
        public static int s_orderer = -1;
        static int Orderer => s_orderer >= 0 ? s_orderer : SimMain.g_localPlayerId;

        // FirstAI: every star's slots numbered and its orders given (once the mechs are set up).
        public static void FirstAI()
        {
            s_pilotStarsAssigned.Clear();
            World world = SimMain.g_world;
            if (world == null) return;
            for (int i = 0; i < world.m_objectiveCount; i++)
            {
                Teams.AssignTeamSlots(world, i);
                ResetStarOrders(i);
            }
        }

        // GetTeamMembers: the star's members in play: not destroyed (flags 6), and without p_aliveOnly not shut down
        // (0x10) either, as the original's mask has it.
        public static List<Mech> GetTeamMembers(int p_team, bool p_aliveOnly)
        {
            var members = new List<Mech>();
            World world = SimMain.g_world;
            if (world == null) return members;
            int mask = 6;
            if (!p_aliveOnly) mask |= 0x10;
            foreach (int index in Teams.TeamMembers(world, p_team))
            {
                Mech mech = SimMain.MechOf(index);
                Player player = SimMain.PlayerOf(index);
                if (mech != null && player != null && (mask & player.m_flags) == 0) members.Add(mech);
            }

            return members;
        }

        // ChooseTeamLeader: the first able member other than the leader leads (none: -1). Returns it.
        public static int ChooseTeamLeader(int p_team)
        {
            World world = SimMain.g_world;
            int leader = Teams.GetTeamLeader(world, p_team);
            int newLeader = -1;
            List<Mech> members = GetTeamMembers(p_team, true);
            if (members.Count != 0)
            {
                foreach (Mech member in members)
                {
                    if (member.m_player.m_index == leader) continue;
                    if ((member.PlayerFlags & 6) == 0)
                    {
                        newLeader = member.m_player.m_index;
                        break;
                    }
                }

                Teams.SetTeamLeader(world, p_team, newLeader);
            }

            return newLeader;
        }

        // RetargetGoals: the star's members whose goal is player p_index go after player p_target instead.
        public static void RetargetGoals(int p_team, int p_index, int p_target)
        {
            for (int i = 0; i < SimMain.PlayerCount; i++)
            {
                Mech mech = SimMain.MechOf(i);
                if (mech == null || mech.m_player.m_team != p_team) continue;
                if ((mech.m_aiGoal & 0xf000) == 0 && (mech.m_aiGoal & Mech.c_aiTargetPlayer) != 0 && (mech.m_aiGoal & 0xff) == p_index)
                {
                    mech.m_aiGoal = (ushort)(p_target | Mech.c_aiTargetPlayer);
                }
            }
        }

        // ResetStarOrders: the star's orders for its current objective.
        public static void ResetStarOrders(int p_team) => HandleStarOrder(p_team, 0, 0);

        // HandleStarOrder: an order posted to the star's leader (M_TGTABLE: attack its target), or with none the
        // objective's orders.
        static void HandleStarOrder(int p_team, int p_message, int p_target)
        {
            if (p_message != 0)
            {
                if (p_message == 7) AssignStarTarget(p_team, p_target);
            }
            else AssignStarObjective(p_team);
        }

        // GetEngagementAIFlags: an objective's engagement as AI flags (1: hold fire, 2: hold).
        static int GetEngagementAIFlags(int p_value) => p_value == 1 ? 2 : p_value == 2 ? 1 : 0;

        // AssignStarObjective: the star's AI members get the script of its current objective's type (as leader or
        // follower) and its state: rest, shut down (and Begin objectives too), or follow the leader (the leader idles);
        // the objective's engagement flags. The local player's star only once, but for its Begin objective.
        static void AssignStarObjective(int p_team)
        {
            World world = SimMain.g_world;
            int leader = Teams.GetTeamLeader(world, p_team);
            if (leader == -1)
            {
                leader = ChooseTeamLeader(p_team);
                if (leader == -1) return;
            }

            List<Mech> members = GetTeamMembers(p_team, true);
            if (members.Count == 0) return;
            // No current objective: 0x200; an objective past the table (a star without one) is zeros.
            MissionObjective objective = Objectives.CurrentObjectiveOf(world, p_team);
            int type = Objectives.CurrentObjective(world, p_team) == -1 ? 0x200 : objective != null ? (ushort)objective.m_type : 0;

            int state;
            if (type == 0x400) state = Mech.c_aiStateRest;
            else if (type == 0x800 || type == 0x10) state = Mech.c_aiStateShutdown;
            else state = Mech.c_aiStateFollow;

            // The original reads the objective's engagement even without a current objective (index -1: the mission's
            // own fields before its first objective, here 0).
            int flags = 0x40 | GetEngagementAIFlags(objective != null ? objective.m_engagement : 0);
            int bit;
            for (bit = 0; bit < 16 && ((1 << bit) & type) == 0; bit++)
            {
            }

            foreach (Mech member in members)
            {
                if (member.m_player.m_aiMode != 2) continue;
                member.SetAIScript(2, bit + 1, leader);
                if (SimMain.IsPilot(leader) && s_pilotStarsAssigned.Contains(p_team)) continue;
                if (member.m_player.m_index == leader && state == Mech.c_aiStateFollow) member.QueueAIState(Mech.c_aiStateIdle, 0);
                else member.QueueAIState(state, 2);
                member.m_aiFlags &= ~3;
                member.m_aiFlags |= flags;
                member.ReleaseAnchorNav();
            }

            if (SimMain.IsPilot(Teams.GetTeamLeader(world, p_team)) && type != 0x10) s_pilotStarsAssigned.Add(p_team);
        }

        // AssignStarTarget: members of the star after p_target: on objective 4 every free member up to the objective's
        // count, else the free member (not the leader) nearest the target's value, or the leader; sent by the leader
        // (0x20). Whether one went.
        static bool AssignStarTarget(int p_team, int p_target)
        {
            World world = SimMain.g_world;
            int slot = 0;
            if ((p_target & 0xf000) != 0) return false;
            int leader = Teams.GetTeamLeader(world, p_team);
            if (leader == -1) return false;
            List<Mech> members = GetTeamMembers(p_team, true);
            if (members.Count == 0) return false;
            if ((p_target & 0xf00) == Mech.c_aiTargetPlayer)
            {
                Mech target = SimMain.MechOf(p_target & 0xff);
                slot = target != null ? target.m_aiValue : 0;
            }

            if (Objectives.CurrentObjective(world, p_team) == 4)
            {
                bool result = false;
                MissionObjective objective = Objectives.CurrentObjectiveOf(world, p_team);
                int required = objective != null ? objective.m_requiredCount : 0;
                if (required == 0) required = members.Count;
                for (int i = 0; i < members.Count && required != 0; i++)
                {
                    Mech member = members[i];
                    if (member.m_aiState != Mech.c_aiStateTarget && member.m_aiState != Mech.c_aiStateAttack && !member.IsOutOfAmmo() &&
                        member.m_engageAtWill != 0)
                    {
                        member.SetAIState(Mech.c_aiStateTarget, p_target, true);
                        result = true;
                    }
                }

                return result;
            }

            int chosen = -1, nearest = 0x7fff;
            foreach (Mech member in members)
            {
                if (member.m_player.m_index != leader && (member.m_aiFlags & 3) == 0 && member.m_aiState != Mech.c_aiStateTarget &&
                    member.m_aiState != Mech.c_aiStateAttack && !member.IsOutOfAmmo() && member.m_engageAtWill != 0)
                {
                    int diff = System.Math.Abs(member.m_aiValue - slot);
                    if (diff < nearest)
                    {
                        nearest = diff;
                        chosen = member.m_player.m_index;
                    }
                }
            }

            Mech sent;
            if (chosen != -1) sent = SimMain.MechOf(chosen);
            else
            {
                sent = SimMain.MechOf(leader);
                if (sent != null && ((sent.m_aiFlags & 3) != 0 || sent.m_aiState == Mech.c_aiStateTarget || sent.m_aiState == Mech.c_aiStateAttack ||
                                     sent.IsOutOfAmmo() || sent.m_engageAtWill == 0))
                {
                    sent = null;
                }
            }

            if (sent == null) return false;
            sent.SetAIState(Mech.c_aiStateTarget, p_target, true);
            sent.m_aiFlags |= 0x20;
            sent.m_aiFlags &= ~0x40;
            return true;
        }

        // LeadStar: run by the star's leader: its posted order handed on; then (not for the local player's star) an idle
        // member (or one in formation with ammunition, or the idle leader) sent after the first target of the star's
        // objective that isn't done and that too few members cover, with the objective's script, state and flags (a nuke
        // objective sets one off).
        public static void LeadStar(int p_team)
        {
            World world = SimMain.g_world;
            int leader = Teams.GetTeamLeader(world, p_team);
            if (leader == -1) return;
            Mech leaderMech = SimMain.MechOf(leader);
            if (leaderMech == null) return;
            if (leaderMech.m_postedMessage != 0)
            {
                HandleStarOrder(p_team, leaderMech.m_postedMessage, leaderMech.m_postedTarget);
                leaderMech.m_postedMessage = 0;
            }

            if (SimMain.IsPilot(leader)) return;
            int index = Objectives.CurrentObjective(world, p_team);
            if (index == -1) return;
            MissionObjective objective = Objectives.CurrentObjectiveOf(world, p_team);
            if (objective == null) return;
            int type = objective.m_type;
            if (type == 0 || (type & 0xe10) != 0) return;
            List<Mech> members = GetTeamMembers(p_team, false);
            if (members.Count == 0) return;

            int target = 0;
            for (int i = 0; i < objective.m_targetCount && target == 0; i++)
            {
                int remaining = objective.m_requiredCount;
                if (remaining == 0) remaining = members.Count;
                target = (ushort)objective.m_targets[i];
                if (Mech.IsTargetDone(target, type))
                {
                    target = 0;
                    continue;
                }

                for (int j = 0; j < members.Count && remaining != 0; j++)
                {
                    Mech member = members[j];
                    if (member.m_aiGoal != target) continue;
                    switch (type)
                    {
                        case 0x1000:
                            remaining--;
                            break;
                        case 1:
                        case 2:
                            if (member.m_aiState == Mech.c_aiStateTarget || member.m_aiState == Mech.c_aiStateAttack) remaining--;
                            break;
                        case 4:
                            if (member.m_aiState == Mech.c_aiStatePatrol) remaining--;
                            break;
                        case 8:
                            if (member.m_aiState == Mech.c_aiStateTarget) remaining--;
                            break;
                        case 0x20:
                        case 0x100:
                        case 0x200:
                        case 0x400:
                            if (member.m_aiState == Mech.c_aiStateGoDirect) remaining--;
                            break;
                        case 0x2000:
                            if (member.m_aiState == Mech.c_aiStateFlee) remaining--;
                            break;
                    }
                }

                if (remaining == 0) target = 0;
            }

            if (target == 0) return;

            Mech sent = null;
            foreach (Mech member in members)
            {
                if ((member.m_aiState == Mech.c_aiStateIdle || (member.m_aiState == Mech.c_aiStateFollow && !member.IsOutOfAmmo())) &&
                    leaderMech != member)
                {
                    sent = member;
                    break;
                }
            }

            if (sent == null)
            {
                if (leaderMech.m_aiState == 0 && !leaderMech.IsOutOfAmmo()) sent = leaderMech;
                else return;
            }

            for (int i = 0; i < 16; i++)
            {
                if ((type & (1 << i)) != 0)
                {
                    sent.SetAIScript(2, i + 1, Teams.GetTeamLeader(world, p_team));
                    break;
                }
            }

            int flags = 0x40 | GetEngagementAIFlags(objective.m_engagement);
            int state = -1;
            switch (type)
            {
                case 0x1000:
                    state = Mech.c_aiStateIdle;
                    Effects.StartNuke(sent.ObjectPosition);
                    break;
                case 1:
                case 2:
                    state = Mech.c_aiStateTarget;
                    break;
                case 4:
                    state = Mech.c_aiStatePatrol;
                    break;
                case 8:
                    state = Mech.c_aiStateTarget;
                    break;
                case 0x20:
                case 0x100:
                case 0x200:
                case 0x400:
                    state = Mech.c_aiStateGoDirect;
                    break;
                case 0x2000:
                    state = Mech.c_aiStateFlee;
                    break;
            }

            if (state != -1)
            {
                sent.QueueAIState(state, target);
                sent.m_aiFlags |= flags;
            }
        }

        // FindStarSlotPlayer: the local player's star mate in formation slot p_slot, or -1.
        public static int FindStarSlotPlayer(int p_slot)
        {
            Player local = SimMain.PlayerOf(Orderer);
            if (local == null) return -1;
            for (int i = 0; i < SimMain.PlayerCount; i++)
            {
                Player player = SimMain.PlayerOf(i);
                if (player.m_team == local.m_team && player.m_slot == p_slot) return i;
            }

            return -1;
        }

        // GetLocalStarSize: the local player's star's members.
        public static int GetLocalStarSize()
        {
            Player local = SimMain.PlayerOf(Orderer);
            return local != null && SimMain.g_world != null ? Teams.TeamMembers(SimMain.g_world, local.m_team).Count : 0;
        }

        // OrderStarSlot: the local player's command p_command to formation slot p_slot (slot 0: the whole star), and the
        // mate says it: attack the local player's target (not a nav), follow, patrol the target, go home, shut down, or
        // toggle engaging at will.
        public static bool OrderStarSlot(int p_slot, int p_command)
        {
            int speech = -1;
            Mech local = SimMain.MechOf(Orderer);
            int index = FindStarSlotPlayer(p_slot);
            if (index == -1 || local == null) return false;

            int target = 0x1000;
            switch (p_command)
            {
                case Mech.c_aiStateTarget:
                    if (local.m_targetInfo.m_target != 0) target = (short)local.m_targetInfo.m_target;
                    if ((target & Mech.c_aiTargetNav) != 0)
                    {
                        target = -1;
                        speech = 0;
                    }

                    speech = 1;
                    break;
                case Mech.c_aiStateFollow:
                    target = Mech.c_aiTargetMyLeader;
                    speech = 2;
                    break;
                case Mech.c_aiStatePatrol:
                    if (local.m_targetInfo.m_target != 0) target = (short)local.m_targetInfo.m_target;
                    speech = 3;
                    break;
                case Mech.c_aiStateGoDirect:
                    target = Mech.c_aiTargetHome;
                    speech = 4;
                    break;
                case Mech.c_aiStateShutdown:
                    target = 0;
                    speech = 9;
                    break;
                case Mech.c_aiStateAttack:
                    target = 0;
                    break;
            }

            if ((target & 0x1000) != 0) return false;
            if (index == Orderer) local.OrderPlayers(Mech.c_aiTargetFriendly, p_command, target);
            else local.OrderPlayers(p_slot | Mech.c_aiTargetPlayer, p_command, target);
            if (speech != -1 && local.IsLocal) SayLancemateReport(speech, p_slot);
            return true;
        }

        // g_slotSpeech, g_lancemateSpeech and g_formationSpeech's SNDS ids.
        public static readonly int[] SlotSpeech = { 2, 0x108, 0x109 };
        public static readonly int[] LancemateSpeech = { 0, 0xa, 0xb4, 0x10a, 0x71, 0x22, 0x23, 0x36, 0xc, 0xd, 0x2a };
        static readonly int[] FormationSpeech = { 0xb, 0x4f, 0x51, 0x52, 0x53, 0x56, 0x57 };

        // SayLancemateReport: report p_message of g_lancemateSpeech, after the slot that says it (-1: none; past the
        // table's three, nothing).
        public static void SayLancemateReport(int p_message, int p_slot)
        {
            if (Host.World.SquadEventSpeech(p_message, p_slot)) return;

            if (p_message == -1 || p_message >= 11) return;
            int slot = p_slot >= 0 && p_slot < SlotSpeech.Length ? SlotSpeech[p_slot] : 0;
            MissionSpeech.QueueSpeech(slot, LancemateSpeech[p_message], -1);
        }

        // SayFormation: "Formation change to" and the formation's name.
        public static void SayFormation(int p_formation)
        {
            // The host's squad speech for it (event 2, the mates' join line) instead, when it has one.
            if (Host.World.SquadEventSpeech(2, 0)) return;

            if (p_formation >= 0 && p_formation < 6) MissionSpeech.QueueSpeech(FormationSpeech[0], FormationSpeech[p_formation + 1], -1);
        }
    }
}
