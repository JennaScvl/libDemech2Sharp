using System;

namespace Demech2
{
    // A VFX shape table the cockpit draws (LoadCachedResource of "SHP"): its resource and its frames' headers. The
    // game decodes and draws the frames (VFX_shape_draw).
    public abstract class HudShape
    {
        public int Id;
        public byte[] Data;

        public abstract int FrameCount { get; }

        // VFX_shape_resolution: the frame's width << 16 | height.
        public int Resolution(int p_frame)
        {
            int at = BitConverter.ToInt32(Data, 8 + p_frame * 8);
            int width = BitConverter.ToInt32(Data, at + 0x10) - BitConverter.ToInt32(Data, at + 8) + 1;
            int height = BitConverter.ToInt32(Data, at + 0x14) - BitConverter.ToInt32(Data, at + 0xc) + 1;
            return width << 16 | (height & 0xffff);
        }

        // VFX_shape_origin: the frame's second dword.
        public int Origin(int p_frame) => BitConverter.ToInt32(Data, BitConverter.ToInt32(Data, 8 + p_frame * 8) + 4);

        // VFX_shape_bounds: (width - 1) << 16 | (height - 1), the frame's first dword.
        public int Bounds(int p_frame) => BitConverter.ToInt32(Data, BitConverter.ToInt32(Data, 8 + p_frame * 8));
    }
}
