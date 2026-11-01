namespace Demech2
{
    // The "dorcs" cheat's sequence (demech2 MW2/src/dorcs.c: ShowDorcs, UpdateDorcs): after a while the view shrinks to
    // a point and back, collapses to a point (its edges a line at a time, outlined), then a picture shows (VFX\VFXJK.BIN,
    // a GIF, its palette faded in), then another (VFXHD.BIN, the palette to black and in again), then the programmers'
    // page (menu 3); once it closes the picture stays a moment and the view comes back. The game moves the view and
    // shows the pictures (Host.Cockpit.Dorcs...).
    public static class Dorcs
    {
        // g_dorcsState: 0 idle, else the step.
        static int s_state;
        static bool s_active;
        static int s_time;
        static bool s_reverse = true;

        // g_dorcsPoint, g_dorcsRectFrom, g_dorcsRectTo (16.16 fractions of the screen until placed).
        static readonly Pane PointRect = new Pane(0x8000, 0x8000, 0x8000, 0x8000);
        static readonly Pane FromRect = new Pane(0x7d71, 0x7d71, 0x828f, 0x828f);
        static readonly Pane ToRect = new Pane(0, 0, 0x10000, 0x10000);
        static RectTransition s_transition;
        static Pane s_point;

        static bool s_gifLoaded;

        public static bool Active => s_active;

        // A mission starts: no sequence running.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            s_active = false;
            s_state = 0;
            s_gifLoaded = false;
        }

        public static void Reset()
        {
            s_active = false;
            s_state = 0;
            Host.Cockpit.DorcsReleaseGif();
            s_gifLoaded = false;
        }

        // ShowDorcs.
        public static void ShowDorcs()
        {
            if (s_active) return;
            s_active = true;
            s_state = 0;
        }

        // UpdateDorcs, once a frame in place of the cockpit's frame. Returns whether the cockpit draws as usual.
        public static bool UpdateDorcs(ICockpitSurface p_hud)
        {
            if (!s_active) return true;
            Pane display = p_hud.Display;
            int now = Host.Cockpit.RenderClock;
            switch (s_state)
            {
                case 0:
                    Pane screenFrom = ScreenScale.ScaleRectToFrame(display, FromRect), screenTo = ScreenScale.ScaleRectToFrame(display, ToRect);
                    s_point = ScreenScale.ScaleRectToFrame(display, PointRect);
                    s_transition = new RectTransition(0xb5, screenFrom, screenTo);
                    s_gifLoaded = false;
                    s_time = now + 0x389;
                    s_state = 1;
                    return true;
                case 1:
                    if (now < s_time) return true;
                    s_transition.Duration = 0x279;
                    s_reverse = true;
                    s_transition.Start();
                    s_state = 2;
                    goto case 2;
                case 2:
                {
                    SimMenus.CloseInGameMenus();
                    Pane? rect = s_transition.Update(s_reverse);
                    if (rect != null)
                    {
                        ShowView(p_hud, rect.Value, -1);
                        break;
                    }

                    if (s_reverse)
                    {
                        s_reverse = false;
                        s_transition.Start();
                        ShowView(p_hud, display, -1);
                        break;
                    }

                    s_transition.Duration = 0x2d4;
                    s_transition.First = s_point;
                    s_transition.Start();
                    s_state = 3;
                    goto case 3;
                }
                case 3:
                {
                    SimMenus.CloseInGameMenus();
                    Pane? rect = s_transition.UpdateByAxis(true);
                    if (rect != null)
                    {
                        ShowView(p_hud, rect.Value, 10);
                        break;
                    }

                    s_state = 4;
                    goto case 4;
                }
                case 4:
                    SimMenus.CloseInGameMenus();
                    Host.Cockpit.DorcsHideView();
                    Host.Cockpit.DorcsStartFade(0, 0, 0);
                    if (Host.Cockpit.DorcsLoadGif("VFXJK.BIN"))
                    {
                        s_gifLoaded = true;
                        Host.Cockpit.DorcsStartFade(0, 1, 0xb5);
                    }

                    DrawGif(p_hud, display);
                    s_time = now + 0x10f;
                    s_state = 5;
                    break;
                case 5:
                    SimMenus.CloseInGameMenus();
                    if (now >= s_time)
                    {
                        if (s_gifLoaded && Host.Cockpit.DorcsLoadGif("VFXHD.BIN"))
                        {
                            Host.Cockpit.DorcsStartFade(0, 0, 0);
                            Host.Cockpit.DorcsStartFade(0, 1, 0xb5);
                        }
                        else s_gifLoaded = false;

                        s_time = now + 0x10f;
                        s_state = 6;
                    }

                    DrawGif(p_hud, display);
                    break;
                case 6:
                    SimMenus.CloseInGameMenus();
                    DrawGif(p_hud, display);
                    if (now >= s_time)
                    {
                        SimMenus.RequestMenu(3);
                        s_state = 7;
                    }

                    break;
                case 7:
                    DrawGif(p_hud, display);
                    SimMenus.Draw(p_hud);
                    if (!SimMenus.AnyMenuOpen && !SimMenus.Paused)
                    {
                        s_time = now + 0x10f;
                        s_state = -1;
                    }

                    break;
                case -1:
                    SimMenus.CloseInGameMenus();
                    if (now < s_time)
                    {
                        DrawGif(p_hud, display);
                        break;
                    }

                    p_hud.Wipe(display, 0);
                    Host.Cockpit.DorcsRestoreView();
                    Host.Cockpit.DorcsReleaseGif();
                    s_active = false;
                    s_state = 0;
                    break;
            }

            return false;
        }

        // The scene drawn into p_rect (the eye camera's viewport), the page black around it; outlined in p_outline unless -1.
        static void ShowView(ICockpitSurface p_hud, Pane p_rect, int p_outline)
        {
            Pane display = p_hud.Display;
            if (p_rect.X1 < p_rect.X0 || p_rect.Y1 < p_rect.Y0)
            {
                Host.Cockpit.DorcsHideView();
                p_hud.Wipe(display, 0);
                return;
            }

            Host.Cockpit.DorcsShowView(p_rect, display);
            if (p_rect.Y0 > display.Y0) p_hud.Wipe(new Pane(display.X0, display.Y0, display.X1, p_rect.Y0 - 1), 0);
            if (p_rect.Y1 < display.Y1) p_hud.Wipe(new Pane(display.X0, p_rect.Y1 + 1, display.X1, display.Y1), 0);
            if (p_rect.X0 > display.X0) p_hud.Wipe(new Pane(display.X0, p_rect.Y0, p_rect.X0 - 1, p_rect.Y1), 0);
            if (p_rect.X1 < display.X1) p_hud.Wipe(new Pane(p_rect.X1 + 1, p_rect.Y0, display.X1, p_rect.Y1), 0);
            if (p_outline >= 0)
            {
                int w = p_rect.X1 - p_rect.X0, h = p_rect.Y1 - p_rect.Y0;
                p_hud.Line(p_rect, 0, 0, w, 0, p_outline);
                p_hud.Line(p_rect, w, 0, w, h, p_outline);
                p_hud.Line(p_rect, w, h, 0, h, p_outline);
                p_hud.Line(p_rect, 0, h, 0, 0, p_outline);
            }
        }

        // The page black, the picture (FitRectToGif: its own size, centered) at the palette's brightness.
        static void DrawGif(ICockpitSurface p_hud, Pane p_display)
        {
            p_hud.Wipe(p_display, 0);
            if (!s_gifLoaded) return;
            Host.Cockpit.DorcsDrawGif(p_hud, p_display);
        }
    }
}
