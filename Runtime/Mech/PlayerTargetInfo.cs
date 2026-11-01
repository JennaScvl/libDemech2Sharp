using UnityEngine;

namespace Demech2
{
    // Player::m_targetInfo (PlayerTargetInfo): the target (low byte the index; 0x100 a nav, 0x200 a player, 0x400 a game
    // thing; 0x1000 lost), and where it is: its ground distance, its distance, its position (units), bearing and pitch
    // (16.16 degrees).
    public sealed class PlayerTargetInfo
    {
        public int m_target = -1;
        public int m_distance, m_range;
        public Vector3Int m_position;
        public int m_heading, m_pitch;

        public void Reset()
        {
            m_target = -1;
            m_distance = m_range = 0;
            m_position = Vector3Int.zero;
            m_heading = m_pitch = 0;
        }

        // The place, distances, bearing and pitch of p_info (not its id).
        public void CopyPlace(PlayerTargetInfo p_info)
        {
            m_distance = p_info.m_distance;
            m_range = p_info.m_range;
            m_position = p_info.m_position;
            m_heading = p_info.m_heading;
            m_pitch = p_info.m_pitch;
        }
    }
}
