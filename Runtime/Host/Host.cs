namespace Demech2
{
    // The library's one hook point: what the game implements and installs at startup. Mech hosts live on each Mech.
    public static class Host
    {
        public static IWorldHost World;
        public static IFxHost Fx;
        public static ISoundHost Sound;
        public static INetRules Net;
        public static IVariant Variant;
        public static IInputHost Input;
        public static IAnimHost Anim;
        public static ICockpitHost Cockpit;           // the cockpit and the in-mission UI
        public static IAudioHost Audio;               // the cockpit sounds, sound settings and speech
        public static ISegmentTestHost SegmentTest; // collision.c's segment tests: the game's override
    }
}
