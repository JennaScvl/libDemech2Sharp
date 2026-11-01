using System;

namespace Demech2
{
    // The scrounge object (demech2 MW2/src/gridobject.c: SetGridObject, UpdateGridObject): one decoration object that
    // keeps up with the eye in whole grid cells, so its tiles always surround the view. With child objects (its own
    // copies around it) the cell is its span rounded up to 0x4000 units and it snaps to the nearest cell once the eye
    // strays 9/16 of a cell from it; without, a third of the span and it follows the cell the eye is in. Y stays 0.
    // The game places the object at (m_anchorX, 0, m_anchorZ) when UpdateGridObject says it moved.
    public sealed class GridObject
    {
        const int Unit = 0x4000;

        int m_cell, m_snapDist;
        bool m_snaps;
        public int m_anchorX, m_anchorZ;
        int m_cellX, m_cellZ;

        // SetGridObject. p_span: the root shape's first model's span in X or Z (units).
        public void SetGridObject(int p_span, bool p_hasChildren)
        {
            if (p_hasChildren)
            {
                m_cell = (p_span / Unit + 1) * Unit;
                m_snaps = true;
            }
            else
            {
                m_cell = (p_span / 3 / Unit + 1) * Unit;
                m_snaps = false;
            }

            m_snapDist = ((m_cell >> 3) + m_cell) >> 1;
            m_anchorX = m_anchorZ = m_cellX = m_cellZ = 0;
        }

        // UpdateGridObject, from UpdateEyepoint: the eye (whole units). True when the object moved to a new anchor.
        public bool UpdateGridObject(int p_x, int p_z)
        {
            if (m_cell == 0) return false;
            int x = p_x, z = p_z;
            int newX = m_cellX, newZ = m_cellZ;
            if (m_snaps)
            {
                if (Math.Abs(x - m_anchorX) > m_snapDist || Math.Abs(z - m_anchorZ) > m_snapDist)
                {
                    newX = (x + (x >= 0 ? m_cell >> 1 : (-m_cell) >> 1)) / m_cell;
                    newZ = (z + (z >= 0 ? m_cell >> 1 : (-m_cell) >> 1)) / m_cell;
                }
            }
            else
            {
                newX = x / m_cell;
                newZ = z / m_cell;
            }

            if (newX == m_cellX && newZ == m_cellZ) return false;
            m_cellX = newX;
            m_cellZ = newZ;
            m_anchorX = m_cellX * m_cell;
            m_anchorZ = m_cellZ * m_cell;
            return true;
        }
    }
}
