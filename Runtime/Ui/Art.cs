namespace Demech2
{
    // The cockpit's art (setres.c g_artResolution, g_pixelAspect; palidentity.c g_textColors, ResetTextColors; hud.c
    // LoadCachedResource of "SHP" and "FONT"): the art resolution the page is drawn at, the text color table (entry 0xe
    // is the text's color), and the shapes and fonts, which the game loads (Host.Cockpit).
    public static class Art
    {
        // g_artResolutionSizes: the screens the art is drawn for, by art resolution.
        static readonly int[,] Sizes = { { 320, 200 }, { 640, 480 }, { 1024, 768 } };

        // g_artResolution, and the page (the screen it's drawn for).
        public static int g_artResolution = 1;
        public static int PageWidth => Sizes[g_artResolution, 0];
        public static int PageHeight => Sizes[g_artResolution, 1];

        // g_pixelAspect (16.16): the page's pixels' height over their width on a 4:3 display (5:6 at 320x200).
        public static int g_pixelAspect => (int)(((long)PageHeight << 16) * 4 / (PageWidth * 3));

        public const int Font = 1; // the HUD's font, FONT 1 + the art resolution

        // g_textColors: the identity, but for the entries the drawing code sets.
        public static readonly int[] g_textColors = Identity();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ResetTextColors();

        // ResetTextColors.
        public static void ResetTextColors()
        {
            for (int i = 0; i < 0x100; i++) g_textColors[i] = i;
        }

        // LoadCachedResource of the loaded PRJ's SHP p_id, of 1.1's SHP p_id11, of FONT p_id; null without one.
        public static HudShape ShapeOf(int p_id) => Host.Cockpit.ShapeOf(p_id);
        public static HudShape Shape11(int p_id11) => Host.Cockpit.Shape11(p_id11);
        public static VfxFont FontOf(int p_id) => Host.Cockpit.FontOf(p_id);

        // The HUD's font (FONT HUD_ART_RESOLUTION + 1).
        public static VfxFont HudFont => FontOf(g_artResolution + Font);

        static int[] Identity()
        {
            var table = new int[0x100];
            for (int i = 0; i < 0x100; i++) table[i] = i;
            return table;
        }
    }
}
