namespace Demech2
{
    // The dropship loading screen (demech2 MW2/src/supanim.c: StartSupAnim, SupAnimTimerCallback, StopSupAnim). The
    // backdrop's palette fades in from black, the first 16 entries are set to g_supAnimPalette, then the dropship
    // animates, a frame each timer period, at 55% of the screen's width. The page, its shapes and palette are the
    // game's (ISupAnimHost); this steps them once each rendered frame (UpdateSupAnim).
    public sealed class SupAnim
    {
        public const int c_width = 640, c_height = 480;

        // AIL_set_timer_period(g_supAnimTimer, 330000): microseconds.
        const float c_framePeriod = 0.33f;

        // g_supAnimPalette: the first 16 palette entries in single player (6-bit components).
        static byte[,] s_supAnimPalette;

        static byte[,] g_supAnimPalette
        {
            get
            {
                if (s_supAnimPalette != null) return s_supAnimPalette;
                LoadingColor[] colors = SimTables.LoadingPalette;
                var table = new byte[colors.Length, 3];
                for (int i = 0; i < colors.Length; i++)
                {
                    table[i, 0] = colors[i].r;
                    table[i, 1] = colors[i].g;
                    table[i, 2] = colors[i].b;
                }

                return s_supAnimPalette = table;
            }
        }

        readonly ISupAnimHost host;

        int g_supAnimFrameCount, g_supAnimFrame, g_supAnimX, g_supAnimY;

        // The blend (GdiBlendPalettes): from black to the backdrop's palette over steps frames.
        readonly byte[] target = new byte[0x300];
        readonly double[] deltas = new double[0x300];
        int steps, step;
        float nextFrame;

        // The backdrop has faded in and the dropship (if any) has drawn a frame.
        public bool Shown { get; private set; }

        public SupAnim(ISupAnimHost p_host) => host = p_host;

        // StartSupAnim's drawing: the palette set to black, the backdrop drawn, then its palette blended in.
        public void StartSupAnim(bool p_slowFade)
        {
            host.DrawShape(false, 0, 0, 0);
            host.ShapePalette(target);
            steps = (p_slowFade ? 30 : 2) >> 1;
            for (int i = 0; i < 0x300; i++) deltas[i] = (double)target[i] / steps;
            step = steps;
            host.Realize(new byte[0x300]);
        }

        public void UpdateSupAnim()
        {
            if (step > 0)
            {
                // GdiBlendPalettes' loop: one in-between palette each pass.
                step--;
                var blend = new byte[0x300];
                for (int i = 0; i < 0x300; i++) blend[i] = (byte)((steps - step) * deltas[i]);
                if (step == 0) StartAnimation(blend);
                else host.Realize(blend);
                return;
            }

            if (!host.HasShape || g_supAnimFrameCount < 2)
            {
                Shown = true;
                return;
            }

            // SupAnimTimerCallback, each period.
            if (host.RealTime < nextFrame) return;
            nextFrame = host.RealTime + c_framePeriod;
            host.ClearPage();
            host.DrawShape(false, 0, 0, 0);
            host.DrawShape(true, g_supAnimFrame, g_supAnimX, g_supAnimY);
            host.Realize(null);
            g_supAnimFrame = (g_supAnimFrame + 1) % g_supAnimFrameCount;
            Shown = true;
        }

        // After the blend: g_supAnimPalette over the first 16 entries, then the dropship and its timer.
        // Not in GBL DOS / Pentium: their backdrops set entries 0-15 themselves, in their own order.
        void StartAnimation(byte[] p_palette)
        {
            bool ownFirstColors = host.OwnsFirstColors;
            for (int i = 0; i < 16 && !ownFirstColors; i++)
            {
                for (int c = 0; c < 3; c++) p_palette[i * 3 + c] = g_supAnimPalette[i, c];
            }

            host.Realize(p_palette);
            if (!host.HasShape) return;
            g_supAnimFrameCount = host.ShapeFrameCount();
            g_supAnimX = (int)((c_width - 1) * 0.55);
            g_supAnimY = 0;
            nextFrame = host.RealTime + c_framePeriod;
        }
    }
}
