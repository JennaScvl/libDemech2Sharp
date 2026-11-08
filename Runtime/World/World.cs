using System.Collections.Generic;

namespace Demech2
{
    // The world's sim state (world.c, team.c, objective.c, targeting.c's tables): the navs, players and game things,
    // the teams and their formations, and the stars' missions as they play out. The game's scene owns it and fills it
    // as its stream loads.
    public sealed class World
    {
        public const int c_maxNavs = 0x80, c_maxPlayers = 0x3c, c_maxTeams = 16;

        public readonly List<NavPoint> m_navs = new List<NavPoint>();       // g_navTable
        public readonly List<Player> m_players = new List<Player>();        // g_players
        public readonly List<GameThing> m_gameThings = new List<GameThing>(); // g_gameThings

        public readonly Team[] m_teams = new Team[c_maxTeams];             // g_teams
        public readonly List<Formation> m_formations = new List<Formation>(); // g_formationTemplates

        // g_starSides: the side of each affiliation's star.
        public readonly int[] m_starSides = new int[16];

        // g_localStar: the local player's team (its gpspec's).
        public int m_localStar = -1;

        // g_objectiveTable, g_objectiveCount (the stars with a mission table), g_currentObjective by star,
        // g_objectiveAnnounced (the local star's objective i announced), g_missionResultAnnounced.
        public readonly MissionTable[] m_missions = new MissionTable[c_maxTeams];
        public int m_objectiveCount;
        public readonly int[] m_currentObjective = new int[c_maxTeams];
        public readonly bool[] m_objectiveAnnounced = new bool[48];
        public bool m_missionResultAnnounced;

        // The host's data for this world, or null.
        public object m_ext;
    }

    // A game thing (gamething.h): flags 4 destroyed, 0x20 inspected, 0x40 an objective's, 0x100 "Unknown" until
    // inspected, 0x400 and 0x1000 on the radar (0x1000 without the range limit), 0x800 never the local player's target,
    // 0x8000 its object is loaded straight into chunks.
    public sealed class GameThing
    {
        public int m_flags;
        public int m_teamsReached;
        public int m_staticObject = -1; // its object's static index, -1 once destroyed
        public int m_hitPoints;
        public int m_affiliation = -1;
        public long m_radius;           // its shape's (set once loaded), 16.16 units
        public string m_name = "", m_shortName = "";
    }

    // A team (Team, g_teamFormations).
    public sealed class Team
    {
        public int m_affiliation, m_side, m_leader;
        public int m_formation;         // the template g_teamFormations copies
        public int[] m_x = new int[8], m_z = new int[8];
        public int[] m_heading = new int[8]; // 16.16 degrees
        public readonly List<int> m_members = new List<int>();
    }

    // A formation template (g_formationTemplates).
    public sealed class Formation
    {
        public string m_name;
        public int[] m_x = new int[8], m_z = new int[8];
        public int[] m_heading = new int[8]; // 16.16 degrees
    }

    // A star's mission (g_missionTables, g_objectiveTable): its objectives and how it went.
    public sealed class MissionTable
    {
        public readonly List<MissionObjective> m_objectives = new List<MissionObjective>();
        public int m_startTime = -1, m_endTime = -1; // mission seconds
        public int m_timeLimit;          // seconds (0: none)
        public string m_successSound = "", m_failSound = "";
        public int m_status;             // 0 in progress, 2 successful, 3 failed, 4 out of time
        public object m_ext;             // the host's data for this table, or null
    }
}
