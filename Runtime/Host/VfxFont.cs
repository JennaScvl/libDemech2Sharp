using System;

namespace Demech2
{
    // A VFX font (VFX_font_height, VFX_character_width): the height at +8, then from +0x10 each character's offset; a
    // character is its width, then width by height pixels. The game draws the pixels (VFX_character_draw).
    public class VfxFont
    {
        protected readonly byte[] data;

        public readonly int Height;

        public VfxFont(byte[] data)
        {
            this.data = data;
            Height = BitConverter.ToInt32(data, 8);
        }

        public int CharacterWidth(char c)
        {
            int glyph = Glyph(c);
            return glyph < 0 ? 0 : BitConverter.ToInt32(data, glyph);
        }

        public int TextWidth(string text)
        {
            int width = 0;
            foreach (char c in text) width += CharacterWidth(c);
            return width;
        }

        protected int Glyph(char c)
        {
            int entry = 0x10 + (c & 0xff) * 4;
            if (entry + 4 > data.Length) return -1;
            int glyph = BitConverter.ToInt32(data, entry);
            return glyph <= 0 || glyph + 4 > data.Length ? -1 : glyph;
        }
    }
}
