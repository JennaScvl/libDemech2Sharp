namespace Demech2
{
    // A shape's face colors (demech2 MW2/src/wtbshapes.c).
    public static class WtbShapes
    {
        // MapFaceId with g_shapeOwnerSet (a player's shapes): faces 0 and 0x14 take the team's affiliation, which
        // picks its camouflage and insignia. The original has no face-id table.
        public static uint MapFaceId(uint p_id, int p_affiliation)
        {
            if ((p_id & 0xff) == 0 || (p_id & 0xff) == 0x14)
            {
                p_id += (uint)p_affiliation;
            }

            return p_id;
        }
    }
}
