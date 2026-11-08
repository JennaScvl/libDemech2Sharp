namespace Demech2
{
    // The library's one hook point: what the game implements and installs at startup. Mech hosts live on each Mech.
    public static class Host
    {
        public static IWorldHost World;
        public static IFxHost Fx;
        public static ISoundHost Sound;
        public static INetRules Net;
        public static IInputHost Input;
        public static IAnimHost Anim;
        public static ICockpitHost Cockpit;           // the cockpit and the in-mission UI
        public static IAudioHost Audio;               // the cockpit sounds, sound settings and speech
        public static ISegmentTestHost SegmentTest; // collision.c's segment tests: the game's override

        // The rules a host may change; demech2's by default.
        public static WeaponRules Weapons = new WeaponRules();
        public static MechRules Mechs = new MechRules();
        public static AiRules Ai = new AiRules();
        public static EffectRules Effects = new EffectRules();
        public static ObjectiveRules Objectives = new ObjectiveRules();
    }
}
