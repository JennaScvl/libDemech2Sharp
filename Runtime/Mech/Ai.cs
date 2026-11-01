using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // A player's AI (demech2 MW2/src/ai.c: UpdateAI, RunAIRules, CollectAIRules, InitializeAI, the message, state and
    // transition functions, SetAIState and its helpers, the navs it places, RecordAttack, PostAIMessage, OrderPlayers,
    // SweepTorso; aiattacker.c, aidist.c, aiprox.c, aireset.c; aiweapons.c: RunAIWeapons, DecideAIFire; lineofsight.c;
    // mechdamage.c: RunAutopilot, PunchInAutoHeading), in 1.1's fixed point. An AI player (the gpspec's ai 2) runs a state
    // machine (AiState) its scripts drive (AiScripts: AIT resources of rules, by priority in three slots); a rule waits
    // for a message (proximity, distance, reaching, destruction, a target in range) about a target and makes a transition
    // into a new state with a new target. Targets are AI target ids: 0x100 a nav, 0x200 a player, 0x400 a game thing
    // (the low byte the index), or the symbolic ones (0x2100 home ...). Its state steers it through its steering, as
    // INPUT.MAP steers the user's mech. Every player keeps the AI's members (the user's are read by the AI players: what
    // it attacks, its own navs; the autopilot steers it with the AI's functions). Not ported: the mono monitor's logs.
    public sealed partial class Mech
    {
        public const int c_aiStateIdle = 0, c_aiStateAvoid = 1, c_aiStateTarget = 2, c_aiStateAttack = 3, c_aiStateFlee = 4, c_aiStateFollow = 5, c_aiStateRecon = 6,
            c_aiStatePatrol = 7, c_aiStateGoDirect = 8, c_aiStateRest = 10, c_aiStateShutdown = 11, c_aiStateDead = 12;

        public const int c_aiTargetNav = 0x100, c_aiTargetPlayer = 0x200, c_aiTargetThing = 0x400, c_aiTargetHome = 0x2100,
            c_aiTargetRbAnchor = 0x2101, c_aiTargetUser = 0x2200, c_aiTargetMyLeader = 0x2201, c_aiTargetMe = 0x2202,
            c_aiTargetFriendly = 0x4200, c_aiTargetEnemy = 0x4201;

        // g_aiRules[60][6]: each player's collected rules, NULL-terminated, one flat table as the original's: a player
        // collecting more than five runs into the next player's.
        public static readonly AiRule[] g_aiRules = new AiRule[60 * 6 + 1];

        // g_aiStateTime, g_aiSpreadTargets, g_lairdoCheat (the "lairdo" cheat). (g_hiddenTargetCount is
        // IsTargetDetectable's, which nothing calls.)
        public static int g_aiStateTime;
        public static bool g_aiSpreadTargets = true;
        public static bool g_lairdoCheat;
        // Not the original's: the "zombie" cheat (CheatCodes): AI pilots stand still and hold fire.
        public static bool g_zombieCheat;

        public int m_rules;                                 // this player's row of g_aiRules
        public readonly AiScripts.Script[] m_ruleSets = new AiScripts.Script[3]; // by priority
        public readonly int[] m_stackStates = new int[2], m_stackTargets = new int[2]; // m_stack (AiStackEntry)
        public int m_stackCount;

        // Player::m_ai (PlayerAi): the posted message and its target, the state, its target and goal, the mech's value,
        // the flags (1 held, 2 hold fire, 0x10 an order, 0x20 sent by the leader, 0x40 the objective's).
        public int m_postedMessage, m_postedTarget;
        public int m_aiState;                            // S16
        public int m_aiTarget, m_aiGoal;                   // U16
        public int m_aiValue;                            // U16
        public int m_aiFlags;                            // S16

        public int m_engageAtWill;
        public int m_gunnery, m_piloting;                         // m_piloting (S8)
        public int m_nextFireTime;
        // Titanium's pilot personality (its AI parameter 6, player+0x189, an index into mercsw.dll 0x1008b2f8): weapon
        // groups (player+0x192), the heat it fires up to (+0x1b0), its chance in 100 of a lock (+0x1b4).
        public int m_aiGroupMode, m_aiHeatLimit = 65, m_aiLockChance;
        // GBL (MW2.flat 0x217b0, 0x2c970): the AI's own maneuver table (+0x1ac), the distance its stupid maneuver backs
        // off within (+0x1a8, 15000 by default), and a personality's group fire (+0x15a).
        public ManeuverTable m_gblManeuvers;
        public int m_gblAvoidRange = 15000;
        public bool m_gblGroupFire;
        public static bool GblAi => Host.Variant.IsGhostBearsLegacy;

        // Titanium's personalities: {group mode, heat percent of 65, lock chance}; the fields the weapons read.
        public static readonly (int groups, int heat, int lockChance)[] TitaniumPersonalities =
        {
            (0, 75, 25), (0, 75, 25), (1, 100, 15), (2, 75, 25), (2, 75, 60), (0, 75, 100), (0, 80, 85), (0, 75, 25),
            (0, 75, 35),
        };

#if UNITY_EDITOR || MEKENGINE_MERCS
        // Mercenaries' personalities (DOS-Win95 MERCSW.DLL g_aiPersonalities 0x100ad0b8, 9 x 0x1c; InitAIPersonality
        // 0x10026c76): Titanium's weapon fields, and the distance the stupid maneuver backs off within (+0x1fe; 0 gives
        // 15000) and the lead aim (+0x1c0: RunAIWeaponsLeading). Their maneuver tables (+0x202) are all the mech's own.
        public static readonly (int avoidRange, bool lead)[] MercsPersonalities =
        {
            (0, false), (20000, false), (20000, false), (15000, false), (15000, false), (25000, false), (15000, true),
            (25000, false), (30000, true),
        };
        public int m_mercsAvoidRange = 15000;
        public bool m_mercsLeadAim;
#endif
        public int m_restAlertRange, m_alertRange, m_leashRange;    // m_alertRange, m_leashRange
        public int m_nav = 0x1000;                       // a nav target id the AI placed, or 0x1000

        // An AI rule (AiRule).
        public sealed class AiRule
        {
            public int m_message;    // S16
            public int m_target;     // U16
            public int m_arg;        // S16
            public int m_transition; // U8
            public int m_newState;   // S16
            public int m_newTarget;  // S16
        }

        public static int S16(int p_value) => (short)p_value;
        public static int U16(int p_value) => (ushort)p_value;

        public AiRule Rule(int p_index) => m_rules + p_index < g_aiRules.Length ? g_aiRules[m_rules + p_index] : null;

        public void SetRule(int p_index, AiRule p_rule)
        {
            if (m_rules + p_index < g_aiRules.Length) g_aiRules[m_rules + p_index] = p_rule;
        }

        public Player PlayerAt(int p_index) => SimMain.PlayerOf(p_index);
        public static Mech MechAt(int p_index) => SimMain.MechOf(p_index);

        // ---- UpdateAI -----------------------------------------------------------------------------------------------

        // UpdateAI: the star's leader leads its star (LeadStar); an AI player runs its rules and, unless one changed its
        // state, its state's function, then targets its goal; a player the AI doesn't drive (the user) records an attack on
        // an enemy mech its mech runs into.
        public void UpdateAI()
        {
            // A MekNet client: the host alone runs the AI (its mechs' steering comes with their states).
            if (Host.Net.FollowsHost) return;
            if (m_player.m_aiMode == 1) return;
            if (m_aiState == c_aiStateDead) return;
            if (Teams.GetTeamLeader(SimMain.g_world, m_player.m_team) == m_player.m_index) StarOrders.LeadStar(m_player.m_team);

            if (m_player.m_aiMode != 2)
            {
                if (m_collisionTicks != 0 && m_collidedWith != -1 && SimMain.SideOf(m_player.m_index, m_collidedWith) == 1) RecordAttack(m_player.m_index, m_collidedWith);
            }
            else if (g_zombieCheat)
            {
                m_steering["drive.throttle"] = 0;
                m_steering["turn"] = 0;
                m_steering["arms.fire"] = 0;
                m_steering["jets.fore"] = 0;
                m_steering["jets.aft"] = 0;
                m_steering["jets.left"] = 0;
                m_steering["jets.right"] = 0;
            }
            else
            {
                if (!RunAIRules()) RunAIState(m_aiState, m_aiTarget);
                SetTarget(S16(m_aiGoal));
            }
        }

        // g_aiStateFns.
        public void RunAIState(int p_state, int p_target)
        {
            switch (p_state)
            {
                case 0:
                case 11:
                    AiStateIdle();
                    break;
                case 3:
                    AiStateAttack(p_target);
                    break;
                case 1:
                case 2:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                    AiStateMove(p_target);
                    break;
            }
        }

        // RunAIRules: the rules against the posted message and their own messages; the first that fires makes its
        // transition and, unless that says otherwise, enters its new state. Once the posted message matched a rule, every
        // later rule counts as matched too (the original never clears its flag).
        public bool RunAIRules()
        {
            int i = 0;
            bool posted = false;
            TargetAttacker();
            AiRule rule;
            while ((rule = Rule(i)) != null)
            {
                i++;
                int target = ResolveRuleTarget(rule.m_target, S16(m_aiGoal), 0, S16(m_aiTarget));
                int arg = ResolveRuleTarget(U16(rule.m_arg), S16(m_aiGoal), 0, S16(m_aiTarget));
                if (rule.m_message == m_postedMessage && m_postedTarget == target)
                {
                    posted = true;
                    m_postedMessage = 0;
                }

                if (posted || (rule.m_message > 0 && rule.m_message < 8 && (target = RunAIMessage(rule.m_message, target, arg)) != 0))
                {
                    if (RunAITransition(rule.m_transition, rule))
                    {
                        SetAIState(rule.m_newState, ResolveRuleTarget(U16(rule.m_newTarget), S16(m_aiGoal), target, S16(m_aiTarget)), false);
                        return true;
                    }
                }
            }

            return false;
        }

        // g_aiMessageFns.
        public int RunAIMessage(int p_message, int p_target, int p_arg)
        {
            switch (p_message)
            {
                case 1:
                    return AiMessageProx(p_target, p_arg);
                case 2:
                    return AiMessageDist(p_target, p_arg);
                case 3:
                    return AiMessageReach(p_target, p_arg);
                case 4:
                    return S16(p_target);
                case 6:
                    return AiMessageDestroy(p_target);
                case 7:
                    return AiMessageTargetable(p_target, p_arg);
            }

            return 0;
        }

        // g_aiTransitionFns: whether the rule's new state is to be entered.
        public bool RunAITransition(int p_transition, AiRule p_rule)
        {
            switch (p_transition)
            {
                case 0:
                    return true;
                case 1:
                    m_stackCount = 0;
                    return true;
                case 2:
                    return AiTransitionPush();
                case 3:
                    return AiTransitionPop();
                case 4:
                    return AiTransitionNotify(p_rule);
            }

            return false;
        }

        // CollectAIRules: the rules of the current state from the scripts, the higher-priority script's first; a rule for
        // the message and target of one collected is skipped.
        public void CollectAIRules()
        {
            SetRule(0, null);
            for (int i = 2; i >= 0; i--)
            {
                AiScripts.Script script = m_ruleSets[i];
                if (script == null) continue;
                int pos = 0;
                int count = script.Word(pos++);
                int skip = 0, ruleCount = 0;
                bool found = false;
                while (count-- > 0)
                {
                    found = script.Word(pos++) == m_aiState;
                    if (found)
                    {
                        ruleCount = script.Word(pos);
                        break;
                    }

                    skip += script.Word(pos);
                    pos++;
                }

                if (!found) continue;
                int at = S16(script.Word(0) * 4 + 2 + skip * 12);
                for (int n = 0; n < ruleCount; n++)
                {
                    AiRule rule = script.RuleAt(at + n * 12);
                    if (rule == null || rule.m_message == 0) break;
                    int j = 0;
                    bool duplicate = false;
                    while (!duplicate && Rule(j) != null)
                    {
                        if (Rule(j).m_target == rule.m_target && Rule(j).m_message == rule.m_message) duplicate = true;
                        j++;
                    }

                    if (!duplicate)
                    {
                        SetRule(j, rule);
                        SetRule(j + 1, null);
                    }
                }
            }
        }

        // InitializeAI: the rules, state and anchor nav; whether it may engage (an enemy of the local player's team);
        // for an AI player its first script and its skills and ranges from the gpspec's AI parameters (gunnery, leash,
        // resting and active alert ranges in hundreds, piloting; 0 gives the defaults).
        public void InitializeAI()
        {
            m_rules = m_player.m_index * 6;
            m_aiState = c_aiStateIdle;
            m_nav = 0x1000;
            ResetAI();
            // Not on the user's star (any pilot's in a match).
            m_engageAtWill = SimMain.HasPilot && !SimMain.IsPilotStar(m_player.m_team) ? 1 : 0;

            if (m_player.m_aiMode == 2)
            {
                SetAIScript(0, 0, Teams.GetTeamLeader(SimMain.g_world, m_player.m_team));
                m_gunnery = (sbyte)m_player.m_aiParams[0];
                if (m_gunnery == 0) m_gunnery = 1;
                m_leashRange = m_player.m_aiParams[1];
                if (m_leashRange == 0) m_leashRange = 250;
                m_restAlertRange = m_player.m_aiParams[2];
                if (m_restAlertRange == 0) m_restAlertRange = 250;
                m_alertRange = m_player.m_aiParams[3];
                if (m_alertRange == 0) m_alertRange = 250;
                m_piloting = (sbyte)m_player.m_aiParams[4];
                if (m_piloting == 0) m_piloting = 1;
                // FUN_10007f21: the local player's star's mates get their voice group (gpspec +0x1e).
                if (Host.Variant.IsTitanium && SimMain.PlayerOf(SimMain.g_localPlayerId) is Player local && local.m_team == m_player.m_team)
                {
                    Host.World.SetSquadVoice(m_player.m_slot, m_player.m_aiParams[7]);
                }
                m_gblManeuvers = null;
                m_gblAvoidRange = 0;
                m_gblGroupFire = false;
#if UNITY_EDITOR || MEKENGINE_MERCS
                m_mercsAvoidRange = 0;
                m_mercsLeadAim = false;
#endif
                if (GblAi)
                {
                    // GBL @ 0x218c5: a gunnery of 100 to 103 names a personality (0xa62dc): gunnery and piloting 1, its
                    // maneuvers, its back-off distance, and personality 2 fires its weapons as one group (a mech of one or
                    // two weapons gets them all in group 0).
                    int raw = (byte)m_player.m_aiParams[0];
                    if (raw >= 100 && raw < 200 && raw % 100 < GblPersonalities.Length)
                    {
                        var personality = GblPersonalities[raw % 100];
                        m_gunnery = 1;
                        m_piloting = 1;
                        m_gblAvoidRange = personality.range;
                        m_gblManeuvers = personality.table;
                        if (personality.groupFire)
                        {
                            m_gblGroupFire = true;
                            if (m_config != null && m_config.m_weaponCount > 0 && m_config.m_weaponCount < 3)
                                for (int i = 0; i < m_config.m_weaponCount; i++) SetWeaponGroup(i, 0);
                            SelectWeaponGroup(0);
                        }
                    }
                }
                m_gunnery = (sbyte)(m_gunnery + 1);
                m_leashRange *= 100;
                m_restAlertRange *= 100;
                m_alertRange *= 100;
                if (WeaponDef.Titanium)
                {
                    // FUN_100081ad: the personality (past the table's nine, the first's; the original reads on).
                    int index = (byte)m_player.m_aiParams[6];
                    var personality = TitaniumPersonalities[index < TitaniumPersonalities.Length ? index : 0];
                    m_aiGroupMode = personality.groups;
                    m_aiHeatLimit = (int)(personality.heat / 100.0 * 65.0);
                    m_aiLockChance = personality.lockChance;
#if UNITY_EDITOR || MEKENGINE_MERCS
                    if (WeaponDef.Mercs)
                    {
                        var mercs = MercsPersonalities[index < MercsPersonalities.Length ? index : 0];
                        m_mercsAvoidRange = mercs.avoidRange;
                        m_mercsLeadAim = mercs.lead;
                    }
#endif

                    if (m_aiGroupMode != 0) SelectWeaponGroup(0);
                }
            }

            InitializeManeuvers();
        }

        // ResetAI: the AI's state (dead for an AI player, until its star's orders), target, goal, scripts, stack and posted
        // message cleared.
        public void ResetAI()
        {
            LeaveAIState();
            m_aiTarget = 0;
            m_aiGoal = 0;
            m_aiFlags = 0;
            m_aiState = m_player.m_aiMode != 2 ? c_aiStateIdle : c_aiStateDead;
            for (int i = 0; i < 3; i++) SetAIScript(i, -1, 0);
            SetRule(0, null);
            m_stackCount = 0;
            m_postedMessage = 0;
            m_nextFireTime = 0;
            ReleaseAnchorNav();
            CollectAIRules();
        }

        // SetAIScript: script slot p_slot gets script p_script of the player type's table, as its star's leader or a
        // follower; -1 clears it. Doors have none.
        public void SetAIScript(int p_slot, int p_script, int p_leader)
        {
            if (p_script == -1)
            {
                m_ruleSets[p_slot] = null;
                return;
            }

            int follower = m_player.m_index == p_leader ? 0 : 1;
            if (m_type == 7) return;
            m_ruleSets[p_slot] = AiScripts.Get(AiScripts.ScriptFor(m_type, p_script, follower));
        }

        // TargetAttacker: turns on a player attacking it (FindAttacker), unless its AI flags hold it (bit 0) or it is
        // already busy with that player, calling off the teammate on it (unless that one was sent by the objective).
        public void TargetAttacker()
        {
            if ((m_aiFlags & 1) != 0) return;
            int target = S16(FindAttacker());
            if (target != -1 && (m_aiGoal != m_nav || m_aiState != c_aiStateGoDirect) &&
                ((m_aiState != c_aiStateTarget && m_aiState != c_aiStateAttack) || m_aiGoal != U16(target)))
            {
                Mech other = FindNearestTarget(target, -3, out _, out _);
                if (other != null)
                {
                    if ((other.m_aiFlags & 0x40) != 0) target = -1;
                    else other.SetAIState(c_aiStateIdle, 0, false);
                }

                if (target != -1) SetAIState(c_aiStateTarget, target, true);
            }
        }

        // FindAttacker: the first player in play attacking this one (in state attack with it the goal) from the other
        // side, or 0xffff.
        public int FindAttacker()
        {
            int index = m_player.m_index;
            for (int i = 0; i < SimMain.PlayerCount; i++)
            {
                Mech other = MechAt(i);
                if (other != null && (other.PlayerFlags & 6) == 0 && other.m_aiGoal == U16(index | c_aiTargetPlayer) &&
                    !Teams.OnSameSide(SimMain.g_world, other.m_player.m_index, index) && other.m_aiState == c_aiStateAttack)
                {
                    return other.m_player.m_index | c_aiTargetPlayer;
                }
            }

            return 0xffff;
        }

        // ResolveRuleTarget: a rule's target field: '@' the goal, '%' the target found, '*' the current target.
        public static int ResolveRuleTarget(int p_value, int p_goal, int p_found, int p_target)
        {
            int result = S16(p_value);
            if (p_value == '@') result = p_goal;
            else if (p_value == '%') result = p_found;
            else if (p_value == '*') result = p_target;
            return result;
        }

        // ResolveTarget: a symbolic target resolved for the player (FindNearestTarget); targeting, the nearest no teammate
        // attacks (with g_aiSpreadTargets nothing else).
        public int ResolveTarget(int p_target)
        {
            if ((p_target & 0xf000) == 0) return p_target;
            FindNearestTarget(p_target, -3, out int nearest, out int best);
            if (m_aiState == c_aiStateTarget)
            {
                if (nearest != 0) return nearest;
                if (!g_aiSpreadTargets) return best;
                return 0;
            }

            return best;
        }

        // ---- Messages -----------------------------------------------------------------------------------------------

        // M_PROX: the nearest of p_target's targets closer than p_arg hundreds (-3: any), not destroyed.
        public int AiMessageProx(int p_target, int p_arg)
        {
            int result = 0, nearestTarget = -1, target = -1;
            int limit = p_arg == -3 ? 100000000 : p_arg * 100;
            int nearest = limit;
            while ((target = NextTarget(p_target, target)) != -1)
            {
                if (!IsTargetDone(target, 2) && SetTarget(target) && (int)m_targetInfo.m_distance < nearest)
                {
                    nearest = (int)m_targetInfo.m_distance;
                    nearestTarget = target;
                }
            }

            if (nearest < limit && nearestTarget != -1) result = nearestTarget;
            return S16(result);
        }

        // M_DIST: the farthest of p_target's targets farther than p_arg hundreds (-4: the leash range, -2: the alert
        // range, -3: 0, 0: the target's own range), not destroyed.
        public int AiMessageDist(int p_target, int p_arg)
        {
            int result = 0, farthestTarget = -1, target = -1;
            int limit;
            if (p_arg == -3) limit = 0;
            else if (p_arg == -4) limit = m_leashRange;
            else if (p_arg == -2) limit = m_alertRange;
            else if (p_arg == 0) limit = GetTargetRange(p_target);
            else limit = p_arg * 100;

            int farthest = limit;
            while ((target = NextTarget(p_target, target)) != -1)
            {
                if (!IsTargetDone(target, 2) && SetTarget(target) && farthest < (int)m_targetInfo.m_distance)
                {
                    farthest = (int)m_targetInfo.m_distance;
                    farthestTarget = target;
                }
            }

            if (limit < farthest && farthestTarget != -1) result = farthestTarget;
            return S16(result);
        }

        // M_REACH: within p_arg hundreds of p_target (0: within the target's range, when reaching its anchor nav
        // releases it, and a nav moves the player on to its next one, a patrol to its next own nav).
        public int AiMessageReach(int p_target, int p_arg)
        {
            int result;
            if (p_arg == 0)
            {
                result = AiMessageProx(p_target, S16(GetTargetRange(p_target) / 100));
                if (result != 0 && (p_target & c_aiTargetNav) != 0)
                {
                    if (m_nav == p_target) ReleaseAnchorNav();
                    if (m_aiState == c_aiStatePatrol)
                    {
                        AdvanceNavTarget(p_target);
                        m_aiTarget = U16(m_targetInfo.m_target);
                        if ((m_targetInfo.m_target & 0x1000) != 0) AdvanceNavTarget(c_aiTargetNav);
                    }
                    else AdvanceNavTarget(p_target);
                }
            }
            else result = AiMessageProx(p_target, p_arg);

            return result;
        }

        // M_TGTABLE: the nearest of p_target's targets within p_arg hundreds (-2 the alert range, -1 the resting one, 0
        // the target's range) no teammate attacks (without g_aiSpreadTargets any); never while the flags hold it.
        public int AiMessageTargetable(int p_target, int p_arg)
        {
            if ((m_aiFlags & 3) != 0) return 0;
            if (p_arg == -2) p_arg = S16(m_alertRange / 100);
            else if (p_arg == -1) p_arg = S16(m_restAlertRange / 100);
            else if (p_arg == 0) p_arg = S16(GetTargetRange(p_target) / 100);
            FindNearestTarget(p_target, p_arg, out int nearest, out int best);
            return g_aiSpreadTargets ? nearest : best;
        }

        // M_DESTROY: p_target once it's destroyed, reported when the local player leads the star.
        public int AiMessageDestroy(int p_target)
        {
            if (!IsTargetDone(U16(p_target), 2)) p_target = 0;
            else if (Teams.GetTeamLeader(SimMain.g_world, m_player.m_team) == SimMain.g_localPlayerId) StarOrders.SayLancemateReport(5, m_player.m_slot);
            return S16(p_target);
        }

        // FindNearestTarget: of p_target's targets, the nearest (p_best) and the nearest no teammate attacks (p_nearest)
        // for an objective other than defending, both within p_arg hundreds. Returns the teammate that does.
        public Mech FindNearestTarget(int p_target, int p_arg, out int p_nearest, out int p_best)
        {
            Mech blocker = null;
            int nearest = 100000000;
            int target = -1;
            p_nearest = p_best = 0;
            List<Mech> members = StarOrders.GetTeamMembers(m_player.m_team, true);
            if (members.Count == 0) return null;
            MissionObjective objective = Objectives.CurrentObjectiveOf(SimMain.g_world, m_player.m_team);
            if (Objectives.CurrentObjective(SimMain.g_world, m_player.m_team) == -1) return null;
            int type = objective != null ? (ushort)objective.m_type : 0;

            while ((target = NextTarget(p_target, target)) != -1)
            {
                bool closer = false;
                if (!SetTarget(target)) continue;
                if (m_targetInfo.m_distance < nearest)
                {
                    nearest = (int)m_targetInfo.m_distance;
                    p_best = target;
                    closer = true;
                    if ((target & c_aiTargetPlayer) == 0) p_nearest = target;
                }

                if ((target & c_aiTargetPlayer) != 0)
                {
                    foreach (Mech member in members)
                    {
                        if (m_player.m_index == member.m_player.m_index) continue;
                        if ((member.PlayerFlags & 0x16) != 0) continue;
                        if ((member.m_aiGoal & 0xf000) != 0) continue;
                        if (member.m_aiGoal == U16(target) && (member.m_aiState == c_aiStateAttack || member.m_aiState == c_aiStateTarget) && closer &&
                            type != 4)
                        {
                            blocker = member;
                            closer = false;
                            break;
                        }
                    }

                    if (closer) p_nearest = target;
                }
            }

            if (p_nearest != 0 && AiMessageProx(p_nearest, p_arg) == 0) p_nearest = 0;
            if (p_best != 0 && AiMessageProx(p_best, p_arg) == 0) p_best = 0;
            return blocker;
        }

        // NextTarget: the target after p_previous (-1 for the first) of p_target: a plain target once, or each player a
        // symbolic target names (the enemies: the other side's but neutrals; the friendlies: the local player's star's,
        // or with the "lairdo" cheat side 0's); -1 after the last.
        public int NextTarget(int p_target, int p_previous)
        {
            int result = -1;
            int index = m_player.m_index;
            p_target = S16(p_target);
            if ((p_target & 0x6000) == 0) return (p_target & 0xfff) != (p_previous & 0xfff) ? p_target : -1;

            if (p_previous != -1)
            {
                if ((p_target & 0x2000) != 0) return -1;
                p_previous &= 0xff;
            }

            if ((p_target & c_aiTargetPlayer) != 0)
            {
                if ((p_target & 0x4000) != 0)
                {
                    switch (p_target)
                    {
                        case c_aiTargetEnemy:
                            while (++p_previous < SimMain.PlayerCount)
                            {
                                if (!Teams.OnSameSide(SimMain.g_world, p_previous, index) && SimMain.PlayerSide(p_previous) != 2)
                                {
                                    result = p_previous;
                                    break;
                                }
                            }

                            break;
                        case c_aiTargetFriendly:
                            while (++p_previous < SimMain.PlayerCount)
                            {
                                if (SimMain.IsPilot(Teams.GetTeamLeader(SimMain.g_world, PlayerAt(p_previous).m_team)) ||
                                    (g_lairdoCheat && SimMain.PlayerSide(p_previous) == 0))
                                {
                                    result = p_previous;
                                    break;
                                }
                            }

                            break;
                    }
                }
                else
                {
                    switch (p_target)
                    {
                        case c_aiTargetUser:
                            result = SimMain.UserId;
                            break;
                        case c_aiTargetMyLeader:
                            result = Teams.GetTeamLeader(SimMain.g_world, m_player.m_team);
                            break;
                        case c_aiTargetMe:
                            result = m_player.m_index;
                            break;
                    }
                }
            }
            else if ((p_target & c_aiTargetThing) != 0)
            {
            }
            else if ((p_target & c_aiTargetNav) != 0)
            {
                switch (p_target)
                {
                    case c_aiTargetHome:
                        result = S16(Objectives.GetTeamHomeTarget(SimMain.g_world, m_player.m_team));
                        break;
                    case c_aiTargetRbAnchor:
                        result = S16(m_nav);
                        break;
                }
            }

            if (result != -1 && (result & 0xf00) == 0) result = (p_target & 0xf00) | result;
            return S16(result);
        }

        // ---- States -------------------------------------------------------------------------------------------------

        public void AiStateIdle() => m_targetInfo.m_distance = 0;

        // AiStateMove: toward the target (away from it fleeing), around what lies ahead (AvoidObstacles); targeting it
        // fires on it; recon, patrol and go-direct sweep the torso, follow keeps to its formation place too.
        public void AiStateMove(int p_target)
        {
            SetTarget(S16(p_target));
            BrakeFall();
            int range = GetTargetRange(S16(p_target));
            g_aiStateTime = Clock.g_currentClock;
            switch (m_aiState)
            {
                case c_aiStateTarget:
                {
                    ClearAIFire();
                    int bearing;
                    if (!AvoidObstacles())
                    {
                        m_steering["drive.throttle"] = GetApproachThrottle(range);
                        bearing = SteerToTarget();
                    }
                    else bearing = GetTargetBearing();

                    RunAIWeapons(bearing);
                    break;
                }
                case c_aiStateRecon:
                case c_aiStatePatrol:
                case c_aiStateGoDirect:
                    SweepTorso();
                    if (!AvoidObstacles())
                    {
                        m_steering["drive.throttle"] = GetApproachThrottle(range);
                        SteerToTarget();
                    }

                    g_aiStateTime += 0xb5;
                    break;
                case c_aiStateFollow:
                    SweepTorso();
                    if (!AvoidObstacles())
                    {
                        m_steering["drive.throttle"] = GetApproachThrottle(range);
                        SteerToTarget();
                    }

                    PlaceFormationNav();
                    g_aiStateTime += 0xb5;
                    break;
                case c_aiStateFlee:
                    m_targetInfo.m_heading = (m_targetInfo.m_heading + 0xb40000) % 0x1680000;
                    if (!AvoidObstacles())
                    {
                        m_steering["drive.throttle"] = 0x400;
                        SteerToTarget();
                    }

                    g_aiStateTime += 0xb5;
                    break;
                default:
                    if (m_player.m_aiMode == 2) m_steering["drive.throttle"] = 0x333;
                    break;
            }
        }

        // AiStateAttack: the maneuvers (RunManeuver).
        public void AiStateAttack(int p_target)
        {
            ClearAIFire();
            RunManeuver(p_target);
        }

        // The AI's triggers let go: Titanium's group trigger (+0x27) with the weapon's (+0x25).
        public void ClearAIFire()
        {
            m_steering["arms.fire"] = 0;
            if (WeaponDef.Titanium || (GblAi && m_gblGroupFire)) m_steering["arms.fire.group"] = 0;
        }

        // ---- Transitions --------------------------------------------------------------------------------------------

        // T_PUSH: the state and goal saved for T_POP (the first push takes the slot, later ones overwrite it).
        public bool AiTransitionPush()
        {
            if (m_stackCount == 0)
            {
                m_stackStates[0] = m_aiState;
                m_stackTargets[0] = m_aiGoal;
                m_stackCount++;
            }
            else
            {
                m_stackStates[0] = m_aiState;
                m_stackTargets[0] = m_aiGoal;
            }

            return true;
        }

        // T_POP: back to the saved state (and not into the rule's).
        public bool AiTransitionPop()
        {
            if (m_stackCount == 0) return true;
            m_stackCount--;
            SetAIState(m_stackStates[m_stackCount], S16(m_stackTargets[m_stackCount]), false);
            return false;
        }

        // T_NOTIFY: the rule's message posted to the star's leader with this player's target (an order: HandleStarOrder).
        public bool AiTransitionNotify(AiRule p_rule)
        {
            int leader = Teams.GetTeamLeader(SimMain.g_world, m_player.m_team);
            Mech mech = leader != -1 ? MechAt(leader) : null;
            mech?.PostAIMessage(p_rule.m_message, U16(m_targetInfo.m_target), p_rule.m_arg);
            return false;
        }

        // ---- Targets and steering -----------------------------------------------------------------------------------

        // SetTarget: p_target the target, and where it is (UpdateTarget). Whether it's valid.
        public bool SetTarget(int p_target)
        {
            m_targetInfo.m_target = S16(p_target);
            return Targeting.UpdateTarget(this);
        }

        // GetTargetRange: the range p_target counts as reached in: a nav's radius, a game thing's 10000 (a player's too:
        // its case runs on), else 100; at least 100.
        public int GetTargetRange(int p_target)
        {
            int range;
            switch (p_target & 0xf00)
            {
                case c_aiTargetNav:
                {
                    int index = p_target & 0xff;
                    range = index < SimMain.g_world.m_navs.Count ? SimMain.g_world.m_navs[index].m_radius : 0;
                    break;
                }
                case c_aiTargetPlayer:
                case c_aiTargetThing:
                    range = 10000;
                    break;
                default:
                    range = 100;
                    break;
            }

            if (range < 100) range = 100;
            return range;
        }

        // GetApproachThrottle: to close to p_range of the target: full beyond twice it, stopped inside it, else 0x333;
        // slower while turning hard (past 45 degrees' worth), half running hot (above 65); none below 0x66.
        public int GetApproachThrottle(int p_range)
        {
            int throttle;
            if ((long)p_range * 2 < m_targetInfo.m_distance) throttle = 0x400;
            else if (m_targetInfo.m_distance < p_range) throttle = 0;
            else throttle = 0x333;

            int turn = Math.Abs(m_steering["turn"]);
            if (throttle != 0 && turn >> 16 > 45)
            {
                throttle = (0x4000000 - (int)(turn * 2L / 3)) >> 16; // turn / 1.5
                if (throttle < 0x66) throttle = 0x66;
            }

            if (m_heat >> 16 > 65) throttle >>= 1;
            if (throttle < 0x66) throttle = 0;
            return throttle;
        }

        // AimTorsoPan, AimTorsoTilt: the torso toward p_angle (or the target's pitch), off by p_delta (AddClamped).
        public int AimTorsoPan(int p_angle, int p_delta) => AddClamped(p_angle / 0x2d00, p_delta * 2, true);
        public int AimTorsoTilt(int p_delta) => AddClamped(m_targetInfo.m_pitch / 0xf00, p_delta * 2, true);

        // AddClamped: p_delta added to p_value (with p_value's sign with p_sameSign), clamped to +/-0x400.
        public static int AddClamped(int p_value, int p_delta, bool p_sameSign)
        {
            if (p_sameSign && (p_value < 0 ? -1 : 1) != (p_delta < 0 ? -1 : 1)) p_delta = -p_delta;
            p_value += p_delta;
            if (p_value > 0x400) p_value = 0x400;
            else if (p_value < -0x400) p_value = -0x400;
            return p_value;
        }

        // SteerToTarget: the legs turned toward the target (full beyond 45 degrees). Returns the bearing to it.
        public int SteerToTarget()
        {
            int delta = GetTargetBearing();
            int degrees = (delta >> 16) % 360;
            if (degrees > 45) m_steering["turn"] = 0x3333333;
            else if (degrees < -45) m_steering["turn"] = -0x3333333;
            else
            {
                int scaled = degrees << 16;
                scaled /= 45;
                m_steering["turn"] = Fixed.Mul16(0x3333333, scaled);
            }

            return delta;
        }

        // GetTargetBearing: the bearing to the target, 16.16 degrees either way (Titanium's local player on the
        // autopilot: the route's, 0x1000ac69).
        public int GetTargetBearing()
        {
            int delta = (TitaniumRoute && IsPilot && m_autopilot != 0 ? m_routeInfo.m_heading : m_targetInfo.m_heading) - m_heading;
            if (delta > 0xb40000) delta -= 0x1680000;
            else if (delta < -0xb40000) delta += 0x1680000;
            return delta;
        }

        // IsTargetDone: whether p_target is done for an objective of type p_check: destroyed (flag 4; never for a nav) or
        // inspected (0x20). A symbolic target always is.
        public static bool IsTargetDone(int p_target, int p_check)
        {
            int type = p_target & 0xff00, index = p_target & 0xff;
            if ((type & 0xf000) != 0) return true;
            int flags = 0;
            bool hasFlags = false;
            switch (type)
            {
                case c_aiTargetPlayer:
                    if (index < SimMain.PlayerCount)
                    {
                        flags = SimMain.PlayerOf(index).m_flags;
                        hasFlags = true;
                    }

                    break;
                case c_aiTargetThing:
                    if (index < SimMain.g_world.m_gameThings.Count)
                    {
                        flags = SimMain.g_world.m_gameThings[index].m_flags;
                        hasFlags = true;
                    }

                    break;
                case c_aiTargetNav:
                    if (index < SimMain.g_world.m_navs.Count)
                    {
                        flags = SimMain.g_world.m_navs[index].m_flags;
                        hasFlags = true;
                    }

                    break;
            }

            if (!hasFlags) return true;
            // The original leaves the mask unset for the other objective types (0x10, 0x20, 0x200, 0x400, 0x2000...):
            // taken as 0, never done (DecompileAmbiguities.MD).
            int mask = 0;
            switch (p_check)
            {
                case 1:
                case 2:
                case 4:
                case 0x1000:
                    if (type == c_aiTargetNav) return false;
                    mask = 4;
                    break;
                case 8:
                case 0x100:
                    mask = 0x20;
                    break;
            }

            return (mask & flags) != 0;
        }

        // ---- AI state -----------------------------------------------------------------------------------------------

        // LeaveAIState: the maneuver ends or the navs are released, and an order's end is reported (the order flag 0x10
        // goes; an unfinished target keeps the flags it had).
        public void LeaveAIState()
        {
            int flags = 0;
            ClearAIFire();
            switch (m_aiState)
            {
                case c_aiStatePatrol:
                    if (IsTargetDone(m_aiGoal, 4) && (m_aiFlags & 0x10) != 0) StarOrders.SayLancemateReport(10, m_player.m_slot);
                    ReleaseNavPoints();
                    break;
                case c_aiStateFollow:
                    ReleaseNavPoints();
                    break;
                case c_aiStateAttack:
                    EndManeuver();
                    m_steering["turn"] = 0;
                    m_steering["torso.pitch"] = 0;
                    m_steering["torso.yaw"] = 0;
                    if (IsTargetDone(m_aiGoal, 8) && (m_aiFlags & 0x10) != 0) StarOrders.SayLancemateReport(10, m_player.m_slot);
                    break;
                case c_aiStateRecon:
                    if (IsTargetDone(m_aiGoal, 8) && (m_aiFlags & 0x10) != 0) StarOrders.SayLancemateReport(10, m_player.m_slot);
                    break;
                case c_aiStateTarget:
                    if (!IsTargetDone(m_aiGoal, 2)) flags = m_aiFlags;
                    else if ((m_aiFlags & 0x10) != 0) StarOrders.SayLancemateReport(10, m_player.m_slot);
                    break;
            }

            m_aiFlags = S16(m_aiFlags & ~0x10);
            m_aiFlags = S16(m_aiFlags | flags);
        }

        // EnterAIState: a restarting player powers up; outside attack the local star's players keep only their hold
        // flags; targeting schedules the first shot (and a patrolling AI places its anchor nav, as the original's test of
        // the leader of star 0 or 1 lets it); fleeing and shutting down hold the player; resting and shutting down power
        // the mech down; idling stops it.
        public void EnterAIState(int p_state)
        {
            p_state = U16(p_state);
            if ((PlayerFlags & 0x10) != 0)
            {
                PlayerFlags &= ~0x10;
                PowerState = 0;
                m_wokeFromShutdown = true;
                if (Host.World.InertDiagEnabled) UnityEngine.Debug.Log($"[InertDiag] #{m_player.m_index} team {m_player.m_team} wakes: AI state {m_aiState} -> {p_state} at mission time {Objectives.g_missionTime} objective {Objectives.CurrentObjective(SimMain.g_world, m_player.m_team)}\n{System.Environment.StackTrace}"); // TEMP
            }

            if (p_state != c_aiStateAttack && SimMain.IsPilotStar(m_player.m_team)) m_aiFlags &= 3;

            switch (p_state)
            {
                case c_aiStateTarget:
                    m_nextFireTime = SimRandom.RandomIntBelow(10) * 0x16 + Clock.g_currentClock;
                    if (Teams.GetTeamLeader(SimMain.g_world, !SimMain.IsPilot(m_player.m_team) ? 1 : 0) != 0 && m_aiState == c_aiStatePatrol && (m_nav & 0x1000) != 0)
                    {
                        int nav = Teams.AddNavPoint(SimMain.g_world, m_player.m_index, (int)m_positionX, (int)m_positionY, (int)m_positionZ);
                        if (nav != -1)
                        {
                            SimMain.g_world.m_navs[nav].m_flags |= 1;
                            SimMain.g_world.m_navs[nav].m_owner = m_player.m_index | c_aiTargetPlayer;
                            m_nav = nav | c_aiTargetNav;
                        }
                    }

                    break;
                case c_aiStateFlee:
                    m_aiFlags = 1;
                    break;
                case c_aiStateFollow:
                    PlaceFormationNav();
                    break;
                case c_aiStatePatrol:
                    PlacePatrolNavs();
                    break;
                case c_aiStateShutdown:
                    m_aiFlags = 1;
                    PlayerFlags |= 0x10;
                    PowerState |= 3;
                    m_steering["drive.throttle"] = 0;
                    m_targetInfo.m_distance = 0;
                    break;
                case c_aiStateRest:
                    PlayerFlags |= 0x10;
                    PowerState |= 3;
                    m_steering["drive.throttle"] = 0;
                    m_targetInfo.m_distance = 0;
                    break;
                case c_aiStateIdle:
                    m_steering["drive.throttle"] = 0;
                    m_targetInfo.m_distance = 0;
                    break;
            }

            g_aiStateTime = Clock.g_currentClock;
            m_aiState = S16(p_state);
        }

        // QueueAIState: p_state now, or behind the pushed state when there is one (where nothing pops it).
        public void QueueAIState(int p_state, int p_target)
        {
            if (m_stackCount == 0) SetAIState(p_state, S16(p_target), false);
            else if (m_stackCount < 2)
            {
                m_stackStates[m_stackCount] = p_state;
                m_stackTargets[m_stackCount] = U16(p_target);
            }
        }

        // HasAIState: whether a script has rules for p_state.
        public bool HasAIState(int p_state)
        {
            for (int i = 0; i <= 2; i++)
            {
                AiScripts.Script script = m_ruleSets[i];
                if (script == null) continue;
                int count = script.Word(0);
                for (int j = 0; j < count; j++)
                {
                    if (script.Word(j * 2 + 1) == p_state) return true;
                }
            }

            return false;
        }

        // SetAIState: p_state with p_target, the current one pushed with p_push, when a script has it; following itself
        // a leader idles.
        public void SetAIState(int p_state, int p_target, bool p_push)
        {
            if (p_state == -1 || !HasAIState(p_state)) return;
            if (p_push) AiTransitionPush();
            else m_stackCount = 0;
            LeaveAIState();
            if (p_state == c_aiStateFollow && p_target == c_aiTargetMyLeader && Teams.GetTeamLeader(SimMain.g_world, m_player.m_team) == m_player.m_index)
            {
                p_state = c_aiStateIdle;
                p_target = 0;
            }

            m_aiTarget = U16(ResolveTarget(S16(p_target)));
            m_aiGoal = m_aiTarget;
            EnterAIState(p_state);
            CollectAIRules();
        }

        // ---- Navs ---------------------------------------------------------------------------------------------------

        // The nav ids above p_index move down one (RemoveNavPoint): the AI target, goal and nav, and with p_target the
        // target (not the local player's).
        public void ShiftNavIds(int p_index, bool p_target)
        {
            if ((m_aiTarget & 0x100) != 0 && (m_aiTarget & 0xff) > p_index) m_aiTarget--;
            if ((m_aiGoal & 0x100) != 0 && (m_aiGoal & 0xff) > p_index) m_aiGoal--;
            if (p_target && (m_targetInfo.m_target & 0x100) != 0 && (m_targetInfo.m_target & 0xff) > p_index) m_targetInfo.m_target--;
            if ((m_nav & 0x100) != 0 && (m_nav & 0xff) > p_index) m_nav--;
        }

        // ReleaseNavPoints: the navs the player placed, but its anchor.
        public void ReleaseNavPoints()
        {
            for (int i = 0; i < SimMain.g_world.m_navs.Count; i++)
            {
                NavPoint nav = SimMain.g_world.m_navs[i];
                if ((nav.m_flags & 1) == 0 || nav.m_owner != (m_player.m_index | c_aiTargetPlayer)) continue;
                if ((m_nav & 0x1000) == 0)
                {
                    if ((m_nav & 0xff) != i) Teams.RemoveNavPoint(SimMain.g_world, m_player.m_index, i | c_aiTargetNav);
                }
                else Teams.RemoveNavPoint(SimMain.g_world, m_player.m_index, i | c_aiTargetNav);
            }
        }

        // PlacePatrolNavs: four navs around the target (its range out, a game thing's 25000), and the first targeted.
        public void PlacePatrolNavs()
        {
            int range = GetTargetRange(S16(m_aiTarget));
            int index = m_aiTarget & 0xff;
            int x, y, z;
            switch (m_aiTarget & 0xf00)
            {
                case c_aiTargetThing:
                {
                    Vector3Int at = Host.World.ThingPosition(index);
                    x = at.x;
                    y = at.y;
                    z = at.z;
                    range = 25000;
                    break;
                }
                case c_aiTargetPlayer:
                {
                    Mech mech = MechAt(index);
                    if (mech == null) return;
                    x = (int)mech.m_positionX;
                    y = (int)mech.m_positionY;
                    z = (int)mech.m_positionZ;
                    break;
                }
                case c_aiTargetNav:
                {
                    if (index >= SimMain.g_world.m_navs.Count) return;
                    Vector3Int at = SimMain.g_world.m_navs[index].m_position;
                    x = at.x;
                    y = at.y;
                    z = at.z;
                    break;
                }
                default:
                    return;
            }

            Teams.AddNavPoint(SimMain.g_world, m_player.m_index, x - range, y, z);
            Teams.AddNavPoint(SimMain.g_world, m_player.m_index, x, y, z - range);
            Teams.AddNavPoint(SimMain.g_world, m_player.m_index, x + range, y, z);
            Teams.AddNavPoint(SimMain.g_world, m_player.m_index, x, y, z + range);
            AdvanceNavTarget(c_aiTargetNav);
        }

        // AdvanceNavTarget: p_target targeted, then on to the next of the player's own navs, past its anchor, as the AI
        // target.
        public void AdvanceNavTarget(int p_target)
        {
            m_targetInfo.m_target = S16(p_target);
            Targeting.CycleNavTarget(this, 1, true);
            if (m_nav == m_targetInfo.m_target) Targeting.CycleNavTarget(this, 1, true);
            m_aiTarget = U16(m_targetInfo.m_target);
        }

        // PlaceFormationNav: a nav at the player's place in its star's formation (its height the slot's heading, as the
        // original passes it), the AI target.
        public void PlaceFormationNav()
        {
            ReleaseNavPoints();
            if (Teams.GetTeamSlotPosition(SimMain.g_world, m_player.m_index, out int x, out int z, out int slotHeading))
            {
                int nav = Teams.AddNavPoint(SimMain.g_world, m_player.m_index, x, slotHeading, z);
                if (nav != -1)
                {
                    SimMain.g_world.m_navs[nav].m_flags |= 1;
                    SimMain.g_world.m_navs[nav].m_owner = m_player.m_index | c_aiTargetPlayer;
                    m_aiTarget = nav | c_aiTargetNav;
                }
            }
        }

        // ReleaseAnchorNav: as written it only releases m_nav when m_nav is 0 (an index without the nav type), so the
        // anchor stays.
        public void ReleaseAnchorNav()
        {
            if (m_nav != 0) return;
            Teams.RemoveNavPoint(SimMain.g_world, m_player.m_index, m_nav);
            m_nav = 0x1000;
        }

        // RecordAttack: the local player's AI attacks player p_target (what its attackers' TargetAttacker sees).
        public static void RecordAttack(int p_index, int p_target)
        {
            if (!SimMain.IsPilot(p_index)) return;
            Mech mech = MechAt(p_index);
            if (mech == null) return;
            mech.m_aiGoal = U16(p_target | c_aiTargetPlayer);
            mech.m_aiState = c_aiStateAttack;
        }

        // PostAIMessage: a message to the player unless one is waiting.
        public void PostAIMessage(int p_message, int p_target, int p_arg)
        {
            if (m_postedMessage != 0) return;
            m_postedMessage = p_message;
            m_postedTarget = U16(p_target);
        }

        // OrderPlayers: the players of p_targets (the local player's orders to its star) into p_state with p_target:
        // attack toggles engaging at will; patrol replaces the state, the others push it; the order flags them. Returns
        // how many it skipped.
        public int OrderPlayers(int p_targets, int p_state, int p_target)
        {
            int skipped = 0, target = -1;
            while ((target = NextTarget(p_targets, target)) != -1)
            {
                if ((target & c_aiTargetPlayer) == 0) continue;
                if (S16(p_target) == target)
                {
                    skipped++;
                    continue;
                }

                Mech other = MechAt(target & 0xff);
                if (other == null || other.IsPilot) continue;
                if (other.m_aiState != c_aiStateDead && other.HasAIState(p_state))
                {
                    if (p_state == c_aiStateAttack)
                    {
                        other.m_engageAtWill = other.m_engageAtWill == 0 ? 1 : 0;
                        other.m_aiFlags = S16(other.m_aiFlags & ~3);
                    }
                    else
                    {
                        other.m_aiFlags = 0;
                        other.SetAIState(p_state, p_target, p_state != c_aiStatePatrol);
                        if (p_state == c_aiStateTarget) other.m_aiFlags |= 0x11;
                        else
                        {
                            if (p_state == c_aiStateFollow) other.m_engageAtWill = 0;
                            other.m_aiFlags |= 0x12;
                        }
                    }
                }
                else skipped++;
            }

            return skipped;
        }

        // SweepTorso: a mech sweeps its torso from side to side, 5 degrees every 0x20 ticks (0x40 stopped), turning back
        // past 45.
        public void SweepTorso()
        {
            int period = m_steering["drive.throttle"] == 0 ? 0x40 : 0x20;
            if (m_type != 1) return;
            if (m_maneuverEnd > period + Clock.g_currentClock) m_maneuverEnd = period + Clock.g_currentClock;
            if (m_maneuverEnd > Clock.g_currentClock) return;
            if (m_maneuverParam != 0) m_steering["torso.yaw"] += 0x50000;
            else m_steering["torso.yaw"] -= 0x50000;
            if (Math.Abs(m_steering["torso.yaw"]) > 0x2d0000) m_maneuverParam = m_maneuverParam == 0 ? 1 : 0;
            m_maneuverEnd = period + Clock.g_currentClock;
        }

        // ---- Weapons ------------------------------------------------------------------------------------------------

        // RunAIWeapons: at random times (up to m_gunnery x 22 ticks) with the torso within 10 degrees of p_heading,
        // against the local player always (else one time in three), aims at the goal and on a roll of 0 may fire
        // (DecideAIFire; a skilled pilot only with a line of sight). Then turns the torso toward p_heading and tilts it.
        // Whether it fired.
        public bool RunAIWeapons(int p_heading)
        {
#if UNITY_EDITOR || MEKENGINE_MERCS
            // Mercenaries (RunAIWeaponsDispatch 0x10022b70): a lead-aiming personality's own.
            if (WeaponDef.Mercs && m_mercsLeadAim) return RunAIWeaponsLeading(p_heading);
#endif
            bool fired = false;
            int roll = 0;
            if (m_nextFireTime <= Clock.g_currentClock)
            {
                roll = SimRandom.RandomIntBelow(m_gunnery);
                m_nextFireTime = roll * 22 + Clock.g_currentClock;
                int delta = p_heading - TorsoTwist;
#if UNITY_EDITOR || MEKENGINE_MERCS
                // Mercenaries (0x10022e3c): the torso's pitch within 10 degrees of the target's too (the player's own pitch,
                // +0x5c, has no counterpart here).
                int pitchError = (TorsoPitch + m_targetInfo.m_pitch) % 0x1680000;
                bool pitchOk = !WeaponDef.Mercs || (pitchError < 0xa0000 && pitchError > -0xa0000);
                if (delta < 0xa0000 && delta > -0xa0000 && pitchOk)
#else
                if (delta < 0xa0000 && delta > -0xa0000)
#endif
                {
                    if (SimMain.IsPilot(m_aiGoal & 0xff) || SimRandom.RandomIntBelow(3) == 0)
                    {
                        SetTarget(S16(m_aiGoal));
                        if (WeaponDef.Titanium)
                        {
                            // Titanium (0x10005633): the personality's chooser, the line of sight always checked, the
                            // group's trigger for a group pilot, then maybe a lock.
                            bool guided = false;
                            if (roll == 0 && (m_aiGroupMode == 0 ? ChooseAIWeaponTitanium(out guided) : ChooseAIWeaponGroup(out guided)) &&
                                CanSeeTarget(true))
                            {
                                m_steering[m_aiGroupMode == 0 ? "arms.fire" : "arms.fire.group"] = 1;
                                fired = true;
                                if (guided) TryAILock();
                            }
                        }
                        else if (roll == 0 && DecideAIFire() && (!m_skillFlags[4] || CanSeeTarget(true)))
                        {
                            // GBL @ 0x2ec37: a group-fire personality pulls the group trigger (+0x27).
                            m_steering[GblAi && m_gblGroupFire ? "arms.fire.group" : "arms.fire"] = 1;
                            fired = true;
                        }
                    }
                }
            }

            if (!fired) roll = 0;
            m_steering["torso.yaw"] = AimTorsoPan(p_heading, roll) * 0x2d00;
            m_steering["torso.pitch"] = -(AimTorsoTilt(roll) * 0xf00);
            return fired;
        }

#if UNITY_EDITOR || MEKENGINE_MERCS
        // Mercenaries' RunAIWeaponsLeading (0x10022bc1; personalities 6 and 8): the aim led on the goal (ComputeLeadPoint), the
        // torso within a degree of the lead's bearing and ten of its pitch; the goal not set as the target first; a moving
        // player goal (its throttle off zero) gets the torso aimed without the gunnery's scatter.
        public bool RunAIWeaponsLeading(int p_heading)
        {
            int yaw = p_heading, pitch = m_targetInfo.m_pitch;
            ComputeLeadPoint(ref yaw, ref pitch);
            int delta = yaw - TorsoTwist;
            int pitchError = (TorsoPitch + pitch) % 0x1680000;
            bool fired = false, guided = false;
            int roll = 0;
            if (m_nextFireTime <= Clock.g_currentClock)
            {
                roll = SimRandom.RandomIntBelow(m_gunnery);
                m_nextFireTime = roll * 22 + Clock.g_currentClock;
                if (delta < 0x10000 && delta > -0x10000 && pitchError < 0xa0000 && pitchError > -0xa0000 &&
                    (SimMain.IsPilot(m_aiGoal & 0xff) || SimRandom.RandomIntBelow(3) == 0) && roll == 0)
                {
                    if ((m_aiGroupMode == 0 ? ChooseAIWeaponTitanium(out guided) : ChooseAIWeaponGroup(out guided)) && CanSeeTarget(true))
                    {
                        m_steering[m_aiGroupMode == 0 ? "arms.fire" : "arms.fire.group"] = 1;
                        fired = true;
                    }
                }
            }

            if (guided && fired) TryAILock();
            if (((m_aiGoal >> 8) & 0xf) == 2 && MechAt(m_aiGoal & 0xff) is Mech goal && goal.m_steering["drive.throttle"] != 0) roll = 0;
            if (!fired) roll = 0;
            m_steering["torso.yaw"] = AimTorsoPan(yaw, roll) * 0x2d00;
            m_steering["torso.pitch"] = -(AddClamped(pitch / 0xf00, roll * 2, true) * 0xf00);
            return fired;
        }

        // Mercenaries' ComputeLeadPoint (0x10023525): with a player as the goal (the target for the local player), the
        // closing speed along the line to it (its 16.16 velocity's length, negative when it closes) against the selected
        // weapon's shot speed gives the flight time (the target's range over the difference; 2 when they match); within the
        // shot's lifetime and past a tick, the goal's place that far on sets p_yaw (from the heading, within half a turn)
        // and p_pitch. Else they stay.
        public void ComputeLeadPoint(ref int p_yaw, ref int p_pitch)
        {
            int goalId = IsPilot ? m_targetInfo.m_target : m_aiGoal;
            if (((goalId >> 8) & 0xf) != 2 || m_config == null) return;
            Mech goal = MechAt(goalId & 0xff);
            if (goal == null) return;
            WeaponDef def = m_selectedWeapon >= 0 && m_selectedWeapon < m_config.m_weapons.Length ? WeaponDef.Of(m_config.m_weapons[m_selectedWeapon].m_type) : default;
            int shotSpeed = def.m_speed;
            Vector3Int d = goal.ObjectPosition - ObjectPosition;
            Vector3Int v = goal.Velocity;
            if (v.x == 0 && v.y == 0 && v.z == 0) return;
            long along = ((long)d.x * v.x >> 16) + ((long)d.z * v.z >> 16) + ((long)d.y * v.y >> 16);
            int speed = (int)Fixed.Sqrt((long)v.x * v.x + (long)v.y * v.y + (long)v.z * v.z) >> 16;
            if (along < 0) speed = -speed;
            int time = Math.Abs(speed) == shotSpeed ? 2 : shotSpeed - speed != 0 ? m_targetInfo.m_range / (shotSpeed - speed) : 0;
            if (time == 0 || time > def.m_lifetime || time <= Clock.g_deltaTime) return;
            var lead = new Vector3Int(
                (int)(goal.ObjectPosition.x + ((long)v.x * time >> 16)),
                (int)(goal.ObjectPosition.y + ((long)v.y * time >> 16)),
                (int)(goal.ObjectPosition.z + ((long)v.z * time >> 16)));
            Targeting.GetBearingAndRange(lead - ObjectPosition, out int bearing, out _, out _, out int leadPitch);
            int yaw = bearing - m_heading;
            if (yaw > 0xb40000) yaw -= 0x1680000;
            else if (yaw < -0xb40000) yaw += 0x1680000;
            p_yaw = yaw;
            p_pitch = leadPitch;
        }
#endif

        // DecideAIFire: the selected weapon fires when the target is within its range, the mech cool enough (below 65
        // with its heat), the weapon ready and loaded, and by chance per its recycle time; else the next weapon of its
        // group is tried. A direct-fire weapon of type 0 or 4 tilts the torso up; a guided weapon may lock on (a good
        // gunner), and the local player hears it coming.
        public bool DecideAIFire()
        {
            if (m_config == null || m_selectedWeapon < 0) return false;
            bool fire = false;
            int type = 0;
            for (int i = 0; i < m_config.m_weaponCount && !fire; i++)
            {
                fire = true;
                WeaponSlot slot = m_config.m_weapons[m_selectedWeapon];
                type = slot.m_type;
                WeaponDef def = WeaponDef.Of(type);
                int band = GetTargetRangeBand(def);
                if (band != 1 && band != 2) fire = false;
                if (fire)
                {
                    fire = false;
                    if (GblAi && m_gblGroupFire)
                    {
                        // GBL (0x2ec90): every weapon's heat counted, as written: the selected one's, once per weapon,
                        // when it is ready and loaded.
                        int ammo = 0, heat = 0;
                        for (int k = 0; k < m_config.m_weaponCount; k++)
                        {
                            if (slot.m_state == WeaponSlot.Ready && slot.m_ammo != 0)
                            {
                                ammo += slot.m_ammo;
                                heat += def.m_heatFx;
                            }
                        }
                        if (ammo != 0 && (m_heat + heat) >> 16 < 65 && SimRandom.RandomIntBelow(def.m_recycle / 90 + 1) == 0) fire = true;
                    }
                    else if ((def.m_heatFx + m_heat) >> 16 < 65 && IsSelectedWeaponReady() == 1 && slot.m_ammo != 0 &&
                        SimRandom.RandomIntBelow(def.m_recycle / 90 + 1) == 0)
                    {
                        fire = true;
                    }
                }

                if (!fire) SelectNextWeaponInGroup(true);
            }

            if (fire)
            {
                if (type == 0) m_steering["torso.pitch"] += 0x1e000;
                else if (type == 4) m_steering["torso.pitch"] += 0x3c000;
                if (WeaponDef.Of(type).m_guided != 0)
                {
                    if (m_gunnery <= 4 && SimRandom.RandomIntBelow(m_gunnery + 1) == 0) m_flags |= 0x80;
                    if (m_aiGoal == (SimMain.g_localPlayerId | c_aiTargetPlayer)) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0x6f), 100, 0x40, 5, 0x32);
                }
            }

            return fire;
        }

        // Titanium's DecideAIFire (0x10005864): the selected weapon in range (band 1 or 2), the heat and the weapon's
        // below the personality's limit, ready, with ammunition (FUN_10006032) and by chance per its recycle time; else the
        // group's next. Types 0 and 4 tilt the torso up; a guided weapon asks for a lock.
        public bool ChooseAIWeaponTitanium(out bool p_guided)
        {
            p_guided = false;
            if (m_config == null) return false;
            bool fire = false;
            int type = 0;
            WeaponDef def = default;
            for (int i = 0; i < m_config.m_weaponCount && !fire; i++)
            {
                fire = false;
                WeaponSlot slot = null;
                if (m_selectedWeapon != -1)
                {
                    slot = m_config.m_weapons[m_selectedWeapon];
                    type = slot.m_type;
                    def = WeaponDef.Of(type);
                    int band = GetTargetRangeBand(def);
                    if (band > 0 && band < 3) fire = true;
                }

                if (fire)
                {
                    fire = m_heat + (long)def.m_heatFx < (long)m_aiHeatLimit << 16 && IsSelectedWeaponReady() == 1 &&
                           m_config.TitaniumHasAmmo(slot) && SimRandom.RandomIntBelow(def.m_recycle / 0x5a + 1) == 0;
                }

                if (!fire) SelectNextWeaponInGroup(true);
            }

            if (fire)
            {
                if (type == 0) m_steering["torso.pitch"] += 0x1e000;
                else if (type == 4) m_steering["torso.pitch"] += 0x3c000;
                if (def.m_guided != 0) p_guided = true;
            }

            return fire;
        }

        // Titanium's group pilot (0x10005a23): every weapon in group 0, then those whose target is between their short and
        // long ranges (band 2), loaded and not empty, into group 1, until their heat with the mech's would reach the
        // personality's limit (an AI's; that ends it without firing). The first ready guided one is selected, else group
        // 1; nothing in range moves on to the group's next weapon. The torso tilts for types 0 and 4.
        public bool ChooseAIWeaponGroup(out bool p_guided)
        {
            p_guided = false;
            if (m_config == null) return false;
            bool fire = false;
            long heat = 0; // 16.16
            int tilt = 0, firstGuided = 0;
            for (int i = 0; i < m_config.m_weaponCount && i < m_config.m_weapons.Length; i++)
            {
                WeaponSlot slot = m_config.m_weapons[i];
                int type = (short)slot.m_type;
                WeaponDef def = WeaponDef.Of(type);
                slot.m_group = 0;
                bool loaded = slot.m_pool == WeaponSlot.NoPool ||
                              (slot.m_pool < m_config.m_poolCount && m_config.m_pools[slot.m_pool].m_shots != 0);
                if (m_targetInfo.m_target == -1 || (m_targetInfo.m_target & 0x1000) != 0 || GetTargetRangeBand(def) != 2 || !loaded ||
                    slot.m_state == WeaponSlot.Empty)
                {
                    continue;
                }

                if (!IsPilot && (long)m_aiHeatLimit << 16 <= m_heat + heat + def.m_heatFx)
                {
                    fire = false;
                    break;
                }

                fire = true;
                slot.m_group = 1;
                heat += def.m_heatFx;
                if (firstGuided == 0 && def.m_guided != 0 && slot.m_state == WeaponSlot.Ready) firstGuided = i + 1;
                if (type == 0) tilt = 0x1e000;
                if (type == 4) tilt = 0x3c000;
            }

            if (!fire) SelectNextWeaponInGroup(true);
            else
            {
                m_steering["torso.pitch"] += tilt;
                if (firstGuided == 0) SelectWeaponGroup(1);
                else m_selectedWeapon = firstGuided - 1;
            }

            if (fire && firstGuided != 0) p_guided = true;
            return fire;
        }

        // FUN_10005c42: by the personality's chance the lock comes on; the local player, the goal, hears it coming.
        public void TryAILock()
        {
            if (SimRandom.RandomIntBelow(100) >= m_aiLockChance) return;
            if (LockJammed) return;
            m_flags |= 0x80;
            if (m_aiGoal == (SimMain.g_localPlayerId | c_aiTargetPlayer)) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0x6f), 100, 0x40, 5, 0x32);
        }

        // GetTargetRangeBand: 3 within the weapon's short range, 2 within its long one, 1 right at it, 0 beyond.
        public int GetTargetRangeBand(WeaponDef p_weapon)
        {
            if (m_targetInfo.m_distance < p_weapon.m_shortRange) return 3;
            if (m_targetInfo.m_distance < p_weapon.m_longRange) return 2;
            if (m_targetInfo.m_distance > p_weapon.m_longRange) return 0;
            return 1;
        }

        // CanSeeTarget: nothing but the target itself on the line to it; with p_ahead, never one behind the view
        // (between 90 and 270 degrees off it).
        public bool CanSeeTarget(bool p_ahead)
        {
            int bearing = (m_heading + TorsoTwist - m_targetInfo.m_heading + 0x1680000) % 0x1680000;
            if (p_ahead && bearing < 0x10e0000 && bearing > 0x5a0000) return false;
            return HasLineToTarget((int)m_positionY);
        }

        // HasLineToTarget: the segment from the player (at height p_y) to its target is clear, or hits the target.
        public bool HasLineToTarget(int p_y)
        {
            CollisionRay ray = CollisionRay.BuildRayFromSegment(FixVec.FromUnits((int)m_positionX, p_y, (int)m_positionZ), FixVec.FromUnits(m_targetInfo.m_position));
            ray.BuildRayFixed();
            if (ShapeCollision.TestSegmentCollision(ref ray, out Shape hit, m_player.m_index) && hit != null)
            {
                int surface = hit.Kind;
                int target = m_targetInfo.m_target;
                if ((surface & 0x100) != 0 && (target & 0x200) != 0) return hit.Owner == (target & 0xff);
                if ((surface & 0x200) != 0 && (target & 0x400) != 0) return hit.Owner == (target & 0xff);
                return false;
            }

            return true;
        }

        // ---- The autopilot ------------------------------------------------------------------------------------------

        // RunAutopilot: mode 1 follows the navs in order, skipping those reached (none left: it gives up), marking each
        // it reaches and turning off after the last; then it steers at its target around what lies ahead (AvoidObstacles),
        // the throttle kept in m_maneuverParam while it avoids.
        public void RunAutopilot()
        {
            if (m_autopilot == 0) return;
            // Titanium (0x100356c0): the route's nav, which the nav cycles keep with the main target's.
            PlayerTargetInfo route = TitaniumRoute ? m_routeInfo : m_targetInfo;
            if (m_autopilot == 1)
            {
                if ((route.m_target & 0x100) == 0 || (route.m_target & 0x1000) != 0)
                {
                    Targeting.CycleNavTarget(this, 0, false);
                    int index = route.m_target & 0xff;
                    int first = index;
                    while (index < SimMain.g_world.m_navs.Count && (SimMain.g_world.m_navs[index].m_flags & 0x20) != 0)
                    {
                        Targeting.CycleNavTarget(this, 1, false);
                        index = route.m_target & 0xff;
                        if (index == first)
                        {
                            route.m_target |= 0x1000;
                            return;
                        }
                    }
                }

                if ((route.m_target & 0x100) != 0)
                {
                    int index = route.m_target & 0xff;
                    int first = index;
                    if (index < SimMain.g_world.m_navs.Count && SimMain.g_world.m_navs[index].m_radius > route.m_distance && IsPilot)
                    {
                        NavPoint nav = SimMain.g_world.m_navs[index];
                        if ((nav.m_flags & 0x20) == 0)
                        {
                            nav.m_flags |= 0x20;
                            nav.m_teamsReached |= 1 << m_player.m_team;
                            CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xe7), 100, 0x40, 5, 0x50);
                        }

                        Targeting.CycleNavTarget(this, 1, false);
                        if ((route.m_target & 0xff) < first)
                        {
                            m_autopilot = 0;
                            route.m_target |= 0x1000;
                            m_steering["drive.throttle"] = 0;
                            m_steering["drive.throttle.hold"] = 1;
                            return;
                        }
                    }
                }
            }

            if (!AvoidObstacles())
            {
                SteerToTarget();
                if (m_maneuverFlag == 1)
                {
                    m_steering["drive.throttle"] = m_maneuverParam;
                    m_maneuverFlag = 0;
                }

                m_maneuverParam = m_steering["drive.throttle"];
            }
            else m_maneuverFlag = 1;
        }

        // PunchInAutoHeading (PUNCH_IN_AUTO_HDG): the target heading (the HUD's bearing marker, which the autopilot steers
        // by) set to where the torso faces, unless the autopilot follows the navs.
        public void PunchInAutoHeading()
        {
            if (m_autopilot == 1) return;
            (TitaniumRoute ? m_routeInfo : m_targetInfo).m_heading = (m_heading + 0x1680000 + m_torsoTwist.m_value) % 0x1680000;
        }
    }
}
