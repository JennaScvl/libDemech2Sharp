namespace Demech2
{
    // The two message boxes at the top and bottom of the screen (demech2 MW2/src/timedoverlays.c: LayoutMessageBoxes,
    // ShowInGameMessage, DrawTimedOverlays): a line of text on the message bar (SHP 0x4c), for a while.
    public static class TimedOverlays
    {
        const int Background = 0x4c; // 1.1's MSGBAR

        // TimedOverlay.
        sealed class Overlay
        {
            public string Text = "";
            public int TextX = 0x28f, TextY;
            public bool Active;
            public int Priority, Font = 1, ExpireTime;
            public Pane Target = new Pane(0, 0, 0x10000, 0x10000);
        }

        static readonly Overlay[] s_overlays = { new Overlay(), new Overlay() };
        static bool s_laidOut;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reset();

        // The boxes, emptied and to be laid out again (a new mission).
        public static void Reset()
        {
            s_overlays[0] = new Overlay();
            s_overlays[1] = new Overlay();
            s_laidOut = false;
        }

        // Not the original's: a box showing p_text taken down (g_statusMessage's line cleared: MissionEnd's regenerate).
        public static void Hide(string p_text)
        {
            foreach (Overlay overlay in s_overlays)
                if (overlay.Active && overlay.Text == p_text) overlay.Active = false;
        }

        // LayoutMessageBoxes: each box the screen, scaled about its center to its background's size, moved to the top
        // (the first) or the bottom edge; the text 1% in and centered vertically.
        static void Layout()
        {
            s_laidOut = true;
            for (int i = 0; i < 2; i++)
            {
                Overlay overlay = s_overlays[i];
                Pane target = ScreenScale.ScaleRectToScreen(overlay.Target);
                HudShape shape = Art.Shape11(Background + Art.g_artResolution);
                if (shape != null) target = FitRectToShape(target, shape.Bounds(0));

                int sign = i == 0 ? -1 : 1;
                int dx = -target.X0, dy = target.Y0 * sign;
                target.X0 += dx;
                target.Y0 += dy;
                target.X1 += dx;
                target.Y1 += dy;
                overlay.Target = target;
                (overlay.TextX, overlay.TextY) = ScreenScale.ScalePointToFrame(target, overlay.TextX, overlay.TextY);

                VfxFont font = Art.FontOf(overlay.Font + Art.g_artResolution);
                if (font != null) overlay.TextY = (target.Y1 - target.Y0 + 1 - font.Height) / 2;
            }
        }

        // FitRectToShape: p_rect scaled about its center to the size p_bounds gives ((width - 1) << 16 | height - 1).
        public static Pane FitRectToShape(Pane p_rect, int p_bounds)
        {
            int scaleX = Fixed.Div16(p_bounds >> 16, p_rect.X1 - p_rect.X0 + 1);
            int scaleY = Fixed.Div16(p_bounds & 0xffff, p_rect.Y1 - p_rect.Y0 + 1);
            return ScaleRectAboutCenter(p_rect, scaleX, scaleY);
        }

        // ScaleRectAboutCenter.
        public static Pane ScaleRectAboutCenter(Pane p_rect, int p_scaleX, int p_scaleY)
        {
            int centerX = ((p_rect.X1 - p_rect.X0 + 1) >> 1) + p_rect.X0;
            int centerY = ((p_rect.Y1 - p_rect.Y0 + 1) >> 1) + p_rect.Y0;
            return new Pane(Fixed.Mul16(p_rect.X0 - centerX, p_scaleX) + centerX, Fixed.Mul16(p_rect.Y0 - centerY, p_scaleY) + centerY,
                Fixed.Mul16(p_rect.X1 - centerX, p_scaleX) + centerX, Fixed.Mul16(p_rect.Y1 - centerY, p_scaleY) + centerY);
        }

        // ShowInGameMessage: p_text in a free box for p_duration ticks, else in place of the one of the lowest priority
        // not above p_priority (of those, the one that expires last). Returns whether it shows.
        public static bool ShowInGameMessage(string p_text, int p_font, int p_duration, int p_priority)
        {
            int slot = -1, lowestPriority = 0x29a, latest = 0;
            for (int i = 0; i < 2; i++)
            {
                Overlay overlay = s_overlays[i];
                if (!overlay.Active)
                {
                    slot = i;
                    break;
                }

                if (overlay.Priority > p_priority) continue;
                if (overlay.Priority < lowestPriority)
                {
                    slot = i;
                    lowestPriority = overlay.Priority;
                    latest = overlay.ExpireTime;
                }
                else if (overlay.Priority == lowestPriority && overlay.ExpireTime > latest)
                {
                    slot = i;
                    latest = overlay.ExpireTime;
                }
            }

            if (slot == -1) return false;
            Overlay shown = s_overlays[slot];
            shown.Text = p_text.Length > 0xff ? p_text.Substring(0, 0xff) : p_text;
            shown.Active = true;
            shown.Priority = p_priority;
            shown.ExpireTime = p_duration + Host.Cockpit.RenderClock;
            shown.Font = p_font < 1 ? 1 : p_font;
            return true;
        }

        // DrawTimedOverlays: each showing box's bar and text, until it expires.
        public static void Draw(ICockpitSurface p_hud)
        {
            if (!s_laidOut) Layout();
            foreach (Overlay overlay in s_overlays)
            {
                if (!overlay.Active) continue;
                if (Host.Cockpit.RenderClock >= overlay.ExpireTime)
                {
                    overlay.Active = false;
                    continue;
                }

                VfxFont font = Art.FontOf(overlay.Font + Art.g_artResolution);
                HudShape background = Art.Shape11(Background + Art.g_artResolution);
                if (font == null || background == null) continue;
                p_hud.Shape(overlay.Target, background, 0, 0, 0);
                p_hud.String(overlay.Target, overlay.TextX, overlay.TextY, font, overlay.Text, Art.g_textColors);
            }
        }
    }
}
