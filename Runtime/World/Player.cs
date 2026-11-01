using UnityEngine;

namespace Demech2
{
    // A player (gpspec): its mech, its team and slot.
    public sealed class Player
    {
        public int m_index, m_team, m_slot = -1, m_aiMode;
        public int m_flags;            // Player::m_flags: the gpspec's (0x400, 0x1000: shown on the radar)
        public string m_chassis, m_config, m_name, m_shortName;
        public int m_mekId = -1;       // the gpspec's MEK resource (-2 for a user variant's file)
        public GameObject m_model;
        public Vector3Int m_position;
        public int m_heading;          // 16.16 degrees
        public Mech m_mech;
        public readonly int[] m_aiParams = new int[8]; // the gpspec's gunnery, leash, rest alert, alert ranges, piloting
    }
}
