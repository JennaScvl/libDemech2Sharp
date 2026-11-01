using System.Collections.Generic;

namespace Demech2
{
    // The sim's shared combat state (simmain.c's globals around g_players: g_localPlayerId, g_difficulty,
    // g_mechPoweredUp, g_aimedShape...): the world whose players fight, and each player's mech.
    public static class SimMain
    {
        // g_localPlayerId: the first gpspec in single player; in a MekNet match the gpspec the handoff didn't mark
        // remote. Back to 0 at End.
        public static int g_localPlayerId = 0;

        // The ground GetTerrainHeight finds (the world's terrain and box shapes).
        public static TerrainHeight g_terrain;

        // The world the players fight in.
        public static World g_world;

        public static DifficultyCfg g_difficulty = new DifficultyCfg();

        // g_mechPoweredUp: set once any mech first runs; until then nothing takes damage.
        public static bool g_mechPoweredUp;

        // g_aimedShape: the mech or thing the local player's aim ray last hit.
        public static Shape g_aimedShape;

        // g_lastHitShooter: the player whose shot hit something last.
        public static int g_lastHitShooter = -1;

        public static bool g_autoEject;
        public static bool g_localMechDestroyed, g_ejectStarted;

        // g_hitFadePending (the cockpit flashes ZAPPED for it), g_hitFadeCount.
        public static bool g_hitFadePending;
        public static int g_hitFadeCount;

        public static bool g_criticalHeatWarned;
        public static int g_criticalHeatWarningTime;

        // g_players[i]->m_mech, by player index.
        public static readonly List<Mech> g_mechs = new List<Mech>();

        // Not the original's: the pilots, the humans' mechs (the local player alone until SetPilots names a match's
        // slots).
        static readonly HashSet<int> s_pilots = new HashSet<int>();
        static bool s_pilotsSet;

        // The mission's end: the world, the local player, the pilots and mechs, the aimed shape forgotten.
        public static void End()
        {
            g_world = null;
            g_localPlayerId = 0;
            s_pilots.Clear();
            s_pilotsSet = false;
            g_mechs.Clear();
            g_aimedShape = null;
        }

        public static void AddMech(Mech p_mech)
        {
            int index = p_mech.m_player.m_index;
            while (g_mechs.Count <= index) g_mechs.Add(null);
            g_mechs[index] = p_mech;
        }

        public static int PlayerCount => g_world != null ? g_world.m_players.Count : 0;

        // g_players[p_index]->m_mech, or null.
        public static Mech MechOf(int p_index) => p_index >= 0 && p_index < g_mechs.Count ? g_mechs[p_index] : null;

        public static Mech LocalMech => MechOf(g_localPlayerId);

        public static Player PlayerOf(int p_index) =>
            g_world != null && p_index >= 0 && p_index < g_world.m_players.Count ? g_world.m_players[p_index] : null;

        // GetPlayerSide.
        public static int PlayerSide(int p_index) => g_world != null ? Teams.GetPlayerSide(g_world, p_index) : 2;

        // GetThingSide: g_starSides of the thing's affiliation, 2 without one.
        public static int ThingSide(int p_index) => g_world != null ? Teams.GetThingSide(g_world, p_index) : 2;

        // ---- Not the original's: the pilots, and the sides as each pilot's gameplay sees them ----------------------

        public static void SetPilots(IEnumerable<int> p_players)
        {
            s_pilots.Clear();
            foreach (int player in p_players)
                if (player >= 0) s_pilots.Add(player);
            s_pilotsSet = true;
        }

        public static bool IsPilot(int p_index) => p_index >= 0 && (s_pilotsSet ? s_pilots.Contains(p_index) : p_index == g_localPlayerId);

        public static bool HasPilot => s_pilotsSet ? s_pilots.Count > 0 : PlayerOf(g_localPlayerId) != null;

        // The "user" an AI target names (TargetUser): the local player, in a match the lowest pilot.
        public static int UserId
        {
            get
            {
                if (!Host.Net.NetRules || !s_pilotsSet) return g_localPlayerId;
                int best = -1;
                foreach (int pilot in s_pilots)
                    if (best < 0 || pilot < best) best = pilot;
                return best;
            }
        }

        // A star with a pilot in it (the local star in single player).
        public static bool IsPilotStar(int p_team)
        {
            if (g_world == null) return false;
            if (!s_pilotsSet)
            {
                Player local = PlayerOf(g_localPlayerId);
                return local != null && local.m_team == p_team;
            }

            foreach (int pilot in s_pilots)
            {
                Player player = PlayerOf(pilot);
                if (player != null && player.m_team == p_team) return true;
            }

            return false;
        }

        // Player p_index's side as player p_viewer's gameplay sees it: GetPlayerSide (the local player's view) outside
        // a match; in one, friendly on the viewer's star, else enemy (neutral as the mission says).
        public static int SideOf(int p_viewer, int p_index)
        {
            if (g_world == null) return 2;
            int side = Teams.GetPlayerSide(g_world, p_index);
            if (!Host.Net.NetRules || side == 2) return side;
            Player viewer = PlayerOf(p_viewer), other = PlayerOf(p_index);
            if (viewer == null || other == null) return 2;
            return viewer.m_team == other.m_team ? 0 : 1;
        }

        // Players p_a (of side p_sideA, GetPlayerSide's) and p_b on one side: equal sides, in a match one star.
        public static bool SameSide(int p_a, int p_b, int p_sideA)
        {
            if (!Host.Net.NetRules) return PlayerSide(p_b) == p_sideA;
            Player a = PlayerOf(p_a), b = PlayerOf(p_b);
            return a != null && b != null && a.m_team == b.m_team;
        }

        // Game thing p_index's side as player p_viewer's gameplay sees it (GetThingSide outside a match).
        public static int ThingSideOf(int p_viewer, int p_index)
        {
            if (g_world == null) return 2;
            if (!Host.Net.NetRules) return Teams.GetThingSide(g_world, p_index);
            Player viewer = PlayerOf(p_viewer);
            int team = Teams.ThingTeam(g_world, p_index);
            if (team < 0 || viewer == null) return 2;
            return team == viewer.m_team ? 0 : 1;
        }

        // g_feetToTorso as every mech's twist test reads it: any pilot's.
        public static bool AnyFeetToTorso
        {
            get
            {
                foreach (Mech mech in g_mechs)
                    if (mech != null && mech.FeetToTorsoActive && mech.IsPilot) return true;
                return false;
            }
        }

        // A destroyed mech's (0x100) or game thing's (0x200) shape no pilot aims at any more.
        public static void ForgetAimed(int p_kind, int p_owner)
        {
            foreach (Mech mech in g_mechs)
            {
                Shape aimed = mech != null ? mech.m_aimedShape : null;
                if (aimed != null && (aimed.Kind & p_kind) != 0 && aimed.Owner == p_owner) mech.m_aimedShape = null;
            }
        }
    }
}
