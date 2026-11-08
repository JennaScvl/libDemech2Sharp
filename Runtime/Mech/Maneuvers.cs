using System;
using UnityEngine;

namespace Demech2
{
    // An AI player's maneuvers against its target (demech2 MW2/src/maneuvers.c), in 1.1's fixed point: in the attack
    // state it runs one maneuver at a time, chosen from its player type's table (what may follow the last one) unless
    // something asks for another (out of ammunition: jump in, run at the target and self-destruct, or flee; a shot to
    // dodge: jump aside), until it's done or its time is up. Walking at the target, taking a place around it, closing in
    // (weaving), jumping above it or onto it, backing off, popping up from below, circling it. Every movement steers around
    // what lies ahead (AvoidObstacles: up to four probe rays on the side it avoids on), jumps to turn faster and brakes
    // falls with the jets.
    public sealed partial class Mech
    {
        public const int c_maneuverStupid = 0, c_maneuverBehind = 1, c_maneuverAchick = 2, c_maneuverAsrp = 3, c_maneuverAjmpin = 4,
            c_maneuverAdfa = 5, c_maneuverKama = 6, c_maneuverWchick = 7, c_maneuverWbackp = 8, c_maneuverWpeek = 9,
            c_maneuverAvoid = 10, c_maneuverSprint = 11, c_maneuverCircle = 12;

        // Sixteen directions around a mech, as (x, z) divisors of a length (StreamingAssets/Tables/PilotProbeRays.json).
        public static int[,] s_probeRays;

        public static int[,] ProbeRays
        {
            get
            {
                if (s_probeRays != null) return s_probeRays;
                ProbeRay[] rays = SimTables.PilotProbeRays;
                var directions = new int[rays.Length, 2];
                for (int i = 0; i < rays.Length; i++)
                {
                    directions[i, 0] = rays[i].dx;
                    directions[i, 1] = rays[i].dz;
                }

                return s_probeRays = directions;
            }
        }

        // The maneuver tables (StreamingAssets/Tables/PilotManeuvers.json: the standard one, the 1-ton mech's, and the
        // plain, orbiting and flanking ones): each entry a maneuver, how many may follow it and those, as the rows
        // { maneuver, follower count, seven followers } the choice reads.
        public static int[][] s_standardManeuvers, s_featherweightManeuvers, s_plainManeuvers, s_orbitManeuvers, s_flankManeuvers;

        public static ManeuverFile Maneuvers => SimTables.PilotManeuvers;

        public static int[][] Rows(ManeuverStep[] p_steps)
        {
            var rows = new int[p_steps.Length][];
            for (int i = 0; i < p_steps.Length; i++)
            {
                var row = new int[2 + p_steps[i].followers.Length];
                row[0] = p_steps[i].maneuver;
                row[1] = p_steps[i].followerCount;
                Array.Copy(p_steps[i].followers, 0, row, 2, p_steps[i].followers.Length);
                rows[i] = row;
            }

            return rows;
        }

        public static int[][] StandardManeuvers => s_standardManeuvers ??= Rows(Maneuvers.standard);
        public static int[][] FeatherweightManeuvers => s_featherweightManeuvers ??= Rows(Maneuvers.featherweight);
        public static int[][] PlainManeuvers => s_plainManeuvers ??= Rows(Maneuvers.plain);
        public static int[][] OrbitManeuvers => s_orbitManeuvers ??= Rows(Maneuvers.orbiting);
        public static int[][] FlankManeuvers => s_flankManeuvers ??= Rows(Maneuvers.flanking);

        // A maneuver table (ManeuverTable): its entries and how many of the last only ever follow. A table read from
        // data takes its entries from Source on first use.
        public sealed class ManeuverTable
        {
            int[][] entries;
            public Func<int[][]> Source;
            public int[][] m_entries { get => entries ??= Source(); set => entries = value; }
            public int m_followOnly;
            public int m_count => m_entries.Length;
        }

        // g_maneuverTables: by player type (1 to 8), and in [8] the 1-ton mech's.
        public static readonly ManeuverTable[] g_maneuverTables =
        {
            new ManeuverTable { Source = () => StandardManeuvers, m_followOnly = 7 },
            new ManeuverTable { Source = () => PlainManeuvers }, new ManeuverTable { Source = () => PlainManeuvers },
            new ManeuverTable { Source = () => PlainManeuvers }, new ManeuverTable { Source = () => OrbitManeuvers },
            new ManeuverTable { Source = () => PlainManeuvers }, new ManeuverTable { Source = () => PlainManeuvers },
            new ManeuverTable { Source = () => FlankManeuvers },
            new ManeuverTable { Source = () => FeatherweightManeuvers, m_followOnly = 7 },
        };

        public int m_maneuver = -1, m_lastManeuver = -1;   // m_lastManeuver (S16)
        public int m_nextManeuver;                        // one asked for (0 none), or -2 to flee
        public int m_maneuverEnd, m_maneuverTimer;          // m_maneuverTimer
        public int m_maneuverParam, m_maneuverFlag;         // m_maneuverFlag: each maneuver's own
        public Shape m_avoidShape;
        public int m_nextAvoidCheck;
        public int m_avoidSide;                           // (S16)
        public int m_probeScale = 1;                      // (16.16)
        public bool m_controlsJets;
        public int m_lastTargetDistance;
        public readonly bool[] m_skillFlags = new bool[7]; // m_skillFlag0 to 6
        public readonly int[] m_placesTaken = new int[8];  // (S8)

        // RunManeuver: the current maneuver for a tick, ended when it is done, when another is asked for (out of
        // ammunition: jump in, self-destruct or flee), when the mech is stuck or its time is up; with none, one chosen
        // and started.
        public void RunManeuver(int p_target)
        {
            bool done = false;
            if (m_maneuver != -1)
            {
                switch (m_maneuver)
                {
                    case c_maneuverStupid:
                        BrakeFall();
                        ManeuverStupid(p_target);
                        break;
                    case c_maneuverBehind:
                    {
                        if (m_maneuverTimer <= Clock.g_currentClock)
                        {
                            ReleaseNavPoints();
                            Mech leader = MechAt(m_aiGoal & 0xff);
                            if (leader != null && m_maneuverParam != 0) leader.m_placesTaken[m_maneuverParam / 2] = (sbyte)(leader.m_placesTaken[m_maneuverParam / 2] - 1);
                            m_maneuverTimer = Clock.g_currentClock + 543;
                            if (m_maneuverFlag == -1)
                            {
                                m_maneuverParam = ChooseFlankPlace();
                                m_maneuverFlag = 0;
                            }

                            PlaceOffsetNav(m_aiGoal, m_maneuverParam, 15000);
                            if (leader != null) leader.m_placesTaken[m_maneuverParam / 2] = (sbyte)(leader.m_placesTaken[m_maneuverParam / 2] + 1);
                        }

                        BrakeFall();
                        ManeuverBehind(p_target);
                        break;
                    }
                    case c_maneuverAchick:
                        BrakeFall();
                        if (ManeuverAchick(p_target)) done = true;
                        break;
                    case c_maneuverAsrp:
                        BrakeFall();
                        if (ManeuverAchick(p_target)) done = true;
                        if (!AvoidObstacles()) m_steering["turn"] += m_maneuverParam * 0x1c20000;
                        if (m_maneuverTimer <= Clock.g_currentClock)
                        {
                            m_maneuverParam = -m_maneuverParam;
                            m_maneuverTimer = Clock.g_currentClock + 543;
                        }

                        break;
                    case c_maneuverAjmpin:
                        if (ManeuverAjmpin(p_target)) done = true;
                        break;
                    case c_maneuverSprint:
                        if (ManeuverSprint(p_target)) done = true;
                        break;
                    case c_maneuverAdfa:
                        if (ManeuverAdfa(p_target) || m_jumpFuel <= 0) done = true;
                        break;
                    case c_maneuverKama:
                        if (ManeuverKama(p_target)) done = true;
                        break;
                    case c_maneuverWchick:
                        BrakeFall();
                        if (ManeuverWchick(p_target)) done = true;
                        break;
                    case c_maneuverWbackp:
                        BrakeFall();
                        if (ManeuverWbackp(p_target)) done = true;
                        break;
                    case c_maneuverWpeek:
                        if (ManeuverWpeek(p_target)) done = true;
                        break;
                    case c_maneuverAvoid:
                        ManeuverAvoid(p_target);
                        break;
                    case c_maneuverCircle:
                        BrakeFall();
                        if (ManeuverCircle(p_target)) done = true;
                        break;
                }

                if (m_topSpeed != 0 && IsOutOfAmmo() && !m_controlsJets && m_maneuver != c_maneuverKama && m_aiState != c_aiStateFlee)
                {
                    if (SimRandom.RandomIntBelow(4) != 0 && m_lastManeuver != c_maneuverAdfa && CanJump(40))
                    {
                        m_nextManeuver = c_maneuverAjmpin;
                        m_maneuverFlag = 1;
                    }
                    else
                    {
                        m_nextManeuver = SimRandom.RandomIntBelow(2) != 0 ? c_maneuverKama : -2;
                        if (!m_skillFlags[5]) m_nextManeuver = -2;
                    }
                }

                if (m_type == 1 && IsStuck()) done = true;
                if (m_maneuverEnd != 0 && m_maneuverEnd <= Clock.g_currentClock) done = true;
                if (m_nextManeuver != 0) done = true;
                if (done) EndManeuver();
            }
            else
            {
                m_maneuver = ChooseManeuver();
                if (m_maneuver != -1) StartManeuver();
            }
        }

        // InitializeManeuvers: no maneuver yet, and the ones its piloting (1 best to 4) allows: 0 jumping to turn and 1
        // jumping in (piloting 3 or better), 2 dodging (2), 3 a mech's (any), 4 firing only with a line of sight (4), 5 a
        // mech's self-destruct (1), 6 (any).
        public void InitializeManeuvers()
        {
            m_maneuver = -1;
            m_lastManeuver = -1;
            m_nextManeuver = 0;
            m_avoidShape = null;
            m_avoidSide = 0;
            m_probeScale = 1;
            m_nextAvoidCheck = 0;
            Array.Clear(m_placesTaken, 0, m_placesTaken.Length);
            if (m_piloting < 1 || m_piloting > 4) m_piloting = 1;
            m_skillFlags[0] = m_piloting <= 3;
            m_skillFlags[1] = m_piloting <= 3;
            m_skillFlags[2] = m_piloting <= 2;
            m_skillFlags[3] = m_type == 1 && m_piloting <= 5;
            m_skillFlags[4] = m_piloting <= 4;
            m_skillFlags[5] = m_type == 1 && m_piloting <= 1;
            m_skillFlags[6] = m_piloting <= 5;
            Host.Ai.ManeuversInitialized(this);
        }

        // ChooseManeuver: one an order asked for (-2: flee), a mech's escape when stuck, a walk at a target that isn't a
        // player, a pop-up from below a hidden target it can jump to, else one its table lets follow the last one (any
        // that starts a run at first), redrawn until its conditions hold.
        public int ChooseManeuver()
        {
            int choice;
            ManeuverTable table = m_type >= 1 && m_type <= 8 ? g_maneuverTables[m_type - 1] : g_maneuverTables[1];
            if (m_config != null && m_config.m_tons == 1) table = g_maneuverTables[8];
            table = Host.Ai.ManeuverTableOf(this, table);

            do
            {
                SetTarget(S16(m_aiTarget));
                if (m_nextManeuver != 0)
                {
                    choice = -1;
                    if (m_nextManeuver == -2)
                    {
                        SetAIState(c_aiStateFlee, S16(m_aiGoal), false);
                        m_aiFlags = 1;
                    }
                    else choice = m_nextManeuver;

                    m_nextManeuver = 0;
                    break;
                }

                if (m_type == 1)
                {
                    if (IsStuck())
                    {
                        choice = c_maneuverAvoid;
                        break;
                    }

                    if ((m_aiGoal & 0x200) == 0)
                    {
                        choice = c_maneuverStupid;
                        break;
                    }

                    if (CanJump(0x14) && m_skillFlags[1] && IsBelowHiddenTarget() && HasLineToTarget((int)m_targetInfo.m_position.y))
                    {
                        choice = m_lastManeuver == 0 ? c_maneuverWpeek : c_maneuverStupid;
                        if (choice == 0 && SimRandom.RandomIntBelow(2) != 0) choice = c_maneuverWbackp;
                        break;
                    }
                }

                int index;
                if (m_lastManeuver == -1)
                {
                    index = SimRandom.RandomIntBelow(table.m_count - table.m_followOnly);
                    choice = table.m_entries[index][0];
                }
                else
                {
                    index = FindManeuver(table, m_lastManeuver);
                    if (index == -1) index = 0;
                    choice = table.m_entries[index][2 + SimRandom.RandomIntBelow(table.m_entries[index][1])];
                }

                if (m_lastManeuver == c_maneuverAjmpin && m_maneuverFlag == 1)
                {
                    choice = c_maneuverAdfa;
                    break;
                }

                if (choice == c_maneuverAjmpin && (!CanJump(0x14) || !m_skillFlags[1])) choice = -1;
                if (choice == c_maneuverAjmpin && GetTargetBearing() > m_maxTorsoTwist) choice = c_maneuverStupid;
                if (choice == c_maneuverAsrp && m_maxTorsoTwist < 0xa0000) choice = c_maneuverAchick;
                if (choice == c_maneuverAdfa && m_onGround) choice = c_maneuverStupid;
            } while (choice == -1);

            return choice;
        }

        public static int FindManeuver(ManeuverTable p_table, int p_id)
        {
            for (int i = 0; i < p_table.m_count; i++)
            {
                if (p_table.m_entries[i][0] == p_id) return i;
            }

            return -1;
        }

        // StartManeuver: the maneuver set up, with the time it ends (m_maneuverEnd: 50 seconds, or its own).
        public void StartManeuver()
        {
            m_aiGoal = m_aiTarget;
            m_maneuverTimer = 0;
            m_lastTargetDistance = m_maneuverParam = m_maneuverFlag = 0;
            m_controlsJets = false;
            m_maneuverEnd = Clock.g_currentClock + 0x235a;
            switch (m_maneuver)
            {
                case c_maneuverStupid:
                    m_maneuverEnd = (SimRandom.RandomIntBelow(5) + 8) * 181 + Clock.g_currentClock;
                    break;
                case c_maneuverBehind:
                    m_maneuverEnd = Clock.g_currentClock + 0xe24;
                    m_maneuverFlag = -1;
                    break;
                case c_maneuverAsrp:
                    m_maneuverParam = SimRandom.RandomIntBelow(2) != 0 ? 1 : -1;
                    break;
                case c_maneuverAjmpin:
                    m_steering["drive.throttle"] = 0;
                    m_steering["turn"] = 0;
                    m_controlsJets = true;
                    SetJumpJets(true);
                    m_maneuverEnd = Clock.g_currentClock + 0x389;
                    break;
                case c_maneuverSprint:
                    m_steering["turn"] = 0;
                    m_controlsJets = true;
                    if (m_maneuverParam == 0 || m_maneuverParam == 2) m_steering["jets.left"] = 1;
                    else m_steering["jets.fore"] = 1;
                    SetJumpJets(true);
                    m_maneuverEnd = Clock.g_currentClock + 0x16a;
                    break;
                case c_maneuverAdfa:
                    GetClosingRate();
                    m_controlsJets = true;
                    break;
                case c_maneuverWbackp:
                    m_maneuverEnd = (SimRandom.RandomIntBelow(11) + 10) * 181 + Clock.g_currentClock;
                    m_steering["reverse"] = 1;
                    break;
                case c_maneuverWchick:
                    if (m_targetInfo.m_distance < 4000) PlaceOffsetNav(m_player.m_index | 0x200, SimRandom.RandomIntBelow(2) != 0 ? 4 : 12, 10000);
                    else PlaceOffsetNav(m_player.m_index | 0x200, SimRandom.RandomIntBelow(2) != 0 ? 3 : 15, 10000);
                    break;
                case c_maneuverWpeek:
                    m_maneuverParam = 0;
                    m_steering["turn"] = 0;
                    m_controlsJets = true;
                    break;
                case c_maneuverAvoid:
                    if (m_collisionTicks == 0 || m_lastManeuver != c_maneuverAvoid || m_collidedWith != -1)
                    {
                        m_steering["reverse"] = 1;
                        m_maneuverEnd = Clock.g_currentClock + 0x5a8;
                    }
                    else m_maneuverEnd = Clock.g_currentClock + 0x2d4;

                    break;
                case c_maneuverCircle:
                    m_maneuverEnd = Clock.g_currentClock + 0x46b4;
                    PlacePatrolNavs();
                    break;
            }
        }

        // EndManeuver: what it set up undone; recorded as the last; the goal the target again.
        public void EndManeuver()
        {
            switch (m_maneuver)
            {
                case c_maneuverBehind:
                    ReleaseNavPoints();
                    if ((m_aiGoal & 0x200) != 0)
                    {
                        Mech leader = MechAt(m_aiGoal & 0xff);
                        if (leader != null) leader.m_placesTaken[m_maneuverParam / 2] = (sbyte)(leader.m_placesTaken[m_maneuverParam / 2] - 1);
                    }

                    break;
                case c_maneuverAjmpin:
                    SetJumpJets(false);
                    break;
                case c_maneuverSprint:
                    m_steering["jets.fore"] = 0;
                    m_steering["jets.aft"] = 0;
                    m_steering["jets.left"] = 0;
                    m_steering["jets.right"] = 0;
                    SetJumpJets(false);
                    break;
                case c_maneuverAdfa:
                    SetJumpJets(false);
                    m_steering["jets.fore"] = 0;
                    m_steering["jets.aft"] = 0;
                    break;
                case c_maneuverWbackp:
                case c_maneuverAvoid:
                    m_steering["reverse"] = 0;
                    break;
                case c_maneuverWchick:
                case c_maneuverCircle:
                    ReleaseNavPoints();
                    break;
                case c_maneuverWpeek:
                    SetJumpJets(false);
                    break;
            }

            SetJumpJets(false);
            m_lastManeuver = m_maneuver;
            m_maneuver = -1;
            m_maneuverEnd = m_maneuverTimer = 0;
            m_aiTarget = m_aiGoal;
            m_maneuverParam = m_maneuverFlag = 0;
            SetTarget(S16(m_aiGoal));
        }

        // PlaceOffsetNav: a nav where GetOffsetPoint puts it (a player's: its height the player's heading, as the
        // original passes it), targeted.
        public void PlaceOffsetNav(int p_target, int p_direction, int p_distance)
        {
            GetOffsetPoint(p_target, p_direction, out int x, out int z, out int y, S16(p_distance));
            int nav = Teams.AddNavPoint(SimMain.g_world, m_player.m_index, x, y, z);
            if (nav == -1) return;
            SimMain.g_world.m_navs[nav].m_flags |= 1;
            SimMain.g_world.m_navs[nav].m_owner = m_player.m_index | 0x200;
            AdvanceNavTarget(0x100);
        }

        // GetOffsetPoint: the point p_distance out in direction p_direction from a player (p_y its heading) or a game
        // thing, in the world; a thing without an object is offset from its position (p_y 0). A thing's object leaves
        // p_y as it was in the original (its caller's uninitialized local): here 0.
        public void GetOffsetPoint(int p_target, int p_direction, out int p_x, out int p_z, out int p_y, int p_distance)
        {
            int index = p_target & 0xff;
            p_x = p_z = p_y = 0;
            int dx = ProbeRays[p_direction, 0] != 0 ? p_distance / ProbeRays[p_direction, 0] : 0;
            int dz = ProbeRays[p_direction, 1] != 0 ? p_distance / ProbeRays[p_direction, 1] : 0;
            FixVec point; // 16.16 units
            switch (p_target & 0xf00)
            {
                case 0x200:
                {
                    Mech mech = MechAt(index);
                    if (mech == null) return;
                    p_y = mech.m_heading;
                    // The offset turned by the heading (Quaternion.Euler(0, heading, 0)).
                    long sin = Fixed.Sin(mech.m_heading), cos = Fixed.Cos(mech.m_heading);
                    point = FixVec.FromUnits(mech.ObjectPosition) + new FixVec(dx * cos + dz * sin, 0, dz * cos - dx * sin);
                    break;
                }
                case 0x400:
                {
                    Transform obj = Host.World.ThingObject(index);
                    if (obj == null)
                    {
                        Vector3Int at = Host.World.ThingPosition(index);
                        p_x = at.x;
                        p_z = at.z;
                        p_y = 0;
                        if (ProbeRays[p_direction, 0] != 0) p_x += p_distance / ProbeRays[p_direction, 0];
                        if (ProbeRays[p_direction, 1] != 0) p_z += p_distance / ProbeRays[p_direction, 1];
                        return;
                    }

                    // The thing's object as it stands (its node, SimNodes).
                    point = Host.Anim.GetObjWorldPoint(obj, FixVec.FromUnits(dx, 0, dz));
                    break;
                }
                default:
                    return;
            }

            p_x = (int)(point.X / 65536);
            p_z = (int)(point.Z / 65536);
        }

        // IsSharpTurn: past 5 degrees either way; a stopped player then creeps forward.
        public bool IsSharpTurn(int p_turn)
        {
            if (p_turn > 0x50000 || p_turn < -0x50000)
            {
                if (m_steering["drive.throttle"] == 0) m_steering["drive.throttle"] = 0x66;
                return true;
            }

            return false;
        }

        // stupid: steer at the target and close to 15000 (Host.Ai.StupidApproach), asking for avoid within 4500
        // (Host.Ai.StupidAvoids).
        public void ManeuverStupid(int p_target)
        {
            int bearing;
            SetTarget(p_target);
            if (!AvoidObstacles())
            {
                m_steering["drive.throttle"] = GetApproachThrottle(Host.Ai.StupidApproach(this));
                bearing = SteerToTarget();
                IsSharpTurn(bearing);
            }
            else bearing = GetTargetBearing();

            if (Host.Ai.StupidAvoids(this))
                m_nextManeuver = c_maneuverAvoid;
            RunAIWeapons(bearing);
            JumpToTurn();
        }

        // behind: fire at the goal while closing on the place taken around it (the target); stopped and turned away,
        // turn in place until more than 4500 off.
        public void ManeuverBehind(int p_target)
        {
            SetTarget(S16(m_aiGoal));
            int bearing = GetTargetBearing();
            RunAIWeapons(bearing);
            if (m_maneuverFlag == 0)
            {
                SetTarget(S16(m_aiTarget));
                if (!AvoidObstacles())
                {
                    SteerToTarget();
                    m_steering["drive.throttle"] = GetApproachThrottle(4000);
                }

                if (m_steering["drive.throttle"] == 0 && Math.Abs(bearing) > 0x50000) m_maneuverFlag = 1;
                JumpToTurn();
            }

            if (m_maneuverFlag == 1)
            {
                bearing = AvoidObstacles() ? GetTargetBearing() : SteerToTarget();
                if (!IsSharpTurn(bearing)) m_steering["drive.throttle"] = 0;
                else JumpToTurn();
                SetTarget(S16(m_aiTarget));
                if (m_targetInfo.m_distance > 4500) m_maneuverFlag = 0;
            }
        }

        // achick: close on the target; done within 8000 (more when closing fast).
        public bool ManeuverAchick(int p_target)
        {
            int bearing;
            SetTarget(p_target);
            if (!AvoidObstacles())
            {
                bearing = SteerToTarget();
                m_steering["drive.throttle"] = GetApproachThrottle(5500);
            }
            else bearing = GetTargetBearing();

            RunAIWeapons(bearing);
            int scale = Fixed.Div16(GetClosingRate() << 16, 500000);
            if (scale < 0x10000) scale = 0x10000;
            bool result = (long)m_targetInfo.m_distance << 16 <= (long)scale * 8000;
            JumpToTurn();
            return result;
        }

        // kama: run at the target (even when what it avoids is the target's own shape); self-destruct within 2000.
        public bool ManeuverKama(int p_target)
        {
            SetTarget(p_target);
            if (!AvoidObstacles() || GetTargetShape(p_target) == m_avoidShape)
            {
                SteerToTarget();
                m_steering["drive.throttle"] = GetApproachThrottle(0);
            }

            if (m_targetInfo.m_range <= 2000)
            {
                m_steering["mech.selfdestruct"] = 1;
                return true;
            }

            return false;
        }

        // wbackp: back away at full throttle facing the target; done once colliding.
        public bool ManeuverWbackp(int p_target)
        {
            SetTarget(p_target);
            int bearing = SteerToTarget();
            RunAIWeapons(bearing);
            bool result = m_collisionTicks != 0;
            m_steering["drive.throttle"] = 0x400;
            JumpToTurn();
            return result;
        }

        // wchick: fire at the goal while closing on the point at its side (the target); done within 3000.
        public bool ManeuverWchick(int p_target)
        {
            SetTarget(S16(m_aiGoal));
            int bearing = GetTargetBearing();
            RunAIWeapons(bearing);
            SetTarget(S16(m_aiTarget));
            if (!AvoidObstacles())
            {
                m_steering["drive.throttle"] = GetApproachThrottle(3000);
                SteerToTarget();
            }

            JumpToTurn();
            return m_targetInfo.m_distance <= 3000;
        }

        // wpeek: jump while below a hidden target (maybe on to jump onto it), a second more, then brake the fall.
        public bool ManeuverWpeek(int p_target)
        {
            bool result = false;
            SetTarget(p_target);
            int bearing = GetTargetBearing();
            RunAIWeapons(bearing);
            switch (m_maneuverParam)
            {
                case 0:
                    if (CanJump(40) && IsBelowHiddenTarget()) SetJumpJets(true);
                    else
                    {
                        SetJumpJets(false);
                        m_maneuverParam = 1;
                        if (CanJump(20) && SimRandom.RandomIntBelow(4) == 0) m_nextManeuver = c_maneuverAdfa;
                        m_maneuverTimer = Clock.g_currentClock + 181;
                    }

                    break;
                case 1:
                    if (m_maneuverTimer < Clock.g_currentClock)
                    {
                        m_maneuverParam = 2;
                        SetJumpJets(false);
                    }
                    else SetJumpJets(true);

                    SteerToTarget();
                    break;
                case 2:
                    if (BrakeFall()) result = true;
                    break;
            }

            return result;
        }

        // ajmpin: fire at the target; done 2000 above it.
        public bool ManeuverAjmpin(int p_target)
        {
            SetTarget(p_target);
            RunAIWeapons(GetTargetBearing());
            return m_positionY >= m_targetInfo.m_position.y + 2000;
        }

        // sprint: fire at the target while the jets carry it aside.
        public bool ManeuverSprint(int p_target)
        {
            SetTarget(p_target);
            RunAIWeapons(GetTargetBearing());
            return false;
        }

        // adfa: jump at the target while more than 1000 off, the jets forward or back by how fast it closes (aside when
        // it's beyond the torso's twist); closer, the jets cut (twice: done). Done on the ground, colliding, or unable
        // to jump on.
        public bool ManeuverAdfa(int p_target)
        {
            SetTarget(p_target);
            int turn = GetTargetBearing();
            RunAIWeapons(turn);
            bool result = m_onGround || m_collisionTicks != 0;
            if (m_maneuverParam == 2) return result;
            turn = Math.Abs(turn);
            int side = turn > m_maxTorsoTwist && turn < m_maxTorsoTwist * 3 ? 1 : 0;
            if (m_targetInfo.m_distance > 1000)
            {
                int rate = GetClosingRate();
                if (rate > (m_targetInfo.m_distance <= 6000 ? 9 : 36)) SetJumpDirection(side);
                else SetJumpDirection(side == 0 ? 1 : 0);
                SetJumpJets(true);
                if (!CanJump(40)) result = true;
            }
            else
            {
                if (m_steering["jets.on"] != 0) m_maneuverParam++;
                SetJumpJets(false);
                m_steering["jets.fore"] = 0;
                m_steering["jets.aft"] = 0;
            }

            return result;
        }

        // avoid: full throttle (backwards when StartManeuver reversed it), steering at the target.
        public void ManeuverAvoid(int p_target)
        {
            int bearing;
            SetTarget(p_target);
            if (m_steering["reverse"] == 0) bearing = !AvoidObstacles() ? SteerToTarget() : GetTargetBearing();
            else bearing = SteerToTarget();
            RunAIWeapons(bearing);
            m_steering["drive.throttle"] = 0x400;
            JumpToTurn();
        }

        // circle: fire at the goal while closing on the navs around it, placed again now and then far from it, the next
        // within 3000.
        public bool ManeuverCircle(int p_target)
        {
            SetTarget(S16(m_aiGoal));
            if (m_maneuverTimer < Clock.g_currentClock)
            {
                if (m_targetInfo.m_distance > 15000)
                {
                    ReleaseNavPoints();
                    PlacePatrolNavs();
                }

                m_maneuverTimer = Clock.g_currentClock + 0x5a8;
            }

            int bearing = GetTargetBearing();
            RunAIWeapons(bearing);
            SetTarget(S16(m_aiTarget));
            if (!AvoidObstacles())
            {
                m_steering["drive.throttle"] = GetApproachThrottle(3000);
                SteerToTarget();
            }

            JumpToTurn();
            if (m_targetInfo.m_distance <= 3000) AdvanceNavTarget(S16(m_aiTarget));
            return false;
        }

        // IsBelowHiddenTarget: a player target without a clear line to it, more than 800 above, and jump jets.
        public bool IsBelowHiddenTarget()
        {
            if ((m_targetInfo.m_target & 0xf00) != 0x200) return false;
            if (HasLineToTarget((int)m_positionY)) return false;
            return m_targetInfo.m_position.y - 800 > m_positionY && m_jumpFuel >= 0;
        }

        // BrakeFall: below 20000 the jets fire while the mech falls faster than 85% of the fall damage speed and cut
        // once slower than 75%. Whether it's on the ground.
        public bool BrakeFall()
        {
            if (m_jumpFuel < 0 || m_jumpThrust == 0) return m_onGround;
            bool value = m_steering["jets.on"] != 0;
            if (m_positionY < 20000)
            {
                if (m_velocityY < -899859) value = true; // -0x102762 * 0.85
                else if (m_velocityY > -793994) value = false; // -0x102762 * 0.75
            }

            SetJumpJets(value);
            return m_onGround;
        }

        public void SetJumpJets(bool p_value) => m_steering["jets.on"] = p_value ? 1 : 0;

        // AvoidObstacles: around what lies ahead: up to four probe rays (BuildProbeRay) as long as the speed (at least
        // 0.3 of 5000; a vehicle's half) on the side it avoids, turning away from what they hit (a slope it can stand on
        // stops the probing) and slowing if the first one hits; the first missing with the target dead ahead ends the
        // avoiding, a second ray from its far edge holds the turn. Between checks (every 10 ticks for the local player,
        // 90 or 181 for the others) whether it avoids a shape. Whether it steered.
        public bool AvoidObstacles()
        {
            int turn = 0;
            if (m_topSpeed == 0 || (m_avoidSide == 0 && m_steering["drive.throttle"] == 0) || m_steering["reverse"] != 0) return false;
            if (m_nextAvoidCheck > Clock.g_currentClock) return m_avoidShape != null;

            if (m_avoidSide == 0)
            {
                int length = ApproxLen.ApproximateVectorLength(m_velocityX, 0, m_velocityZ);
                m_probeScale = Fixed.Div16(length, 0x7a120);
                if (m_type != 1) m_probeScale >>= 1;
                if (m_probeScale <= 19660) m_probeScale = 0x4ccc; // below 0.3 * 0x10000
            }

            int i;
            for (i = 0; i < 4; i++)
            {
                CollisionRay ray = BuildProbeRay(m_avoidSide, i, Fixed.Mul16(m_probeScale, 0x13880000) >> 16, false);
                if (ShapeCollision.TestSegmentCollision(ref ray, out Shape found, m_player.m_index))
                {
                    Shape hit = found;
                    if (IsStandableShape(hit)) break;
                    if (m_avoidSide == 0)
                    {
                        m_avoidSide = GetAvoidSide(hit, (int)m_positionX, (int)m_positionY, (int)m_positionZ);
                        m_avoidShape = hit;
                    }

                    turn += m_avoidSide * 13421772; // (int)(avoidSide * (0.2 * 0x10000000) / 4)
                }
                else
                {
                    if (i == 0 && Math.Abs(GetTargetBearing()) <= 0x10000)
                    {
                        m_avoidShape = null;
                        m_avoidSide = 0;
                    }

                    if (i == 0 && m_avoidSide != 0)
                    {
                        CollisionRay edge = BuildProbeRay(-m_avoidSide, 1, Fixed.Mul16(m_probeScale, 0x13880000) >> 16, true);
                        if (ShapeCollision.TestSegmentCollision(ref edge, out Shape edgeFound, m_player.m_index))
                        {
                            Shape edgeHit = edgeFound;
                            if (IsStandableShape(edgeHit)) break;
                            turn += m_avoidSide * 26843545; // (int)(avoidSide * (0.1 * 0x10000000))
                        }
                    }

                    break;
                }
            }

            if (turn != 0)
            {
                turn = ClampMagnitude(turn, 0x3333333);
                m_steering["turn"] = turn;
                int throttle = IsPilot ? m_maneuverParam : 0x400;
                m_steering["drive.throttle"] = i == 0 ? throttle : 0x100;
                if (m_type == 6)
                {
                    m_steering["drive.throttle"] = 0;
                    m_steering["turn"] = 0;
                }
            }

            if (IsPilot) m_nextAvoidCheck = Clock.g_currentClock + 10;
            else m_nextAvoidCheck = Clock.g_currentClock + (m_avoidShape != null ? 90 : 181);
            return turn != 0;
        }

        // IsStandableShape: ground to stand on: a shape with a height test (a box or terrain) hit on a face flat enough,
        // or debris (0x50).
        public static bool IsStandableShape(Shape p_shape)
        {
            if (p_shape == null) return false;
            if ((p_shape.CollisionType == 0 || p_shape.CollisionType == 5) && ShapeCollision.g_segmentNormal.Y >= 0xc41b) return true;
            return (p_shape.Kind & 0xf0) == 0x50;
        }

        // BuildProbeRay: p_length along direction p_step (mirrored for a negative p_side) in the mech's frame, level, from
        // its position or with p_fromEdge from its side (its radius out).
        public CollisionRay BuildProbeRay(int p_side, int p_step, int p_length, bool p_fromEdge)
        {
            int index = p_side >= 0 ? p_step : (0x10 - p_step) % 16;
            int dx = ProbeRays[index, 0], dz = ProbeRays[index, 1];
            if (dx != 0) dx = p_length / dx;
            if (dz != 0) dz = p_length / dz;
            // Turned by the heading (Quaternion.Euler(0, heading, 0)): 16.16 units.
            long sin = Fixed.Sin(m_heading), cos = Fixed.Cos(m_heading);
            FixVec at = FixVec.FromUnits((int)m_positionX, (int)m_positionY, (int)m_positionZ);
            FixVec end = at + new FixVec(dx * cos + dz * sin, 0, dz * cos - dx * sin);
            FixVec start = at;
            if (p_fromEdge)
            {
                int x = p_side >= 0 ? m_radius - 1 : -m_radius + 1;
                start = at + new FixVec(x * cos, 0, -x * sin);
            }

            return CollisionRay.BuildRayFromSegment(start, new FixVec(end.X / 65536 << 16, start.Y, end.Z / 65536 << 16));
        }

        // GetAvoidSide: which way to turn around p_shape from (p_x, p_y, p_z): 1 when it lies to the left, else -1.
        public int GetAvoidSide(Shape p_shape, int p_x, int p_y, int p_z)
        {
            FixVec d = p_shape.FxCenter - FixVec.FromUnits(p_x, p_y, p_z);
            int angle = Fixed.Heading(d.X, d.Z) - m_heading; // GetBearingAndRange's bearing
            if (angle > 0xb40000) angle -= 0x1680000;
            else if (angle < -0xb40000) angle += 0x1680000;
            return angle >= 0 ? -1 : 1;
        }

        // GetHeadingTo: the heading to p_target (0 to 360 degrees), keeping the target.
        public int GetHeadingTo(int p_target)
        {
            int target = m_targetInfo.m_target;
            SetTarget(p_target);
            int result = (GetTargetBearing() + 0x1680000) % 0x1680000;
            SetTarget(target);
            return result;
        }

        public static int ClampMagnitude(int p_value, int p_limit)
        {
            if (p_value > p_limit) return p_limit;
            if (p_value < -p_limit) return -p_limit;
            return p_value;
        }

        // IsOutOfAmmo: weapons, but none left that can fire.
        public bool IsOutOfAmmo()
        {
            if (m_config == null) return false;
            bool found = false;
            for (int i = 0; i < m_config.m_weaponCount && !found; i++)
            {
                WeaponSlot slot = m_config.m_weapons[i];
                if (slot.m_state != WeaponSlot.Empty && Host.Weapons.Loaded(m_config, slot)) found = true;
            }

            return !found && m_config.m_weaponCount != 0;
        }

        // SetJumpDirection: the jets forward (p_value) or backward.
        public void SetJumpDirection(int p_value)
        {
            m_steering["jets.fore"] = p_value;
            m_steering["jets.aft"] = p_value == 0 ? 1 : 0;
        }

        // GetClosingRate: how fast the target came closer since the last call, per tick.
        public int GetClosingRate()
        {
            if (Clock.g_deltaTime == 0) return 0;
            int distance = (int)m_targetInfo.m_distance;
            int rate = (m_lastTargetDistance - distance) / Clock.g_deltaTime;
            m_lastTargetDistance = distance;
            return rate;
        }

        // JumpToTurn: the jets fire to turn faster while the target is outside the torso's twist (on the ground), and
        // stop once it's inside unless the maneuver works them; only with the skill, and not while avoiding.
        public void JumpToTurn()
        {
            if (!m_skillFlags[0] || m_avoidSide != 0 || m_maneuver == c_maneuverAvoid) return;
            SetTarget(S16(m_aiTarget));
            int turn = Math.Abs(GetTargetBearing());
            if (turn >= m_maxTorsoTwist || (turn <= -m_maxTorsoTwist && m_steering["drive.throttle"] != 0))
            {
                if (CanJump(20) && m_onGround && m_steering["jets.on"] == 0) SetJumpJets(true);
            }
            else if (!m_controlsJets && m_steering["jets.on"] != 0 && m_onGround) SetJumpJets(false);
        }

        // CanJump: jump fuel left (at least 6), jets that lift and the heat under p_limit.
        public bool CanJump(int p_limit) => m_jumpFuel >= 6 && m_jumpThrust != 0 && m_heat >> 16 < p_limit;

        // GetTargetShape: the shape of the player or game thing p_target names, or null.
        public Shape GetTargetShape(int p_target)
        {
            int index = p_target & 0xff;
            switch (p_target & 0xf00)
            {
                case 0x200:
                    return MechAt(index)?.m_host.RootShape;
                case 0x400:
                    return Host.World.ThingShape(index);
            }

            return null;
        }

        // DodgeShot: when this mech fires a guided weapon (or type 21) at a player, one time in three the target may
        // not care; else an AI shooter's target, or the user's target inside the user's sights (within 3 degrees' pitch
        // and 15 of bearing) dodges: with fuel, two times in three it jumps aside along a clear way (sprint), else, able
        // to, jumps in.
        public void DodgeShot(WeaponSlot p_slot)
        {
            if (!m_skillFlags[2] || SimRandom.RandomIntBelow(3) == 0) return;
            if (WeaponDef.Of(p_slot.m_type).m_guided == 0 && p_slot.m_type != 21) return;
            int id;
            if (m_player.m_aiMode == 2) id = m_targetInfo.m_target;
            else if ((m_aiGoal & 0x200) != 0) id = m_aiGoal;
            else id = m_targetInfo.m_target;

            int kind = id & 0xf00, index = id & 0xff;
            Mech other = MechAt(index);
            if (index == 0 || kind != 0x200 || other == null || other.m_nextManeuver == c_maneuverAjmpin) return;
            Mech target = m_player.m_aiMode == 2 ? other : null;
            if (target == null && (Cockpit.Current != null && Cockpit.Current.ProjectAimPoint(out _, out _)) && (Cockpit.Current != null && Cockpit.Current.ProjectWorldPoint(other.ObjectPosition, out _, out _, out _)))
            {
                int range = 0x30000;
                int maxAngle = 15;
                int pitch = m_targetInfo.m_pitch / 0xf00;
                int bearing = (GetTargetBearing() >> 16) % 360;
                if (pitch < range && -range < pitch && bearing < maxAngle && -maxAngle < bearing) target = other;
            }

            if (target == null || !target.CanJump(0x41)) return;
            if (SimRandom.RandomIntBelow(3) != 0)
            {
                int toward = target.GetHeadingTo(m_player.m_index);
                int step = Fixed.Div16(toward, 0x5a0000) >> 16;
                int side = SimRandom.RandomIntBelow(2) != 0 ? -1 : 1;
                step = (step + 1) % 4 * 4;
                for (int j = 0; j < 2; j++)
                {
                    CollisionRay ray = target.BuildProbeRay(side, step, 5000, false);
                    if (!ShapeCollision.TestSegmentCollision(ref ray, out _, target.m_player.m_index))
                    {
                        target.m_nextManeuver = c_maneuverSprint;
                        target.m_maneuverParam = step;
                        break;
                    }

                    side = -side;
                }
            }

            if (target.m_nextManeuver == 0 && target.m_skillFlags[1]) target.m_nextManeuver = c_maneuverAjmpin;
        }

        // ChooseFlankPlace: the place around the goal player (eight, 45 degrees apart) the player is nearest, moved off
        // its front and back; else the first free one on its side. Returns twice the place.
        public int ChooseFlankPlace()
        {
            Mech leader = MechAt(m_aiGoal & 0xff);
            if (leader == null) return 0;
            int angle = Fixed.Div16(leader.GetHeadingTo(m_player.m_index | 0x200), 0x2d0000);
            int place = (angle + 0x8000) >> 16;
            if (place >= 8) place = 0;
            if (place == 0 || place == 1 || place == 7)
            {
                if (place == 0)
                {
                    place = SimRandom.RandomIntBelow(2) != 0 ? 2 : 6;
                    if (m_placesTaken[place] != 0) place = place == 2 ? 6 : 2;
                }

                if (place == 1) place = 3;
                if (place == 7) place = 5;
            }
            else
            {
                bool found = false;
                int i;
                if (place > 4)
                {
                    for (i = 6; i > 3 && !found; i--)
                    {
                        if (leader.m_placesTaken[i] == 0) found = true;
                    }

                    i++;
                }
                else
                {
                    for (i = 2; i < 5 && !found; i++)
                    {
                        if (leader.m_placesTaken[i] == 0) found = true;
                    }

                    i--;
                }

                place = i;
            }

            return place * 2;
        }

        // IsStuck: colliding, not reversing, not in a maneuver that jumps or avoids.
        public bool IsStuck() => m_collisionTicks != 0 && m_steering["reverse"] != 1 && !m_controlsJets && m_maneuver != c_maneuverAvoid;
    }
}
