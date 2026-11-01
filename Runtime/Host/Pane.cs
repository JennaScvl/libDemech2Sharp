namespace Demech2
{
    // A rectangle of the page (demech2's PANE): inclusive pixel corners. Drawing into one is relative to its top left
    // and clipped to it, as the VFX calls draw into a pane.
    public struct Pane
    {
        public int X0, Y0, X1, Y1;

        public Pane(int p_x0, int p_y0, int p_x1, int p_y1)
        {
            X0 = p_x0;
            Y0 = p_y0;
            X1 = p_x1;
            Y1 = p_y1;
        }

        public int Width => X1 - X0 + 1;
        public int Height => Y1 - Y0 + 1;

        public override string ToString() => $"({X0},{Y0})-({X1},{Y1})";
    }
}
