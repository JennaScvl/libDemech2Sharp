using UnityEngine;

namespace Demech2
{
    // A nav point (g_navTable).
    public sealed class NavPoint
    {
        public string m_name;
        public Vector3Int m_position;   // units
        public int m_heading;           // 16.16 degrees
        public int m_radius, m_team, m_owner, m_flags, m_used;
        public int m_teamsReached;      // a bit per team that reached it
        public Transform m_obj;      // a navobject on a class object
    }
}
