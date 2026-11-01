namespace Demech2
{
    // A timed move of a pane's rectangle between two others (demech2 MW2/src/recttransition.c): the edges together, or the
    // horizontal edges in the first half and the vertical ones in the second. The cockpit's panels and map views open and
    // close through them as the mech starts up and shuts down.
    public sealed class RectTransition
    {
        public const int AxisBoth = 0, AxisHorizontal = 1, AxisVertical = 2;

        // RectTransitionDef: the duration (ticks), the rectangles, as 16.16 fractions until scaled to a frame.
        public int Duration;
        public Pane First, Second, Out;

        // RectTransitionState.
        public bool Active, WasActive;
        public int Elapsed;

        // A transition shared by several layouts keeps one state (g_mapTransitionState): p_state.
        readonly RectTransition state;

        public RectTransition(int p_duration, Pane p_first, Pane p_second, RectTransition p_state = null)
        {
            Duration = p_duration;
            First = p_first;
            Second = p_second;
            state = p_state;
        }

        RectTransition State => state ?? this;

        // StartRectTransition.
        public void Start()
        {
            State.Active = true;
            State.WasActive = false;
        }

        // StopRectTransition.
        public void Stop()
        {
            State.Active = false;
            State.WasActive = false;
        }

        public int StateElapsed
        {
            get => State.Elapsed;
            set => State.Elapsed = value;
        }

        // LerpPaneRect: p_out's edges p_t (16.16) of the way, the horizontal ones unless p_axis is vertical and the
        // vertical ones unless it is horizontal.
        public static Pane Lerp(Pane p_from, Pane p_to, Pane p_out, int p_t, int p_axis)
        {
            if (p_axis != AxisVertical)
            {
                p_out.X0 = p_from.X0 + Fixed.Mul16(p_t, p_to.X0 - p_from.X0);
                p_out.X1 = p_from.X1 + Fixed.Mul16(p_t, p_to.X1 - p_from.X1);
            }

            if (p_axis != AxisHorizontal)
            {
                p_out.Y0 = p_from.Y0 + Fixed.Mul16(p_t, p_to.Y0 - p_from.Y0);
                p_out.Y1 = p_from.Y1 + Fixed.Mul16(p_t, p_to.Y1 - p_from.Y1);
            }

            return p_out;
        }

        // UpdateRectTransition: advances it, forward or (p_reverse) back; its rectangle, or null when it isn't running.
        public Pane? Update(bool p_reverse)
        {
            RectTransition s = State;
            Pane? result = null;
            bool active = s.Active;
            if (active)
            {
                Pane from = p_reverse ? Second : First, to = p_reverse ? First : Second;
                if (!s.WasActive)
                {
                    Out = from;
                    s.Elapsed = 0;
                }
                else
                {
                    s.Elapsed += Host.Cockpit.FrameTicks;
                    int t = Fixed.Div16(s.Elapsed, Duration);
                    if (t >= 0x10000)
                    {
                        Out = to;
                        s.Elapsed = 0;
                        active = false;
                    }
                    else Out = Lerp(from, to, Out, t, AxisBoth);
                }

                result = Out;
            }

            s.WasActive = s.Active;
            s.Active = active;
            return result;
        }

        // UpdateRectTransitionByAxis: as Update, the horizontal edges first (the vertical ones in reverse).
        public Pane? UpdateByAxis(bool p_reverse)
        {
            RectTransition s = State;
            Pane? result = null;
            bool active = s.Active;
            if (active)
            {
                Pane from = p_reverse ? Second : First, to = p_reverse ? First : Second;
                int firstAxis = p_reverse ? AxisVertical : AxisHorizontal, secondAxis = p_reverse ? AxisHorizontal : AxisVertical;
                if (!s.WasActive)
                {
                    Out = from;
                    s.Elapsed = 0;
                }
                else
                {
                    s.Elapsed += Host.Cockpit.FrameTicks;
                    int t = Fixed.Div16(s.Elapsed, Duration);
                    if (t >= 0x10000)
                    {
                        Out = to;
                        s.Elapsed = 0;
                        active = false;
                    }
                    else if (t <= 0x8000)
                    {
                        Out = Lerp(from, to, from, Fixed.Mul16(t, 0x20000), firstAxis);
                    }
                    else
                    {
                        Out = Lerp(from, to, to, Fixed.Mul16(t - 0x8000, 0x20000), secondAxis);
                    }
                }

                result = Out;
            }

            s.WasActive = s.Active;
            s.Active = active;
            return result;
        }
    }

    // The original's screen scaling (16.16 fixed point, Demech2.Fixed): the HUD's tables are 320x200 coordinates and
    // 16.16 fractions of the screen or of a frame.
    public static class ScreenScale
    {
        // The screen's last pixel (g_screenWidthMinus1, g_screenHeightMinus1).
        public static int g_screenWidthMinus1 = Art.PageWidth - 1, g_screenHeightMinus1 = Art.PageHeight - 1;

        // ScaleRectFromLowRes, then ScaleRectToScreen: 320x200 to screen pixels.
        public static Pane LowResToScreen(Pane p_rect) => new Pane(
            Fixed.Mul16(g_screenWidthMinus1, Fixed.Div16(p_rect.X0, 319)), Fixed.Mul16(g_screenHeightMinus1, Fixed.Div16(p_rect.Y0, 199)),
            Fixed.Mul16(g_screenWidthMinus1, Fixed.Div16(p_rect.X1, 319)), Fixed.Mul16(g_screenHeightMinus1, Fixed.Div16(p_rect.Y1, 199)));

        // ScalePointFromLowRes, then ScalePointToScreen.
        public static (int x, int y) LowResPointToScreen(int p_x, int p_y) =>
            (Fixed.Mul16(g_screenWidthMinus1, Fixed.Div16(p_x, 319)), Fixed.Mul16(g_screenHeightMinus1, Fixed.Div16(p_y, 199)));

        // ScaleRectToScreen: 16.16 fractions of the screen.
        public static Pane ScaleRectToScreen(Pane p_rect) => new Pane(Fixed.Mul16(g_screenWidthMinus1, p_rect.X0),
            Fixed.Mul16(g_screenHeightMinus1, p_rect.Y0), Fixed.Mul16(g_screenWidthMinus1, p_rect.X1), Fixed.Mul16(g_screenHeightMinus1, p_rect.Y1));

        // ScaleRectToFrame: 16.16 fractions of p_frame, placed in it.
        public static Pane ScaleRectToFrame(Pane p_frame, Pane p_rect)
        {
            int width = p_frame.X1 - p_frame.X0, height = p_frame.Y1 - p_frame.Y0;
            return new Pane(Fixed.Mul16(p_rect.X0, width) + p_frame.X0, Fixed.Mul16(p_rect.Y0, height) + p_frame.Y0,
                Fixed.Mul16(p_rect.X1, width) + p_frame.X0, Fixed.Mul16(p_rect.Y1, height) + p_frame.Y0);
        }

        // ScalePointToFrame: a point in 16.16 fractions of p_frame's size, relative to it.
        public static (int x, int y) ScalePointToFrame(Pane p_frame, int p_x, int p_y) =>
            (Fixed.Mul16(p_frame.X1 - p_frame.X0, p_x), Fixed.Mul16(p_frame.Y1 - p_frame.Y0, p_y));
    }
}
