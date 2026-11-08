using UnityEngine;

namespace Demech2
{
    // The palette's fades (demech2 MW2/src/palette.c: StartPaletteFade, StartPaletteFlash, UpdatePaletteFade,
    // GetPaletteFadeSteps; fadepal.c: FlashZappedPalette, FlashZappedPaletteLevel), drawn by the game as a translucent
    // plane over the whole screen (cockpit and all) in the fade's color: the screen tints toward the palette and back
    // with the original's timing. The original itself tints its textures for three of the palettes
    // (StartPaletteFade's colors: ZAPPED red, slot 0xc green, slot 0x10 black). Not the original's: the palette isn't
    // swapped, the plane stands in for it; only ZAPPED and black draw (explosions light the world instead); the
    // infrared green is the game's light amplification's instead. Every fade keeps its timing (the effect camera waits
    // on it).
    public static class ScreenFlash
    {
        const float PeakAlpha = 0.5f;

        // The world's palette group (SetPaletteResourceId) and g_basePalette, the time of day's.
        public static readonly int[] g_paletteIds = new int[0x14];
        public static int g_basePalette;

        // Whether the game's plane exists (the fade's state lives as long as it does).
        static bool s_shown;

        // The fade: toward the palette over fadeIn ticks, then back over fadeBack; a fade away from the tint
        // (FadeToBasePalette) from fadeOutFrom. The plane's alpha as last drawn.
        static Color s_color;
        static int s_startTime, s_fadeIn, s_fadeBack;
        static bool s_active, s_fadeOut;
        static float s_fadeOutFrom, s_alpha;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            g_basePalette = 0;
        }

        // The game's plane is gone: the fade with it.
        public static void PlaneDestroyed()
        {
            s_shown = false;
            s_color = default;
            s_startTime = s_fadeIn = s_fadeBack = 0;
            s_active = s_fadeOut = false;
            s_fadeOutFrom = s_alpha = 0;
        }

        // The palette group's ids cleared (the game reads the first palette_grp record of the world's BWDs into
        // g_paletteIds) and the base palette.
        public static void SetPalettes(int p_basePalette)
        {
            System.Array.Clear(g_paletteIds, 0, g_paletteIds.Length);
            g_basePalette = p_basePalette;
        }

        // FlashZappedPalette: the full red flash (ZAPPED, two seconds), as the local mech goes down.
        public static void FlashZappedPalette() => StartPaletteFade(0x11, 0x16a, 1);

        // FlashZappedPaletteLevel: ZAPPED over p_level fifteenths of two seconds.
        public static void FlashZappedPaletteLevel(int p_level)
        {
            int duration = p_level > 15 ? 0x16a : (int)(p_level / 15.0f * 362.0f);
            StartPaletteFade(0x11, duration, 1);
        }

        // StartPaletteFlash: slot p_offset past the base palette.
        public static void StartPaletteFlash(int p_offset, int p_duration, int p_mode) =>
            StartPaletteFade(p_offset + g_basePalette, p_duration, p_mode);

        // StartPaletteFade: toward palette slot p_palette over p_duration ticks and back (p_mode 1: half the time each
        // way; 2: at once, then back over the whole time; 0: there and it stays). While a fade runs, only mode 0 starts
        // another.
        public static void StartPaletteFade(int p_palette, int p_duration, int p_mode)
        {
            if (p_palette < 0 || p_palette >= g_paletteIds.Length) return;
            ShowPlane();
            if (Steps > 0 && p_mode != 0) return;
            if (p_mode == 1) p_duration >>= 1;
            s_color = ColorOf(p_palette);
            s_startTime = Clock.g_currentClock;
            s_fadeIn = p_mode == 2 ? 0 : p_duration;
            s_fadeBack = p_mode == 1 || p_mode == 2 ? p_duration : int.MaxValue;
            s_active = true;
            s_fadeOut = false;
        }

        // FadeToBasePalette: a fade that stays (mode 0) to palette p_palette, the new base palette. The 3D-card edition
        // tints only for the infrared (0xc), ZAPPED and black palettes: to any other the tint fades away.
        public static void FadeToBasePalette(int p_palette, int p_duration)
        {
            g_basePalette = p_palette;
            if (p_palette == 0xc || p_palette == 0x11 || p_palette == 0x10)
            {
                StartPaletteFade(p_palette, p_duration, 0);
                return;
            }

            ShowPlane();
            if (!s_active) return;
            s_fadeOutFrom = s_alpha;
            s_startTime = Clock.g_currentClock;
            s_fadeIn = 0;
            s_fadeBack = Mathf.Max(1, p_duration);
            s_fadeOut = true;
        }

        static void ShowPlane()
        {
            if (s_shown) return;
            s_shown = true;
            Host.Cockpit.ShowFlashPlane();
        }

        // GetPaletteFadeSteps: whether a fade is still running.
        public static int GetPaletteFadeSteps() => s_shown ? Steps : 0;

        static int Steps
        {
            get
            {
                if (!s_active) return 0;
                int elapsed = Clock.g_currentClock - s_startTime;
                if (s_fadeBack == int.MaxValue) return elapsed < s_fadeIn ? 1 : 0;
                return elapsed < s_fadeIn + s_fadeBack ? 1 : 0;
            }
        }

        // The plane's color for palette p_palette: clear (undrawn) but for ZAPPED and black.
        static Color ColorOf(int p_palette)
        {
            switch (p_palette)
            {
                case 0x11:
                    return new Color(1, 8 / 255f, 8 / 255f);
                case 0x10:
                    return Color.black;
                default:
                    return Color.clear;
            }
        }

        // UpdatePaletteFade: the plane's tint this frame, at the real clock p_renderTicks.
        public static Color UpdatePaletteFade(float p_renderTicks)
        {
            float alpha = 0;
            if (s_active && s_fadeOut)
            {
                float elapsed = p_renderTicks - s_startTime;
                if (elapsed < s_fadeBack) alpha = s_fadeOutFrom * (1f - (float)elapsed / s_fadeBack);
                else s_active = s_fadeOut = false;
            }
            else if (s_active)
            {
                float elapsed = p_renderTicks - s_startTime;
                if (elapsed < s_fadeIn) alpha = PeakAlpha * elapsed / Mathf.Max(1, s_fadeIn);
                else if (s_fadeBack == int.MaxValue) alpha = PeakAlpha;
                else if (elapsed < s_fadeIn + s_fadeBack) alpha = PeakAlpha * (1f - (float)(elapsed - s_fadeIn) / Mathf.Max(1, s_fadeBack));
                else s_active = false;
            }

            var color = new Color(s_color.r, s_color.g, s_color.b, alpha * s_color.a);
            s_alpha = color.a;
            return color;
        }
    }
}
